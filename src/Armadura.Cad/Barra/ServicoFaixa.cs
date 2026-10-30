using System;
using System.Collections.Generic;
using System.Linq;
using Armadura.Cad.Infra;
using Armadura.Core.Configuracao;
using Armadura.Core.Modelo;
using Armadura.Core.Persistencia;

namespace Armadura.Cad.Barra;

/// <summary>
/// Faixas de armadura (seção 2.3). A entidade principal é uma cota nativa (faixas com comprimento)
/// ou um MText (chamadas); as partes são os círculos do corte e as linhas de chamada.
/// Tudo é regenerado a partir dos dados da faixa, de forma idempotente.
/// </summary>
internal static class ServicoFaixa
{
    // ---------- criação ----------

    /// <summary>Cria a faixa, liga à barra e já calcula quantidade e partes. Retorna a entidade principal.</summary>
    public static ObjectId Criar(Transaction tr, Polyline barra, DadosFaixa f, Point3d posicao, ConfiguracaoArmadura cfg)
    {
        var db = barra.Database;
        XDataCad.GarantirRegApp(db, tr);
        var d = XDataCad.LerBarra(barra);
        string hBarra = XDataCad.Hex(barra.ObjectId);
        f.HandleBarra = hBarra;
        double h = cfg.AlturaTexto(Desenho.CmPorUnidade(db, cfg), f.EscalaEfetiva(d.Escala));
        var layer = Desenho.GarantirLayer(db, tr, cfg.LayerFaixa, cfg.CorFaixa);

        Entity principal;
        var p1 = new Point3d(f.P1X, f.P1Y, barra.Elevation);
        var p2 = new Point3d(f.P2X, f.P2Y, barra.Elevation);
        switch (f.Tipo)
        {
            case TipoFaixa.Ortogonal:
            case TipoFaixa.CorteCotaOrtogonal:
            {
                // como o DIMLINEAR: horizontal ou vertical conforme o lado em que a posição foi clicada
                double dx = Math.Abs(p2.X - p1.X), dy = Math.Abs(p2.Y - p1.Y);
                bool horizontal = Math.Abs(posicao.Y - (p1.Y + p2.Y) / 2) * Math.Max(dx, 1e-9)
                                  >= Math.Abs(posicao.X - (p1.X + p2.X) / 2) * Math.Max(dy, 1e-9);
                if (dx < 1e-9) horizontal = false;
                if (dy < 1e-9) horizontal = true;
                principal = new RotatedDimension(horizontal ? 0 : Math.PI / 2, p1, p2, posicao, "", db.Dimstyle);
                break;
            }
            case TipoFaixa.Alinhada:
            case TipoFaixa.CorteCotaAlinhada:
                principal = new AlignedDimension(p1, p2, posicao, "", db.Dimstyle);
                break;
            default:
                principal = new MText
                {
                    Location = posicao, TextHeight = h, Attachment = AttachmentPoint.MiddleLeft, Contents = "",
                };
                break;
        }
        principal.SetDatabaseDefaults(db);
        principal.LayerId = layer;
        var esp = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
        esp.AppendEntity(principal);
        tr.AddNewlyCreatedDBObject(principal, true);
        XDataCad.Gravar(principal, f);

        if (!barra.IsWriteEnabled) barra.UpgradeOpen();
        d.Faixas.Add(XDataCad.Hex(principal.ObjectId));
        // a indicação só aponta a barra (não distribui): não passa espaçamento para ela
        if (d.Espacamento <= 0 && f.Espacamento > 0 && f.Tipo != TipoFaixa.Indicacao) d.Espacamento = f.Espacamento;
        XDataCad.Gravar(barra, d);
        ServicoBarra.Atualizar(tr, barra, cfg);
        return principal.ObjectId;
    }

    // ---------- atualização ----------

