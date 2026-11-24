using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using DEB.Core.Analise.Objetos;

namespace DEB.Revit.Analise
{
    /// <summary>
    /// Ajustes do modelo analítico (D50) gravados no .rvt (Extensible Storage nas Informações do projeto), em JSON: objeto → propriedade
    /// → valor. O modelo do Revit continua sendo a origem; os ajustes valem por cima toda vez que a análise é montada.
    /// </summary>
    internal static class AjustesRevit
    {
        // GUID fixo: gravado nos modelos (nunca mudar)
        private static readonly Guid SchemaId = new Guid("9e7d3a41-2b6c-4f18-8d05-71c4e2a9b630");
        private const string Campo = "Json";

        private static Schema GetSchema(bool criar)
        {
            Schema s = Schema.Lookup(SchemaId);
            if (s != null || !criar) return s;
            var b = new SchemaBuilder(SchemaId);
            b.SetSchemaName("PCEB_AjustesAnalise");
            b.SetDocumentation("PCEB: ajustes do modelo analítico (D50) em JSON.");
            b.SetReadAccessLevel(AccessLevel.Public);
            b.SetWriteAccessLevel(AccessLevel.Public);
            b.AddSimpleField(Campo, typeof(string));
            return b.Finish();
        }

        public static Dictionary<string, Dictionary<string, string>> Ler(Document doc, List<string> notas = null)
        {
            try
            {
                Schema s = GetSchema(false);
                Entity ent = s == null ? null : doc.ProjectInformation?.GetEntity(s);
                string json = ent != null && ent.IsValid() ? ent.Get<string>(Campo) : null;
                return AjustesAnalise.DeJson(json);
            }
            catch (Exception ex) { notas?.Add("Ajustes do modelo analítico não lidos: " + ex.Message); return new Dictionary<string, Dictionary<string, string>>(); }
        }

        public static void Gravar(Document doc, Dictionary<string, Dictionary<string, string>> ajustes)
        {
            using (var t = new Transaction(doc, "PCEB: ajustes do modelo analítico"))
            {
                t.Start();
                var ent = new Entity(GetSchema(true));
                ent.Set(Campo, AjustesAnalise.ParaJson(ajustes));
                doc.ProjectInformation.SetEntity(ent);
                t.Commit();
            }
        }
    }
}
