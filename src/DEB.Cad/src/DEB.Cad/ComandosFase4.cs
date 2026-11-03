using System;
using System.Globalization;
using System.Linq;
using DEB.Cad.Barra;
using DEB.Cad.Infra;
using DEB.Core.Normas;
using DEB.Core.Tabelas;

[assembly: Rt.CommandClass(typeof(DEB.Cad.ComandosFase4))]

namespace DEB.Cad;

/// <summary>Traspasse (ARMDIVIDIR), treliças e telas soldadas (fase 4).</summary>
public sealed class ComandosFase4
{
    private const Rt.CommandFlags Flags = Rt.CommandFlags.Modal | Rt.CommandFlags.UsePickSet;

    private static string F(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);

    /// <summary>
    /// ARMDIVIDIR: barra → mostra o traspasse e os trechos válidos → clique no ponto (o centro do
    /// traspasse) ou Enter para dividir pelo comprimento comercial. Opções: concreto, aderência,
    /// % emendada, modo (calculado/fixo).
    /// </summary>
    [Rt.CommandMethod("ARMDIVIDIR", Flags)]
    public void Dividir() => Comandos.Executar("ARMDIVIDIR", (doc, ed, db, cfg) =>
    {
        var barraId = Comandos.SelecionarBarra(ed, db, "\nSelecione a armadura a dividir:");
        if (barraId.IsNull) return;
        while (true)
        {
            Core.Modelo.DadosArmadura d;
            System.Collections.Generic.List<Core.Modelo.GeoSegmento> geo;
            double cpu = Desenho.CmPorUnidade(db, cfg);
            using (var tr = new TransacaoSegura(db))
            {
                var pl = (Polyline)tr.GetObject(barraId, OpenMode.ForRead);
                d = XDataCad.LerBarra(pl);
                geo = GeometriaBarra.Ler(pl, cpu);
                if (pl.Closed) { ed.WriteMessage("\nBarra fechada (estribo) não é dividida."); return; }
            }
            if (TabelaBarras.EhCordoalha(d.Aco)) { ed.WriteMessage("\nCordoalha de protensão não leva traspasse."); return; }
            if (!d.EhBarraComum) { ed.WriteMessage("\nTreliças e telas não são divididas."); return; }
            double l0 = ServicoTraspasse.Traspasse(d, cfg);
            double comercial = ServicoTraspasse.Comercial(d, cfg);
            var validos = PlanoDivisao.IntervalosValidos(geo, l0);
            ed.WriteMessage($"\nTraspasse ø{F(d.Bitola)} {d.Aco}: {F(l0)} cm ({cfg.Traspasse.Resumo(d.Bitola, d.Aco, cfg.Barras)}).");
            ed.WriteMessage(validos.Count == 0
                ? "\nNenhum trecho reto comporta o traspasse."
                : "\nCentro do traspasse pode ficar em (cm a partir do início): " + string.Join("; ", validos.Select(v => $"{F(v.ini)} a {F(v.fim)}")));

            var po = new PromptPointOptions($"\nPonto da divisão (Enter = automática a cada {F(comercial)} cm) ou")
                { AllowNone = true, AppendKeywordsToMessage = true };
            Comandos.AdicionarPalavras(po.Keywords, "Concreto", "Aderencia", "Emendadas", "Modo");
            var r = ed.GetPoint(po);
            if (r.Status == PromptStatus.Keyword)
            {
                var t = cfg.Traspasse; // a do desenho, se ele tiver configuração própria
                switch (r.StringResult)
                {
                    case "Concreto": { var v = Comandos.PedirReal(ed, "fck do concreto (MPa)", t.Fck, 10); if (v != null) t.Fck = v.Value; break; }
                    case "Aderencia": t.Aderencia = t.Aderencia == SituacaoAderencia.Boa ? SituacaoAderencia.Ma : SituacaoAderencia.Boa; break;
                    case "Emendadas": { var v = Comandos.PedirReal(ed, "% de barras emendadas na mesma seção", t.PercentualEmendado, 1); if (v != null) t.PercentualEmendado = v.Value; break; }
                    case "Modo": t.Modo = t.Modo == ModoTraspasse.Calculado ? ModoTraspasse.Fixo : ModoTraspasse.Calculado; break;
                }
                Plugin.SalvarConfig();
                continue;
            }
            if (r.Status == PromptStatus.None)
            {
                using var tr = new TransacaoSegura(db);
                var pecas = ServicoTraspasse.DividirAutomatico(tr, (Polyline)tr.GetObject(barraId, OpenMode.ForRead), cfg);
                tr.Commit();
                ed.WriteMessage(pecas == null ? "\nNão há como dividir: falta trecho reto para o traspasse."
                    : pecas.Count == 1 ? $"\nA barra já cabe no comprimento comercial ({F(comercial)} cm)."
                    : $"\nBarra dividida em {pecas.Count} peças com traspasse de {F(l0)} cm.");
                return;
            }
            if (r.Status != PromptStatus.OK) return;
            using (var tr = new TransacaoSegura(db))
            {
                var pl = (Polyline)tr.GetObject(barraId, OpenMode.ForRead);
                var w = r.Value.TransformBy(ed.CurrentUserCoordinateSystem);
                var perto = pl.GetClosestPointTo(new Point3d(w.X, w.Y, pl.Elevation), false);
                double s = pl.GetDistAtPoint(perto) * cpu;
                var c = PlanoDivisao.MaisProximo(geo, l0, s);
                if (c == null) { ed.WriteMessage("\nNenhum trecho reto comporta o traspasse."); return; }
                if (Math.Abs(c.Value - s) > 0.5) ed.WriteMessage($"\nPonto ajustado para {F(c.Value)} cm (o traspasse precisa caber no trecho reto).");
                ServicoTraspasse.Dividir(tr, pl, c.Value, cfg);
                tr.Commit();
            }
            ed.WriteMessage($"\nBarra dividida: 2 barras com traspasse de {F(l0)} cm. Use ARMRAATUALIZAR para numerar.");
            return;
        }
    });

