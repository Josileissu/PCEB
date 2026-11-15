using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using DEB.Core.Formas;

namespace DEB.Revit.Formas
{
    /// <summary>
    /// Parâmetros SD_ das pranchas de formas (D25, tabela em docs/04_arquitetura/pranchas_de_formas.md): compartilhados, de
    /// instância, vinculados ao projeto por categoria. Os nomes e os GUIDs são FIXOS — ficam gravados nos modelos e nas famílias
    /// de identificador do escritório (os rótulos leem o GUID); nunca mudar. Escada e parede entram nas etapas 6 e 7.
    /// </summary>
    internal static class ParametrosFormas
    {
        public enum Cat { Pilar, Viga, Laje, Fundacao }

        public sealed class Def
        {
            public string Nome;
            public Guid Guid;
            public ForgeTypeId Spec;
            public Cat[] Categorias;
        }

        private static Def D(string nome, string guid, ForgeTypeId spec, params Cat[] cats) =>
            new Def { Nome = nome, Guid = new Guid(guid), Spec = spec, Categorias = cats };

        private static ForgeTypeId Texto => SpecTypeId.String.Text;
        private static ForgeTypeId Numero => SpecTypeId.Number;

        // GUIDs fixos (8e2a5c10-4b7d-4e61-9f3a-0a1b2c3dNNNN). NÃO ALTERAR.
        public static readonly Def Secao = D("SD_Secao", "8e2a5c10-4b7d-4e61-9f3a-0a1b2c3d0001", Texto, Cat.Pilar, Cat.Viga);
        public static readonly Def NivelTopo = D("SD_NivelTopo", "8e2a5c10-4b7d-4e61-9f3a-0a1b2c3d0002", Texto, Cat.Pilar);
        public static readonly Def NivelTopoBloco = D("SD_NivelTopoBloco", "8e2a5c10-4b7d-4e61-9f3a-0a1b2c3d0003", Texto, Cat.Pilar);
        public static readonly Def NivelBase = D("SD_NivelBase", "8e2a5c10-4b7d-4e61-9f3a-0a1b2c3d0004", Texto, Cat.Pilar);
        public static readonly Def ComprimentoTotal = D("SD_ComprimentoTotal", "8e2a5c10-4b7d-4e61-9f3a-0a1b2c3d0005", Texto, Cat.Pilar);
        /// <summary>Esforços na fundação (tf, tf.m), na ordem de <see cref="CsvCargas.Colunas"/>.</summary>
        public static readonly Def[] Esforcos =
        {
            D("SD_Fz_Max", "8e2a5c10-4b7d-4e61-9f3a-0a1b2c3d0006", Numero, Cat.Pilar),
            D("SD_Fz_Min", "8e2a5c10-4b7d-4e61-9f3a-0a1b2c3d0007", Numero, Cat.Pilar),
            D("SD_Mx_Pos", "8e2a5c10-4b7d-4e61-9f3a-0a1b2c3d0008", Numero, Cat.Pilar),
            D("SD_Mx_Neg", "8e2a5c10-4b7d-4e61-9f3a-0a1b2c3d0009", Numero, Cat.Pilar),
            D("SD_My_Pos", "8e2a5c10-4b7d-4e61-9f3a-0a1b2c3d000a", Numero, Cat.Pilar),
            D("SD_My_Neg", "8e2a5c10-4b7d-4e61-9f3a-0a1b2c3d000b", Numero, Cat.Pilar),
            D("SD_Fx_Pos", "8e2a5c10-4b7d-4e61-9f3a-0a1b2c3d000c", Numero, Cat.Pilar),
            D("SD_Fx_Neg", "8e2a5c10-4b7d-4e61-9f3a-0a1b2c3d000d", Numero, Cat.Pilar),
            D("SD_Fy_Pos", "8e2a5c10-4b7d-4e61-9f3a-0a1b2c3d000e", Numero, Cat.Pilar),
            D("SD_Fy_Neg", "8e2a5c10-4b7d-4e61-9f3a-0a1b2c3d000f", Numero, Cat.Pilar),
        };
        public static readonly Def Observacao = D("SD_Observacao", "8e2a5c10-4b7d-4e61-9f3a-0a1b2c3d0010", Texto, Cat.Pilar, Cat.Fundacao);
        public static readonly Def Elevacao = D("SD_Elevacao", "8e2a5c10-4b7d-4e61-9f3a-0a1b2c3d0011", Texto, Cat.Viga);
        public static readonly Def LajeTipo = D("SD_Laje_Tipo", "8e2a5c10-4b7d-4e61-9f3a-0a1b2c3d0012", Texto, Cat.Laje);
        public static readonly Def LajeAltura = D("SD_Laje_Altura", "8e2a5c10-4b7d-4e61-9f3a-0a1b2c3d0013", Numero, Cat.Laje);
        public static readonly Def LajeElevacao = D("SD_Laje_Elevacao", "8e2a5c10-4b7d-4e61-9f3a-0a1b2c3d0014", Numero, Cat.Laje);
        public static readonly Def LajeNivel = D("SD_Laje_Nivel", "8e2a5c10-4b7d-4e61-9f3a-0a1b2c3d0015", Numero, Cat.Laje);
        public static readonly Def LajePesoProprio = D("SD_Laje_PesoProprio", "8e2a5c10-4b7d-4e61-9f3a-0a1b2c3d0016", Numero, Cat.Laje);
        public static readonly Def LajeAdicional = D("SD_Laje_Adicional", "8e2a5c10-4b7d-4e61-9f3a-0a1b2c3d0017", Numero, Cat.Laje);
        public static readonly Def LajeAcidental = D("SD_Laje_Acidental", "8e2a5c10-4b7d-4e61-9f3a-0a1b2c3d0018", Numero, Cat.Laje);
        public static readonly Def LajeLocalizada = D("SD_Laje_Localizada", "8e2a5c10-4b7d-4e61-9f3a-0a1b2c3d0019", Numero, Cat.Laje);
        /// <summary>Posição da marca na ordem natural (P2 antes de P10): as tabelas ordenam por ele (o Revit ordena texto caractere a
        /// caractere) e filtram por ele (&gt; 0 = peça que o comando tratou). Coluna escondida nas tabelas.</summary>
        public static readonly Def Ordem = D("SD_Ordem", "8e2a5c10-4b7d-4e61-9f3a-0a1b2c3d001a", SpecTypeId.Int.Integer, Cat.Pilar, Cat.Laje);

