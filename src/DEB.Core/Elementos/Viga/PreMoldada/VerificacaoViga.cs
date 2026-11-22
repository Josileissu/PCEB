using System;
using System.Collections.Generic;
using System.Linq;
using DEB.Core.Documentacao;
using DEB.Core.Motor;
using DEB.Core.Normas.Verificacao;

namespace DEB.Core.Elementos.Viga.PreMoldada;

/// <summary>
/// Verificações normativas da viga e dos dentes (Etapa 4, docs/04_arquitetura/verificacoes_normativas.md), pelas regras únicas do
/// núcleo (<see cref="VerificacoesViga"/>, <see cref="VerificacoesDente"/>, <see cref="RegrasCobrimento"/>) com o que o template
/// desenha. Avisos só das falhas, no formato do pilar; tudo vai para o relatório de verificação (<see cref="ResultadoTemplate.Verificacoes"/>).
/// Não desenham nada.
/// </summary>
public sealed partial class TemplateVigaPreMoldada
{
    private static FuncaoBarraViga Funcao(LinhaLongViga l) =>
        l.Inferior ? FuncaoBarraViga.Inferior : l.Pele ? FuncaoBarraViga.Pele : l.Funcao == FSuperior ? FuncaoBarraViga.Superior : FuncaoBarraViga.Construtiva;

    /// <summary>Barras longitudinais na seção em x (as mesmas posições da SEÇÃO X-X - ARMADURA do vão).</summary>
    public static List<BarraSecaoViga> BarrasNaSecao(ValoresTemplate v, List<LinhaLongViga> longs, double b, double h, double x) =>
        longs.Where(l => x >= l.Ini - 1e-6 && x <= l.Fim + 1e-6)
             .SelectMany(l => PontosNaSecao(l, b, h, v.Numero("CobFundo"), v.Numero("CobTopo"), v.Numero("CobLateral"), v.Numero("EstBitola"), longs)
                 .Select(p => new BarraSecaoViga { X = p.X, Y = p.Y, Bitola = l.Bitola, Funcao = Funcao(l) }))
             .ToList();

    private static double AreaLinha(LinhaLongViga l) => l.Quantidade * Math.PI * l.Bitola * l.Bitola / 400;

    /// <summary>Verificações da viga (V, G) e de cada dente (D); preenche <see cref="ResultadoTemplate.Verificacoes"/> e os avisos.</summary>
    private static void VerificarViga(ResultadoTemplate r, ValoresTemplate v, double b, double h, double L, ExtremidadeViga e, ExtremidadeViga d,
        List<LinhaLongViga> longs, List<ZonaEstriboViga> zonas, AbaViga aba)
    {
        var lista = new ListaVerificacoes();
        double fck = v.Numero("Fck"), cobL = v.Numero("CobLateral"), phiT = v.Numero("EstBitola");
        var dv = new DadosViga
        {
            B = b, H = h, D = AlturaUtil(v), FckMPa = fck, AcoEstribo = v["EstAco"], AgregadoMm = v.Numero("DiamAgregado"),
            Protendida = v.SimNao("Protendida"), ComAba = aba.Existe,
            EstBitola = phiT, EstRamos = 2, EstDistRamos = b - 2 * cobL - phiT / 10,
            Zonas = zonas.Where(z => z.Contada <= 0).Select(z => (z.Nome, z.Esp)).ToList(),
            VdSobreVRd2 = string.IsNullOrWhiteSpace(v[Calculo.CalculoViga.ChaveVdVRd2]) ? (double?)null : v.Numero(Calculo.CalculoViga.ChaveVdVRd2),
        };

        // longitudinais: armadura mínima e pele no meio do vão; máxima e espaçamentos em cada composição (o meio de cada linha)
        var meio = BarrasNaSecao(v, longs, b, h, L / 2);
        lista.Add(VerificacoesViga.ArmaduraMinima(dv, meio.Where(x => x.Funcao == FuncaoBarraViga.Inferior).Sum(x => x.Area)), false);
        foreach (var x in VerificacoesViga.Pele(dv, meio)) lista.Add(x, false);
        var juntas = new ListaVerificacoes();
        foreach (double xs in new[] { L / 2 }.Concat(longs.Select(l => (l.Ini + l.Fim) / 2)).Distinct())
        {
            var sec = BarrasNaSecao(v, longs, b, h, xs);
            if (sec.Count == 0) continue;
            juntas.Juntar(new[] { VerificacoesViga.ArmaduraMaxima(dv, sec.Sum(x => x.Area)) });
            juntas.Juntar(VerificacoesViga.EspacamentoLivre(dv, sec));
        }
        foreach (var x in juntas) lista.Add(x, false);
        // V11: pontas retas — a parte da armadura inferior do vão que chega ao apoio
        var infVao = longs.Where(l => l.Inferior && L / 2 >= l.Ini - 1e-6 && L / 2 <= l.Fim + 1e-6).ToList();
        if (infVao.Count > 0)
        {
            double asVao = infVao.Sum(AreaLinha), cobP = v.Numero("CobPontas");
            if (!e.TemDente) lista.Add(VerificacoesViga.AncoragemNoApoio("esquerda", asVao, infVao.Where(l => l.Ini <= cobP + 1).Sum(AreaLinha)), false);
            if (!d.TemDente) lista.Add(VerificacoesViga.AncoragemNoApoio("direita", asVao, infVao.Where(l => l.Fim >= L - cobP - 1).Sum(AreaLinha)), false);
        }
        foreach (var x in VerificacoesViga.Estribos(dv)) lista.Add(x, false);

        // cobrimento das faces (fundo, topo, laterais) com as reduções do pré-moldado aplicadas e avisadas (igual ao pilar)
        var dc = new DadosCobrimento
        {
            Classe = TabelasNbr.Classe(v["ClasseAgressividade"]), Elemento = ElementoCobrimento.VigaPilar, PreMoldado = true,
            Protendido = dv.Protendida, FckMPa = fck, AguaCimentoAte045 = v.SimNao("AguaCimento045"),
            CobrimentoMm = new[] { v.Numero("CobFundo"), v.Numero("CobTopo"), cobL }.Min() * 10, MaiorBitolaMm = phiT, AgregadoMm = dv.AgregadoMm,
        };
        foreach (var x in RegrasCobrimento.Verificar(dc)) lista.Add(x, false);

        // dentes Gerber
        foreach (var (x, esq) in new[] { (e, true), (d, false) })
        {
            if (!x.TemDente) continue;
            var t = x.Dente;
            var dd = new DadosDente
            {
                Nome = $"ponta {(esq ? "esquerda" : "direita")}, {t.Nome}", B = b, DVig = dv.D, Altura = t.Altura, Comprimento = t.Comprimento,
                CobTopo = v.Numero("CobTopo"), CobPontas = v.Numero("CobPontas"),
                GerberN = t.EstGerberQtd, GerberPhi = t.EstGerberBitola, TirN = t.TiranteQtd, TirPhi = t.TiranteBitola, TirComp = t.TiranteComp,
                GrampoN = t.GrampoQtd, GrampoPhi = t.GrampoBitola, GrampoPerna = t.GrampoPerna,
                CosturaN = t.CosturaQtd, CosturaPhi = t.CosturaBitola, CosturaPerna = t.CosturaPerna,
                SuspPhi = t.SuspBitola, TrechoSuspensao = x.TrechoSuspensao,
                // posições medidas da ponta (como na ponta esquerda), as mesmas do desenho
                SuspPosicoes = t.SuspBitola > 0 ? PosicoesSuspensao(t, v.Numero("CobPontas"), true, L, x.TrechoSuspensao) : new List<double>(),
                Furos = x.Furos, FckMPa = fck, Aco = v["LongAco"],
                Vd = string.IsNullOrWhiteSpace(v[esq ? Calculo.CalculoViga.ChaveVdE : Calculo.CalculoViga.ChaveVdD]) ? (double?)null : v.Numero(esq ? Calculo.CalculoViga.ChaveVdE : Calculo.CalculoViga.ChaveVdD),
            };
            foreach (var res in VerificacoesDente.Verificar(dd)) lista.Add(res);
        }

        // o espaçamento livre da suspensão já é avisado na leitura da ponta (LerExtremidade): não repete o aviso
        foreach (var a in lista.Where(x => x.Situacao != SituacaoVerificacao.Ok && !(x.Codigo == "D6" && x.Descricao.StartsWith("Espaçamento livre")))
                               .Select(x => x.Mensagem).Distinct())
            if (!r.Avisos.Contains(a)) r.Avisos.Add(a);
        r.Verificacoes.Juntar(lista);
    }

