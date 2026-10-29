using System;
using System.Collections.Generic;
using System.Linq;
using Armadura.Cad.Barra;
using Armadura.Cad.Infra;
using Armadura.Core.Configuracao;
using Armadura.Core.Modelo;
using Armadura.Core.Persistencia;
using Armadura.Core.Reconhecimento;

[assembly: Rt.CommandClass(typeof(Armadura.Cad.ComandosReconhecimento))]

namespace Armadura.Cad;

/// <summary>Resultado do reconhecimento (para a mensagem do comando e o autoteste).</summary>
internal sealed class ResultadoReconhecimento
{
    public List<ObjectId> Barras { get; } = new List<ObjectId>();
    public int Estribos { get; set; }
    public int CotasAproveitadas { get; set; }
    public List<ObjectId> CurvasSemTexto { get; } = new List<ObjectId>();
    public List<string> TextosSemBarra { get; } = new List<string>();
    public List<string> Avisos { get; } = new List<string>();
}

/// <summary>
/// ARMRECONHECER: barras desenhadas por outros programas (ou à mão), em QUALQUER layer — polilinhas e linhas
/// com o texto ao lado ("25 N1 ø10 c/15 C=300") viram armaduras com os dados do texto. As medidas dos trechos
/// ("35", "42") viram as cotas da barra no mesmo lugar. ARMTEXTOPADRAO: descrição e cotas de volta ao padrão.
/// </summary>
public sealed class ComandosReconhecimento
{
    /// <summary>Distância máxima texto → barra, em alturas do texto.</summary>
    internal const double DistanciaTexto = 8;
    /// <summary>Distância máxima medida → trecho, em alturas do texto.</summary>
    internal const double DistanciaMedida = 4;
    private static readonly double[] EscalasComuns = { 10, 20, 25, 50, 75, 100 };

    [Rt.CommandMethod("ARMRECONHECER", Rt.CommandFlags.UsePickSet | Rt.CommandFlags.Redraw)]
    public void ArmReconhecer() => Comandos.Executar("ARMRECONHECER", (doc, ed, db, cfg) =>
    {
        var filtro = new SelectionFilter(new[] { new TypedValue((int)DxfCode.Start, "LWPOLYLINE,LINE,TEXT,MTEXT") });
        var impl = ed.SelectImplied();
        ObjectId[] ids;
        if (impl.Status == PromptStatus.OK && impl.Value != null && impl.Value.Count > 0)
        {
            ids = impl.Value.GetObjectIds();
            ed.SetImpliedSelection(new ObjectId[0]);
        }
        else
        {
            var r = ed.GetSelection(new PromptSelectionOptions
            {
                MessageForAdding = "\nSelecione as barras (polilinhas/linhas) e os textos delas, em qualquer layer:",
            }, filtro);
            if (r.Status != PromptStatus.OK) return;
            ids = r.Value.GetObjectIds();
        }

        var pk = new PromptKeywordOptions("\nTextos originais reconhecidos") { AllowNone = true, AppendKeywordsToMessage = true };
        Comandos.AdicionarPalavras(pk.Keywords, "Apagar", "Manter");
        pk.Keywords.Default = "Apagar";
        var rk = ed.GetKeywords(pk);
        if (rk.Status != PromptStatus.OK && rk.Status != PromptStatus.None) return;
        bool apagar = rk.Status == PromptStatus.None || rk.StringResult == "Apagar";

        var res = Reconhecer(db, ids, cfg, apagar, () => Comandos.PerguntarEstribo(ed));
        if (res == null) return;

        ed.WriteMessage($"\n{res.Barras.Count} barra(s) reconhecida(s)" + (res.Estribos > 0 ? $" ({res.Estribos} estribo(s) com ganchos)" : "") +
                        (res.CotasAproveitadas > 0 ? $"; {res.CotasAproveitadas} medida(s) viraram cotas" : "") + ".");
        foreach (var a in res.Avisos) ed.WriteMessage("\n  " + a);
        if (res.TextosSemBarra.Count > 0)
            ed.WriteMessage($"\n{res.TextosSemBarra.Count} texto(s) de barra sem polilinha por perto: " +
                            string.Join(" | ", res.TextosSemBarra.Take(10)) + (res.TextosSemBarra.Count > 10 ? " …" : ""));
        if (res.CurvasSemTexto.Count > 0)
        {
            ed.WriteMessage($"\n{res.CurvasSemTexto.Count} polilinha(s)/linha(s) sem texto reconhecido ficaram selecionadas (ARMCONVERTER converte com os valores correntes).");
            ed.SetImpliedSelection(res.CurvasSemTexto.Where(id => !id.IsErased).ToArray());
        }
        if (res.Barras.Count > 0) ed.WriteMessage("\nUse ARMRAATUALIZAR para numerar e atualizar a relação do aço.");
        Log.Info($"ARMRECONHECER: {res.Barras.Count} barras, {res.CotasAproveitadas} cotas, {res.TextosSemBarra.Count} textos sem barra, {res.CurvasSemTexto.Count} curvas sem texto.");
    });

