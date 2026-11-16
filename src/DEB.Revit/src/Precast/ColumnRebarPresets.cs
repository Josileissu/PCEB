using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using DEB.Core.Templates.PilarPreMoldado.Modelo3D;

namespace DEB.Revit.Precast
{
    /// <summary>
    /// Modelos de armadura de pilar salvos pelo usuário (%AppData%\PCEB\DEB.Revit\ModelosArmaduraPilar.json). Saiu de
    /// ColumnRebarSpec quando a especificação foi para o núcleo (etapa 3, 16/11/2026); o arquivo e o formato não mudaram.
    /// </summary>
    internal static class ColumnRebarPresets
    {
        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        };

        public static string PresetsPath => Path.Combine(DEB.Revit.Core.UserFolders.Root, "ModelosArmaduraPilar.json");

        public static List<ColumnRebarSpec> LoadPresets()
        {
            try
            {
                if (!File.Exists(PresetsPath)) return new List<ColumnRebarSpec>();
                return JsonSerializer.Deserialize<List<ColumnRebarSpec>>(File.ReadAllText(PresetsPath), Json)
                       ?? new List<ColumnRebarSpec>();
            }
            catch
            {
                return new List<ColumnRebarSpec>();
            }
        }

        public static void SavePreset(ColumnRebarSpec spec)
        {
            List<ColumnRebarSpec> all = LoadPresets().Where(p => !string.Equals(p.Name, spec.Name, StringComparison.OrdinalIgnoreCase)).ToList();
            all.Add(spec.Clone());
            Directory.CreateDirectory(Path.GetDirectoryName(PresetsPath));
            File.WriteAllText(PresetsPath, JsonSerializer.Serialize(all.OrderBy(p => p.Name).ToList(), Json));
        }

        /// <summary>Verificações da especificação para um pilar levantado (as regras estão no núcleo).</summary>
        public static List<string> Check(this ColumnRebarSpec s, PrecastColumn rep) => s.Check(rep.Frame, rep.Corbels);
    }
}
