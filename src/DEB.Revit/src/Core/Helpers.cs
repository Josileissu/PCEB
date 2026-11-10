using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace DEB.Revit.Core
{
    /// <summary>Conversões de unidade (o Revit trabalha internamente em pés).</summary>
    internal static class U
    {
        public static double Mm(double mm) => UnitUtils.ConvertToInternalUnits(mm, UnitTypeId.Millimeters);
        public static double ToMm(double internalLength) => UnitUtils.ConvertFromInternalUnits(internalLength, UnitTypeId.Millimeters);
        public static string Cm(double internalLength) => (ToMm(internalLength) / 10.0).ToString("0.#");
        public static string M(double internalLength) => (ToMm(internalLength) / 1000.0).ToString("0.00");
    }

    /// <summary>Gera nomes únicos (vistas, assemblies, números de folha).</summary>
    internal sealed class NameRegistry
    {
        private readonly HashSet<string> _used;

        public NameRegistry(IEnumerable<string> used)
        {
            _used = new HashSet<string>(used.Where(s => s != null), StringComparer.OrdinalIgnoreCase);
        }

        public string Unique(string baseName)
        {
            string name = baseName;
            int i = 2;
            while (_used.Contains(name)) name = $"{baseName} ({i++})";
            _used.Add(name);
            return name;
        }

        public static NameRegistry ForViews(Document doc) =>
            new NameRegistry(new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().Select(v => v.Name));

        public static NameRegistry ForAssemblyTypes(Document doc) =>
            new NameRegistry(new FilteredElementCollector(doc).OfClass(typeof(AssemblyType)).Select(e => e.Name));

        public static NameRegistry ForSheetNumbers(Document doc) =>
            new NameRegistry(new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>().Select(s => s.SheetNumber));

        /// <summary>Remove caracteres não aceitos em nomes de vista / folha.</summary>
        public static string Sanitize(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return s;
            foreach (char c in "{}[]|;<>?`~:\\/") s = s.Replace(c, '_');
            return s.Trim();
        }
    }

    /// <summary>Descarta avisos (warnings) durante as transações do plugin; erros continuam bloqueando.</summary>
    internal sealed class WarningSwallower : IFailuresPreprocessor
    {
        public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
        {
            foreach (FailureMessageAccessor f in failuresAccessor.GetFailureMessages())
            {
                if (f.GetSeverity() == FailureSeverity.Warning)
                    failuresAccessor.DeleteWarning(f);
                else
                    LastError = f.GetDescriptionText();   // vai para o log quando a transação não é confirmada
            }
            return FailureProcessingResult.Continue;
        }

        /// <summary>Último erro do Revit numa transação com este pré-processador (para explicar "não confirmada").</summary>
        [ThreadStatic] public static string LastError;

        /// <summary>" (motivo)" do último erro, e limpa.</summary>
        public static string TakeReason()
        {
            string r = LastError;
            LastError = null;
            return string.IsNullOrEmpty(r) ? "" : $" ({r})";
        }

        public static void Attach(Transaction t)
        {
            FailureHandlingOptions o = t.GetFailureHandlingOptions();
            o.SetFailuresPreprocessor(new WarningSwallower());
            t.SetFailureHandlingOptions(o);
        }
    }

    /// <summary>Retângulo 2D em coordenadas de folha.</summary>
    internal readonly struct Rect2
    {
        public readonly double L, B, R, T;
        public Rect2(double l, double b, double r, double t) { L = l; B = b; R = r; T = t; }
        public double W => R - L;
        public double H => T - B;
        public bool Intersects(Rect2 o) => L < o.R && R > o.L && B < o.T && T > o.B;
    }
}
