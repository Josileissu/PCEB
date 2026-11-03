using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Armadura.Cad.Barra;
using Armadura.Cad.Infra;
using Armadura.Core.Persistencia;
using Color = System.Drawing.Color;

namespace Armadura.Cad.Paleta;

/// <summary>
/// Paleta "Armadura": PropertyGrid sincronizado com a seleção do desenho (barra, descrição ou cota
/// selecionadas → a barra). Com várias barras, os valores diferentes aparecem em branco e uma
/// alteração vale para todas.
/// </summary>
internal static class PaletaArmadura
{
    private static PaletteSet _ps;
    private static PropertyGrid _grid;
    private static Label _status;
    private static Document _docLigado;
    private static bool _aplicando;

    public static bool Visivel => _ps != null && _ps.Visible;

    /// <summary>Barras mostradas agora (para o autoteste).</summary>
    internal static IReadOnlyList<PropriedadesBarra> Mostradas =>
        _grid?.SelectedObjects?.OfType<PropriedadesBarra>().ToList() ?? new List<PropriedadesBarra>();

    public static void Mostrar()
    {
        if (_ps == null) Criar();
        _ps.Visible = true;
        LigarDocumento(CadApp.DocumentManager.MdiActiveDocument);
        Recarregar();
    }

    public static void Alternar()
    {
        if (Visivel) _ps.Visible = false;
        else Mostrar();
    }

    private static void Criar()
    {
        _ps = new PaletteSet("Armadura", new Guid("6F1C1B0E-4E7B-4D5A-9C3E-2A7B5D0A8E11"))
        {
            Style = PaletteSetStyles.ShowCloseButton | PaletteSetStyles.ShowAutoHideButton,
            MinimumSize = new Size(260, 300),
            Size = new Size(320, 560),
        };

        var painel = new Panel { Dock = DockStyle.Fill };
        _grid = new PropertyGrid
        {
            Dock = DockStyle.Fill,
            PropertySort = PropertySort.Categorized,
            ToolbarVisible = false,
            HelpVisible = true,
        };
        _grid.PropertyValueChanged += (s, e) => Aplicar();
        _status = new Label { Dock = DockStyle.Top, Height = 34, Padding = new Padding(4), AutoEllipsis = true };
        painel.Controls.Add(_grid);
        painel.Controls.Add(_status);
        AplicarTema(painel, _status, _grid);
        _ps.Add("Propriedades", painel);
        // ocultar automaticamente: recolhida, a paleta não fica "Visible"; ao abrir ou ativar, relê a seleção
        _ps.StateChanged += (s, e) => { if (!_aplicando) Recarregar(); };
        _ps.PaletteActivated += (s, e) => { if (!_aplicando) Recarregar(); };
        painel.VisibleChanged += (s, e) => { if (painel.Visible && !_aplicando) Recarregar(); };

        CadApp.DocumentManager.DocumentActivated += (s, e) => { LigarDocumento(e.Document); Recarregar(); };
        CadApp.DocumentManager.DocumentToBeDestroyed += (s, e) => { if (e.Document == _docLigado) LigarDocumento(null); };
    }

    /// <summary>Cores escuras quando o CAD está no tema escuro (COLORTHEME = 0).</summary>
    internal static void AplicarTema(Control painel, Label status, PropertyGrid grid)
    {
        bool escuro;
        try { escuro = Convert.ToInt32(CadApp.GetSystemVariable("COLORTHEME")) == 0; }
        catch { return; } // variável inexistente: mantém as cores do Windows
        if (!escuro) return;
        var fundo = Color.FromArgb(59, 68, 83);
        var campo = Color.FromArgb(43, 49, 59);
        var texto = Color.FromArgb(230, 230, 230);
        painel.BackColor = fundo;
        status.BackColor = fundo;
        status.ForeColor = texto;
        grid.BackColor = fundo;
        grid.ViewBackColor = campo;
        grid.ViewForeColor = texto;
        grid.LineColor = fundo;
        grid.CategoryForeColor = texto;
        grid.HelpBackColor = fundo;
        grid.HelpForeColor = texto;
        grid.DisabledItemForeColor = Color.FromArgb(150, 150, 150);
        grid.CategorySplitterColor = fundo;
        grid.ViewBorderColor = fundo;
        grid.HelpBorderColor = fundo;
    }

