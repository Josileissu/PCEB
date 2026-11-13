using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using DEB.Core.Formas;
using DEB.Core.Numeracao;
using DEB.Revit.Core;
using DEB.Revit.Presentation;

namespace DEB.Revit.Formas
{
    /// <summary>
    /// "Atualizar dados das pranchas" (D25, etapa 1): calcula e grava os textos/números SD_ que os identificadores e as tabelas
    /// mostram — seção, níveis e comprimento dos pilares, seção e elevação das vigas, dados das lajes — e a ordem natural das
    /// marcas (SD_Ordem). Os valores digitados ou importados (esforços, sobrecargas, observação) nunca são tocados; o peso próprio
    /// e o tipo da laje só são preenchidos quando estão vazios. Precisa de transação.
    /// </summary>
    internal sealed class DadosFormas
    {
        private readonly Document _doc;
        private readonly FormasSettings _cfg;
        private readonly PieceLog _log;
        private readonly CotasProjeto _cotas;
        private readonly HashSet<string> _semParametro = new HashSet<string>();
        private int _alterados, _falhas;

        public DadosFormas(Document doc, FormasSettings cfg, PieceLog log)
        {
            _doc = doc; _cfg = cfg; _log = log;
            _cotas = CotasProjeto.Ler(doc, cfg.NivelZero, log);
        }

        public void Atualizar()
        {
            int p = Pilares(), v = Vigas(), l = Lajes();
            _log.Info($"Dados atualizados: {p} pilar(es), {v} viga(s), {l} laje(s); {_alterados} valor(es) gravado(s).");
            if (_falhas > 0) _log.Warn($"{_falhas} valor(es) não puderam ser gravados (elemento em grupo, de outro usuário ou parâmetro bloqueado).");
            foreach (string n in _semParametro) _log.Warn($"Parâmetro {n} ausente em alguns elementos (não vinculado à categoria?).");
        }

        // ------------------------------------------------------------------ pilares

        private int Pilares()
        {
            List<FamilyInstance> todos = Instancias(BuiltInCategory.OST_StructuralColumns);
            List<FamilyInstance> pilares = ModeloFormas.Filtrar(_doc, BuiltInCategory.OST_StructuralColumns, _cfg.FiltroPilares, _log, "pilar");
            int fora = todos.Count - pilares.Count;
            if (fora > 0) _log.Info($"{fora} pilar(es) fora do filtro (material/família/marca) — sem dados e fora da tabela de cargas.");

            var blocos = new FilteredElementCollector(_doc).OfCategory(BuiltInCategory.OST_StructuralFoundation).WhereElementIsNotElementType()
                .Select(e => (E: e, B: e.get_BoundingBox(null))).Where(x => x.B != null).ToList();
            var semSecao = new List<string>();
            foreach (FamilyInstance c in pilares)
            {
                string marca = ModeloFormas.Marca(c);
                string secao = ModeloFormas.Secao(c, _cfg);
                if (secao == "") semSecao.Add(Nome(c));
                Texto(c, ParametrosFormas.Secao, secao);

                if (!ModeloFormas.AlturasPilar(_doc, c, out double zBase, out double zTopo)) { _log.Warn($"Pilar {Nome(c)}: base/topo não lidos."); continue; }
                Texto(c, ParametrosFormas.NivelTopo, _cotas.Nivel(zTopo));
                Texto(c, ParametrosFormas.NivelBase, _cotas.Nivel(zBase));
                Texto(c, ParametrosFormas.ComprimentoTotal, TextosFormas.Comprimento(U.ToMm(zTopo - zBase) / 1000));
                double? topoBloco = TopoDoBloco(c, zBase, zTopo, blocos);
                Texto(c, ParametrosFormas.NivelTopoBloco, topoBloco == null ? "" : _cotas.Nivel(topoBloco.Value));
            }
            if (semSecao.Count > 0)
                _log.Warn($"Seção não encontrada (pilar circular ou família sem os parâmetros {string.Join("/", _cfg.ParametrosLargura)} e " +
                          $"{string.Join("/", _cfg.ParametrosAltura)}): SD_Secao vazio em {Lista(semSecao)}.");

            // ordem natural das marcas; os de fora do filtro saem da tabela (SD_Ordem = 0)
            Dictionary<string, int> pos = ComparadorNatural.Posicoes(pilares.Select(ModeloFormas.Marca));
            var tratados = new HashSet<ElementId>(pilares.Select(c => c.Id));
            foreach (FamilyInstance c in todos)
            {
                string m = ModeloFormas.Marca(c);
                int ordem = tratados.Contains(c.Id) && m != "" && pos.TryGetValue(m, out int o) ? o : 0;
                Inteiro(c, ParametrosFormas.Ordem, ordem, soSePositivo: ordem == 0);
            }
            return pilares.Count;
        }

