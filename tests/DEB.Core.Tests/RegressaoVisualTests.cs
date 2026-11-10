using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using DEB.Core.Configuracao;
using DEB.Core.Motor.Entrada;
using DEB.Core.Motor.Fundacoes;
using DEB.Core.Templates;
using DEB.Core.Templates.BaseReservatorio;
using DEB.Core.Templates.PilarPreMoldado;
using DEB.Core.Templates.Reservatorio;
using DEB.Core.Templates.VigaPreMoldada;
using Xunit;

namespace DEB.Core.Tests;

/// <summary>
/// Regressão visual dos templates (2026-11-10): cada caso gera o desenho e compara, entidade por entidade, com a referência
/// gravada em <c>tests/DEB.Core.Tests/Referencias/&lt;caso&gt;.txt</c> (uma linha por contorno, barra, texto, cota, faixa…, com as
/// coordenadas ao centésimo). Qualquer mudança no que o template desenha — de propósito ou não — aparece aqui, sem abrir o CAD.
/// A referência também lista os TEXTOS SOBREPOSTOS (caixas aproximadas): uma sobreposição nova aparece como diferença.
///
/// Mudou de propósito (padrão novo do usuário, correção)? Confira as diferenças que o teste mostra e regrave:
///   $env:ARM_ATUALIZAR_REFERENCIAS = "1"; dotnet test tests\DEB.Core.Tests --filter RegressaoVisual
/// e inclua os .txt alterados no commit (o diff deles é o registro do que mudou no desenho).
/// O desenho gerado de quem falhou fica em %TEMP%\deb_regressao\&lt;caso&gt;.txt.
/// </summary>
[Collection("Pilar")]   // os templates usam os catálogos estáticos
public class RegressaoVisualTests
{
    private static ResultadoTemplate Gerar(ITemplateArmadura t, params (string, string)[] valores)
    {
        var v = new ValoresTemplate(t.Parametros);
        foreach (var (k, val) in valores) v[k] = val;
        return t.Gerar(v, new ConfiguracaoArmadura());
    }

    public static IEnumerable<object[]> Casos() => new[]
    {
        "pilar_padrao", "viga_padrao_dentes", "viga_reta_protendida", "viga_aba_topo_dois_lados", "viga_aba_elevada_um_lado", "viga_L_aba_no_fundo",
        "viga_consolo_lateral", "viga_aba_e_ressalto", "bloco_motor_padrao", "bloco_motor_b28", "base_reservatorio_padrao", "reservatorio_padrao",
    }.Select(c => new object[] { c });

