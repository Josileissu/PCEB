using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DEB.Cad.Infra;
using DEB.Core.Documentacao;

namespace DEB.Cad.Pdf;

/// <summary>
/// "Criar folhas na impressora" com uma .pc3 (ex.: "AutoCAD PDF (High Quality Print).pc3"; pedido do usuário de 17/11/2026):
/// grava as folhas da lista de formatos no .pmp ligado ao .pc3, conciliando com o que já existe (regra e formato no núcleo:
/// <see cref="TamanhosPc3"/>). Antes de gravar, cópia de segurança "&lt;arquivo&gt;.bak_AAAAMMDD-HHMMSS" do .pmp (e do .pc3, se
/// ele for ligado a um .pmp novo). Depois, a lista de dispositivos do CAD é recarregada.
/// Vale para .pc3 no formato da Autodesk (AutoCAD; ZWCAD se usar o mesmo formato). O GstarCAD 2021 grava .pc3/.pmp num formato
/// binário próprio: aí o botão explica e não grava nada.
/// </summary>
internal static class FolhasPc3
{
    /// <summary>Plano já calculado (sem gravar): o .pc3, o .pmp (lido ou novo) e os passos.</summary>
    internal sealed class Plano
    {
        public string Pc3 { get; set; }
        public string Pmp { get; set; }
        public bool PmpNovo { get; set; }
        public bool Religar { get; set; }
        public ArquivoPia ArquivoPc3 { get; set; }
        public ArquivoPia ArquivoPmp { get; set; }
        public List<PassoTamanhoPc3> Passos { get; set; } = new List<PassoTamanhoPc3>();
        public int Mudancas => Passos.Count(p => p.Acao != AcaoTamanhoPc3.Manter) + (Religar ? 1 : 0);
    }

    /// <summary>Pasta dos .pc3 do CAD em uso (preferências ou PrinterConfigDir do perfil).</summary>
    public static string PastaPc3() =>
        PlotadorPdf.PastaDoCad(new[] { "PrinterConfigPath" }, "PrinterConfigDir");

    /// <summary>Pasta dos .pmp (preferências, PrinterDescDir do perfil ou "PMP Files" ao lado dos .pc3).</summary>
    public static string PastaPmp(string pastaPc3) =>
        PlotadorPdf.PastaDoCad(new[] { "PrinterDescPath" }, "PrinterDescDir") ?? Path.Combine(pastaPc3, "PMP Files");

    /// <summary>Calcula o que vai mudar (lê os arquivos, não grava). Erros conhecidos saem como <see cref="ErroLotePdfException"/>.</summary>
    public static Plano Planejar(string impressora, IEnumerable<FormatoFolha> formatos)
    {
        var pasta = PastaPc3() ?? throw new ErroLotePdfException(CausaErroPdf.ImpressoraInexistente, "a pasta das impressoras (.pc3) deste CAD não foi achada");
        var pc3 = Path.Combine(pasta, impressora.Trim());
        if (!File.Exists(pc3)) throw new ErroLotePdfException(CausaErroPdf.ImpressoraInexistente, $"o arquivo \"{impressora}\" não existe em {pasta}");
        var bytes = File.ReadAllBytes(pc3);
        if (!ArquivoPia.EhPia(bytes))
            throw new ErroLotePdfException(CausaErroPdf.DriverSemTamanhoPersonalizado,
                $"o .pc3 \"{impressora}\" deste CAD não está no formato da Autodesk (o GstarCAD usa um formato próprio): o plugin não cria folhas nele; use a PDF24");
        var p = new Plano { Pc3 = pc3, ArquivoPc3 = LerPia(bytes, pc3) };
        var ligado = TamanhosPc3.PmpLigado(p.ArquivoPc3.Raiz);
        if (ligado.Length > 0 && File.Exists(ligado))
        {
            p.Pmp = ligado;
            p.ArquivoPmp = LerPia(File.ReadAllBytes(ligado), ligado);
        }
        else
        {
            // sem .pmp, ou ligado a um caminho que não existe neste PC (.pc3 copiado de outra máquina): .pmp novo, ligado ao .pc3
            p.Pmp = Path.Combine(PastaPmp(pasta), Path.GetFileNameWithoutExtension(pc3) + ".pmp");
            p.PmpNovo = !File.Exists(p.Pmp);
            p.ArquivoPmp = p.PmpNovo ? TamanhosPc3.NovoPmp(p.ArquivoPc3.Raiz, p.Pmp) : LerPia(File.ReadAllBytes(p.Pmp), p.Pmp);
            // .pmp novo no formato do .pc3 (AutoCAD 2025+: JSON)
            if (p.PmpNovo && p.ArquivoPc3.Json) p.ArquivoPmp.ParaJson();
            p.Religar = true;
        }
        p.Passos = TamanhosPc3.Conciliar(p.ArquivoPmp.Raiz, formatos);
        return p;
    }

