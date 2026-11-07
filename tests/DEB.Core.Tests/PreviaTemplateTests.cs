using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DEB.Core.Configuracao;
using DEB.Core.Persistencia;
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
        v["Niveis"] = "P106|-11.43;P118|-11.43";
        Gravar(pasta, "previa_bloco.json", t.Gerar(v, new ConfiguracaoArmadura()));
        var v15 = new ValoresTemplate(t.Parametros);
        foreach (var (k, val) in new[] { ("Nome", "B15"), ("Arranjo", DEB.Core.Templates.Blocos.TemplateBlocoEstacas.Triangulo), ("EspX", "150"), ("DistFace", "40"),
                     ("DiamEstaca", "50"), ("PilarBx", "40"), ("PilarBy", "40"), ("tParede", "25"), ("hBase", "105"), ("hColar", "30"), ("Lemb", "65"),
                     ("PrincBitola", "12.5"), ("PrincEsp", "11") })
            v15[k] = val;
        Gravar(pasta, "previa_bloco_b15.json", t.Gerar(v15, new ConfiguracaoArmadura()));
    }

    /// <summary>Grava a prévia (formato lido por tools/previa_template.py). Usado também pelos testes do motor.</summary>
    internal static void Gravar(string pasta, string arquivo, ResultadoTemplate r)
    {
        ValorJson N(double d, int casas = 3) => ValorJson.De(Math.Round(d, casas));
        ValorJson L(params ValorJson[] itens) { var l = ValorJson.NovaLista(); foreach (var i in itens) l.Adicionar(i); return l; }
        ValorJson P((double X, double Y) p) => L(N(p.X), N(p.Y));
        ValorJson Ps(IEnumerable<(double X, double Y)> ps) => L(ps.Select(P).ToArray());
        ValorJson S(string s) => ValorJson.De((s ?? "").Replace("\n", " "));
        var o = ValorJson.NovoObjeto()
            .Definir("barras", L(r.Barras.Select(b => Ps(b.Vertices)).ToArray()))
            .Definir("contornos", L(r.Contornos.Select(c => Ps(c.Fechado ? c.Vertices.Concat(c.Vertices.Take(1)) : c.Vertices)).ToArray()))
            .Definir("layers", L(r.Contornos.Select(c => S(c.Layer)).ToArray()))
            .Definir("textos", L(r.Textos.Select(x => ValorJson.NovoObjeto().Definir("t", S(x.Texto)).Definir("x", N(x.X)).Definir("y", N(x.Y))
                .Definir("h", N(x.Altura)).Definir("a", N(x.Angulo, 4)).Definir("al", x.Alinhamento)).ToArray()))
            .Definir("circulos", L(r.Circulos.Select(c => L(N(c.X), N(c.Y), N(c.Raio))).ToArray()))
            .Definir("cotas", L(r.Cotas.Select(c => L(P(c.P1), P(c.P2), P(c.Posicao))).ToArray()))
            .Definir("faixas", L(r.Faixas.Select(f => L(P(f.P1), P(f.P2), P(f.Posicao), Ps(f.Referencias))).ToArray()))
            .Definir("tabelas", L(r.Tabelas.Select(x => L(N(x.X), N(x.Y), N(x.Larguras.Sum() * x.Escala / 10), N((x.Linhas.Count + 1) * 0.8 * x.Escala))).ToArray()))
            .Definir("ra", r.PosicaoRA is { } ra ? P(ra) : ValorJson.Nulo);
        Json.GravarArquivo(Path.Combine(pasta, arquivo), o);
    }
}