    private static void LigarDocumento(Document doc)
    {
        if (_docLigado == doc) return;
        if (_docLigado != null)
        {
            try
            {
                _docLigado.ImpliedSelectionChanged -= AoMudarSelecao;
                _docLigado.CommandEnded -= AoTerminarComando;
            }
            catch (Exception ex) { Log.Erro("Paleta.Desligar", ex); }
        }
        _docLigado = doc;
        if (doc == null) return;
        doc.ImpliedSelectionChanged += AoMudarSelecao;
        doc.CommandEnded += AoTerminarComando;
    }

    private static void AoMudarSelecao(object sender, EventArgs e) { if (!_aplicando) Recarregar(); }

    // depois de um comando (grips, STRETCH, ARMPROP…) os valores podem ter mudado
    private static void AoTerminarComando(object sender, CommandEventArgs e) { if (!_aplicando) Recarregar(); }

    /// <summary>
    /// Lê a seleção atual e remonta o PropertyGrid. Roda mesmo com a paleta "invisível": com ocultar
    /// automaticamente (recolhida na borda), o GstarCAD informa Visible = false, e a paleta abria com a
    /// seleção antiga (relato do usuário, 2026-10-27).
    /// </summary>
    public static void Recarregar()
    {
        if (_ps == null || _grid == null) return;
        try
        {
            _grid.SelectedObjects = LerSelecao(CadApp.DocumentManager.MdiActiveDocument, out var status);
            _status.Text = status;
        }
        catch (Exception ex) { Log.Erro("Paleta.Recarregar", ex); }
    }

    /// <summary>Gravação em andamento (as janelas ignoram os eventos de seleção que ela provoca).</summary>
    internal static bool Aplicando => _aplicando;

    /// <summary>
    /// Objetos de propriedades da seleção atual — armaduras têm prioridade; depois faixas (se só houver
    /// faixas), tabelas e detalhamentos (blocos). Usado pela paleta e pela janela rápida.
    /// </summary>
    internal static object[] LerSelecao(Document doc, out string status)
    {
        var objetos = new List<PropriedadesBarra>();
        var tabelas = new List<PropriedadesTabela>();
        int outros = 0;
        ObjectId[] ids = null;
        if (doc != null)
        {
            var sel = doc.Editor.SelectImplied();
            if (sel.Status == PromptStatus.OK && sel.Value != null)
            {
                ids = sel.Value.GetObjectIds();
                // só faixas (cota, corte, chamada) selecionadas: propriedades da faixa (escala própria)
                var faixas = LerFaixas(doc.Database, ids, out bool soFaixas);
                if (soFaixas && faixas.Count > 0)
                {
                    status = faixas.Count == 1 ? faixas[0].Tipo : $"{faixas.Count} faixas — valores diferentes ficam em branco.";
                    return faixas.Cast<object>().ToArray();
                }
                objetos = Ler(doc.Database, ids, out outros);
                tabelas = LerTabelas(doc.Database, ids);
                outros -= tabelas.Count;
            }
        }
        if (objetos.Count == 0 && tabelas.Count > 0)
        {
            status = tabelas.Count == 1 ? "Relação do aço" : $"{tabelas.Count} tabelas de relação do aço";
            return tabelas.Cast<object>().ToArray();
        }
        if (objetos.Count == 0 && ids != null)
        {
            var dets = LerDetalhamentos(doc.Database, ids);
            if (dets.Count > 0)
            {
                status = dets.Count == 1 ? $"Detalhamento {dets[0].Nome}" : $"{dets.Count} detalhamentos";
                return dets.Cast<object>().ToArray();
            }
        }
        status = objetos.Count switch
        {
            0 => "Selecione armaduras ou uma relação do aço no desenho." + (outros > 0 ? $"\n{outros} objeto(s) que não são armadura." : ""),
            1 => objetos[0].Descricao,
            _ => $"{objetos.Count} armaduras — valores diferentes ficam em branco." + (outros > 0 ? $" ({outros} outro(s) ignorado(s))" : ""),
        };
        return objetos.Cast<object>().ToArray();
    }

