using System;
using System.Collections.Generic;
using System.Linq;
using DEB.Core.Configuracao;
using DEB.Core.Motor;
using DEB.Core.Motor.Fundacoes;
using DEB.Core.Templates;
using DEB.Core.Templates.Blocos;
using Xunit;

namespace DEB.Core.Tests.Motor;

/// <summary>Motor de fundações (frente B): paridade com o TemplateBlocoEstacas (BlocoEstacasTests) e sobrescritas.</summary>
[Collection("Pilar")]   // o template de comparação usa catálogos estáticos
public class MotorBlocoTests
{
    // ---------------- montagem ----------------

    /// <summary>Perfil do escritório com as chaves combinadas (sem depender da frente A).</summary>
    private static PerfilDetalhamento Perfil() => new PerfilDetalhamento
    {
        Id = "teste", TipoElemento = ElementoBloco.TipoId,
        Padroes =
        {
            ["cobrimento.solo"] = 4, ["cobrimento.calice"] = 2, ["secundaria.perna"] = 20, ["superior.perna"] = 20,
            ["suspensao.desconto_perna_externa"] = 6, ["arranque.pe"] = 20, ["arranque.espera"] = 0, ["calice.grampos"] = 1,
            ["principal.perna"] = 0, ["fundo_calice.comprimento"] = 100,
        },
        Regras = { ["principal.perna"] = "altura_base", ["arranque.pe_sentido"] = "fora" },
    };

    private static ArmaduraCalculada A(string f, double phi, int q = 0, double esp = 0) =>
        new ArmaduraCalculada { Funcao = f, Bitola = phi, Quantidade = q, Espacamento = esp };

    /// <summary>B28 (valores padrão do template).</summary>
    private static ElementoBloco B28() => new ElementoBloco
    {
        Nome = "B28", Pilares = { "P106", "P118", "P119", "P120", "P121", "P122" },
        Armaduras =
        {
            A(Funcoes.Principal, 16, 5, 5), A(Funcoes.Secundaria, 10, 0, 15), A(Funcoes.Superior, 8, 0, 12),
            A(Funcoes.Lateral, 8, 0, 15), A(Funcoes.EstriboVertical, 8, 0, 12),
            A(Funcoes.CaliceTopo, 12.5, 3, 5.5), A(Funcoes.Calice, 10, 0, 12), A(Funcoes.CaliceVertical, 10, 2),
            A(Funcoes.Suspensao, 10, 4, 12), A(Funcoes.FundoCalice, 10, 7),
            A(Funcoes.Arranque, 12.5), A(Funcoes.ArranqueEstribo, 6.3, 3),
        },
    };

    private static ElementoBloco B15()
    {
        var e = B28();
        e.Nome = "B15"; e.Arranjo = ArranjoEstacas.Triangulo; e.EspX = 150; e.DistFace = 40; e.DiamEstaca = 50;
        e.PilarBx = 40; e.PilarBy = 40; e.ParedeCalice = 25; e.AlturaBase = 105; e.AlturaColarinho = 30; e.Lemb = 65;
        return e;
    }

    private static ElementoBloco B31()
    {
        var e = B28();
        e.Nome = "B31"; e.EstacasY = 2; e.EspX = 150; e.EspY = 150; e.DistFace = 40; e.DiamEstaca = 50;
        e.PilarBx = 60; e.PilarBy = 60; e.ParedeCalice = 30; e.AlturaBase = 110; e.AlturaColarinho = 50; e.Lemb = 95;
        e.Armadura(Funcoes.Principal).Bitola = 20; e.Armadura(Funcoes.Principal).Quantidade = 7; e.Armadura(Funcoes.Principal).Espacamento = 14;
        return e;
    }

    private static ElementoBloco Arranque() { var e = B28(); e.Ligacao = LigacaoPilar.Arranque; return e; }

    private static ElementoBloco UmaEstaca()
    {
        var e = B28();
        e.Nome = "B1"; e.EstacasX = 1; e.EstacasY = 1; e.EspX = 0; e.DiamEstaca = 50; e.AlturaBase = 70;
        e.Ligacao = LigacaoPilar.CaliceEmbutido; e.Lemb = 45;
        return e;
    }

