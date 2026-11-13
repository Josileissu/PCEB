using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;

namespace DEB.Revit.Formas
{
    /// <summary>
    /// Marca dos elementos criados (ou adotados) pelos comandos de formas: Extensible Storage no próprio elemento, com o papel
    /// ("vista-locacao:&lt;nível&gt;", "tabela-cargas", "id-pilar"...). Rodar de novo acha o que é do comando e atualiza, sem
    /// duplicar; o que não tem a marca nunca é apagado nem renomeado.
    /// </summary>
    internal static class MarcaFormas
    {
        private static readonly Guid SchemaId = new Guid("c41e7b52-3d0a-4f86-b1e9-7a2d5c8f3e61");
        private const string Campo = "Papel";

        private static Schema GetSchema(bool create)
        {
            Schema s = Schema.Lookup(SchemaId);
            if (s != null || !create) return s;
            var b = new SchemaBuilder(SchemaId);
            b.SetSchemaName("DEB_Formas");
            b.SetDocumentation("DEB.Revit: elementos criados pelos comandos das pranchas de formas (papel de cada um).");
            b.SetReadAccessLevel(AccessLevel.Public);
            b.SetWriteAccessLevel(AccessLevel.Public);
            b.AddSimpleField(Campo, typeof(string));
            return b.Finish();
        }

        /// <summary>Papel gravado no elemento; "" se não é do comando.</summary>
        public static string Papel(Element e)
        {
            Schema s = GetSchema(false);
            if (s == null || e == null) return "";
            try
            {
                Entity ent = e.GetEntity(s);
                return ent != null && ent.IsValid() ? ent.Get<string>(Campo) ?? "" : "";
            }
            catch { return ""; }
        }

        /// <summary>Grava o papel. Precisa de transação. Devolve false se o elemento não aceitou (o comando segue).</summary>
        public static bool Marcar(Element e, string papel)
        {
            try
            {
                var ent = new Entity(GetSchema(true));
                ent.Set(Campo, papel ?? "");
                e.SetEntity(ent);
                return true;
            }
            catch { return false; }
        }

        /// <summary>Tira a marca (a vista adotada passa a ser outra). Precisa de transação.</summary>
        public static void Desmarcar(Element e)
        {
            Schema s = GetSchema(false);
            if (s == null) return;
            try { e.DeleteEntity(s); } catch { /* sem a marca */ }
        }

        /// <summary>Elementos do coletor com o papel dado (exato).</summary>
        public static List<Element> Com(FilteredElementCollector col, string papel) =>
            GetSchema(false) == null ? new List<Element>() : col.ToElements().Where(e => Papel(e) == papel).ToList();

        /// <summary>Elementos do coletor cujo papel começa com o prefixo.</summary>
        public static List<Element> ComPrefixo(FilteredElementCollector col, string prefixo) =>
            GetSchema(false) == null ? new List<Element>() : col.ToElements().Where(e => Papel(e).StartsWith(prefixo, StringComparison.Ordinal)).ToList();
    }
}
