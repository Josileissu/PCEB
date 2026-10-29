using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using Armadura.Cad.Barra;
using Armadura.Cad.Infra;
using Armadura.Core.Configuracao;
using Armadura.Core.Modelo;
using Armadura.Core.Tabelas;
using Armadura.Core.Templates;

[assembly: Rt.CommandClass(typeof(Armadura.Cad.ComandosTemplate))]

namespace Armadura.Cad;

/// <summary>
/// ARMTEMPLATE: armadura padrão de um elemento estrutural (bases de reservatório, blocos, pilares,
/// vigas, lajes). Ambiente preparado em 2026-10-27; os templates em si virão dos padrões do escritório.
/// </summary>
public sealed class ComandosTemplate
{
    [Rt.CommandMethod("ARMTEMPLATE")]
    public void ArmTemplate() => Comandos.Executar("ARMTEMPLATE", (doc, ed, db, cfg) =>
    {
        if (CatalogoTemplates.Todos.Count == 0)
        {
            ed.WriteMessage("\nTemplates de armadura — elementos previstos (aguardando os padrões do escritório em PDF/DWG):");
            foreach (var (_, nome, variacoes) in CatalogoTemplates.Categorias)
                ed.WriteMessage($"\n  • {nome}" + (variacoes.Length > 0 ? $" ({variacoes})" : ""));
            ed.WriteMessage("\nNenhum template implementado ainda.\n");
            return;
        }
        using var f = new FormTemplate();
        if (CadApp.ShowModalDialog(f) != DialogResult.OK || f.Escolhido == null) return;
        var r = ed.GetPoint("\nPonto de inserção:");
        if (r.Status != PromptStatus.OK) return;
        var resultado = f.Escolhido.Gerar(f.Valores, cfg);
        using (var tr = db.TransactionManager.StartTransaction())
        {
            var ids = Desenhar(tr, db, cfg, resultado, r.Value.TransformBy(ed.CurrentUserCoordinateSystem));
            tr.Commit();
            ed.WriteMessage($"\n{f.Escolhido.Nome}: {ids.Count} armadura(s) desenhada(s)" +
                            (resultado.Detalhamento.Length > 0 ? $" no detalhamento \"{resultado.Detalhamento}\"." : "."));
        }
        foreach (var a in resultado.Avisos) ed.WriteMessage("\nAviso: " + a);
    });

