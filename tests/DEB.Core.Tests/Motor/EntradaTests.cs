using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DEB.Core.Motor;
using DEB.Core.Motor.Entrada;
using DEB.Core.Templates;
using DEB.Core.Templates.Blocos;
using Xunit;

namespace DEB.Core.Tests.Motor;

/// <summary>Entrada de dados do motor (frente D): conversor do template, planilha de blocos, catálogo e sobrescritas.</summary>
public class EntradaTests
{
    private static readonly TemplateBlocoEstacas T = new TemplateBlocoEstacas();

    private static ValoresTemplate Padrao() => new ValoresTemplate(T.Parametros);

    /// <summary>Todos os parâmetros do template iguais (números pelo valor; textos sem diferenciar maiúsculas).</summary>
    private static void Iguais(ValoresTemplate a, ValoresTemplate b)
    {
        foreach (var p in T.Parametros)
        {
            if (p.Tipo == TipoParametro.Numero || p.Tipo == TipoParametro.Inteiro || p.Tipo == TipoParametro.Bitola)
                Assert.True(Math.Abs(a.Numero(p.Nome) - b.Numero(p.Nome)) < 1e-9, $"{p.Nome}: {a[p.Nome]} ≠ {b[p.Nome]}");
            else if (p.Tipo == TipoParametro.SimNao)
                Assert.True(a.SimNao(p.Nome) == b.SimNao(p.Nome), p.Nome);
            else if (p.Tipo == TipoParametro.Tabela)
                Assert.Equal(string.Join(";", a.Tabela(p.Nome).Select(l => l[0] + "|" + ValoresTemplate.NumeroCelula(l, 1).ToString(CultureInfo.InvariantCulture))),
                             string.Join(";", b.Tabela(p.Nome).Select(l => l[0] + "|" + ValoresTemplate.NumeroCelula(l, 1).ToString(CultureInfo.InvariantCulture))));
            else
                Assert.True(string.Equals(a[p.Nome], b[p.Nome], StringComparison.OrdinalIgnoreCase), $"{p.Nome}: \"{a[p.Nome]}\" ≠ \"{b[p.Nome]}\"");
        }
    }

    private static ValoresTemplate IdaEVolta(ValoresTemplate v)
    {
        var e = ConversorBloco.DeValores(v, out var perfil);
        var w = new ValoresTemplate();   // vazio: tudo tem de vir do elemento e das sobreposições
        ConversorBloco.ParaValores(e, perfil, w);
        return w;
    }

    // ======================== conversor ========================

    [Fact]
    public void ConversorIdaEVoltaComOsPadroesDoTemplate()
    {
        var v = Padrao();
        var e = ConversorBloco.DeValores(v, out var perfil);
        Assert.Equal("B28", e.Nome);
        Assert.Equal(6, e.Pilares.Count);
        Assert.Equal(ArranjoEstacas.Grade, e.Arranjo);
        Assert.Equal(LigacaoPilar.CaliceColarinho, e.Ligacao);
        Assert.Equal(InterfaceCalice.Chaves, e.Interface);
        Assert.Equal(55, e.AlturaBase);
        var pr = e.Armadura(Funcoes.Principal);
        Assert.Equal(16, pr.Bitola); Assert.Equal(5, pr.Quantidade); Assert.Equal(5, pr.Espacamento);
        Assert.Equal(12.5, e.Armadura(Funcoes.CaliceTopo).Bitola);
        Assert.Equal(7, e.Armadura(Funcoes.FundoCalice).Quantidade);
        Assert.Equal(12, ConversorBloco.FuncoesBloco.Count());
        Assert.All(ConversorBloco.FuncoesBloco, f => Assert.NotNull(e.Armadura(f)));
        // padrões do escritório: fora do elemento
        Assert.Equal(4, perfil.Padroes["cobrimento.solo"]);
        Assert.Equal(2, perfil.Padroes["cobrimento.calice"]);
        Assert.Equal(20, perfil.Padroes["secundaria.perna"]);
        Assert.Equal(20, perfil.Padroes["superior.perna"]);
        Assert.Equal(20, perfil.Padroes["arranque.pe"]);
        Assert.Equal(0, perfil.Padroes["arranque.espera"]);
        Assert.Equal(1, perfil.Padroes["calice.grampos"]);
        Assert.Equal(100, perfil.Padroes["fundo_calice.comprimento"]);
        Assert.Equal("altura_base", perfil.Regras["principal.perna"]);
        Assert.Equal("fora", perfil.Regras["arranque.pe_sentido"]);
        Assert.Equal(20, perfil.Escala); Assert.Equal("PT", perfil.Idioma); Assert.True(perfil.Forma); Assert.True(perfil.Detalhes);

        Iguais(v, IdaEVolta(v));
    }