    private static ModeloArmadura Gerar(ElementoBloco e, params Sobrescrita[] s)
    {
        var m = new MotorBlocoEstacas().Gerar(e, Perfil(), s);
        Assert.Equal(ElementoBloco.TipoId, m.TipoElemento);
        Assert.Equal(m.Posicoes.Count, m.Posicoes.Select(p => p.Chave).Distinct().Count());
        foreach (var p in m.Posicoes)
        {
            Assert.True(p.Vertices.Count >= 2, p.Chave);
            Assert.True(p.Bitola > 0, p.Chave);
            Assert.Equal(p.Distribuicao.Quantidade * p.Distribuicao.Repeticoes, p.Barras.Count);
            Assert.Equal(p.Vertices.Count - 1, p.Formatos.Count);
            if (p.ComprimentosVar.Count > 0) Assert.Equal(p.ComprimentosVar.Count, p.VerticesVar.Count);
        }
        Assert.DoesNotContain(m.Avisos, a => a.Contains("[VALIDAR]"));
        return m;
    }

    private static double Seg(List<P3> v, int i) => (v[i + 1] - v[i]).Comprimento;

    // ---------------- paridade com o template ----------------

    private static readonly Dictionary<string, string> NomeTemplate = new Dictionary<string, string>
    {
        ["principal_x"] = "principal x", ["principal_y"] = "principal y", ["secundaria_x"] = "secundária x", ["secundaria_y"] = "secundária y",
        ["superior_x"] = "superior x", ["superior_y"] = "superior y", ["lateral"] = "lateral horizontal", ["estribo_vertical_x"] = "estribo vertical",
        ["calice_topo"] = "cálice: horizontais do topo", ["calice_grampos_topo_x"] = "cálice: grampos do topo (x)",
        ["calice_grampos_topo_y"] = "cálice: grampos do topo (y)", ["calice"] = "cálice: horizontais", ["calice_grampos_x"] = "cálice: grampos (x)",
        ["calice_grampos_y"] = "cálice: grampos (y)", ["calice_vertical"] = "cálice: verticais", ["suspensao"] = "suspensão",
        ["fundo_calice"] = "sob a base do pilar", ["arranque"] = "arranque", ["arranque_estribo"] = "estribo do pilar no bloco",
    };

    private static ResultadoTemplate Template(Action<ValoresTemplate> ajustar, bool uma = false)
    {
        var t = new TemplateBlocoEstacas(uma);
        var v = new ValoresTemplate(t.Parametros);
        v["FundoBitola"] = "10";
        ajustar?.Invoke(v);
        return t.Gerar(v, new ConfiguracaoArmadura());
    }

    /// <summary>Mesmas barras (bitola, quantidade total, VAR) que o template, exceto as principais do triangular (VAR no motor).</summary>
    private static void Paridade(ModeloArmadura m, ResultadoTemplate r)
    {
        bool tri = m.Posicao("principal_lado1") != null;
        foreach (var b in r.Barras.Where(b => !(tri && b.Nome.StartsWith("principal"))))
        {
            string chave = NomeTemplate.Single(kv => kv.Value == b.Nome).Key;
            var p = m.Posicao(chave);
            Assert.True(p != null, "falta " + chave);
            Assert.Equal(b.Bitola, p.Bitola);
            Assert.Equal(b.Quantidade * Math.Max(1, b.Repeticoes), p.Barras.Count);
            Assert.Equal(b.ComprimentosVar, p.ComprimentosVar);
        }
        Assert.Equal(r.Barras.Count, m.Posicoes.Count);
        // avisos do template (sem desenho) também no motor
        foreach (var a in r.Avisos) Assert.Contains(a, m.Avisos);
    }