    /// <summary>
    /// Desenha o resultado de um template no espaço corrente: forma, armaduras (com grupos VAR e telas),
    /// faixas e indicações, textos, cotas e tabelas; com <see cref="ResultadoTemplate.Detalhamento"/>, tudo
    /// vira um bloco e a relação do aço parcial dele é inserida. Retorna as armaduras criadas (antes do bloco).
    /// </summary>
    internal static List<ObjectId> Desenhar(Transaction tr, Database db, ConfiguracaoArmadura cfg, ResultadoTemplate r, Point3d origem)
    {
        double cpu = Desenho.CmPorUnidade(db, cfg);
        var esp = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
        var todos = new List<ObjectId>();
        Point2d P(double x, double y) => new Point2d(origem.X + x / cpu, origem.Y + y / cpu);
        Point3d P3(double x, double y) { var q = P(x, y); return new Point3d(q.X, q.Y, origem.Z); }
        ObjectId Layer(string nome, short cor) => Desenho.GarantirLayer(db, tr, nome, cor);
        T Add<T>(T e, ObjectId layer) where T : Entity
        {
            e.SetDatabaseDefaults(db);
            e.LayerId = layer;
            esp.AppendEntity(e);
            tr.AddNewlyCreatedDBObject(e, true);
            todos.Add(e.ObjectId);
            return e;
        }
        Polyline Poli(IList<(double X, double Y)> pts, IList<double> bulges, bool fechada)
        {
            var pl = new Polyline();
            for (int i = 0; i < pts.Count; i++) pl.AddVertexAt(i, P(pts[i].X, pts[i].Y), i < bulges.Count ? bulges[i] : 0, 0, 0);
            pl.Closed = fechada;
            pl.Elevation = origem.Z;
            return pl;
        }
        short CorDoLayer(string nome) => nome switch { "ARM-TANQUE" => 4, "ARM-ESTACA" => 5, "ARM-CORTE" => 1, "ARM-TEXTO-FORMA" => 7, "ARM-NIVEL" => 9, _ => 8 };

        // ---- forma, textos, cotas ----
        foreach (var c in r.Contornos) Add(Poli(c.Vertices, c.Bulges, c.Fechado), Layer(c.Layer, CorDoLayer(c.Layer)));
        foreach (var c in r.Circulos) Add(new Circle(P3(c.X, c.Y), Vector3d.ZAxis, c.Raio / cpu), Layer(c.Layer, CorDoLayer(c.Layer)));
        foreach (var t in r.Textos)
        {
            var p = P3(t.X, t.Y);
            var txt = new DBText { TextString = t.Texto, Height = t.Altura / cpu, Rotation = t.Angulo, Position = p };
            if (db.Textstyle.IsValid) txt.TextStyleId = db.Textstyle;
            if (t.Alinhamento != 0)
            {
                txt.HorizontalMode = t.Alinhamento == 1 ? TextHorizontalMode.TextCenter : TextHorizontalMode.TextRight;
                txt.AlignmentPoint = p;
            }
            Add(txt, Layer(t.Layer, CorDoLayer(t.Layer)));
            if (t.Alinhamento != 0) txt.AdjustAlignment(db);
        }
        foreach (var c in r.Cotas)
        {
            var dim = Add(new RotatedDimension(c.Angulo, P3(c.P1.X, c.P1.Y), P3(c.P2.X, c.P2.Y), P3(c.Posicao.X, c.Posicao.Y), c.Texto, db.Dimstyle),
                Layer("ARM-COTA-FORMA", 8));
            double h = cfg.AlturaTexto(cpu, c.Escala > 0 ? c.Escala : r.Escala);
            dim.Dimscale = 1; dim.Dimtxt = h; dim.Dimasz = h; dim.Dimtsz = 0.5 * h; dim.Dimexe = 0.5 * h; dim.Dimexo = 0.5 * h; dim.Dimgap = 0.3 * h;
        }
        foreach (var t in r.Tabelas) Tabela(tr, db, esp, t, P3(t.X, t.Y), cfg, cpu, r.Escala, todos);

        // ---- armaduras ----
        var ids = new List<ObjectId>();
        var porNome = new Dictionary<string, ObjectId>(StringComparer.Ordinal);
        var layerBarra = Layer(cfg.LayerBarra, cfg.CorBarra);
        foreach (var b in r.Barras)
        {
            var pl = Add(Poli(b.Vertices, b.Bulges, b.Fechada), layerBarra);
            var d = new DadosArmadura
            {
                Bitola = b.Bitola, Aco = b.Aco, Quantidade = Math.Max(1, b.Quantidade), Espacamento = b.Espacamento, Estribo = b.Estribo,
                Escala = b.Escala > 0 ? b.Escala : r.Escala, Repeticoes = Math.Max(1, b.Repeticoes), Corrido = b.Corrido,
                ComprimentosVar = new List<double>(b.ComprimentosVar),
            };
            if (!string.IsNullOrWhiteSpace(b.Tela))
            {
                var m = cfg.Telas.Tela(b.Tela);
                d.Tela = m?.Nome ?? b.Tela;
                d.Bitola = m?.PhiLongitudinal ?? 5;
                d.Aco = m?.Aco ?? "CA-60";
                d.TelaCotas = false;
            }
            int n = GeometriaBarra.NumeroSegmentos(pl);
            for (int s = 0; s < n; s++)
            {
                var f = s < b.Formatos.Count ? b.Formatos[s] : FormatoSegmento.Linha;
                bool sob = b.Sobrepostos.Contains(s);
                d.Segmentos.Add(new Segmento
                {
                    Formato = f, Sobreposto = sob, Var = b.SegmentosVar.Contains(s),
                    Cotar = !Segmento.EhGancho(f) && !sob && (b.Cotados.Count == 0 || b.Cotados.Contains(s)),
                });
            }
            ServicoBarra.Criar(tr, pl, d, cfg);
            if (b.PosicaoDescricao.HasValue)
            {
                // descrição onde o template pediu (ex.: na vertical, ao lado da barra); o plugin guarda a posição
                var dd = XDataCad.LerBarra(pl);
                var desc = ServicoBarra.Parte<MText>(tr, db, dd.HandleDescricao, Core.Persistencia.TipoParte.Descricao, XDataCad.Hex(pl.ObjectId));
                if (desc != null)
                {
                    desc.UpgradeOpen();
                    desc.Attachment = AttachmentPoint.MiddleCenter;
                    desc.Location = P3(b.PosicaoDescricao.Value.X, b.PosicaoDescricao.Value.Y);
                    desc.Rotation = b.RotacaoDescricao;
                    ServicoBarra.Atualizar(tr, pl, cfg);
                }
            }
            ids.Add(pl.ObjectId);
            if (!string.IsNullOrEmpty(b.Nome)) porNome[b.Nome] = pl.ObjectId;
        }

        // ---- faixas e indicações ----
        foreach (var f in r.Faixas)
        {
            if (!porNome.TryGetValue(f.Barra ?? "", out var bid)) continue;
            var a = P(f.P1.X, f.P1.Y);
            var b = P(f.P2.X, f.P2.Y);
            var dados = new DadosFaixa
            {
                Tipo = f.Tipo, Espacamento = f.Espacamento > 0 ? f.Espacamento : 15, Escala = f.Escala > 0 ? f.Escala : r.Escala,
                P1X = a.X, P1Y = a.Y, P2X = b.X, P2Y = b.Y,
                Referencias = f.Referencias.Select(x => { var q = P(x.X, x.Y); return (q.X, q.Y); }).ToList(),
                Repeticoes = Math.Max(1, f.Repeticoes), MostrarDiametro = f.MostrarDiametro,
            };
            ServicoFaixa.Criar(tr, (Polyline)tr.GetObject(bid, OpenMode.ForWrite), dados, P3(f.Posicao.X, f.Posicao.Y), cfg);
        }

        if (string.IsNullOrWhiteSpace(r.Detalhamento)) return ids;

        // ---- detalhamento (bloco) com tudo, e a relação do aço parcial dele ----
        foreach (var id in ids) todos.AddRange(PartesDaBarra(tr, db, (Polyline)tr.GetObject(id, OpenMode.ForRead)));
        string nome = r.Detalhamento.Trim();
        for (int k = 2; Relacao.ServicoDetalhamento.ValidarNome(db, tr, nome) != null && k < 1000; k++) nome = $"{r.Detalhamento.Trim()} ({k})";
        Relacao.ServicoDetalhamento.Criar(db, tr, todos.Distinct().Where(i => !i.IsErased).ToArray(), nome,
            new DadosDetalhamento { Grupo = Relacao.ServicoDetalhamento.GrupoSugerido(nome), Comentarios = "Gerado pelo template." });
        r.Detalhamento = nome;
        if (r.PosicaoRA.HasValue)
        {
            var op = Core.RelacaoAco.OpcoesTabelaRA.Padrao(cfg);
            op.Escala = r.Escala;
            op.Blocos.Add(nome);
            op.NomeNoTitulo = true;
            Relacao.ServicoRA.Inserir(db, tr, P3(r.PosicaoRA.Value.X, r.PosicaoRA.Value.Y), cfg, op);
        }
        else Relacao.ServicoRA.Atualizar(db, tr, cfg);
        return ids;
    }

