using System;
using System.IO;
using Armadura.Cad.Barra;
using Armadura.Cad.Infra;
using Armadura.Cad.Overrules;
using Armadura.Core.Configuracao;

[assembly: Rt.ExtensionApplication(typeof(Armadura.Cad.Plugin))]
[assembly: Rt.CommandClass(typeof(Armadura.Cad.Comandos))]

namespace Armadura.Cad;

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
            RecarregarConfig();
            // templates de elementos (docs/TEMPLATES.md)
            Core.Templates.CatalogoTemplates.Registrar(new Core.Templates.BaseReservatorio.TemplateBaseReservatorio());
            DesenhoArmadura.Registrar();
            var dm = CadApp.DocumentManager;
            foreach (Document d in dm) Sincronizador.Anexar(d);
            dm.DocumentCreated += (s, e) => Sincronizador.Anexar(e.Document);
            dm.DocumentToBeDestroyed += (s, e) => Sincronizador.Desanexar(e.Document);
            Log.Info($"Plugin {Versao} carregado.");
            dm.MdiActiveDocument?.Editor.WriteMessage(
                $"\nArmadura {Versao} carregado. Comandos: ARMBARRA, ARMCONVERTER, ARMPALETA, ARMPROP, ARMEDITSEG, " +
                "ARMRAINSERIR, ARMRAATUALIZAR, ARMCONFIG, ARMSOBRE.\n");
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
    }

    public static void SalvarConfig()
    {
        try { Config.Salvar(CaminhoConfig); }
        catch (Exception ex) { Log.Erro("SalvarConfig", ex); }
    }
}
