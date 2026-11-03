using System;
using System.IO;
using System.Linq;
using DEB.Core.Importacao;
using DEB.Core.Modelo;
using Xunit;
using Xunit.Abstractions;

namespace DEB.Core.Tests;

/// <summary>Importação do .cad do QiArmaduras com os exemplos do usuário (armadura/auxiliar_cad/).</summary>
public class ImportacaoQiCadTests
{
    private readonly ITestOutputHelper _saida;
    public ImportacaoQiCadTests(ITestOutputHelper saida) { _saida = saida; }

    private static string Exemplo(string nome)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "auxiliar_cad"))) dir = dir.Parent;
        var caminho = dir == null ? null : Path.Combine(dir.FullName, "auxiliar_cad", nome);
        return caminho != null && File.Exists(caminho) ? caminho : null;
    }

    [Fact]
    public void DesenhoDeTesteDoUsuario()
    {
        var arq = Exemplo("Desenho 1.cad");
        if (arq == null) return; // exemplo só existe na máquina do usuário
        var doc = LeitorQiCad.Ler(arq);
        Assert.Equal(3560, doc.Todos.Count());
        var r = ConversorQiCad.Converter(doc);
        foreach (var a in r.Avisos) _saida.WriteLine("aviso: " + a);
        Assert.Equal(50, r.EscalaPredominante);
        Assert.Equal(4, r.Barras.Count); // barra + estribo, soltos e no detalhamento
        Assert.Single(r.Detalhamentos, d => d.Nome == "teste1");
        Assert.Equal(2, r.Barras.Count(b => b.Detalhamento == "teste1"));

        var reta = r.Barras.First(b => !b.Dados.Estribo && b.Detalhamento == "");
        Assert.Equal(5.0, reta.Dados.Bitola);
        Assert.Equal("CA-60", reta.Dados.Aco);
        Assert.Equal(10, reta.Dados.Quantidade);
        Assert.Equal(1, reta.Dados.Numero);
        Assert.Equal(547, reta.Pontos[1].X - reta.Pontos[0].X, 1); // 10,94 × 50
        Assert.Equal(50, reta.Dados.Escala);

        var est = r.Barras.First(b => b.Dados.Estribo && b.Detalhamento == "");
        Assert.Equal(9, est.Pontos.Count);
        Assert.Equal(new[] { FormatoSegmento.Gancho45, FormatoSegmento.Linha, FormatoSegmento.Linha, FormatoSegmento.Linha,
                             FormatoSegmento.Linha, FormatoSegmento.Linha, FormatoSegmento.Linha, FormatoSegmento.Gancho45 },
                     est.Dados.Segmentos.Select(s => s.Formato));
        Assert.True(est.Dados.Segmentos[1].Sobreposto && est.Dados.Segmentos[6].Sobreposto);
        Assert.Equal(14, est.Dados.Espacamento);
        Assert.Equal(117, est.Pontos[3].X - est.Pontos[2].X, 0); // largura do estribo
        Assert.True(est.Descricao.HasValue);

        Assert.Equal(2, r.Rebatidas.Count(x => x.Detalhamento == ""));
        Assert.Contains(r.Faixas, f => f.Dados.Tipo == TipoFaixa.Ortogonal && f.Barra == est.Origem);
        Assert.Contains(r.Faixas, f => f.Dados.Tipo == TipoFaixa.Indicacao && f.Dados.Referencias.Count == 1);
        Assert.Single(r.Tabelas);
        Assert.Contains(r.Linhas, l => l.Detalhamento == "" && l.Pontos.Count == 3); // polilinha qualquer
        Assert.False(r.NumeracaoPorDetalhamento); // N1/N2 iguais às soltas: mesma forma
    }

    [Fact]
    public void ProjetoRealDeBlocos()
    {
        var arq = Exemplo("PRANCHAS_Blocos_R00.cad");
        if (arq == null) return;
        var r = ConversorQiCad.Converter(LeitorQiCad.Ler(arq));
        foreach (var a in r.Avisos) _saida.WriteLine("aviso: " + a);
        _saida.WriteLine($"{r.Barras.Count} barras, {r.Faixas.Count} faixas, {r.Linhas.Count} linhas, {r.Circulos.Count} círculos, " +
                         $"{r.Textos.Count} textos, {r.Cotas.Count} cotas, {r.Detalhamentos.Count} detalhamentos, {r.Tabelas.Count} tabelas");
        Assert.Equal(327, r.Barras.Count);
        Assert.Equal(20, r.Detalhamentos.Count);
        Assert.All(r.Barras, b => Assert.True(b.Dados.Bitola > 0, $"bitola da barra #{b.Origem}"));
        Assert.Contains(r.Detalhamentos, d => d.Nome == "SAPATA S1" && d.Repeticoes == 41);
        Assert.Equal(20, r.Tabelas.Count);
        Assert.True(r.Tabelas.All(t => t.Detalhamentos.Count > 0));
        // estribo ø20 com ganchos de 90° (tipo 1): 30 cm = ponta 10φ + pino 8φ/2 + φ
        var n16 = r.Barras.First(b => b.Origem == 21209);
        Assert.Equal(FormatoSegmento.Gancho90, n16.Dados.Segmentos[0].Formato);
        Assert.Equal(20.0, n16.Dados.Bitola);
        Assert.Equal(30, Math.Sqrt(Math.Pow(n16.Pontos[1].X - n16.Pontos[0].X, 2) + Math.Pow(n16.Pontos[1].Y - n16.Pontos[0].Y, 2)), 1);
    }
}