        public static IEnumerable<Def> Todos =>
            new[] { Secao, NivelTopo, NivelTopoBloco, NivelBase, ComprimentoTotal }
                .Concat(Esforcos)
                .Concat(new[] { Observacao, Elevacao, LajeTipo, LajeAltura, LajeElevacao, LajeNivel, LajePesoProprio, LajeAdicional,
                                LajeAcidental, LajeLocalizada, Ordem });

        public static BuiltInCategory Bic(Cat c) => c switch
        {
            Cat.Pilar => BuiltInCategory.OST_StructuralColumns,
            Cat.Viga => BuiltInCategory.OST_StructuralFraming,
            Cat.Laje => BuiltInCategory.OST_Floors,
            _ => BuiltInCategory.OST_StructuralFoundation,
        };

        /// <summary>Id do parâmetro no projeto (campos das tabelas); inválido se não existe.</summary>
        public static ElementId Id(Document doc, Def d) => SharedParameterElement.Lookup(doc, d.Guid)?.Id ?? ElementId.InvalidElementId;

        /// <summary>
        /// Cria (e vincula às categorias) os parâmetros que faltam, por um arquivo de parâmetros compartilhados temporário; o arquivo
        /// do usuário é restaurado no fim. Parâmetro que já existe e falta categoria: vínculo ampliado. Nome já usado por OUTRO
        /// parâmetro (outro GUID): não cria, avisa. Precisa de transação. Devolve quantos foram criados.
        /// </summary>
        public static int Garantir(Document doc, List<string> avisos)
        {
            var app = doc.Application;
            var faltam = new List<Def>();
            foreach (Def d in Todos)
            {
                SharedParameterElement spe = SharedParameterElement.Lookup(doc, d.Guid);
                if (spe == null) { faltam.Add(d); continue; }
                AmpliarVinculo(doc, spe.GetDefinition(), d, avisos);
            }
            if (faltam.Count == 0) return 0;

            // nomes já usados por outros parâmetros do projeto
            var usados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var it = doc.ParameterBindings.ForwardIterator();
            while (it.MoveNext()) if (it.Key != null) usados.Add(it.Key.Name);

            int criados = 0;
            string anterior = app.SharedParametersFilename;
            string temp = Path.Combine(Path.GetTempPath(), $"DEB_Formas_params_{Guid.NewGuid():N}.txt");
            File.WriteAllText(temp, "");
            try
            {
                app.SharedParametersFilename = temp;
                DefinitionFile file = app.OpenSharedParameterFile();
                DefinitionGroup group = file.Groups.get_Item("DEB_Formas") ?? file.Groups.Create("DEB_Formas");
                foreach (Def d in faltam)
                {
                    if (usados.Contains(d.Nome))
                    {
                        avisos.Add($"Já existe no projeto outro parâmetro chamado \"{d.Nome}\" (não é o do plugin): renomeie-o para o plugin poder criar o dele. Esse dado não será gravado.");
                        continue;
                    }
                    Definition def = group.Definitions.get_Item(d.Nome) ??
                                     group.Definitions.Create(new ExternalDefinitionCreationOptions(d.Nome, d.Spec) { GUID = d.Guid });
                    if (doc.ParameterBindings.Insert(def, app.Create.NewInstanceBinding(Categorias(doc, d)), GroupTypeId.Data)) criados++;
                    else avisos.Add($"O Revit não aceitou criar o parâmetro \"{d.Nome}\".");
                }
            }
            finally
            {
                try { app.SharedParametersFilename = anterior ?? ""; } catch { /* arquivo do usuário sumiu: fica sem */ }
                try { File.Delete(temp); } catch { /* temporário */ }
            }
            return criados;
        }

        private static CategorySet Categorias(Document doc, Def d)
        {
            var cats = new CategorySet();
            foreach (Cat c in d.Categorias)
            {
                Category cat = Category.GetCategory(doc, Bic(c));
                if (cat != null && cat.AllowsBoundParameters) cats.Insert(cat);
            }
            return cats;
        }

        private static void AmpliarVinculo(Document doc, InternalDefinition def, Def d, List<string> avisos)
        {
            try
            {
                if (!(doc.ParameterBindings.get_Item(def) is InstanceBinding ib)) return;   // de tipo (feito à mão): não mexe
                var falta = d.Categorias.Select(c => Category.GetCategory(doc, Bic(c)))
                    .Where(c => c != null && c.AllowsBoundParameters && !ib.Categories.Contains(c)).ToList();
                if (falta.Count == 0) return;
                CategorySet cats = ib.Categories;
                foreach (Category c in falta) cats.Insert(c);
                if (!doc.ParameterBindings.ReInsert(def, doc.Application.Create.NewInstanceBinding(cats), def.GetGroupTypeId()))
                    avisos.Add($"Parâmetro \"{d.Nome}\": não foi possível incluir as categorias que faltam.");
            }
            catch (Exception ex) { avisos.Add($"Parâmetro \"{d.Nome}\": vínculo não conferido ({ex.Message})."); }
        }
    }
}
