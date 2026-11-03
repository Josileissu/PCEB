using System;
using System.IO;
using System.Text;

namespace DEB.Revit.Core
{
    /// <summary>
    /// Pasta de dados do usuário (configuração, padrões de apresentação, modelos de armadura, logs):
    /// %AppData%\PCEB\DEB.Revit. Na 1ª vez, copia o conteúdo da pasta do nome antigo
    /// (%AppData%\StructuralDetailer), que fica intacta.
    /// </summary>
    public static class UserFolders
    {
        public static string Root => _root.Value;

        private static readonly Lazy<string> _root = new Lazy<string>(() =>
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string nova = Path.Combine(appData, "PCEB", "DEB.Revit");
            string antiga = Path.Combine(appData, "StructuralDetailer");
            try
            {
                if (!Directory.Exists(nova) && Directory.Exists(antiga))
                {
                    CopyFolder(antiga, nova);
                    File.WriteAllText(Path.Combine(nova, "migrado_de_StructuralDetailer.txt"),
                        $"Copiado de {antiga} em {DateTime.Now:dd/MM/yyyy HH:mm}. A pasta antiga pode ser apagada.", Encoding.UTF8);
                }
            }
            catch { /* migração nunca derruba o Revit; no pior caso começa com os padrões de fábrica */ }
            return nova;
        });

        private static void CopyFolder(string origem, string destino)
        {
            Directory.CreateDirectory(destino);
            foreach (string f in Directory.GetFiles(origem))
                File.Copy(f, Path.Combine(destino, Path.GetFileName(f)), false);
            foreach (string d in Directory.GetDirectories(origem))
                CopyFolder(d, Path.Combine(destino, Path.GetFileName(d)));
        }
    }
}
