using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using DEB.Core.Persistencia;

namespace DEB.Core.Intercambio;

// =====================================================================================================================
// Peças levantadas num modelo (Revit) para detalhar no CAD (pedido do usuário, 2026-11-09): o DEB.Revit grava um JSON
// com os grupos de peças iguais (iguais ou giradas = mesma marca) e o DEB.Cad gera um detalhamento por grupo pelo template.
// Só dados neutros: medidas em cm no sistema local da peça, cotas de nível em m. Nada do Revit nem do CAD aqui.
// =====================================================================================================================

/// <summary>Consolo numa face do pilar (sistema local: seção b em x, h em y; z a partir da base).</summary>
public sealed class ConsoloPeca
{
    /// <summary>Face do template do pilar: 1 = +x, 2 = −y, 3 = −x, 4 = +y.</summary>
    public int Face { get; set; }
    /// <summary>Início e fim ao longo da face (cm): coordenada y nas faces 1 e 3, x nas faces 2 e 4.</summary>
    public double Inicio { get; set; }
    public double Fim { get; set; }
    /// <summary>Base e topo do consolo junto à face, a partir da base do pilar (cm).</summary>
    public double Base { get; set; }
    public double Topo { get; set; }
    /// <summary>Balanço (cm).</summary>
    public double Balanco { get; set; }
    /// <summary>Altura na ponta (cm); menor que a altura junto à face = trapezoidal.</summary>
    public double AlturaPonta { get; set; }

    public double Largura => Fim - Inicio;
    public double AlturaFace => Topo - Base;
}

/// <summary>Nível do modelo que corta o pilar (nome e cota em m).</summary>
public sealed class NivelPeca
{
    public string Nome { get; set; } = "";
    public double Cota { get; set; }
}

/// <summary>Peça do modelo que pertence a um grupo (id do elemento, marca que tinha, giro em relação ao representante).</summary>
public sealed class ElementoPeca
{
    public string Id { get; set; } = "";
    /// <summary>Identificador estável do elemento no modelo (UniqueId do Revit); vazio = não informado. Opcional no JSON.</summary>
    public string Uid { get; set; } = "";
    public string Marca { get; set; } = "";
    public int Giro { get; set; }

    internal ValorJson ParaJson()
    {
        var o = ValorJson.NovoObjeto().Definir("id", Id);
        if (!string.IsNullOrEmpty(Uid)) o.Definir("uid", Uid);
        return o.Definir("marca", Marca).Definir("giro", Giro);
    }

    internal static ElementoPeca DeJson(ValorJson je, string caminho) => new ElementoPeca
    {
        Id = LeitorJson.Txt(je, "id", "", caminho),
        Uid = LeitorJson.Txt(je, "uid", "", caminho),
        Marca = LeitorJson.Txt(je, "marca", "", caminho),
        Giro = LeitorJson.Int(je, "giro", 0, caminho),
    };
}

/// <summary>Grupo de pilares iguais (ou girados 90/180/270°) do modelo.</summary>
public sealed class PilarPeca
{
    public string Marca { get; set; } = "";
    public List<ElementoPeca> Elementos { get; set; } = new List<ElementoPeca>();
    /// <summary>Seção b (x), h (y) e comprimento total (cm).</summary>
    public double B { get; set; }
    public double H { get; set; }
    public double L { get; set; }
    /// <summary>Cota da base (m), no referencial dos níveis do modelo.</summary>
    public double CotaBase { get; set; }
    /// <summary>Comprimento embutido no cálice (cm); null = o modelo não informa (fica o do padrão).</summary>
    public double? Embutimento { get; set; }
    /// <summary>Níveis do modelo entre a base e o topo; null = o modelo não informa (ficam os do padrão).</summary>
    public List<NivelPeca> Niveis { get; set; }
    public List<ConsoloPeca> Consolos { get; set; } = new List<ConsoloPeca>();

    public int Quantidade => Math.Max(1, Elementos.Count);
}

/// <summary>Dente Gerber numa ponta da viga: altura do dente (a parte que fica sobre o apoio) e comprimento (ponta → recorte), cm.</summary>
public sealed class DentePeca
{
    public double Altura { get; set; }
    public double Comprimento { get; set; }
}

/// <summary>
/// Aba lateral da viga (apoio de laje; VP-4, 2026-11-09): L (um lado) ou T invertido (dois lados), num trecho da viga.
/// Esquerda = à esquerda de quem olha ao longo do eixo, da ponta esquerda para a direita (o lado de cima da planta do CAD).
/// </summary>
public sealed class AbaPeca
{
    public const string Esquerda = "esquerda", Direita = "direita", DoisLados = "dois";
    public string Lado { get; set; } = Direita;
    /// <summary>Balanço a partir da face da alma, altura junto da alma e fundo da aba acima do fundo da viga (cm).</summary>
    public double Largura { get; set; }
    public double Altura { get; set; }
    /// <summary>Altura na ponta (cm); menor que <see cref="Altura"/> = fundo inclinado. 0 = igual (retangular).</summary>
    public double AlturaPonta { get; set; }
    public double Base { get; set; }
    /// <summary>Trecho com aba, cm a partir da ponta esquerda.</summary>
    public double Inicio { get; set; }
    public double Fim { get; set; }
}

