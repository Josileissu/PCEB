using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using DEB.Revit.Core;
using DEB.Revit.Presentation;

namespace DEB.Revit.Formas
{
    /// <summary>
    /// Tabelas das pranchas de formas (D25, etapa 2), tabelas do Revit lendo os parâmetros SD_:
    ///  • "Cargas na fundação" (pilares): Nome | Carga máx | Carga mín | Mx +/− | My +/− | Fx +/− | Fy +/−, cabeçalhos agrupados;
    ///  • "Lajes" (pisos de um nível): Nome | Tipo | Altura | Elevação | Nível | Peso próprio | Sobrecarga: Adicional/Acidental/Localizada.
    /// Ordem natural das marcas por PCEB_Ordem (coluna escondida; o Revit ordena texto caractere a caractere), que também filtra
    /// (&gt; 0 = peça tratada pelo "Atualizar dados"). A tabela é achada pela marca do comando: rodar de novo refaz as colunas da
    /// mesma tabela (continua nas folhas), sem duplicar. Precisa de transação.
    /// </summary>
    internal sealed class TabelasFormas
    {
        private readonly Document _doc;
        private readonly FormasSettings _cfg;
        private readonly PieceLog _log;

        public TabelasFormas(Document doc, FormasSettings cfg, PieceLog log) { _doc = doc; _cfg = cfg; _log = log; }

        public const string PapelCargas = "tabela-cargas";
        public static string PapelLajes(Level l) => "tabela-lajes:" + l.UniqueId;

        public ViewSchedule Cargas()
        {
            ViewSchedule vs = Obter(PapelCargas, BuiltInCategory.OST_StructuralColumns, _cfg.NomeTabelaCargas);
            ScheduleDefinition def = Limpar(vs);
            Campo(def, new ElementId(BuiltInParameter.ALL_MODEL_MARK), "Nome");
            Campo(def, ParametrosFormas.Esforcos[0], "Carga máx\n(tf)");
            Campo(def, ParametrosFormas.Esforcos[1], "Carga mín\n(tf)");
            for (int k = 2; k < ParametrosFormas.Esforcos.Length; k++)
                Campo(def, ParametrosFormas.Esforcos[k], k % 2 == 0 ? "Positivo" : "Negativo");
            Ordem(def, true);
            Titulo(vs, _cfg.TituloTabelaCargas);
            Agrupar(vs, new[] { (3, 4, "Mx Máximo (tf.m)"), (5, 6, "My Máximo (tf.m)"), (7, 8, "Fx Máximo (tf)"), (9, 10, "Fy Máximo (tf)") });
            Estilos(vs);
            return vs;
        }

        public ViewSchedule Lajes(Level nivel)
        {
            string nome = Fill(_cfg.NomeTabelaLajes, nivel);
            ViewSchedule vs = Obter(PapelLajes(nivel), BuiltInCategory.OST_Floors, nome);
            ScheduleDefinition def = Limpar(vs);
            Campo(def, new ElementId(BuiltInParameter.ALL_MODEL_MARK), "Nome");
            Campo(def, ParametrosFormas.LajeTipo, "Tipo");
            Campo(def, ParametrosFormas.LajeAltura, "Altura\n(cm)");
            Campo(def, ParametrosFormas.LajeElevacao, "Elevação\n(cm)");
            Campo(def, ParametrosFormas.LajeNivel, "Nível\n(cm)");
            Campo(def, ParametrosFormas.LajePesoProprio, "Peso próprio\n(kgf/m²)");
            Campo(def, ParametrosFormas.LajeAdicional, "Adicional");
            Campo(def, ParametrosFormas.LajeAcidental, "Acidental");
            Campo(def, ParametrosFormas.LajeLocalizada, "Localizada");
            Ordem(def, false);
            // filtro pelo nível (coluna escondida)
            SchedulableField sfNivel = def.GetSchedulableFields().FirstOrDefault(f =>
                f.ParameterId == new ElementId(BuiltInParameter.LEVEL_PARAM) || f.ParameterId == new ElementId(BuiltInParameter.SCHEDULE_LEVEL_PARAM));
            var esperadas = new HashSet<string>(new FilteredElementCollector(_doc).OfClass(typeof(Floor)).Cast<Floor>()
                .Where(f => f.LevelId == nivel.Id && (f.get_Parameter(ParametrosFormas.Ordem.Guid)?.AsInteger() ?? 0) > 0)
                .Select(ModeloFormas.Marca), StringComparer.OrdinalIgnoreCase);
            if (sfNivel != null)
            {
                ScheduleField fn = def.AddField(sfNivel);
                fn.IsHidden = true;
                // filtro pelo nível: pelo id; se o Revit não casar (tabela vazia com lajes no nível), pelo nome; senão sem filtro
                var tentativas = new List<Func<ScheduleFilter>>
                {
                    () => new ScheduleFilter(fn.FieldId, ScheduleFilterType.Equal, nivel.Id),
                    () => new ScheduleFilter(fn.FieldId, ScheduleFilterType.Equal, nivel.Name),
                };
                bool ok = false;
                foreach (var t in tentativas)
                {
                    int n = def.GetFilterCount();
                    try { def.AddFilter(t()); } catch { continue; }
                    if (esperadas.Count == 0 || Linhas(vs, esperadas) > 0) { ok = true; break; }
                    def.RemoveFilter(n);
                }
                if (!ok) _log.Warn($"Tabela \"{nome}\": o filtro pelo nível não funcionou; a tabela lista as lajes de todos os níveis.");
            }
            else _log.Warn($"Tabela \"{nome}\": campo de nível não encontrado; a tabela lista as lajes de todos os níveis.");
            _log.Info($"Tabela \"{nome}\": {Linhas(vs, esperadas)} de {esperadas.Count} laje(s) com marca do nível.");
            Titulo(vs, Fill(_cfg.TituloTabelaLajes, nivel));
            Agrupar(vs, new[] { (6, 8, "Sobrecarga (kgf/m²)") });
            Estilos(vs);
            return vs;
        }