    /// <summary>
    /// ARMCORTEDOBRA (seção 6): atualiza a relação e grava a lista de corte e dobra ao lado do DWG
    /// (&lt;desenho&gt;_corte_dobra.csv para Excel/ERP e .html com o desenho de cada posição).
    /// </summary>
    [Rt.CommandMethod("ARMCORTEDOBRA")]
    public void CorteDobra() => Comandos.Executar("ARMCORTEDOBRA", (doc, ed, db, cfg) =>
    {
        Core.RelacaoAco.ResultadoRA r;
        using (var tr = new TransacaoSegura(db))
        {
            Relacao.ServicoRA.Atualizar(db, tr, cfg);
            r = Relacao.ServicoRA.ResultadoDoDesenho(db, tr, cfg);
            tr.Commit();
        }
        if (r.Linhas.Count(l => !l.EhTrelica) == 0) { ed.WriteMessage("\nNenhuma armadura no desenho."); return; }
        var (csv, html) = GravarCorteDobra(doc, r, cfg);
        ed.WriteMessage($"\nLista de corte e dobra gravada:\n  {csv}\n  {html}");
        if (Environment.GetEnvironmentVariable("ARM_AUTOTESTE_SAIDA") == null)
            try { System.Diagnostics.Process.Start(html); } catch (Exception ex) { Log.Erro("abrir HTML", ex); }
    });

    internal static (string csv, string html) GravarCorteDobra(Document doc, Core.RelacaoAco.ResultadoRA r, Core.Configuracao.ConfiguracaoArmadura cfg)
    {
        // ao lado do DWG; desenho ainda não salvo → Documentos
        string nome = doc.Name ?? "";
        string pasta = System.IO.Path.IsPathRooted(nome) && System.IO.File.Exists(nome)
            ? System.IO.Path.GetDirectoryName(nome)
            : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        string baseNome = System.IO.Path.GetFileNameWithoutExtension(nome);
        if (string.IsNullOrWhiteSpace(baseNome)) baseNome = "desenho";
        string csv = System.IO.Path.Combine(pasta, baseNome + "_corte_dobra.csv");
        string html = System.IO.Path.Combine(pasta, baseNome + "_corte_dobra.html");
        System.IO.File.WriteAllText(csv, Core.RelacaoAco.ExportadorCorteDobra.Csv(r, cfg), new System.Text.UTF8Encoding(true));
        System.IO.File.WriteAllText(html, Core.RelacaoAco.ExportadorCorteDobra.Html(r, cfg, "Corte e dobra — " + baseNome),
            new System.Text.UTF8Encoding(false));
        return (csv, html);
    }

