using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using DEB.Core.Configuracao.Projeto;
using DEB.Core.Elementos.Contencao;

namespace DEB.Revit.Contencao
{
    /// <summary>
    /// Muro de arrimo no Revit (D47): lê a parede e a sapata corrida (WallFoundation) para a geometria do muro e guarda os dados do
    /// cálculo na parede (Extensible Storage). O Revit só é lido e recebe os dados aceitos; a geometria não é alterada nesta etapa.
    /// Convenção: o solo contido fica do lado interno da parede — o lado do talão (heel) da sapata de arrimo do Revit.
    /// </summary>
    internal static class MuroRevit
    {
        // GUID fixo: gravado nos modelos (nunca mudar)
        private static readonly Guid SchemaId = new Guid("5b0f4c2e-7d1a-4c39-9a8e-2f6d0c3b8e17");
        private const string Campo = "Json";
        private const double Pe = 0.3048;

        private static Schema GetSchema(bool criar)
        {
            Schema s = Schema.Lookup(SchemaId);
            if (s != null || !criar) return s;
            var b = new SchemaBuilder(SchemaId);
            b.SetSchemaName("PCEB_MuroArrimo");
            b.SetDocumentation("PCEB: dados do muro de arrimo em balanço (D47) em JSON.");
            b.SetReadAccessLevel(AccessLevel.Public);
            b.SetWriteAccessLevel(AccessLevel.Public);
            b.AddSimpleField(Campo, typeof(string));
            return b.Finish();
        }

        /// <summary>A sapata corrida ligada à parede (null = sem sapata).</summary>
        public static WallFoundation Sapata(Wall w) =>
            new FilteredElementCollector(w.Document).OfClass(typeof(WallFoundation)).Cast<WallFoundation>().FirstOrDefault(f => f.WallId == w.Id);

        /// <summary>Dados do muro: os gravados na parede, ou a geometria lida da parede e da sapata sobre a configuração do projeto.</summary>
        public static DadosMuro Ler(Wall w, ConfiguracaoProjeto cfg, List<string> notas)
        {
            var partida = DadosMuro.DaConfiguracao(cfg);
            try
            {
                Schema s = GetSchema(false);
                Entity ent = s == null ? null : w.GetEntity(s);
                string json = ent != null && ent.IsValid() ? ent.Get<string>(Campo) : null;
                if (!string.IsNullOrWhiteSpace(json)) { notas.Add("Dados do muro gravados na parede (aplicados antes)."); return SessaoMuro.DadosDeJson(json, partida); }
            }
            catch (Exception ex) { notas.Add("Dados gravados na parede não lidos: " + ex.Message); }

            // geometria: espessura e altura da parede; sapata de arrimo (ponta/talão) ou de apoio (centrada)
            double t = w.Width * Pe;
            BoundingBoxXYZ bb = w.get_BoundingBox(null);
            double altura = bb != null ? (bb.Max.Z - bb.Min.Z) * Pe : 3.0;
            partida.EspessuraTopo = partida.EspessuraBase = Math.Round(t, 3);
            partida.AlturaFuste = Math.Round(altura, 3);
            WallFoundation sap = Sapata(w);
            if (sap == null) { notas.Add("A parede não tem sapata corrida: ponta, talão e sapata ficam com os valores de partida — ajuste na janela."); return partida; }
            if (w.Document.GetElement(sap.GetTypeId()) is WallFoundationType tipo)
            {
                double esp = tipo.get_Parameter(BuiltInParameter.STRUCTURAL_FOUNDATION_THICKNESS)?.AsDouble() ?? 0;
                if (esp > 0) partida.EspessuraSapata = Math.Round(esp * Pe, 3);
                if (DeArrimo(tipo))
                {
                    partida.Ponta = Math.Round(Comp(tipo, BuiltInParameter.CONTINUOUS_FOOTING_TOP_TOE, BuiltInParameter.CONTINUOUS_FOOTING_BOTTOM_TOE) * Pe, 3);
                    partida.Talao = Math.Round(Comp(tipo, BuiltInParameter.CONTINUOUS_FOOTING_TOP_HEEL, BuiltInParameter.CONTINUOUS_FOOTING_BOTTOM_HEEL) * Pe, 3);
                }
                else
                {
                    double largura = Comp(tipo, BuiltInParameter.CONTINUOUS_FOOTING_WIDTH, BuiltInParameter.CONTINUOUS_FOOTING_BEARING_WIDTH);
                    double balanco = Math.Max(0, (largura * Pe - t) / 2);
                    partida.Ponta = partida.Talao = Math.Round(balanco, 3);
                    notas.Add($"Sapata \"{tipo.Name}\" é de apoio (centrada): ponta = talão = {balanco:0.00} m. Use uma sapata de arrimo para ponta e talão diferentes.");
                }
            }
            // a parede começa no topo da sapata: altura do fuste = altura da parede
            return partida;
        }

