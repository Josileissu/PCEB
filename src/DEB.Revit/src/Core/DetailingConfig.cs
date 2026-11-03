using System;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace DEB.Revit.Core
{
    /// <summary>
    /// Configuração do escritório. Fica em %AppData%\PCEB\DEB.Revit\DetalhamentoConfig.json
    /// e é criada com os valores padrão na primeira execução.
    /// Escalas são o denominador (25 = 1:25). Medidas de folha em mm de papel.
    /// </summary>
    public sealed class DetailingConfig
    {
        // ---------- Escalas ----------
        public int BeamElevationScale { get; set; } = 25;
        public int ColumnElevationScale { get; set; } = 25;
        public int SectionScale { get; set; } = 10;
        public int View3DScale { get; set; } = 25;

        /// <summary>Escalas usadas, em ordem, quando as vistas não cabem na folha.</summary>
        public int[] ScaleLadder { get; set; } = { 5, 10, 20, 25, 50, 75, 100 };

        // ---------- Templates de vista (nome exato no projeto; vazio = não aplica) ----------
        public string ElevationViewTemplate { get; set; } = "";
        public string SectionViewTemplate { get; set; } = "";
        public string View3DViewTemplate { get; set; } = "";

        // ---------- Folha ----------
        /// <summary>Trecho do nome "Família Tipo" do carimbo (ex.: "A1"). Vazio = primeiro carimbo do projeto.</summary>
        public string TitleBlockName { get; set; } = "";
        public string SheetNumberPrefix { get; set; } = "DET-";
        public string SheetNamePrefix { get; set; } = "DETALHAMENTO - ";

        public double SheetMarginLeftMm { get; set; } = 25;
        public double SheetMarginRightMm { get; set; } = 10;
        public double SheetMarginTopMm { get; set; } = 10;
        public double SheetMarginBottomMm { get; set; } = 10;

        /// <summary>Área reservada ao carimbo no canto inferior direito (dentro das margens).</summary>
        public double TitleBlockStampWidthMm { get; set; } = 180;
        public double TitleBlockStampHeightMm { get; set; } = 60;

        public double ViewportGapMm { get; set; } = 15;
        /// <summary>Espaço abaixo de cada vista para o título da viewport.</summary>
        public double ViewportLabelAllowanceMm { get; set; } = 15;

        /// <summary>Usado só se o projeto não tiver nenhum carimbo carregado (A1 = 841 x 594).</summary>
        public double DefaultSheetWidthMm { get; set; } = 841;
        public double DefaultSheetHeightMm { get; set; } = 594;

        // ---------- Cotas ----------
        /// <summary>Distância (mm de papel) da linha de cota até a face da peça.</summary>
        public double DimensionOffsetMm { get; set; } = 10;

        /// <summary>Folga (mm do modelo) entre a peça e o recorte de cada elevação/corte.</summary>
        public double CropMarginMm { get; set; } = 300;

        // ---------- Cortes ----------
        /// <summary>Distância do corte de apoio da viga à extremidade, em mm. 0 = usar a altura da viga.</summary>
        public double BeamSupportSectionDistanceMm { get; set; } = 0;

        // ---------- Armadura de pilar (NBR 6118) ----------
        public bool ColumnRebarEnabled { get; set; } = true;
        /// <summary>Cobrimento nominal até a face externa do estribo (CAA II, pilar = 30 mm).</summary>
        public double RebarCoverMm { get; set; } = 30;
        /// <summary>Bitola longitudinal (NBR 6118 18.4.2.1: mínimo 10 mm).</summary>
        public double ColumnLongBarDiameterMm { get; set; } = 10;
        /// <summary>Bitola do estribo (NBR 6118 18.4.3: mínimo 5 mm e Ø long / 4).</summary>
        public double ColumnStirrupDiameterMm { get; set; } = 5;
        /// <summary>Espaçamento máximo entre eixos de barras longitudinais (≤ 400 mm; o plugin também limita a 2 × menor lado).</summary>
        public double ColumnLongBarMaxSpacingMm { get; set; } = 400;
        /// <summary>Espaçamento máximo dos estribos (≤ 200 mm; o plugin também limita ao menor lado e a 12 Ø long).</summary>
        public double ColumnStirrupMaxSpacingMm { get; set; } = 200;
        /// <summary>Distância do primeiro e do último estribo às extremidades do pilar.</summary>
        public double ColumnStirrupEndOffsetMm { get; set; } = 50;
        /// <summary>Grampos nas barras intermediárias além de 20 Ø estribo do canto (NBR 6118 18.2.4).</summary>
        public bool ColumnCrossTies { get; set; } = true;
        /// <summary>Nomes dos tipos de barra criados quando o projeto não tem a bitola: {0} = bitola em mm.</summary>
        public string LongBarTypeNameFormat { get; set; } = "Ø{0} CA-50";
        public string StirrupBarTypeNameFormat { get; set; } = "Ø{0} CA-60";

        // ---------- Conteúdo ----------
        public bool Include3D { get; set; } = true;
        public bool IncludePartList { get; set; } = true;
        public bool OpenSheetWhenDone { get; set; } = true;

        // ======================================================================

        public static string ConfigPath => Path.Combine(UserFolders.Root, "DetalhamentoConfig.json");

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        public static DetailingConfig Load(out string warning)
        {
            warning = null;
            try
            {
                if (!File.Exists(ConfigPath))
                {
                    var def = new DetailingConfig();
                    def.Save();
                    return def;
                }

                var cfg = JsonSerializer.Deserialize<DetailingConfig>(File.ReadAllText(ConfigPath), JsonOptions);
                return cfg ?? new DetailingConfig();
            }
            catch (Exception ex)
            {
                warning = "Não foi possível ler a configuração (" + ex.Message + "). Usando valores padrão.";
                return new DetailingConfig();
            }
        }

        public DetailingConfig Clone() => (DetailingConfig)MemberwiseClone();

        public void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath));
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(this, JsonOptions));
        }
    }
}
