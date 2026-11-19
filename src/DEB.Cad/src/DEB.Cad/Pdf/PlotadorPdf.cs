using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using DEB.Core.Documentacao;

namespace DEB.Cad.Pdf;

/// <summary>
/// Plota UM layout de um documento aberto e ativo na impressora do lote, com uma configuração TEMPORÁRIA (PlotInfo.OverrideSettings):
/// o page setup salvo no layout não muda, salvo com "gravar no layout".
/// </summary>
internal static class PlotadorPdf
{
    /// <summary>Nome canônico (UserNNNNN…) da folha cujo nome LOCAL é <paramref name="nomeLocal"/>; null se a impressora não tiver.</summary>
    public static string MidiaCanonica(PlotSettingsValidator v, PlotSettings ps, string nomeLocal)
    {
        StringCollection lista = v.GetCanonicalMediaNameList(ps);
        string porCanonico = null;
        foreach (string c in lista)
        {
            string local;
            try { local = v.GetLocaleMediaName(ps, c); } catch { local = null; }
            if (string.Equals((local ?? "").Trim(), nomeLocal.Trim(), StringComparison.OrdinalIgnoreCase)) return c;
            if (string.Equals(c.Trim(), nomeLocal.Trim(), StringComparison.OrdinalIgnoreCase)) porCanonico = c;
        }
        return porCanonico;
    }

    /// <summary>Lista "canônico = local" das folhas da impressora (diagnóstico no log).</summary>
    public static List<string> ListarMidias(string impressora)
    {
        var res = new List<string>();
        var db = HostApplicationServices.WorkingDatabase;
        using var tr = db.TransactionManager.StartTransaction();
        var lay = (Layout)tr.GetObject(LayoutManager.Current.GetLayoutId(LayoutManager.Current.CurrentLayout), OpenMode.ForRead);
        using var ps = new PlotSettings(lay.ModelType);
        ps.CopyFrom(lay);
        var v = PlotSettingsValidator.Current;
        v.SetPlotConfigurationName(ps, impressora, null);
        v.RefreshLists(ps);
        foreach (string c in v.GetCanonicalMediaNameList(ps))
        {
            string local;
            try { local = v.GetLocaleMediaName(ps, c); } catch (Exception ex) { local = "(erro: " + ex.Message + ")"; }
            // tamanho e margens que o CAD considera para a folha (diagnóstico da margem de 3 mm, 18/11/2026)
            string medida = "";
            try
            {
                v.SetCanonicalMediaName(ps, c);
                v.SetPlotPaperUnits(ps, PlotPaperUnit.Millimeters);
                var t = ps.PlotPaperSize; var m = ps.PlotPaperMargins;
                medida = FormattableString.Invariant($" | {t.X:0.##}x{t.Y:0.##} mm, margens {m.MinPoint.X:0.##} {m.MinPoint.Y:0.##} {m.MaxPoint.X:0.##} {m.MaxPoint.Y:0.##}");
            }
            catch (Exception ex) { medida = " | (medida: " + ex.Message + ")"; }
            res.Add(c + " = " + local + medida);
        }
        tr.Commit();
        return res;
    }

