using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DEB.Cad.Barra;
using DEB.Cad.Infra;
using DEB.Core.Modelo;
using DEB.Core.Persistencia;

[assembly: Rt.CommandClass(typeof(DEB.Cad.Testes.AutoTesteJoin))]

namespace DEB.Cad.Testes;

/// <summary>
/// DIAGNÓSTICO do JOIN / PEDIT→Juntar sobre armaduras (estudo de 2026-11-12; nada é corrigido aqui).
/// ARMTESTEJOINLOG cria, para cada caso, uma barra de teste (gancho no início, cota movida, faixa ligada) e o objeto
/// a juntar, dispara o comando nativo por SendStringToExecute e, no passo seguinte (ARMTESTEJOINPASSO), registra:
/// eventos do Database (anexado/apagado/modificado), início/fim/cancelamento de comandos, prompts, o objeto que
/// sobreviveu, o XData, a ordem dos vértices e o estado da descrição/cotas/faixa (vínculos 1005).
/// Cada caso roda duas vezes: "cru" (Sincronizador suspenso: o que o CAD faz sozinho) e "plugin" (como hoje).
/// Relatório: %AppData%\PCEB\DEB.Cad\logs\autoteste_join_&lt;CAD&gt;.txt. Com ARM_AUTOTESTE_SAIDA, fecha a instância no fim.
/// </summary>
public sealed class AutoTesteJoin
{
#if AUTOCAD
    private const string Cad = "AutoCAD";
#elif ZWCAD
    private const string Cad = "ZWCAD";
#else
    private const string Cad = "GstarCAD";
#endif

    private sealed class Caso
    {
        public string Id = "", Titulo = "";
        public bool Cru;
        /// <summary>Monta os objetos do caso e devolve o texto a mandar para a linha de comando.</summary>
        public Func<Transaction, Database, Core.Configuracao.ConfiguracaoArmadura, Point2d, string> Preparar;
        /// <summary>ARMTESTEJOIN: verificações de passa/falha depois do comando (só no modo "plugin").</summary>
        public Action<Transaction, Database, Core.Configuracao.ConfiguracaoArmadura> Verificar;
        /// <summary>Só no ARMTESTEJOIN (U, ARMCONVERTER): não entra no diagnóstico.</summary>
        public bool SoTeste;
    }

    // ARMTESTEJOIN (passa/falha) × ARMTESTEJOINLOG (só diagnóstico)
    private static bool _teste;
    private static int _ok, _falhas;
    private static ObjectId _tabela = ObjectId.Null;

    private static void Checar(bool cond, string descricao)
    {
        if (cond) _ok++; else _falhas++;
        Rel.AppendLine((cond ? "  [OK]    " : "  [FALHA] ") + descricao);
    }

    private static readonly List<Caso> Casos = new List<Caso>();
    private static int _passo;
    private static IDisposable _suspensao;
    private static Document _doc;
    private static readonly StringBuilder Rel = new StringBuilder();
    private static readonly List<string> Eventos = new List<string>();
    private static int _seq;
    private static object _peditAccept;
    // objetos do caso atual: nome → handle (A = barra, L = objeto a juntar, B = 2ª barra, partes de A/B)
    private static readonly Dictionary<string, string> Hs = new Dictionary<string, string>();
    private static readonly Dictionary<string, Point2d[]> VertAntes = new Dictionary<string, Point2d[]>();
    private static HashSet<long> _handlesAntes = new HashSet<long>();
    private static Point2d _base;

    private static string Arquivo => Path.Combine(Log.PastaLogs, _teste ? $"autoteste_join_teste_{Cad}.txt" : $"autoteste_join_{Cad}.txt");
    private static string F(double v) => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
    private static string P(Point2d p) => $"({F(p.X - _base.X)},{F(p.Y - _base.Y)})";
    private static string P(Point3d p) => $"({F(p.X - _base.X)},{F(p.Y - _base.Y)})";
    private static string Sel(string h) => $"(handent \"{h}\")";

    private static void Gravar()
    {
        try
        {
            Directory.CreateDirectory(Log.PastaLogs);
            File.WriteAllText(Arquivo, Rel.ToString(), Encoding.UTF8);
        }
        catch (Exception ex) { Log.Erro("ARMTESTEJOINLOG: gravar", ex); }
    }

    // ------------------------------------------------------------------ comandos

    /// <summary>
    /// Autoteste do JOIN / PEDIT→Juntar (passa/falha). Relatório: logs\autoteste_join_teste_&lt;CAD&gt;.txt e também
    /// logs\autoteste.txt (o que o tools\autoteste_autocad.ps1 -Comando ARMTESTEJOIN mostra no fim).
    /// </summary>
    [Rt.CommandMethod("ARMTESTEJOIN")]
    public void Teste() => Comandos.Executar("ARMTESTEJOIN", (doc, ed, db, cfg) =>
    {
        _teste = true; _ok = _falhas = 0;
        using (var tr = db.TransactionManager.StartTransaction())
        {
            _tabela = Relacao.ServicoRA.Inserir(db, tr, new Point3d(12000, -9000, 0), cfg);
            tr.Commit();
        }
        Comecar(doc, db, cfg);
    }, suspender: false);

    [Rt.CommandMethod("ARMTESTEJOINLOG")]
    public void Iniciar() => Comandos.Executar("ARMTESTEJOINLOG", (doc, ed, db, cfg) =>
    {
        _teste = false;
        Comecar(doc, db, cfg);
    }, suspender: false);

