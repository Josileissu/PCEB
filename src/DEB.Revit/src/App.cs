using System.Reflection;
using Autodesk.Revit.UI;
using StructuralDetailer.Commands;
using StructuralDetailer.UI;

namespace StructuralDetailer
{
    /// <summary>
    /// Ponto de entrada do plugin: cria a aba "Detalhamento" no Ribbon.
    /// </summary>
    public class App : IExternalApplication
    {
        public const string TabName = "Detalhamento";

        public Result OnStartup(UIControlledApplication application)
        {
            try { application.CreateRibbonTab(TabName); }
            catch (Autodesk.Revit.Exceptions.ArgumentException) { /* aba já existe */ }

            RibbonPanel panel = application.CreateRibbonPanel(TabName, "Peças estruturais");
            string dll = Assembly.GetExecutingAssembly().Location;

            var detalhar = new PushButtonData("SD_DetalharPeca", "Detalhar\npeça", dll, typeof(DetailPieceCommand).FullName)
            {
                ToolTip = "Gera o detalhamento da peça selecionada: assembly, elevações, cortes, cotas e folha.",
                LongDescription =
                    "Selecione uma ou mais vigas / pilares (moldados in loco ou pré-fabricados) e clique. " +
                    "Para cada peça é criado um assembly alinhado ao eixo da peça, com elevações, cortes transversais, " +
                    "vista 3D, lista de peças, cotas gerais e uma folha montada automaticamente.",
                AvailabilityClassName = typeof(ProjectOnlyAvailability).FullName,
                LargeImage = IconFactory.Create(32, "DP"),
                Image = IconFactory.Create(16, "D")
            };
            panel.AddItem(detalhar);

            var config = new PushButtonData("SD_Configurar", "Configurar", dll, typeof(OpenConfigCommand).FullName)
            {
                ToolTip = "Abre o arquivo de configuração (escalas, templates de vista, carimbo, margens da folha).",
                LargeImage = IconFactory.Create(32, "⚙"),
                Image = IconFactory.Create(16, "⚙")
            };
            panel.AddItem(config);

            var diag = new PushButtonData("SD_Diagnostico", "Diagnóstico", dll, typeof(DiagnosticsCommand).FullName)
            {
                ToolTip = "Grava um levantamento do modelo (pilares, vigas, consolos, contatos entre peças) para análise.",
                AvailabilityClassName = typeof(ProjectOnlyAvailability).FullName,
                LargeImage = IconFactory.Create(32, "?"),
                Image = IconFactory.Create(16, "?")
            };
            panel.AddItem(diag);

            RibbonPanel precast = application.CreateRibbonPanel(TabName, "Pré-moldados");
            var pilares = new PushButtonData("SD_PilaresPM", "Pilares\npré-moldados", dll, typeof(PrecastColumnsCommand).FullName)
            {
                ToolTip = "Pré-visualiza todos os pilares pré-moldados (com consolos), agrupa as peças iguais e gera armaduras e detalhamento.",
                LongDescription =
                    "Levanta os pilares da obra e os consolos encostados neles (inclusive consolos aninhados nas famílias de viga), " +
                    "agrupa as peças iguais, mesmo giradas, e abre uma janela para escolher a armadura de cada grupo: longitudinal, " +
                    "estribos por zonas, tirante e costura dos consolos, alças de içamento e insertos. Depois arma todos os pilares, " +
                    "cria os assemblies (pilar + consolos + armaduras) e uma folha por peça.",
                AvailabilityClassName = typeof(ProjectOnlyAvailability).FullName,
                LargeImage = IconFactory.Create(32, "PM"),
                Image = IconFactory.Create(16, "P")
            };
            precast.AddItem(pilares);

            AutoRun.Register(application);
            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application) => Result.Succeeded;
    }
}
