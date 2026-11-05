using System;
using System.Collections.Generic;
using System.Linq;
using DEB.Cad.Infra;
using DEB.Core.Persistencia;

namespace DEB.Cad.Barra;

/// <summary>
/// Faz polilinha + textos se comportarem como uma entidade "armadura".
/// Os eventos do Database só ENFILEIRAM ids; o processamento acontece no fim do comando
/// (CommandEnded), numa transação própria — nunca dentro do callback de notificação.
/// </summary>
internal sealed class Sincronizador
{
    private static readonly Dictionary<Document, Sincronizador> PorDocumento = new Dictionary<Document, Sincronizador>();
    private static readonly HashSet<string> ComandosDeDesfazer =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "U", "UNDO", "REDO", "MREDO", "OOPS" };

    private readonly Document _doc;
    private readonly HashSet<ObjectId> _modificados = new HashSet<ObjectId>();
    private readonly HashSet<ObjectId> _apagados = new HashSet<ObjectId>();
    private readonly HashSet<ObjectId> _anexados = new HashSet<ObjectId>();
    /// <summary>
    /// Transformações dos blocos explodidos no comando atual: as faixas guardam pontos absolutos (no bloco, em coordenadas
    /// do bloco); ao explodir, as entidades voltam ao desenho e os pontos têm de ir junto (antes as chamadas iam para longe).
    /// </summary>
    private readonly List<Matrix3d> _explodidos = new List<Matrix3d>();
    private string _comando = "";
    private static readonly HashSet<string> ComandosDeExplodir = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "EXPLODE", "XPLODE", "BURST" };
    private int _suspenso;

    // MATCHPROP (pedido do usuário, 2026-11-05): o comando nativo só copia layer/cor/estilo; os dados do plugin
    // (barra, faixa, tabela) vão junto por aqui. A origem é o 1º objeto escolhido (ou o pré-selecionado); os destinos
    // são os escolhidos depois e os modificados pelo comando.
    private static readonly HashSet<string> ComandosDeCopiarPropriedades =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "MATCHPROP", "PAINTER" };
    private bool _matchAtivo;
    private bool _matchOrigemPreSelecionada;
    private ObjectId _matchOrigem = ObjectId.Null;
    private readonly List<ObjectId> _matchDestinos = new List<ObjectId>();
    private ObjectId _matchOrigemPendente = ObjectId.Null;
    private List<ObjectId> _matchDestinosPendentes = new List<ObjectId>();

    /// <summary>Log de cada fim de comando (variável de ambiente ARM_LOG_DETALHADO=1).</summary>
    private static readonly bool Detalhado = Environment.GetEnvironmentVariable("ARM_LOG_DETALHADO") == "1";

    private Sincronizador(Document doc) { _doc = doc; }

    public static void Anexar(Document doc)
    {
        if (doc == null || PorDocumento.ContainsKey(doc)) return;
        var s = new Sincronizador(doc);
        PorDocumento[doc] = s;
        var db = doc.Database;
        db.ObjectModified += s.AoModificar;
        db.ObjectErased += s.AoApagar;
        db.ObjectAppended += s.AoAnexar;
        doc.CommandEnded += s.AoTerminarComando;
        doc.CommandCancelled += s.AoTerminarComando;
        doc.CommandFailed += s.AoTerminarComando;
        doc.CommandWillStart += s.AoIniciarComando;
    }

    public static void Desanexar(Document doc)
    {
        if (doc == null || !PorDocumento.TryGetValue(doc, out var s)) return;
        PorDocumento.Remove(doc);
        try
        {
            var db = doc.Database;
            db.ObjectModified -= s.AoModificar;
            db.ObjectErased -= s.AoApagar;
            db.ObjectAppended -= s.AoAnexar;
            doc.CommandEnded -= s.AoTerminarComando;
            doc.CommandCancelled -= s.AoTerminarComando;
            doc.CommandFailed -= s.AoTerminarComando;
            doc.CommandWillStart -= s.AoIniciarComando;
            ConfigDesenho.Esquecer(db);
        }
        catch (Exception ex) { Log.Erro("Desanexar", ex); }
    }

    public static void DesanexarTodos()
    {
        foreach (var d in PorDocumento.Keys.ToList()) Desanexar(d);
    }

    /// <summary>Suspende o enfileiramento enquanto os comandos do próprio plugin editam as barras.</summary>
    public static IDisposable Suspender(Document doc)
    {
        if (doc != null && PorDocumento.TryGetValue(doc, out var s)) { s._suspenso++; return new Retomar(s); }
        return new Retomar(null);
    }

    private sealed class Retomar : IDisposable
    {
        private Sincronizador _s;
        public Retomar(Sincronizador s) { _s = s; }
        public void Dispose() { if (_s != null) { _s._suspenso--; _s = null; } }
    }

    // ---------- eventos (só enfileiram) ----------

    private static bool Interessa(DBObject o) =>
        (o is Polyline || o is MText || o is DBText || o is Dimension || o is Circle || o is Line || o is Table) && XDataCad.TemDados(o);

    private static void Apagar(Entity e)
    {
        if (e.IsErased) return;
        if (!e.IsWriteEnabled) e.UpgradeOpen();
        e.Erase();
    }

    private void AoModificar(object sender, ObjectEventArgs e)
    {
        if (_suspenso > 0) return;
        try { if (Interessa(e.DBObject)) _modificados.Add(e.DBObject.ObjectId); }
        catch (Exception ex) { Log.Erro("AoModificar", ex); }
    }

    private void AoApagar(object sender, ObjectErasedEventArgs e)
    {
        if (_suspenso > 0) return;
        try
        {
            if (e.Erased && e.DBObject is BlockReference br && ComandosDeExplodir.Contains(_comando)) _explodidos.Add(br.BlockTransform);
            if (!Interessa(e.DBObject)) return;
            if (e.Erased) _apagados.Add(e.DBObject.ObjectId);
            else _apagados.Remove(e.DBObject.ObjectId);
        }
        catch (Exception ex) { Log.Erro("AoApagar", ex); }
    }

    private void AoAnexar(object sender, ObjectEventArgs e)
    {
        if (_suspenso > 0) return;
        try { if (Interessa(e.DBObject)) _anexados.Add(e.DBObject.ObjectId); }
        catch (Exception ex) { Log.Erro("AoAnexar", ex); }
    }

    private void AoTerminarComando(object sender, CommandEventArgs e)
    {
        if (Detalhado)
            Log.Info($"fim de {e.GlobalCommandName}: modificados {_modificados.Count}, apagados {_apagados.Count}, anexados {_anexados.Count}");
        if (_matchAtivo) FinalizarMatch();
        if (ComandosDeDesfazer.Contains(e.GlobalCommandName ?? "")) { Limpar(); return; }
        Processar();
    }

    // ---------- MATCHPROP ----------

    private void IniciarMatch()
    {
        _matchAtivo = true;
        _matchOrigem = ObjectId.Null;
        _matchOrigemPreSelecionada = false;
        _matchDestinos.Clear();
        var ed = _doc.Editor;
        try
        {
            // com PICKFIRST, o objeto pré-selecionado é a origem e o comando não pergunta por ela
            var pre = ed.SelectImplied();
            if (pre.Status == PromptStatus.OK && pre.Value != null && pre.Value.Count == 1)
            {
                _matchOrigem = pre.Value[0].ObjectId;
                _matchOrigemPreSelecionada = true;
            }
        }
        catch (Exception ex) { Log.Erro("MATCHPROP: pré-seleção", ex); }
        ed.PromptForEntityEnding += AoEscolherEntidade;
        ed.PromptForSelectionEnding += AoSelecionar;
    }

    private void FinalizarMatch()
    {
        _matchAtivo = false;
        try
        {
            _doc.Editor.PromptForEntityEnding -= AoEscolherEntidade;
            _doc.Editor.PromptForSelectionEnding -= AoSelecionar;
        }
        catch (Exception ex) { Log.Erro("MATCHPROP: desligar eventos", ex); }
        if (_matchOrigem.IsNull) return;
        // Enter ou ESC no fim: o que já foi pintado fica, então aplica nos dois casos
        _matchOrigemPendente = _matchOrigem;
        _matchDestinosPendentes = _matchDestinos.ToList();
        _matchOrigem = ObjectId.Null;
        _matchDestinos.Clear();
    }

    private void AoEscolherEntidade(object sender, PromptForEntityEndingEventArgs e)
    {
        try
        {
            if (!_matchAtivo || e.Result == null || e.Result.Status != PromptStatus.OK) return;
            var id = e.Result.ObjectId;
            if (_matchOrigem.IsNull) _matchOrigem = id;
            // pré-selecionado e o CAD perguntou mesmo assim: a escolha confirma a origem, não é destino
            else if (_matchOrigemPreSelecionada && _matchDestinos.Count == 0 && id == _matchOrigem) return;
            else _matchDestinos.Add(id);
            _matchOrigemPreSelecionada = false;
        }
        catch (Exception ex) { Log.Erro("MATCHPROP: escolha", ex); }
    }

    private void AoSelecionar(object sender, PromptForSelectionEndingEventArgs e)
    {
        try
        {
            if (!_matchAtivo || e.Selection == null) return;
            foreach (SelectedObject so in e.Selection)
                if (so != null) _matchDestinos.Add(so.ObjectId);
        }
        catch (Exception ex) { Log.Erro("MATCHPROP: seleção", ex); }
    }

    private enum TipoCopia { Nenhum, Barra, Faixa, Tabela }

    /// <summary>O objeto do plugin por trás do que foi clicado (texto ou cota → barra; linha/círculo → faixa).</summary>
    private static (TipoCopia tipo, Entity principal) Classificar(Transaction tr, Entity ent)
    {
        switch (XDataCad.Tipo(ent))
        {
            case TipoParte.Faixa: return (TipoCopia.Faixa, ent);
            case TipoParte.Corte:
            case TipoParte.Chamada:
                var f = ServicoFaixa.FaixaDaParte(tr, ent);
                return f == null ? (TipoCopia.Nenhum, null) : (TipoCopia.Faixa, f);
            case TipoParte.Tabela when ent is Table: return (TipoCopia.Tabela, ent);
            case TipoParte.Barra:
            case TipoParte.Descricao:
            case TipoParte.Cota:
            case TipoParte.Rebatida:
            case TipoParte.Desenho:
                var b = ServicoBarra.BarraDe(tr, ent);
                return b == null ? (TipoCopia.Nenhum, null) : (TipoCopia.Barra, b);
            default: return (TipoCopia.Nenhum, null);
        }
    }

    /// <summary>Copia os dados do plugin da origem para os destinos do mesmo tipo (barra, faixa ou tabela).</summary>
    private static int AplicarMatch(Transaction tr, ObjectId origemId, IEnumerable<ObjectId> destinos,
        HashSet<ObjectId> barras, ref bool atualizarRA, Core.Configuracao.ConfiguracaoArmadura cfg)
    {
        if (!origemId.IsValid || origemId.IsErased || !(tr.GetObject(origemId, OpenMode.ForRead) is Entity oe)) return 0;
        var (tipo, origem) = Classificar(tr, oe);
        if (tipo == TipoCopia.Nenhum) return 0;
        var feitos = new HashSet<ObjectId> { origem.ObjectId };
        int n = 0;
        foreach (var id in destinos)
        {
            if (!id.IsValid || id.IsErased || !(tr.GetObject(id, OpenMode.ForRead) is Entity de)) continue;
            var (td, destino) = Classificar(tr, de);
            if (td != tipo || !feitos.Add(destino.ObjectId)) continue;
            switch (tipo)
            {
                case TipoCopia.Barra:
                {
                    var d = XDataCad.LerBarra(destino);
                    d.CopiarPropriedadesDe(XDataCad.LerBarra(origem));
                    destino.UpgradeOpen();
                    XDataCad.Gravar(destino, d);
                    barras.Add(destino.ObjectId);
                    break;
                }
                case TipoCopia.Faixa:
                {
                    var f = ServicoFaixa.Ler(destino);
                    f.CopiarPropriedadesDe(ServicoFaixa.Ler(origem));
                    destino.UpgradeOpen();
                    XDataCad.Gravar(destino, f);
                    var bar = ServicoFaixa.BarraDaFaixa(tr, destino);
                    if (bar != null) barras.Add(bar.ObjectId);
                    break;
                }
                case TipoCopia.Tabela:
                {
                    var t = (Table)destino;
                    var op = Relacao.ServicoRA.Opcoes(t, cfg);
                    op.CopiarPropriedadesDe(Relacao.ServicoRA.Opcoes(origem, cfg));
                    Relacao.ServicoRA.GravarOpcoes(t, op);
                    atualizarRA = true;
                    break;
                }
            }
            n++;
        }
        return n;
    }

    /// <summary>Antes de plotar, exportar ou salvar: renumera e atualiza as tabelas (seção 2.8).</summary>
    private void AoIniciarComando(object sender, CommandEventArgs e)
    {
        _comando = e.GlobalCommandName ?? "";
        try { if (ComandosDeCopiarPropriedades.Contains(_comando)) IniciarMatch(); }
        catch (Exception ex) { Log.Erro("MATCHPROP: início", ex); }
        try
        {
            // roda no início de TODO comando: decide pela configuração já em memória, sem abrir o DWG
            string nome = e.GlobalCommandName ?? "";
            if (!ConfigDesenho.ObterSemLer(_doc.Database).ComandosQueAtualizamRA
                    .Any(c => string.Equals(c, nome, StringComparison.OrdinalIgnoreCase))) return;
            var cfg = ConfigDesenho.Obter(_doc.Database);
            Processar(); // pendências de comandos anteriores primeiro
            _suspenso++;
            try
            {
                using (_doc.LockDocument())
                using (var tr = _doc.Database.TransactionManager.StartTransaction())
                {
                    if (Relacao.ServicoRA.TemBarras(_doc.Database, tr))
                    {
                        var relogio = System.Diagnostics.Stopwatch.StartNew();
                        var (barras, posicoes) = Relacao.ServicoRA.Atualizar(_doc.Database, tr, cfg);
                        Log.Info($"RA atualizada antes de {nome}: {barras} barra(s), {posicoes} posição(ões) em {relogio.ElapsedMilliseconds} ms.");
                    }
                    tr.Commit();
                }
            }
            finally { _suspenso--; }
        }
        catch (Exception ex)
        {
            Log.Erro("Atualizar RA antes de " + e.GlobalCommandName, ex);
            try { _doc.Editor.WriteMessage("\nArmadura: não foi possível atualizar a relação do aço (veja o log)."); } catch { }
        }
    }

    private void Limpar()
    {
        _modificados.Clear();
        _apagados.Clear();
        _anexados.Clear();
        _explodidos.Clear();
    }

    // ---------- processamento ----------

    public void Processar()
    {
        if (_modificados.Count == 0 && _apagados.Count == 0 && _anexados.Count == 0 && _matchOrigemPendente.IsNull) return;
        var anexados = _anexados.ToList();
        var apagados = _apagados.ToList();
        var modificados = _modificados.ToList();
        var explodidos = _explodidos.ToList();
        var matchOrigem = _matchOrigemPendente;
        var matchDestinos = _matchDestinosPendentes.Concat(modificados).ToList();
        _matchOrigemPendente = ObjectId.Null;
        _matchDestinosPendentes = new List<ObjectId>();
        Limpar();

        var cfg = ConfigDesenho.Obter(_doc.Database);
        var relogio = System.Diagnostics.Stopwatch.StartNew();
        _suspenso++;
        try
        {
            using (_doc.LockDocument())
            using (var tr = _doc.Database.TransactionManager.StartTransaction())
            {
                var db = _doc.Database;
                var barras = new HashSet<ObjectId>();
                var rebatidas = new HashSet<ObjectId>();
                bool atualizarRA = false;

                // 0. EXPLODE: as faixas que saíram de um bloco levam os pontos guardados pela transformação do bloco
                if (explodidos.Count > 0) AjustarExplodidas(tr, anexados, explodidos);

                // 1. objetos novos: cópias de barra ganham GUID novo; textos copiados sem a barra são órfãos
                if (anexados.Count > 0)
                {
                    var porGuid = IndicePorGuid(db, tr);
                    var novos = new HashSet<ObjectId>(anexados);
                    foreach (var id in anexados)
                    {
                        if (id.IsErased || !id.IsValid) continue;
                        var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                        if (ent == null) continue;
                        switch (XDataCad.Tipo(ent))
                        {
                            case TipoParte.Barra when ent is Polyline pl:
                            {
                                var d = XDataCad.LerBarra(pl);
                                bool copia = porGuid.TryGetValue(d.Id, out var mesmos)
                                             && mesmos.Any(o => o != id && (!novos.Contains(o) || o.Handle.Value < id.Handle.Value));
                                if (copia)
                                {
                                    // GUID novo. As referências (1005) já foram traduzidas pelo CAD: o que
                                    // foi copiado junto continua ligado à cópia; o que aponta para as partes
                                    // do original é descartado/recriado pela validação recíproca
                                    d.Id = Guid.NewGuid();
                                    pl.UpgradeOpen();
                                    XDataCad.Gravar(pl, d);
                                }
                                barras.Add(id);
                                break;
                            }
                            case TipoParte.Faixa:
                            {
                                // faixa copiada: passa a ser mais uma faixa da mesma barra (soma na quantidade)
                                var bar = ServicoFaixa.BarraApontada(tr, ent);
                                if (bar == null) { Apagar(ent); break; }
                                var d = XDataCad.LerBarra(bar);
                                string hf = XDataCad.Hex(id);
                                if (!d.Faixas.Contains(hf, StringComparer.OrdinalIgnoreCase))
                                {
                                    d.Faixas.Add(hf);
                                    bar.UpgradeOpen();
                                    XDataCad.Gravar(bar, d);
                                }
                                barras.Add(bar.ObjectId);
                                break;
                            }
                            case TipoParte.Rebatida when ent is Polyline reb:
                            {
                                // rebatida copiada: mais um espelho da mesma barra (o deslocamento é acertado abaixo)
                                var bar = ServicoRebatida.BarraApontada(tr, ent);
                                if (bar == null) { Apagar(ent); break; }
                                var d = XDataCad.LerBarra(bar);
                                string hr = XDataCad.Hex(id);
                                if (!d.Rebatidas.Contains(hr, StringComparer.OrdinalIgnoreCase))
                                {
                                    d.Rebatidas.Add(hr);
                                    bar.UpgradeOpen();
                                    XDataCad.Gravar(bar, d);
                                }
                                rebatidas.Add(id);
                                break;
                            }
                            case TipoParte.Descricao:
                            case TipoParte.Cota:
                                // texto copiado sem a barra: a barra copiada recria os seus
                                if (ServicoBarra.BarraDaParte(tr, ent) == null) Apagar(ent);
                                break;
                            case TipoParte.Corte:
                            case TipoParte.Chamada:
                                // círculo/linha copiado sem a faixa: a faixa copiada recria os seus
                                if (ServicoFaixa.FaixaDaParte(tr, ent) == null) Apagar(ent);
                                break;
                            case TipoParte.Desenho:
                                // banzo/sinusoide/diagonal/detalhe de dobra copiado sem o dono: o dono recria o seu
                                if (ServicoDesenhoGerado.BarraDaParte(tr, ent) == null && !Relacao.ServicoRA.ParteDeTabelaValida(tr, ent))
                                    Apagar(ent);
                                break;
                            case TipoParte.Tabela:
                                // tabela copiada: preenche (e desenha os detalhes) de novo
                                atualizarRA = true;
                                break;
                        }
                    }
                }

                // 2. apagados: barra leva as partes; descrição leva a barra; cota só desliga "Cotar"
                foreach (var id in apagados)
                {
                    if (!id.IsValid || !id.IsErased) continue;
                    var obj = tr.GetObject(id, OpenMode.ForRead, true) as Entity;
                    if (obj == null) continue;
                    var tipo = XDataCad.Tipo(obj);
                    if (tipo == TipoParte.Barra)
                    {
                        var d = XDataCad.LerBarra(obj);
                        ServicoBarra.ApagarPartes(tr, db, d, XDataCad.Hex(id));
                    }
                    else if (tipo == TipoParte.Faixa)
                    {
                        // faixa apagada: leva as partes; a barra recalcula a quantidade
                        ServicoFaixa.ApagarPartes(tr, obj);
                        var bar = ServicoFaixa.BarraApontada(tr, obj);
                        if (bar != null) barras.Add(bar.ObjectId);
                    }
                    else if (tipo == TipoParte.Tabela && obj is Table tab)
                    {
                        Relacao.ServicoRA.ApagarDetalhes(tr, tab, cfg); // detalhes de dobra vão junto
                    }
                    else if (tipo == TipoParte.Desenho)
                    {
                        // parte do desenho apagada sozinha: a treliça/tela a recria
                        var v = XDataCad.LerVinculo(obj);
                        var bid = XDataCad.IdDoHandle(db, v.HandleBarra);
                        if (!bid.IsNull && !bid.IsErased) barras.Add(bid);
                    }
                    else if (tipo == TipoParte.Rebatida)
                    {
                        var bar = ServicoRebatida.BarraApontada(tr, obj);
                        if (bar != null) barras.Add(bar.ObjectId); // a barra deixa de listá-la
                    }
                    else if (tipo == TipoParte.Corte || tipo == TipoParte.Chamada)
                    {
                        // círculo apagado sozinho: a faixa o recria na próxima atualização.
                        // Linha de chamada apagada: a faixa a esquece até "Mostrar linha de chamada" = Sim
                        var v = XDataCad.LerVinculo(obj);
                        var fid = XDataCad.IdDoHandle(db, v.HandleBarra);
                        if (!fid.IsNull && !fid.IsErased && tr.GetObject(fid, OpenMode.ForRead) is Entity fe)
                        {
                            var fd = ServicoFaixa.Ler(fe);
                            string hl = XDataCad.Hex(id);
                            if (tipo == TipoParte.Chamada && fd != null
                                && fd.Partes.Any(p => string.Equals(p, hl, StringComparison.OrdinalIgnoreCase))
                                && !fd.LinhasApagadas.Contains(v.Indice))
                            {
                                fd.LinhasApagadas.Add(v.Indice);
                                fe.UpgradeOpen();
                                XDataCad.Gravar(fe, fd);
                            }
                            var bar = ServicoFaixa.BarraDaFaixa(tr, fe);
                            if (bar != null) barras.Add(bar.ObjectId);
                        }
                    }
                    else if (tipo == TipoParte.Descricao || tipo == TipoParte.Cota)
                    {
                        var v = XDataCad.LerVinculo(obj);
                        var bid = XDataCad.IdDoHandle(db, v.HandleBarra);
                        if (bid.IsNull || bid.IsErased) continue;
                        if (!(tr.GetObject(bid, OpenMode.ForRead) is Polyline pl)) continue;
                        var d = XDataCad.LerBarra(pl);
                        if (d == null) continue;
                        string h = XDataCad.Hex(id);
                        if (tipo == TipoParte.Descricao && string.Equals(d.HandleDescricao, h, StringComparison.OrdinalIgnoreCase))
                        {
                            ServicoBarra.ApagarComPartes(tr, pl);
                        }
                        else if (tipo == TipoParte.Cota && v.Indice < d.Segmentos.Count
                                 && string.Equals(d.Segmentos[v.Indice].HandleCota, h, StringComparison.OrdinalIgnoreCase))
                        {
                            d.Segmentos[v.Indice].Cotar = false;
                            d.Segmentos[v.Indice].HandleCota = "";
                            pl.UpgradeOpen();
                            XDataCad.Gravar(pl, d);
                            barras.Add(bid);
                        }
                    }
                }

                // MATCHPROP: os dados do plugin da origem vão para os destinos ANTES da atualização (que já usa os novos)
                if (!matchOrigem.IsNull)
                {
                    int copiados = AplicarMatch(tr, matchOrigem, matchDestinos, barras, ref atualizarRA, cfg);
                    if (copiados > 0)
                    {
                        Log.Info($"MATCHPROP: propriedades do plugin copiadas para {copiados} objeto(s).");
                        try { _doc.Editor.WriteMessage($"\nArmadura: propriedades copiadas para {copiados} objeto(s)."); } catch { }
                    }
                }

                // 3. modificados: barra recalcula; descrição movida só atualiza a posição guardada
                foreach (var id in modificados)
                {
                    if (!id.IsValid || id.IsErased) continue;
                    if (!(tr.GetObject(id, OpenMode.ForRead) is Entity ent)) continue;
                    if (XDataCad.Tipo(ent) == TipoParte.Rebatida) rebatidas.Add(id);
                    if (ent is Table tm && XDataCad.Tipo(tm) == TipoParte.Tabela)
                    {
                        Relacao.ServicoRA.AcompanharMovimento(tr, tm, cfg); // MOVE da tabela leva os detalhes
                        continue;
                    }
                    // texto editado pelo usuário: guarda as quebras de linha (Enter) antes de regenerar
                    GuardarQuebras(tr, ent);
                    // cota movida pelo usuário: o deslocamento fica guardado e acompanha a barra
                    if (ent is DBText cotaMovida && XDataCad.Tipo(ent) == TipoParte.Cota)
                        ServicoBarra.RegistrarMovimentoCota(tr, cotaMovida, cfg);
                    // qualquer parte (descrição movida, faixa esticada, chamada movida…) atualiza a barra dona
                    var dona = ServicoBarra.BarraDe(tr, ent);
                    if (dona != null) barras.Add(dona.ObjectId);
                }

                // rebatidas editadas primeiro: a forma delas vai para a barra (ou só o deslocamento muda)
                foreach (var id in rebatidas)
                {
                    if (id.IsErased || !(tr.GetObject(id, OpenMode.ForRead) is Polyline reb)) continue;
                    ServicoRebatida.AoModificar(tr, reb);
                    var dona = ServicoRebatida.BarraDaRebatida(tr, reb);
                    if (dona != null) barras.Add(dona.ObjectId);
                }

                foreach (var id in barras)
                {
                    if (id.IsErased) continue;
                    if (tr.GetObject(id, OpenMode.ForRead) is Polyline pl)
                        ServicoBarra.Atualizar(tr, pl, cfg);
                }
                // "verificação de comprimento limite": barra esticada além do comercial é dividida
                foreach (var id in barras)
                    if (!id.IsErased && tr.GetObject(id, OpenMode.ForRead) is Polyline pl)
                        ServicoTraspasse.VerificarLimite(tr, pl, cfg);
                if (atualizarRA) Relacao.ServicoRA.Atualizar(db, tr, cfg);
                tr.Commit();
            }
        }
        catch (Exception ex)
        {
            Log.Erro("Sincronizador.Processar", ex);
            try { _doc.Editor.WriteMessage("\nArmadura: erro ao atualizar as barras (veja o log). " + ex.Message); } catch { }
        }
        finally
        {
            _suspenso--;
            // lentidão no uso real fica registrada (checagem de desempenho, 2026-10-30)
            if (relogio.ElapsedMilliseconds > 200 || Detalhado)
                Log.Info($"Sincronizador: {relogio.ElapsedMilliseconds} ms (modificados {modificados.Count}, apagados {apagados.Count}, anexados {anexados.Count})");
        }
    }

    /// <summary>
    /// Faixas de texto vindas de um bloco explodido: escolhe a transformação do bloco que leva a âncora guardada ao texto
    /// (vários blocos explodidos de uma vez) e transforma os pontos. Sem âncora (desenhos antigos), só com um bloco.
    /// </summary>
    private static void AjustarExplodidas(Transaction tr, List<ObjectId> anexados, List<Matrix3d> transformacoes)
    {
        int vistas = 0, ajustadas = 0;
        foreach (var id in anexados)
        {
            if (id.IsErased || !(tr.GetObject(id, OpenMode.ForRead) is MText mt) || XDataCad.Tipo(mt) != TipoParte.Faixa) continue;
            var f = ServicoFaixa.Ler(mt);
            if (f == null) continue;
            vistas++;
            Matrix3d? escolhida = null;
            if (f.TemAncora)
            {
                double tol = Math.Max(1e-6, 1e-3 * mt.TextHeight);
                foreach (var m in transformacoes)
                {
                    var p = new Point3d(f.AncoraX, f.AncoraY, 0).TransformBy(m);
                    if (Math.Abs(p.X - mt.Location.X) <= tol && Math.Abs(p.Y - mt.Location.Y) <= tol) { escolhida = m; break; }
                }
            }
            else if (transformacoes.Count == 1) escolhida = transformacoes[0];
            if (escolhida == null) continue;
            mt.UpgradeOpen();
            ServicoFaixa.Transformar(mt, escolhida.Value);
            ajustadas++;
        }
        Log.Info($"EXPLODE: {transformacoes.Count} bloco(s); {ajustadas} de {vistas} faixa(s) de texto com os pontos transformados.");
    }

    /// <summary>
    /// Descrição da barra ou texto da faixa editado (MTEDIT/DDEDIT): as quebras de linha que o usuário deu
    /// passam para os dados, e o texto regenerado as mantém. O restante da edição volta ao texto gerado.
    /// </summary>
    internal static void GuardarQuebras(Transaction tr, Entity ent)
    {
        var tipo = XDataCad.Tipo(ent);
        if (tipo == TipoParte.Descricao && ent is MText mt)
        {
            var pl = ServicoBarra.BarraDaParte(tr, ent) as Polyline;
            var d = pl == null ? null : XDataCad.LerBarra(pl);
            if (d == null || d.EhTela && d.TelaIdentificada) return; // a tela identificada já tem as 2 linhas dela
            var q = Core.Descricao.QuebrasTexto.Ler(mt.Contents);
            if (Core.Descricao.QuebrasTexto.Iguais(q, d.QuebrasDescricao)) return;
            d.QuebrasDescricao = q;
            if (!pl.IsWriteEnabled) pl.UpgradeOpen();
            XDataCad.Gravar(pl, d);
        }
        else if (tipo == TipoParte.Faixa && (ent is MText || ent is Dimension))
        {
            var f = ServicoFaixa.Ler(ent);
            if (f == null) return;
            var q = Core.Descricao.QuebrasTexto.Ler(ent is MText m ? m.Contents : ((Dimension)ent).DimensionText);
            if (Core.Descricao.QuebrasTexto.Iguais(q, f.Quebras)) return;
            f.Quebras = q;
            if (!ent.IsWriteEnabled) ent.UpgradeOpen();
            XDataCad.Gravar(ent, f);
        }
    }

    private static Dictionary<Guid, List<ObjectId>> IndicePorGuid(Database db, Transaction tr)
    {
        var r = new Dictionary<Guid, List<ObjectId>>();
        foreach (var id in Desenho.Entidades<Polyline>(db, tr, incluirBlocos: true))
        {
            var pl = (Polyline)tr.GetObject(id, OpenMode.ForRead);
            var d = XDataCad.LerBarra(pl);
            if (d == null) continue;
            if (!r.TryGetValue(d.Id, out var l)) r[d.Id] = l = new List<ObjectId>();
            l.Add(id);
        }
        return r;
    }
}