    [Fact]
    public void ConversorIdaEVoltaComOB15EValoresDoEscritorio()
    {
        var v = Padrao();
        v["Nome"] = "B15"; v["Arranjo"] = TemplateBlocoEstacas.Triangulo; v["EspX"] = "150"; v["DistFace"] = "40"; v["DiamEstaca"] = "50";
        v["PilarBx"] = "40"; v["PilarBy"] = "40"; v["tParede"] = "25"; v["hBase"] = "105"; v["hColar"] = "30"; v["Lemb"] = "65";
        v["CalTopoBitola"] = "16"; v["CalTopoQtd"] = "5"; v["CalTopoEsp"] = "4"; v["Quantidade"] = "24";
        v["Niveis"] = "P1|-3.43;P67|-11.63"; v["Pilares"] = "P1 e P67";
        v["PrincPerna"] = "68"; v["ArrPeSentido"] = TemplateBlocoEstacas.PeDentro; v["CalGrampos"] = "Não";
        v["Idioma"] = "PT/EN"; v["Escala"] = "25"; v["Detalhes"] = "Não"; v["Interface"] = TemplateBlocoEstacas.Rugosa;
        v["Ligacao"] = TemplateBlocoEstacas.Embutido;

        var e = ConversorBloco.DeValores(v, out var perfil);
        Assert.Equal(ArranjoEstacas.Triangulo, e.Arranjo);
        Assert.Equal(LigacaoPilar.CaliceEmbutido, e.Ligacao);
        Assert.Equal(24, e.Repeticoes);
        Assert.Equal(2, e.Niveis.Count); Assert.Equal(-11.63, e.Niveis[1].CotaM, 6);
        Assert.Equal(new[] { "P1", "P67" }, e.Pilares);
        Assert.Equal(68, perfil.Padroes["principal.perna"]);
        Assert.Equal("valor", perfil.Regras["principal.perna"]);
        Assert.Equal("dentro", perfil.Regras["arranque.pe_sentido"]);
        Assert.Equal(0, perfil.Padroes["calice.grampos"]);

        Iguais(v, IdaEVolta(v));

        // aplicar no perfil: padrões, regras e folha
        var p = new PerfilDetalhamento { TipoElemento = ElementoBloco.TipoId };
        p.Folha.Detalhes.Add("chaves");
        p.Folha.Vistas.Add(new VistaPerfil { Id = "forma", Tipo = TipoVista.FormaPlanta });
        perfil.Forma = false;
        perfil.AplicarEm(p);
        Assert.Equal(68, p.Padrao("principal.perna", 0));
        Assert.Equal("valor", p.Regra("principal.perna", ""));
        Assert.Equal(25, p.Folha.Escala); Assert.Equal("PT/EN", p.Folha.Idioma);
        Assert.Empty(p.Folha.Detalhes); Assert.Empty(p.Folha.Vistas);
    }

    // ======================== planilha ========================

    private const string Planilha3 =
        "# blocos da obra\n" +
        "Nome;Pilares;Quantidade;Níveis;Arranjo;NEstX;EspX;Diâmetro Estaca (cm);Dist_Face;H_BASE;Ligação;principal_bitola;principal_qtd;principal_esp;Coluna Estranha\n" +
        "B3;\"P1; P2 e P3\";;\"P1=-3,43; P2=-3,43; P3=-11,63\";grade;2;75;25;17,5;45;arranque;12,5;4;;x\n" +
        "B4;P10;2;;triangulo;;150;50;45;95;colarinho;16;5;4,5;\n" +
        "B9;P20;;;grade;2;12O;40;35;55;colarinho;16;5;5;\n";