        /// <summary>Sapata corrida de arrimo (uso estrutural "Arrimo"/"Retaining"), e não de apoio.</summary>
        public static bool DeArrimo(WallFoundationType tipo)
        {
            string u = tipo.get_Parameter(BuiltInParameter.CONTINUOUS_FOOTING_STRUCTURAL_USAGE)?.AsValueString() ?? "";
            // "Retaining" (inglês), "Retenção"/"Contenção"/"Arrimo" (português)
            return new[] { "etain", "etenç", "ontenç", "rrimo" }.Any(x => u.IndexOf(x, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        /// <summary>O maior dos parâmetros de comprimento (pés) — a ponta e o talão têm valor em cima e embaixo.</summary>
        private static double Comp(Element e, params BuiltInParameter[] ps) =>
            ps.Select(p => e.get_Parameter(p)).Where(p => p != null && p.StorageType == StorageType.Double).Select(p => p.AsDouble()).DefaultIfEmpty(0).Max();

        /// <summary>Dados do muro gravados num elemento qualquer (muro PCEB); sem dados: os de partida da configuração.</summary>
        public static DadosMuro LerElemento(Element e, ConfiguracaoProjeto cfg, List<string> notas)
        {
            if (e is Wall w) return Ler(w, cfg, notas);
            var partida = DadosMuro.DaConfiguracao(cfg);
            try
            {
                Schema s = GetSchema(false);
                Entity ent = s == null ? null : e.GetEntity(s);
                string json = ent != null && ent.IsValid() ? ent.Get<string>(Campo) : null;
                if (!string.IsNullOrWhiteSpace(json))
                {
                    var d = SessaoMuro.DadosDeJson(json, partida);
                    // o comprimento vale o da instância (pode ter sido mudado no Revit)
                    var pc = e.LookupParameter(FamiliaMuro.PComp);
                    if (pc != null && pc.StorageType == StorageType.Double && pc.AsDouble() > 0) d.Comprimento = Math.Round(pc.AsDouble() * Pe, 3);
                    return d;
                }
            }
            catch (Exception ex) { notas.Add("Dados gravados no muro não lidos: " + ex.Message); }
            notas.Add("O elemento não tem dados de muro gravados: valores de partida da configuração.");
            return partida;
        }

        /// <summary>Grava os dados no elemento (dentro de uma transação aberta).</summary>
        public static void GravarNoElemento(Element e, DadosMuro d)
        {
            var ent = new Entity(GetSchema(true));
            ent.Set(Campo, SessaoMuro.DadosParaJson(d));
            e.SetEntity(ent);
        }

        /// <summary>Grava os dados aceitos na parede (abre a própria transação).</summary>
        public static void Gravar(Wall w, DadosMuro d)
        {
            using (var t = new Transaction(w.Document, "Muro de arrimo (PCEB)"))
            {
                t.Start();
                var ent = new Entity(GetSchema(true));
                ent.Set(Campo, SessaoMuro.DadosParaJson(d));
                w.SetEntity(ent);
                t.Commit();
            }
        }
    }
}
