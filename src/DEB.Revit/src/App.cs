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

            var cfgProjeto = new PushButtonData("DEB_ConfiguracaoProjeto", "Configuração\ndo projeto", dll, typeof(ConfiguracaoProjetoCommand).FullName)
            {
                ToolTip = "Normas, coeficientes, materiais, durabilidade, unidades e bitolas do projeto (D45): padrão do escritório e do projeto.",
                LongDescription = "O escritório fica num arquivo comum ao CAD; o projeto fica gravado dentro do modelo. Valor fora da norma é aceito com justificativa, que sai no memorial.",
                LargeImage = IconFactory.Create(32, "⚖"),
                Image = IconFactory.Create(16, "⚖")
            };
            panel.AddItem(cfgProjeto);

            var bibLig = new PushButtonData("DEB_BibliotecaLigacoes", "Biblioteca de\nligações", dll, typeof(BibliotecaLigacoesCommand).FullName)
            {
                ToolTip = "Ligações metálicas parametrizadas do PCEB e do escritório (D49): chapa de alma, cantoneira dupla, chapa de topo e placa de base.",
                LongDescription = "As do PCEB vêm com o programa; o escritório edita, cria e grava as suas num arquivo comum ao CAD e ao Revit. A análise usa a primeira de cada tipo e verifica pela NBR 8800:2024.",
                LargeImage = IconFactory.Create(32, "⛓"),
                Image = IconFactory.Create(16, "⛓")
            };
            panel.AddItem(bibLig);

            var modLig = new PushButtonData("DEB_ModelarLigacoes", "Modelar\nligações", dll, typeof(ModelarLigacoesCommand).FullName)
            {
                ToolTip = "Verifica e modela as ligações das peças de aço (D49) com as famílias PCEB: chapas, parafusos e chumbadores.",
                LongDescription = "Usa a biblioteca de ligações e a NBR 8800:2024; mostra o resumo antes de criar. Cada ligação vira um grupo \"PCEB <id>\"; rodar de novo atualiza.",
                AvailabilityClassName = typeof(ProjectOnlyAvailability).FullName,
                LargeImage = IconFactory.Create(32, "⚙"),
                Image = IconFactory.Create(16, "⚙")
            };
            panel.AddItem(modLig);

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

            var vigas = new PushButtonData("DEB_VigasPM", "Vigas\npré-moldadas", dll, typeof(PrecastBeamsCommand).FullName)
            {
                ToolTip = "Calcula as vigas pré-moldadas de concreto armado (biapoiadas) pelas cargas digitadas, propõe a armadura e arma no Revit.",
                LongDescription =
                    "Levanta as vigas da obra (o mesmo levantamento do \"Conferir peças\"), agrupa as iguais e lê as cargas digitadas nos " +
                    "parâmetros PCEB_ da viga (permanente, variável e concentradas; o peso próprio é calculado). Calcula flexão, cortante, " +
                    "dente Gerber, içamento, flecha e fissuras (NBR 6118 / NBR 9062), propõe a armadura no padrão do escritório e, no aceite, " +
                    "cria as barras, a montagem, as vistas e as tabelas de aço. O relatório de cálculo de cada grupo vai para \"Relatórios PCEB\".",
                AvailabilityClassName = typeof(ProjectOnlyAvailability).FullName,
                LargeImage = IconFactory.Create(32, "VM"),
                Image = IconFactory.Create(16, "V")
            };
            precast.AddItem(vigas);

            var analitico = new PushButtonData("DEB_ModeloAnalitico", "Modelo de\nanálise", dll, typeof(ModeloAnaliticoCommand).FullName)
            {
                ToolTip = "Monta pilares, vigas e ligações por consolo como objetos (D46), analisa e mostra na janela do PCEB: 3D, deformada, diagramas e mapa de cores.",
                LongDescription = "O modelo é do PCEB (não escreve no Revit). Cada barra guarda o dono (pilar, viga ou ligação). Consolo = barra rígida até o ponto de apoio; aparelho de apoio = mola pela rigidez do elastômero.",
                LargeImage = IconFactory.Create(32, "⌗"),
                Image = IconFactory.Create(16, "⌗")
            };
            precast.AddItem(analitico);

            var conferir = new PushButtonData("DEB_ConferirPecas", "Conferir\npeças", dll, typeof(ReviewPiecesCommand).FullName)
            {
                ToolTip = "Confere vigas e pilares antes de exportar: seção medida na geometria × nome do tipo, trechos e seções cotadas.",
                LongDescription =
                    "Cada peça é cortada ao longo do eixo (viga) ou da altura (pilar) em trechos de seção constante — dentes, abas de laje " +
                    "e de escada, trechos sem apoio. A janela mostra os grupos de peças iguais pela geometria, o tipo do Revit, a situação " +
                    "(ok, atenção, seção que o CAD ainda não desenha), a elevação com os trechos e as seções em escala com as cotas. " +
                    "Não altera o modelo.",
                AvailabilityClassName = typeof(ProjectOnlyAvailability).FullName,
                LargeImage = IconFactory.Create(32, "CF"),
                Image = IconFactory.Create(16, "F")
            };
            precast.AddItem(conferir);

            var exportar = new PushButtonData("DEB_ExportarPecas", "Exportar\npara o CAD", dll, typeof(ExportPiecesCommand).FullName)
            {
                ToolTip = "Grava os grupos de pilares e vigas pré-moldados iguais num arquivo .json para detalhar no CAD (ARMIMPORTARPECAS).",
                LongDescription =
                    "Usa o mesmo levantamento do botão \"Pilares pré-moldados\" (peças iguais ou giradas = mesmo grupo) e grava, por " +
                    "grupo: seção, comprimento, cota da base, níveis, embutimento e consolos; e as vigas pré-moldadas (seção, comprimento, " +
                    "dentes Gerber; invertidas = iguais). No GstarCAD/AutoCAD, o ARMIMPORTARPECAS gera um detalhamento por grupo pelos " +
                    "templates do pilar e da viga pré-moldados. Não altera o modelo.",
                AvailabilityClassName = typeof(ProjectOnlyAvailability).FullName,
                LargeImage = IconFactory.Create(32, "CAD"),
                Image = IconFactory.Create(16, "C")
            };
            precast.AddItem(exportar);

            var lajeAlveolar = new PushButtonData("DEB_PaginarLajeAlveolar", "Paginar laje\nalveolar", dll, typeof(LajeAlveolar.PaginarLajeAlveolarCommand).FullName)
            {
                ToolTip = "Pagina os pisos selecionados em painéis alveolares (módulo, ajustes, aberturas, pilares, faixas maciças) e insere os painéis no modelo.",
                LongDescription =
                    "Lê o contorno e os furos do croqui de cada piso, o sentido do vão, as vigas em volta (alma e aba medidas na geometria) e os " +
                    "pilares; distribui os painéis pela estratégia escolhida (aberturas primeiro, melhor distribuição ou de um lado), mostra a prévia " +
                    "com os avisos e, ao confirmar, insere os painéis (família de quadro estrutural escolhida), as faixas maciças e, se ligados, a capa, " +
                    "as chaves de cisalhamento e os tirantes dos suportes de instalações. Marca, etapa de concretagem e relatório no fim.",
                AvailabilityClassName = typeof(ProjectOnlyAvailability).FullName,
                LargeImage = IconFactory.Create(32, "LA"),
                Image = IconFactory.Create(16, "L")
            };
            precast.AddItem(lajeAlveolar);

            AddToolsPanel(application, dll);
            AddFormasPanel(application, dll);

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

        /// <summary>Painel "Formas" (D25): pranchas de locação e de montagem — dados SD_, cargas por CSV, plantas anotadas, tabelas e folhas.</summary>
        private static void AddFormasPanel(UIControlledApplication application, string dll)
        {
            RibbonPanel formas = application.CreateRibbonPanel(TabName, "Formas");
            string projectOnly = typeof(ProjectOnlyAvailability).FullName;
            PushButtonData Btn(string id, string text, System.Type cmd, string tip, string longTip, string icon) => new PushButtonData(id, text, dll, cmd.FullName)
            {
                ToolTip = tip,
                LongDescription = longTip,
                AvailabilityClassName = projectOnly,
                LargeImage = IconFactory.Create(32, icon),
                Image = IconFactory.Create(16, icon.Substring(0, 1))
            };
            formas.AddItem(Btn("DEB_FormasDados", "Atualizar\ndados", typeof(Formas.AtualizarDadosFormasCommand),
                "Cria os parâmetros SD_ das pranchas de formas e grava os textos calculados (seção, níveis, comprimento, elevação, dados das lajes).",
                "Pilares: PCEB_Secao, PCEB_NivelTopo, PCEB_NivelTopoBloco, PCEB_NivelBase (\"12,00 m (907,80)\") e PCEB_ComprimentoTotal. Vigas: PCEB_Secao e " +
                "PCEB_Elevacao (\"e=-29.5\"). Lajes com marca: altura, elevação, nível, tipo e peso próprio (estes dois só se vazios). Esforços, " +
                "sobrecargas e observações digitados nunca são alterados. Rode de novo depois de mexer no modelo.", "D"));
            formas.AddItem(Btn("DEB_FormasCargas", "Cargas\n(CSV)", typeof(Formas.CargasCsvFormasCommand),
                "Importa ou exporta os esforços na fundação dos pilares (Marca;Fz_Max;Fz_Min;Mx_Pos;…;Fy_Neg).",
                "Importar grava os valores nos pilares com a mesma marca (vírgula ou ponto decimal; célula vazia não altera) e lista as marcas " +
                "que não existem no modelo. Exportar grava os valores do modelo no mesmo formato.", "CSV"));
            formas.AddItem(Btn("DEB_FormasLocacao", "Planta de\nlocação", typeof(Formas.PlantaLocacaoCommand),
                "Planta de locação do nível em 1:50: cotas de eixos, identificadores de pilares e fundações, tabela de cargas na fundação e folha.",
                "As famílias de identificador e os nomes vêm da seção \"Formas\" do padrão PCEB-FORMAS-A0 (Configurar). Rodar de novo atualiza o " +
                "que o comando criou, sem duplicar.", "LO"));
            formas.AddItem(Btn("DEB_FormasMontagem", "Planta de\nmontagem", typeof(Formas.PlantaMontagemCommand),
                "Planta de montagem do pavimento em 1:50: cotas de eixos, identificadores de pilares, vigas e lajes, sentido do vão, tabela de lajes e folha.",
                "As famílias de identificador e os nomes vêm da seção \"Formas\" do padrão PCEB-FORMAS-A0. Rodar de novo atualiza o que o comando " +
                "criou, sem duplicar.", "MO"));
            formas.AddItem(Btn("DEB_FormasPaginacao", "Paginação\ndas lajes", typeof(Formas.PlantaPaginacaoCommand),
                "Planta de paginação das lajes alveolares do pavimento: painéis identificados, tabela dos painéis, nota dos furos e folha.",
                "Painéis = quadro estrutural com a marca do filtro de painéis (padrão: começa com L). Rodar de novo atualiza sem duplicar.", "PG"));
            formas.AddItem(Btn("DEB_FormasCortes", "Cortes e\nelevações", typeof(Formas.CortesFormasCommand),
                "Cria (ou atualiza) um corte por direção entre os eixos centrais e as quatro elevações da estrutura, com identificadores e folhas.",
                "Os cortes que já existem no modelo não são alterados; os do comando são reaproveitados ao rodar de novo.", "CT"));
            formas.AddItem(Btn("DEB_FormasSecoes", "Seções\nconsideradas", typeof(Formas.SecoesFormasCommand),
                "Folha com as seções de vigas e pilares usadas no modelo (tabela por tipo: seção, quantidade, material).",
                "Usa PCEB_Secao gravado pelo \"Atualizar dados\".", "SC"));
            formas.AddItem(Btn("DEB_FormasAnotarCorte", "Anotar\ncorte", typeof(Formas.AnotarCorteCommand),
                "Cota, nomeia e põe cotas de nível no corte ativo (ou nos cortes escolhidos), no padrão do escritório: geral, caixa de elevador ou escada.",
                "Só o que está cortado ou visível é anotado (peça escondida atrás de outra ou de fase posterior fica de fora). Rodar de novo refaz " +
                "as cotas do comando; o que foi feito à mão não é tocado.", "AC"));
        }

        public Result OnShutdown(UIControlledApplication application) => Result.Succeeded;
    }
}
