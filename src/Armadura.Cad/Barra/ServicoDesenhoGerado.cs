using System;
using System.Collections.Generic;
using System.Linq;
using Armadura.Cad.Infra;
using Armadura.Core.Configuracao;
using Armadura.Core.Modelo;
using Armadura.Core.Persistencia;
using Armadura.Core.Tabelas;

namespace Armadura.Cad.Barra;

/// <summary>
/// Desenho gerado de treliças (banzo superior + sinusoide, a partir do banzo inferior) e de telas
/// (diagonal do painel). As partes são polilinhas/linhas nativas ligadas à barra e regeneradas a
/// cada atualização (seções 2.5 e 2.6).
/// </summary>
internal static class ServicoDesenhoGerado
{
    /// <summary>Recria/acerta as partes; devolve os handles válidos (na ordem).</summary>
    /// <summary>
    /// Comprimento (unid.) da interrupção da diagonal: a largura do texto da tela mais uma folga de meia altura
    /// de cada lado. Sem a medida do CAD, estima pelo nº de caracteres da linha mais longa.
    /// </summary>
    private static double VaoDoTexto(Transaction tr, Database db, DadosArmadura d, ConfiguracaoArmadura cfg, double cpu)
    {
        double h = cfg.AlturaTexto(cpu, d.Escala);
        var id = XDataCad.IdDoHandle(db, d.HandleDescricao);
        double largura = 0;
        if (!id.IsNull && !id.IsErased && tr.GetObject(id, OpenMode.ForRead) is MText mt)
        {
            try { largura = mt.ActualWidth; } catch (Exception) { largura = 0; }
            if (largura <= 1e-9)
                largura = mt.Contents.Split(new[] { "\\P" }, StringSplitOptions.None).Max(s => s.Length) * 0.8 * h;
        }
        return largura > 0 ? largura + h : 0;
    }

    public static List<string> Atualizar(Transaction tr, Polyline pl, DadosArmadura d, string hBarra, ConfiguracaoArmadura cfg)
    {
        var db = pl.Database;
        var existentes = d.PartesDesenho.Select(h => Parte(tr, db, h, hBarra)).Where(e => e != null).ToList();
        var desejadas = new List<(Type tipo, List<Point2d> pts)>();
        double cpu = Desenho.CmPorUnidade(db, cfg);

        if (d.EhTrelica && pl.NumberOfVertices >= 2)
        {
            var m = cfg.Trelicas.Trelica(d.Trelica);
            if (m != null)
            {
                var p0 = pl.GetPoint2dAt(0);
                var p1 = pl.GetPoint2dAt(1);
                double l = p0.GetDistanceTo(p1);
                if (l > 1e-9)
                {
                    var u = (p1 - p0).GetNormal();
                    var n = u.GetPerpendicularVector();
                    double h = m.Altura / cpu;
                    desejadas.Add((typeof(Polyline), new List<Point2d> { p0 + n * h, p1 + n * h })); // banzo superior
                    var sin = Catalogos.Sinusoide(l * cpu, m.Altura, m.Passo)
                        .Select(s => p0 + u * (s.X / cpu) + n * (s.Y / cpu)).ToList();
                    desejadas.Add((typeof(Polyline), sin));
                }
            }
        }
        else if (d.EhTela && pl.NumberOfVertices >= 4)
        {
            var v0 = pl.GetPoint2dAt(0);
            var v2 = pl.GetPoint2dAt(2);
            double vao = d.TelaIdentificada ? VaoDoTexto(tr, db, d, cfg, cpu) : 0;
            if (vao > 1e-9 && vao < v0.GetDistanceTo(v2) * 0.9)
            {
                // tela identificada: diagonal interrompida onde fica o texto (centrado no meio dela)
                var u = (v2 - v0).GetNormal();
                var meio = v0 + (v2 - v0) * 0.5;
                desejadas.Add((typeof(Line), new List<Point2d> { v0, meio - u * (vao / 2) }));
                desejadas.Add((typeof(Line), new List<Point2d> { meio + u * (vao / 2), v2 }));
            }
            else desejadas.Add((typeof(Line), new List<Point2d> { v0, v2 })); // diagonal
            // distribuição do aço: os fios do modelo dentro do painel (lados v0→v1 e v0→v3)
            var e1 = pl.GetPoint2dAt(1) - v0;
            var e2 = pl.GetPoint2dAt(3) - v0;
            var m = cfg.Telas.Tela(d.Tela);
            if (d.TelaDistribuicao && m != null && e1.Length > 1e-9 && e2.Length > 1e-9)
            {
                var u1 = e1.GetNormal();
                var u2 = e2.GetNormal();
                foreach (var (a, b) in DistribuicaoTela.Fios(e1.Length * cpu, e2.Length * cpu, m))
                    desejadas.Add((typeof(Line), new List<Point2d>
                    {
                        v0 + u1 * (a.X / cpu) + u2 * (a.Y / cpu), v0 + u1 * (b.X / cpu) + u2 * (b.Y / cpu),
                    }));
            }
        }

        var owner = (BlockTableRecord)tr.GetObject(pl.OwnerId, OpenMode.ForRead);
        var usadas = new HashSet<ObjectId>();
        var resultado = new List<string>();
        foreach (var (tipo, pts) in desejadas)
        {
            var e = existentes.FirstOrDefault(x => x.GetType() == tipo && !usadas.Contains(x.ObjectId));
            if (e != null) { usadas.Add(e.ObjectId); if (!e.IsWriteEnabled) e.UpgradeOpen(); }
            if (tipo == typeof(Line))
            {
                var ln = (Line)e ?? new Line();
                ln.StartPoint = new Point3d(pts[0].X, pts[0].Y, pl.Elevation);
                ln.EndPoint = new Point3d(pts[1].X, pts[1].Y, pl.Elevation);
                e = ln;
            }
            else
            {
                var p = (Polyline)e ?? new Polyline();
                if (!Igual(p, pts))
                {
                    while (p.NumberOfVertices > pts.Count) p.RemoveVertexAt(p.NumberOfVertices - 1);
                    for (int i = 0; i < pts.Count; i++)
                    {
                        if (i < p.NumberOfVertices) { p.SetPointAt(i, pts[i]); p.SetBulgeAt(i, 0); }
                        else p.AddVertexAt(i, pts[i], 0, 0, 0);
                    }
                    p.Elevation = pl.Elevation;
                }
                e = p;
            }
            if (e.LayerId != pl.LayerId) e.LayerId = pl.LayerId;
            if (e.Visible != d.Visivel) e.Visible = d.Visivel;
            if (e.ObjectId.IsNull)
            {
                if (!owner.IsWriteEnabled) owner.UpgradeOpen();
                owner.AppendEntity(e);
                tr.AddNewlyCreatedDBObject(e, true);
                XDataCad.Gravar(e, CodecDados.Codificar(new Vinculo { Tipo = TipoParte.Desenho, HandleBarra = hBarra }));
            }
            resultado.Add(XDataCad.Hex(e.ObjectId));
        }
        foreach (var e in existentes.Where(x => !usadas.Contains(x.ObjectId)))
        {
            if (!e.IsWriteEnabled) e.UpgradeOpen();
            e.Erase();
        }
        return resultado;
    }

