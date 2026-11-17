using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace DEB.Core.Documentacao;

public enum AcaoTamanhoPc3
{
    /// <summary>Não havia: criado.</summary>
    Criar,
    /// <summary>Mesmo nome com outro tamanho: medidas corrigidas.</summary>
    Ajustar,
    /// <summary>Havia uma folha do mesmo tamanho com outro nome: passa a ter o nome da lista (não duplica).</summary>
    Renomear,
    /// <summary>Mesmo nome e tamanho: nada a fazer.</summary>
    Manter,
}

/// <summary>Folha personalizada de um .pmp (udm/media: size + description ligados pelo nome da descrição).</summary>
public sealed class TamanhoPc3
{
    public string NomeLocal { get; set; } = "";
    public string Canonico { get; set; } = "";
    public double Largura { get; set; }
    public double Altura { get; set; }
    public double Maior => Math.Max(Largura, Altura);
    public double Menor => Math.Min(Largura, Altura);
    internal NoPia Size { get; set; }
    internal NoPia Descricao { get; set; }
}

public sealed class PassoTamanhoPc3
{
    public FormatoFolha Formato { get; set; }
    public AcaoTamanhoPc3 Acao { get; set; }
    /// <summary>Como estava (nome e medidas), para o resumo.</summary>
    public string Antes { get; set; } = "";
    public string Texto => Acao switch
    {
        AcaoTamanhoPc3.Criar => $"criada: {Formato.Nome} ({TamanhosPc3.Mm(Formato.Maior)} × {TamanhosPc3.Mm(Formato.Menor)} mm)",
        AcaoTamanhoPc3.Ajustar => $"ajustada: {Formato.Nome} ({TamanhosPc3.Mm(Formato.Maior)} × {TamanhosPc3.Mm(Formato.Menor)} mm; era {Antes})",
        AcaoTamanhoPc3.Renomear => $"reaproveitada: {Formato.Nome} (era \"{Antes}\", mesmo tamanho)",
        _ => $"mantida: {Formato.Nome}",
    };
}

/// <summary>
/// Folhas personalizadas de um .pc3 da Autodesk (ex.: "AutoCAD PDF (High Quality Print).pc3"), gravadas no .pmp ligado a ele
/// (pedido do usuário de 17/11/2026: o botão "Criar folhas na impressora" cria de verdade, sem passo a passo, e concilia com o
/// que já existe). Estrutura levantada nos .pmp do DWG TrueView 2022 (folhas criadas pelo próprio programa) e conferida no
/// console dele (accoreconsole: PLOT → "?" lista as folhas; plotar com a folha nova deu o tamanho certo):
/// <code>
/// udm{ calibration{ _x=1.0 _y=1.0 } media{
///   size{ N{ caps_type=2 name="UserDefinedMetric (L x AMM) localized_name="&lt;nome&gt; media_description_name="&lt;descrição&gt;
///            media_group=15 landscape_mode=TRUE } }
///   description{ N{ caps_type=2 name="&lt;descrição&gt; media_bounds_urx=L media_bounds_ury=A printable_bounds_llx=0.0 …lly=0.0
///                   …urx=L …ury=A printable_area=L*A dimensional=TRUE } } } }
/// </code>
/// com descrição "UserDefinedMetric Landscape L.LLW x A.AAH - (0, 0) x (l, a) =área MM" (l, a e área truncados). A folha é
/// gravada deitada (L = maior), como o assistente da Autodesk faz; o lote calcula o giro pelo tamanho real do papel.
/// O .pc3 sem .pmp tem <c>meta{ user_defined_model_pathname="</c> vazio: o .pmp novo leva o meta do .pc3 (sem os config_*)
/// e o caminho dele é gravado nos dois.
/// </summary>
public static class TamanhosPc3
{
    /// <summary>Diferença máxima (mm) para considerar duas folhas do mesmo tamanho (em qualquer orientação).</summary>
    public const double Tolerancia = 1.0;

    public static string Mm(double v) => Fmt.Virgula(v, "0.##");

    /// <summary>Número como o .pmp grava (ponto decimal; inteiro com ".0"; sem o código opcional entre parênteses).</summary>
    public static string Num(double v)
    {
        if (Math.Abs(v - Math.Round(v)) < 1e-9) return Math.Round(v).ToString("0", CultureInfo.InvariantCulture) + ".0";
        return v.ToString("R", CultureInfo.InvariantCulture);
    }

    /// <summary>Lê um número do .pmp (ignora o código entre parênteses).</summary>
    public static double LerNum(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return 0;
        int p = s.IndexOf(' ');
        if (p > 0) s = s.Substring(0, p);
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;
    }

    public static string Canonico(double l, double a) => FormattableString.Invariant($"UserDefinedMetric ({l:0.00} x {a:0.00}MM)");

    public static string Descricao(double l, double a) =>
        FormattableString.Invariant($"UserDefinedMetric Landscape {l:0.00}W x {a:0.00}H - (0, 0) x ({Math.Floor(l):0}, {Math.Floor(a):0}) ={Math.Floor(l * a):0} MM");

