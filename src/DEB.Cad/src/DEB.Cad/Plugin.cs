using System;
using System.IO;
using DEB.Cad.Barra;
using DEB.Cad.Infra;
using DEB.Cad.Overrules;
using DEB.Core.Configuracao;

[assembly: Rt.ExtensionApplication(typeof(DEB.Cad.Plugin))]
[assembly: Rt.CommandClass(typeof(DEB.Cad.Comandos))]

namespace DEB.Cad;

/// <summary>Entrada do plugin: carrega a configuração, registra o Overrule e liga o sincronizador.</summary>
public sealed class Plugin : Rt.IExtensionApplication
{
    public static ConfiguracaoArmadura Config { get; private set; } = new ConfiguracaoArmadura();

    public static string CaminhoConfig => Path.Combine(Log.PastaDados, "config.json");

    public static string Versao => typeof(Plugin).Assembly.GetName().Version.ToString(3);

    public void Initialize()
    {
        try
        {
            Demo.Iniciar();
            try { Layers.GarantirArquivo(); } catch (Exception ex) { Log.Erro("layers.csv", ex); }
            RecarregarConfig();
            // templates de elementos (docs/TEMPLATES.md)
            Core.Templates.CatalogoTemplates.Registrar(new Core.Templates.BaseReservatorio.TemplateBaseReservatorio());
            Core.Templates.CatalogoTemplates.Registrar(new Core.Templates.PilarPreMoldado.TemplatePilarPreMoldado());
            Core.Templates.CatalogoTemplates.Registrar(new Core.Templates.Reservatorio.TemplateReservatorio());
            // blocos pelo motor de detalhamento (2026-11-07): mesma janela e modelos do template antigo; perfil e receita do
            // usuário em %AppData%\PCEB\Perfis e \Receitas (sem arquivo = fábrica); origem gravada no detalhamento
            // blocos: uma entrada por número de estacas e variante (pedido do usuário, 2026-11-08)
            foreach (var bv in Core.Motor.Fundacoes.TemplateBlocoVariante.Todas(
                         Core.Motor.Perfis.RepositorioPerfis.PastaPadrao(), Core.Motor.Receitas.RepositorioReceitas.PastaPadrao()))
                Core.Templates.CatalogoTemplates.Registrar(bv);
            // modelos dos templates (PADRÃO + elementos salvos): carregados a cada abertura do GstarCAD
            try { ModelosTemplates.Carregar(); } catch (Exception ex) { Log.Erro("Modelos dos templates", ex); }
            DesenhoArmadura.Registrar();
            var dm = CadApp.DocumentManager;
            foreach (Document d in dm) Sincronizador.Anexar(d);
            dm.DocumentCreated += (s, e) => Sincronizador.Anexar(e.Document);
            dm.DocumentToBeDestroyed += (s, e) => Sincronizador.Desanexar(e.Document);
            Log.Info($"Plugin {Versao} carregado.");
            dm.MdiActiveDocument?.Editor.WriteMessage(
                $"\nArmadura {Versao} carregado. Comandos: ARMBARRA, ARMCONVERTER, ARMPALETA, ARMPROP, ARMEDITSEG, " +
                "ARMRAINSERIR, ARMRAATUALIZAR, ARMCONFIG, ARMSOBRE.\n");
            if (Demo.Ativa)
                dm.MdiActiveDocument?.Editor.WriteMessage("\n" + (Demo.Expirada ? Demo.Mensagem : Demo.TextoRestante) + "\n");
#if GSTARCAD
            // sugestões da linha de comando (AUTOCOMPLETE) do GstarCAD: lista fixa em alias.pgc
            try
            {
                int novos = Autocompletar.Garantir();
                if (novos > 0)
                    dm.MdiActiveDocument?.Editor.WriteMessage(
                        $"\nArmadura: {novos} comando(s) incluído(s) nas sugestões da linha de comando — valem a partir da próxima abertura do GstarCAD.\n");
            }
            catch (Exception ex) { Log.Erro("Autocompletar", ex); }
#endif
            if (Config.MostrarPaleta)
            {
                try { Paleta.PaletaArmadura.Mostrar(); }
                catch (Exception ex) { Log.Erro("Abrir paleta", ex); }
            }
            if (Config.MostrarComandos)
            {
                try { Paleta.PainelComandos.Mostrar(); }
                catch (Exception ex) { Log.Erro("Abrir paleta de comandos", ex); }
            }
        }
        catch (Exception ex)
        {
            Log.Erro("Initialize", ex);
        }
    }

    public void Terminate()
    {
        try
        {
            DesenhoArmadura.Remover();
            Sincronizador.DesanexarTodos();
            SalvarConfig();
        }
        catch (Exception ex) { Log.Erro("Terminate", ex); }
    }

    public static void RecarregarConfig()
    {
        Config = ConfiguracaoArmadura.Carregar(CaminhoConfig, aviso =>
        {
            Log.Info(aviso);
            CadApp.DocumentManager.MdiActiveDocument?.Editor.WriteMessage("\n" + aviso);
        });
        ConfigDesenho.EsquecerTodos(); // relê as configurações dos desenhos com os correntes novos
        // catálogo de consolos padrão do template do pilar pré-moldado (ARMCONSOLOS)
        Core.Templates.PilarPreMoldado.TemplatePilarPreMoldado.Catalogo = Config.Consolos;
        Core.Templates.PilarPreMoldado.TemplatePilarPreMoldado.CatalogoInsertos = Config.Insertos;
    }

    public static void SalvarConfig()
    {
        try { Config.Salvar(CaminhoConfig); }
        catch (Exception ex) { Log.Erro("SalvarConfig", ex); }
    }
}
