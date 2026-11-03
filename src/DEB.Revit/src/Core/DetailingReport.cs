using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace DEB.Revit.Core
{
    internal sealed class PieceLog
    {
        public string Title { get; }
        public List<string> Infos { get; } = new List<string>();
        public List<string> Warnings { get; } = new List<string>();
        public List<string> Errors { get; } = new List<string>();
        /// <summary>Exceções completas (com pilha); vão só para o arquivo de log.</summary>
        public List<string> Traces { get; } = new List<string>();

        public PieceLog(string title) { Title = title; }

        public void Info(string s) => Infos.Add(s);
        public void Warn(string s) => Warnings.Add(s);
        public void Error(string s) => Errors.Add(s);
        public void Trace(string step, Exception ex) => Traces.Add($"[{step}] {ex}");
    }

    internal sealed class DetailingReport
    {
        public List<PieceLog> Pieces { get; } = new List<PieceLog>();
        public List<string> General { get; } = new List<string>();

        public PieceLog Begin(string title)
        {
            var p = new PieceLog(title);
            Pieces.Add(p);
            return p;
        }

        public int Succeeded => Pieces.Count(p => p.Errors.Count == 0 && p.Infos.Count > 0);
        public int WarningCount => Pieces.Sum(p => p.Warnings.Count) + General.Count;

        public string Summary()
        {
            var sb = new StringBuilder();
            foreach (PieceLog p in Pieces)
            {
                string status = p.Errors.Count > 0 ? "✗" : p.Warnings.Count > 0 ? "⚠" : "✓";
                sb.AppendLine($"{status} {p.Title}");
                foreach (string i in p.Infos) sb.AppendLine("   " + i);
                foreach (string e in p.Errors) sb.AppendLine("   ERRO: " + e);
            }
            return sb.ToString().TrimEnd();
        }

        public string Details()
        {
            var sb = new StringBuilder();
            foreach (string g in General) sb.AppendLine("• " + g);
            foreach (PieceLog p in Pieces.Where(x => x.Warnings.Count > 0))
            {
                sb.AppendLine(p.Title + ":");
                foreach (string w in p.Warnings) sb.AppendLine("  • " + w);
            }
            return sb.ToString().TrimEnd();
        }

        public static string LogDir => Path.Combine(UserFolders.Root, "logs");

        /// <summary>
        /// Grava o relatório completo (resumo, avisos e exceções com pilha) em
        /// %AppData%\PCEB\DEB.Revit\logs\. Nunca lança exceção.
        /// </summary>
        public string WriteToFile(string documentTitle, string revitVersion)
        {
            try
            {
                Directory.CreateDirectory(LogDir);
                string path = Path.Combine(LogDir, $"detalhamento_{DateTime.Now:yyyyMMdd_HHmmss}.log");

                var sb = new StringBuilder();
                sb.AppendLine($"Data: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine($"Revit: {revitVersion}");
                sb.AppendLine($"Modelo: {documentTitle}");
                sb.AppendLine($"Plugin: {typeof(DetailingReport).Assembly.GetName().Version}");
                sb.AppendLine();
                sb.AppendLine("== Resumo ==");
                sb.AppendLine(Summary());
                string details = Details();
                if (details.Length > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("== Avisos ==");
                    sb.AppendLine(details);
                }
                foreach (PieceLog p in Pieces.Where(x => x.Traces.Count > 0))
                {
                    sb.AppendLine();
                    sb.AppendLine($"== Exceções: {p.Title} ==");
                    foreach (string t in p.Traces) sb.AppendLine(t).AppendLine();
                }

                File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
                return path;
            }
            catch
            {
                return null;
            }
        }
    }
}