        /// <summary>Topo do bloco sob o pilar: a fundação cuja caixa contém o eixo do pilar, com o topo entre a base e o topo do
        /// pilar (o bloco com cálice abraça a base; estacas ficam abaixo e não contam). Várias: a mais alta.</summary>
        private static double? TopoDoBloco(FamilyInstance c, double zBase, double zTopo, List<(Element E, BoundingBoxXYZ B)> blocos)
        {
            XYZ p = ModeloFormas.Eixo(c);
            if (p == null) return null;
            double tol = U.Mm(10), abaixo = U.Mm(50);
            var cand = blocos.Where(b => p.X >= b.B.Min.X - tol && p.X <= b.B.Max.X + tol && p.Y >= b.B.Min.Y - tol && p.Y <= b.B.Max.Y + tol &&
                                         b.B.Max.Z >= zBase - abaixo && b.B.Max.Z <= zTopo).ToList();
            return cand.Count == 0 ? (double?)null : cand.Max(b => b.B.Max.Z);
        }

        // ------------------------------------------------------------------ vigas

        private int Vigas()
        {
            List<FamilyInstance> vigas = ModeloFormas.Filtrar(_doc, BuiltInCategory.OST_StructuralFraming, _cfg.FiltroVigas, _log, "viga");
            var semSecao = new List<string>();
            foreach (FamilyInstance b in vigas)
            {
                string secao = ModeloFormas.Secao(b, _cfg);
                if (secao == "") semSecao.Add(Nome(b));
                Texto(b, ParametrosFormas.Secao, secao);
                Level lvl = ModeloFormas.NivelDaViga(_doc, b);
                double? topo = ModeloFormas.TopoDaViga(b, _cfg);
                if (lvl == null || topo == null) { _log.Warn($"Viga {Nome(b)}: sem nível de referência ou eixo; SD_Elevacao não calculado."); continue; }
                Texto(b, ParametrosFormas.Elevacao, TextosFormas.Elevacao(U.ToMm(topo.Value - lvl.ProjectElevation) / 10));
            }
            if (semSecao.Count > 0) _log.Warn($"Seção não encontrada nas vigas: {Lista(semSecao)}.");
            return vigas.Count;
        }

        // ------------------------------------------------------------------ lajes

        private int Lajes()
        {
            List<Floor> pisos = new FilteredElementCollector(_doc).OfClass(typeof(Floor)).Cast<Floor>().ToList();
            List<Floor> lajes = pisos.Where(f => ModeloFormas.Marca(f) != "").ToList();
            foreach (Floor f in lajes)
            {
                Level lvl = _doc.GetElement(f.LevelId) as Level;
                double esp = U.ToMm(f.get_Parameter(BuiltInParameter.FLOOR_ATTR_THICKNESS_PARAM)?.AsDouble() ?? 0) / 10;
                double desl = f.get_Parameter(BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM)?.AsDouble() ?? 0;
                Numero(f, ParametrosFormas.LajeAltura, TextosFormas.Arred(esp, 0.1), soSeVazio: false);
                Numero(f, ParametrosFormas.LajeElevacao, TextosFormas.Arred(U.ToMm(desl) / 10, 0.1), soSeVazio: false);
                if (lvl != null) Numero(f, ParametrosFormas.LajeNivel, TextosFormas.Arred(_cotas.RelativoCm(lvl.ProjectElevation + desl), 0.1), soSeVazio: false);
                string tipo = _doc.GetElement(f.GetTypeId())?.Name ?? "";
                Texto(f, ParametrosFormas.LajeTipo, tipo, soSeVazio: true);
                double pp = _cfg.PesoProprioPorTipo != null && _cfg.PesoProprioPorTipo.TryGetValue(tipo, out double ppTipo)
                    ? ppTipo : TextosFormas.PesoProprio(esp, _cfg.DensidadeConcretoKgfM3);
                Numero(f, ParametrosFormas.LajePesoProprio, pp, soSeVazio: true);
            }
            Dictionary<string, int> pos = ComparadorNatural.Posicoes(lajes.Select(ModeloFormas.Marca));
            foreach (Floor f in pisos)
            {
                string m = ModeloFormas.Marca(f);
                int ordem = m != "" && pos.TryGetValue(m, out int o) ? o : 0;
                Inteiro(f, ParametrosFormas.Ordem, ordem, soSePositivo: ordem == 0);
            }
            return lajes.Count;
        }