    private static bool Igual(Polyline p, List<Point2d> pts)
    {
        if (p.ObjectId.IsNull || p.NumberOfVertices != pts.Count) return false;
        for (int i = 0; i < pts.Count; i++) if (p.GetPoint2dAt(i).GetDistanceTo(pts[i]) > 1e-9) return false;
        return true;
    }

    private static Entity Parte(Transaction tr, Database db, string handle, string hBarra)
    {
        var id = XDataCad.IdDoHandle(db, handle);
        if (id.IsNull || id.IsErased || !(tr.GetObject(id, OpenMode.ForRead) is Entity e)) return null;
        var v = XDataCad.LerVinculo(e);
        return v != null && v.Tipo == TipoParte.Desenho && string.Equals(v.HandleBarra, hBarra, StringComparison.OrdinalIgnoreCase) ? e : null;
    }

    /// <summary>Barra dona de uma parte do desenho, se o vínculo for recíproco.</summary>
    public static Polyline BarraDaParte(Transaction tr, Entity parte)
    {
        var v = XDataCad.LerVinculo(parte);
        if (v == null || v.Tipo != TipoParte.Desenho) return null;
        var id = XDataCad.IdDoHandle(parte.Database, v.HandleBarra);
        if (id.IsNull || id.IsErased || !(tr.GetObject(id, OpenMode.ForRead) is Polyline pl)) return null;
        var d = XDataCad.LerBarra(pl);
        string h = XDataCad.Hex(parte.ObjectId);
        return d != null && d.PartesDesenho.Any(x => string.Equals(x, h, StringComparison.OrdinalIgnoreCase)) ? pl : null;
    }

    public static void ApagarTodas(Transaction tr, Database db, DadosArmadura d, string hBarra)
    {
        foreach (var h in d.PartesDesenho)
        {
            var e = Parte(tr, db, h, hBarra);
            if (e != null && !e.IsErased) { e.UpgradeOpen(); e.Erase(); }
        }
    }
}