        public static string PapelPaineis(Level l) => "tabela-paineis:" + l.UniqueId;

        /// <summary>Painéis alveolares do nível (quadro estrutural com marca do prefixo do filtro): Painel | Tipo | Comprimento.</summary>
        public ViewSchedule Paineis(Level nivel)
        {
            string nome = Fill(_cfg.NomeTabelaPaineis, nivel);
            ViewSchedule vs = Obter(PapelPaineis(nivel), BuiltInCategory.OST_StructuralFraming, nome);
            // resumida (uma linha por tipo e comprimento, com a quantidade): a lista painel a painel passava da folha; a marca de cada
            // painel está na planta
            ScheduleDefinition def = Limpar(vs);
            def.IsItemized = false;
            ScheduleField ft = Campo(def, new ElementId(BuiltInParameter.ELEM_TYPE_PARAM), "Tipo");
            ScheduleField fc = Campo(def, new ElementId(BuiltInParameter.STRUCTURAL_FRAME_CUT_LENGTH), "Comprimento\n(cm)");
            if (fc != null) try { fc.SetFormatOptions(new FormatOptions(UnitTypeId.Centimeters) { UseDefault = false, Accuracy = 0.1 }); } catch { }
            try { def.AddField(ScheduleFieldType.Count).ColumnHeading = "Quantidade"; } catch { }
            SchedulableField sfm = def.GetSchedulableFields().FirstOrDefault(f => f.ParameterId == new ElementId(BuiltInParameter.ALL_MODEL_MARK));
            if (sfm != null)
            {
                ScheduleField fm = def.AddField(sfm);
                fm.IsHidden = true;
                string pre = _cfg.FiltroPaineis?.PrefixoMarca?.Trim() ?? "";
                if (pre != "") def.AddFilter(new ScheduleFilter(fm.FieldId, ScheduleFilterType.BeginsWith, pre));
            }
            if (ft != null) def.AddSortGroupField(new ScheduleSortGroupField(ft.FieldId, ScheduleSortOrder.Ascending));
            if (fc != null) def.AddSortGroupField(new ScheduleSortGroupField(fc.FieldId, ScheduleSortOrder.Descending));
            def.ShowGrandTotal = true;
            def.ShowGrandTotalTitle = true;
            def.ShowGrandTotalCount = true;
            SchedulableField sfn = def.GetSchedulableFields().FirstOrDefault(f => f.ParameterId == new ElementId(BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM));
            if (sfn != null)
            {
                ScheduleField fn = def.AddField(sfn);
                fn.IsHidden = true;
                def.AddFilter(new ScheduleFilter(fn.FieldId, ScheduleFilterType.Equal, nivel.Id));
            }
            Titulo(vs, Fill(_cfg.TituloTabelaPaineis, nivel));
            Estilos(vs);
            return vs;
        }