    /// <summary>
    /// Recalcula a faixa (quantidade, texto, partes). Retorna se ela ainda pertence à barra e a
    /// quantidade que soma na barra.
    /// </summary>
    public static (bool valida, int soma) Atualizar(Transaction tr, Entity ent, DadosArmadura barra, string hBarra,
        double elevacao, ConfiguracaoArmadura cfg)
    {
        var f = Ler(ent);
        if (f == null || ent.IsErased || !string.Equals(f.HandleBarra, hBarra, StringComparison.OrdinalIgnoreCase))
            return (false, 0);

        var db = ent.Database;
        double cpu = Desenho.CmPorUnidade(db, cfg);
        double escala = f.EscalaEfetiva(barra.Escala); // a faixa pode ter escala própria (seção ≠ elevação)
        double h = cfg.AlturaTexto(cpu, escala);
        string hFaixa = XDataCad.Hex(ent.ObjectId);

        // o conjunto foi movido inteiro (EXPLODE do detalhamento, edição do bloco no lugar, MOVE de tudo)?
        // Então os pontos guardados andam junto. Texto movido sozinho (as linhas ficaram) não desloca nada.
        if (!(ent is Dimension) && DeslocamentoDoConjunto(tr, db, ent, f, hFaixa) is Vector2d mov)
            f.Deslocar(mov.X, mov.Y);

        // geometria da linha base (unidades do desenho)
        Point2d a, b;
        double comprimentoCm;
        bool arco = f.Tipo == TipoFaixa.CorteChamadaArco;
        if (ent is Dimension dim)
        {
            (a, b) = LinhaBase(dim);
            comprimentoCm = a.GetDistanceTo(b) * cpu;
            f.P1X = a.X; f.P1Y = a.Y; f.P2X = b.X; f.P2Y = b.Y;
        }
        else
        {
            a = new Point2d(f.P1X, f.P1Y);
            b = new Point2d(f.P2X, f.P2Y);
            comprimentoCm = arco ? f.Raio * Math.Abs(f.AnguloFim - f.AnguloIni) * cpu : a.GetDistanceTo(b) * cpu;
        }
        bool fechado = arco && Math.Abs(Math.Abs(f.AnguloFim - f.AnguloIni) - 2 * Math.PI) < 1e-6;

        if (f.Tipo == TipoFaixa.Indicacao) f.Quantidade = 0;
        else if (!f.QuantidadeDigitada)
            f.Quantidade = CalculoFaixa.Quantidade(comprimentoCm, f.Espacamento, f.EspacamentoInicial, cfg.ModoQuantidadeFaixa, fechado);

        // texto
        string texto = Core.Descricao.QuebrasTexto.Aplicar(TextoFaixa.Formatar(f, barra, cfg), f.Quebras); // Enter do usuário
        if (ent is Dimension d2)
        {
            bool mudou = d2.DimensionText != texto || Math.Abs(d2.Dimtxt - h) > 1e-9;
            if (mudou)
            {
                if (!d2.IsWriteEnabled) d2.UpgradeOpen();
                d2.DimensionText = texto;
                AplicarEstiloCota(d2, h);
            }
        }
        else if (ent is MText mt && (mt.Contents != texto || Math.Abs(mt.TextHeight - h) > 1e-9))
        {
            if (!mt.IsWriteEnabled) mt.UpgradeOpen();
            mt.Contents = texto;
            mt.TextHeight = h;
        }

        // partes
        var owner = (BlockTableRecord)tr.GetObject(ent.OwnerId, OpenMode.ForRead);
        var existentes = f.Partes.Select(p => Parte(tr, db, p, hFaixa)).Where(e => e != null).ToList();
        var novas = new List<Entity>();
        var usadas = new HashSet<ObjectId>();

        // círculos do corte
        var centros = new List<Point2d>();
        if (f.TemCorte && f.Quantidade > 0)
        {
            double r = barra.Bitola / 20.0 * cfg.AmpliacaoCorte / cpu; // raio (unid.)
            double afast = cfg.AfastamentoCorteMm / 10.0 * cfg.EscalaEfetiva(escala) / cpu + r;
            if (arco)
            {
                double lado = f.LadoCorte; // +1 externo, −1 interno
                double raioCirc = f.Raio + lado * afast;
                double total = f.AnguloFim - f.AnguloIni;
                var pos = CalculoFaixa.Posicoes(Math.Abs(total) * f.Raio * cpu, f.Quantidade, f.Espacamento, f.EspacamentoInicial, fechado);
                foreach (var s in pos)
                {
                    double ang = f.AnguloIni + Math.Sign(total) * (s / cpu) / f.Raio;
                    centros.Add(new Point2d(f.CX + raioCirc * Math.Cos(ang), f.CY + raioCirc * Math.Sin(ang)));
                }
            }
            else if (a.GetDistanceTo(b) > 1e-9)
            {
                var u = (b - a).GetNormal();
                var n = u.GetPerpendicularVector() * f.LadoCorte;
                foreach (var s in CalculoFaixa.Posicoes(comprimentoCm, f.Quantidade, f.Espacamento, f.EspacamentoInicial))
                    centros.Add(a + u * (s / cpu) + n * afast);
            }
            var layerCorte = Desenho.GarantirLayer(db, tr, cfg.LayerCorte, cfg.CorCorte);
            foreach (var c in centros)
            {
                var e = Reaproveitar(existentes, usadas, cfg.CortePreenchido ? typeof(Polyline) : typeof(Circle));
                if (cfg.CortePreenchido) e = Disco((Polyline)e, c, r, elevacao);
                else e = Circulo((Circle)e, c, r, elevacao);
                if (e.LayerId != layerCorte) { if (!e.ObjectId.IsNull && !e.IsWriteEnabled) e.UpgradeOpen(); e.LayerId = layerCorte; }
                novas.Add(e);
            }
        }

        // linhas de chamada
        // linhas de chamada: a opção "ocultar" apaga as existentes; as apagadas pelo usuário não voltam
        var linhas = new List<(Point2d de, Point2d para, int indice)>();
        if (ent is MText texto2 && !f.OcultarLinhas)
        {
            var alvoTexto = new Point2d(texto2.Location.X, texto2.Location.Y);
            if (f.Tipo == TipoFaixa.Chamada || f.Tipo == TipoFaixa.Indicacao)
                linhas.AddRange(f.Referencias.Select((rf, i) => (new Point2d(rf.X, rf.Y), alvoTexto, i)));
            else if (centros.Count > 0)
            {
                var alvo = f.Tipo == TipoFaixa.CorteChamadaArco ? centros[centros.Count / 2]
                    : f.PontoChamada == 0 ? centros[0] : f.PontoChamada == 2 ? centros[centros.Count - 1] : centros[centros.Count / 2];
                linhas.Add((alvo, alvoTexto, 0));
            }
            linhas.RemoveAll(l => !f.MostraLinha(l.indice));
        }
        // índice da referência de cada parte nova (alinhado com "novas"; os círculos ficam com 0).
        // Lista e não dicionário: entidades ainda não gravadas têm ObjectId nulo e se confundiriam
        var indices = Enumerable.Repeat(0, novas.Count).ToList();
        foreach (var (de, para, indice) in linhas)
        {
            var e = (Line)Reaproveitar(existentes, usadas, typeof(Line)) ?? new Line();
            bool nova = e.ObjectId.IsNull;
            var p0 = new Point3d(de.X, de.Y, elevacao);
            var p1 = new Point3d(para.X, para.Y, elevacao);
            if (nova || e.StartPoint.DistanceTo(p0) > 1e-9 || e.EndPoint.DistanceTo(p1) > 1e-9 || e.LayerId != ent.LayerId)
            {
                if (!nova && !e.IsWriteEnabled) e.UpgradeOpen();
                e.StartPoint = p0;
                e.EndPoint = p1;
                e.LayerId = ent.LayerId;
            }
            novas.Add(e);
            indices.Add(indice);
        }

        // grava as novas, apaga as sobras; cada linha guarda o índice da sua referência
        // (para saber qual não recriar quando o usuário a apagar)
        for (int j = 0; j < novas.Count; j++)
        {
            var e = novas[j];
            int idx = indices[j];
            if (e.ObjectId.IsNull)
            {
                if (!owner.IsWriteEnabled) owner.UpgradeOpen();
                owner.AppendEntity(e);
                tr.AddNewlyCreatedDBObject(e, true);
            }
            else if (!(e is Line) || XDataCad.LerVinculo(e)?.Indice == idx) continue;
            if (!e.IsWriteEnabled) e.UpgradeOpen();
            XDataCad.Gravar(e, new Vinculo
            {
                Tipo = e is Line ? TipoParte.Chamada : TipoParte.Corte, HandleBarra = hFaixa, Indice = idx,
            });
        }
        foreach (var e in existentes.Where(e => !usadas.Contains(e.ObjectId)))
        {
            if (!e.IsWriteEnabled) e.UpgradeOpen();
            e.Erase();
        }
        f.Partes = novas.Select(e => XDataCad.Hex(e.ObjectId)).ToList();
        if (ent is MText mtA) { f.TemAncora = true; f.AncoraX = mtA.Location.X; f.AncoraY = mtA.Location.Y; }
        if (centros.Count > 0) { f.TemCirculo0 = true; f.Circulo0X = centros[0].X; f.Circulo0Y = centros[0].Y; }
        else f.TemCirculo0 = false;
        XDataCad.GravarSeMudou(ent, f);
        return (true, f.QuantidadeTotal);
    }

