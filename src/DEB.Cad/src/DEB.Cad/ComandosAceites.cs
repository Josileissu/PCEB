using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using DEB.Cad.Infra;
using DEB.Cad.Paleta;
using DEB.Core.Documentacao;
using DEB.Core.Normas.Verificacao;

[assembly: Rt.CommandClass(typeof(DEB.Cad.ComandosAceites))]

namespace DEB.Cad;

/// <summary>
/// ARMACEITES (D37): tela do aceite das pendências "Conferir" dos relatórios de verificação. Lê os relatórios da pasta da obra
/// (a do desenho; sem nome: Documentos\PCEB), grava os aceites em PCEB_aceites.json e regrava os relatórios e o índice da obra.
/// Não mexe no desenho.
/// </summary>
public sealed class ComandosAceites
{
    /// <summary>Pasta da obra do desenho: a mesma em que o ARMTEMPLATE grava os relatórios.</summary>
    internal static string PastaObra(Database db)
    {
        string dwg = db?.Filename ?? "";
        return dwg.EndsWith(".dwg", StringComparison.OrdinalIgnoreCase) && File.Exists(dwg)
            ? Path.GetDirectoryName(dwg)
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "PCEB");
    }

    [Rt.CommandMethod("ARMACEITES", Rt.CommandFlags.Session)]
    public void Aceites()
    {
        var ed = CadApp.DocumentManager.MdiActiveDocument?.Editor;
        if (Demo.Expirada) { ed?.WriteMessage("\n" + Demo.Mensagem + "\n"); return; }
        try
        {
            using var f = new FormAceites(PastaObra(CadApp.DocumentManager.MdiActiveDocument?.Database));
            if (CadApp.ShowModalDialog(f) != DialogResult.OK) return;
            var decisoes = f.Decisoes();
            string indice = RelatoriosObra.Aplicar(f.PastaObra, decisoes, f.Engenheiro);
            ed?.WriteMessage($"\nARMACEITES: {decisoes.Count(d => d.Aceitar)} de {decisoes.Count} pendência(s) aceita(s); relatórios regravados. Índice: {indice}\n");
        }
        catch (Exception ex)
        {
            // barreira do comando: nada derruba o CAD
            Log.Erro("ARMACEITES", ex);
            try { ed?.WriteMessage($"\nARMACEITES: {ex.Message} (detalhes em {Log.ArquivoHoje})\n"); } catch { }
        }
    }

    /// <summary>
    /// ARMTESTEACEITES: a tela do aceite sem cliques, numa obra de teste em ARM_ACEITES_PASTA (criada com dois grupos); marca uma
    /// pendência, grava e confere os arquivos. ARM_ACEITES_JANELA = PNG da janela. Relatório em logs\autoteste_aceites.txt.
    /// </summary>
    [Rt.CommandMethod("ARMTESTEACEITES", Rt.CommandFlags.Session)]
    public void Teste()
    {
        var rel = new StringBuilder();
        int falhas = 0;
        void C(bool ok, string o) { rel.AppendLine((ok ? "[OK]    " : "[FALHA] ") + o); if (!ok) falhas++; }
        try
        {
            string pasta = Environment.GetEnvironmentVariable("ARM_ACEITES_PASTA");
            if (string.IsNullOrEmpty(pasta)) pasta = Path.Combine(Path.GetTempPath(), "pceb_aceites_teste");
            if (Directory.Exists(Path.Combine(pasta, RelatorioHtml.Subpasta))) Directory.Delete(Path.Combine(pasta, RelatorioHtml.Subpasta), true);
            File.Delete(Path.Combine(pasta, AceitesObra.NomeArquivo));
            ResultadoVerificacao V(string cod, SituacaoVerificacao s, string valor) => new ResultadoVerificacao
            {
                Codigo = cod, Grupo = "Geral", Descricao = "Verificação de teste " + cod, Valor = valor, Limite = "≥ 2,0 cm", Situacao = s,
                Referencia = "NBR 6118, 18.3.2.2", Dica = s == SituacaoVerificacao.Ok ? "" : "Use menos estribos de bitola maior.",
            };
            var g1 = new RelatorioGrupo { Marca = "P1", Elemento = "Pilar pré-moldado", Origem = "autoteste" };
            g1.Verificacoes.Add(V("P1", SituacaoVerificacao.Ok, "40 cm")); g1.Verificacoes.Add(V("G2", SituacaoVerificacao.Conferir, "2,5 cm"));
            var g2 = new RelatorioGrupo { Marca = "V1", Elemento = "Viga pré-moldada", Origem = "autoteste" };
            g2.Verificacoes.Add(V("D6", SituacaoVerificacao.Conferir, "1,2 cm")); g2.Verificacoes.Add(V("C3", SituacaoVerificacao.NaoAtende, "2,5 cm"));
            RelatorioHtml.Gravar(g1, pasta); RelatorioHtml.Gravar(g2, pasta);

            using (var f = new FormAceites(pasta))
            {
                f.Show();
                C(f.Linhas == 2, $"janela lista as 2 pendências \"Conferir\" ({f.Linhas})");
                f.Engenheiro = "Eng. Teste";
                f.Marcar(1, true, "estribos de ramos duplos");
                System.Windows.Forms.Application.DoEvents();
                C(f.Resumo.StartsWith("2 pendência(s), 1 aceita(s)"), "resumo acompanha a marcação: " + f.Resumo);
                string png = Environment.GetEnvironmentVariable("ARM_ACEITES_JANELA");
                if (!string.IsNullOrEmpty(png))
                    using (var bmp = new System.Drawing.Bitmap(f.Width, f.Height))
                    {
                        f.DrawToBitmap(bmp, new System.Drawing.Rectangle(0, 0, f.Width, f.Height));
                        bmp.Save(png);
                        rel.AppendLine("        janela salva em " + png);
                    }
                string indice = RelatoriosObra.Aplicar(f.PastaObra, f.Decisoes(), f.Engenheiro);
                f.Close();
                C(File.Exists(indice) && File.ReadAllText(indice).Contains("Pendências aceitas"), "índice da obra regravado com o aceite: " + indice);
            }
            var pend = RelatoriosObra.Pendencias(pasta);
            var ac = pend.FirstOrDefault(p => p.Marca == "V1")?.Aceite;
            C(ac != null && ac.Engenheiro == "Eng. Teste" && ac.Comentario == "estribos de ramos duplos", "aceite do V1 gravado no arquivo da obra");
            C(pend.First(p => p.Marca == "P1").Aceite == null, "P1 continua em aberto");
            C(File.ReadAllText(Path.Combine(pasta, RelatorioHtml.Subpasta, RelatorioHtml.NomeArquivo("V1"))).Contains("ACEITO"), "relatório do V1 regravado com ACEITO");
            using (var f = new FormAceites(pasta)) C(f.Decisoes()[1].Aceitar && f.Engenheiro == "Eng. Teste", "reabrir mostra o aceite marcado e lembra o engenheiro");
        }
        catch (Exception ex) { C(false, "exceção: " + ex); }
        rel.Insert(0, $"Autoteste ARMACEITES {DateTime.Now:yyyy-MM-dd HH:mm:ss} — {falhas} falha(s)\r\n");
        try { File.WriteAllText(Path.Combine(Log.PastaLogs, "autoteste_aceites.txt"), rel.ToString(), Encoding.UTF8); } catch { }
        if (Environment.GetEnvironmentVariable("ARM_ACEITES_FIM") == "1") System.Diagnostics.Process.GetCurrentProcess().Kill();   // encerra a instância de TESTE
    }
}
