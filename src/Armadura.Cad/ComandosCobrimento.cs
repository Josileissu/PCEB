using System;
using System.Collections.Generic;
using Armadura.Cad.Barra;
using Armadura.Cad.Infra;
using Armadura.Core.Configuracao;
using Armadura.Core.Modelo;

[assembly: Rt.CommandClass(typeof(Armadura.Cad.ComandosCobrimento))]

namespace Armadura.Cad;

/// <summary>
/// "Ponto no cobrimento" (seção 3.7) e ARMRECOTA. A barra é desenhada clicando nas LINHAS da forma:
/// cada vértice é a interseção das linhas deslocadas pelo cobrimento.
/// </summary>
public sealed class ComandosCobrimento
{
    /// <summary>Reta infinita (ponto + direção) no plano XY.</summary>
    private readonly struct Reta
    {
        public readonly Point2d P;
        public readonly Vector2d U;
        public Reta(Point2d p, Vector2d u) { P = p; U = u.GetNormal(); }

        public Reta Deslocada(double d, Point2d lado)
        {
            var n = U.GetPerpendicularVector();
            if ((lado - P).DotProduct(n) < 0) n = n.Negate();
            return new Reta(P + n * d, U);
        }

        public Point2d Projetar(Point2d q) => P + U * (q - P).DotProduct(U);

        public Point2d? Intersecao(Reta o)
        {
            double cruz = U.X * o.U.Y - U.Y * o.U.X;
            if (Math.Abs(cruz) < 1e-9) return null; // paralelas
            var w = o.P - P;
            double t = (w.X * o.U.Y - w.Y * o.U.X) / cruz;
            return P + U * t;
        }
    }

    /// <summary>Chamado pelo ARMBARRA (opção Cobrimento). Retorna a barra criada ou ObjectId.Null.</summary>
    internal static ObjectId DesenharPorCobrimento(Editor ed, Database db, ConfiguracaoArmadura cfg, DadosArmadura dados)
    {
        var rc = Comandos.PedirReal(ed, "Cobrimento (cm)", Plugin.Config.CobrimentoCorrente, 0);
        if (rc == null) return ObjectId.Null;
        double c = rc.Value / Desenho.CmPorUnidade(db, cfg);
        Plugin.Config.CobrimentoCorrente = rc.Value;
        Plugin.SalvarConfig();

        var ucs = ed.CurrentUserCoordinateSystem;
        Point2d P2(Point3d p) { var w = p.TransformBy(ucs); return new Point2d(w.X, w.Y); }

        var vertices = new List<Point2d>();
        Reta? atual = null;
        Point2d? referencia = null; // ponto do lado de dentro, para deduzir o lado das próximas linhas
        double elevacao = 0;

        while (true)
        {
            var pe = new PromptEntityOptions(atual == null
                ? "\nPrimeira linha da forma (Enter termina):"
                : "\nPróxima linha da forma ou")
            { AllowNone = true, AppendKeywordsToMessage = atual != null };
            pe.SetRejectMessage("\nSelecione uma linha ou polilinha.");
            pe.AddAllowedClass(typeof(Line), false);
            pe.AddAllowedClass(typeof(Polyline), false);
            if (atual != null) Comandos.AdicionarPalavras(pe.Keywords, "Ponto", "Desfazer");
            var r = ed.GetEntity(pe);

            if (r.Status == PromptStatus.Keyword)
            {
                if (r.StringResult == "Desfazer") { if (vertices.Count > 0) vertices.RemoveAt(vertices.Count - 1); continue; }
                var rp = ed.GetPoint("\nPonto (projetado na linha deslocada atual):");
                if (rp.Status != PromptStatus.OK) continue;
                var v = atual.Value.Projetar(P2(rp.Value));
                vertices.Add(v);
                referencia = v;
                continue;
            }
            if (r.Status != PromptStatus.OK) break;

            Reta bruta;
            using (var tr = new TransacaoSegura(db))
            {
                var ent = (Curve)tr.GetObject(r.ObjectId, OpenMode.ForRead);
                var pick = r.PickedPoint.TransformBy(ucs);
                var perto = ent.GetClosestPointTo(new Point3d(pick.X, pick.Y, ent.StartPoint.Z), false);
                var dir = ent.GetFirstDerivative(perto);
                elevacao = perto.Z;
                bruta = new Reta(new Point2d(perto.X, perto.Y), new Vector2d(dir.X, dir.Y));
            }

            Reta nova;
            Point2d? inter = atual?.Intersecao(bruta);
            if (atual == null || inter == null || referencia == null)
            {
                // primeira linha, ou paralela à anterior: pergunta o lado
                var rl = ed.GetPoint("\nLado do deslocamento (clique do lado de dentro da peça):");
                if (rl.Status != PromptStatus.OK) continue;
                nova = bruta.Deslocada(c, P2(rl.Value));
            }
            else
            {
                // lado de dentro = lado em que está o trecho já desenhado
                nova = bruta.Deslocada(c, referencia.Value);
            }

            if (atual != null)
            {
                var v = atual.Value.Intersecao(nova);
                if (v != null) vertices.Add(v.Value);
                else ed.WriteMessage("\nLinhas paralelas: use a opção Ponto para marcar o vértice.");
            }
            atual = nova;
            referencia = vertices.Count > 0 ? vertices[vertices.Count - 1] : nova.Projetar(new Point2d(bruta.P.X, bruta.P.Y));
            // a referência precisa estar do lado de dentro da linha nova: um pouco para trás no trecho anterior
            if (vertices.Count >= 2)
            {
                var a = vertices[vertices.Count - 2];
                var b = vertices[vertices.Count - 1];
                referencia = b + (a - b) * 0.5;
            }
        }

        if (vertices.Count < 2) { ed.WriteMessage("\nSão precisos ao menos 2 vértices."); return ObjectId.Null; }
        using var t = new TransacaoSegura(db);
        var pl = new Polyline { Elevation = elevacao };
        pl.SetDatabaseDefaults(db);
        for (int i = 0; i < vertices.Count; i++) pl.AddVertexAt(i, vertices[i], 0, 0, 0);
        var esp = (BlockTableRecord)t.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
        esp.AppendEntity(pl);
        t.AddNewlyCreatedDBObject(pl, true);
        ServicoBarra.Criar(t, pl, dados, cfg);
        t.Commit();
        return pl.ObjectId;
    }

