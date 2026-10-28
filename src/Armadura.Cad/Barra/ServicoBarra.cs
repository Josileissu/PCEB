using System;
using System.Collections.Generic;
using System.Linq;
using Armadura.Cad.Infra;
using Armadura.Core.Calculo;
using Armadura.Core.Configuracao;
using Armadura.Core.Descricao;
using Armadura.Core.Modelo;
using Armadura.Core.Persistencia;
using Armadura.Core.Tabelas;

namespace Armadura.Cad.Barra;

/// <summary>
/// Mantém a barra coerente: lê a geometria, recalcula, e cria/atualiza/apaga a descrição e as cotas.
/// Idempotente: pode ser chamado quantas vezes for preciso.
/// </summary>
internal static class ServicoBarra
{
    private const double TolCm = 0.05;

    /// <summary>Transforma a polilinha em armadura (ou atualiza os dados de uma que já é).</summary>
    public static void Criar(Transaction tr, Polyline pl, DadosArmadura d, ConfiguracaoArmadura cfg)
    {
        var db = pl.Database;
        XDataCad.GarantirRegApp(db, tr);
        if (!pl.IsWriteEnabled) pl.UpgradeOpen();
        pl.LayerId = Desenho.GarantirLayer(db, tr, cfg.LayerBarra, cfg.CorBarra);
        d.AjustarSegmentos(GeometriaBarra.NumeroSegmentos(pl));
        if (d.Escala <= 0) d.Escala = cfg.Escala;
        for (int i = 0; i < d.Segmentos.Count; i++)
            if (GeometriaBarra.EhArco(pl, i)) d.Segmentos[i].Formato = FormatoSegmento.Curva;
        XDataCad.Gravar(pl, CodecDados.Codificar(d));
        Atualizar(tr, pl, cfg);
    }

