using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using DEB.Core.Elementos.Viga.PreMoldada.Calculo;

namespace DEB.Revit.Vigas
{
    /// <summary>
    /// Cargas digitadas pelo engenheiro nas vigas (D40, "eu digito"): parâmetros compartilhados de INSTÂNCIA da categoria
    /// Framing estrutural, editáveis na paleta de propriedades e na janela "Vigas pré-moldadas". Valores característicos;
    /// o peso próprio é calculado pelo programa. GUIDs FIXOS (3c7d9a20-5e1f-4a2b-8c3d-7b6a5f4e00NN): nunca mudar.
    /// </summary>
    internal static class ParametrosCargasViga
    {
        public sealed class Def
        {
            public string Nome;
            public Guid Guid;
            public ForgeTypeId Spec;
            public string Descricao;
        }

        private static Def D(string nome, string guid, ForgeTypeId spec, string desc) =>
            new Def { Nome = nome, Guid = new Guid(guid), Spec = spec, Descricao = desc };

        public static readonly Def G = D("PCEB_Carga permanente (kN/m)", "3c7d9a20-5e1f-4a2b-8c3d-7b6a5f4e0001", SpecTypeId.Number,
            "Carga permanente distribuída ADICIONAL ao peso próprio da viga (laje, capa fora da viga, revestimento, alvenaria), característica, kN/m.");
        public static readonly Def Q = D("PCEB_Carga variável (kN/m)", "3c7d9a20-5e1f-4a2b-8c3d-7b6a5f4e0002", SpecTypeId.Number,
            "Carga variável distribuída (sobrecarga de uso pela laje), característica, kN/m.");
        public static readonly Def Concentradas = D("PCEB_Cargas concentradas", "3c7d9a20-5e1f-4a2b-8c3d-7b6a5f4e0003", SpecTypeId.String.Text,
            "Cargas concentradas características: \"x: G + Q; x: G + Q\" (x em cm da ponta esquerda da peça; G e Q em kN). Ex.: 250: 30 + 12");
        public static readonly Def ApoioE = D("PCEB_Apoio esquerdo (cm)", "3c7d9a20-5e1f-4a2b-8c3d-7b6a5f4e0004", SpecTypeId.Number,
            "Distância da ponta esquerda ao eixo do apoio, cm. 0 = automática (furo do pino; sem pino, meio do dente; ponta reta, 10 cm).");
        public static readonly Def ApoioD = D("PCEB_Apoio direito (cm)", "3c7d9a20-5e1f-4a2b-8c3d-7b6a5f4e0005", SpecTypeId.Number,
            "Distância da ponta direita ao eixo do apoio, cm. 0 = automática.");

        public static readonly Def GAba = D("PCEB_Carga permanente na aba (kN/m)", "3c7d9a20-5e1f-4a2b-8c3d-7b6a5f4e0006", SpecTypeId.Number,
            "Carga permanente apoiada NA ABA, por lado (laje), característica, kN/m. Não está incluída na carga permanente acima; fora do eixo da alma, gera torção.");
        public static readonly Def QAba = D("PCEB_Carga variável na aba (kN/m)", "3c7d9a20-5e1f-4a2b-8c3d-7b6a5f4e0007", SpecTypeId.Number,
            "Carga variável apoiada NA ABA, por lado (sobrecarga pela laje), característica, kN/m. Não está incluída na carga variável acima.");
        public static readonly Def ExcAba = D("PCEB_Excentricidade da carga na aba (cm)", "3c7d9a20-5e1f-4a2b-8c3d-7b6a5f4e0008", SpecTypeId.Number,
            "Distância do eixo da alma à linha da carga na aba, cm. 0 = automática (meio do balanço da aba).");

        public static IEnumerable<Def> Todos => new[] { G, Q, Concentradas, ApoioE, ApoioD, GAba, QAba, ExcAba };

        /// <summary>Cria (e vincula ao Framing estrutural) os que faltam; precisa de transação. Devolve quantos criou.</summary>
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
            string temp = Path.Combine(Path.GetTempPath(), $"DEB_Vigas_params_{Guid.NewGuid():N}.txt");
            File.WriteAllText(temp, "");
            try
            {
                app.SharedParametersFilename = temp;
                DefinitionFile file = app.OpenSharedParameterFile();
                DefinitionGroup group = file.Groups.get_Item("DEB_Vigas") ?? file.Groups.Create("DEB_Vigas");
                var cats = new CategorySet();
                cats.Insert(Category.GetCategory(doc, BuiltInCategory.OST_StructuralFraming));
                foreach (Def d in faltam)
                {
                    if (usados.Contains(d.Nome)) { avisos.Add($"Já existe outro parâmetro chamado \"{d.Nome}\": renomeie-o para o plugin criar o dele."); continue; }
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

        private static Parameter P(Element e, Def d) => e.get_Parameter(d.Guid);

        private static double Num(Element e, Def d)
        {
            Parameter p = P(e, d);
            if (p == null) return 0;
            if (p.StorageType == StorageType.Double) return p.AsDouble();
            if (p.StorageType == StorageType.Integer) return p.AsInteger();
            return double.TryParse((p.AsString() ?? "").Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : 0;
        }

        /// <summary>Cargas da viga lidas dos parâmetros (sem os parâmetros: tudo zero).</summary>
        public static CargasViga Ler(Element e, List<string> erros = null) => new CargasViga
        {
            G = Num(e, G), Q = Num(e, Q), ApoioE = Num(e, ApoioE), ApoioD = Num(e, ApoioD),
            GAba = Num(e, GAba), QAba = Num(e, QAba), ExcAba = Num(e, ExcAba),
            Concentradas = CargasViga.LerConcentradas(P(e, Concentradas)?.AsString() ?? "", erros),
        };

        /// <summary>Grava as cargas na viga (precisa de transação e dos parâmetros).</summary>
        public static void Gravar(Element e, CargasViga c)
        {
            P(e, G)?.Set(c.G);
            P(e, Q)?.Set(c.Q);
            P(e, ApoioE)?.Set(c.ApoioE);
            P(e, ApoioD)?.Set(c.ApoioD);
            P(e, GAba)?.Set(c.GAba);
            P(e, QAba)?.Set(c.QAba);
            P(e, ExcAba)?.Set(c.ExcAba);
            P(e, Concentradas)?.Set(CargasViga.TextoConcentradas(c.Concentradas));
        }
    }
}
