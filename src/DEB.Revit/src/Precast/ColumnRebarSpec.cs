using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace StructuralDetailer.Precast
{
    /// <summary>
    /// Armadura escolhida para um grupo de pilares. Todas as medidas em mm.
    /// Serializável: os modelos salvos ficam em %AppData%\StructuralDetailer\ModelosArmaduraPilar.json.
    /// </summary>
    public sealed class ColumnRebarSpec
    {
        public string Name { get; set; } = "";

        // ---- Geral ----
        public double CoverMm { get; set; } = 25;

        // ---- Longitudinal ----
        public double LongDiameterMm { get; set; } = 12.5;
        /// <summary>Barras em cada face paralela a X (inclui os cantos).</summary>
        public int LongBarsAlongX { get; set; } = 2;
        /// <summary>Barras em cada face paralela a Y (inclui os cantos).</summary>
        public int LongBarsAlongY { get; set; } = 2;
        /// <summary>Cobrimento nas pontas (topo e base) das barras longitudinais.</summary>
        public double LongEndCoverMm { get; set; } = 25;

        // ---- Estribos ----
        public double StirrupDiameterMm { get; set; } = 5;
        public double StirrupSpacingMm { get; set; } = 150;
        public double StirrupEndOffsetMm { get; set; } = 50;
        public bool CrossTies { get; set; } = true;

        // ---- Zonas de estribos ----
        public double TopZoneLengthMm { get; set; } = 500;
        public double TopZoneSpacingMm { get; set; } = 100;
        public double BaseZoneLengthMm { get; set; } = 0;
        public double BaseZoneSpacingMm { get; set; } = 100;
        /// <summary>Quanto a zona do consolo se estende acima e abaixo dele.</summary>
        public double CorbelZoneExtensionMm { get; set; } = 200;
        public double CorbelZoneSpacingMm { get; set; } = 75;

        // ---- Consolo (NBR 9062) ----
        public bool CorbelRebar { get; set; } = true;
        public double TieDiameterMm { get; set; } = 12.5;
        public int TieCount { get; set; } = 3;
        /// <summary>Perna vertical da ponta do tirante dentro do pilar (ancoragem).</summary>
        public double TieInnerLegMm { get; set; } = 400;
        public double SewDiameterMm { get; set; } = 8;
        /// <summary>Estribos horizontais de costura, distribuídos em 2/3 d abaixo do tirante.</summary>
        public int SewCount { get; set; } = 3;

        // ---- Içamento / insertos ----
        public int LiftLoops { get; set; } = 1;
        public double LiftDiameterMm { get; set; } = 16;
        public double LiftEmbedMm { get; set; } = 800;
        public double LiftProtrusionMm { get; set; } = 150;
        public double LiftWidthMm { get; set; } = 150;
        /// <summary>Distância entre alças, quando há duas.</summary>
        public double LiftSpacingMm { get; set; } = 200;
        /// <summary>Família de inserto a posicionar no topo (vazio = nenhum). Formato "Família" ou "Família : Tipo".</summary>
        public string InsertFamily { get; set; } = "";

        public ColumnRebarSpec Clone() => (ColumnRebarSpec)MemberwiseClone();

        /// <summary>Padrão para o pilar: zona de base = embutimento; espaçamentos dentro dos limites da NBR 6118.</summary>
        internal static ColumnRebarSpec Default(PrecastColumn rep)
        {
            var s = new ColumnRebarSpec { Name = "Padrão" };
            double bMin = Math.Min(U_.ToMm(rep.Frame.Bx), U_.ToMm(rep.Frame.By));
            s.LongBarsAlongX = Math.Max(2, (int)Math.Ceiling((U_.ToMm(rep.Frame.Bx) - 2 * 40) / 300.0) + 1);
            s.LongBarsAlongY = Math.Max(2, (int)Math.Ceiling((U_.ToMm(rep.Frame.By) - 2 * 40) / 300.0) + 1);
            s.StirrupSpacingMm = Math.Floor(Math.Min(Math.Min(200, bMin), 12 * s.LongDiameterMm) / 5) * 5;
            s.BaseZoneLengthMm = rep.EmbedmentMm;
            s.CorbelRebar = rep.Corbels.Count > 0;
            return s;
        }

        // ------------------------------------------------------------------
        // Verificações (NBR 6118 / NBR 9062). Retornam mensagens; não bloqueiam a geração.

        internal List<string> Check(PrecastColumn rep)
        {
            var w = new List<string>();
            double bx = U_.ToMm(rep.Frame.Bx), by = U_.ToMm(rep.Frame.By);
            double bMin = Math.Min(bx, by);

            if (LongDiameterMm < 10) w.Add("Ø longitudinal < 10 mm (NBR 6118 18.4.2.1).");
            if (LongDiameterMm > bMin / 8) w.Add($"Ø longitudinal > 1/8 do menor lado ({bMin / 8:0.#} mm).");
            if (StirrupDiameterMm < Math.Max(5, LongDiameterMm / 4)) w.Add($"Ø estribo < max(5 mm; Ø long/4 = {LongDiameterMm / 4:0.#} mm) (18.4.3).");

            double sMax = Math.Min(Math.Min(200, bMin), 12 * LongDiameterMm);
            foreach (var (label, s) in new[] { ("corrente", StirrupSpacingMm), ("topo", TopZoneSpacingMm),
                                               ("base", BaseZoneSpacingMm), ("consolo", CorbelZoneSpacingMm) })
                if (s > sMax + 1e-6) w.Add($"Espaçamento de estribos ({label}) {s:0} mm > {sMax:0} mm = min(200; menor lado; 12 Ø long).");

            double cover = CoverMm + StirrupDiameterMm + LongDiameterMm / 2;
            double gapX = (bx - 2 * cover) / Math.Max(1, LongBarsAlongX - 1);
            double gapY = (by - 2 * cover) / Math.Max(1, LongBarsAlongY - 1);
            double gapMax = Math.Min(400, 2 * bMin);
            if (Math.Max(gapX, gapY) > gapMax + 1e-6)
                w.Add($"Espaçamento entre barras longitudinais {Math.Max(gapX, gapY):0} mm > {gapMax:0} mm = min(400; 2 × menor lado) (18.4.2.2).");
            double clear = Math.Min(gapX, gapY) - LongDiameterMm;
            if (LongBarsAlongX > 1 && LongBarsAlongY > 1 && clear < Math.Max(20, LongDiameterMm))
                w.Add($"Espaçamento livre entre barras {clear:0} mm < max(20 mm; Ø) (18.4.2.2).");

            double ac = bx * by;
            double asTot = LongBarCount * Math.PI * LongDiameterMm * LongDiameterMm / 4;
            double rho = asTot / ac * 100;
            if (rho < 0.4) w.Add($"Taxa geométrica ρ = {rho:0.00}% < 0,40% (As,mín, 17.3.5.3.1).");
            if (rho > 4.0) w.Add($"Taxa geométrica ρ = {rho:0.00}% > 4% (máximo incluindo traspasse, 17.3.5.3.2).");

            if (CorbelRebar && rep.Corbels.Count > 0)
            {
                Corbel k = rep.Corbels[0];
                double width = U_.ToMm(k.Width);
                double tieGap = (width - 2 * CoverMm - TieDiameterMm) / Math.Max(1, TieCount - 1) - TieDiameterMm;
                if (TieCount > 1 && tieGap < 20) w.Add($"Tirante do consolo: espaçamento livre {tieGap:0} mm < 20 mm.");
                if (TieCount < 2) w.Add("Tirante do consolo com menos de 2 barras.");
                if (SewCount < 2) w.Add("Costura do consolo com menos de 2 estribos horizontais (NBR 9062: As,cost ≥ 0,4 As,tir).");
                double asTie = TieCount * Math.PI * TieDiameterMm * TieDiameterMm / 4;
                double asSew = SewCount * 2 * Math.PI * SewDiameterMm * SewDiameterMm / 4;   // 2 ramos por estribo
                if (asSew < 0.4 * asTie) w.Add($"Costura {asSew:0} mm² < 0,4 × tirante ({0.4 * asTie:0} mm²) (NBR 9062).");
            }

            if (LiftLoops > 0 && LiftEmbedMm > U_.ToMm(rep.Frame.H) / 2)
                w.Add("Comprimento embutido da alça de içamento maior que metade do pilar.");
            return w;
        }

        public int LongBarCount => 2 * LongBarsAlongX + 2 * Math.Max(0, LongBarsAlongY - 2);

        // ------------------------------------------------------------------
        // Modelos salvos

        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        };

        public static string PresetsPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "StructuralDetailer", "ModelosArmaduraPilar.json");

        public static List<ColumnRebarSpec> LoadPresets()
        {
            try
            {
                if (!File.Exists(PresetsPath)) return new List<ColumnRebarSpec>();
                return JsonSerializer.Deserialize<List<ColumnRebarSpec>>(File.ReadAllText(PresetsPath), Json)
                       ?? new List<ColumnRebarSpec>();
            }
            catch
            {
                return new List<ColumnRebarSpec>();
            }
        }

        public static void SavePreset(ColumnRebarSpec spec)
        {
            List<ColumnRebarSpec> all = LoadPresets().Where(p => !string.Equals(p.Name, spec.Name, StringComparison.OrdinalIgnoreCase)).ToList();
            all.Add(spec.Clone());
            Directory.CreateDirectory(Path.GetDirectoryName(PresetsPath));
            File.WriteAllText(PresetsPath, JsonSerializer.Serialize(all.OrderBy(p => p.Name).ToList(), Json));
        }
    }
}