    public static void Atualizar(Transaction tr, Polyline pl, ConfiguracaoArmadura cfg)
    {
        var d = XDataCad.LerBarra(pl);
        if (d == null || pl.IsErased) return;
        var db = pl.Database;
        double cpu = Desenho.CmPorUnidade(db, cfg);
        var geo = GeometriaBarra.Ler(pl, cpu);
        string hBarra = XDataCad.Hex(pl.ObjectId);

        // número de segmentos mudou (vértice inserido/removido): apaga as cotas que sobraram
        for (int i = geo.Count; i < d.Segmentos.Count; i++)
            Apagar(tr, Parte<DBText>(tr, db, d.Segmentos[i].HandleCota, TipoParte.Cota, hBarra));
        d.AjustarSegmentos(geo.Count);
        // painel de tela: cotas só nas dimensões (1º lado = largura, 2º = altura), se a opção estiver ligada
        // tela identificada: as dimensões vão no texto da diagonal, sem cotas nos lados
        if (d.EhTela) for (int i = 0; i < d.Segmentos.Count; i++) d.Segmentos[i].Cotar = d.TelaCotas && !d.TelaIdentificada && i < 2;

        for (int i = 0; i < geo.Count; i++)
        {
            var s = d.Segmentos[i];
            bool arco = geo[i].Tipo == TipoSegmento.Arco;
            if (arco) s.Formato = FormatoSegmento.Curva;
            else if (s.Formato == FormatoSegmento.Curva) s.Formato = FormatoSegmento.Linha;
            // gancho esticado por grip/stretch/extend volta a ser "Linha" (seção 2.1)
            else if (Segmento.EhGancho(s.Formato) && s.ComprimentoRegistrado > 0
                     && Math.Abs(geo[i].Comprimento - s.ComprimentoRegistrado) > TolCm)
                s.Formato = FormatoSegmento.Linha;
            s.ComprimentoRegistrado = geo[i].Comprimento;
        }

        // ganchos seguem a norma: bitola, aço ou "estribo" mudaram → o segmento muda de tamanho
        if (AjustarGanchos(pl, d, geo, cfg, cpu))
        {
            geo = GeometriaBarra.Ler(pl, cpu);
            for (int i = 0; i < geo.Count; i++) d.Segmentos[i].ComprimentoRegistrado = geo[i].Comprimento;
        }

        // ---- faixas: recalculam e, se alguma soma, a quantidade da barra é a soma delas ----
        if (d.TemFaixas)
        {
            var validas = new List<string>();
            int soma = 0;
            bool algumaSoma = false;
            foreach (var hf in d.Faixas.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var fid = XDataCad.IdDoHandle(db, hf);
                if (fid.IsNull || fid.IsErased || !(tr.GetObject(fid, OpenMode.ForRead) is Entity fe)) continue;
                var (valida, q) = ServicoFaixa.Atualizar(tr, fe, d, hBarra, pl.Elevation, cfg);
                if (!valida) continue;
                validas.Add(hf);
                soma += q;
                algumaSoma |= ServicoFaixa.Ler(fe).SomaQuantidade;
            }
            d.Faixas = validas;
            if (algumaSoma && !d.EhVarLista) d.Quantidade = soma;
        }
        // grupo VAR: a quantidade é o nº de barras da lista (as faixas só indicam a distribuição)
        if (d.EhVarLista) d.Quantidade = d.ComprimentosVar.Count;

        double comp = CalculoComprimento.ComprimentoUnitario(geo, cfg, d);
        double h = cfg.AlturaTexto(cpu, d.Escala);
        double afast = cfg.AfastamentoTexto(cpu, d.Escala);
        var owner = (BlockTableRecord)tr.GetObject(pl.OwnerId, OpenMode.ForRead);
        var layerTexto = Desenho.GarantirLayer(db, tr, cfg.LayerTexto, cfg.CorTexto);
        var origem = pl.NumberOfVertices > 0 ? pl.GetPoint2dAt(0) : Point2d.Origin;

        // ---- descrição ----
        string texto = FormatadorDescricao.Formatar(d, comp, cfg);
        if (d.EhTela && d.TelaIdentificada && geo.Count >= 2)
        {
            var (l1, l2) = FormatadorDescricao.TextoTela(d, geo[0].Comprimento, geo[1].Comprimento);
            texto = l1 + "\\P" + l2;
        }
        texto = Core.Descricao.QuebrasTexto.Aplicar(texto, d.QuebrasDescricao); // Enter dado pelo usuário
        var desc = Parte<MText>(tr, db, d.HandleDescricao, TipoParte.Descricao, hBarra);
        if (desc == null)
        {
            Point2d pos = d.TemPosicaoDescricao
                ? origem + new Vector2d(d.DescricaoDx, d.DescricaoDy)
                : PosicaoPadraoDescricao(pl, d, geo, h);
            desc = new MText
            {
                Location = new Point3d(pos.X, pos.Y, pl.Elevation),
                TextHeight = h,
                Attachment = AttachmentPoint.MiddleCenter,
                Contents = texto,
                LayerId = layerTexto,
            };
            AdicionarParte(tr, owner, desc, new Vinculo { Tipo = TipoParte.Descricao, HandleBarra = hBarra });
            d.HandleDescricao = XDataCad.Hex(desc.ObjectId);
        }
        else if (desc.Contents != texto || Math.Abs(desc.TextHeight - h) > 1e-9)
        {
            desc.UpgradeOpen();
            desc.Contents = texto;
            desc.TextHeight = h;
        }
        AjustarTextoDaTela(pl, d, desc, h);
        d.DescricaoDx = desc.Location.X - origem.X;
        d.DescricaoDy = desc.Location.Y - origem.Y;
        d.TemPosicaoDescricao = true;

        // ---- cotas dos segmentos ----
        var extensao = pl.GeometricExtents;
        var centroDesenho = new Point2d((extensao.MinPoint.X + extensao.MaxPoint.X) / 2, (extensao.MinPoint.Y + extensao.MaxPoint.Y) / 2);
        for (int i = 0; i < geo.Count; i++)
        {
            var s = d.Segmentos[i];
            var cota = Parte<DBText>(tr, db, s.HandleCota, TipoParte.Cota, hBarra);
            if (!s.Cotar || geo[i].Comprimento < TolCm)
            {
                Apagar(tr, cota);
                s.HandleCota = "";
                continue;
            }
            var (meio, dir) = GeometriaBarra.Meio(pl, i);
            // texto sempre legível: direção entre -90° e +90°
            if (dir.X < -1e-9 || (Math.Abs(dir.X) <= 1e-9 && dir.Y < 0)) dir = dir.Negate();
            var normal = dir.GetPerpendicularVector();
            // cota do lado de fora do desenho: se "acima do texto" aponta para dentro, o texto vai
            // para o outro lado, alinhado pelo topo (sem encostar na linha)
            // treliça: o desenho fica acima do banzo inferior, então a cota vai para baixo
            bool porCima = !d.EhTrelica && (centroDesenho - meio).DotProduct(normal) <= 1e-6;
            var p = meio + normal * (porCima ? afast : -afast);
            var vertical = porCima ? TextVerticalMode.TextBottom : TextVerticalMode.TextTop;
            var ponto = new Point3d(p.X, p.Y, pl.Elevation);
            string valor = CalculoComprimento.TextoCota(geo[i].Comprimento, s.Var, cfg);
            double rot = Math.Atan2(dir.Y, dir.X);

            if (cota == null)
            {
                cota = new DBText
                {
                    TextString = valor, Height = h, Rotation = rot, LayerId = layerTexto,
                    HorizontalMode = TextHorizontalMode.TextCenter, VerticalMode = vertical,
                    Position = ponto, AlignmentPoint = ponto, Visible = d.Visivel,
                };
                if (db.Textstyle.IsValid) cota.TextStyleId = db.Textstyle;
                AdicionarParte(tr, owner, cota, new Vinculo { Tipo = TipoParte.Cota, HandleBarra = hBarra, Indice = i });
                cota.AdjustAlignment(db);
                s.HandleCota = XDataCad.Hex(cota.ObjectId);
            }
            else
            {
                bool mudou = cota.TextString != valor || cota.AlignmentPoint.DistanceTo(ponto) > 1e-6
                             || Math.Abs(cota.Rotation - rot) > 1e-9 || Math.Abs(cota.Height - h) > 1e-9
                             || cota.Visible != d.Visivel || cota.VerticalMode != vertical;
                var vinc = XDataCad.LerVinculo(cota);
                if (mudou || vinc.Indice != i)
                {
                    cota.UpgradeOpen();
                    cota.VerticalMode = vertical;
                    cota.TextString = valor;
                    cota.Height = h;
                    cota.Rotation = rot;
                    cota.AlignmentPoint = ponto;
                    cota.Visible = d.Visivel;
                    cota.AdjustAlignment(db);
                    if (vinc.Indice != i)
                        XDataCad.Gravar(cota, CodecDados.Codificar(new Vinculo { Tipo = TipoParte.Cota, HandleBarra = hBarra, Indice = i }));
                }
            }
        }

        if (pl.Visible != d.Visivel) { if (!pl.IsWriteEnabled) pl.UpgradeOpen(); pl.Visible = d.Visivel; }
        if (d.Rebatidas.Count > 0) d.Rebatidas = ServicoRebatida.Atualizar(tr, pl, d, hBarra);
        if (!d.EhBarraComum || d.PartesDesenho.Count > 0) d.PartesDesenho = ServicoDesenhoGerado.Atualizar(tr, pl, d, hBarra, cfg);
        XDataCad.GravarSeMudou(pl, CodecDados.Codificar(d));
    }