    [Fact]
    public void B28CaliceComColarinho()
    {
        var m = Gerar(B28());
        Paridade(m, Template(null));
        var p = m.Posicao("principal_x");
        Assert.Equal(16, p.Bitola);
        Assert.Equal(5, p.Distribuicao.Quantidade); Assert.Equal(1, p.Distribuicao.Repeticoes); Assert.Equal(5, p.Distribuicao.Espacamento);
        Assert.Equal(46, p.Parametros["perna"]); Assert.Equal(180, p.Parametros["comprimento_reto"]);
        Assert.Equal("topo da base − cobrimento", p.Regras["perna"]);
        Assert.Equal(46, Seg(p.Vertices, 0), 6); Assert.Equal(180, Seg(p.Vertices, 1), 6);
        Assert.Equal(5, p.Vertices[1].Z, 6);                       // face de baixo = máx(cobrimento, embutimento da estaca)
        Assert.Null(m.Posicao("principal_y"));
        Assert.Empty(p.Manuais);

        var topo = m.Posicao("calice_topo");
        Assert.Equal(FormaBarra.Estribo, topo.Forma);
        Assert.Equal(3, topo.Distribuicao.Quantidade); Assert.Equal(5.5, topo.Distribuicao.Espacamento);
        Assert.Equal(72, topo.Vertices.Max(v => v.X) - topo.Vertices.Min(v => v.X), 6);
        Assert.Equal(62, topo.Vertices.Max(v => v.Y) - topo.Vertices.Min(v => v.Y), 6);
        Assert.Equal(2, topo.Sobrepostos.Count);
        Assert.True(topo.Distribuicao.Inicio.Z > topo.Distribuicao.Fim.Z);   // do topo para baixo

        var gr = m.Posicao("calice_grampos_topo_x");
        Assert.Equal(15, Seg(gr.Vertices, 0), 6); Assert.Equal(72, Seg(gr.Vertices, 1), 6); Assert.Equal(2, gr.Distribuicao.Repeticoes);
        Assert.Equal(8, m.Posicao("calice_vertical").Distribuicao.Quantidade * m.Posicao("calice_vertical").Distribuicao.Repeticoes);
        var s = m.Posicao("suspensao");
        Assert.Equal(4, s.Distribuicao.Quantidade);
        Assert.Equal(6, s.Parametros["perna"] - s.Parametros["perna_externa"], 6);
        Assert.NotNull(m.Posicao("calice"));
        Assert.Equal(100, Seg(m.Posicao("fundo_calice").Vertices, 0), 6);
        Assert.True(m.Posicao("superior_x") != null || m.Posicao("superior_y") != null);
        Assert.Null(m.Posicao("arranque"));

        // corpos e níveis
        var bloco = m.Corpos.Single(k => k.Papel == "bloco");
        Assert.Equal(190, bloco.Planta.Max(q => q.X), 6); Assert.Equal(70, bloco.Planta.Max(q => q.Y), 6);
        Assert.Equal(0, bloco.Z0); Assert.Equal(55, bloco.Z1);
        Assert.True(m.Corpos.Single(k => k.Papel == "furo_calice").Vazio);
        Assert.Equal(2, m.Corpos.Count(k => k.Papel == "estaca" && k.Raio == 20 && k.Planta.Count == 0));
        Assert.Contains(m.Corpos, k => k.Papel == "colarinho" && k.Z0 == 55 && k.Z1 == 95);
        Assert.Contains(m.Corpos, k => k.Papel == "lastro" && k.Z0 == -5);
        Assert.Contains(m.Corpos, k => k.Papel == "regularizacao");
        Assert.Contains(m.Corpos, k => k.Papel == "pilar" && k.Z0 == 45);
        Assert.Equal(0, m.Niveis["fundo"]); Assert.Equal(55, m.Niveis["topo_base"]); Assert.Equal(95, m.Niveis["topo"]);
        Assert.Equal(45, m.Niveis["fundo_pilar"]); Assert.Equal(5, m.Niveis["arrasamento"]);
    }

    [Fact]
    public void B15Triangular()
    {
        var m = Gerar(B15());
        Paridade(m, Template(v =>
        {
            v["Nome"] = "B15"; v["Arranjo"] = TemplateBlocoEstacas.Triangulo; v["EspX"] = "150"; v["DistFace"] = "40"; v["DiamEstaca"] = "50";
            v["PilarBx"] = "40"; v["PilarBy"] = "40"; v["tParede"] = "25"; v["hBase"] = "105"; v["hColar"] = "30"; v["Lemb"] = "65";
        }));
        var bloco = m.Corpos.Single(k => k.Papel == "bloco");
        Assert.Equal(6, bloco.Planta.Count);
        Assert.Equal(210, bloco.Planta.Max(q => q.X) - bloco.Planta.Min(q => q.X), 0);
        Assert.Equal(242.4, bloco.Planta.Max(q => q.Y) - bloco.Planta.Min(q => q.Y), 1);
        Assert.Equal(3, m.Posicoes.Count(p => p.Funcao == Funcoes.Principal));
        Assert.All(new[] { "principal_lado1", "principal_lado2", "principal_lado3" }, k => Assert.NotNull(m.Posicao(k)));
        var sec = m.Posicoes.First(p => p.Funcao == Funcoes.Secundaria && p.ComprimentosVar.Count > 1);
        Assert.Equal(sec.ComprimentosVar.Count, sec.VerticesVar.Count);
        Assert.Contains(1, sec.SegmentosVar);
        Assert.Equal(FormaBarra.Estribo, m.Posicao("lateral").Forma);
        Assert.Null(m.Posicao("estribo_vertical_x"));
        Assert.Contains(m.Avisos, a => a.StartsWith("Bloco triangular"));
    }