    /// <summary>Descrição, cotas, faixas (e partes) e desenho gerado de uma barra.</summary>
    private static IEnumerable<ObjectId> PartesDaBarra(Transaction tr, Database db, Polyline pl)
    {
        var d = XDataCad.LerBarra(pl);
        if (d == null) yield break;
        foreach (var h in new[] { d.HandleDescricao }.Concat(d.Segmentos.Select(s => s.HandleCota)).Concat(d.Rebatidas).Concat(d.PartesDesenho))
        {
            var id = XDataCad.IdDoHandle(db, h);
            if (!id.IsNull && !id.IsErased) yield return id;
        }
        foreach (var hf in d.Faixas)
        {
            var fid = XDataCad.IdDoHandle(db, hf);
            if (fid.IsNull || fid.IsErased) continue;
            yield return fid;
            foreach (var hp in ServicoFaixa.Ler((Entity)tr.GetObject(fid, OpenMode.ForRead))?.Partes ?? new List<string>())
            {
                var pid = XDataCad.IdDoHandle(db, hp);
                if (!pid.IsNull && !pid.IsErased) yield return pid;
            }
        }
    }

    /// <summary>Tabela de texto simples (título mesclado + linhas), com o canto superior esquerdo em <paramref name="p"/>.</summary>
    private static void Tabela(Transaction tr, Database db, BlockTableRecord esp, TabelaTemplate t, Point3d p, ConfiguracaoArmadura cfg,
        double cpu, double escala, List<ObjectId> todos)
    {
        int cols = Math.Max(1, t.Linhas.Count == 0 ? 1 : t.Linhas.Max(l => l.Length));
        double h = cfg.AlturaTexto(cpu, t.Escala > 0 ? t.Escala : escala);
        var tab = new Table { Position = p, LayerId = Desenho.GarantirLayer(db, tr, cfg.LayerTabela, cfg.CorTabela) };
        if (db.Tablestyle.IsValid) tab.TableStyle = db.Tablestyle;
        esp.AppendEntity(tab);
        tr.AddNewlyCreatedDBObject(tab, true);
        tab.SetSize(t.Linhas.Count + 1, cols);
        // margem do texto dentro das células (sem ela o texto encosta nas bordas)
        try { tab.HorizontalCellMargin = 0.5 * h; tab.VerticalCellMargin = 0.3 * h; }
        catch (Rt.Exception ex) { Log.Erro("Tabela.Margens", ex); }
        for (int c = 0; c < cols; c++) tab.SetColumnWidth(c, (c < t.Larguras.Count && t.Larguras[c] > 0 ? t.Larguras[c] : 6) * h);
        // mescla ANTES de escrever (no GstarCAD, mesclar depois zera a altura do texto)
        if (cols > 1) tab.MergeCells(CellRange.Create(tab, 0, 0, 0, cols - 1));
        CellAlignment Alinhamento(int c) => (c < t.Alinhamentos.Count ? t.Alinhamentos[c] : 1) switch
        {
            0 => CellAlignment.MiddleLeft,
            2 => CellAlignment.MiddleRight,
            _ => CellAlignment.MiddleCenter,
        };
        void Cel(int l, int c, string s, CellAlignment al)
        {
            var cel = tab.Cells[l, c];
            cel.TextHeight = h;
            // o GstarCAD ignora a margem das células: um espaço afasta o texto da borda
            string txt = s ?? "";
            if (txt.Length > 0 && al == CellAlignment.MiddleLeft) txt = " " + txt.Replace(TabelaTemplate.Quebra, TabelaTemplate.Quebra + " ");
            else if (txt.Length > 0 && al == CellAlignment.MiddleRight) txt = txt.Replace(TabelaTemplate.Quebra, " " + TabelaTemplate.Quebra) + " ";
            cel.TextString = txt.Replace(TabelaTemplate.Quebra, "\\P");
            cel.Alignment = al;
        }
        Cel(0, 0, t.Titulo, Alinhamento(0) == CellAlignment.MiddleLeft ? CellAlignment.MiddleLeft : CellAlignment.MiddleCenter);
        for (int l = 0; l < t.Linhas.Count; l++)
            for (int c = 0; c < cols; c++) Cel(l + 1, c, c < t.Linhas[l].Length ? t.Linhas[l][c] : "", Alinhamento(c));
        int NLinhas(string s) => (s ?? "").Split('\n').Length;
        tab.SetRowHeight(0, TabelaTemplate.AlturaLinha(NLinhas(t.Titulo)) * h);
        for (int l = 0; l < t.Linhas.Count; l++) tab.SetRowHeight(l + 1, TabelaTemplate.AlturaLinha(t.Linhas[l].Max(NLinhas)) * h);
        todos.Add(tab.ObjectId);
    }
}