    private static void Comecar(Document doc, Database db, Core.Configuracao.ConfiguracaoArmadura cfg)
    {
        Rel.Clear(); Eventos.Clear(); _passo = 0; _seq = 0;
        _doc = doc;
        Rel.AppendLine($"Diagnóstico JOIN/PEDIT — {Cad} {Ler("ACADVER")} — {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        _peditAccept = Ler("PEDITACCEPT");
        Rel.AppendLine($"PEDITACCEPT = {_peditAccept ?? "(não existe)"}; PICKFIRST = {Ler("PICKFIRST")}; PICKADD = {Ler("PICKADD")}; " +
                       $"unidades: 1 un = {F(Desenho.CmPorUnidade(db, cfg))} cm");
        Rel.AppendLine("Coordenadas relativas ao canto do caso. 'cru' = Sincronizador suspenso; 'plugin' = como hoje.");
        Rel.AppendLine("Barra A: v0 (0,g) → v1 (0,0) → v2 (300,0); seg0 = gancho 90°, seg1 com cota movida (dx 10, dy 5); faixa ortogonal ligada.");
        MontarCasos();
        Ligar(doc);
        Gravar();
        Proximo(doc, db, cfg);
    }

    [Rt.CommandMethod("ARMTESTEJOINPASSO")]
    public void Passo() => Comandos.Executar("ARMTESTEJOINPASSO", (doc, ed, db, cfg) =>
    {
        Proximo(doc, db, cfg);
    }, suspender: false);

    private static object Ler(string variavel)
    {
        try { return CadApp.GetSystemVariable(variavel); } catch { return null; }
    }

    private static void Proximo(Document doc, Database db, Core.Configuracao.ConfiguracaoArmadura cfg)
    {
        try
        {
            if (_passo > 0)
            {
                var caso = Casos[_passo - 1];
                _suspensao?.Dispose();
                _suspensao = null;
                Rel.AppendLine($"  ao voltar: CMDNAMES = '{Ler("CMDNAMES")}', CMDACTIVE = {Ler("CMDACTIVE")}");
                Rel.AppendLine("  eventos (na ordem):");
                foreach (var e in Eventos) Rel.AppendLine("    " + e);
                Eventos.Clear();
                Conferir(db, cfg, caso);
                if (_teste && caso.Verificar != null)
                {
                    Rel.AppendLine("  verificações:");
                    try
                    {
                        using (Sincronizador.Suspender(doc))
                        using (var tr = db.TransactionManager.StartTransaction())
                        {
                            caso.Verificar(tr, db, cfg);
                            tr.Commit();
                        }
                    }
                    catch (Exception ex) { Checar(false, $"{caso.Id}: exceção na verificação — {ex.Message}"); }
                }
                Limpar(doc, db);
                Gravar();
            }
            if (_passo >= Casos.Count)
            {
                Desligar(doc);
                Rel.AppendLine();
                Rel.AppendLine($"Fim. PEDITACCEPT no fim = {Ler("PEDITACCEPT") ?? "(não existe)"} (não alterado pelo teste).");
                if (_teste)
                {
                    Rel.Insert(0, $"ARMTESTEJOIN {Cad} — {_ok} ok, {_falhas} falha(s)\r\n");
                    try { File.WriteAllText(Path.Combine(Log.PastaLogs, "autoteste.txt"), Rel.ToString(), Encoding.UTF8); } catch { }
                    Log.Info($"autoteste: ARMTESTEJOIN fim ({_ok} ok, {_falhas} falha(s))");
                }
                Gravar();
                Log.Info("autoteste: ARMTESTEJOINLOG fim");
                if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ARM_AUTOTESTE_SAIDA")))
                    doc.SendStringToExecute("ARMTESTEFIM\n", true, false, true);
                return;
            }
            var c = Casos[_passo++];
            Hs.Clear(); VertAntes.Clear();
            _base = new Point2d(12000 + 1500 * (_passo - 1), -5000);
            string texto;
            using (Sincronizador.Suspender(doc))
            using (var tr = db.TransactionManager.StartTransaction())
            {
                texto = c.Preparar(tr, db, cfg, _base);
                tr.Commit();
            }
            using (var tr = db.TransactionManager.StartTransaction())
            {
                _handlesAntes = new HashSet<long>(((BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead))
                    .Cast<ObjectId>().Select(i => i.Handle.Value));
                Rel.AppendLine();
                Rel.AppendLine($"=== Caso {c.Id} [{(c.Cru ? "cru" : "plugin")}] — {c.Titulo}");
                Rel.AppendLine("  antes: " + string.Join("; ", Hs.Select(kv => $"{kv.Key}={kv.Value}")));
                foreach (var kv in VertAntes) Rel.AppendLine($"  vértices de {kv.Key}: " + string.Join(" ", kv.Value.Select(P)));
                var dA = LerBarraH(tr, db, Hs.TryGetValue("A", out var ha) ? ha : "");
                if (dA != null) Rel.AppendLine("  A antes: " + ResumoBarra(tr, db, ha, dA));
                tr.Commit();
            }
            Eventos.Clear(); // a criação dos objetos do caso não interessa
            Rel.AppendLine("  enviado: " + texto.Replace("\n", "⏎").Replace("\x1B", "ESC"));
            Gravar();
            if (c.Cru) _suspensao = Sincronizador.Suspender(doc);
            _seq = 0;
            doc.SendStringToExecute(texto, true, false, true);
            // ESC de segurança: se o comando terminou certo, cai no "Command:" e não faz nada; se ficou esperando,
            // o log mostra CommandCancelled (Enter a menos) ou um 2º CommandWillStart (Enter sobrando).
            // (no GstarCAD o ESC tem de ir sozinho: "\x1B\x1BARM…" virou o comando desconhecido "\x1BRM…")
            doc.SendStringToExecute("\x1B\x1B", true, false, true);
            doc.SendStringToExecute("ARMTESTEJOINPASSO\n", true, false, true);
        }
        catch (Exception ex)
        {
            Log.Erro("ARMTESTEJOINLOG", ex);
            Rel.AppendLine("ERRO: " + ex);
            Gravar();
            _suspensao?.Dispose();
            _suspensao = null;
            Desligar(doc);
        }
    }

    // ------------------------------------------------------------------ casos

    private static void MontarCasos()
    {
        Casos.Clear();
        var lista = new List<Caso>
        {
            new Caso { Id = "a", Verificar = (tr, db, cfg) => ConferirJuntada(tr, db, cfg, "A", new (double, double)[] { (0,GanchoA), (0,0), (300,0), (500,0) }, new[] { FormatoSegmento.Gancho90, FormatoSegmento.Linha, FormatoSegmento.Linha }, 0, 509, ra: true), Titulo = "JOIN: armadura 1º, LINE colinear encostada no FIM (sai do fim)", Preparar = (tr, db, cfg, b) =>
            {
                CriarA(tr, db, cfg, b);
                Linha(tr, db, "L", b + new Vector2d(300, 0), b + new Vector2d(500, 0));
                return $"_.JOIN {Sel(Hs["A"])}\n{Sel(Hs["L"])}\n\n";
            }},
            new Caso { Id = "a2", Verificar = (tr, db, cfg) => ConferirJuntada(tr, db, cfg, "A", new (double, double)[] { (0,GanchoA), (0,0), (300,0), (300,80) }, new[] { FormatoSegmento.Gancho90, FormatoSegmento.Linha, FormatoSegmento.Linha }, 0, 387), Titulo = "JOIN: armadura 1º, LINE perpendicular desenhada CHEGANDO no fim", Preparar = (tr, db, cfg, b) =>
            {
                CriarA(tr, db, cfg, b);
                Linha(tr, db, "L", b + new Vector2d(300, 80), b + new Vector2d(300, 0));
                return $"_.JOIN {Sel(Hs["A"])}\n{Sel(Hs["L"])}\n\n";
            }},
            new Caso { Id = "b", Verificar = (tr, db, cfg) => ConferirJuntada(tr, db, cfg, "A", new (double, double)[] { (0,GanchoA), (0,0), (300,0), (500,0) }, new[] { FormatoSegmento.Gancho90, FormatoSegmento.Linha, FormatoSegmento.Linha }, 0, 509), Titulo = "JOIN: LINE 1º, armadura depois (LINE no fim)", Preparar = (tr, db, cfg, b) =>
            {
                CriarA(tr, db, cfg, b);
                Linha(tr, db, "L", b + new Vector2d(300, 0), b + new Vector2d(500, 0));
                return $"_.JOIN {Sel(Hs["L"])}\n{Sel(Hs["A"])}\n\n";
            }},
            new Caso { Id = "c", Verificar = (tr, db, cfg) => ConferirJuntada(tr, db, cfg, "A", new (double, double)[] { (-200,GanchoA), (0,GanchoA), (0,0), (300,0) }, new[] { FormatoSegmento.Linha, FormatoSegmento.Linha, FormatoSegmento.Linha }, 1, 507), Titulo = "JOIN: armadura 1º, LINE encostada no INÍCIO (ponta do gancho)", Preparar = (tr, db, cfg, b) =>
            {
                double g = CriarA(tr, db, cfg, b);
                Linha(tr, db, "L", b + new Vector2d(-200, g), b + new Vector2d(0, g));
                return $"_.JOIN {Sel(Hs["A"])}\n{Sel(Hs["L"])}\n\n";
            }},
            new Caso { Id = "c2", Verificar = (tr, db, cfg) => ConferirJuntada(tr, db, cfg, "A", new (double, double)[] { (-200,GanchoA), (0,GanchoA), (0,0), (300,0) }, new[] { FormatoSegmento.Linha, FormatoSegmento.Linha, FormatoSegmento.Linha }, 1, 507), Titulo = "JOIN: armadura 1º, LINE saindo do INÍCIO para fora (desenhada a partir da ponta do gancho)", Preparar = (tr, db, cfg, b) =>
            {
                double g = CriarA(tr, db, cfg, b);
                Linha(tr, db, "L", b + new Vector2d(0, g), b + new Vector2d(-200, g));
                return $"_.JOIN {Sel(Hs["A"])}\n{Sel(Hs["L"])}\n\n";
            }},
            new Caso { Id = "d", Verificar = (tr, db, cfg) => ConferirJuntada(tr, db, cfg, "A", new (double, double)[] { (0,GanchoA), (0,0), (300,0), (450,0), (450,60) }, new[] { FormatoSegmento.Gancho90, FormatoSegmento.Linha, FormatoSegmento.Linha, FormatoSegmento.Linha }, 0, 517), Titulo = "JOIN: armadura 1º, POLILINHA comum de 2 trechos encostada no fim", Preparar = (tr, db, cfg, b) =>
            {
                CriarA(tr, db, cfg, b);
                Poli(tr, db, "L", b + new Vector2d(300, 0), b + new Vector2d(450, 0), b + new Vector2d(450, 60));
                return $"_.JOIN {Sel(Hs["A"])}\n{Sel(Hs["L"])}\n\n";
            }},
            new Caso { Id = "e1", Verificar = (tr, db, cfg) => ConferirJuntada(tr, db, cfg, "A", new (double, double)[] { (0,GanchoA), (0,0), (300,0), (500,0) }, new[] { FormatoSegmento.Gancho90, FormatoSegmento.Linha, FormatoSegmento.Linha }, 0, 509), Titulo = "PEDIT armadura → Juntar → LINE no fim", Preparar = (tr, db, cfg, b) =>
            {
                CriarA(tr, db, cfg, b);
                Linha(tr, db, "L", b + new Vector2d(300, 0), b + new Vector2d(500, 0));
                return $"_.PEDIT {Sel(Hs["A"])}\n_J\n{Sel(Hs["L"])}\n\n\n";
            }},
            new Caso { Id = "e1i", Verificar = (tr, db, cfg) => ConferirJuntada(tr, db, cfg, "A", new (double, double)[] { (-200, GanchoA), (0, GanchoA), (0, 0), (300, 0) }, new[] { FormatoSegmento.Linha, FormatoSegmento.Linha, FormatoSegmento.Linha }, 1, 507),
                Titulo = "PEDIT armadura → Juntar → LINE no INÍCIO (ponta do gancho)", Preparar = (tr, db, cfg, b) =>
            {
                double g = CriarA(tr, db, cfg, b);
                Linha(tr, db, "L", b + new Vector2d(-200, g), b + new Vector2d(0, g));
                return $"_.PEDIT {Sel(Hs["A"])}\n_J\n{Sel(Hs["L"])}\n\n\n";
            }},
            new Caso { Id = "e2", Verificar = (tr, db, cfg) => ConferirJuntada(tr, db, cfg, "A", new (double, double)[] { (0,GanchoA), (0,0), (300,0), (500,0) }, new[] { FormatoSegmento.Gancho90, FormatoSegmento.Linha, FormatoSegmento.Linha }, 0, 509), Titulo = "PEDIT LINE (converter) → Juntar → armadura", Preparar = (tr, db, cfg, b) =>
            {
                CriarA(tr, db, cfg, b);
                Linha(tr, db, "L", b + new Vector2d(300, 0), b + new Vector2d(500, 0));
                // PEDITACCEPT = 1: converte sem perguntar; 0 ou inexistente: pergunta "transformar em polilinha? <S>"
                bool pergunta = !(_peditAccept is short s && s == 1) && !(_peditAccept is int i && i == 1);
                return $"_.PEDIT {Sel(Hs["L"])}\n" + (pergunta ? "_Y\n" : "") + $"_J\n{Sel(Hs["A"])}\n\n\n";
            }},
            new Caso { Id = "f", Verificar = (tr, db, cfg) => ConferirJuntada(tr, db, cfg, "B", new (double, double)[] { (0,GanchoA), (0,0), (300,0), (700,0), (700,GanchoA) }, new[] { FormatoSegmento.Linha, FormatoSegmento.Linha, FormatoSegmento.Linha, FormatoSegmento.Gancho90 }, 2, 718), Titulo = "JOIN de duas armaduras (A 1º; B, mais longa, começa no fim de A, gancho no fim de B)", Preparar = (tr, db, cfg, b) =>
            {
                CriarA(tr, db, cfg, b);
                CriarB(tr, db, cfg, b);
                return $"_.JOIN {Sel(Hs["A"])}\n{Sel(Hs["B"])}\n\n";
            }},
            new Caso { Id = "g", Verificar = (tr, db, cfg) => ConferirIntacta(tr, db, "g: linha que não encosta"), Titulo = "JOIN: armadura 1º, LINE que NÃO encosta (folga de 20)", Preparar = (tr, db, cfg, b) =>
            {
                CriarA(tr, db, cfg, b);
                Linha(tr, db, "L", b + new Vector2d(320, 0), b + new Vector2d(500, 0));
                return $"_.JOIN {Sel(Hs["A"])}\n{Sel(Hs["L"])}\n\n";
            }},
            new Caso { Id = "u", SoTeste = true, Verificar = (tr, db, cfg) => ConferirIntacta(tr, db, "U depois do JOIN"),
                Titulo = "JOIN (armadura 1º, LINE no fim) e um U logo depois", Preparar = (tr, db, cfg, b) =>
            {
                CriarA(tr, db, cfg, b);
                Linha(tr, db, "L", b + new Vector2d(300, 0), b + new Vector2d(500, 0));
                return $"_.JOIN {Sel(Hs["A"])}\n{Sel(Hs["L"])}\n\n_.U\n";
            }},
            new Caso { Id = "conv", SoTeste = true, Verificar = ConferirConversao,
                Titulo = "ARMCONVERTER com linhas soltas (cadeia invertida, isolada, fechada, ramificada)", Preparar = (tr, db, cfg, b) =>
            {
                Linha(tr, db, "L1", b + new Vector2d(0, 0), b + new Vector2d(100, 0));
                Linha(tr, db, "L2", b + new Vector2d(100, 50), b + new Vector2d(100, 0));   // desenhada ao contrário
                Linha(tr, db, "L3", b + new Vector2d(0, 200), b + new Vector2d(150, 200)); // isolada
                Linha(tr, db, "R1", b + new Vector2d(400, 0), b + new Vector2d(420, 0));    // retângulo fechado
                Linha(tr, db, "R2", b + new Vector2d(420, 50), b + new Vector2d(420, 0));
                Linha(tr, db, "R3", b + new Vector2d(420, 50), b + new Vector2d(400, 50));
                Linha(tr, db, "R4", b + new Vector2d(400, 50), b + new Vector2d(400, 0));
                Linha(tr, db, "X1", b + new Vector2d(600, 0), b + new Vector2d(700, 0));    // ramificada
                Linha(tr, db, "X2", b + new Vector2d(700, 0), b + new Vector2d(800, 0));
                Linha(tr, db, "X3", b + new Vector2d(700, 0), b + new Vector2d(700, 100));
                string adds = string.Join(" ", Hs.Values.Select(h => $"(ssadd {Sel(h)} ss)"));
                // pré-seleção (UsePickSet): o comando só pergunta "vira estribo?" — Enter = Sim
                return $"(progn (setq ss (ssadd)) {adds} (sssetfirst nil ss) (princ))\nARMCONVERTER\n\n";
            }},
        };
        foreach (var c in lista)
        {
            if (_teste) { Casos.Add(c); continue; }
            if (c.SoTeste) continue;
            Casos.Add(new Caso { Id = c.Id, Titulo = c.Titulo, Cru = true, Preparar = c.Preparar });
            Casos.Add(new Caso { Id = c.Id, Titulo = c.Titulo, Cru = false, Preparar = c.Preparar });
        }
    }

    private static BlockTableRecord Espaco(Transaction tr, Database db) =>
        (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);

    /// <summary>Barra A: gancho 90° no início (seg0), trecho de 300 (seg1, cota movida), faixa ortogonal. Devolve g (un).</summary>
    private static double CriarA(Transaction tr, Database db, Core.Configuracao.ConfiguracaoArmadura cfg, Point2d b)
    {
        double cpu = Desenho.CmPorUnidade(db, cfg);
        double g = cfg.Dobramento.ComprimentoGancho(FormatoSegmento.Gancho90, 10, "CA-50", false) / cpu;
        _ganchoA = g;
        var pl = new Polyline();
        pl.SetDatabaseDefaults(db);
        pl.AddVertexAt(0, b + new Vector2d(0, g), 0, 0, 0);
        pl.AddVertexAt(1, b, 0, 0, 0);
        pl.AddVertexAt(2, b + new Vector2d(300, 0), 0, 0, 0);
        Espaco(tr, db).AppendEntity(pl);
        tr.AddNewlyCreatedDBObject(pl, true);
        ServicoBarra.Criar(tr, pl, new DadosArmadura { Bitola = 10, Aco = "CA-50", Quantidade = 4 }, cfg);
        var d = XDataCad.LerBarra(pl);
        d.Segmentos[0].Formato = FormatoSegmento.Gancho90;
        d.Segmentos[0].ComprimentoRegistrado = g * cpu;
        d.Segmentos[1].CotaMovida = true;
        d.Segmentos[1].CotaDx = 10;
        d.Segmentos[1].CotaDy = 5;
        XDataCad.Gravar(pl, d);
        ServicoBarra.Atualizar(tr, pl, cfg);
        var fx = ServicoFaixa.Criar(tr, pl,
            new DadosFaixa { Tipo = TipoFaixa.Ortogonal, Espacamento = 15, P1X = b.X + 50, P1Y = b.Y - 60, P2X = b.X + 250, P2Y = b.Y - 60 },
            new Point3d(b.X + 150, b.Y - 90, 0), cfg);
        ServicoBarra.Atualizar(tr, pl, cfg);
        Registrar(tr, db, "A", pl);
        Hs["A.faixa"] = XDataCad.Hex(fx);
        return g;
    }

    /// <summary>Barra B: começa no fim de A, 300 de reta e gancho 90° para cima no fim.</summary>
    private static void CriarB(Transaction tr, Database db, Core.Configuracao.ConfiguracaoArmadura cfg, Point2d b)
    {
        double cpu = Desenho.CmPorUnidade(db, cfg);
        double g = cfg.Dobramento.ComprimentoGancho(FormatoSegmento.Gancho90, 10, "CA-50", false) / cpu;
        var pl = new Polyline();
        pl.SetDatabaseDefaults(db);
        pl.AddVertexAt(0, b + new Vector2d(300, 0), 0, 0, 0);
        pl.AddVertexAt(1, b + new Vector2d(700, 0), 0, 0, 0);
        pl.AddVertexAt(2, b + new Vector2d(700, g), 0, 0, 0);
        Espaco(tr, db).AppendEntity(pl);
        tr.AddNewlyCreatedDBObject(pl, true);
        ServicoBarra.Criar(tr, pl, new DadosArmadura { Bitola = 10, Aco = "CA-50", Quantidade = 2 }, cfg);
        var d = XDataCad.LerBarra(pl);
        d.Segmentos[1].Formato = FormatoSegmento.Gancho90;
        d.Segmentos[1].ComprimentoRegistrado = g * cpu;
        XDataCad.Gravar(pl, d);
        ServicoBarra.Atualizar(tr, pl, cfg);
        Registrar(tr, db, "B", pl);
    }

    private static void Registrar(Transaction tr, Database db, string nome, Polyline pl)
    {
        Hs[nome] = XDataCad.Hex(pl.ObjectId);
        VertAntes[nome] = Enumerable.Range(0, pl.NumberOfVertices).Select(pl.GetPoint2dAt).ToArray();
        var d = XDataCad.LerBarra(pl);
        Hs[nome + ".desc"] = d.HandleDescricao;
        for (int i = 0; i < d.Segmentos.Count; i++) Hs[$"{nome}.cota{i}"] = d.Segmentos[i].HandleCota;
    }

    private static void Linha(Transaction tr, Database db, string nome, Point2d a, Point2d b)
    {
        var ln = new Line(new Point3d(a.X, a.Y, 0), new Point3d(b.X, b.Y, 0));
        ln.SetDatabaseDefaults(db);
        Espaco(tr, db).AppendEntity(ln);
        tr.AddNewlyCreatedDBObject(ln, true);
        Hs[nome] = XDataCad.Hex(ln.ObjectId);
        VertAntes[nome] = new[] { a, b };
    }

    private static void Poli(Transaction tr, Database db, string nome, params Point2d[] v)
    {
        var pl = new Polyline();
        pl.SetDatabaseDefaults(db);
        for (int i = 0; i < v.Length; i++) pl.AddVertexAt(i, v[i], 0, 0, 0);
        Espaco(tr, db).AppendEntity(pl);
        tr.AddNewlyCreatedDBObject(pl, true);
        Hs[nome] = XDataCad.Hex(pl.ObjectId);
        VertAntes[nome] = v;
    }

    // ------------------------------------------------------------------ conferência

    private static DadosArmadura LerBarraH(Transaction tr, Database db, string h)
    {
        var id = XDataCad.IdDoHandle(db, h);
        if (id.IsNull || !id.IsValid) return null;
        return tr.GetObject(id, OpenMode.ForRead, true) is Polyline pl ? XDataCad.LerBarra(pl) : null;
    }

    private static string Estado(Transaction tr, Database db, string h)
    {
        if (string.IsNullOrEmpty(h)) return "(vazio)";
        var id = XDataCad.IdDoHandle(db, h);
        if (id.IsNull || !id.IsValid) return $"{h}: inexistente";
        var o = tr.GetObject(id, OpenMode.ForRead, true);
        string tipo = XDataCad.TemDados(o) ? XDataCad.Tipo(o).ToString() : "sem XData";
        return $"{h}: {o.GetType().Name} {(id.IsErased ? "APAGADO" : "existe")} ({tipo})";
    }

    private static string ResumoBarra(Transaction tr, Database db, string h, DadosArmadura d)
    {
        var sb = new StringBuilder();
        sb.Append($"N{d.Numero} q{d.Quantidade} ø{F(d.Bitola)} id {d.Id.ToString().Substring(0, 8)}; {d.Segmentos.Count} seg [");
        for (int i = 0; i < d.Segmentos.Count; i++)
        {
            var s = d.Segmentos[i];
            var cid = XDataCad.IdDoHandle(db, s.HandleCota);
            string cota = string.IsNullOrEmpty(s.HandleCota) ? "-" : s.HandleCota;
            if (!cid.IsNull && !cid.IsErased && tr.GetObject(cid, OpenMode.ForRead) is DBText ct)
            {
                var v = XDataCad.LerVinculo(ct);
                cota += $"=\"{ct.TextString}\"" + (v != null && string.Equals(v.HandleBarra, h, StringComparison.OrdinalIgnoreCase) && v.Indice == i ? "" : $"(vínculo→{v?.HandleBarra}#{v?.Indice})");
            }
            else if (!string.IsNullOrEmpty(s.HandleCota)) cota += "(inválida)";
            sb.Append($"{i}:{s.Formato}{(s.Cotar ? "" : " nãoCota")}{(s.Var ? " VAR" : "")}{(s.CotaMovida ? $" movida({F(s.CotaDx)},{F(s.CotaDy)})" : "")} reg {F(s.ComprimentoRegistrado)} cota {cota}");
            if (i < d.Segmentos.Count - 1) sb.Append(" | ");
        }
        sb.Append("]");
        var did = XDataCad.IdDoHandle(db, d.HandleDescricao);
        if (!did.IsNull && !did.IsErased && tr.GetObject(did, OpenMode.ForRead) is MText mt)
        {
            var v = XDataCad.LerVinculo(mt);
            sb.Append($"; descrição {d.HandleDescricao} \"{mt.Contents}\"" +
                      (v != null && string.Equals(v.HandleBarra, h, StringComparison.OrdinalIgnoreCase) ? "" : $" (vínculo→{v?.HandleBarra})"));
        }
        else sb.Append($"; descrição {d.HandleDescricao} inválida/apagada");
        foreach (var hf in d.Faixas)
        {
            var fid = XDataCad.IdDoHandle(db, hf);
            if (fid.IsNull || fid.IsErased || !(tr.GetObject(fid, OpenMode.ForRead) is Entity fe)) { sb.Append($"; faixa {hf} inválida/apagada"); continue; }
            var f = ServicoFaixa.Ler(fe);
            sb.Append($"; faixa {hf} →{f?.HandleBarra}{(string.Equals(f?.HandleBarra, h, StringComparison.OrdinalIgnoreCase) ? "" : " (NÃO recíproca)")}");
        }
        return sb.ToString();
    }

    private static void Conferir(Database db, Core.Configuracao.ConfiguracaoArmadura cfg, Caso caso)
    {
        using var tr = db.TransactionManager.StartTransaction();
        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
        Rel.AppendLine("  originais depois:");
        foreach (var kv in Hs) Rel.AppendLine($"    {kv.Key,-9} {Estado(tr, db, kv.Value)}");

        // tudo o que está na área do caso
        var na = new List<Entity>();
        foreach (ObjectId id in ms)
        {
            if (!(tr.GetObject(id, OpenMode.ForRead) is Entity e)) continue;
            Point3d c;
            try { var ext = e.GeometricExtents; c = ext.MinPoint + (ext.MaxPoint - ext.MinPoint) * 0.5; }
            catch { continue; }
            if (c.X < _base.X - 400 || c.X > _base.X + 900 || c.Y < _base.Y - 300 || c.Y > _base.Y + 300) continue;
            na.Add(e);
        }
        Rel.AppendLine("  na área do caso agora:");
        foreach (var e in na)
        {
            string h = XDataCad.Hex(e.ObjectId);
            bool novo = !_handlesAntes.Contains(e.ObjectId.Handle.Value);
            string tipo = XDataCad.TemDados(e) ? XDataCad.Tipo(e).ToString() : "sem XData";
            string extra = "";
            if (e is Polyline pl) extra = $" {pl.NumberOfVertices} vért: " + string.Join(" ", Enumerable.Range(0, pl.NumberOfVertices).Select(i => P(pl.GetPoint2dAt(i))));
            else if (e is Line ln) extra = $" {P(ln.StartPoint)}→{P(ln.EndPoint)}";
            else if (e is MText mt) extra = $" \"{mt.Contents}\"";
            else if (e is DBText tx) extra = $" \"{tx.TextString}\"";
            else if (e is Dimension dm) extra = $" \"{dm.DimensionText}\"";
            if (XDataCad.TemDados(e) && XDataCad.Tipo(e) != TipoParte.Barra)
            {
                var v = XDataCad.LerVinculo(e);
                if (v != null && !string.IsNullOrEmpty(v.HandleBarra)) extra += $" vínculo→{v.HandleBarra}" + (v.Tipo == TipoParte.Cota ? $"#{v.Indice}" : "");
                else if (XDataCad.Tipo(e) == TipoParte.Faixa) extra += $" faixa→{ServicoFaixa.Ler(e)?.HandleBarra}";
                var dona = ServicoBarra.BarraDe(tr, e);
                extra += dona == null ? " [ÓRFÃO: sem barra recíproca]" : $" [dona {XDataCad.Hex(dona.ObjectId)}]";
            }
            Rel.AppendLine($"    {h,-6} {(novo ? "NOVO " : "     ")}{e.GetType().Name,-16} {tipo,-10}{extra}");
        }

        // barras sobreviventes e vértices
        var barras = na.OfType<Polyline>().Where(p => XDataCad.TemDados(p) && XDataCad.Tipo(p) == TipoParte.Barra).ToList();
        var polis = na.OfType<Polyline>().ToList();
        Rel.AppendLine($"  RESULTADO: {polis.Count} polilinha(s) na área, {barras.Count} com XData de barra.");
        foreach (var pl in polis)
        {
            string h = XDataCad.Hex(pl.ObjectId);
            string quem = h == Hv("A") ? "= A (mesmo handle)" : h == Hv("L") ? "= L (mesmo handle)"
                : h == Hv("B") ? "= B (mesmo handle)" : _handlesAntes.Contains(pl.ObjectId.Handle.Value) ? "(pré-existente)" : "NOVA";
            string ordem = "";
            if (VertAntes.TryGetValue("A", out var va) && pl.NumberOfVertices > 1)
            {
                var v0 = pl.GetPoint2dAt(0); var vn = pl.GetPoint2dAt(pl.NumberOfVertices - 1);
                int iA0 = Indice(pl, va[0]), iAn = Indice(pl, va[va.Length - 1]);
                ordem = iA0 < 0 || iAn < 0 ? "; vértices de A não estão todos nela"
                    : iA0 < iAn ? $"; A na MESMA ordem (v0 de A no índice {iA0})" : $"; A INVERTIDA (v0 de A no índice {iA0}, fim de A no {iAn})";
            }
            Rel.AppendLine($"    polilinha {h} {quem}: XData barra = {(XDataCad.TemDados(pl) && XDataCad.Tipo(pl) == TipoParte.Barra ? "SIM" : "não")}{ordem}");
            var d = XDataCad.TemDados(pl) ? XDataCad.LerBarra(pl) : null;
            if (d != null)
            {
                Rel.AppendLine("      dados: " + ResumoBarra(tr, db, h, d));
                try
                {
                    var geo = GeometriaBarra.Ler(pl, Desenho.CmPorUnidade(db, cfg));
                    Rel.AppendLine($"      geometria: {geo.Count} seg [{string.Join(", ", geo.Select(s => F(s.Comprimento)))}] cm; " +
                                   $"C real = {F(Core.Calculo.CalculoComprimento.ComprimentoUnitario(geo, cfg, d))}");
                }
                catch (Exception ex) { Rel.AppendLine("      geometria: erro " + ex.Message); }
            }
        }
        tr.Commit();
    }

    /// <summary>Apaga tudo o que ficou na área do caso (com o Sincronizador suspenso): sobras de um caso não podem mexer no seguinte.</summary>
    private static void Limpar(Document doc, Database db)
    {
        using (Sincronizador.Suspender(doc))
        using (var tr = db.TransactionManager.StartTransaction())
        {
            var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
            int n = 0;
            foreach (ObjectId id in ms)
            {
                if (!(tr.GetObject(id, OpenMode.ForRead) is Entity e)) continue;
                Point3d c;
                try { var ext = e.GeometricExtents; c = ext.MinPoint + (ext.MaxPoint - ext.MinPoint) * 0.5; }
                catch { continue; }
                if (c.X < _base.X - 400 || c.X > _base.X + 900 || c.Y < _base.Y - 300 || c.Y > _base.Y + 300) continue;
                e.UpgradeOpen();
                e.Erase();
                n++;
            }
            tr.Commit();
            Rel.AppendLine($"  (área do caso limpa: {n} objeto(s) apagados pelo teste)");
        }
        Eventos.Clear();
    }

    /// <summary>Gancho 90° ø10 CA-50 (cm → unidades do desenho) — o "g" dos casos.</summary>
    private static double GanchoA => _ganchoA;
    private static double _ganchoA = 11.5;

    private static List<Polyline> PolilinhasNaArea(Transaction tr, Database db)
    {
        var r = new List<Polyline>();
        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
        foreach (ObjectId id in ms)
        {
            if (!(tr.GetObject(id, OpenMode.ForRead) is Polyline pl)) continue;
            Point3d c;
            try { var ext = pl.GeometricExtents; c = ext.MinPoint + (ext.MaxPoint - ext.MinPoint) * 0.5; }
            catch { continue; }
            if (c.X < _base.X - 400 || c.X > _base.X + 900 || c.Y < _base.Y - 300 || c.Y > _base.Y + 300) continue;
            r.Add(pl);
        }
        return r;
    }

    private static bool Viva(Database db, string h, out ObjectId id)
    {
        id = XDataCad.IdDoHandle(db, h);
        return !id.IsNull && id.IsValid && !id.IsErased;
    }

    /// <summary>Barra que ficou com a geometria juntada: mesmo handle, vértices no sentido dela, dados por trecho, cotas, descrição, faixa.</summary>
    private static void ConferirJuntada(Transaction tr, Database db, Core.Configuracao.ConfiguracaoArmadura cfg, string chave,
        (double x, double y)[] esperado, FormatoSegmento[] formatos, int antes, int cEsperado, bool ra = false)
    {
        string id = Casos[_passo - 1].Id, h = Hs[chave];
        Checar(Viva(db, h, out var oid), $"{id}: a armadura {chave} continua, com o mesmo handle {h}");
        if (oid.IsNull || oid.IsErased) return;
        var pl = (Polyline)tr.GetObject(oid, OpenMode.ForRead);
        var d = XDataCad.LerBarra(pl);
        Checar(d != null, $"{id}: XData ARMADURA_BR mantido");
        if (d == null) return;
        var vs = Enumerable.Range(0, pl.NumberOfVertices).Select(i => pl.GetPoint2dAt(i) - new Vector2d(_base.X, _base.Y)).ToList();
        bool vok = vs.Count == esperado.Length && vs.Zip(esperado, (a, e) => Math.Abs(a.X - e.x) < 1e-6 && Math.Abs(a.Y - e.y) < 1e-6).All(x => x);
        Checar(vok, $"{id}: vértices no sentido original da barra: " + string.Join(" ", vs.Select(v => $"({F(v.X)},{F(v.Y)})")));
        Checar(d.Segmentos.Select(x => x.Formato).SequenceEqual(formatos), $"{id}: formatos por trecho " + string.Join(",", d.Segmentos.Select(x => x.Formato)));
        bool cotas = d.Segmentos.Count == pl.NumberOfVertices - 1 && Enumerable.Range(0, d.Segmentos.Count).All(i =>
        {
            var ct = ServicoBarra.Parte<DBText>(tr, db, d.Segmentos[i].HandleCota, TipoParte.Cota, h);
            return d.Segmentos[i].Cotar && ct != null && XDataCad.LerVinculo(ct).Indice == i;
        });
        Checar(cotas, $"{id}: uma cota recíproca por trecho, índice do vínculo certo (" +
                      string.Join(" ", d.Segmentos.Select(x => x.HandleCota)) + ")");
        bool cotasAntigas = Enumerable.Range(0, 2).All(k => !Hs.TryGetValue($"{chave}.cota{k}", out var hc)
            || antes + k < d.Segmentos.Count && string.Equals(d.Segmentos[antes + k].HandleCota, hc, StringComparison.OrdinalIgnoreCase));
        Checar(cotasAntigas, $"{id}: as cotas antigas continuam (mesmo handle) e foram para os trechos {antes} e {antes + 1}");
        if (chave == "A") Checar(d.Segmentos[antes + 1].CotaMovida && d.Segmentos[antes + 1].CotaDx == 10, $"{id}: cota movida acompanhou o trecho (índice {antes + 1})");
        var desc = ServicoBarra.Parte<MText>(tr, db, d.HandleDescricao, TipoParte.Descricao, h);
        Checar(desc != null && string.Equals(d.HandleDescricao, Hs[chave + ".desc"], StringComparison.OrdinalIgnoreCase) && desc.Contents.Contains($"C={cEsperado}"),
            $"{id}: descrição com o mesmo handle e C={cEsperado} ({desc?.Contents})");
        if (Hs.TryGetValue(chave + ".faixa", out var hf))
        {
            var fe = Viva(db, hf, out var fid) ? tr.GetObject(fid, OpenMode.ForRead) as Entity : null;
            Checar(fe != null && ServicoFaixa.BarraDaFaixa(tr, fe)?.ObjectId == oid && d.Faixas.Contains(hf, StringComparer.OrdinalIgnoreCase),
                $"{id}: faixa {hf} continua ligada (vínculo recíproco)");
        }
        var outras = PolilinhasNaArea(tr, db).Where(p => p.ObjectId != oid).Select(p => XDataCad.Hex(p.ObjectId)).ToList();
        Checar(outras.Count == 0, $"{id}: a polilinha nova do CAD foi apagada (sobrou: {string.Join(" ", outras)})");
        if (Hs.TryGetValue("L", out var hl)) Checar(!Viva(db, hl, out _), $"{id}: o objeto juntado ({hl}) foi absorvido");
        if (chave == "B")
        {
            // a outra (A, mais curta) saiu com as partes
            bool foi = new[] { "A", "A.desc", "A.cota0", "A.cota1", "A.faixa" }.All(k => !Viva(db, Hs[k], out _));
            Checar(foi, $"{id}: a armadura mais curta (A) e a descrição/cotas/faixa dela foram apagadas");
        }
        if (ra && !_tabela.IsNull && !_tabela.IsErased)
        {
            string Textos() { var t = (Table)tr.GetObject(_tabela, OpenMode.ForRead); return string.Join("|", Enumerable.Range(0, t.Rows.Count).SelectMany(r => Enumerable.Range(0, t.Columns.Count).Select(c => t.Cells[r, c].TextString))); }
            string antesRA = Textos();
            Rel.AppendLine($"  relação do aço ANTES de atualizar já tem 509? {antesRA.Contains("509")} (como na barra esticada: só PLOT/SAVE/REGEN/ARMRAATUALIZAR atualizam)");
            Relacao.ServicoRA.Atualizar(db, tr, cfg);
            string depoisRA = Textos();
            Checar(depoisRA.Contains("509"), $"{id}: relação do aço atualizada com o comprimento novo (509): " + depoisRA);
            var d2 = XDataCad.LerBarra(tr.GetObject(oid, OpenMode.ForRead));
            Checar(d2.Numero > 0, $"{id}: a barra recuperada foi numerada pela relação do aço (N{d2.Numero})");
        }
    }

    /// <summary>Nada mudou: a armadura A com os 3 vértices, partes e a LINE no lugar.</summary>
    private static void ConferirIntacta(Transaction tr, Database db, string id)
    {
        string h = Hs["A"];
        bool viva = Viva(db, h, out var oid);
        var pl = viva ? (Polyline)tr.GetObject(oid, OpenMode.ForRead) : null;
        var d = pl == null ? null : XDataCad.LerBarra(pl);
        Checar(pl != null && d != null && pl.NumberOfVertices == 3 && d.Segmentos.Count == 2 && d.Segmentos[0].Formato == FormatoSegmento.Gancho90,
            $"{id}: a armadura continua como antes (3 vértices, gancho no trecho 0)");
        bool partes = d != null && new[] { "A.desc", "A.cota0", "A.cota1", "A.faixa" }.All(k => Viva(db, Hs[k], out _))
                      && ServicoBarra.Parte<MText>(tr, db, d.HandleDescricao, TipoParte.Descricao, h) != null;
        Checar(partes, $"{id}: descrição, cotas e faixa continuam");
        Checar(Viva(db, Hs["L"], out _), $"{id}: a LINE continua");
        var outras = PolilinhasNaArea(tr, db).Where(p => p.ObjectId != oid).Select(p => XDataCad.Hex(p.ObjectId)).ToList();
        Checar(outras.Count == 0, $"{id}: nenhuma polilinha a mais (sobrou: {string.Join(" ", outras)})");
    }

    /// <summary>ARMCONVERTER com linhas: cadeia → 1 barra, isolada → reta, fechada → estribo, ramificada → não converte.</summary>
    private static void ConferirConversao(Transaction tr, Database db, Core.Configuracao.ConfiguracaoArmadura cfg)
    {
        var barras = PolilinhasNaArea(tr, db).Where(p => XDataCad.Tipo(p) == TipoParte.Barra).ToList();
        Checar(barras.Count == 3, $"conv: 3 barras (cadeia, isolada, estribo) — {barras.Count}");
        var cadeia = barras.FirstOrDefault(p => !p.Closed && p.NumberOfVertices == 3);
        var pts = cadeia == null ? "" : string.Join(" ", Enumerable.Range(0, 3).Select(i => P(cadeia.GetPoint2dAt(i))));
        Checar(cadeia != null && pts == "(0,0) (100,0) (100,50)", $"conv: L1 + L2 (ao contrário) viraram UMA barra na ordem das ligações: {pts}");
        Checar(barras.Any(p => !p.Closed && p.NumberOfVertices == 2 && P(p.GetPoint2dAt(0)) == "(0,200)"), "conv: linha isolada virou barra reta");
        // o estribo com ganchos é uma polilinha ABERTA (volta + passagens dos ganchos), marcada como estribo
        var est = barras.FirstOrDefault(p => XDataCad.LerBarra(p).Estribo);
        Checar(est != null && est.NumberOfVertices >= 6, $"conv: linhas em cadeia fechada viraram estribo com ganchos ({est?.NumberOfVertices} vértices)");
        Checar(new[] { "L1", "L2", "L3", "R1", "R2", "R3", "R4" }.All(k => !Viva(db, Hs[k], out _)), "conv: as linhas convertidas foram substituídas");
        Checar(new[] { "X1", "X2", "X3" }.All(k => Viva(db, Hs[k], out var x) && !XDataCad.TemDados(tr.GetObject(x, OpenMode.ForRead))),
            "conv: grupo ramificado (3 linhas no mesmo ponto) não foi convertido");
    }

    private static string Hv(string k) => Hs.TryGetValue(k, out var v) ? v : "";

    private static int Indice(Polyline pl, Point2d p)
    {
        for (int i = 0; i < pl.NumberOfVertices; i++) if (pl.GetPoint2dAt(i).GetDistanceTo(p) < 1e-6) return i;
        return -1;
    }

    // ------------------------------------------------------------------ eventos (só registram)

    private static void Ligar(Document doc)
    {
        var db = doc.Database;
        db.ObjectAppended += AoAnexar;
        db.ObjectErased += AoApagar;
        db.ObjectModified += AoModificar;
        doc.CommandWillStart += AoIniciar;
        doc.CommandEnded += AoTerminar;
        doc.CommandCancelled += AoCancelar;
        doc.CommandFailed += AoFalhar;
        var ed = doc.Editor;
        ed.PromptingForSelection += AoPedirSelecao;
        ed.PromptingForEntity += AoPedirEntidade;
        ed.PromptingForKeyword += AoPedirPalavra;
        ed.PromptingForPoint += AoPedirPonto;
        ed.PromptingForString += AoPedirTexto;
        ed.PromptForSelectionEnding += AoFimSelecao;
        ed.PromptForEntityEnding += AoFimEntidade;
    }

    private static void Desligar(Document doc)
    {
        try
        {
            var db = doc.Database;
            db.ObjectAppended -= AoAnexar;
            db.ObjectErased -= AoApagar;
            db.ObjectModified -= AoModificar;
            doc.CommandWillStart -= AoIniciar;
            doc.CommandEnded -= AoTerminar;
            doc.CommandCancelled -= AoCancelar;
            doc.CommandFailed -= AoFalhar;
            var ed = doc.Editor;
            ed.PromptingForSelection -= AoPedirSelecao;
            ed.PromptingForEntity -= AoPedirEntidade;
            ed.PromptingForKeyword -= AoPedirPalavra;
            ed.PromptingForPoint -= AoPedirPonto;
            ed.PromptingForString -= AoPedirTexto;
            ed.PromptForSelectionEnding -= AoFimSelecao;
            ed.PromptForEntityEnding -= AoFimEntidade;
        }
        catch (Exception ex) { Log.Erro("ARMTESTEJOINLOG: desligar", ex); }
    }

    private static void Ev(string texto)
    {
        try
        {
            // ações do próprio plugin (Sincronizador no fim do comando) ficam marcadas
            bool plugin = new System.Diagnostics.StackTrace().ToString().Contains("Sincronizador");
            Eventos.Add($"{++_seq,3} {(plugin ? "[plugin] " : "")}{texto}");
        }
        catch { }
    }

    private static string Desc(DBObject o)
    {
        try
        {
            string tipo = XDataCad.TemDados(o) ? "XData " + XDataCad.Tipo(o) : "sem XData";
            string extra = "";
            if (o is Polyline pl && pl.NumberOfVertices > 0)
                extra = $" {pl.NumberOfVertices} vért {P(pl.GetPoint2dAt(0))}…{P(pl.GetPoint2dAt(pl.NumberOfVertices - 1))}";
            else if (o is Line ln) extra = $" {P(ln.StartPoint)}→{P(ln.EndPoint)}";
            return $"{o.ObjectId.Handle} {o.GetType().Name} ({tipo}){extra}";
        }
        catch (Exception ex) { return $"{o?.ObjectId.Handle} {o?.GetType().Name} (erro ao ler: {ex.Message})"; }
    }

    private static void AoAnexar(object s, ObjectEventArgs e) { if (e.DBObject is Entity) Ev("ObjectAppended  " + Desc(e.DBObject)); }
    private static void AoApagar(object s, ObjectErasedEventArgs e) { if (e.DBObject is Entity) Ev((e.Erased ? "ObjectErased    " : "ObjectUNerased  ") + Desc(e.DBObject)); }
    private static void AoModificar(object s, ObjectEventArgs e) { if (e.DBObject is Entity) Ev("ObjectModified  " + Desc(e.DBObject)); }
    private static void AoIniciar(object s, CommandEventArgs e) => Ev("CommandWillStart " + e.GlobalCommandName);
    private static void AoTerminar(object s, CommandEventArgs e) => Ev("CommandEnded     " + e.GlobalCommandName);
    private static void AoCancelar(object s, CommandEventArgs e) => Ev("CommandCancelled " + e.GlobalCommandName);
    private static void AoFalhar(object s, CommandEventArgs e) => Ev("CommandFailed    " + e.GlobalCommandName);
    private static void AoPedirSelecao(object s, PromptSelectionOptionsEventArgs e) => Ev("prompt seleção: " + e.Options?.MessageForAdding?.Trim());
    private static void AoPedirEntidade(object s, PromptEntityOptionsEventArgs e) => Ev("prompt entidade: " + e.Options?.Message?.Trim());
    private static void AoPedirPalavra(object s, PromptKeywordOptionsEventArgs e) => Ev("prompt opção: " + e.Options?.Message?.Trim());
    private static void AoPedirPonto(object s, PromptPointOptionsEventArgs e) => Ev("prompt ponto: " + e.Options?.Message?.Trim());
    private static void AoPedirTexto(object s, PromptStringOptionsEventArgs e) => Ev("prompt texto: " + e.Options?.Message?.Trim());
    private static void AoFimSelecao(object s, PromptForSelectionEndingEventArgs e) => Ev($"seleção feita: {e.Selection?.Count ?? 0} objeto(s)");
    private static void AoFimEntidade(object s, PromptForEntityEndingEventArgs e) =>
        Ev($"entidade escolhida: {e.Result?.Status} {(e.Result != null && e.Result.Status == PromptStatus.OK ? e.Result.ObjectId.Handle.ToString() : "")}");
}