        /// <summary>"Seções consideradas": uma linha por tipo (seção PCEB_Secao, tipo, quantidade, material) das vigas ou dos pilares
        /// tratados pelo "Atualizar dados" (os que têm PCEB_Secao).</summary>
        public ViewSchedule Secoes(bool vigas)
        {
            BuiltInCategory cat = vigas ? BuiltInCategory.OST_StructuralFraming : BuiltInCategory.OST_StructuralColumns;
            ViewSchedule vs = Obter(vigas ? "tabela-secoes-vigas" : "tabela-secoes-pilares", cat,
                vigas ? "PCEB - SEÇÕES CONSIDERADAS - VIGAS" : "PCEB - SEÇÕES CONSIDERADAS - PILARES");
            ScheduleDefinition def = Limpar(vs);
            def.IsItemized = false;
            ScheduleField fs = Campo(def, ParametrosFormas.Secao, "Seção\n(cm)");
            ScheduleField ft = Campo(def, new ElementId(BuiltInParameter.ELEM_FAMILY_AND_TYPE_PARAM), "Família e tipo");
            if (ft != null) try { ft.GridColumnWidth = U.Mm(110); } catch { /* largura do Revit */ }
            try { def.AddField(ScheduleFieldType.Count).ColumnHeading = "Quantidade"; } catch { }
            Campo(def, new ElementId(BuiltInParameter.STRUCTURAL_MATERIAL_PARAM), "Material");
            if (fs != null)
            {
                try { def.AddFilter(new ScheduleFilter(fs.FieldId, ScheduleFilterType.HasValue)); }
                catch { try { def.AddFilter(new ScheduleFilter(fs.FieldId, ScheduleFilterType.NotEqual, "")); } catch { } }
                def.AddSortGroupField(new ScheduleSortGroupField(fs.FieldId, ScheduleSortOrder.Ascending));
            }
            if (ft != null) def.AddSortGroupField(new ScheduleSortGroupField(ft.FieldId, ScheduleSortOrder.Ascending));
            def.ShowGrandTotal = true;
            def.ShowGrandTotalTitle = true;
            def.ShowGrandTotalCount = true;
            Titulo(vs, vigas ? "SEÇÕES CONSIDERADAS - VIGAS" : "SEÇÕES CONSIDERADAS - PILARES");
            Estilos(vs);
            return vs;
        }

        // ------------------------------------------------------------------

        /// <summary>Quantas linhas do corpo da tabela têm, na 1ª coluna, uma das marcas dadas (depois de regenerar).</summary>
        private int Linhas(ViewSchedule vs, HashSet<string> marcas)
        {
            try
            {
                _doc.Regenerate();
                TableSectionData b = vs.GetTableData().GetSectionData(SectionType.Body);
                int n = 0;
                for (int r = b.FirstRowNumber; r <= b.LastRowNumber; r++)
                    if (marcas.Contains(vs.GetCellText(SectionType.Body, r, b.FirstColumnNumber) ?? "")) n++;
                return n;
            }
            catch { return -1; }
        }

        public static string Fill(string s, Level l) => (s ?? "").Replace("{Nivel}", l?.Name ?? "");

        /// <summary>Tabela do comando com o papel, ou uma nova (nome único; avisa se o nome pedido já é de outra tabela).</summary>
        private ViewSchedule Obter(string papel, BuiltInCategory cat, string nome)
        {
            ViewSchedule vs = MarcaFormas.Com(new FilteredElementCollector(_doc).OfClass(typeof(ViewSchedule)), papel).OfType<ViewSchedule>().FirstOrDefault();
            if (vs != null) { _log.Info($"Tabela \"{vs.Name}\" atualizada."); return vs; }
            vs = ViewSchedule.CreateSchedule(_doc, new ElementId(cat));
            string limpo = ModeloFormas.Nome(nome);
            string unico = NameRegistry.ForViews(_doc).Unique(limpo);
            if (unico != limpo) _log.Warn($"Já existe uma vista \"{limpo}\" que não é do comando; a tabela nova se chama \"{unico}\".");
            vs.Name = unico;
            MarcaFormas.Marcar(vs, papel);
            _log.Info($"Tabela \"{unico}\" criada.");
            return vs;
        }

        private static ScheduleDefinition Limpar(ViewSchedule vs)
        {
            ScheduleDefinition def = vs.Definition;
            def.ClearSortGroupFields();
            def.ClearFilters();
            def.ClearFields();
            def.IsItemized = true;
            def.ShowGrandTotal = false;
            return def;
        }

        private ScheduleField Campo(ScheduleDefinition def, ParametrosFormas.Def d, string titulo)
        {
            ElementId id = ParametrosFormas.Id(_doc, d);
            if (id == ElementId.InvalidElementId) { _log.Warn($"Tabela: parâmetro {d.Nome} não existe no projeto (coluna \"{titulo.Replace('\n', ' ')}\" fora)."); return null; }
            return Campo(def, id, titulo);
        }

