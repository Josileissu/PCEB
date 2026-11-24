using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using DEB.Core.Configuracao.Projeto;

namespace DEB.Revit.Configuracao
{
    /// <summary>
    /// Configuração do projeto (D43) no Revit: o escritório vem do arquivo comum ao CAD (<see cref="ArquivoEscritorio"/>); o projeto
    /// mora dentro do .rvt (Extensible Storage nas Informações do projeto), para ir junto com o modelo, com uma cópia legível
    /// "&lt;modelo&gt;.pceb.json" ao lado do arquivo.
    /// </summary>
    internal static class ConfiguracaoRevit
    {
        // GUID fixo: está gravado nos modelos (nunca mudar)
        private static readonly Guid SchemaId = new Guid("0c19b8be-015a-427e-af6d-f6564d6c96a1");
        private const string Campo = "Json";

        private static Schema GetSchema(bool criar)
        {
            Schema s = Schema.Lookup(SchemaId);
            if (s != null || !criar) return s;
            var b = new SchemaBuilder(SchemaId);
            b.SetSchemaName("PCEB_ConfiguracaoProjeto");
            b.SetDocumentation("PCEB: configuração do projeto (D43), nível do projeto em JSON.");
            b.SetReadAccessLevel(AccessLevel.Public);
            b.SetWriteAccessLevel(AccessLevel.Public);
            b.AddSimpleField(Campo, typeof(string));
            return b.Finish();
        }

        /// <summary>Configuração em vigor no modelo: escritório (arquivo) + projeto (modelo). Os avisos vão para a lista.</summary>
        public static ConfiguracaoProjeto Carregar(Document doc, List<string> avisos = null)
        {
            var cfg = new ConfiguracaoProjeto { Escritorio = ArquivoEscritorio.Ler(out string aviso) };
            if (aviso != null) avisos?.Add(aviso);
            if (doc == null || doc.IsFamilyDocument) return cfg;
            try
            {
                Schema s = GetSchema(false);
                Entity ent = s == null ? null : doc.ProjectInformation?.GetEntity(s);
                string json = ent != null && ent.IsValid() ? ent.Get<string>(Campo) : null;
                if (!string.IsNullOrWhiteSpace(json))
                {
                    NivelValores p = NivelValores.DeJson(json);
                    p.Nivel = NivelConfiguracao.Projeto;
                    string a = null;
                    ArquivoEscritorio.Limpar(p, ref a);
                    if (a != null) avisos?.Add("Projeto: " + a);
                    cfg.Projeto = p;
                }
            }
            catch (Exception ex) { avisos?.Add("Configuração do projeto gravada no modelo não lida: " + ex.Message + ". Vale a do escritório."); }
            return cfg;
        }

        /// <summary>Grava o nível do projeto no modelo (abre a própria transação) e a cópia .pceb.json ao lado do .rvt.</summary>
        public static void GravarProjeto(Document doc, NivelValores projeto, List<string> avisos, bool copia = true)
        {
            projeto.Nome = doc.Title;
            string json = projeto.ParaJson();
            using (var t = new Transaction(doc, "Configuração do projeto (PCEB)"))
            {
                t.Start();
                var ent = new Entity(GetSchema(true));
                ent.Set(Campo, json);
                doc.ProjectInformation.SetEntity(ent);
                t.Commit();
            }
            if (!copia || string.IsNullOrEmpty(doc.PathName) || doc.IsWorkshared && !File.Exists(doc.PathName)) return;
            try
            {
                string arq = Path.Combine(Path.GetDirectoryName(doc.PathName), Path.GetFileNameWithoutExtension(doc.PathName) + ".pceb.json");
                File.WriteAllText(arq, json, new UTF8Encoding(false));
            }
            catch (Exception ex) { avisos.Add("Cópia .pceb.json não gravada: " + ex.Message); }
        }
    }
}