    [Rt.CommandMethod("ARMTEXTOPADRAO", Rt.CommandFlags.UsePickSet)]
    public void ArmTextoPadrao() => Comandos.Executar("ARMTEXTOPADRAO", (doc, ed, db, cfg) =>
    {
        var impl = ed.SelectImplied();
        ObjectId[] ids;
        if (impl.Status == PromptStatus.OK && impl.Value != null && impl.Value.Count > 0) ids = impl.Value.GetObjectIds();
        else
        {
            var r = ed.GetSelection(new PromptSelectionOptions { MessageForAdding = "\nSelecione as armaduras (descrição e cotas voltam à posição padrão):" });
            if (r.Status != PromptStatus.OK) return;
            ids = r.Value.GetObjectIds();
        }
        int n = 0;
        using (var tr = db.TransactionManager.StartTransaction())
        {
            var feitas = new HashSet<ObjectId>();
            foreach (var id in ids)
            {
                var pl = ServicoBarra.ResolverBarra(tr, id);
                if (pl == null || !feitas.Add(pl.ObjectId)) continue;
                ServicoBarra.RestaurarPosicoes(tr, (Polyline)tr.GetObject(pl.ObjectId, OpenMode.ForWrite), cfg);
                n++;
            }
            tr.Commit();
        }
        ed.WriteMessage($"\n{n} armadura(s) com os textos na posição padrão.");
    });

    private sealed class Curva
    {
        public ObjectId Id;
        public Curve Ent;
        public double ComprimentoCm;
    }

    private sealed class Texto
    {
        public ObjectId Id;
        public Point2d Centro;
        public double Altura;
        public TextoBarra Barra;
        public double Medida;
    }