    /// <summary>
    /// Tela com "texto na diagonal": a descrição fica no meio da diagonal (v0→v2), girada com ela (sempre
    /// legível) e um pouco acima da linha. Sem a opção, volta a ficar na horizontal.
    /// </summary>
    private static void AjustarTextoDaTela(Polyline pl, DadosArmadura d, MText desc, double h)
    {
        double rot = 0;
        Point3d? pos = null;
        if (d.EhTela && (d.TelaTextoDiagonal || d.TelaIdentificada) && pl.NumberOfVertices >= 4)
        {
            var a = pl.GetPoint2dAt(0);
            var b = pl.GetPoint2dAt(2);
            var dir = b - a;
            if (dir.Length > 1e-9)
            {
                if (dir.X < -1e-9 || (Math.Abs(dir.X) <= 1e-9 && dir.Y < 0)) dir = dir.Negate();
                rot = Math.Atan2(dir.Y, dir.X);
                // identificada: centrado SOBRE a diagonal (ela é interrompida no texto); senão, um pouco acima
                double acima = d.TelaIdentificada ? 0 : 0.8 * h;
                var meio = a + (b - a) * 0.5 + dir.GetNormal().GetPerpendicularVector() * acima;
                pos = new Point3d(meio.X, meio.Y, pl.Elevation);
            }
        }
        else if (!d.EhTela || Math.Abs(desc.Rotation) < 1e-12) return;
        bool mudou = Math.Abs(desc.Rotation - rot) > 1e-9 || (pos.HasValue && desc.Location.DistanceTo(pos.Value) > 1e-6);
        if (!mudou) return;
        if (!desc.IsWriteEnabled) desc.UpgradeOpen();
        desc.Rotation = rot;
        if (pos.HasValue) desc.Location = pos.Value;
    }

