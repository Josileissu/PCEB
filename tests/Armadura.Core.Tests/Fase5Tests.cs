using System;
using System.Globalization;
using System.Linq;
using Armadura.Core.Modelo;
using Armadura.Core.RelacaoAco;
using Xunit;

namespace Armadura.Core.Tests;

public class Fase5Tests
{
    private static string T(int i, double l) => Math.Round(l).ToString(CultureInfo.InvariantCulture);
    private static GeoSegmento R(double l, double defl = 0) => new GeoSegmento(TipoSegmento.Reta, l, defl);

    [Fact]
    public void BarraRetaOcupaALarguraComAMedidaEmCima()
    {
        var e = EsquemaDobra.Gerar(new[] { R(994) }, false, 100, 20, 2, T);
        Assert.Equal(2, e.Vertices.Count);
        double margem = 1.8 * 2;
        Assert.Equal(-50 + margem, e.Vertices[0].X, 6);
        Assert.Equal(50 - margem, e.Vertices[1].X, 6);
        Assert.All(e.Vertices, v => Assert.Equal(0, v.Y, 6));
        var r = Assert.Single(e.Rotulos);
        Assert.Equal("994", r.Texto);
        Assert.True(r.Y > 0);
        Assert.Equal(0, r.Rotacao, 6);
    }

    [Fact]
    public void GanchosCurtosAparecemEMaiorTrechoFicaNaHorizontal()
    {
        // 12 ↓, 961 →, 12 ↑ desenhado na vertical (rumo inicial qualquer): o 961 deve ficar horizontal
        var geo = new[] { R(12), R(961, 90), R(12, 90) };
        var e = EsquemaDobra.Gerar(geo, false, 100, 20, 2, T);
        Assert.Equal(4, e.Vertices.Count);
        var a = e.Vertices[1];
        var b = e.Vertices[2];
        Assert.Equal(a.Y, b.Y, 6); // trecho de 961 horizontal
        double longo = Math.Abs(b.X - a.X);
        double gancho = Math.Abs(e.Vertices[1].Y - e.Vertices[0].Y);
        Assert.True(gancho >= 0.10 * longo, $"gancho visível ({gancho} de {longo})"); // mínimo de 12% antes de encaixar
        Assert.Equal(new[] { "12", "961", "12" }, e.Rotulos.Select(r => r.Texto));
        // tudo dentro da caixa
        Assert.All(e.Vertices, v => Assert.True(Math.Abs(v.X) <= 50 && Math.Abs(v.Y) <= 10));
        // medidas dos ganchos verticais giradas a 90° (legíveis)
        Assert.Equal(Math.PI / 2, Math.Abs(e.Rotulos[0].Rotacao), 6);
    }

    [Fact]
    public void TabelaComDetalheDeDobraNoFim()
    {
        var cfg = new Configuracao.ConfiguracaoArmadura();
        var forma = new System.Collections.Generic.List<GeoSegmento> { R(12), R(961, 90), R(12, 90) };
        var r = RelacaoDoAco.Calcular(new[]
        {
            new ItemRA { Numero = 1, Aco = "CA-50", Bitola = 10, Quantidade = 2, ComprimentoUnitario = 985, Forma = forma },
            new ItemRA { Numero = 2, Aco = "CA-50", Bitola = 12.5, Quantidade = 20, ComprimentoUnitario = 994, Forma = new() { R(994) } },
        }, cfg);
        var sem = MontadorTabela.Montar(r, new OpcoesTabelaRA(), cfg);
        var com = MontadorTabela.Montar(r, new OpcoesTabelaRA { DetalheDobra = true }, cfg);

        Assert.Equal(sem.Colunas + 1, com.Colunas);
        Assert.Equal(com.Colunas - 1, com.ColunaDetalhe);
        // a estrutura atual não muda: as colunas antigas têm os mesmos textos
        for (int i = 0; i < sem.Linhas.Count; i++)
            Assert.Equal(sem.Linhas[i], com.Linhas[i].Take(sem.Colunas).ToArray());
        Assert.Equal(MontadorTabela.TituloDetalhe, com.Linhas[1][com.ColunaDetalhe]);
        Assert.Equal(2, com.LinhasComForma.Count);
        Assert.Same(forma, com.LinhasComForma[2].Forma);
        // título e resumo se estendem até a nova coluna
        Assert.Contains((0, 0, com.ColunaDetalhe), com.Mesclas);
        int cabResumo = com.Linhas.FindIndex(l => l[4] == "PESO (kg)");
        Assert.Contains((cabResumo, 4, com.ColunaDetalhe), com.Mesclas);
    }

    [Fact]
    public void ListaDeCorteEDobra()
    {
        var cfg = new Configuracao.ConfiguracaoArmadura();
        var r = RelacaoDoAco.Calcular(new[]
        {
            new ItemRA { Numero = 1, Aco = "CA-50", Bitola = 10, Quantidade = 2, ComprimentoUnitario = 985,
                         Forma = new() { R(12), R(961, 90), R(12, 90) }, Elemento = "V1" },
            new ItemRA { Numero = 2, Aco = "CA-60", Trelica = "TR 8644", Quantidade = 4, ComprimentoUnitario = 300, Forma = new() { R(300) } },
        }, cfg, porElemento: true);
        var csv = ExportadorCorteDobra.Csv(r, cfg).Split('\n').Where(l => l.Trim().Length > 0).ToArray();
        Assert.Equal(2, csv.Length); // cabeçalho + 1 (treliça fica fora)
        Assert.StartsWith("ELEMENTO;N;AÇO;BITOLA (mm)", csv[0]);
        Assert.Contains("\"A=12; B=961; C=12\"", csv[1]);
        Assert.Contains("\"B:90; C:90\"", csv[1]);
        Assert.Contains(";19,70;", csv[1]);       // 1970 cm = 19,70 m (vírgula decimal)
        Assert.Contains(";12,15;", csv[1]);       // 19,70 m × 0,617 = 12,15 kg

        var html = ExportadorCorteDobra.Html(r, cfg, "Teste");
        Assert.Contains("<svg", html);
        Assert.Contains(">961<", html);
        Assert.DoesNotContain("TR 8644", html);
        Assert.Equal("AA", ExportadorCorteDobra.Letra(26));
    }

    [Fact]
    public void ArcoMantemOBulge()
    {
        var geo = new[] { R(200), new GeoSegmento(TipoSegmento.Arco, Math.PI * 50, 0, 180), R(200) };
        var e = EsquemaDobra.Gerar(geo, false, 100, 40, 2, T);
        Assert.Equal(1.0, Math.Abs(e.Vertices[1].Bulge), 6); // meia-volta: bulge ±1
        Assert.Equal(3, e.Rotulos.Count);
        Assert.Equal("157", e.Rotulos[1].Texto);
    }
}