    public static bool MesmoTamanho(double maior1, double menor1, double maior2, double menor2, double tol = Tolerancia) =>
        Math.Abs(maior1 - maior2) <= tol && Math.Abs(menor1 - menor2) <= tol;

    private static NoPia Midia(NoPia pmp) => pmp?["udm"]?["media"];

    /// <summary>Folhas personalizadas do .pmp (sem medidas = 0 quando a descrição não existe).</summary>
    public static List<TamanhoPc3> Listar(NoPia pmp)
    {
        var res = new List<TamanhoPc3>();
        var m = Midia(pmp);
        if (m?["size"] == null) return res;
        var descs = m["description"]?.Filhos ?? new List<NoPia>();
        foreach (var s in m["size"].Filhos.Where(f => f.Bloco))
        {
            var nomeDesc = s.LerTexto("media_description_name");
            var d = descs.FirstOrDefault(x => x.Bloco && x.LerTexto("name") == nomeDesc);
            res.Add(new TamanhoPc3
            {
                NomeLocal = (s.LerTexto("localized_name") ?? "").Trim(), Canonico = s.LerTexto("name") ?? "",
                Largura = LerNum(d?.Ler("media_bounds_urx")), Altura = LerNum(d?.Ler("media_bounds_ury")), Size = s, Descricao = d,
            });
        }
        return res;
    }

    /// <summary>
    /// Concilia as folhas do .pmp com a lista de formatos (altera <paramref name="pmp"/>): mesmo nome e tamanho = manter; mesmo
    /// nome, outro tamanho = ajustar; outro nome com o mesmo tamanho (±<see cref="Tolerancia"/> mm, qualquer orientação) =
    /// reaproveitar com o nome da lista; senão criar. Nunca duplica e não mexe nas folhas que não casam com a lista.
    /// </summary>
    public static List<PassoTamanhoPc3> Conciliar(NoPia pmp, IEnumerable<FormatoFolha> formatos, double tol = Tolerancia)
    {
        var udm = pmp.BlocoOuNovo("udm");
        if (udm["calibration"] == null)
        {
            var cal = new NoPia("calibration");
            cal.Filhos.Add(new NoPia("_x", "1.0")); cal.Filhos.Add(new NoPia("_y", "1.0"));
            udm.Filhos.Insert(0, cal);
        }
        var media = udm.BlocoOuNovo("media");
        var sizes = media.BlocoOuNovo("size");
        var descs = media.BlocoOuNovo("description");
        var existentes = Listar(pmp);
        var usados = new HashSet<TamanhoPc3>();
        var passos = new List<PassoTamanhoPc3>();
        foreach (var f in (formatos ?? Enumerable.Empty<FormatoFolha>()).Where(f => f != null && f.Maior > 0 && !string.IsNullOrWhiteSpace(f.Nome)))
        {
            double l = f.Maior, a = f.Menor;
            var porNome = existentes.FirstOrDefault(t => !usados.Contains(t) && string.Equals(t.NomeLocal, f.Nome.Trim(), StringComparison.OrdinalIgnoreCase));
            if (porNome != null)
            {
                usados.Add(porNome);
                if (MesmoTamanho(porNome.Maior, porNome.Menor, l, a, tol)) { passos.Add(new PassoTamanhoPc3 { Formato = f, Acao = AcaoTamanhoPc3.Manter }); continue; }
                var antes = $"{Mm(porNome.Maior)} × {Mm(porNome.Menor)} mm";
                Medidas(porNome, l, a, descs);
                passos.Add(new PassoTamanhoPc3 { Formato = f, Acao = AcaoTamanhoPc3.Ajustar, Antes = antes });
                continue;
            }
            var porTamanho = existentes.FirstOrDefault(t => !usados.Contains(t) && t.Maior > 0 && MesmoTamanho(t.Maior, t.Menor, l, a, tol)
                                                            && !NomeDeOutroFormato(t, formatos, f));
            if (porTamanho != null)
            {
                usados.Add(porTamanho);
                var antes = porTamanho.NomeLocal;
                porTamanho.Size.DefinirTexto("localized_name", f.Nome.Trim());
                porTamanho.NomeLocal = f.Nome.Trim();
                passos.Add(new PassoTamanhoPc3 { Formato = f, Acao = AcaoTamanhoPc3.Renomear, Antes = antes });
                continue;
            }
            var novo = new TamanhoPc3 { NomeLocal = f.Nome.Trim() };
            novo.Size = new NoPia(sizes.Filhos.Count.ToString(CultureInfo.InvariantCulture));
            novo.Size.Definir("caps_type", "2");
            novo.Size.DefinirTexto("name", "");
            novo.Size.DefinirTexto("localized_name", novo.NomeLocal);
            novo.Size.DefinirTexto("media_description_name", "");
            novo.Size.Definir("media_group", "15");
            novo.Size.Definir("landscape_mode", "TRUE");
            sizes.Filhos.Add(novo.Size);
            Medidas(novo, l, a, descs);
            usados.Add(novo);
            existentes.Add(novo);
            passos.Add(new PassoTamanhoPc3 { Formato = f, Acao = AcaoTamanhoPc3.Criar });
        }
        return passos;
    }