    [Fact]
    public void PlanilhaComTresBlocosUmComErro()
    {
        var r = ImportadorBlocos.Importar(Planilha3);
        Assert.Equal(new[] { "B3", "B4" }, r.Blocos.Select(b => b.Nome));
        Assert.Equal(new[] { 3, 4 }, r.Linhas);
        var erro = Assert.Single(r.Erros);
        Assert.Equal(5, erro.Linha);
        Assert.Equal("EspX", erro.Coluna);
        Assert.Contains("12O", erro.Texto);
        Assert.Contains(r.Avisos, a => a.Linha == 2 && a.Coluna == "Coluna Estranha");

        var b3 = r.Blocos[0];
        Assert.Equal(new[] { "P1", "P2", "P3" }, b3.Pilares);
        Assert.Equal(0, b3.Repeticoes);   // vazio = nº de pilares
        Assert.Equal(3, b3.Niveis.Count);
        Assert.Equal("P3", b3.Niveis[2].Pilar); Assert.Equal(-11.63, b3.Niveis[2].CotaM, 6);
        Assert.Equal(17.5, b3.DistFace);                 // vírgula decimal
        Assert.Equal(25, b3.DiamEstaca);                 // cabeçalho com acento, espaço e unidade
        Assert.Equal(45, b3.AlturaBase);                 // cabeçalho em maiúsculas
        Assert.Equal(LigacaoPilar.Arranque, b3.Ligacao);
        Assert.Equal(12.5, b3.Armadura(Funcoes.Principal).Bitola);
        Assert.Equal(4, b3.Armadura(Funcoes.Principal).Quantidade);
        Assert.Equal(5, b3.Armadura(Funcoes.Principal).Espacamento);   // vazio = padrão do template
        Assert.Equal(10, b3.Armadura(Funcoes.Secundaria).Bitola);      // coluna ausente = padrão

        var b4 = r.Blocos[1];
        Assert.Equal(ArranjoEstacas.Triangulo, b4.Arranjo);
        Assert.Equal(2, b4.Repeticoes);
        Assert.Equal(new[] { "P10" }, b4.Pilares);
        Assert.Empty(b4.Niveis);
        Assert.Equal(4.5, b4.Armadura(Funcoes.Principal).Espacamento);
    }

    [Fact]
    public void PlanilhaValidaOpcoesInteirosENome()
    {
        var r = ImportadorBlocos.Importar(
            "nome;nestx;ligacao;aco;fck\n" +
            ";2;colarinho;CA-50;30\n" +
            "B1;2,5;colarinho;CA-50;30\n" +
            "B2;2;soldado;CA-50;30\n" +
            "B3;2;embutido;ca60;35\n" +
            "B3;3;embutido;CA-50;30\n");
        Assert.Contains(r.Erros, m => m.Linha == 2 && m.Coluna == "nome");
        Assert.Contains(r.Erros, m => m.Linha == 3 && m.Coluna == "nestx");
        Assert.Contains(r.Erros, m => m.Linha == 4 && m.Coluna == "ligacao");
        Assert.Equal(2, r.Blocos.Count);
        Assert.Equal("CA-60", r.Blocos[0].Aco);
        Assert.Equal(LigacaoPilar.CaliceEmbutido, r.Blocos[0].Ligacao);
        Assert.Contains(r.Avisos, m => m.Linha == 6 && m.Texto.Contains("repetido"));
        Assert.Contains("linha 3", r.Erros.First(m => m.Linha == 3).ToString());
    }

    [Fact]
    public void PlanilhaSemColunaNomeEhErro()
    {
        var r = ImportadorBlocos.Importar("pilares;espx\nP1;120\n");
        Assert.Empty(r.Blocos);
        Assert.Contains(r.Erros, m => m.Linha == 1);
    }