    private static bool AjustarGanchos(Polyline pl, DadosArmadura d, System.Collections.Generic.List<GeoSegmento> geo,
        ConfiguracaoArmadura cfg, double cpu)
    {
        bool mudou = false;
        bool cordoalha = Core.Tabelas.TabelaBarras.EhCordoalha(d.Aco);
        for (int i = 0; i < geo.Count; i++)
        {
            var f = d.Segmentos[i].Formato;
            if (!Segmento.EhGancho(f)) continue;
            if (cordoalha) { d.Segmentos[i].Formato = FormatoSegmento.Linha; continue; } // virou cordoalha: sem gancho
            double norma = cfg.Dobramento.ComprimentoGancho(f, d.Bitola, d.Aco, d.Estribo);
            if (Math.Abs(geo[i].Comprimento - norma) <= TolCm) continue;
            if (!pl.IsWriteEnabled) pl.UpgradeOpen();
            mudou |= GeometriaBarra.AlterarComprimento(pl, i, norma / cpu);
        }
        return mudou;
    }

    // ---------- edição de segmento (ARMEDITSEG e paleta) ----------

    /// <summary>Novo comprimento (cm) de um segmento reto. Retorna a mensagem de erro, ou null.</summary>
    public static string AlterarComprimentoSegmento(Polyline pl, DadosArmadura d, int i, double cm, double cpu)
    {
        if (i < 0 || i >= d.Segmentos.Count) return "Segmento inexistente.";
        if (GeometriaBarra.EhArco(pl, i)) return "O comprimento de um segmento em arco não é editável.";
        if (cm <= 0) return "O comprimento deve ser positivo.";
        if (!pl.IsWriteEnabled) pl.UpgradeOpen();
        if (!GeometriaBarra.AlterarComprimento(pl, i, cm / cpu)) return "Este segmento não pode ser alterado.";
        var s = d.Segmentos[i];
        if (Segmento.EhGancho(s.Formato)) s.Formato = FormatoSegmento.Linha; // comprimento manual: deixa de ser gancho
        s.ComprimentoRegistrado = cm;
        return null;
    }

    /// <summary>Muda o formato; o tamanho do gancho é aplicado por <see cref="Atualizar"/>. Retorna erro ou null.</summary>
    public static string AlterarFormatoSegmento(Polyline pl, DadosArmadura d, int i, FormatoSegmento f, double cpu)
    {
        if (i < 0 || i >= d.Segmentos.Count) return "Segmento inexistente.";
        bool arco = GeometriaBarra.EhArco(pl, i);
        if (arco && f != FormatoSegmento.Curva) return "Segmento em arco tem sempre o formato Curva.";
        if (!arco && f == FormatoSegmento.Curva) return "Curva é só para segmentos em arco.";
        if (Segmento.EhGancho(f) && pl.Closed) return "Barra fechada não tem ganchos.";
        if (Segmento.EhGancho(f) && Core.Tabelas.TabelaBarras.EhCordoalha(d.Aco)) return "Cordoalha de protensão não tem ganchos.";
        if (Segmento.EhGancho(f) && !d.EhBarraComum) return "Treliça e tela não têm ganchos.";
        d.Segmentos[i].Formato = f;
        // registra o comprimento atual para Atualizar não confundir com "gancho esticado"
        d.Segmentos[i].ComprimentoRegistrado = GeometriaBarra.Ler(pl, cpu)[i].Comprimento;
        return null;
    }