    [Fact]
    public void B31QuatroEstacas()
    {
        var m = Gerar(B31());
        Paridade(m, Template(v =>
        {
            v["Nome"] = "B31"; v["NEstY"] = "2"; v["EspX"] = "150"; v["EspY"] = "150"; v["DistFace"] = "40"; v["DiamEstaca"] = "50";
            v["PilarBx"] = "60"; v["PilarBy"] = "60"; v["tParede"] = "30"; v["hBase"] = "110"; v["hColar"] = "50"; v["Lemb"] = "95";
            v["PrincBitola"] = "20"; v["PrincQtd"] = "7"; v["PrincEsp"] = "14";
        }));
        foreach (var k in new[] { "principal_x", "principal_y" })
        {
            var p = m.Posicao(k);
            Assert.Equal(7, p.Distribuicao.Quantidade); Assert.Equal(2, p.Distribuicao.Repeticoes); Assert.Equal(14, p.Barras.Count);
            Assert.Equal(101, p.Parametros["perna"]);
        }
        var topo = m.Posicao("calice_topo");
        Assert.Equal(120, topo.Vertices.Max(v => v.X) - topo.Vertices.Min(v => v.X), 6);
        Assert.NotNull(m.Posicao("estribo_vertical_x"));
        Assert.DoesNotContain(m.Avisos, a => a.Contains("Embutimento") && a.Contains("40 cm"));
    }

    [Fact]
    public void ArranqueSemCalice()
    {
        var m = Gerar(Arranque());
        Paridade(m, Template(v => v["Ligacao"] = TemplateBlocoEstacas.Arranque));
        var a = m.Posicao("arranque");
        Assert.Equal(4, a.Distribuicao.Quantidade); Assert.Equal(4, a.Barras.Count);
        Assert.Equal(20, Seg(a.Vertices, 0), 6);                               // pé
        Assert.Equal(55, a.Vertices[1].Z + a.Parametros["comprimento_interno"], 6);   // fundo + trecho = topo
        Assert.Equal("traspasse NBR 6118 (100 % emendado), a cada 5 cm", a.Regras["espera"]);
        // pé para fora: a barra da esquerda tem o pé à esquerda
        var esq = a.Barras.OrderBy(b => b[1].X).First();
        Assert.True(esq[0].X < esq[1].X);
        Assert.NotNull(m.Posicao("arranque_estribo"));
        Assert.DoesNotContain(m.Posicoes, p => p.Chave.StartsWith("calice"));
        Assert.DoesNotContain(m.Corpos, k => k.Papel == "furo_calice" || k.Papel == "colarinho");
        Assert.False(m.Niveis.ContainsKey("fundo_pilar"));
    }

    [Fact]
    public void UmaEstacaComCaliceEmbutido()
    {
        var m = Gerar(UmaEstaca());
        var r = Template(v => { v["Ligacao"] = TemplateBlocoEstacas.Embutido; v["Lemb"] = "45"; }, uma: true);
        Paridade(m, r);
        Assert.DoesNotContain(m.Posicoes, p => p.Funcao == Funcoes.Principal);
        Assert.NotNull(m.Posicao("secundaria_x"));
        Assert.NotNull(m.Posicao("calice_topo"));
        Assert.DoesNotContain(m.Corpos, k => k.Papel == "colarinho");
        Assert.Single(m.Corpos, k => k.Papel == "estaca");
    }

    // ---------------- sobrescritas ----------------

    [Fact]
    public void SobrescritaDaPernaDoB31MudaGeometria()
    {
        var antes = Gerar(B31()).Posicao("principal_x");
        var m = Gerar(B31(), new Sobrescrita { Posicao = "principal_x", Parametro = "perna", Valor = 68 });
        var p = m.Posicao("principal_x");
        Assert.Equal(68, p.Parametros["perna"]);
        Assert.Contains("perna", p.Manuais);
        Assert.Equal(68, Seg(p.Vertices, 0), 6);
        Assert.Equal(68, Seg(p.Vertices, 2), 6);
        Assert.Equal(101, Seg(antes.Vertices, 0), 6);
        Assert.All(p.Barras, b => Assert.Equal(68, Seg(b, 0), 6));
        // a outra direção continua pela regra
        Assert.Empty(m.Posicao("principal_y").Manuais);
        Assert.Equal(101, m.Posicao("principal_y").Parametros["perna"]);
        Assert.DoesNotContain(m.Avisos, a => a.StartsWith("principal_x"));
        Assert.DoesNotContain(m.Avisos, a => a.StartsWith("Sobrescrita ignorada"));
    }