    private static string PedirModelo(Editor ed, string titulo, System.Collections.Generic.IEnumerable<string> nomes, string atual,
        Func<string, bool> existe)
    {
        ed.WriteMessage($"\n{titulo}: {string.Join("  ", nomes)}");
        var r = ed.GetString(new PromptStringOptions($"\nModelo <{atual}>:") { AllowSpaces = true, DefaultValue = atual, UseDefaultValue = true });
        if (r.Status != PromptStatus.OK) return null;
        var v = string.IsNullOrWhiteSpace(r.StringResult) ? atual : r.StringResult.Trim();
        if (!existe(v)) { ed.WriteMessage("\nModelo fora do catálogo (ARMCONFIG)."); return null; }
        return v;
    }

    /// <summary>ARMTRELICA (seção 3.1): 2 pontos = banzo inferior; o desenho vem do modelo do catálogo.</summary>
    [Rt.CommandMethod("ARMTRELICA")]
    public void Trelica() => Comandos.Executar("ARMTRELICA", (doc, ed, db, cfg) =>
    {
        Point3d p1;
        while (true)
        {
            ed.WriteMessage($"\nAtual: {Plugin.Config.TrelicaCorrente}, quantidade {cfg.QuantidadeCorrente}" +
                            (cfg.EspacamentoCorrente > 0 ? $", c/{F(cfg.EspacamentoCorrente)}" : ""));
            var po = new PromptPointOptions("\nInício do banzo inferior ou") { AppendKeywordsToMessage = true };
            Comandos.AdicionarPalavras(po.Keywords, "Modelo", "Quantidade", "Espacamento");
            var r = ed.GetPoint(po);
            if (r.Status == PromptStatus.Keyword)
            {
                if (r.StringResult == "Modelo")
                {
                    var m = PedirModelo(ed, "Treliças", cfg.Trelicas.Select(t => t.Nome), Plugin.Config.TrelicaCorrente,
                        n => cfg.Trelicas.Trelica(n) != null);
                    if (m != null) Plugin.Config.TrelicaCorrente = cfg.Trelicas.Trelica(m).Nome;
                }
                else if (r.StringResult == "Quantidade")
                {
                    var q = Comandos.PedirInteiro(ed, "Quantidade", cfg.QuantidadeCorrente, 1);
                    if (q != null) Plugin.Config.QuantidadeCorrente = cfg.QuantidadeCorrente = q.Value;
                }
                else
                {
                    var s = Comandos.PedirReal(ed, "Espaçamento (cm, 0 = não indicar)", cfg.EspacamentoCorrente, 0);
                    if (s != null) Plugin.Config.EspacamentoCorrente = cfg.EspacamentoCorrente = s.Value;
                }
                Plugin.SalvarConfig();
                continue;
            }
            if (r.Status != PromptStatus.OK) return;
            p1 = r.Value;
            break;
        }
        var r2 = ed.GetPoint(new PromptPointOptions("\nFim do banzo inferior:") { UseBasePoint = true, BasePoint = p1 });
        if (r2.Status != PromptStatus.OK) return;
        var a = p1.TransformBy(ed.CurrentUserCoordinateSystem);
        var b = r2.Value.TransformBy(ed.CurrentUserCoordinateSystem);
        var modelo = cfg.Trelicas.Trelica(Plugin.Config.TrelicaCorrente) ?? cfg.Trelicas.First();
        using var tr = new TransacaoSegura(db);
        CriarTrelica(tr, db, cfg, new Point2d(a.X, a.Y), new Point2d(b.X, b.Y), modelo.Nome, cfg.QuantidadeCorrente, cfg.EspacamentoCorrente);
        tr.Commit();
        ed.WriteMessage($"\nTreliça {modelo.Nome} criada ({F(a.DistanceTo(b) * Desenho.CmPorUnidade(db, cfg))} cm).");
    });

