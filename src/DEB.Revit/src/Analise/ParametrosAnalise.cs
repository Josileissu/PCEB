using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;

namespace DEB.Revit.Analise
{
    /// <summary>
    /// Parâmetros compartilhados da análise nas lajes (D50): cargas, peso próprio das pré-fabricadas e tipo de laje. GUIDs FIXOS
    /// <c>6a1e3f40-2c7b-4d19-8e5a-9b0c1d2e00NN</c> — gravados nos modelos, nunca mudar. A leitura aceita também parâmetros de projeto com
    /// o mesmo nome criados à mão antes deste grupo existir.
    /// </summary>
    internal static class ParametrosAnalise
    {
        internal sealed class Def
        {
            public string Nome = "";
            public Guid Guid;
            public ForgeTypeId Spec;
            public string Descricao = "";
        }

        private static Def D(string nome, string guid, ForgeTypeId spec, string desc) => new Def { Nome = nome, Guid = new Guid(guid), Spec = spec, Descricao = desc };

        public static readonly Def LajeG = D(EstruturaRevit.ParamLajeG, "6a1e3f40-2c7b-4d19-8e5a-9b0c1d2e0001", SpecTypeId.Number,
            "Carga permanente adicional sobre a laje (kN/m²), além do peso próprio. Vazio = a da configuração do projeto.");
        public static readonly Def LajeQ = D(EstruturaRevit.ParamLajeQ, "6a1e3f40-2c7b-4d19-8e5a-9b0c1d2e0002", SpecTypeId.Number,
            "Carga variável sobre a laje (kN/m²). Vazio = a da configuração do projeto.");
        public static readonly Def LajePeso = D(EstruturaRevit.ParamLajePeso, "6a1e3f40-2c7b-4d19-8e5a-9b0c1d2e0003", SpecTypeId.Number,
            "Peso próprio da laje pré-fabricada (kN/m²): alveolar, treliçada ou vigota. Vazio = pela fração maciça da configuração.");
        public static readonly Def LajeTipo = D(EstruturaRevit.ParamLajeTipo, "6a1e3f40-2c7b-4d19-8e5a-9b0c1d2e0004", SpecTypeId.String.Text,
            "Tipo de laje para a análise: maciça, alveolar, treliçada ou vigota protendida. Vazio = pelo nome do tipo de piso.");

        public static IEnumerable<Def> Todos => new[] { LajeG, LajeQ, LajePeso, LajeTipo };

        /// <summary>Parâmetro do elemento: o compartilhado (pelo GUID) ou um de projeto com o mesmo nome.</summary>
        public static Parameter Ler(Element e, Def d) => e.get_Parameter(d.Guid) ?? e.LookupParameter(d.Nome);

        public static int Faltam(Document doc) => Todos.Count(d => SharedParameterElement.Lookup(doc, d.Guid) == null);

        /// <summary>Cria (e vincula aos pisos) os que faltam; precisa de transação. Devolve quantos criou.</summary>
        public static int Garantir(Document doc, List<string> avisos)
        {
            var app = doc.Application;
            var faltam = Todos.Where(d => SharedParameterElement.Lookup(doc, d.Guid) == null).ToList();
            if (faltam.Count == 0) return 0;
            var usados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var it = doc.ParameterBindings.ForwardIterator();
            while (it.MoveNext()) if (it.Key != null) usados.Add(it.Key.Name);

            int criados = 0;
            string anterior = app.SharedParametersFilename;
            string temp = Path.Combine(Path.GetTempPath(), $"DEB_Analise_params_{Guid.NewGuid():N}.txt");
            File.WriteAllText(temp, "");
            try
            {
                app.SharedParametersFilename = temp;
                DefinitionFile file = app.OpenSharedParameterFile();
                DefinitionGroup group = file.Groups.get_Item("DEB_Analise") ?? file.Groups.Create("DEB_Analise");
                var cats = new CategorySet();
                cats.Insert(Category.GetCategory(doc, BuiltInCategory.OST_Floors));
                foreach (Def d in faltam)
                {
                    if (usados.Contains(d.Nome))
                    {
                        avisos.Add($"Já existe no projeto um parâmetro chamado \"{d.Nome}\" (criado à mão): a análise continua lendo esse; o do PCEB não foi criado.");
                        continue;
                    }
                    Definition def = group.Definitions.get_Item(d.Nome) ??
                                     group.Definitions.Create(new ExternalDefinitionCreationOptions(d.Nome, d.Spec) { GUID = d.Guid, Description = d.Descricao });
                    if (doc.ParameterBindings.Insert(def, app.Create.NewInstanceBinding(cats), GroupTypeId.StructuralAnalysis)) criados++;
                    else avisos.Add($"O Revit não aceitou criar o parâmetro \"{d.Nome}\".");
                }
            }
            finally
            {
                try { app.SharedParametersFilename = anterior ?? ""; } catch { }
                try { File.Delete(temp); } catch { }
            }
            return criados;
        }
    }
}
