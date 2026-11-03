using System;
using System.Linq;
using DEB.Core.Configuracao;
using DEB.Core.Modelo;
using DEB.Core.Tabelas;
using DEB.Core.Templates;
using DEB.Core.Templates.PilarPreMoldado;
using Xunit;

namespace DEB.Core.Tests;

/// <summary>Etapa 2 do template do pilar: armaduras (medidas do P61).</summary>
[Collection("Pilar")]
public class PilarArmadurasTests
{
    private static readonly TemplatePilarPreMoldado T = new TemplatePilarPreMoldado();
    private static readonly double[] Cob = { 3.5, 3.5, 3.5, 3.5 };

    private static ResultadoTemplate Gerar(params (string k, string v)[] mudancas)
    {
        var v = new ValoresTemplate(T.Parametros);
        foreach (var (k, x) in mudancas) v[k] = x;
        Assert.Empty(CatalogoTemplates.Validar(T, v));
        return T.Gerar(v, new ConfiguracaoArmadura { AlturaTextoMm = 2 });
    }

    [Fact]
    public void BarrasDoCantoEmLComoNoP61()
    {
        // 50x50, cobrimento 3,5, estribo ø8, canto ø25 + 2 ø20 a 5 cm: centro do canto a 3,5 + 0,8 + 1,25 = 5,55 da face
        var b = TemplatePilarPreMoldado.PosicoesBarras(50, 50, Cob, 8, 25, 3, 5, 20, 0, 0, 20);
        Assert.Equal(12, b.Count);
        Assert.Equal(4, b.Count(x => x.Grupo == "canto"));
        Assert.Equal(8, b.Count(x => x.Grupo == "vizinha"));
        Assert.Contains(b, x => x.Grupo == "canto" && Math.Abs(x.X - 5.55) < 1e-9 && Math.Abs(x.Y - 5.55) < 1e-9);
        // vizinha da face inferior: 5 cm à direita do canto, alinhada ao estribo (3,5 + 0,8 + 1,0)
        Assert.Contains(b, x => x.Grupo == "vizinha" && Math.Abs(x.X - 10.55) < 1e-9 && Math.Abs(x.Y - 5.3) < 1e-9);
        // intermediárias: 2 por face b ficam entre as vizinhas, em terços
        var f = TemplatePilarPreMoldado.PosicoesBarras(50, 50, Cob, 8, 25, 3, 5, 20, 2, 0, 16);
        var baixo = f.Where(x => x.Grupo == "faceB" && x.Y < 25).Select(x => x.X).OrderBy(x => x).ToArray();
        Assert.Equal(2, baixo.Length);
        Assert.Equal(10.55 + (39.45 - 10.55) / 3, baixo[0], 6);
    }

    [Fact]
    public void TraspasseNuncaMenorQue45Phi()
    {
        var tab = TabelaBarras.Padrao();
        foreach (var phi in new[] { 10.0, 12.5, 16, 20, 25 })
            foreach (var mesma in new[] { false, true })
            {
                double l = TemplatePilarPreMoldado.TraspassePilar(phi, "CA-50", 40, mesma, tab);
                Assert.True(l >= 45 * phi / 10 - 1e-9, $"ø{phi}: {l}");
                Assert.Equal(0, l % 5, 6);
            }
        // na mesma seção (100 % emendadas, α0t = 2,0) o traspasse é maior que alternado (50 %, α0t = 1,8)
        Assert.True(TemplatePilarPreMoldado.TraspassePilar(20, "CA-50", 40, true, tab) > TemplatePilarPreMoldado.TraspassePilar(20, "CA-50", 40, false, tab));
    }