    /// <summary>
    /// Deslocamento do conjunto desde a última atualização, ou null. Conjunto movido = o texto saiu da âncora E as
    /// linhas de chamada continuam presas a ele (ou o 1º círculo do corte andou o mesmo tanto).
    /// </summary>
    private static Vector2d? DeslocamentoDoConjunto(Transaction tr, Database db, Entity ent, DadosFaixa f, string hFaixa)
    {
        if (!f.TemAncora || !(ent is MText mt)) return null;
        var loc = new Point2d(mt.Location.X, mt.Location.Y);
        var d = loc - new Point2d(f.AncoraX, f.AncoraY);
        double tol = Math.Max(1e-6, 1e-3 * mt.TextHeight);
        if (d.Length <= tol) return null;
        foreach (var p in f.Partes.Select(h => Parte(tr, db, h, hFaixa)).Where(e => e != null))
        {
            if (p is Line ln && new Point2d(ln.EndPoint.X, ln.EndPoint.Y).GetDistanceTo(loc) <= tol) return d;
            if (f.TemCirculo0)
            {
                Point2d? c = p is Circle ci ? new Point2d(ci.Center.X, ci.Center.Y)
                    : p is Polyline pl && pl.NumberOfVertices == 2 ? pl.GetPoint2dAt(0) + (pl.GetPoint2dAt(1) - pl.GetPoint2dAt(0)) * 0.5 : (Point2d?)null;
                if (c.HasValue && (c.Value - new Point2d(f.Circulo0X, f.Circulo0Y) - d).Length <= tol) return d;
            }
        }
        return null;
    }