    /// <summary>Apaga a barra e todas as suas partes.</summary>
    public static void ApagarComPartes(Transaction tr, Polyline pl)
    {
        var d = XDataCad.LerBarra(pl);
        if (d == null) return;
        ApagarPartes(tr, pl.Database, d, XDataCad.Hex(pl.ObjectId));
        if (!pl.IsErased) { if (!pl.IsWriteEnabled) pl.UpgradeOpen(); pl.Erase(); }
    }

    public static void ApagarPartes(Transaction tr, Database db, DadosArmadura d, string hBarra)
    {
        Apagar(tr, Parte<MText>(tr, db, d.HandleDescricao, TipoParte.Descricao, hBarra));
        foreach (var s in d.Segmentos)
            Apagar(tr, Parte<DBText>(tr, db, s.HandleCota, TipoParte.Cota, hBarra));
        foreach (var hf in d.Faixas)
        {
            var fid = XDataCad.IdDoHandle(db, hf);
            if (fid.IsNull || fid.IsErased || !(tr.GetObject(fid, OpenMode.ForRead) is Entity fe)) continue;
            var f = ServicoFaixa.Ler(fe);
            if (f != null && string.Equals(f.HandleBarra, hBarra, StringComparison.OrdinalIgnoreCase))
                ServicoFaixa.Apagar(tr, fe);
        }
        ServicoRebatida.ApagarTodas(tr, db, d, hBarra);
        ServicoDesenhoGerado.ApagarTodas(tr, db, d, hBarra);
    }

    /// <summary>
    /// Barra a que uma entidade do plugin pertence: a própria barra, descrição/cota, faixa ou parte de
    /// faixa (círculo do corte, linha de chamada). Null se não pertencer a nenhuma.
    /// </summary>
    public static Polyline BarraDe(Transaction tr, Entity ent)
    {
        switch (XDataCad.Tipo(ent))
        {
            case TipoParte.Barra: return ent as Polyline;
            case TipoParte.Descricao:
            case TipoParte.Cota: return BarraDaParte(tr, ent);
            case TipoParte.Faixa: return ServicoFaixa.BarraDaFaixa(tr, ent);
            case TipoParte.Corte:
            case TipoParte.Chamada:
                var faixa = ServicoFaixa.FaixaDaParte(tr, ent);
                return faixa == null ? null : ServicoFaixa.BarraDaFaixa(tr, faixa);
            case TipoParte.Rebatida: return ServicoRebatida.BarraDaRebatida(tr, ent);
            case TipoParte.Desenho: return ServicoDesenhoGerado.BarraDaParte(tr, ent);
            default: return null;
        }
    }

    /// <summary>
    /// Abre a parte pelo handle só se ela for do tipo esperado e apontar de volta para a barra
    /// (protege contra handles de cópias e de outros desenhos).
    /// </summary>
    public static T Parte<T>(Transaction tr, Database db, string handle, TipoParte tipo, string hBarra) where T : Entity
    {
        var id = XDataCad.IdDoHandle(db, handle);
        if (id.IsNull || id.IsErased) return null;
        if (tr.GetObject(id, OpenMode.ForRead) is not T ent) return null;
        var v = XDataCad.LerVinculo(ent);
        return v != null && v.Tipo == tipo && string.Equals(v.HandleBarra, hBarra, StringComparison.OrdinalIgnoreCase)
            ? ent : null;
    }