    /// <summary>
    /// Nomes LOCAIS das folhas que a impressora oferece neste CAD (para avisar "crie as folhas na impressora"). null = não deu para
    /// ler (sem desenho aberto, impressora inexistente…): a janela não avisa nada.
    /// </summary>
    public static HashSet<string> NomesMidias(string impressora)
    {
        if (string.IsNullOrWhiteSpace(impressora) || CadApp.DocumentManager.Count == 0) return null;
        try
        {
            var res = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var linha in ListarMidias(impressora.Trim()))
            {
                // "canônico = local | medida e margens": a medida é só do diagnóstico (com ela no nome, nenhuma folha era achada)
                int m = linha.IndexOf(" | ", StringComparison.Ordinal);
                var l = m >= 0 ? linha.Substring(0, m) : linha;
                int i = l.IndexOf(" = ", StringComparison.Ordinal);
                if (i >= 0) res.Add(l.Substring(i + 3).Trim());
                res.Add((i >= 0 ? l.Substring(0, i) : l).Trim());
            }
            return res.Count > 0 ? res : null;
        }
        catch (Exception ex) { Infra.Log.Erro("ARMPDFLOTE: folhas da impressora " + impressora, ex); return null; }
    }

    /// <summary>Configurações de plotagem do CAD (.pc3, ex.: AutoCAD PDF) e impressoras do sistema que o CAD enxerga.</summary>
    public static List<string> Dispositivos()
    {
        var res = new List<string>();
        try { foreach (string s in PlotSettingsValidator.Current.GetPlotDeviceList()) res.Add(s); }
        catch (Exception ex) { Infra.Log.Erro("ARMPDFLOTE: lista de impressoras", ex); }
        return res;
    }

    public static List<string> EstilosDisponiveis()
    {
        var res = new List<string>();
        try { foreach (string s in PlotSettingsValidator.Current.GetPlotStyleSheetList()) res.Add(s); }
        catch (Exception ex) { Infra.Log.Erro("ARMPDFLOTE: lista de estilos", ex); }
        return res;
    }

    /// <summary>
    /// Plota <paramref name="f"/> (documento já ativo e travado pelo chamador). <paramref name="nomeTrabalho"/> = nome do trabalho
    /// de impressão (a PDF24 usa como nome do arquivo). <paramref name="arquivo"/> = plotar para este arquivo (impressora .pc3,
    /// ex.: AutoCAD PDF); null = mandar para a impressora. Devolve um texto com o papel e a área usados (para o log).
    /// </summary>
    public static string Plotar(Document doc, FolhaPdf f, ConfigLotePdf cfg, string nomeTrabalho, bool gravarNoLayout, string arquivo = null,
        Action<EtapaLotePdf> etapa = null, Action<string> aviso = null)
    {
        etapa ??= _ => { };
        aviso ??= _ => { };
        etapa(EtapaLotePdf.Configurar);
        var db = doc.Database;
        var lm = LayoutManager.Current;
        if (!string.Equals(lm.CurrentLayout, f.Layout, StringComparison.OrdinalIgnoreCase)) lm.CurrentLayout = f.Layout;
        string info;
        using (var tr = db.TransactionManager.StartTransaction())
        {
            var layId = lm.GetLayoutId(f.Layout);
            var lay = (Layout)tr.GetObject(layId, OpenMode.ForRead);
            // 1ª tentativa: a cópia do page setup do layout (mantém espessuras, sombras etc.); se ela estiver estragada ou apontar
            // para coisas que não existem neste PC (impressora/papel/ctb de outra máquina) e o validador recusar, configura do zero
            PlotSettings ps = null;
            string estilo;
            try
            {
                ps = new PlotSettings(lay.ModelType);
                ps.CopyFrom(lay);
                estilo = Configurar(ps, f, cfg, lay.CurrentStyleSheet, aviso);
            }
            catch (ErroLotePdfException) { ps?.Dispose(); throw; }
            catch (Exception ex)
            {
                ps?.Dispose();
                Infra.Log.Erro($"ARMPDFLOTE: page setup de {f.NomeDesenho} / {f.Rotulo} recusado; configurando do zero", ex);
                ps = new PlotSettings(lay.ModelType);
                estilo = Configurar(ps, f, cfg, lay.CurrentStyleSheet, aviso);
                aviso("page setup do arquivo ignorado (impressora/papel de outro computador?); configurado do zero");
            }
            using var _ps = ps;
            info = FormattableString.Invariant(
                $"papel {ps.CanonicalMediaName} = \"{f.Formato.Nome}\" {ps.PlotPaperSize.X:0.#}x{ps.PlotPaperSize.Y:0.#} mm, giro {ps.PlotRotation}, área {ps.PlotType}, estilo {estilo}");

            var pi = new Plt.PlotInfo { Layout = layId, OverrideSettings = ps };
            var piv = new Plt.PlotInfoValidator { MediaMatchingPolicy = Plt.MatchingPolicy.MatchEnabled };
            piv.Validate(pi);

            etapa(EtapaLotePdf.Plotar);
            if (Plt.PlotFactory.ProcessPlotState != Plt.ProcessPlotState.NotPlotting)
                throw new ErroLotePdfException(CausaErroPdf.CadOcupado, "o CAD já está plotando outro trabalho");
            using (var pe = Plt.PlotFactory.CreatePublishEngine())
            {
                pe.BeginPlot(null, null);
                pe.BeginDocument(pi, nomeTrabalho, null, 1, arquivo != null, arquivo ?? "");
                var ppi = new Plt.PlotPageInfo();
                pe.BeginPage(ppi, pi, true, null);
                pe.BeginGenerateGraphics(null);
                pe.EndGenerateGraphics(null);
                pe.EndPage(null);
                pe.EndDocument(null);
                pe.EndPlot(null);
            }

            if (gravarNoLayout)
            {
                lay.UpgradeOpen();
                lay.CopyFrom(ps);
            }
            tr.Commit();
        }
        return info;
    }

    /// <summary>
    /// Impressora, papel, área, escala, giro e estilo de plotagem em <paramref name="ps"/> (sem depender do que o layout tinha).
    /// Devolve o estilo usado. Falhas conhecidas saem como <see cref="ErroLotePdfException"/>.
    /// </summary>
    private static string Configurar(PlotSettings ps, FolhaPdf f, ConfigLotePdf cfg, string estiloDoLayout, Action<string> aviso)
    {
        var v = PlotSettingsValidator.Current;
        var impressora = cfg.ImpressoraLote;
        try { v.SetPlotConfigurationName(ps, impressora, null); }
        catch (Exception ex)
        {
            throw new ErroLotePdfException(ImpressorasPdf.Tipo(impressora) == TipoImpressoraPdf.Pdf24 ? CausaErroPdf.Pdf24NaoInstalada : CausaErroPdf.ImpressoraInexistente,
                $"a impressora \"{impressora}\" não existe neste CAD/computador", ex);
        }
        v.RefreshLists(ps);
        var canon = MidiaCanonica(v, ps, f.Formato.Nome) ?? throw SemPapel(impressora, f.Formato.Nome);
        v.SetPlotConfigurationName(ps, impressora, canon);
        v.SetPlotPaperUnits(ps, PlotPaperUnit.Millimeters);
        if (f.TemCaixa && (cfg.Area == AreaPlotLote.Moldura || f.NoModelo))
        {
            // no Model a janela é sempre a moldura (uma folha por moldura)
            v.SetPlotWindowArea(ps, f.Caixa);
            v.SetPlotType(ps, PlotType.Window);
        }
        else if (cfg.Area == AreaPlotLote.Layout && !ps.ModelType) v.SetPlotType(ps, PlotType.Layout);
        else v.SetPlotType(ps, PlotType.Extents);
        // moldura reconhecida: escala FIXA pela unidade do desenho (folha em cm: 1 mm = 0,1 unidade; em mm: 1 = 1), centralizada —
        // não depende do papel (que pode ter folga) nem de a moldura ter a medida exata. Com "ajustar à folha" o CAD encolhia o
        // desenho para a área imprimível dele — o GstarCAD reserva 3,2 mm de cada lado nos papéis personalizados da PDF24 (o driver
        // diz 0) e o PDF saía a 98,9 % (relato do usuário, 18/11/2026). Sem moldura (área = layout/extensão), continua ajustando.
        double escala = FormatosFolha.EscalaPlot(f.Reconhecido);
        if (f.TemCaixa && escala > 0 && (cfg.Area == AreaPlotLote.Moldura || f.NoModelo))
        {
            v.SetUseStandardScale(ps, false);
            v.SetCustomPrintScale(ps, new CustomScale(escala, 1));
        }
        else
        {
            v.SetUseStandardScale(ps, true);
            v.SetStdScaleType(ps, StdScaleType.ScaleToFit);
        }
        // folha deitada x desenho deitado: gira 90° quando as orientações não batem. Orientação do papel REAL da impressora (a
        // folha do .pc3 é gravada deitada mesmo para o A4 em pé da lista); sem medida, a do formato
        var papel = ps.PlotPaperSize;
        bool papelDeitado = papel.X > 0 && papel.Y > 0 ? papel.X >= papel.Y : f.Formato.Paisagem;
        v.SetPlotRotation(ps, f.Deitada == papelDeitado ? PlotRotation.Degrees000 : PlotRotation.Degrees090);
        v.SetPlotCentered(ps, true);

        // estilo: o escolhido (ou o do layout); se não existir neste PC, PCEB.ctb (gravado na pasta de estilos do CAD)
        var disponiveis = EstilosDisponiveis();
        var (estilo, av) = EstiloPlotagemPadrao.Escolher(cfg.EstiloPlotagem, estiloDoLayout, disponiveis.Count > 0 ? disponiveis : null);
        if (string.Equals(estilo, EstiloPlotagemPadrao.NomeArquivo, StringComparison.OrdinalIgnoreCase)) estilo = GarantirPceb(v, ps, disponiveis);
        if (av != null) aviso(av);
        try { v.SetCurrentStyleSheet(ps, estilo); }
        catch (Exception ex) { throw new ErroLotePdfException(CausaErroPdf.CtbNaoEncontrado, $"ctb \"{estilo}\" não encontrado/recusado pelo CAD", ex); }
        ps.PlotPlotStyles = true;
        return estilo;
    }

    /// <summary>
    /// Papel que falta na impressora, com a causa específica quando dá para saber: impressora do Windows cujo driver não mostra os
    /// formulários de papel personalizados (Microsoft Print to PDF) ou .pc3 em formato que o plugin não grava (GstarCAD).
    /// </summary>
    internal static ErroLotePdfException SemPapel(string impressora, string papel)
    {
        var motivo = MotivoSemFolhas(impressora);
        return new ErroLotePdfException(motivo != null ? CausaErroPdf.DriverSemTamanhoPersonalizado : CausaErroPdf.PapelInexistente,
            $"papel \"{papel}\" não existe na impressora \"{impressora}\"" + (motivo != null ? ": " + motivo : ""));
    }

    /// <summary>
    /// Por que a impressora não pode ter as folhas da lista (null = pode; basta criá-las): driver do Windows que ignora formulários
    /// personalizados (os formulários existem no Windows, mas o driver não lista nenhum) — o resultado fica guardado por impressora.
    /// </summary>
    internal static string MotivoSemFolhas(string impressora)
    {
        if (string.IsNullOrWhiteSpace(impressora) || ImpressorasPdf.Tipo(impressora) == TipoImpressoraPdf.Pc3) return null;
        lock (_motivos)
        {
            if (_motivos.TryGetValue(impressora, out var m)) return m;
            try
            {
                List<FormularioWindows> forms;
                using (var srv = FormulariosWindows.Abrir(false)) forms = srv.Listar();
                m = FormulariosPapel.DriverIgnoraPersonalizados(forms, FormulariosWindows.PapeisDaImpressora(impressora))
                    ? FormulariosPapel.MensagemDriverSemPersonalizados(impressora) : null;
            }
            catch (Exception ex) { Infra.Log.Erro("ARMPDFLOTE: conferir formulários do driver de " + impressora, ex); m = null; }
            _motivos[impressora] = m;
            return m;
        }
    }

    private static readonly Dictionary<string, string> _motivos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Esquece o que se sabe dos drivers (depois de criar formulários).</summary>
    internal static void EsquecerMotivos() { lock (_motivos) _motivos.Clear(); }

    /// <summary>Garante PCEB.ctb na pasta de estilos do CAD e na lista do validador; devolve o nome a usar.</summary>
    private static string GarantirPceb(PlotSettingsValidator v, PlotSettings ps, List<string> disponiveis)
    {
        var nome = EstiloPlotagemPadrao.NomeArquivo;
        if (disponiveis.Any(d => string.Equals(d, nome, StringComparison.OrdinalIgnoreCase))) return nome;
        var pasta = PastaEstilos()
                    ?? throw new ErroLotePdfException(CausaErroPdf.CtbNaoEncontrado, "ctb não encontrado e a pasta de estilos de plotagem do CAD não foi achada para gravar " + nome);
        var (caminho, criado) = EstiloPlotagemPadrao.GarantirNaPasta(pasta);
        if (criado) Infra.Log.Info("ARMPDFLOTE: gravado " + caminho);
        v.RefreshLists(ps);
        if (!NaLista(nome))
        {
            // o GstarCAD guarda a lista de estilos da sessão: regravar a pasta nas preferências faz o CAD reler a pasta
            try
            {
                var (files, prop, atual) = PreferenciaEstilos();
                if (files != null)
                {
                    files.GetType().InvokeMember(prop, System.Reflection.BindingFlags.SetProperty, null, files,
                        new object[] { string.IsNullOrWhiteSpace(atual) ? pasta : atual });
                    v.RefreshLists(ps);
                }
            }
            catch (Exception ex) { Infra.Log.Erro("ARMPDFLOTE: reler a pasta de estilos", ex); }
        }
        if (!NaLista(nome))
            throw new ErroLotePdfException(CausaErroPdf.CtbNaoEncontrado,
                $"ctb não encontrado; {nome} foi gravado em {pasta}, mas este CAD só o enxerga depois de reaberto — feche e abra o CAD e exporte de novo");
        return nome;
    }

    /// <summary>
    /// Ao carregar o plugin: grava PCEB.ctb na pasta de estilos do CAD se ainda não existir (nunca sobrescreve). O GstarCAD lê a
    /// lista de estilos uma vez por sessão (conferido em 17/11: gravar no meio da sessão, RefreshLists e regravar
    /// PrinterStyleSheetPath não fazem ele enxergar o arquivo novo), então o arquivo tem de estar lá antes da 1ª plotagem.
    /// </summary>
    public static void GarantirPcebAoCarregar()
    {
        try
        {
            var pasta = PastaEstilos();
            if (pasta == null) { Infra.Log.Info("ARMPDFLOTE: pasta de estilos de plotagem não achada ao carregar; PCEB.ctb não gravado"); return; }
            var (caminho, criado) = EstiloPlotagemPadrao.GarantirNaPasta(pasta);
            if (criado) Infra.Log.Info("ARMPDFLOTE: gravado " + caminho);
        }
        catch (Exception ex) { Infra.Log.Erro("ARMPDFLOTE: gravar PCEB.ctb ao carregar", ex); }
    }

    private static bool NaLista(string nome) => EstilosDisponiveis().Any(d => string.Equals(d, nome, StringComparison.OrdinalIgnoreCase));

    /// <summary>Nomes da propriedade da pasta de estilos em Preferences.Files (o GstarCAD 2021 não tem o 1º: DISP_E_UNKNOWNNAME).</summary>
    private static readonly string[] PropsEstilos = { "PrinterStyleSheetPath", "PrintStyleSheetPath", "PlotStyleSheetPath" };

    /// <summary>
    /// Objeto Preferences.Files, o nome da propriedade que respondeu e o valor (a 1ª de <paramref name="nomes"/> que existir).
    /// (null, …) = nenhuma serviu.
    /// </summary>
    private static (object Files, string Propriedade, string Valor) Preferencia(string[] nomes)
    {
        object prefs = CadApp.Preferences;
        object files = prefs.GetType().InvokeMember("Files", System.Reflection.BindingFlags.GetProperty, null, prefs, null);
        foreach (var nome in nomes)
        {
            try
            {
                var v = files.GetType().InvokeMember(nome, System.Reflection.BindingFlags.GetProperty, null, files, null);
                return (files, nome, Convert.ToString(v));
            }
            catch { }
        }
        return (null, null, null);
    }

    private static (object Files, string Propriedade, string Valor) PreferenciaEstilos() => Preferencia(PropsEstilos);

    /// <summary>
    /// Pasta de configuração do CAD em uso: Preferences.Files.&lt;propriedade&gt; (objeto COM de preferências, igual nos três CADs);
    /// senão o valor <paramref name="valorPerfil"/> do perfil corrente no registro (&lt;raiz do produto&gt;\Profiles\&lt;CPROFILE&gt;\General;
    /// é o que serve no GstarCAD 2021). null = não achada.
    /// </summary>
    internal static string PastaDoCad(string[] propriedades, string valorPerfil)
    {
        try
        {
            var (files, prop, valor) = Preferencia(propriedades);
            var achada = PrimeiraPasta(valor);
            if (achada != null) return achada;
            Infra.Log.Info(files == null ? $"ARMPDFLOTE: Preferences.Files sem {string.Join("/", propriedades)}" : $"ARMPDFLOTE: Preferences.Files.{prop} = \"{valor}\" (não serve)");
        }
        catch (Exception ex) { Infra.Log.Erro("ARMPDFLOTE: preferências " + string.Join("/", propriedades), ex); }
        try
        {
            var raiz = HostApplicationServices.Current.UserRegistryProductRootKey;
            var perfil = Convert.ToString(CadApp.GetSystemVariable("CPROFILE"));
            using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey($@"{raiz}\Profiles\{perfil}\General");
            var valor = k?.GetValue(valorPerfil) as string;
            var achada = PrimeiraPasta(Environment.ExpandEnvironmentVariables(valor ?? ""));
            if (achada != null) return achada;
            Infra.Log.Info($@"ARMPDFLOTE: {raiz}\Profiles\{perfil}\General\{valorPerfil} = ""{valor}"" (não serve)");
        }
        catch (Exception ex) { Infra.Log.Erro("ARMPDFLOTE: registro do perfil " + valorPerfil, ex); }
        return null;
    }

    /// <summary>
    /// Pasta de estilos de plotagem (ctb) do CAD em uso: <see cref="PastaDoCad"/> (preferências; no GstarCAD 2021, o
    /// PrinterStyleSheetDir do perfil) e, por último, a pasta de um ctb da lista achado pelo FindFile do CAD.
    /// </summary>
    public static string PastaEstilos()
    {
        var p = PastaDoCad(PropsEstilos, "PrinterStyleSheetDir");
        if (p != null) return p;
        try
        {
            var db = HostApplicationServices.WorkingDatabase;
            foreach (var s in EstilosDisponiveis())
            {
                string achado = null;
                try { achado = HostApplicationServices.Current.FindFile(s, db, FindFileHint.Default); } catch { }
                if (!string.IsNullOrEmpty(achado) && System.IO.File.Exists(achado)) return System.IO.Path.GetDirectoryName(achado);
            }
        }
        catch (Exception ex) { Infra.Log.Erro("ARMPDFLOTE: pasta de estilos pelo FindFile", ex); }
        return null;
    }

    private static string PrimeiraPasta(string lista)
    {
        foreach (var p in (lista ?? "").Split(';'))
        {
            var c = p.Trim().TrimEnd('\\', '/');
            if (c.Length > 0 && System.IO.Directory.Exists(c)) return c;
        }
        return null;
    }
}