    /// <summary>ARMRECOTA (seção 3.1): clica no segmento → a cota apagada volta (Cotar = Sim). Em laço.</summary>
    [Rt.CommandMethod("ARMRECOTA")]
    public void Recota() => Comandos.Executar("ARMRECOTA", (doc, ed, db, cfg) =>
    {
        while (true)
        {
            var pe = new PromptEntityOptions("\nClique no segmento da armadura (Enter termina):") { AllowNone = true };
            pe.SetRejectMessage("\nNão é uma armadura.");
            pe.AddAllowedClass(typeof(Polyline), false);
            var r = ed.GetEntity(pe);
            if (r.Status != PromptStatus.OK) return;
            using var tr = new TransacaoSegura(db);
            var pl = tr.GetObject(r.ObjectId, OpenMode.ForRead) as Polyline;
            var d = pl == null ? null : XDataCad.LerBarra(pl);
            if (d == null) { ed.WriteMessage("\nNão é uma armadura."); continue; }
            var w = r.PickedPoint.TransformBy(ed.CurrentUserCoordinateSystem);
            var perto = pl.GetClosestPointTo(new Point3d(w.X, w.Y, pl.Elevation), false);
            int idx = (int)Math.Floor(pl.GetParameterAtPoint(perto) + 1e-9);
            d.AjustarSegmentos(GeometriaBarra.NumeroSegmentos(pl));
            idx = Math.Max(0, Math.Min(d.Segmentos.Count - 1, idx));
            if (d.Segmentos[idx].Cotar) { ed.WriteMessage($"\nO segmento {idx + 1} já tem cota."); continue; }
            d.Segmentos[idx].Cotar = true;
            pl.UpgradeOpen();
            XDataCad.Gravar(pl, Core.Persistencia.CodecDados.Codificar(d));
            ServicoBarra.Atualizar(tr, pl, cfg);
            tr.Commit();
            ed.WriteMessage($"\nCota do segmento {idx + 1} restaurada.");
        }
    });
}