    [Fact]
    public void PecasComEmendas()
    {
        // 1383,5 cm com comercial de 1200 e traspasse 125: 1200 + (1075 → 1387)
        var p = TemplatePilarPreMoldado.Pecas(3.5, 1387, 1200, 125, 1200);
        Assert.Equal(2, p.Count);
        Assert.Equal(1203.5, p[0].Fim, 6);
        Assert.Equal(1203.5 - 125, p[1].Ini, 6);
        Assert.Equal(1387, p[1].Fim, 6);
        Assert.True(p.All(x => x.Comprimento <= 1200 + 1e-9));
        // curta: uma peça só
        Assert.Single(TemplatePilarPreMoldado.Pecas(0, 900, 1200, 100, 1200));
        // alternada: a 1ª peça do grupo B termina a pelo menos 1,3 traspasse da emenda do grupo A
        double pb = TemplatePilarPreMoldado.PrimeiraAlternada(1200, 125);
        Assert.True(1200 - pb >= 1.3 * 125);
        Assert.Equal(0, pb % 5, 6);
    }

    [Fact]
    public void P61CompletoComEmendasAlternadas()
    {
        var r = Gerar();
        var longs = r.Barras.Where(b => b.Nome.StartsWith("long")).ToList();
        // cantos (4 ø25) e vizinhas (8 ø20), cada grupo em A e B com 2 peças = 8 barras
        Assert.Equal(8, longs.Count);
        Assert.Equal(12, longs.Where(b => b.Vertices[0].Y < 10).Sum(b => b.Quantidade)); // peças que começam na base
        Assert.True(longs.All(b => Math.Abs(b.Vertices[1].Y - b.Vertices[0].Y) <= 1200 + 1e-9));
        // notas das emendas, U no topo e na base, estribos por bitola e grampos
        Assert.Contains(r.Textos, t => t.Texto.StartsWith("EMENDA: TRASPASSE") && t.Texto.Contains("AJUSTAR SE NECESSÁRIO"));
        Assert.Contains(r.Textos, t => t.Texto.StartsWith("(SPLICE: LAP"));
        Assert.Equal(4, r.Barras.Count(b => b.Nome.StartsWith("U")));
        Assert.Equal(2, r.Barras.Count(b => b.Nome.StartsWith("estribo") && b.Estribo));
        Assert.Single(r.Barras, b => b.Nome.StartsWith("grampo")); // seção quadrada: um grampo com "2x"
        // faixas: base, corrente, topo (estribos) + grampos no topo e na base com 2 repetições
        var ortog = r.Faixas.Where(f => f.Tipo == TipoFaixa.Ortogonal).ToList();
        Assert.Equal(3, ortog.Count(f => f.Barra.StartsWith("estribo")));
        Assert.Equal(2, ortog.Count(f => f.Barra.StartsWith("grampo") && f.Repeticoes == 2));
        Assert.True(ortog.All(f => !f.MostrarDiametro));
        // três seções com indicações para as barras e os U
        Assert.Contains(r.Textos, t => t.Texto == "SEÇÃO 1-1 - ARMADURA");
        Assert.Contains(r.Textos, t => t.Texto == "SEÇÃO 3-3 - ARMADURA");
        Assert.Contains(r.Faixas, f => f.Tipo == TipoFaixa.Indicacao && f.Barra.StartsWith("Utopo"));
        Assert.True(r.Faixas.Count(f => f.Tipo == TipoFaixa.Indicacao && f.Barra.StartsWith("long")) >= 6);
        // 12 barras × 3 seções + pernas dos U (topo e base) + as 3 do detalhe "espaçamento das barras" no canto
        Assert.Equal(12 * 3 + 3, r.Circulos.Count(cc => cc.Layer == "ARM-BARRA-SECAO"));
        Assert.Equal(2 * 3 * 2 * 2, r.Circulos.Count(cc => cc.Layer == "ARM-BARRA-2")); // pernas dos U, em azul como no P56
        Assert.NotNull(r.PosicaoRA);
        // estribo com ganchos de 90° (passagem pelo canto)
        var est = r.Barras.First(b => b.Nome.StartsWith("estribo"));
        Assert.Contains(FormatoSegmento.Gancho90, est.Formatos);
        Assert.NotEmpty(est.Sobrepostos);
    }

    [Fact]
    public void SemEmendasBarrasInteirasComAviso()
    {
        var r = Gerar(("Emendas", TemplatePilarPreMoldado.SemEmendas));
        var longs = r.Barras.Where(b => b.Nome.StartsWith("long")).ToList();
        Assert.Equal(2, longs.Count);
        Assert.All(longs, b => Assert.Equal(1383.5, b.Vertices[1].Y - b.Vertices[0].Y, 6));
        Assert.Contains(r.Avisos, a => a.Contains("rolo"));
        Assert.DoesNotContain(r.Textos, t => t.Texto.StartsWith("EMENDA:"));
    }