    /// <summary>
    /// Reconhece as barras entre os objetos dados. <paramref name="perguntarEstribo"/> só é chamado se alguma
    /// polilinha fechada tiver texto (null = cancelado → nada é feito).
    /// </summary>
    internal static ResultadoReconhecimento Reconhecer(Database db, IEnumerable<ObjectId> ids, ConfiguracaoArmadura cfg,
        bool apagarTextos, Func<bool?> perguntarEstribo)
    {
        var res = new ResultadoReconhecimento();
        double cpu = Desenho.CmPorUnidade(db, cfg);
        using var tr = new TransacaoSegura(db);

        // ---- 1. o que foi selecionado ----
        var curvas = new List<Curva>();
        var textos = new List<Texto>();
        var medidas = new List<Texto>();
        foreach (var id in ids.Distinct())
        {
            if (id.IsErased || !(tr.GetObject(id, OpenMode.ForRead) is Entity ent)) continue;
            if (XDataCad.Tipo(ent) != TipoParte.Nenhum) continue; // já é do plugin
            switch (ent)
            {
                case Polyline pl when pl.NumberOfVertices >= 2 && pl.Length > 1e-9:
                    curvas.Add(new Curva { Id = id, Ent = pl, ComprimentoCm = pl.Length * cpu });
                    break;
                case Line ln when ln.Length > 1e-9:
                    curvas.Add(new Curva { Id = id, Ent = ln, ComprimentoCm = ln.Length * cpu });
                    break;
                case DBText dt:
                    Classificar(id, dt.TextString, Centro(dt, new Point2d(dt.Position.X, dt.Position.Y)), dt.Height, textos, medidas);
                    break;
                case MText mt:
                    Classificar(id, mt.Contents, Centro(mt, new Point2d(mt.Location.X, mt.Location.Y)), mt.TextHeight, textos, medidas);
                    break;
            }
        }

        // ---- 2. cada texto de barra com a polilinha/linha mais próxima (C= que confere pesa a favor) ----
        var candidatos = new List<(int, int, double)>();
        for (int c = 0; c < curvas.Count; c++)
            for (int t = 0; t < textos.Count; t++)
            {
                double dist = Distancia(curvas[c].Ent, textos[t].Centro);
                if (dist > DistanciaTexto * textos[t].Altura) continue;
                double custo = dist / textos[t].Altura;
                if (TextoArmadura.ComprimentoConfere(textos[t].Barra.Comprimento, curvas[c].ComprimentoCm)) custo *= 0.5;
                candidatos.Add((c, t, custo));
            }
        var pares = TextoArmadura.Associar(candidatos);
        var comTexto = new HashSet<int>(pares.Select(p => p.A));
        var usados = new HashSet<int>(pares.Select(p => p.B));
        for (int c = 0; c < curvas.Count; c++) if (!comTexto.Contains(c)) res.CurvasSemTexto.Add(curvas[c].Id);
        for (int t = 0; t < textos.Count; t++) if (!usados.Contains(t)) res.TextosSemBarra.Add(textos[t].Barra.Original);
        if (pares.Count == 0) { tr.Commit(); return res; }

        bool estribos = false;
        if (pares.Any(p => curvas[p.A].Ent is Polyline pl && ServicoEstribo.PoligonoFechado(pl) != null))
        {
            var e = perguntarEstribo();
            if (e == null) { tr.Commit(); return null; }
            estribos = e.Value;
        }

        // ---- 3. as barras ----
        var criadas = new List<(Polyline pl, Texto txt)>();
        foreach (var (c, t) in pares)
        {
            var txt = textos[t];
            var pl = ComoPolilinha(tr, curvas[c].Ent);
            var tb = txt.Barra;
            var d = Comandos.NovaBarra(cfg);
            d.Bitola = tb.Bitola;
            var tabela = cfg.Barras.Obter(tb.Bitola, d.Aco);
            if (tabela != null) d.Aco = tabela.Aco;
            else res.Avisos.Add($"\"{tb.Original}\": ø{tb.Bitola:0.0} fora da tabela de bitolas.");
            d.Quantidade = tb.Quantidade > 0 ? tb.Quantidade : 1;
            d.Repeticoes = tb.Repeticoes;
            d.Espacamento = tb.Espacamento;
            d.Numero = tb.Numero;
            d.Escala = EscalaPeloTexto(txt.Altura, cpu, cfg);
            bool estribo = estribos && ServicoEstribo.PoligonoFechado(pl) != null;
            d.Estribo = estribo;
            // a descrição nasce onde estava o texto original
            var origem = pl.GetPoint2dAt(0);
            d.TemPosicaoDescricao = true;
            d.DescricaoDx = txt.Centro.X - origem.X;
            d.DescricaoDy = txt.Centro.Y - origem.Y;
            ServicoBarra.Criar(tr, pl, d, cfg);
            if (estribo && ServicoEstribo.Converter(tr, pl, XDataCad.LerBarra(pl), cfg) == null)
            {
                ServicoBarra.Atualizar(tr, pl, cfg);
                res.Estribos++;
            }
            if (tb.Comprimento.HasValue && !estribo)
            {
                // o C= do outro programa pode ser a soma externa ou o comprimento real (descontadas as dobras)
                double desenhado = curvas[c].ComprimentoCm;
                double real = Core.Calculo.CalculoComprimento.ComprimentoUnitario(GeometriaBarra.Ler(pl, cpu), cfg, XDataCad.LerBarra(pl));
                if (!TextoArmadura.ComprimentoConfere(tb.Comprimento, desenhado) && !TextoArmadura.ComprimentoConfere(tb.Comprimento, real))
                    res.Avisos.Add(FormattableString.Invariant($"\"{tb.Original}\": o texto diz C={tb.Comprimento:0.#}, a polilinha mede {desenhado:0.#} cm."));
            }
            res.Barras.Add(pl.ObjectId);
            criadas.Add((pl, txt));
        }

        // ---- 4. medidas dos trechos ("35", "42") → cotas da barra no mesmo lugar ----
        var trechos = new List<(Polyline pl, int seg)>();
        var candMedidas = new List<(int, int, double)>();
        foreach (var (pl, _) in criadas)
        {
            var geo = GeometriaBarra.Ler(pl, cpu);
            for (int i = 0; i < geo.Count; i++)
            {
                if (GeometriaBarra.EhArco(pl, i)) continue;
                var seg = pl.GetLineSegment2dAt(i);
                for (int m = 0; m < medidas.Count; m++)
                {
                    if (!TextoArmadura.MedidaConfere(medidas[m].Medida, geo[i].Comprimento)) continue;
                    double dist = seg.GetDistanceTo(medidas[m].Centro);
                    if (dist > DistanciaMedida * medidas[m].Altura) continue;
                    candMedidas.Add((trechos.Count, m, dist / medidas[m].Altura));
                }
                trechos.Add((pl, i));
            }
        }
        var paresMedida = TextoArmadura.Associar(candMedidas);
        var barrasComMedida = new HashSet<ObjectId>(paresMedida.Select(p => trechos[p.A].pl.ObjectId));
        var trechosComMedida = new HashSet<int>(paresMedida.Select(p => p.A));
        foreach (var (pl, _) in criadas)
        {
            if (!barrasComMedida.Contains(pl.ObjectId)) continue;
            // o desenho original cotou só alguns trechos: os outros ficam sem cota
            var d = XDataCad.LerBarra(pl);
            for (int k = 0; k < trechos.Count; k++)
                if (trechos[k].pl.ObjectId == pl.ObjectId && !trechosComMedida.Contains(k) && trechos[k].seg < d.Segmentos.Count)
                    d.Segmentos[trechos[k].seg].Cotar = false;
            if (!pl.IsWriteEnabled) pl.UpgradeOpen();
            XDataCad.Gravar(pl, CodecDados.Codificar(d));
            ServicoBarra.Atualizar(tr, pl, cfg);
        }
        foreach (var (k, m) in paresMedida)
        {
            var (pl, i) = trechos[k];
            var d = XDataCad.LerBarra(pl);
            var cota = ServicoBarra.Parte<DBText>(tr, db, d.Segmentos[i].HandleCota, TipoParte.Cota, XDataCad.Hex(pl.ObjectId));
            if (cota == null) continue;
            // a cota (alinhada pelo pé ou pelo topo) com o centro onde estava a medida
            var (_, dir) = GeometriaBarra.Meio(pl, i);
            if (dir.X < -1e-9 || (Math.Abs(dir.X) <= 1e-9 && dir.Y < 0)) dir = dir.Negate();
            var meiaAltura = dir.GetPerpendicularVector() * (cota.Height / 2);
            var alvo = cota.VerticalMode == TextVerticalMode.TextTop ? medidas[m].Centro + meiaAltura : medidas[m].Centro - meiaAltura;
            cota.UpgradeOpen();
            cota.AlignmentPoint = new Point3d(alvo.X, alvo.Y, pl.Elevation);
            cota.AdjustAlignment(db);
            ServicoBarra.RegistrarMovimentoCota(tr, cota, cfg);
            res.CotasAproveitadas++;
            if (apagarTextos) Apagar(tr, medidas[m].Id);
        }

        if (apagarTextos)
            foreach (var (_, t) in pares) Apagar(tr, textos[t].Id);
        tr.Commit();
        return res;
    }