    private static ResultadoTemplate Caso(string nome)
    {
        var viga = new TemplateVigaPreMoldada();
        switch (nome)
        {
            case "pilar_padrao": return Gerar(new TemplatePilarPreMoldado());
            case "viga_padrao_dentes": return Gerar(viga, ("Nome", "V144"), ("L", "308"));
            case "viga_reta_protendida":
                return Gerar(viga, ("Nome", "V149"), ("Quantidade", "17"), ("B", "40"), ("H", "95"), ("L", "1150"), ("ExtE", TemplateVigaPreMoldada.Reta),
                    ("ExtD", TemplateVigaPreMoldada.Reta), ("FurosE", "2"), ("FurosD", "2"), ("FuroDiam", "40"), ("FuroDistE", "20"), ("FuroDistD", "20"),
                    ("Protendida", "Sim"), ("Longitudinais", "Superior|2|12.5|1|0|0||;Pele|3|10|1|0|0||"),
                    ("Cordoalhas", "1|5|15.2|0|0|20.5;1|2|15.2|250|250|20.5;2|2|15.2|0|0|20.5;2|2|15.2|120|120|20.5;18|2|12.7|0|0|10"));
            case "viga_aba_topo_dois_lados":
                return Gerar(viga, ("Nome", "V121"), ("B", "30"), ("H", "70"), ("L", "457"), ("Aba", TemplateVigaPreMoldada.AbaDois), ("AbaLargura", "15"),
                    ("AbaAltura", "15"), ("AbaAlturaPonta", "12"), ("AbaBase", "55"));
            case "viga_aba_elevada_um_lado":
                return Gerar(viga, ("Nome", "V4"), ("B", "19"), ("H", "70"), ("L", "295.5"), ("Aba", TemplateVigaPreMoldada.AbaEsq), ("AbaLargura", "20"),
                    ("AbaAltura", "15"), ("AbaAlturaPonta", "12"), ("AbaBase", "37.5"));
            case "viga_L_aba_no_fundo":
                return Gerar(viga, ("Nome", "V129"), ("B", "40"), ("H", "50"), ("L", "600"), ("Aba", TemplateVigaPreMoldada.AbaDir), ("AbaLargura", "15"), ("AbaAltura", "20"));
            case "viga_consolo_lateral":
                return Gerar(viga, ("Nome", "V12"), ("B", "19"), ("H", "70"), ("L", "500"), ("Aba", TemplateVigaPreMoldada.AbaDir), ("AbaLargura", "25"),
                    ("AbaAltura", "23.5"), ("AbaBase", "7.5"), ("AbaIni", "142.5"), ("AbaFim", "161.5"));
            case "viga_aba_e_ressalto": { var t = new TemplateVigaPreMoldada(); var v = new ValoresTemplate(t.Parametros); VigaRessaltoTests.V119(v); return t.Gerar(v, new ConfiguracaoArmadura()); }
            case "bloco_motor_padrao": return Gerar(new TemplateBlocoMotor());
            case "bloco_motor_b28": return new TemplateBlocoMotor().Gerar(ConversorBloco.Valores(ImportadorBlocos.ExemploB28()), new ConfiguracaoArmadura());
            case "base_reservatorio_padrao": return Gerar(new TemplateBaseReservatorio());
            case "reservatorio_padrao": return Gerar(new TemplateReservatorio());
            default: throw new ArgumentException(nome);
        }
    }