    [Fact]
    public void MesmaSecaoTodasJuntas()
    {
        var r = Gerar(("EmendaArranjo", TemplatePilarPreMoldado.MesmaSecao));
        var longs = r.Barras.Where(b => b.Nome.StartsWith("long")).ToList();
        Assert.Equal(4, longs.Count); // 2 grupos × 2 peças, sem grupo B
        Assert.Equal(12, longs.Where(b => b.Vertices[0].Y < 10).Sum(b => b.Quantidade));
    }

    [Fact]
    public void AvisosDaNbr6118()
    {
        var r = Gerar(("EstBitola", "5"), ("EstEsp", "25"), ("Longitudinais", "Cantos||25|1||;Ao lado dos cantos||20|1||;Faces 2 e 4|3|20|1||"),
                      ("ZonasEstribos", "Topo|Topo|0|100|8|5|Não;Base|Base|0|260|8|10|Não"));
        var pequeno = Gerar(("B", "20"), ("H", "20"), ("Longitudinais", "Cantos||32|1||"), ("UTopo", "Não"), ("UBase", "Não"));
        Assert.Contains(pequeno.Avisos, a => a.Contains("1/8 da menor dimensão"));
        Assert.Contains(r.Avisos, a => a.Contains("Espaçamento dos estribos"));
        Assert.Contains(r.Avisos, a => a.Contains("20φt"));
        Assert.Contains(r.Avisos, a => a.Contains("φ/4"));
        // sem barras de canto em parte do pilar
        var semCanto = Gerar(("Longitudinais", "Cantos||25|1||700;Faces 2 e 4|1|20|1||"));
        Assert.Contains(semCanto.Avisos, a => a.Contains("Sem barras de canto"));
    }

    [Fact]
    public void ListaDeLongitudinaisComFeixeEFaces()
    {
        var v = new ValoresTemplate(T.Parametros);
        v["Longitudinais"] = "Cantos||20|2||;Face 1|2|16|1||;Faces 2 e 4|1|12,5|1|100|900";
        var avisos = new System.Collections.Generic.List<string>();
        var l = TemplatePilarPreMoldado.LerLongitudinais(v, 3.5, 1387, avisos);
        Assert.Empty(avisos);
        Assert.Equal(3, l.Count);
        Assert.Equal(12.5, l[2].Bitola);
        Assert.Equal(100, l[2].Ini);
        Assert.Equal(1387, l[0].Fim); // vazio = até o topo (menos o cobrimento)
        var p = TemplatePilarPreMoldado.PosicoesLinhas(50, 50, Cob, 8, l, 5);
        // feixe de 2 ø20: o círculo envolvente tem raio φ = 2 cm → centro a 3,5 + 0,8 + 2 da face
        Assert.Contains(p[1], x => Math.Abs(x.X - 6.3) < 1e-9 && Math.Abs(x.Y - 6.3) < 1e-9 && x.Feixe == 2);
        // face 1 (+x): 2 barras, todas no lado direito
        Assert.Equal(2, p[2].Count);
        Assert.All(p[2], x => Assert.True(x.X > 40));
        // faces 2 e 4: uma por face, no meio
        Assert.Equal(2, p[3].Count);
        Assert.All(p[3], x => Assert.Equal(25, x.X, 6));
        Assert.Equal(2, TemplatePilarPreMoldado.CentrosFeixe(20, 2).Count);
        // quantidade da RA: 4 posições × 2 do feixe
        var r = Gerar(("Longitudinais", "Cantos||20|2||"), ("Emendas", TemplatePilarPreMoldado.SemEmendas));
        Assert.Equal(8, r.Barras.Where(b => b.Nome.StartsWith("long")).Sum(b => b.Quantidade));
        // "barras por face" fica vazio nos cantos (a quantidade é fixa)
        var col = T.Parametros.Single(x => x.Nome == "Longitudinais").Colunas[1];
        Assert.Equal("", col.Ajustar(new[] { "Cantos" }, "3"));
        Assert.Equal("3", col.Ajustar(new[] { "Face 2" }, "3"));
        Assert.Equal("12.5", T.Parametros.Single(x => x.Nome == "Longitudinais").Colunas[2].Ajustar(new[] { "Cantos" }, "12,5"));
    }

