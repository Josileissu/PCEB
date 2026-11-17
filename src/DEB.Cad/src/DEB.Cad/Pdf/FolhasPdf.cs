using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DEB.Core.Documentacao;

namespace DEB.Cad.Pdf;

/// <summary>Uma folha (layout) do lote de PDFs.</summary>
internal sealed class FolhaPdf
{
    /// <summary>Caminho completo do DWG (desenho aberto ainda não salvo: só o nome).</summary>
    public string Arquivo { get; set; }
    /// <summary>Desenho aberto neste CAD (null = arquivo fechado, aberto só para a plotagem).</summary>
    public Document Doc { get; set; }
    public string Layout { get; set; }
    public int Ordem { get; set; }
    /// <summary>Retângulo usado para reconhecer o formato e como janela de plotagem (unidades do papel).</summary>
    public Extents2d Caixa { get; set; }
    public bool TemCaixa { get; set; }
    /// <summary>De onde veio a caixa: "polilinha", "bloco &lt;nome&gt;", "extensão".</summary>
    public string OrigemCaixa { get; set; } = "";
    public FormatoReconhecido Reconhecido { get; set; }
    /// <summary>Formato que vai para a plotagem (o reconhecido ou o trocado na janela). null = "?".</summary>
    public FormatoFolha Formato { get; set; }
    public bool Marcada { get; set; } = true;
    public string EstiloAtual { get; set; } = "";
    /// <summary>Resumo do page setup salvo no layout (diagnóstico).</summary>
    public string PageSetupAtual { get; set; } = "";
    public string Erro { get; set; }

    public string NomeDesenho => Path.GetFileName(Arquivo);
    public double Largura => TemCaixa ? Caixa.MaxPoint.X - Caixa.MinPoint.X : 0;
    public double Altura => TemCaixa ? Caixa.MaxPoint.Y - Caixa.MinPoint.Y : 0;
    /// <summary>Desenho mais largo que alto (sem caixa: considera deitado, o caso comum das pranchas).</summary>
    public bool Deitada => !TemCaixa || Largura >= Altura;
}

/// <summary>Lê os layouts de um desenho e reconhece o formato de cada um pela moldura/carimbo.</summary>
internal static class LeitorFolhas
{
    /// <summary>Folhas de um desenho aberto neste CAD.</summary>
    public static List<FolhaPdf> DoDocumento(Document doc, ConfigLotePdf cfg)
    {
        using (doc.LockDocument())
        {
            var arq = string.IsNullOrEmpty(Path.GetDirectoryName(doc.Name)) ? doc.Name : Path.GetFullPath(doc.Name);
            var lista = Ler(doc.Database, arq, cfg);
            foreach (var f in lista) f.Doc = doc;
            return lista;
        }
    }

    /// <summary>Folhas de um DWG fechado (lido num banco lateral, só leitura).</summary>
    public static List<FolhaPdf> DoArquivo(string caminho, ConfigLotePdf cfg)
    {
        using var db = new Database(false, true);
        db.ReadDwgFile(caminho, FileOpenMode.OpenForReadAndAllShare, true, "");
        db.CloseInput(true);
        var antigo = HostApplicationServices.WorkingDatabase;
        try
        {
            // extensões de blocos/textos num banco lateral pedem o banco como corrente
            HostApplicationServices.WorkingDatabase = db;
            return Ler(db, Path.GetFullPath(caminho), cfg);
        }
        finally { HostApplicationServices.WorkingDatabase = antigo; }
    }