/// <summary>Grupo de vigas iguais (ou invertidas: ponta esquerda ↔ direita) do modelo.</summary>
public sealed class VigaPeca
{
    public string Marca { get; set; } = "";
    /// <summary>Giro 180 = a peça está invertida em relação ao representante.</summary>
    public List<ElementoPeca> Elementos { get; set; } = new List<ElementoPeca>();
    /// <summary>Seção b × h (b = largura da ALMA) e comprimento total da peça (cm).</summary>
    public double B { get; set; }
    public double H { get; set; }
    public double L { get; set; }
    /// <summary>Aba lateral (L / T invertido); null = seção retangular.</summary>
    public AbaPeca Aba { get; set; }
    /// <summary>Dente nas pontas esquerda e direita; null = ponta reta.</summary>
    public DentePeca DenteE { get; set; }
    public DentePeca DenteD { get; set; }

    public int Quantidade => Math.Max(1, Elementos.Count);
}

/// <summary>Arquivo de troca (JSON) com as peças levantadas de um modelo.</summary>
public sealed class ArquivoPecas
{
    public const string Formato = "DEB.Pecas";
    /// <summary>Versão mais nova que esta biblioteca lê. A 2 (2026-11-09) trouxe a aba das vigas.</summary>
    public const int VersaoAtual = 2;

    /// <summary>
    /// Versão gravada = a menor que representa o conteúdo: 1 se nenhuma viga tem aba (um DEB.Cad antigo continua lendo), 2 se
    /// tem — o antigo então RECUSA o arquivo em vez de desenhar a viga sem a aba.
    /// </summary>
    public int VersaoNecessaria => Vigas.Any(v => v.Aba != null) ? 2 : 1;

    /// <summary>Arquivo do modelo de onde vieram as peças.</summary>
    public string Origem { get; set; } = "";
    /// <summary>Data e hora do levantamento (texto ISO).</summary>
    public string Data { get; set; } = "";
    public List<PilarPeca> Pilares { get; set; } = new List<PilarPeca>();
    public List<VigaPeca> Vigas { get; set; } = new List<VigaPeca>();
    /// <summary>Observações do levantamento (peças ignoradas etc.).</summary>
    public List<string> Notas { get; set; } = new List<string>();

    // ------------------------------------------------------------------ gravação

    public ValorJson ParaJson()
    {
        var o = ValorJson.NovoObjeto()
            .Definir("formato", Formato).Definir("versao", VersaoNecessaria)
            .Definir("origem", Origem).Definir("data", Data)
            .Definir("unidades", "cm; cota_base e niveis em m");
        var notas = ValorJson.NovaLista();
        foreach (var n in Notas) notas.Adicionar(ValorJson.De(n));
        o.Definir("notas", notas);
        var pilares = ValorJson.NovaLista();
        foreach (var p in Pilares)
        {
            var jp = ValorJson.NovoObjeto().Definir("marca", p.Marca).Definir("quantidade", p.Quantidade);
            var els = ValorJson.NovaLista();
            foreach (var e in p.Elementos) els.Adicionar(e.ParaJson());
            jp.Definir("elementos", els)
              .Definir("b", R(p.B)).Definir("h", R(p.H)).Definir("l", R(p.L))
              .Definir("cota_base", Math.Round(p.CotaBase, 4))
              .Definir("embutimento", p.Embutimento.HasValue ? ValorJson.De(R(p.Embutimento.Value)) : ValorJson.Nulo);
            if (p.Niveis == null) jp.Definir("niveis", ValorJson.Nulo);
            else
            {
                var nv = ValorJson.NovaLista();
                foreach (var n in p.Niveis) nv.Adicionar(ValorJson.NovoObjeto().Definir("nome", n.Nome).Definir("cota", Math.Round(n.Cota, 4)));
                jp.Definir("niveis", nv);
            }
            var cs = ValorJson.NovaLista();
            foreach (var c in p.Consolos)
                cs.Adicionar(ValorJson.NovoObjeto().Definir("face", c.Face)
                    .Definir("inicio", R(c.Inicio)).Definir("fim", R(c.Fim))
                    .Definir("base", R(c.Base)).Definir("topo", R(c.Topo))
                    .Definir("balanco", R(c.Balanco)).Definir("altura_ponta", R(c.AlturaPonta)));
            jp.Definir("consolos", cs);
            pilares.Adicionar(jp);
        }
        o.Definir("pilares", pilares);
        var vigas = ValorJson.NovaLista();
        foreach (var p in Vigas)
        {
            ValorJson Dente(DentePeca d) => d == null ? ValorJson.Nulo
                : ValorJson.NovoObjeto().Definir("altura", R(d.Altura)).Definir("comprimento", R(d.Comprimento));
            var els = ValorJson.NovaLista();
            foreach (var e in p.Elementos) els.Adicionar(e.ParaJson());
            var jv = ValorJson.NovoObjeto().Definir("marca", p.Marca).Definir("quantidade", p.Quantidade)
                .Definir("elementos", els)
                .Definir("b", R(p.B)).Definir("h", R(p.H)).Definir("l", R(p.L))
                .Definir("dente_esquerda", Dente(p.DenteE)).Definir("dente_direita", Dente(p.DenteD));
            if (p.Aba != null)
                jv.Definir("aba", ValorJson.NovoObjeto().Definir("lado", p.Aba.Lado)
                    .Definir("largura", R(p.Aba.Largura)).Definir("altura", R(p.Aba.Altura))
                    .Definir("altura_ponta", R(p.Aba.AlturaPonta > 0 ? p.Aba.AlturaPonta : p.Aba.Altura)).Definir("base", R(p.Aba.Base))
                    .Definir("inicio", R(p.Aba.Inicio)).Definir("fim", R(p.Aba.Fim)));
            vigas.Adicionar(jv);
        }
        return o.Definir("vigas", vigas);
    }