    [Fact]
    public void ReducaoComAlturaEmendaCotadaESecoes()
    {
        // faces 2 e 4: 2 ø20 até 700 e 1 ø16 a partir de 600 → emenda de 100 cm entre as linhas
        var r = Gerar(("Longitudinais", "Cantos||25|1||;Faces 2 e 4|2|20|1||700;Faces 2 e 4|1|16|1|600|"));
        Assert.Contains(r.Textos, t => t.Texto.StartsWith("EMENDA: TRASPASSE 100 cm"));
        // seções: base, corrente abaixo da emenda, corrente acima, topo
        Assert.Contains(r.Textos, t => t.Texto == "SEÇÃO 4-4 - ARMADURA");
        Assert.DoesNotContain(r.Textos, t => t.Texto == "SEÇÃO 5-5 - ARMADURA");
        // barra que termina em 700 e a que começa em 600 na mesma coluna
        var longs = r.Barras.Where(b => b.Nome.StartsWith("long")).ToList();
        Assert.Contains(longs, b => Math.Abs(b.Vertices[1].Y - 700) < 1e-6 && b.Bitola == 20);
        Assert.Contains(longs, b => Math.Abs(b.Vertices[0].Y - 600) < 1e-6 && b.Bitola == 16);
        // traspasse curto: aviso
        var curto = Gerar(("Longitudinais", "Cantos||25|1||;Faces 2 e 4|2|20|1||620;Faces 2 e 4|1|16|1|600|"));
        Assert.Contains(curto.Avisos, a => a.Contains("Emenda das longitudinais 2 e 3"));
        // linha contida em outra do mesmo arranjo: aviso
        var dup = Gerar(("Longitudinais", "Cantos||25|1||;Cantos||20|1|100|500"));
        Assert.Contains(dup.Avisos, a => a.Contains("mesma posição"));
    }

    [Fact]
    public void ZonasDeEstribosEmLista()
    {
        var v = new ValoresTemplate(T.Parametros);
        v["ZonasEstribos"] = "Topo|Topo|0|100|8|5|Sim;Consolo|Base|560|620|10|5|Não;Base|Base|0|260|8|10|Sim";
        var avisos = new System.Collections.Generic.List<string>();
        var z = TemplatePilarPreMoldado.LerZonas(v, 3.5, 1387, 1390.5, avisos);
        Assert.Empty(avisos);
        // base, corrente, consolo, corrente, topo — em ordem e sem vãos
        Assert.Equal(new[] { "Base", "corrente", "Consolo", "corrente", "Topo" }, z.Select(q => q.Nome));
        for (int i = 1; i < z.Count; i++) Assert.Equal(z[i - 1].Y2, z[i].Y1, 6);
        Assert.Equal(1290.5, z[4].Y1, 6); // medida a partir do topo
        Assert.False(z[2].Grampos);
        Assert.Equal(10, TemplatePilarPreMoldado.BitolaMaxEstribo(v));
        // no desenho: 5 faixas de estribos e 3 bitolas de estribo
        var r = Gerar(("ZonasEstribos", v["ZonasEstribos"]));
        Assert.Equal(5, r.Faixas.Count(f => f.Tipo == TipoFaixa.Ortogonal && f.Barra.StartsWith("estribo")));
        Assert.Equal(3, r.Barras.Count(b => b.Nome.StartsWith("estribo")));
        // zonas sobrepostas: aviso
        v["ZonasEstribos"] = "A|Base|0|300|8|10|Sim;B|Base|250|400|8|10|Sim";
        avisos.Clear();
        TemplatePilarPreMoldado.LerZonas(v, 3.5, 1387, 1390.5, avisos);
        Assert.Contains(avisos, a => a.Contains("se sobrepõem"));
    }
}