    /// <summary>Linha base de uma cota: do 1º ponto, no sentido medido, com o comprimento medido.</summary>
    private static (Point2d, Point2d) LinhaBase(Dimension dim)
    {
        if (dim is RotatedDimension rd)
        {
            var p1 = new Point2d(rd.XLine1Point.X, rd.XLine1Point.Y);
            var p2 = new Point2d(rd.XLine2Point.X, rd.XLine2Point.Y);
            var u = new Vector2d(Math.Cos(rd.Rotation), Math.Sin(rd.Rotation));
            double t = (p2 - p1).DotProduct(u);
            return (p1, p1 + u * t);
        }
        if (dim is AlignedDimension ad)
            return (new Point2d(ad.XLine1Point.X, ad.XLine1Point.Y), new Point2d(ad.XLine2Point.X, ad.XLine2Point.Y));
        return (Point2d.Origin, Point2d.Origin);
    }

    /// <summary>Tamanhos da cota proporcionais ao texto da escala da barra (traço oblíquo de estrutura).</summary>
    private static void AplicarEstiloCota(Dimension d, double h)
    {
        d.Dimscale = 1;
        d.Dimtxt = h;
        d.Dimasz = h;
        d.Dimtsz = 0.5 * h;
        d.Dimexe = 0.5 * h;
        d.Dimexo = 0.5 * h;
        d.Dimgap = 0.3 * h;
        d.Dimtih = false;
        d.Dimtoh = false;
    }