    internal static List<PropriedadesBarra> Ler(Database db, IEnumerable<ObjectId> ids, out int outros)
    {
        var r = new List<PropriedadesBarra>();
        var vistos = new HashSet<ObjectId>();
        outros = 0;
        var cfg = ConfigDesenho.Obter(db);
        double cpu = Desenho.CmPorUnidade(db, cfg);
        using var tr = db.TransactionManager.StartOpenCloseTransaction();
        foreach (var id in ids)
        {
            if (id.IsErased) continue;
            var pl = ServicoBarra.ResolverBarra(tr, id);
            if (pl == null) { outros++; continue; }
            if (!vistos.Add(pl.ObjectId)) continue; // barra e a sua descrição selecionadas juntas
            var d = XDataCad.LerBarra(pl);
            var geo = GeometriaBarra.Ler(pl, cpu);
            d.AjustarSegmentos(geo.Count);
            bool comGanchos = d.Estribo && ServicoEstribo.ComGanchos(pl, d);
            r.Add(new PropriedadesBarra(pl.ObjectId, d, geo, cfg, comGanchos, comGanchos ? ServicoEstribo.Posicao(pl) : null));
        }
        tr.Commit();
        return r;
    }

    /// <summary>Faixas da seleção (a principal ou as partes dela); <paramref name="soFaixas"/> = nada além de faixas.</summary>
    internal static List<PropriedadesFaixa> LerFaixas(Database db, IEnumerable<ObjectId> ids, out bool soFaixas)
    {
        var r = new List<PropriedadesFaixa>();
        var vistas = new HashSet<ObjectId>();
        soFaixas = true;
        var cfg = ConfigDesenho.Obter(db);
        using var tr = new TransacaoSegura(db);
        foreach (var id in ids)
        {
            if (id.IsErased || !(tr.GetObject(id, OpenMode.ForRead) is Entity ent)) { soFaixas = false; continue; }
            var tipo = XDataCad.Tipo(ent);
            Entity faixa = tipo == TipoParte.Faixa ? ent
                : tipo == TipoParte.Corte || tipo == TipoParte.Chamada ? ServicoFaixa.FaixaDaParte(tr, ent) : null;
            if (faixa == null) { soFaixas = false; continue; }
            if (!vistas.Add(faixa.ObjectId)) continue;
            var f = ServicoFaixa.Ler(faixa);
            var barra = ServicoFaixa.BarraDaFaixa(tr, faixa);
            if (f == null || barra == null) continue;
            r.Add(new PropriedadesFaixa(faixa.ObjectId, f, cfg.EscalaEfetiva(XDataCad.LerBarra(barra)?.Escala ?? 0)));
        }
        return r;
    }

    /// <summary>Grava as faixas alteradas e recalcula as barras delas.</summary>
    internal static List<string> AplicarFaixas(Document doc, IEnumerable<PropriedadesFaixa> faixas)
    {
        var erros = new List<string>();
        var lista = faixas.Where(f => f.Pendentes.Count > 0).ToList();
        if (lista.Count == 0) return erros;
        var cfg = ConfigDesenho.Obter(doc.Database);
        _aplicando = true;
        try
        {
            using (doc.LockDocument())
            using (Sincronizador.Suspender(doc))
            using (var tr = new TransacaoSegura(doc.Database))
            {
                var barras = new HashSet<ObjectId>();
                foreach (var p in lista)
                {
                    if (p.Id.IsErased) continue;
                    var ent = (Entity)tr.GetObject(p.Id, OpenMode.ForWrite);
                    var f = ServicoFaixa.Ler(ent);
                    if (f == null) continue;
                    foreach (var a in p.Pendentes) a(f);
                    p.Pendentes.Clear();
                    XDataCad.Gravar(ent, f);
                    var barra = ServicoFaixa.BarraDaFaixa(tr, ent);
                    if (barra != null) barras.Add(barra.ObjectId);
                }
                foreach (var id in barras)
                    ServicoBarra.Atualizar(tr, (Polyline)tr.GetObject(id, OpenMode.ForWrite), cfg);
                tr.Commit();
            }
        }
        catch (Exception ex)
        {
            Log.Erro("Paleta.AplicarFaixas", ex);
            erros.Add("Erro ao aplicar: " + ex.Message);
        }
        finally { _aplicando = false; }
        return erros;
    }

    /// <summary>
    /// Autoteste: deixa a paleta "invisível" como o GstarCAD informa quando ela está recolhida
    /// (AutoRollUp/RolledUp lançam NotImplementedException na API do GstarCAD 2021).
    /// </summary>
    internal static void OcultarParaTeste() { if (_ps != null) _ps.Visible = false; }