        // ------------------------------------------------------------------ gravação

        private List<FamilyInstance> Instancias(BuiltInCategory cat) =>
            new FilteredElementCollector(_doc).OfCategory(cat).WhereElementIsNotElementType().OfType<FamilyInstance>()
                .Where(f => f.SuperComponent == null).ToList();

        private Parameter Param(Element e, ParametrosFormas.Def d)
        {
            Parameter p = e.get_Parameter(d.Guid);
            if (p == null) _semParametro.Add(d.Nome);
            return p;
        }

        private void Texto(Element e, ParametrosFormas.Def d, string valor, bool soSeVazio = false)
        {
            Parameter p = Param(e, d);
            if (p == null) return;
            string atual = p.AsString() ?? "";
            if (atual == (valor ?? "") || (soSeVazio && atual.Trim() != "")) return;
            Set(p, () => p.Set(valor ?? ""));
        }

        private void Numero(Element e, ParametrosFormas.Def d, double valor, bool soSeVazio)
        {
            Parameter p = Param(e, d);
            if (p == null) return;
            bool vazio = !p.HasValue || Math.Abs(p.AsDouble()) < 1e-9;
            if ((soSeVazio && !vazio) || (p.HasValue && Math.Abs(p.AsDouble() - valor) < 1e-6)) return;
            Set(p, () => p.Set(valor));
        }

        /// <summary>soSePositivo: zerar só quem tem valor (não "suja" milhares de elementos que nunca tiveram ordem).</summary>
        private void Inteiro(Element e, ParametrosFormas.Def d, int valor, bool soSePositivo)
        {
            Parameter p = e.get_Parameter(d.Guid);
            if (p == null) { if (!soSePositivo) _semParametro.Add(d.Nome); return; }
            if (p.AsInteger() == valor && (p.HasValue || soSePositivo)) return;
            Set(p, () => p.Set(valor));
        }

        private void Set(Parameter p, Func<bool> set)
        {
            try
            {
                if (p.IsReadOnly || !set()) _falhas++;
                else _alterados++;
            }
            catch { _falhas++; }
        }

        private static string Nome(Element e) { string m = ModeloFormas.Marca(e); return m != "" ? m : "id " + e.Id.Value; }

        private static string Lista(List<string> l) => string.Join(", ", l.Take(15)) + (l.Count > 15 ? $" (+{l.Count - 15})" : "");
    }

    /// <summary>
    /// Níveis relativos e cotas absolutas: relativo = ao zero do projeto (ponto base do projeto ou o nível do padrão); absoluto =
    /// coordenadas compartilhadas (ponto de levantamento). Z interno do Revit em pés.
    /// </summary>
    internal sealed class CotasProjeto
    {
        private double _zZero, _zBase, _zBaseCompartilhado;

        public static CotasProjeto Ler(Document doc, string nivelZero, PieceLog log)
        {
            var c = new CotasProjeto();
            try
            {
                BasePoint pbp = BasePoint.GetProjectBasePoint(doc);
                c._zBase = pbp.Position.Z;
                c._zBaseCompartilhado = pbp.SharedPosition.Z;
            }
            catch (Exception ex) { log.Warn($"Ponto base do projeto não lido ({ex.Message}); cotas absolutas = relativas."); }
            c._zZero = c._zBase;
            if (!string.IsNullOrWhiteSpace(nivelZero))
            {
                Level z = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                    .FirstOrDefault(l => string.Equals(l.Name, nivelZero.Trim(), StringComparison.OrdinalIgnoreCase));
                if (z != null) c._zZero = z.ProjectElevation;
                else log.Warn($"Nível zero \"{nivelZero}\" não existe; usado o ponto base do projeto.");
            }
            return c;
        }

        public double RelativoM(double z) => U.ToMm(z - _zZero) / 1000;
        public double RelativoCm(double z) => U.ToMm(z - _zZero) / 10;
        public double AbsolutoM(double z) => U.ToMm(z - _zBase + _zBaseCompartilhado) / 1000;
        public string Nivel(double z) => TextosFormas.Nivel(RelativoM(z), AbsolutoM(z));
    }
}
