using System.Reflection;
using Autodesk.Revit.UI;
using DEB.Revit.Commands;
using DEB.Revit.Tools;
using DEB.Revit.UI;

namespace DEB.Revit
{
    /// <summary>
    /// Ponto de entrada do plugin: cria a aba "Detalhador Estrutural Brasileiro" no Ribbon.
    /// </summary>
    public class App : IExternalApplication
    {
        public const string TabName = "Detalhador Estrutural Brasileiro";

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

            var exportar = new PushButtonData("DEB_ExportarPecas", "Exportar\npara o CAD", dll, typeof(ExportPiecesCommand).FullName)
            {
                ToolTip = "Grava os grupos de pilares pré-moldados iguais num arquivo .json para detalhar no CAD (ARMIMPORTARPECAS).",
                LongDescription =
                    "Usa o mesmo levantamento do botão \"Pilares pré-moldados\" (peças iguais ou giradas = mesmo grupo) e grava, por " +
                    "grupo: seção, comprimento, cota da base, níveis, embutimento e consolos. No GstarCAD/AutoCAD, o ARMIMPORTARPECAS " +
                    "gera um detalhamento por grupo pelo template do pilar pré-moldado. Não altera o modelo.",
                AvailabilityClassName = typeof(ProjectOnlyAvailability).FullName,
                LargeImage = IconFactory.Create(32, "CAD"),
                Image = IconFactory.Create(16, "C")
            };
            precast.AddItem(exportar);

            AddToolsPanel(application, dll);

            AutoRun.Register(application);
            return Result.Succeeded;
        }

        /// <summary>Painel "Ferramentas": os scripts do escritório (Revit_Suelio) transformados em comandos do DEB.</summary>
        private static void AddToolsPanel(UIControlledApplication application, string dll)
        {
            RibbonPanel tools = application.CreateRibbonPanel(TabName, "Ferramentas");
            string projectOnly = typeof(ProjectOnlyAvailability).FullName;
            PushButtonData Btn(string id, string text, System.Type cmd, string tip, string icon) => new PushButtonData(id, text, dll, cmd.FullName)
            {
                ToolTip = tip,
                AvailabilityClassName = projectOnly,
                LargeImage = IconFactory.Create(32, icon),
                Image = IconFactory.Create(16, icon.Substring(0, 1))
            };

            var renumber = (PulldownButton)tools.AddItem(new PulldownButtonData("DEB_Renumerar", "Renumerar\nmarcas")
            {
                ToolTip = "Renumera as marcas das peças selecionadas (soltas ou em grupos), de cima para baixo e da esquerda para a direita.",
                LargeImage = IconFactory.Create(32, "N#"),
                Image = IconFactory.Create(16, "N")
            });
            const string orderTip = " Selecione as peças ou grupos e informe prefixo e número inicial.";
            renumber.AddPushButton(Btn("DEB_RenPilares", "Pilares", typeof(RenumberColumnsCommand), "Pilares em linhas pelo canto superior esquerdo do croqui (tolerância 0,50 m)." + orderTip, "P"));
            renumber.AddPushButton(Btn("DEB_RenVigas", "Vigas", typeof(RenumberBeamsCommand), "Horizontais (cima → baixo), depois verticais (esquerda → direita), depois inclinadas." + orderTip, "V"));
            renumber.AddPushButton(Btn("DEB_RenLajes", "Lajes", typeof(RenumberSlabsCommand), "Linhas pela sobreposição em Y (≥ 50 % da laje), cada uma da esquerda para a direita." + orderTip, "L"));
            renumber.AddPushButton(Btn("DEB_RenFundacoes", "Fundações", typeof(RenumberFoundationsCommand), "Fundações em linhas pelo canto superior esquerdo do croqui (tolerância 0,50 m)." + orderTip, "F"));
            renumber.AddPushButton(Btn("DEB_RenParedes", "Paredes", typeof(RenumberWallsCommand), "Horizontais, depois verticais, depois inclinadas; faixas de 0,50 m de cima para baixo." + orderTip, "W"));
            renumber.AddPushButton(Btn("DEB_RenPecas", "Peças (qualquer)", typeof(RenumberPiecesCommand), "Qualquer elemento com marca editável, em linhas pelo canto superior esquerdo." + orderTip, "*"));

            tools.AddItem(Btn("DEB_SelMarca", "Selecionar\npor marca", typeof(SelectByMarkCommand),
                "Seleciona pilares, fundações, vigas, lajes ou paredes pela marca (começa com, contém, igual, expressão regular).", "S"));
            tools.AddItem(Btn("DEB_Material", "Alterar\nmaterial", typeof(ChangeMaterialCommand),
                "Grava o material escolhido nos tipos de pilares e/ou vigas (padrão: Concreto - Pré-Moldado - C40).", "M"));
            tools.AddItem(Btn("DEB_ExportCsv", "Exportar\nCSV", typeof(ExportCsvCommand),
                "Exporta parâmetros, BuiltInParameters e geometria das peças para 6 arquivos CSV.", "CSV"));

            var copy = (PulldownButton)tools.AddItem(new PulldownButtonData("DEB_Copiar", "Copiar de\noutro arquivo")
            {
                ToolTip = "Copia elementos de outro .rvt para o projeto atual, na mesma posição.",
                LargeImage = IconFactory.Create(32, "⇄"),
                Image = IconFactory.Create(16, "⇄")
            });
            copy.AddPushButton(Btn("DEB_CopFundacoes", "Fundações", typeof(CopyFoundationsCommand), "Fundações, sapatas corridas e montagens com fundações de outro arquivo.", "F"));
            copy.AddPushButton(Btn("DEB_CopMontagens", "Montagens com vistas", typeof(CopyAssembliesCommand), "Montagens com membros, vistas da montagem e folhas de outro arquivo.", "A"));
        }

        public Result OnShutdown(UIControlledApplication application) => Result.Succeeded;
    }
}