    private static Entity Reaproveitar(List<Entity> existentes, HashSet<ObjectId> usadas, Type tipo)
    {
        var e = existentes.FirstOrDefault(x => x.GetType() == tipo && !usadas.Contains(x.ObjectId));
        if (e != null) usadas.Add(e.ObjectId);
        return e;
    }

    /// <summary>Disco cheio (como o DONUT): polilinha fechada de 2 arcos com largura = raio.</summary>
    private static Polyline Disco(Polyline pl, Point2d c, double r, double z)
    {
        var p0 = new Point2d(c.X - r / 2, c.Y);
        var p1 = new Point2d(c.X + r / 2, c.Y);
        if (pl == null)
        {
            pl = new Polyline();
            pl.AddVertexAt(0, p0, 1, r, r);
            pl.AddVertexAt(1, p1, 1, r, r);
            pl.Closed = true;
            pl.Elevation = z;
            return pl;
        }
        if (pl.GetPoint2dAt(0).GetDistanceTo(p0) > 1e-9 || Math.Abs(pl.ConstantWidth - r) > 1e-9)
        {
            if (!pl.IsWriteEnabled) pl.UpgradeOpen();
            pl.SetPointAt(0, p0);
            pl.SetPointAt(1, p1);
            pl.ConstantWidth = r;
            pl.Elevation = z;
        }
        return pl;
    }

    private static Circle Circulo(Circle c, Point2d centro, double r, double z)
    {
        var p = new Point3d(centro.X, centro.Y, z);
        if (c == null) return new Circle(p, Vector3d.ZAxis, r);
        if (c.Center.DistanceTo(p) > 1e-9 || Math.Abs(c.Radius - r) > 1e-9)
        {
            if (!c.IsWriteEnabled) c.UpgradeOpen();
            c.Center = p;
            c.Radius = r;
        }
        return c;
    }

    // ---------- leitura e vínculos ----------

    /// <summary>
    /// Aplica a transformação aos pontos guardados no XData da faixa (linha base, arco e referências das
    /// chamadas), para acompanhar a entidade quando ela é copiada/deslocada para dentro de um bloco.
    /// </summary>
    public static void Transformar(Entity faixa, Matrix3d m)
    {
        var f = Ler(faixa);
        if (f == null) return;
        Point2d T(double x, double y) { var p = new Point3d(x, y, 0).TransformBy(m); return new Point2d(p.X, p.Y); }
        var p1 = T(f.P1X, f.P1Y); f.P1X = p1.X; f.P1Y = p1.Y;
        var p2 = T(f.P2X, f.P2Y); f.P2X = p2.X; f.P2Y = p2.Y;
        if (f.Raio > 0) { var c = T(f.CX, f.CY); f.CX = c.X; f.CY = c.Y; }
        f.Referencias = f.Referencias.Select(r => { var q = T(r.X, r.Y); return (q.X, q.Y); }).ToList();
        if (f.TemAncora) { var q = T(f.AncoraX, f.AncoraY); f.AncoraX = q.X; f.AncoraY = q.Y; }
        if (f.TemCirculo0) { var q = T(f.Circulo0X, f.Circulo0Y); f.Circulo0X = q.X; f.Circulo0Y = q.Y; }
        XDataCad.Gravar(faixa, f);
    }