    [Fact]
    public void SobrescritaForaDaNormaAvisa()
    {
        var m = Gerar(B31(), new Sobrescrita { Posicao = "principal_x", Parametro = "perna", Valor = 150 },
            new Sobrescrita { Posicao = "secundaria_x", Parametro = "perna", Valor = -5 });
        Assert.Contains(m.Avisos, a => a.StartsWith("principal_x: perna manual 150"));
        Assert.Contains(m.Avisos, a => a.StartsWith("secundaria_x: perna manual = -5"));
        var arr = Gerar(Arranque(), new Sobrescrita { Posicao = "arranque", Parametro = "espera", Valor = 10 });
        Assert.Contains(arr.Avisos, a => a.StartsWith("Arranque: espera manual 10"));
        Assert.Equal(10, arr.Posicao("arranque").Vertices.Last().Z - 55, 6);
    }

    [Fact]
    public void RegenerarDaAsMesmasChaves()
    {
        var s = new[] { new Sobrescrita { Posicao = "principal_x", Parametro = "perna", Valor = 68 } };
        foreach (var el in new Func<ElementoBloco>[] { B28, B15, B31, Arranque, UmaEstaca })
        {
            var a = new MotorBlocoEstacas().Gerar(el(), Perfil(), s);
            var b = new MotorBlocoEstacas().Gerar(el(), Perfil(), s);
            Assert.Equal(a.Posicoes.Select(p => p.Chave), b.Posicoes.Select(p => p.Chave));
            for (int i = 0; i < a.Posicoes.Count; i++) Assert.Equal(a.Posicoes[i].Vertices, b.Posicoes[i].Vertices);
            Assert.Equal(a.Avisos, b.Avisos);
        }
    }

    [Fact]
    public void SobrescritaInexistenteViraAviso()
    {
        var m = Gerar(B28(), new Sobrescrita { Posicao = "principal_y", Parametro = "perna", Valor = 50 },
            new Sobrescrita { Posicao = "calice_topo", Parametro = "inventado", Valor = 1 });
        Assert.Contains(m.Avisos, a => a.Contains("\"principal_y\" não existe"));
        Assert.Contains(m.Avisos, a => a.Contains("\"calice_topo\" não tem o parâmetro \"inventado\""));
        Assert.NotNull(m.Posicao("principal_x"));
    }

    [Fact]
    public void PerfilSemChavesUsaOsPadroes()
    {
        var com = new MotorBlocoEstacas().Gerar(B28(), Perfil(), null);
        var sem = new MotorBlocoEstacas().Gerar(B28(), new PerfilDetalhamento(), null);
        Assert.Equal(com.Posicoes.Select(p => p.Chave), sem.Posicoes.Select(p => p.Chave));
        for (int i = 0; i < com.Posicoes.Count; i++) Assert.Equal(com.Posicoes[i].Vertices, sem.Posicoes[i].Vertices);
    }

    [Fact]
    public void PernaPrincipalPorValorDoPerfil()
    {
        var pf = Perfil(); pf.Regras["principal.perna"] = "valor"; pf.Padroes["principal.perna"] = 30;
        var p = new MotorBlocoEstacas().Gerar(B28(), pf, null).Posicao("principal_x");
        Assert.Equal(30, p.Parametros["perna"]);
        Assert.Equal("valor do perfil (principal.perna)", p.Regras["perna"]);
        Assert.Empty(p.Manuais);
    }

    [Fact]
    public void FuncaoAusenteOuBitolaZeroNaoGeraPosicao()
    {
        var e = B28();
        e.Armaduras.RemoveAll(a => a.Funcao == Funcoes.Suspensao);
        e.Armadura(Funcoes.Superior).Bitola = 0;
        var m = Gerar(e);
        Assert.Null(m.Posicao("suspensao"));
        Assert.DoesNotContain(m.Posicoes, p => p.Funcao == Funcoes.Superior);
    }
}