        private ScheduleField Campo(ScheduleDefinition def, ElementId paramId, string titulo)
        {
            SchedulableField sf = def.GetSchedulableFields().FirstOrDefault(f => f.ParameterId == paramId);
            if (sf == null) { _log.Warn($"Tabela: coluna \"{titulo.Replace('\n', ' ')}\" não disponível na categoria."); return null; }
            ScheduleField f = def.AddField(sf);
            f.ColumnHeading = titulo;
            try { f.HorizontalAlignment = ScheduleHorizontalAlignment.Center; } catch { /* alinhamento do Revit */ }
            return f;
        }

        /// <summary>PCEB_Ordem escondido no fim (não muda os índices das colunas visíveis): filtra (&gt; 0) e ordena; desempate pela marca.</summary>
        private void Ordem(ScheduleDefinition def, bool pilares)
        {
            ScheduleField fo = Campo(def, ParametrosFormas.Ordem, "Ordem");
            ScheduleField fm = def.GetFieldCount() > 0 ? def.GetField(0) : null;   // Nome (marca)
            if (fo != null)
            {
                fo.IsHidden = true;
                def.AddFilter(new ScheduleFilter(fo.FieldId, ScheduleFilterType.GreaterThan, 0));
                def.AddSortGroupField(new ScheduleSortGroupField(fo.FieldId, ScheduleSortOrder.Ascending));
            }
            else _log.Warn($"Tabela de {(pilares ? "cargas" : "lajes")}: sem PCEB_Ordem, ordenada pela marca do Revit (P10 antes de P2) e sem filtro.");
            if (fm != null) def.AddSortGroupField(new ScheduleSortGroupField(fm.FieldId, ScheduleSortOrder.Ascending));
        }

        private void Titulo(ViewSchedule vs, string titulo)
        {
            try
            {
                TableSectionData h = vs.GetTableData().GetSectionData(SectionType.Header);
                if (h != null && h.NumberOfRows > 0) h.SetCellText(h.FirstRowNumber, h.FirstColumnNumber, titulo);
            }
            catch (Exception ex) { _log.Warn($"Título da tabela não gravado: {ex.Message}"); }
        }

        /// <summary>
        /// Cabeçalhos agrupados (colunas visíveis, a partir de 0). A API agrupa por linha/coluna das células de cabeçalho; depois do
        /// 1º grupo a linha dos títulos muda, então cada grupo procura a primeira faixa de linhas que o Revit aceita.
        /// </summary>
        private void Agrupar(ViewSchedule vs, IEnumerable<(int De, int Ate, string Titulo)> grupos)
        {
            foreach (var g in grupos)
            {
                bool ok = false;
                for (int top = 0; top <= 2 && !ok; top++)
                    for (int bottom = top; bottom <= 2 && !ok; bottom++)
                    {
                        try
                        {
                            if (!vs.CanGroupHeaders(top, g.De, bottom, g.Ate)) continue;
                            vs.GroupHeaders(top, g.De, bottom, g.Ate, g.Titulo);
                            ok = true;
                        }
                        catch { /* tenta a próxima faixa */ }
                    }
                if (!ok) _log.Warn($"Tabela \"{vs.Name}\": cabeçalho \"{g.Titulo}\" não agrupado (o Revit não aceitou as células).");
            }
        }

        /// <summary>Tipos de texto do padrão (pelo nome); nome que não existe = aviso, fica o do Revit.</summary>
        private void Estilos(ViewSchedule vs)
        {
            void Aplicar(string nome, Action<ElementId> set, string onde)
            {
                if (string.IsNullOrWhiteSpace(nome)) return;
                TextNoteType t = new FilteredElementCollector(_doc).OfClass(typeof(TextNoteType)).Cast<TextNoteType>()
                    .FirstOrDefault(x => string.Equals(x.Name, nome.Trim(), StringComparison.OrdinalIgnoreCase));
                if (t == null) { _log.Warn($"Tipo de texto \"{nome}\" ({onde} das tabelas) não existe no projeto; ficou o do Revit."); return; }
                try { set(t.Id); } catch (Exception ex) { _log.Warn($"Tipo de texto \"{nome}\" não aplicado: {ex.Message}"); }
            }
            Aplicar(_cfg.TextoTabelaCorpo, id => vs.BodyTextTypeId = id, "corpo");
            Aplicar(_cfg.TextoTabelaCabecalho, id => vs.HeaderTextTypeId = id, "cabeçalho");
            Aplicar(_cfg.TextoTabelaTitulo, id => vs.TitleTextTypeId = id, "título");
        }
    }
}