/// <summary>Escolha do template e dos valores dos parâmetros.</summary>
internal sealed class FormTemplate : Form
{
    public ITemplateArmadura Escolhido { get; private set; }
    public ValoresTemplate Valores { get; private set; }

    public FormTemplate()
    {
        Text = "Armadura — templates de elementos";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(720, 520);
        ShowInTaskbar = false;
        MinimizeBox = false;

        var lista = new TreeView { Dock = DockStyle.Left, Width = 240, HideSelection = false };
        foreach (var (cat, nome, _) in CatalogoTemplates.Categorias)
        {
            var no = lista.Nodes.Add(nome);
            var ts = CatalogoTemplates.DaCategoria(cat).ToList();
            if (ts.Count == 0) no.Nodes.Add("(aguardando o padrão)").ForeColor = SystemColors.GrayText;
            foreach (var t in ts) no.Nodes.Add(new TreeNode(t.Nome) { Tag = t });
        }
        lista.ExpandAll();
        var grid = new PropertyGrid { Dock = DockStyle.Fill, ToolbarVisible = false, PropertySort = PropertySort.Categorized };
        var desc = new Label { Dock = DockStyle.Top, Height = 44, Padding = new Padding(6), Text = "Escolha um template à esquerda." };
        var botoes = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40, FlowDirection = System.Windows.Forms.FlowDirection.RightToLeft, Padding = new Padding(4) };
        var cancelar = new Button { Text = "Cancelar", Width = 90, Height = 28, DialogResult = DialogResult.Cancel };
        var ok = new Button { Text = "Desenhar", Width = 90, Height = 28, Enabled = false };
        botoes.Controls.Add(cancelar);
        botoes.Controls.Add(ok);
        CancelButton = cancelar;