    internal static ObjectId CriarTrelica(Transaction tr, Database db, Core.Configuracao.ConfiguracaoArmadura cfg, Point2d a, Point2d b,
        string modelo, int quantidade, double espacamento)
    {
        var m = cfg.Trelicas.Trelica(modelo);
        var pl = new Polyline();
        pl.SetDatabaseDefaults(db);
        pl.AddVertexAt(0, a, 0, 0, 0);
        pl.AddVertexAt(1, b, 0, 0, 0);
        var esp = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
        esp.AppendEntity(pl);
        tr.AddNewlyCreatedDBObject(pl, true);
        ServicoBarra.Criar(tr, pl, new Core.Modelo.DadosArmadura
        {
            Trelica = m?.Nome ?? modelo, Aco = "CA-60", Bitola = m?.PhiInferior ?? 0, Quantidade = Math.Max(1, quantidade),
            Espacamento = espacamento, Escala = cfg.Escala,
        }, cfg);
        return pl.ObjectId;
    }

    /// <summary>ARMTELA: 2 cantos = painel de tela soldada (entra no resumo por massa).</summary>
    [Rt.CommandMethod("ARMTELA")]
    public void Tela() => Comandos.Executar("ARMTELA", (doc, ed, db, cfg) =>
    {
        Point3d c1;
        while (true)
        {
            ed.WriteMessage($"\nAtual: {Plugin.Config.TelaCorrente}");
            var po = new PromptPointOptions("\nPrimeiro canto do painel ou") { AppendKeywordsToMessage = true };
            Comandos.AdicionarPalavras(po.Keywords, "Modelo");
            var r = ed.GetPoint(po);
            if (r.Status == PromptStatus.Keyword)
            {
                var m = PedirModelo(ed, "Telas", cfg.Telas.Select(t => t.Nome), Plugin.Config.TelaCorrente, n => cfg.Telas.Tela(n) != null);
                if (m != null) { Plugin.Config.TelaCorrente = cfg.Telas.Tela(m).Nome; Plugin.SalvarConfig(); }
                continue;
            }
            if (r.Status != PromptStatus.OK) return;
            c1 = r.Value;
            break;
        }
        var r2 = ed.GetCorner("\nCanto oposto:", c1);
        if (r2.Status != PromptStatus.OK) return;
        var a = c1.TransformBy(ed.CurrentUserCoordinateSystem);
        var b = r2.Value.TransformBy(ed.CurrentUserCoordinateSystem);
        using var tr = new TransacaoSegura(db);
        CriarTela(tr, db, cfg, new Point2d(a.X, a.Y), new Point2d(b.X, b.Y), Plugin.Config.TelaCorrente, 1);
        tr.Commit();
        ed.WriteMessage($"\nTela {Plugin.Config.TelaCorrente} criada.");
    });

    internal static ObjectId CriarTela(Transaction tr, Database db, Core.Configuracao.ConfiguracaoArmadura cfg, Point2d a, Point2d b,
        string modelo, int quantidade)
    {
        var m = cfg.Telas.Tela(modelo);
        double x0 = Math.Min(a.X, b.X), x1 = Math.Max(a.X, b.X), y0 = Math.Min(a.Y, b.Y), y1 = Math.Max(a.Y, b.Y);
        var pl = new Polyline();
        pl.SetDatabaseDefaults(db);
        pl.AddVertexAt(0, new Point2d(x0, y0), 0, 0, 0);
        pl.AddVertexAt(1, new Point2d(x1, y0), 0, 0, 0);
        pl.AddVertexAt(2, new Point2d(x1, y1), 0, 0, 0);
        pl.AddVertexAt(3, new Point2d(x0, y1), 0, 0, 0);
        pl.Closed = true;
        var esp = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
        esp.AppendEntity(pl);
        tr.AddNewlyCreatedDBObject(pl, true);
        ServicoBarra.Criar(tr, pl, new Core.Modelo.DadosArmadura
        {
            Tela = m?.Nome ?? modelo, Aco = m?.Aco ?? "CA-60", Bitola = m?.PhiLongitudinal ?? 0,
            Quantidade = Math.Max(1, quantidade), Escala = cfg.Escala, TelaIdentificada = cfg.TelaIdentificada,
        }, cfg);
        // identificada: o T sai da renumeração do desenho (painéis iguais = mesmo T)
        if (cfg.TelaIdentificada) Relacao.ServicoRA.Atualizar(db, tr, cfg);
        return pl.ObjectId;
    }
}