    [Theory]
    [MemberData(nameof(Casos))]
    public void DesenhoIgualAReferencia(string caso)
    {
        var atual = Canonico(Caso(caso));
        string arq = Path.Combine(PastaReferencias(), caso + ".txt");
        bool atualizar = Environment.GetEnvironmentVariable("ARM_ATUALIZAR_REFERENCIAS") == "1";
        if (atualizar || !File.Exists(arq))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(arq));
            File.WriteAllLines(arq, atual, new UTF8Encoding(false));
            Assert.True(atualizar, $"Referência de \"{caso}\" não existia e foi criada em {arq}: confira e rode de novo.");
            return;
        }
        var esperado = File.ReadAllLines(arq, Encoding.UTF8);
        if (esperado.SequenceEqual(atual)) return;

        string pasta = Path.Combine(Path.GetTempPath(), "deb_regressao");
        Directory.CreateDirectory(pasta);
        File.WriteAllLines(Path.Combine(pasta, caso + ".txt"), atual, new UTF8Encoding(false));
        var so1 = esperado.Except(atual).Take(6).ToList();
        var so2 = atual.Except(esperado).Take(6).ToList();
        Assert.Fail($"O desenho de \"{caso}\" mudou ({esperado.Length} → {atual.Count} linhas).\n" +
                    "Só na referência:\n  " + string.Join("\n  ", so1.Select(Curto)) + "\nSó no desenho de agora:\n  " + string.Join("\n  ", so2.Select(Curto)) +
                    $"\nDesenho de agora: {Path.Combine(pasta, caso + ".txt")}\n" +
                    "Se a mudança é de propósito: ARM_ATUALIZAR_REFERENCIAS=1 e rode de novo (ver o comentário da classe).");
    }

    private static string Curto(string s) => s.Length <= 170 ? s : s.Substring(0, 170) + "…";

    /// <summary>A pasta das referências no código-fonte (sobe da pasta do teste até achar o PCEB.sln).</summary>
    private static string PastaReferencias()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "PCEB.sln"))) d = d.Parent;
        Assert.True(d != null, "PCEB.sln não encontrado acima de " + AppContext.BaseDirectory);
        return Path.Combine(d.FullName, "tests", "DEB.Core.Tests", "Referencias");
    }

    // ------------------------------------------------------------------ forma canônica

    private static string N(double x)
    {
        string s = Math.Round(x, 2).ToString("0.##", CultureInfo.InvariantCulture);
        return s == "-0" ? "0" : s;
    }
    private static string P((double X, double Y) p) => N(p.X) + "," + N(p.Y);
    private static string Ps(IEnumerable<(double X, double Y)> ps) => string.Join(" ", ps.Select(P));
    private static string T(string s) => (s ?? "").Replace("\r", "").Replace("\n", "⏎");

    /// <summary>Uma linha por entidade, na ordem em que o template gera (a ordem também é parte do resultado).</summary>
    internal static List<string> Canonico(ResultadoTemplate r)
    {
        var l = new List<string>
        {
            $"DETALHAMENTO {T(r.Detalhamento)} escala {N(r.Escala)} RA {(r.PosicaoRA is { } ra ? P(ra) : "-")} " +
            $"detalhes [{string.Join("; ", r.DetalhesBiblioteca)}] coluna {(r.ColunaDetalhes is { } cd ? P(cd) : "-")}",
        };
        foreach (var a in r.Avisos) l.Add("AVISO " + T(a));
        foreach (var c in r.Contornos)
            l.Add($"CONTORNO {c.Layer} {(c.Fechado ? "fechado" : "aberto")} {Ps(c.Vertices)}" + (c.Bulges.Any(b => b != 0) ? " bulges " + string.Join(" ", c.Bulges.Select(N)) : ""));
        foreach (var c in r.Circulos) l.Add($"CIRCULO {c.Layer} {N(c.X)},{N(c.Y)} r {N(c.Raio)}");
        foreach (var h in r.Hachuras) l.Add($"HACHURA {h.Layer} {h.Padrao} {N(h.EscalaPadrao)} {Ps(h.Vertices)}");
        foreach (var b in r.Barras)
            l.Add($"BARRA {b.Nome} ø{N(b.Bitola)} {b.Aco} q{b.Quantidade} x{b.Repeticoes} e{N(b.Espacamento)} esc {N(b.Escala)}" +
                  (b.Estribo ? " estribo" : "") + (b.Fechada ? " fechada" : "") + (b.Corrido ? " corrido" : "") + (b.Tela.Length > 0 ? " tela " + b.Tela : "") +
                  (b.Layer.Length > 0 ? " layer " + b.Layer : "") + $" [{Ps(b.Vertices)}]" +
                  (b.Bulges.Any(x => x != 0) ? " bulges " + string.Join(" ", b.Bulges.Select(N)) : "") +
                  " fmt " + string.Join("", b.Formatos.Select(f => ((int)f).ToString(CultureInfo.InvariantCulture))) +
                  (b.Sobrepostos.Count > 0 ? " sob " + string.Join(",", b.Sobrepostos.OrderBy(x => x)) : "") +
                  (b.Cotados.Count > 0 ? " cot " + string.Join(",", b.Cotados.OrderBy(x => x)) : "") +
                  (b.ComprimentosVar.Count > 0 ? " var " + string.Join(" ", b.ComprimentosVar.Select(N)) : "") +
                  (b.PosicaoDescricao is { } pd ? $" desc {P(pd)} rot {N(b.RotacaoDescricao)}" : ""));
        foreach (var f in r.Faixas)
            l.Add($"FAIXA {f.Barra} {f.Tipo} {P(f.P1)} {P(f.P2)} pos {P(f.Posicao)} e{N(f.Espacamento)} esc {N(f.Escala)} x{f.Repeticoes}" +
                  (f.MostrarDiametro ? "" : " semdiam") + (f.MostrarComprimento ? "" : " semcomp") + (f.Patamar ? " patamar" : "") +
                  (f.Referencias.Count > 0 ? $" refs [{Ps(f.Referencias)}]" : ""));
        foreach (var t in r.Textos) l.Add($"TEXTO {t.Layer} {N(t.X)},{N(t.Y)} h {N(t.Altura)} ang {N(t.Angulo)} al {t.Alinhamento} «{T(t.Texto)}»");
        foreach (var c in r.Cotas) l.Add($"COTA {P(c.P1)} {P(c.P2)} pos {P(c.Posicao)} ang {N(c.Angulo)} esc {N(c.Escala)}" + (c.Texto.Length > 0 ? $" «{T(c.Texto)}»" : ""));
        foreach (var t in r.Tabelas)
        {
            l.Add($"TABELA {N(t.X)},{N(t.Y)} esc {N(t.Escala)} «{T(t.Titulo)}» larguras {string.Join(" ", t.Larguras.Select(N))} alinh {string.Join("", t.Alinhamentos)}");
            foreach (var linha in t.Linhas) l.Add("  | " + string.Join(" | ", linha.Select(T)));
        }
        var sob = Sobreposicoes(r);
        l.Add($"TEXTOS SOBREPOSTOS {sob.Count}");
        l.AddRange(sob.Select(s => "  " + s));
        return l;
    }

    /// <summary>
    /// Pares de textos horizontais cujas caixas (largura ≈ 0,62 × altura por caractere) se sobrepõem em mais de 25% da menor.
    /// É uma aproximação — serve para comparar com a referência (sobreposição NOVA), não como medida exata.
    /// </summary>
    internal static List<string> Sobreposicoes(ResultadoTemplate r)
    {
        var cx = r.Textos.Where(t => Math.Abs(t.Angulo) < 1e-6 && t.Texto.Trim().Length > 0 && t.Altura > 0).Select(t =>
        {
            double w = 0.62 * t.Altura * t.Texto.Length, x0 = t.Alinhamento == 1 ? t.X - w / 2 : t.Alinhamento == 2 ? t.X - w : t.X;
            return (t, x0, x1: x0 + w, y0: t.Y, y1: t.Y + t.Altura);
        }).ToList();
        var res = new List<string>();
        for (int i = 0; i < cx.Count; i++)
            for (int j = i + 1; j < cx.Count; j++)
            {
                var a = cx[i]; var b = cx[j];
                double ox = Math.Min(a.x1, b.x1) - Math.Max(a.x0, b.x0), oy = Math.Min(a.y1, b.y1) - Math.Max(a.y0, b.y0);
                if (ox <= 0 || oy <= 0) continue;
                double menor = Math.Min((a.x1 - a.x0) * (a.y1 - a.y0), (b.x1 - b.x0) * (b.y1 - b.y0));
                if (ox * oy > 0.25 * menor) res.Add($"«{T(a.t.Texto)}» × «{T(b.t.Texto)}» em {N(Math.Max(a.x0, b.x0))},{N(Math.Max(a.y0, b.y0))}");
            }
        return res;
    }

    [Fact]
    public void DetectorDeSobreposicaoAchaTextosUmSobreOOutro()
    {
        var r = new ResultadoTemplate();
        r.Textos.Add(new TextoTemplate { Texto = "ARMADURA INFERIOR", X = 0, Y = 0, Altura = 10 });
        r.Textos.Add(new TextoTemplate { Texto = "TIRANTE", X = 20, Y = 4, Altura = 10 });          // em cima do primeiro
        r.Textos.Add(new TextoTemplate { Texto = "LONGE", X = 500, Y = 0, Altura = 10 });
        r.Textos.Add(new TextoTemplate { Texto = "CENTRADO", X = 520, Y = 30, Altura = 10, Alinhamento = 1 });   // acima do "LONGE": não encosta
        var s = Sobreposicoes(r);
        Assert.Single(s);
        Assert.Contains("ARMADURA INFERIOR", s[0]);
        Assert.Contains("TIRANTE", s[0]);
    }
}