    [Fact]
    public void ModeloDaPlanilhaRelidoDaB28EB15()
    {
        string csv = ImportadorBlocos.GerarModeloPlanilha();
        var cab = csv.Split('\n').First(l => !l.StartsWith("#")).Trim().Split(';');
        Assert.Equal(ImportadorBlocos.Colunas.ToArray(), cab);
        Assert.Contains("diam_estaca", cab); Assert.Contains("calice_topo_esp", cab); Assert.Contains("fundo_calice_qtd", cab);

        var r = ImportadorBlocos.Importar(csv);
        Assert.Empty(r.Mensagens);
        Assert.Equal(new[] { "B28", "B15" }, r.Blocos.Select(b => b.Nome));
        var b28 = r.Blocos[0];
        Assert.Equal(new[] { "P106", "P118", "P119", "P120", "P121", "P122" }, b28.Pilares);
        Assert.Equal(-11.43, b28.Niveis.Single().CotaM, 6);
        Assert.Equal(2, b28.EstacasX); Assert.Equal(120, b28.EspX); Assert.Equal(40, b28.DiamEstaca); Assert.Equal(35, b28.DistFace);
        Assert.Equal(55, b28.AlturaBase); Assert.Equal(40, b28.AlturaColarinho); Assert.Equal(21, b28.ParedeCalice);
        Assert.Equal(30, b28.PilarBx); Assert.Equal(20, b28.PilarBy); Assert.Equal(50, b28.Lemb);
        void A(ElementoBloco e, string f, double phi, int n, double s)
        {
            var a = e.Armadura(f);
            Assert.True(a != null && a.Bitola == phi && a.Quantidade == n && a.Espacamento == s, $"{e.Nome} {f}: {a?.Quantidade} ø{a?.Bitola} c/{a?.Espacamento}");
        }
        A(b28, Funcoes.Principal, 16, 5, 5);
        A(b28, Funcoes.Secundaria, 10, 0, 15);
        A(b28, Funcoes.Superior, 8, 0, 12);
        A(b28, Funcoes.Lateral, 8, 0, 15);
        A(b28, Funcoes.EstriboVertical, 8, 0, 12);
        A(b28, Funcoes.CaliceTopo, 12.5, 3, 5.5);
        A(b28, Funcoes.Calice, 10, 0, 12);
        A(b28, Funcoes.CaliceVertical, 10, 2, 0);
        A(b28, Funcoes.Suspensao, 10, 4, 12);
        A(b28, Funcoes.FundoCalice, 10, 7, 0);

        var b15 = r.Blocos[1];
        Assert.Equal(ArranjoEstacas.Triangulo, b15.Arranjo);
        Assert.Equal(24, b15.Repeticoes);
        Assert.Equal(150, b15.EspX); Assert.Equal(50, b15.DiamEstaca); Assert.Equal(40, b15.DistFace); Assert.Equal(105, b15.AlturaBase);
        Assert.Equal(30, b15.AlturaColarinho); Assert.Equal(25, b15.ParedeCalice); Assert.Equal(65, b15.Lemb);
        A(b15, Funcoes.CaliceTopo, 16, 5, 4);

        // o B15 da planilha gera o mesmo que o teste do template (válido para o template)
        var v = ConversorBloco.Valores(b15);
        Assert.Empty(CatalogoTemplates.Validar(T, v));
        // exportar de novo dá o mesmo texto
        Assert.Equal(csv.Split('\n').Where(l => !l.StartsWith("#")), ImportadorBlocos.Exportar(r.Blocos).Split('\n').Where(l => !l.StartsWith("#")));
    }

    [Fact]
    public void PlanilhaComPadraoDadoEValorForaDaFaixa()
    {
        var padrao = Padrao();
        padrao["DistFace"] = "40";
        var r = ImportadorBlocos.Importar("NOME;h_base\nB7;5\n", padrao);
        var b = Assert.Single(r.Blocos);
        Assert.Equal(40, b.DistFace);          // da linha PADRÃO dada
        Assert.Empty(b.Pilares);               // identificação não vem do padrão
        Assert.Contains(r.Avisos, m => m.Linha == 2 && m.Coluna == "h_base");   // hBase mínimo 20 → aviso, o bloco entra
    }