    /// <summary>Barra dona de uma parte (descrição/cota), se o vínculo for recíproco.</summary>
    public static Polyline BarraDaParte(Transaction tr, Entity parte)
    {
        var v = XDataCad.LerVinculo(parte);
        if (v == null) return null;
        var id = XDataCad.IdDoHandle(parte.Database, v.HandleBarra);
        if (id.IsNull || id.IsErased) return null;
        if (tr.GetObject(id, OpenMode.ForRead) is not Polyline pl) return null;
        var d = XDataCad.LerBarra(pl);
        if (d == null) return null;
        string h = XDataCad.Hex(parte.ObjectId);
        bool reciproco = v.Tipo switch
        {
            TipoParte.Descricao => string.Equals(d.HandleDescricao, h, StringComparison.OrdinalIgnoreCase),
            TipoParte.Cota => v.Indice >= 0 && v.Indice < d.Segmentos.Count
                              && string.Equals(d.Segmentos[v.Indice].HandleCota, h, StringComparison.OrdinalIgnoreCase),
            _ => false,
        };
        return reciproco ? pl : null;
    }

    /// <summary>Entidade selecionada → barra (a própria, ou a dona da descrição/cota).</summary>
    public static Polyline ResolverBarra(Transaction tr, ObjectId id)
    {
        var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
        return ent == null ? null : BarraDe(tr, ent);
    }

    private static void AdicionarParte(Transaction tr, BlockTableRecord owner, Entity ent, Vinculo v)
    {
        if (!owner.IsWriteEnabled) owner.UpgradeOpen();
        owner.AppendEntity(ent);
        tr.AddNewlyCreatedDBObject(ent, true);
        XDataCad.Gravar(ent, CodecDados.Codificar(v));
    }

    private static void Apagar(Transaction tr, Entity e)
    {
        if (e == null || e.IsErased) return;
        if (!e.IsWriteEnabled) e.UpgradeOpen();
        e.Erase();
    }

    /// <summary>
    /// Posição inicial da descrição, sempre do lado de FORA do desenho da barra: estribos e barras
    /// fechadas → abaixo do desenho; demais → ao lado do maior segmento, afastada da cota.
    /// </summary>
    private static Point2d PosicaoPadraoDescricao(Polyline pl, DadosArmadura d, System.Collections.Generic.List<GeoSegmento> geo, double h)
    {
        if (geo.Count == 0) return pl.NumberOfVertices > 0 ? pl.GetPoint2dAt(0) : Point2d.Origin;
        if (d.EhTrelica && pl.NumberOfVertices >= 2)
        {
            // acima do banzo superior (a altura vem do catálogo)
            var cfgT = ConfigDesenho.ObterSemLer(pl.Database);
            double hTr = (cfgT.Trelicas.Trelica(d.Trelica)?.Altura ?? 0) / Desenho.CmPorUnidade(pl.Database, cfgT);
            var a = pl.GetPoint2dAt(0);
            var b = pl.GetPoint2dAt(1);
            var u = (b - a).GetNormal();
            return a + (b - a) * 0.5 + u.GetPerpendicularVector() * (hTr + 2.5 * h);
        }
        var ext = pl.GeometricExtents;
        var centro = new Point2d((ext.MinPoint.X + ext.MaxPoint.X) / 2, (ext.MinPoint.Y + ext.MaxPoint.Y) / 2);
        bool fechada = pl.Closed || pl.GetPoint2dAt(0).GetDistanceTo(pl.GetPoint2dAt(pl.NumberOfVertices - 1)) < 1e-6
                       || (d.Estribo && geo.Count >= 4);
        if (fechada) return new Point2d(centro.X, ext.MinPoint.Y - 2.5 * h);

        int maior = Enumerable.Range(0, geo.Count).OrderByDescending(i => geo[i].Comprimento).First();
        var (meio, dir) = GeometriaBarra.Meio(pl, maior);
        // maior segmento mais vertical que horizontal: o texto (horizontal) ao lado dele invadiria o
        // desenho → abaixo do desenho
        if (Math.Abs(dir.Y) > Math.Abs(dir.X)) return new Point2d(centro.X, ext.MinPoint.Y - 2.5 * h);
        if (dir.X < -1e-9 || (Math.Abs(dir.X) <= 1e-9 && dir.Y < 0)) dir = dir.Negate();
        var normal = dir.GetPerpendicularVector();
        // para fora: do lado oposto ao centro do desenho (em barra reta, fica acima, como antes)
        if ((centro - meio).DotProduct(normal) > 1e-6) normal = normal.Negate();
        return meio + normal * (3.5 * h);
    }
}