    /// <summary>Lê um .pc3/.pmp da Autodesk (formato 2.0 compactado ou 3.0 JSON); formato desconhecido = erro com o nome do arquivo.</summary>
    private static ArquivoPia LerPia(byte[] bytes, string caminho)
    {
        try { return ArquivoPia.Ler(bytes); }
        catch (Exception ex) when (ex is InvalidDataException || ex is FormatException)
        {
            var linha1 = System.Text.Encoding.ASCII.GetString(bytes, 0, Math.Min(bytes.Length, 40)).Split('\r', '\n')[0];
            throw new ErroLotePdfException(CausaErroPdf.DriverSemTamanhoPersonalizado,
                $"o arquivo \"{Path.GetFileName(caminho)}\" está num formato que o plugin não sabe gravar (\"{linha1}\"): crie as folhas pelo " +
                "Plotter Manager do CAD (Propriedades da impressora → Tamanhos de papel personalizados)", ex);
        }
    }

    /// <summary>Grava o plano (com cópias de segurança) e recarrega a lista de dispositivos. Devolve o resumo.</summary>
    public static string Aplicar(Plano p)
    {
        var carimbo = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        if (File.Exists(p.Pmp)) File.Copy(p.Pmp, p.Pmp + ".bak_" + carimbo, true);
        Directory.CreateDirectory(Path.GetDirectoryName(p.Pmp));
        Gravar(p.Pmp, p.ArquivoPmp.Gravar());
        if (p.Religar)
        {
            File.Copy(p.Pc3, p.Pc3 + ".bak_" + carimbo, true);
            TamanhosPc3.Ligar(p.ArquivoPc3.Raiz, p.Pmp);
            Gravar(p.Pc3, p.ArquivoPc3.Gravar());
        }
        Log.Info($"ARMPDFLOTE: folhas gravadas em {p.Pmp}" + (p.Religar ? $" (ligado em {p.Pc3})" : "") + " — " +
                 string.Join("; ", p.Passos.Select(x => x.Texto)));
        Recarregar();
        return TamanhosPc3.Resumo(p.Passos, Path.GetFileName(p.Pc3)) +
               (p.PmpNovo ? $"\n\n.pmp novo: {p.Pmp}" : "") + (File.Exists(p.Pmp + ".bak_" + carimbo) ? $"\nCópia do anterior: {Path.GetFileName(p.Pmp)}.bak_{carimbo}" : "");
    }

    /// <summary>Grava por arquivo temporário + troca (não deixa o .pmp pela metade se algo falhar).</summary>
    private static void Gravar(string caminho, byte[] dados)
    {
        var tmp = caminho + ".tmp";
        File.WriteAllBytes(tmp, dados);
        if (File.Exists(caminho)) File.Replace(tmp, caminho, null);
        else File.Move(tmp, caminho);
    }

    /// <summary>Recarrega as configurações de plotagem (o CAD relê .pc3/.pmp) para a janela enxergar as folhas sem reabrir.</summary>
    public static void Recarregar()
    {
        try { Plt.PlotConfigManager.RefreshList(Plt.RefreshCode.All); }
        catch (Exception ex) { Log.Erro("ARMPDFLOTE: recarregar a lista de impressoras", ex); }
    }
}
