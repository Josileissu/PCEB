using System;
using System.IO;
using System.Linq;
using System.Text;
using DEB.Core.Configuracao;
using DEB.Core.Templates;
using DEB.Core.Templates.PilarPreMoldado;
using Xunit;

namespace DEB.Core.Tests;

/// <summary>
/// Ferramenta de layout (checagem de 2026-10-30): com ARM_PREVIA=&lt;pasta&gt;, grava o resultado do template do pilar em JSON
/// (barras, contornos, textos, círculos, cotas, faixas, tabelas) para `tools/previa_template.py` desenhar uma prévia sem o CAD.
/// Sem a variável, não faz nada.
/// </summary>
[Collection("Pilar")]
public class PreviaTemplateTests
{
    [Fact]
    public void GravarPreviaDoPilar()
    {
        var pasta = Environment.GetEnvironmentVariable("ARM_PREVIA");
        if (string.IsNullOrEmpty(pasta)) return;
        var t = new TemplatePilarPreMoldado();
        Gravar(pasta, "previa_pilar.json", t.Gerar(new ValoresTemplate(t.Parametros), new ConfiguracaoArmadura()));
    }

    [Fact]
    public void GravarPreviaDoBloco()
    {
        var pasta = Environment.GetEnvironmentVariable("ARM_PREVIA");
        if (string.IsNullOrEmpty(pasta)) return;
        var t = new DEB.Core.Templates.Blocos.TemplateBlocoEstacas();
        var v = new ValoresTemplate(t.Parametros);
        v["Pilares"] = "P1, P2, P3, P5, P6, P7, P8, P9, P11, P14, P16, P17, P18, P19, P20, P21, P26, P28 e P30";
        Gravar(pasta, "previa_bloco.json", t.Gerar(v, new ConfiguracaoArmadura()));
    }

    private static void Gravar(string pasta, string arquivo, ResultadoTemplate r)
    {
        string P((double X, double Y) p) => FormattableString.Invariant($"[{p.X:0.###},{p.Y:0.###}]");
        string Ps(System.Collections.Generic.IEnumerable<(double X, double Y)> ps) => "[" + string.Join(",", ps.Select(P)) + "]";
        string S(string s) => "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ") + "\"";
        var sb = new StringBuilder("{");
        sb.Append("\"barras\":[" + string.Join(",", r.Barras.Select(b => Ps(b.Vertices))) + "],");
        sb.Append("\"contornos\":[" + string.Join(",", r.Contornos.Select(c => Ps(c.Fechado ? c.Vertices.Concat(c.Vertices.Take(1)) : c.Vertices))) + "],");
        sb.Append("\"layers\":[" + string.Join(",", r.Contornos.Select(c => S(c.Layer))) + "],");
        sb.Append("\"textos\":[" + string.Join(",", r.Textos.Select(x => FormattableString.Invariant(
            $"{{\"t\":{S(x.Texto)},\"x\":{x.X:0.###},\"y\":{x.Y:0.###},\"h\":{x.Altura:0.###},\"a\":{x.Angulo:0.####},\"al\":{x.Alinhamento}}}"))) + "],");
        sb.Append("\"circulos\":[" + string.Join(",", r.Circulos.Select(c => FormattableString.Invariant($"[{c.X:0.###},{c.Y:0.###},{c.Raio:0.###}]"))) + "],");
        sb.Append("\"cotas\":[" + string.Join(",", r.Cotas.Select(c => "[" + P(c.P1) + "," + P(c.P2) + "," + P(c.Posicao) + "]")) + "],");
        sb.Append("\"faixas\":[" + string.Join(",", r.Faixas.Select(f => "[" + P(f.P1) + "," + P(f.P2) + "," + P(f.Posicao) + "," + Ps(f.Referencias) + "]")) + "],");
        sb.Append("\"tabelas\":[" + string.Join(",", r.Tabelas.Select(x => FormattableString.Invariant($"[{x.X:0.###},{x.Y:0.###},{x.Larguras.Sum() * x.Escala / 10:0.###},{(x.Linhas.Count + 1) * 0.8 * x.Escala:0.###}]"))) + "],");
        sb.Append("\"ra\":" + (r.PosicaoRA is { } ra ? P(ra) : "null"));
        sb.Append("}");
        Directory.CreateDirectory(pasta);
        File.WriteAllText(Path.Combine(pasta, arquivo), sb.ToString(), new UTF8Encoding(false));
    }
}