    public static DadosFaixa Ler(Entity ent)
    {
        var c = XDataCad.Ler(ent);
        return c == null ? null : CodecDados.DecodificarFaixa(c);
    }

    /// <summary>Parte (círculo/linha) válida: existe e aponta de volta para a faixa.</summary>
    private static Entity Parte(Transaction tr, Database db, string handle, string hFaixa)
    {
        var id = XDataCad.IdDoHandle(db, handle);
        if (id.IsNull || id.IsErased) return null;
        if (!(tr.GetObject(id, OpenMode.ForRead) is Entity e)) return null;
        var v = XDataCad.LerVinculo(e);
        return v != null && (v.Tipo == TipoParte.Corte || v.Tipo == TipoParte.Chamada)
               && string.Equals(v.HandleBarra, hFaixa, StringComparison.OrdinalIgnoreCase) ? e : null;
    }

    /// <summary>Barra da faixa, se a barra listar esta faixa.</summary>
    public static Polyline BarraDaFaixa(Transaction tr, Entity faixa)
    {
        var pl = BarraApontada(tr, faixa);
        return pl != null && XDataCad.Lista(XDataCad.LerBarra(pl).Faixas, faixa.ObjectId) ? pl : null;
    }

    /// <summary>A barra existe e é armadura (mesmo que ainda não liste a faixa — caso das cópias).</summary>
    public static Polyline BarraApontada(Transaction tr, Entity faixa)
    {
        var f = Ler(faixa);
        return f == null ? null : XDataCad.BarraDoHandle(tr, faixa.Database, f.HandleBarra);
    }

    /// <summary>Faixa dona de uma parte (círculo/linha), se ela listar a parte.</summary>
    public static Entity FaixaDaParte(Transaction tr, Entity parte)
    {
        var v = XDataCad.LerVinculo(parte);
        if (v == null || (v.Tipo != TipoParte.Corte && v.Tipo != TipoParte.Chamada)) return null;
        var id = XDataCad.IdDoHandle(parte.Database, v.HandleBarra);
        if (id.IsNull || id.IsErased || !(tr.GetObject(id, OpenMode.ForRead) is Entity faixa)) return null;
        var f = Ler(faixa);
        string h = XDataCad.Hex(parte.ObjectId);
        return f != null && f.Partes.Any(x => string.Equals(x, h, StringComparison.OrdinalIgnoreCase)) ? faixa : null;
    }

    /// <summary>Apaga a faixa e as suas partes.</summary>
    public static void Apagar(Transaction tr, Entity faixa)
    {
        var f = Ler(faixa);
        if (f == null) return;
        string hFaixa = XDataCad.Hex(faixa.ObjectId);
        foreach (var p in f.Partes)
        {
            var e = Parte(tr, faixa.Database, p, hFaixa);
            if (e != null) { if (!e.IsWriteEnabled) e.UpgradeOpen(); e.Erase(); }
        }
        if (!faixa.IsErased) { if (!faixa.IsWriteEnabled) faixa.UpgradeOpen(); faixa.Erase(); }
    }

    /// <summary>Apaga só as partes (a faixa já foi apagada pelo usuário).</summary>
    public static void ApagarPartes(Transaction tr, Entity faixaApagada)
    {
        var f = Ler(faixaApagada);
        if (f == null) return;
        string hFaixa = XDataCad.Hex(faixaApagada.ObjectId);
        foreach (var p in f.Partes)
        {
            var e = Parte(tr, faixaApagada.Database, p, hFaixa);
            if (e != null) { if (!e.IsWriteEnabled) e.UpgradeOpen(); e.Erase(); }
        }
    }
}