    internal static IReadOnlyList<PropriedadesFaixa> FaixasMostradas =>
        _grid?.SelectedObjects?.OfType<PropriedadesFaixa>().ToList() ?? new List<PropriedadesFaixa>();

    internal static List<PropriedadesTabela> LerTabelas(Database db, IEnumerable<ObjectId> ids)
    {
        var r = new List<PropriedadesTabela>();
        var cfg = ConfigDesenho.Obter(db);
        using var tr = db.TransactionManager.StartOpenCloseTransaction();
        foreach (var id in ids)
        {
            if (id.IsErased) continue;
            var op = Relacao.ServicoRA.Opcoes(tr.GetObject(id, OpenMode.ForRead) as Entity, cfg);
            if (op != null) r.Add(new PropriedadesTabela(id, op, cfg.TipoNumeracao));
        }
        tr.Commit();
        return r;
    }

    /// <summary>Blocos selecionados → detalhamentos (qualquer bloco; os dados vazios até serem preenchidos).</summary>
    internal static List<PropriedadesDetalhamento> LerDetalhamentos(Database db, IEnumerable<ObjectId> ids)
    {
        var r = new List<PropriedadesDetalhamento>();
        var vistos = new HashSet<ObjectId>();
        using var tr = new TransacaoSegura(db);
        foreach (var id in ids)
        {
            if (id.IsErased || !(tr.GetObject(id, OpenMode.ForRead) is BlockReference br) || br is Table) continue;
            var defId = br.IsDynamicBlock ? br.DynamicBlockTableRecord : br.BlockTableRecord;
            if (!vistos.Add(defId)) continue;
            var btr = (BlockTableRecord)tr.GetObject(defId, OpenMode.ForRead);
            if (btr.IsFromExternalReference || btr.IsLayout) continue;
            var d = Relacao.ServicoDetalhamento.Ler(btr) ?? new Core.Modelo.DadosDetalhamento();
            r.Add(new PropriedadesDetalhamento(defId, btr.Name, d, Relacao.ServicoDetalhamento.Insercoes(tr, btr)));
        }
        return r;
    }

    /// <summary>Grava os dados (e o nome) dos detalhamentos alterados; renumera/atualiza as relações.</summary>
    internal static List<string> AplicarDetalhamentos(Document doc, IEnumerable<PropriedadesDetalhamento> dets)
    {
        var erros = new List<string>();
        var lista = dets.Where(d => d.Pendentes.Count > 0).ToList();
        if (lista.Count == 0) return erros;
        var db = doc.Database;
        var cfg = ConfigDesenho.Obter(db);
        _aplicando = true;
        try
        {
            using (doc.LockDocument())
            using (Sincronizador.Suspender(doc))
            using (var tr = new TransacaoSegura(db))
            {
                foreach (var p in lista)
                {
                    var btr = (BlockTableRecord)tr.GetObject(p.BlocoId, OpenMode.ForWrite);
                    var d = Relacao.ServicoDetalhamento.Ler(btr) ?? new Core.Modelo.DadosDetalhamento();
                    foreach (var a in p.Pendentes) a(d);
                    p.Pendentes.Clear();
                    Relacao.ServicoDetalhamento.Gravar(db, tr, btr, d);
                    if (!string.Equals(p.NovoNome, btr.Name, StringComparison.Ordinal))
                    {
                        var erro = Relacao.ServicoDetalhamento.ValidarNome(db, tr, p.NovoNome, p.BlocoId);
                        if (erro != null) erros.Add(erro);
                        else ComandosDetalhamento.Renomear(db, tr, btr, p.NovoNome);
                    }
                }
                Relacao.ServicoRA.Atualizar(db, tr, cfg);
                tr.Commit();
            }
        }
        catch (Exception ex)
        {
            Log.Erro("Paleta.AplicarDetalhamentos", ex);
            erros.Add("Erro ao aplicar: " + ex.Message);
        }
        finally { _aplicando = false; }
        return erros;
    }