    /// <summary>Cabeçalho do relatório de verificação do grupo (D37): seção, extremidades, materiais, classe e cobrimentos.</summary>
    private static void DadosParaRelatorio(ResultadoTemplate r, ValoresTemplate v, ExtremidadeViga e, ExtremidadeViga d, AbaViga aba)
    {
        string N(string k, string f = "0.##") => KitDesenho.Num(v.Numero(k), f);
        string Ponta(ExtremidadeViga x) => (x.TemDente ? $"dente {x.Dente.Nome} ({x.Dente.Resumo} cm)" : "reta") + (x.Furos > 0 ? $", {x.Furos} furo(s) ø{N("FuroDiam")} mm" : "");
        r.ElementoRelatorio = v.SimNao("Protendida") ? "Viga pré-moldada protendida" : "Viga pré-moldada";
        void D(string rot, string val) => r.DadosRelatorio.Add(new KeyValuePair<string, string>(rot, val));
        double capa = Capa(v);
        D("Quantidade de peças", Math.Max(1, v.Inteiro("Quantidade")).ToString());
        D("Seção", $"{N("B")} × {N("H")} cm" + (capa > 0 ? $" + capa {KitDesenho.Num(capa, "0.##")} cm (2º estágio)" : "") + (aba.Existe ? ", com aba" : ""));
        D("Comprimento", $"{N("L")} cm");
        D("Extremidades", $"esquerda: {Ponta(e)}; direita: {Ponta(d)}");
        D("Concreto", $"fck = {N("Fck")} MPa; fcj no saque {N("FcjSaque")} MPa; na montagem {N("FcjMontagem")} MPa");
        D("Aço", $"longitudinais {v["LongAco"]}; estribos {v["EstAco"]}");
        D("Agregado graúdo", $"dmáx = {N("DiamAgregado")} mm");
        D("Classe de agressividade", string.IsNullOrWhiteSpace(v["ClasseAgressividade"]) ? "II" : v["ClasseAgressividade"]);
        D("Relação a/c ≤ 0,45", v.SimNao("AguaCimento045") ? "sim (declarada)" : "não declarada");
        D("Cobrimentos (fundo; topo; laterais; pontas)", $"{N("CobFundo")}; {N("CobTopo")}; {N("CobLateral")}; {N("CobPontas")} cm");
    }
}
