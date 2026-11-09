using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using DEB.Core.Pecas;
using DEB.Revit.Core;

namespace DEB.Revit.Precast
{
    /// <summary>
    /// Edições da janela "Conferir peças" (etapa 3, 09/11/2026) — as ÚNICAS coisas que a conferência grava no modelo:
    /// a marca das peças de um grupo (parâmetro Marca: "a marca é o nome da peça") e a decisão "não exportar" (Extensible
    /// Storage do plugin, no próprio elemento: vale nas próximas conferências). A geometria nunca é alterada.
    /// </summary>
    internal static class PieceReviewEdits
    {
        private static readonly Guid SchemaId = new Guid("3b0c2f6e-7a51-4c1d-9a2e-5d1f0c8e4b77");
        private const string FieldSkip = "NaoExportar";

        private static Schema GetSchema(bool create)
        {
            Schema s = Schema.Lookup(SchemaId);
            if (s != null || !create) return s;
            var b = new SchemaBuilder(SchemaId);
            b.SetSchemaName("DEB_ConferenciaPeca");
            b.SetDocumentation("DEB.Revit: decisões da conferência das peças (não exportar para o CAD).");
            b.SetReadAccessLevel(AccessLevel.Public);
            b.SetWriteAccessLevel(AccessLevel.Public);
            b.AddSimpleField(FieldSkip, typeof(bool));
            return b.Finish();
        }

        /// <summary>O elemento está marcado como "não exportar"?</summary>
        public static bool IsSkipped(Element e)
        {
            Schema s = GetSchema(false);
            if (s == null) return false;
            Entity ent = e.GetEntity(s);
            return ent != null && ent.IsValid() && ent.Get<bool>(FieldSkip);
        }

        /// <summary>
        /// Grava a marca em todas as peças do grupo (uma transação, um desfazer). Devolve null se gravou em todas, ou a mensagem
        /// com as que não aceitaram (parâmetro só de leitura, peça de outro usuário no modelo compartilhado…).
        /// </summary>
        public static string SetMark(Document doc, GrupoPecas g, string mark)
        {
            mark = (mark ?? "").Trim();
            var falhas = new List<string>();
            using (var t = new Transaction(doc, "Conferir peças: marca " + mark))
            {
                t.Start();
                WarningSwallower.Attach(t);   // "marca duplicada" é o esperado: peças iguais têm a mesma marca
                foreach (PecaLevantada p in g.Pecas)
                {
                    Element e = doc.GetElement(new ElementId(long.Parse(p.Id, CultureInfo.InvariantCulture)));
                    Parameter par = e?.get_Parameter(BuiltInParameter.ALL_MODEL_MARK);
                    try
                    {
                        if (par == null || par.IsReadOnly || !par.Set(mark)) { falhas.Add(p.Id); continue; }
                        p.Marca = mark;
                    }
                    catch (Exception) { falhas.Add(p.Id); }
                }
                if (t.Commit() != TransactionStatus.Committed) return "O Revit não aceitou a alteração (transação desfeita).";
            }
            return falhas.Count == 0 ? null : $"Marca não gravada em {falhas.Count} peça(s) (parâmetro bloqueado): {string.Join(", ", falhas)}.";
        }

        /// <summary>Grava "não exportar" (ou tira) em todas as peças do grupo. Devolve null se ok, ou a mensagem.</summary>
        public static string SetSkipped(Document doc, GrupoPecas g, bool skip)
        {
            using (var t = new Transaction(doc, skip ? "Conferir peças: não exportar" : "Conferir peças: exportar"))
            {
                t.Start();
                WarningSwallower.Attach(t);
                Schema s = GetSchema(true);
                foreach (PecaLevantada p in g.Pecas)
                {
                    Element e = doc.GetElement(new ElementId(long.Parse(p.Id, CultureInfo.InvariantCulture)));
                    if (e == null) continue;
                    if (skip)
                    {
                        var ent = new Entity(s);
                        ent.Set(FieldSkip, true);
                        e.SetEntity(ent);
                    }
                    else e.DeleteEntity(s);
                    p.NaoExportar = skip;
                }
                if (t.Commit() != TransactionStatus.Committed) return "O Revit não aceitou a alteração (transação desfeita).";
            }
            return null;
        }
    }
}