    private static List<FolhaPdf> Ler(Database db, string arquivo, ConfigLotePdf cfg)
    {
        var res = new List<FolhaPdf>();
        using (var tr = db.TransactionManager.StartTransaction())
        {
            var dic = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);
            foreach (DBDictionaryEntry e in dic)
            {
                var lay = (Layout)tr.GetObject(e.Value, OpenMode.ForRead);
                if (lay.ModelType) continue;
                var f = new FolhaPdf { Arquivo = arquivo, Layout = lay.LayoutName, Ordem = lay.TabOrder, EstiloAtual = lay.CurrentStyleSheet ?? "" };
                try
                {
                    f.PageSetupAtual = FormattableString.Invariant(
                        $"{lay.PlotConfigurationName} | {lay.CanonicalMediaName} | {lay.PlotRotation} | {lay.PlotType} | papel {lay.PlotPaperSize.X:0.#}x{lay.PlotPaperSize.Y:0.#} | janela {lay.PlotWindowArea.MinPoint.X:0.#},{lay.PlotWindowArea.MinPoint.Y:0.#}-{lay.PlotWindowArea.MaxPoint.X:0.#},{lay.PlotWindowArea.MaxPoint.Y:0.#} | origem {lay.PlotOrigin.X:0.#},{lay.PlotOrigin.Y:0.#} | centrado {lay.PlotCentered} | unid {lay.PlotPaperUnits}");
                }
                catch { }
                try { Reconhecer(tr, db, lay, f, cfg); }
                catch (Exception ex) { f.Erro = ex.Message; Infra.Log.Erro($"ARMPDFLOTE: ler {arquivo} / {lay.LayoutName}", ex); }
                res.Add(f);
            }
            tr.Commit();
        }
        return res.OrderBy(f => f.Ordem).ToList();
    }

    /// <summary>
    /// Candidatos: polilinhas retangulares fechadas e blocos (carimbo com moldura) do layout; vale o MAIOR que casa com um formato.
    /// Sem candidato: a extensão do layout. Só conta o que é VISÍVEL na plotagem: sem viewports, sem layers desligados/congelados/
    /// não plotáveis e, dentro dos blocos grandes (explodidos em memória), só os objetos do estado de visibilidade atual — o
    /// GeometricExtents de um bloco dinâmico no GstarCAD inclui os estados ocultos (carimbo com A0…A0++ no mesmo bloco).
    /// </summary>
    private static void Reconhecer(Transaction tr, Database db, Layout lay, FolhaPdf f, ConfigLotePdf cfg)
    {
        var btr = (BlockTableRecord)tr.GetObject(lay.BlockTableRecordId, OpenMode.ForRead);
        var invisiveis = LayersInvisiveis(tr, db);
        // 1ª passada (rápida): extensão bruta, para saber quais blocos são grandes o bastante para serem a moldura
        var bruta = new Coleta();
        var entidades = new List<Entity>();
        foreach (ObjectId id in btr)
        {
            if (!(tr.GetObject(id, OpenMode.ForRead) is Entity ent) || ent is Viewport || !ent.Visible) continue;
            if (invisiveis.Contains(ent.Layer)) continue;
            entidades.Add(ent);
            try { var e = ent.GeometricExtents; if (Valida(e)) bruta.Somar(e); } catch { }
        }
        var c = new Coleta();
        foreach (var ent in entidades) Visitar(tr, ent, null, 0, c, invisiveis, bruta, null);

        var melhor = c.Candidatos
            .Select(k => (k.Caixa, k.Origem, R: FormatosFolha.Reconhecer(k.Caixa.MaxPoint.X - k.Caixa.MinPoint.X, k.Caixa.MaxPoint.Y - k.Caixa.MinPoint.Y, cfg.Formatos, cfg.Tolerancia)))
            .Where(k => k.R.Formato != null)
            .OrderByDescending(k => (k.Caixa.MaxPoint.X - k.Caixa.MinPoint.X) * (k.Caixa.MaxPoint.Y - k.Caixa.MinPoint.Y))
            .FirstOrDefault();
        if (melhor.R != null)
        {
            f.Caixa = melhor.Caixa; f.TemCaixa = true; f.OrigemCaixa = melhor.Origem; f.Reconhecido = melhor.R;
        }
        else if (c.Tem)
        {
            f.Caixa = c.Caixa; f.TemCaixa = true; f.OrigemCaixa = "extensão";
            f.Reconhecido = FormatosFolha.Reconhecer(f.Largura, f.Altura, cfg.Formatos, cfg.Tolerancia);
        }
        else { f.Reconhecido = new FormatoReconhecido(); f.Marcada = false; } // layout vazio: não vira PDF por padrão
        f.Formato = f.Reconhecido.Formato;
    }

    /// <summary>Extensão acumulada e candidatos a moldura.</summary>
    private sealed class Coleta
    {
        public double X0 = double.MaxValue, Y0 = double.MaxValue, X1 = double.MinValue, Y1 = double.MinValue;
        public bool Tem;
        public readonly List<(Extents2d Caixa, string Origem)> Candidatos = new List<(Extents2d, string)>();
        public Extents2d Caixa => new Extents2d(X0, Y0, X1, Y1);
        public double Largura => X1 - X0;
        public double Altura => Y1 - Y0;

        public void Somar(Extents3d e) => Somar(e.MinPoint.X, e.MinPoint.Y, e.MaxPoint.X, e.MaxPoint.Y);

        public void Somar(double x0, double y0, double x1, double y1)
        {
            Tem = true;
            X0 = Math.Min(X0, x0); Y0 = Math.Min(Y0, y0); X1 = Math.Max(X1, x1); Y1 = Math.Max(Y1, y1);
        }
    }

    private static void Visitar(Transaction tr, Entity ent, string layerPai, int nivel, Coleta c, HashSet<string> invisiveis, Coleta bruta, string origem)
    {
        if (!ent.Visible || ent is Viewport || ent is AttributeDefinition) return;
        // dentro de bloco, o layer "0" herda o da inserção
        var layer = layerPai != null && string.Equals(ent.Layer, "0", StringComparison.OrdinalIgnoreCase) ? layerPai : ent.Layer;
        if (invisiveis.Contains(layer)) return;
        Extents3d ext;
        bool temExt;
        try { ext = ent.GeometricExtents; temExt = Valida(ext); } catch { ext = default; temExt = false; }

        if (ent is BlockReference br && temExt && nivel < 4 && Grande(ext, bruta))
        {
            var nome = NomeBloco(tr, br);
            var sub = new Coleta();
            var filhos = new DBObjectCollection();
            bool explodiu = true;
            try { br.Explode(filhos); } catch { explodiu = false; }
            if (explodiu)
            {
                foreach (DBObject o in filhos)
                {
                    try { if (o is Entity e2) Visitar(tr, e2, layer, nivel + 1, sub, invisiveis, bruta, "bloco " + nome); }
                    finally { o.Dispose(); }
                }
                foreach (ObjectId aid in br.AttributeCollection)
                {
                    try
                    {
                        var at = (AttributeReference)tr.GetObject(aid, OpenMode.ForRead);
                        if (at.Visible && !at.Invisible && !invisiveis.Contains(at.Layer)) { var ea = at.GeometricExtents; if (Valida(ea)) sub.Somar(ea); }
                    }
                    catch { }
                }
                if (sub.Tem)
                {
                    c.Somar(sub.X0, sub.Y0, sub.X1, sub.Y1);
                    c.Candidatos.AddRange(sub.Candidatos);
                    c.Candidatos.Add((sub.Caixa, "bloco " + nome));
                }
                return;
            }
        }
        if (!temExt) return;
        c.Somar(ext);
        if (ent is Polyline pl && Retangulo(pl))
            c.Candidatos.Add((new Extents2d(ext.MinPoint.X, ext.MinPoint.Y, ext.MaxPoint.X, ext.MaxPoint.Y), origem == null ? "polilinha" : "polilinha no " + origem));
    }

    /// <summary>Bloco que pode conter a moldura: ocupa ao menos 30% da largura ou da altura da extensão bruta.</summary>
    private static bool Grande(Extents3d e, Coleta bruta) =>
        bruta.Tem && (e.MaxPoint.X - e.MinPoint.X >= 0.3 * bruta.Largura || e.MaxPoint.Y - e.MinPoint.Y >= 0.3 * bruta.Altura);

    private static HashSet<string> LayersInvisiveis(Transaction tr, Database db)
    {
        var res = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
        foreach (ObjectId id in lt)
        {
            var l = (LayerTableRecord)tr.GetObject(id, OpenMode.ForRead);
            if (l.IsOff || l.IsFrozen || !l.IsPlottable) res.Add(l.Name);
        }
        return res;
    }

    private static bool Valida(Extents3d e) =>
        e.MaxPoint.X > e.MinPoint.X && e.MaxPoint.Y > e.MinPoint.Y && Math.Abs(e.MinPoint.X) < 1e9 && Math.Abs(e.MaxPoint.Y) < 1e9;

    /// <summary>Polilinha fechada de 4 lados retos, alinhados aos eixos.</summary>
    private static bool Retangulo(Polyline pl)
    {
        int n = pl.NumberOfVertices;
        var pts = new List<Point2d>();
        for (int i = 0; i < n; i++)
        {
            if (Math.Abs(pl.GetBulgeAt(i)) > 1e-9) return false;
            pts.Add(pl.GetPoint2dAt(i));
        }
        if (pts.Count == 5 && pts[0].GetDistanceTo(pts[4]) < 1e-6) pts.RemoveAt(4);
        else if (!pl.Closed) return false;
        if (pts.Count != 4) return false;
        double tol = 1e-3 * Math.Max(1, pl.GeometricExtents.MaxPoint.DistanceTo(pl.GeometricExtents.MinPoint));
        for (int i = 0; i < 4; i++)
        {
            var a = pts[i]; var b = pts[(i + 1) % 4];
            if (Math.Abs(a.X - b.X) > tol && Math.Abs(a.Y - b.Y) > tol) return false;
        }
        return true;
    }

    private static string NomeBloco(Transaction tr, BlockReference br)
    {
        try
        {
            var id = br.IsDynamicBlock ? br.DynamicBlockTableRecord : br.BlockTableRecord;
            return ((BlockTableRecord)tr.GetObject(id, OpenMode.ForRead)).Name;
        }
        catch { return "?"; }
    }
}