    /// <summary>Grava as opções alteradas e redesenha as tabelas.</summary>
    internal static void AplicarTabelas(Document doc, IEnumerable<PropriedadesTabela> tabelas)
    {
        var lista = tabelas.Where(t => t.Pendentes.Count > 0 || t.NovaNumeracao.HasValue).ToList();
        if (lista.Count == 0) return;
        var numeracao = lista.Select(t => t.NovaNumeracao).LastOrDefault(n => n.HasValue);
        _aplicando = true;
        try
        {
            using (doc.LockDocument())
            using (Sincronizador.Suspender(doc))
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                // numeração é do desenho: vai para a configuração do DWG (ou a do usuário) e renumera tudo
                if (numeracao.HasValue)
                    ConfigDesenho.Alterar(doc.Database, tr, c => c.TipoNumeracao = numeracao.Value);
                var cfg = ConfigDesenho.Obter(doc.Database);
                foreach (var p in lista)
                {
                    if (p.Id.IsErased) continue;
                    var t = (Table)tr.GetObject(p.Id, OpenMode.ForWrite);
                    var op = Relacao.ServicoRA.Opcoes(t, cfg);
                    foreach (var a in p.Pendentes) a(op);
                    p.Pendentes.Clear();
                    Relacao.ServicoRA.GravarOpcoes(t, op);
                }
                Relacao.ServicoRA.Atualizar(doc.Database, tr, cfg);
                tr.Commit();
            }
        }
        finally { _aplicando = false; }
    }

    /// <summary>Grava no desenho as alterações pendentes de todos os objetos mostrados.</summary>
    private static void Aplicar()
    {
        AplicarObjetos(_grid.SelectedObjects);
        Recarregar();
    }

    /// <summary>Grava as alterações pendentes de objetos de propriedades (paleta ou janela rápida).</summary>
    internal static void AplicarObjetos(object[] objetos)
    {
        var doc = CadApp.DocumentManager.MdiActiveDocument;
        if (doc == null || objetos == null) return;
        if (Infra.Demo.Expirada) { MessageBox.Show(Infra.Demo.Mensagem, "Armadura"); return; }
        try { AplicarTabelas(doc, objetos.OfType<PropriedadesTabela>()); }
        catch (Exception ex) { Log.Erro("Paleta.AplicarTabelas", ex); MessageBox.Show("Erro ao aplicar: " + ex.Message, "Armadura"); }
        var erros = AplicarDetalhamentos(doc, objetos.OfType<PropriedadesDetalhamento>());
        erros.AddRange(AplicarFaixas(doc, objetos.OfType<PropriedadesFaixa>()));
        erros.AddRange(Aplicar(doc, objetos.OfType<PropriedadesBarra>()));
        if (erros.Count > 0)
            MessageBox.Show(string.Join("\n", erros.Distinct()), "Armadura", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        try { doc.Editor.UpdateScreen(); } catch (Rt.Exception) { }
    }

    internal static List<string> Aplicar(Document doc, IEnumerable<PropriedadesBarra> barras)
    {
        var erros = new List<string>();
        var comPendencias = barras.Where(b => b.Pendentes.Count > 0).ToList();
        if (comPendencias.Count == 0) return erros;
        var cfg = ConfigDesenho.Obter(doc.Database);
        _aplicando = true;
        try
        {
            using (doc.LockDocument())
            using (Sincronizador.Suspender(doc))
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                double cpu = Desenho.CmPorUnidade(doc.Database, cfg);
                bool renumerarTelas = false;
                foreach (var b in comPendencias)
                {
                    if (b.Id.IsErased) continue;
                    var pl = (Polyline)tr.GetObject(b.Id, OpenMode.ForWrite);
                    var d = XDataCad.LerBarra(pl);
                    if (d == null) continue;
                    d.AjustarSegmentos(GeometriaBarra.NumeroSegmentos(pl));
                    var ctx = new ContextoAlteracao { Tr = tr, Pl = pl, D = d, Cfg = cfg, CmPorUnidade = cpu };
                    foreach (var acao in b.Pendentes) acao(ctx);
                    b.Pendentes.Clear();
                    erros.AddRange(ctx.Erros);
                    XDataCad.Gravar(pl, d);
                    ServicoBarra.Atualizar(tr, pl, cfg);
                    renumerarTelas |= d.EhTela && (d.TelaIdentificada || d.NumeroTela > 0);
                }
                // tela identificada: o T (T1, T2…) sai da renumeração do desenho
                if (renumerarTelas) Relacao.ServicoRA.Atualizar(doc.Database, tr, cfg);
                tr.Commit();
            }
        }
        catch (Exception ex)
        {
            Log.Erro("Paleta.Aplicar", ex);
            erros.Add("Erro ao aplicar: " + ex.Message);
        }
        finally { _aplicando = false; }
        return erros;
    }
}
