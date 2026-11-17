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
    /// <summary>Nome do layout no desenho ("Model" nas folhas desenhadas no Model).</summary>
    public string Layout { get; set; }
    /// <summary>Nome da folha na lista e no PDF: o layout, ou "Model"/"Model 2"… (uma folha por moldura do Model).</summary>
    public string Rotulo { get; set; }
    /// <summary>Folha desenhada no Model (moldura em escala): 1, 2…; 0 = layout.</summary>
    public int IndiceModelo { get; set; }
    public bool NoModelo => IndiceModelo > 0;
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
    /// <summary>Falha da leitura (ex.: nenhuma moldura em lugar nenhum), com a causa para o relatório.</summary>
    public DiagnosticoErroPdf Diagnostico { get; set; }

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

    /// <summary>
    /// Folhas do desenho: os layouts com moldura reconhecida; se nenhum layout tiver moldura (DWG só com Model, layouts vazios ou
    /// só com viewport), as molduras desenhadas no Model, uma folha por moldura. Nada em lugar nenhum: uma linha de erro
    /// ("nenhuma moldura/carimbo…"). A regra é do núcleo (<see cref="FolhasModeloPdf"/>).
    /// </summary>
    private static List<FolhaPdf> Ler(Database db, string arquivo, ConfigLotePdf cfg)
    {
        var res = new List<FolhaPdf>();
        var lidos = new List<LayoutLido>();
        Layout modelo = null;
        using (var tr = db.TransactionManager.StartTransaction())
        {
            var invisiveis = LayersInvisiveis(tr, db);
            var dic = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);
            foreach (DBDictionaryEntry e in dic)
            {
                var lay = (Layout)tr.GetObject(e.Value, OpenMode.ForRead);
                if (lay.ModelType) { modelo = lay; continue; }
                var f = new FolhaPdf
                {
                    Arquivo = arquivo, Layout = lay.LayoutName, Rotulo = lay.LayoutName, Ordem = lay.TabOrder,
                    EstiloAtual = lay.CurrentStyleSheet ?? "", PageSetupAtual = PageSetup(lay),
                };
                try { Reconhecer(tr, lay, f, cfg, invisiveis); }
                catch (Exception ex) { f.Erro = ex.Message; Infra.Log.Erro($"ARMPDFLOTE: ler {arquivo} / {lay.LayoutName}", ex); }
                lidos.Add(new LayoutLido
                {
                    Nome = lay.LayoutName,
                    Vazio = f.Erro == null && !f.TemCaixa,
                    Reconhecido = f.Reconhecido?.Formato != null,
                });
                res.Add(f);
            }

            var molduras = new List<MolduraReconhecida>();
            if (modelo != null && FolhasModeloPdf.PrecisaModelo(lidos))
            {
                try { molduras = MoldurasDoModelo(tr, modelo, cfg, invisiveis); }
                catch (Exception ex) { Infra.Log.Erro($"ARMPDFLOTE: ler o Model de {arquivo}", ex); }
            }
            var plano = FolhasModeloPdf.Decidir(lidos, molduras);
            foreach (var f in res) f.Marcada = plano.LayoutsMarcados.Contains(f.Layout);
            if (plano.UsarModelo)
            {
                for (int i = 0; i < molduras.Count; i++)
                {
                    var m = molduras[i];
                    res.Add(new FolhaPdf
                    {
                        Arquivo = arquivo, Layout = modelo.LayoutName, Rotulo = FolhasModeloPdf.Rotulo(i + 1, molduras.Count), IndiceModelo = i + 1,
                        Ordem = -1000 + i, EstiloAtual = modelo.CurrentStyleSheet ?? "", PageSetupAtual = PageSetup(modelo),
                        Caixa = new Extents2d(m.Caixa.X0, m.Caixa.Y0, m.Caixa.X1, m.Caixa.Y1), TemCaixa = true,
                        OrigemCaixa = m.Origem + " no Model" + Escala(m.Reconhecido.Fator), Reconhecido = m.Reconhecido, Formato = m.Reconhecido.Formato,
                    });
                }
                Infra.Log.Info($"ARMPDFLOTE: {arquivo}: nenhum layout com moldura; {molduras.Count} folha(s) no Model");
            }
            else if (plano.SemMoldura)
            {
                var d = ErrosLotePdf.De(CausaErroPdf.SemMoldura, EtapaLotePdf.Ler);
                res.Add(new FolhaPdf
                {
                    Arquivo = arquivo, Layout = modelo?.LayoutName ?? "Model", Rotulo = "(nenhuma folha)", Marcada = false, Ordem = -2000,
                    Erro = d.Mensagem, Diagnostico = d, Reconhecido = new FormatoReconhecido(),
                });
                Infra.Log.Info($"ARMPDFLOTE: {arquivo}: {d.Mensagem}");
            }
            tr.Commit();
        }
        return res.OrderBy(f => f.Ordem).ToList();
    }

    /// <summary>" (1:50 em cm)" a partir do fator mm de papel por unidade (cm 1:50 = 0,2), quando a escala é redonda.</summary>
    private static string Escala(double fator)
    {
        if (fator <= 0) return "";
        // a unidade do desenho não é conhecida com certeza: mostra as leituras com escala usual (ex.: "1:5 em mm / 1:50 em cm")
        var op = new List<string>();
        foreach (var u in FolhasModeloPdf.Unidades)
        {
            double s = u / fator;
            if (FolhasModeloPdf.Escalas.Any(e => Math.Abs(e - s) < 1e-6))
                op.Add(FormattableString.Invariant($"1:{s:0.#} em {(u == 1 ? "mm" : u == 10 ? "cm" : "m")}"));
        }
        return op.Count > 0 ? " (" + string.Join(" / ", op) + ")" : "";
    }

    private static string PageSetup(Layout lay)
    {
        try
        {
            return FormattableString.Invariant(
                $"{lay.PlotConfigurationName} | {lay.CanonicalMediaName} | {lay.PlotRotation} | {lay.PlotType} | papel {lay.PlotPaperSize.X:0.#}x{lay.PlotPaperSize.Y:0.#} | janela {lay.PlotWindowArea.MinPoint.X:0.#},{lay.PlotWindowArea.MinPoint.Y:0.#}-{lay.PlotWindowArea.MaxPoint.X:0.#},{lay.PlotWindowArea.MaxPoint.Y:0.#} | origem {lay.PlotOrigin.X:0.#},{lay.PlotOrigin.Y:0.#} | centrado {lay.PlotCentered} | unid {lay.PlotPaperUnits} | estilo {lay.CurrentStyleSheet}");
        }
        catch { return ""; }
    }

    /// <summary>
    /// Candidatos do Model: polilinhas retangulares e blocos (explodidos em memória quando são grandes ou têm, sozinhos, a medida de
    /// um formato em alguma escala). A escolha das molduras é do núcleo (<see cref="FolhasModeloPdf.Selecionar"/>).
    /// </summary>
    private static List<MolduraReconhecida> MoldurasDoModelo(Transaction tr, Layout modelo, ConfigLotePdf cfg, HashSet<string> invisiveis)
    {
        var btr = (BlockTableRecord)tr.GetObject(modelo.BlockTableRecordId, OpenMode.ForRead);
        var bruta = new Coleta();
        var entidades = new List<Entity>();
        foreach (ObjectId id in btr)
        {
            if (!(tr.GetObject(id, OpenMode.ForRead) is Entity ent) || !ent.Visible || invisiveis.Contains(ent.Layer)) continue;
            if (!(ent is Polyline) && !(ent is BlockReference)) continue; // só o que pode ser moldura
            entidades.Add(ent);
            try { var e = ent.GeometricExtents; if (Valida(e)) bruta.Somar(e); } catch { }
        }
        var c = new Coleta { Formatos = cfg.Formatos, Tolerancia = cfg.Tolerancia };
        foreach (var ent in entidades) Visitar(tr, ent, null, 0, c, invisiveis, bruta, null);
        return FolhasModeloPdf.Selecionar(c.Candidatos.Select(k => new CandidatoMoldura(
            new CaixaPdf(k.Caixa.MinPoint.X, k.Caixa.MinPoint.Y, k.Caixa.MaxPoint.X, k.Caixa.MaxPoint.Y), k.Origem)), cfg.Formatos, cfg.Tolerancia);
    }

    /// <summary>
    /// Candidatos: polilinhas retangulares fechadas e blocos (carimbo com moldura) do layout; vale o MAIOR que casa com um formato.
    /// Sem candidato: a extensão do layout. Só conta o que é VISÍVEL na plotagem: sem viewports, sem layers desligados/congelados/
    /// não plotáveis e, dentro dos blocos grandes (explodidos em memória), só os objetos do estado de visibilidade atual — o
    /// GeometricExtents de um bloco dinâmico no GstarCAD inclui os estados ocultos (carimbo com A0…A0++ no mesmo bloco).
    /// </summary>
    private static void Reconhecer(Transaction tr, Layout lay, FolhaPdf f, ConfigLotePdf cfg, HashSet<string> invisiveis)
    {
        var btr = (BlockTableRecord)tr.GetObject(lay.BlockTableRecordId, OpenMode.ForRead);
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
        /// <summary>No Model: formatos para decidir se um bloco tem a medida de uma folha em alguma escala (null = layout).</summary>
        public List<FormatoFolha> Formatos;
        public double Tolerancia = 10;
        /// <summary>Blocos explodidos até agora (compartilhado com as sub-coletas; limite no Model, que pode ter milhares).</summary>
        public int[] Explosoes = new int[1];
        public Coleta Filha() => new Coleta { Formatos = Formatos, Tolerancia = Tolerancia, Explosoes = Explosoes };
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

        if (ent is BlockReference br && temExt && nivel < 4 && Explodir(ext, bruta, c))
        {
            c.Explosoes[0]++;
            var nome = NomeBloco(tr, br);
            var sub = c.Filha();
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

    /// <summary>Limite de blocos explodidos no Model (desenho grande: o resto fica só com a extensão).</summary>
    private const int MaxExplosoesModelo = 3000;

    /// <summary>
    /// Explodir o bloco para procurar a moldura? No layout: se for grande. No Model (várias folhas lado a lado, cada uma pequena
    /// perto do todo): também se a extensão dele, sozinha, tiver a medida de um formato em alguma escala (bloco do carimbo).
    /// </summary>
    private static bool Explodir(Extents3d e, Coleta bruta, Coleta c)
    {
        if (c.Formatos == null) return Grande(e, bruta);
        if (c.Explosoes[0] >= MaxExplosoesModelo) return false;
        return Grande(e, bruta) || FolhasModeloPdf.Reconhecer(e.MaxPoint.X - e.MinPoint.X, e.MaxPoint.Y - e.MinPoint.Y, c.Formatos, c.Tolerancia).Formato != null;
    }

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