    public void Gravar(string caminho) => Json.GravarArquivo(caminho, ParaJson());

    // ------------------------------------------------------------------ leitura

    public static ArquivoPecas Ler(string caminho) => DeJson(Json.LerArquivo(caminho));

    /// <summary>Lê o JSON; FormatException com o caminho do campo se algo estiver errado.</summary>
    public static ArquivoPecas DeJson(ValorJson raiz)
    {
        const string c0 = "pecas";
        LeitorJson.Objeto(raiz, c0);
        string formato = LeitorJson.Txt(raiz, "formato", "", c0);
        if (formato != Formato) throw new FormatException($"{c0}.formato: esperado \"{Formato}\" (o arquivo não é um levantamento de peças do DEB).");
        int versao = LeitorJson.Int(raiz, "versao", 0, c0);
        if (versao < 1 || versao > VersaoAtual) throw new FormatException($"{c0}.versao: {versao} não suportada (esta versão lê até {VersaoAtual}).");
        var a = new ArquivoPecas
        {
            Origem = LeitorJson.Txt(raiz, "origem", "", c0),
            Data = LeitorJson.Txt(raiz, "data", "", c0),
            Notas = LeitorJson.ListaTxt(raiz, "notas", new List<string>(), c0),
        };
        var lp = LeitorJson.Lista(raiz, "pilares", c0);
        for (int i = 0; lp != null && i < lp.Quantidade; i++)
        {
            string cp = $"{c0}.pilares[{i}]";
            var jp = LeitorJson.Objeto(lp[i], cp);
            var p = new PilarPeca
            {
                Marca = LeitorJson.Txt(jp, "marca", "", cp),
                B = LeitorJson.Num(jp, "b", 0, cp), H = LeitorJson.Num(jp, "h", 0, cp), L = LeitorJson.Num(jp, "l", 0, cp),
                CotaBase = LeitorJson.Num(jp, "cota_base", 0, cp),
                Embutimento = LeitorJson.Presente(jp, "embutimento") ? LeitorJson.Num(jp, "embutimento", 0, cp) : (double?)null,
            };
            if (p.B <= 0 || p.H <= 0 || p.L <= 0) throw new FormatException($"{cp}: b, h e l têm de ser maiores que zero.");
            var le = LeitorJson.Lista(jp, "elementos", cp);
            for (int k = 0; le != null && k < le.Quantidade; k++)
            {
                p.Elementos.Add(ElementoPeca.DeJson(LeitorJson.Objeto(le[k], $"{cp}.elementos[{k}]"), $"{cp}.elementos[{k}]"));
            }
            // sem a lista de elementos, vale a quantidade informada
            if (p.Elementos.Count == 0)
                for (int k = LeitorJson.Int(jp, "quantidade", 1, cp); k > 0; k--) p.Elementos.Add(new ElementoPeca());
            var ln = LeitorJson.Lista(jp, "niveis", cp);
            if (ln != null)
            {
                p.Niveis = new List<NivelPeca>();
                for (int k = 0; k < ln.Quantidade; k++)
                {
                    var jn = LeitorJson.Objeto(ln[k], $"{cp}.niveis[{k}]");
                    p.Niveis.Add(new NivelPeca { Nome = LeitorJson.Txt(jn, "nome", "", $"{cp}.niveis[{k}]"), Cota = LeitorJson.Num(jn, "cota", 0, $"{cp}.niveis[{k}]") });
                }
            }
            var lc = LeitorJson.Lista(jp, "consolos", cp);
            for (int k = 0; lc != null && k < lc.Quantidade; k++)
            {
                string cc = $"{cp}.consolos[{k}]";
                var jc = LeitorJson.Objeto(lc[k], cc);
                var c = new ConsoloPeca
                {
                    Face = LeitorJson.Int(jc, "face", 0, cc),
                    Inicio = LeitorJson.Num(jc, "inicio", 0, cc), Fim = LeitorJson.Num(jc, "fim", 0, cc),
                    Base = LeitorJson.Num(jc, "base", 0, cc), Topo = LeitorJson.Num(jc, "topo", 0, cc),
                    Balanco = LeitorJson.Num(jc, "balanco", 0, cc),
                };
                c.AlturaPonta = LeitorJson.Num(jc, "altura_ponta", c.AlturaFace, cc);
                if (c.Face < 1 || c.Face > 4) throw new FormatException($"{cc}.face: use 1 a 4.");
                p.Consolos.Add(c);
            }
            a.Pilares.Add(p);
        }
        var lv = LeitorJson.Lista(raiz, "vigas", c0);
        for (int i = 0; lv != null && i < lv.Quantidade; i++)
        {
            string cv = $"{c0}.vigas[{i}]";
            var jv = LeitorJson.Objeto(lv[i], cv);
            DentePeca Dente(string k)
            {
                if (!LeitorJson.Presente(jv, k)) return null;
                var jd = LeitorJson.Objeto(jv[k], $"{cv}.{k}");
                return new DentePeca { Altura = LeitorJson.Num(jd, "altura", 0, $"{cv}.{k}"), Comprimento = LeitorJson.Num(jd, "comprimento", 0, $"{cv}.{k}") };
            }
            var v = new VigaPeca
            {
                Marca = LeitorJson.Txt(jv, "marca", "", cv),
                B = LeitorJson.Num(jv, "b", 0, cv), H = LeitorJson.Num(jv, "h", 0, cv), L = LeitorJson.Num(jv, "l", 0, cv),
                DenteE = Dente("dente_esquerda"), DenteD = Dente("dente_direita"),
            };
            if (v.B <= 0 || v.H <= 0 || v.L <= 0) throw new FormatException($"{cv}: b, h e l têm de ser maiores que zero.");
            if (LeitorJson.Presente(jv, "aba"))
            {
                string ca = $"{cv}.aba";
                var ja = LeitorJson.Objeto(jv["aba"], ca);
                v.Aba = new AbaPeca
                {
                    Lado = LeitorJson.Txt(ja, "lado", AbaPeca.Direita, ca),
                    Largura = LeitorJson.Num(ja, "largura", 0, ca), Altura = LeitorJson.Num(ja, "altura", 0, ca), Base = LeitorJson.Num(ja, "base", 0, ca),
                    Inicio = LeitorJson.Num(ja, "inicio", 0, ca), Fim = LeitorJson.Num(ja, "fim", 0, ca),
                };
                v.Aba.AlturaPonta = LeitorJson.Num(ja, "altura_ponta", v.Aba.Altura, ca);
                if (v.Aba.Lado != AbaPeca.Esquerda && v.Aba.Lado != AbaPeca.Direita && v.Aba.Lado != AbaPeca.DoisLados)
                    throw new FormatException($"{ca}.lado: use \"esquerda\", \"direita\" ou \"dois\".");
                if (v.Aba.Largura <= 0 || v.Aba.Altura <= 0 || v.Aba.Fim <= v.Aba.Inicio) throw new FormatException($"{ca}: largura, altura e trecho (inicio < fim) têm de ser positivos.");
            }
            var le = LeitorJson.Lista(jv, "elementos", cv);
            for (int k = 0; le != null && k < le.Quantidade; k++)
            {
                v.Elementos.Add(ElementoPeca.DeJson(LeitorJson.Objeto(le[k], $"{cv}.elementos[{k}]"), $"{cv}.elementos[{k}]"));
            }
            if (v.Elementos.Count == 0)
                for (int k = LeitorJson.Int(jv, "quantidade", 1, cv); k > 0; k--) v.Elementos.Add(new ElementoPeca());
            a.Vigas.Add(v);
        }
        return a;
    }

    private static double R(double cm) => Math.Round(cm, 2);

    /// <summary>Nome sugerido do arquivo: "pecas_&lt;modelo&gt;_&lt;data&gt;.json".</summary>
    public static string NomeArquivo(string modelo, DateTime quando) =>
        "pecas_" + string.Concat((Path.GetFileNameWithoutExtension(modelo ?? "") ?? "").Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch)) +
        "_" + quando.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".json";
}