        lista.AfterSelect += (s, e) =>
        {
            Escolhido = e.Node.Tag as ITemplateArmadura;
            ok.Enabled = Escolhido != null;
            if (Escolhido == null) { grid.SelectedObject = null; desc.Text = "Escolha um template à esquerda."; return; }
            Valores = new ValoresTemplate(Escolhido.Parametros);
            grid.SelectedObject = new ParametrosEditaveis(Escolhido, Valores);
            desc.Text = Escolhido.Descricao;
        };
        ok.Click += (s, e) =>
        {
            var erros = CatalogoTemplates.Validar(Escolhido, Valores);
            if (erros.Count > 0) { MessageBox.Show(string.Join("\n", erros), "Armadura", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            DialogResult = DialogResult.OK;
            Close();
        };

        var direita = new Panel { Dock = DockStyle.Fill };
        direita.Controls.Add(grid);
        direita.Controls.Add(desc);
        Controls.Add(direita);
        Controls.Add(lista);
        Controls.Add(botoes);
    }
}

/// <summary>Parâmetros de um template como propriedades do PropertyGrid (descritores dinâmicos).</summary>
internal sealed class ParametrosEditaveis : CustomTypeDescriptor
{
    private readonly ITemplateArmadura _t;
    private readonly ValoresTemplate _v;

    public ParametrosEditaveis(ITemplateArmadura t, ValoresTemplate v) { _t = t; _v = v; }

    public override PropertyDescriptorCollection GetProperties(Attribute[] attributes) => GetProperties();

    public override PropertyDescriptorCollection GetProperties() =>
        new PropertyDescriptorCollection(_t.Parametros.Select(p => (PropertyDescriptor)new Descritor(p, _v)).ToArray());

    public override object GetPropertyOwner(PropertyDescriptor pd) => this;

    private sealed class Descritor : PropertyDescriptor
    {
        private readonly ParametroTemplate _p;
        private readonly ValoresTemplate _v;

        public Descritor(ParametroTemplate p, ValoresTemplate v) : base(p.Nome, new Attribute[]
        {
            new CategoryAttribute(p.Grupo), new DescriptionAttribute(p.Descricao),
            new DisplayNameAttribute(p.Unidade.Length > 0 ? $"{p.Rotulo} ({p.Unidade})" : p.Rotulo),
        }) { _p = p; _v = v; }

        public override Type ComponentType => typeof(ParametrosEditaveis);
        public override bool IsReadOnly => false;
        public override Type PropertyType => typeof(string);
        public override bool CanResetValue(object c) => true;
        public override void ResetValue(object c) => _v[_p.Nome] = _p.Padrao;
        public override bool ShouldSerializeValue(object c) => false;
        public override object GetValue(object c) => _v[_p.Nome];

        public override void SetValue(object c, object valor)
        {
            var s = (valor as string ?? "").Trim();
            if (_p.Tipo == TipoParametro.Numero || _p.Tipo == TipoParametro.Inteiro || _p.Tipo == TipoParametro.Bitola)
            {
                if (!double.TryParse(s.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                    throw new ArgumentException("Informe um número.");
            }
            _v[_p.Nome] = s;
        }

        public override TypeConverter Converter => _p.Tipo switch
        {
            TipoParametro.Opcao => new Lista(_p.Opcoes),
            TipoParametro.SimNao => new Lista(new List<string> { "Sim", "Não" }),
            _ => base.Converter,
        };
    }

    private sealed class Lista : StringConverter
    {
        private readonly List<string> _opcoes;
        public Lista(List<string> opcoes) { _opcoes = opcoes; }
        public override bool GetStandardValuesSupported(ITypeDescriptorContext c) => true;
        public override bool GetStandardValuesExclusive(ITypeDescriptorContext c) => true;
        public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext c) => new StandardValuesCollection(_opcoes);
    }
}