    private static void Classificar(ObjectId id, string conteudo, Point2d centro, double altura, List<Texto> textos, List<Texto> medidas)
    {
        if (altura <= 1e-9) return;
        var tb = TextoArmadura.Interpretar(conteudo);
        if (tb != null) textos.Add(new Texto { Id = id, Centro = centro, Altura = altura, Barra = tb });
        else if (TextoArmadura.EhMedida(conteudo, out var v)) medidas.Add(new Texto { Id = id, Centro = centro, Altura = altura, Medida = v });
    }

    private static Point2d Centro(Entity e, Point2d reserva)
    {
        try
        {
            var ext = e.GeometricExtents;
            return new Point2d((ext.MinPoint.X + ext.MaxPoint.X) / 2, (ext.MinPoint.Y + ext.MaxPoint.Y) / 2);
        }
        catch (Rt.Exception) { return reserva; }
    }

    private static double Distancia(Curve c, Point2d p)
    {
        var q = c.GetClosestPointTo(new Point3d(p.X, p.Y, c is Polyline pl ? pl.Elevation : ((Line)c).StartPoint.Z), false);
        return new Point2d(q.X, q.Y).GetDistanceTo(p);
    }

    /// <summary>Linha vira polilinha de 2 vértices (no mesmo layer e espaço); polilinha fica como está.</summary>
    private static Polyline ComoPolilinha(Transaction tr, Curve c)
    {
        if (c is Polyline pl) return (Polyline)tr.GetObject(pl.ObjectId, OpenMode.ForWrite);
        var ln = (Line)tr.GetObject(c.ObjectId, OpenMode.ForWrite);
        var nova = new Polyline { Elevation = ln.StartPoint.Z };
        nova.SetPropertiesFrom(ln);
        nova.AddVertexAt(0, new Point2d(ln.StartPoint.X, ln.StartPoint.Y), 0, 0, 0);
        nova.AddVertexAt(1, new Point2d(ln.EndPoint.X, ln.EndPoint.Y), 0, 0, 0);
        var dono = (BlockTableRecord)tr.GetObject(ln.OwnerId, OpenMode.ForWrite);
        dono.AppendEntity(nova);
        tr.AddNewlyCreatedDBObject(nova, true);
        ln.Erase();
        return nova;
    }

    /// <summary>Escala da barra pela altura do texto original (texto de 2 un → 1:10; 5 un → 1:25).</summary>
    internal static double EscalaPeloTexto(double alturaTexto, double cpu, ConfiguracaoArmadura cfg)
    {
        if (cfg.AlturaTextoMm <= 0) return cfg.Escala;
        double escala = alturaTexto * cpu * 10.0 / cfg.AlturaTextoMm;
        if (escala < 5 || escala > 200) return cfg.Escala;
        return EscalasComuns.OrderBy(e => Math.Abs(Math.Log(e / escala))).First();
    }

    private static void Apagar(Transaction tr, ObjectId id)
    {
        if (id.IsErased) return;
        tr.GetObject(id, OpenMode.ForWrite).Erase();
    }
}