    /// <summary>A folha existente já tem o nome de OUTRO formato da lista (não pode ser renomeada para este).</summary>
    private static bool NomeDeOutroFormato(TamanhoPc3 t, IEnumerable<FormatoFolha> formatos, FormatoFolha atual) =>
        formatos.Any(o => o != atual && o != null && string.Equals(o.Nome?.Trim(), t.NomeLocal, StringComparison.OrdinalIgnoreCase));

    /// <summary>Grava medidas, nome canônico e descrição de uma folha (cria a descrição se faltar).</summary>
    private static void Medidas(TamanhoPc3 t, double l, double a, NoPia descs)
    {
        var desc = Descricao(l, a);
        t.Canonico = Canonico(l, a);
        t.Size.DefinirTexto("name", t.Canonico);
        t.Size.DefinirTexto("media_description_name", desc);
        if (t.Descricao == null)
        {
            t.Descricao = new NoPia(descs.Filhos.Count.ToString(CultureInfo.InvariantCulture));
            descs.Filhos.Add(t.Descricao);
        }
        var d = t.Descricao;
        d.Filhos.Clear();
        d.Definir("caps_type", "2");
        d.DefinirTexto("name", desc);
        d.Definir("media_bounds_urx", Num(l));
        d.Definir("media_bounds_ury", Num(a));
        d.Definir("printable_bounds_llx", "0.0");
        d.Definir("printable_bounds_lly", "0.0");
        d.Definir("printable_bounds_urx", Num(l));
        d.Definir("printable_bounds_ury", Num(a));
        d.Definir("printable_area", Num(l * a));
        d.Definir("dimensional", "TRUE");
        t.Largura = l; t.Altura = a;
    }

    /// <summary>Caminho do .pmp ligado ao .pc3 (vazio = nenhum).</summary>
    public static string PmpLigado(NoPia pc3) => (pc3?["meta"]?.LerTexto("user_defined_model_pathname") ?? "").Trim();

    /// <summary>Liga o .pc3 ao .pmp (grava o caminho no meta).</summary>
    public static void Ligar(NoPia pc3, string caminhoPmp) => pc3.BlocoOuNovo("meta").DefinirTexto("user_defined_model_pathname", caminhoPmp);

    /// <summary>.pmp novo para um .pc3 que não tinha: o meta do .pc3 (sem os config_*) com o caminho do .pmp, e o udm vazio.</summary>
    public static ArquivoPia NovoPmp(NoPia pc3, string caminhoPmp)
    {
        var raiz = new NoPia("");
        var meta = new NoPia("meta");
        foreach (var p in pc3?["meta"]?.Filhos ?? new List<NoPia>())
            if (!p.Bloco && !p.Chave.StartsWith("config_", StringComparison.Ordinal)) meta.Filhos.Add(new NoPia(p.Chave, p.Valor));
        meta.DefinirTexto("user_defined_model_pathname", caminhoPmp);
        raiz.Filhos.Add(meta);
        var udm = new NoPia("udm");
        var cal = new NoPia("calibration");
        cal.Filhos.Add(new NoPia("_x", "1.0")); cal.Filhos.Add(new NoPia("_y", "1.0"));
        udm.Filhos.Add(cal);
        var media = new NoPia("media");
        media.Filhos.Add(new NoPia("size")); media.Filhos.Add(new NoPia("description"));
        udm.Filhos.Add(media);
        raiz.Filhos.Add(udm);
        return new ArquivoPia { Cabecalho = ArquivoPia.CabecalhoPc3, Raiz = raiz, TerminaEmNulo = true };
    }

    /// <summary>Resumo para a janela: uma linha por formato.</summary>
    public static string Resumo(IEnumerable<PassoTamanhoPc3> passos, string impressora)
    {
        var l = passos.ToList();
        var sb = new StringBuilder();
        int mudou = l.Count(p => p.Acao != AcaoTamanhoPc3.Manter);
        sb.AppendLine(mudou == 0 ? $"\"{impressora}\" já tinha todas as folhas." : $"\"{impressora}\": {l.Count(p => p.Acao == AcaoTamanhoPc3.Criar)} criada(s), " +
                      $"{l.Count(p => p.Acao == AcaoTamanhoPc3.Ajustar)} ajustada(s), {l.Count(p => p.Acao == AcaoTamanhoPc3.Renomear)} reaproveitada(s), " +
                      $"{l.Count(p => p.Acao == AcaoTamanhoPc3.Manter)} mantida(s).");
        foreach (var p in l) sb.AppendLine("   " + p.Texto);
        return sb.ToString().TrimEnd();
    }
}