    // ======================== catálogo e sobrescritas ========================

    private sealed class MotorFalso : IMotorDetalhamento<ElementoBloco>
    {
        public int Chamadas;
        public string TipoElemento => ElementoBloco.TipoId;
        public ModeloArmadura Gerar(ElementoBloco elemento, PerfilDetalhamento perfil, IReadOnlyList<Sobrescrita> sobrescritas)
        {
            Chamadas++;
            var m = new ModeloArmadura { TipoElemento = TipoElemento, Nome = elemento.Nome };
            var p = new PosicaoArmadura { Chave = "principal_x", Funcao = Funcoes.Principal };
            p.Parametros["perna"] = perfil.Padrao("principal.perna", 46);
            foreach (var s in sobrescritas.Where(s => s.Posicao == p.Chave)) { p.Parametros[s.Parametro] = s.Valor; p.Manuais.Add(s.Parametro); }
            m.Posicoes.Add(p);
            return m;
        }
    }

    private sealed class ElementoPilarFalso : ElementoEstrutural
    {
        public override string Tipo => "pilar_teste";
    }

    [Fact]
    public void CatalogoDespachaPeloTipo()
    {
        var motor = new MotorFalso();
        var cat = new CatalogoMotores().Registrar(motor);
        Assert.True(cat.Tem("bloco_estacas"));
        Assert.Equal(new[] { "bloco_estacas" }, cat.Tipos);

        var sob = new List<Sobrescrita>();
        Sobrescritas.Definir(sob, "principal_x", "perna", 68);
        var m = cat.Gerar(new ElementoBloco { Nome = "B31" }, null, sob);
        Assert.Equal(1, motor.Chamadas);
        Assert.Equal("B31", m.Nome);
        Assert.Equal(68, m.Posicao("principal_x").Parametros["perna"]);
        Assert.Contains("perna", m.Posicao("principal_x").Manuais);

        var m2 = cat.Gerar(new ElementoBloco(), new PerfilDetalhamento { TipoElemento = "bloco_estacas" }, null);
        Assert.Equal(46, m2.Posicao("principal_x").Parametros["perna"]);

        var ex = Assert.Throws<InvalidOperationException>(() => cat.Gerar(new ElementoPilarFalso { Nome = "P1" }, null, null));
        Assert.Contains("pilar_teste", ex.Message);
        Assert.Contains("bloco_estacas", ex.Message);
        Assert.Throws<ArgumentException>(() => cat.Gerar(new ElementoBloco(), new PerfilDetalhamento { TipoElemento = "pilar_teste" }, null));
    }

    [Fact]
    public void UtilitariosDeSobrescritas()
    {
        var l = new List<Sobrescrita>();
        Sobrescritas.Definir(l, "principal_x", "perna", 68);
        Sobrescritas.Definir(l, "principal_x", "perna", 70);      // substitui
        Sobrescritas.Definir(l, "principal_x", "quantidade", 6);
        Sobrescritas.Definir(l, "calice_topo", "espacamento", 5);
        Assert.Equal(3, l.Count);
        Assert.Equal(70, Sobrescritas.Obter(l, "principal_x", "perna"));
        Assert.True(Sobrescritas.Remover(l, "principal_x", "quantidade"));
        Assert.False(Sobrescritas.Remover(l, "principal_x", "quantidade"));
        Assert.Null(Sobrescritas.Obter(l, "principal_x", "quantidade"));
        Assert.Equal(1, Sobrescritas.Limpar(l, "principal_x"));
        Assert.Equal(0, Sobrescritas.Limpar(l, "principal_x"));
        Assert.Equal("calice_topo", Assert.Single(l).Posicao);
        Assert.Throws<ArgumentException>(() => Sobrescritas.Definir(l, "", "perna", 1));
    }
}
