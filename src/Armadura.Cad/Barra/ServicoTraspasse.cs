using System;
using System.Collections.Generic;
using System.Linq;
using Armadura.Cad.Infra;
using Armadura.Core.Configuracao;
using Armadura.Core.Modelo;
using Armadura.Core.Normas;
using Armadura.Core.Persistencia;
using Armadura.Core.Tabelas;

namespace Armadura.Cad.Barra;

/// <summary>
/// Dividir barra com traspasse (seção 3.3): duas barras independentes sobrepostas no comprimento de
/// traspasse, centrado no ponto escolhido. A 2ª peça é afastada um pouco (só no desenho) para as duas
/// aparecerem.
/// </summary>
internal static class ServicoTraspasse
{
    public static double Traspasse(DadosArmadura d, ConfiguracaoArmadura cfg) =>
        cfg.Traspasse.Comprimento(d.Bitola, d.Aco, cfg.Barras);

    public static double Comercial(DadosArmadura d, ConfiguracaoArmadura cfg)
    {
        var b = cfg.Barras.Obter(d.Bitola, d.Aco);
        if (b == null) return 1200;
        return b.Fabricacao == Fabricacao.Rolo ? 0 : (b.ComprimentoComercial > 0 ? b.ComprimentoComercial : 1200);
    }

    /// <summary>Ponto (e índice do segmento) a uma distância (cm) do início da barra.</summary>
    private static (int seg, Point2d p) PontoNaDistancia(Polyline pl, IReadOnlyList<GeoSegmento> geo, double cm, double cpu)
    {
        double o = 0;
        for (int i = 0; i < geo.Count; i++)
        {
            if (cm <= o + geo[i].Comprimento + 1e-9)
            {
                var a = pl.GetPoint2dAt(i);
                var b = pl.GetPoint2dAt((i + 1) % pl.NumberOfVertices);
                return (i, a + (b - a).GetNormal() * ((cm - o) / cpu));
            }
            o += geo[i].Comprimento;
        }
        return (geo.Count - 1, pl.GetPoint2dAt(pl.NumberOfVertices - 1));
    }

    /// <summary>
    /// Divide a barra com o centro do traspasse em <paramref name="centroCm"/> (já validado). Retorna
    /// (1ª peça, 2ª peça). A barra original e as partes dela são apagadas; as faixas vão para a 1ª peça.
    /// </summary>
    /// <param name="sentido">+1 afasta a 2ª peça para a esquerda; −1 para a direita (a divisão automática
    /// alterna, para as peças não irem se afastando em escada)</param>
    public static (ObjectId, ObjectId) Dividir(Transaction tr, Polyline pl, double centroCm, ConfiguracaoArmadura cfg, int sentido = 1)
    {
        var db = pl.Database;
        double cpu = Desenho.CmPorUnidade(db, cfg);
        var d = XDataCad.LerBarra(pl);
        if (pl.Closed) throw new InvalidOperationException("Barra fechada (estribo) não é dividida.");
        var geo = GeometriaBarra.Ler(pl, cpu);
        d.AjustarSegmentos(geo.Count);
        double l0 = Traspasse(d, cfg);
        var (sA, pA) = PontoNaDistancia(pl, geo, centroCm + l0 / 2, cpu); // fim da 1ª peça
        var (sB, pB) = PontoNaDistancia(pl, geo, centroCm - l0 / 2, cpu); // início da 2ª peça
        if (sA != sB || geo[sA].Tipo != TipoSegmento.Reta)
            throw new InvalidOperationException("O traspasse precisa caber num trecho reto da barra.");

        Polyline Peca(int de, Point2d? inicio, int ate, Point2d? fim)
        {
            var p = new Polyline { Elevation = pl.Elevation, Normal = pl.Normal };
            p.SetPropertiesFrom(pl);
            int k = 0;
            if (inicio.HasValue) p.AddVertexAt(k++, inicio.Value, 0, 0, 0);
            for (int v = de; v <= ate; v++)
                p.AddVertexAt(k++, pl.GetPoint2dAt(v), v < ate || !fim.HasValue ? pl.GetBulgeAt(v) : 0, 0, 0);
            if (fim.HasValue) p.AddVertexAt(k, fim.Value, 0, 0, 0);
            return p;
        }
        // 1ª: vértices 0..sA + ponto final; 2ª: ponto inicial + vértices sB+1..fim
        var a = Peca(0, null, sA, pA);
        a.SetBulgeAt(a.NumberOfVertices - 1, 0);
        var b = Peca(sB + 1, pB, pl.NumberOfVertices - 1, null);

        // afasta a 2ª peça para o lado, só para as duas aparecerem no desenho
        var dir = (pl.GetPoint2dAt((sA + 1) % pl.NumberOfVertices) - pl.GetPoint2dAt(sA)).GetNormal();
        double afast = cfg.Traspasse.AfastamentoDesenhoMm / 10.0 * cfg.EscalaEfetiva(d.Escala) / cpu;
        var n = dir.GetPerpendicularVector() * (afast * Math.Sign(sentido == 0 ? 1 : sentido));
        b.TransformBy(Matrix3d.Displacement(new Vector3d(n.X, n.Y, 0)));

        var owner = (BlockTableRecord)tr.GetObject(pl.OwnerId, OpenMode.ForWrite);
        DadosArmadura Dados(int primeiro, int quantos, bool cortaInicio, bool cortaFim, bool ficaComFaixas)
        {
            var x = d.Clonar();
            x.Id = Guid.NewGuid();
            x.Numero = 0;
            x.HandleDescricao = "";
            x.TemPosicaoDescricao = ficaComFaixas && d.TemPosicaoDescricao; // a 1ª peça mantém a descrição no lugar
            x.Rebatidas.Clear();
            if (!ficaComFaixas) x.Faixas.Clear();
            x.AssinaturaBloqueio = "";
            x.Segmentos = d.Segmentos.Skip(primeiro).Take(quantos).Select(s => { var c = s.Clonar(); c.HandleCota = ""; return c; }).ToList();
            // o segmento cortado deixa de ser gancho (agora é ponta de barra)
            if (cortaInicio && x.Segmentos.Count > 0) x.Segmentos[0].Formato = FormatoSegmento.Linha;
            if (cortaFim && x.Segmentos.Count > 0) x.Segmentos[x.Segmentos.Count - 1].Formato = FormatoSegmento.Linha;
            foreach (var s in x.Segmentos) s.ComprimentoRegistrado = 0;
            return x;
        }
        var dA = Dados(0, sA + 1, false, true, true);
        var dB = Dados(sB, geo.Count - sB, true, false, false);

        owner.AppendEntity(a);
        tr.AddNewlyCreatedDBObject(a, true);
        owner.AppendEntity(b);
        tr.AddNewlyCreatedDBObject(b, true);
        string hA = XDataCad.Hex(a.ObjectId);

        // faixas passam para a 1ª peça; a 2ª fica com a quantidade fixa (= soma atual)
        foreach (var hf in d.Faixas)
        {
            var fid = XDataCad.IdDoHandle(db, hf);
            if (fid.IsNull || fid.IsErased || !(tr.GetObject(fid, OpenMode.ForWrite) is Entity fe)) continue;
            var f = ServicoFaixa.Ler(fe);
            if (f == null) continue;
            f.HandleBarra = hA;
            XDataCad.Gravar(fe, CodecDados.Codificar(f));
        }

        XDataCad.Gravar(a, CodecDados.Codificar(dA));
        XDataCad.Gravar(b, CodecDados.Codificar(dB));

        // apaga a original e as partes dela (descrição, cotas, rebatidas) — as faixas já mudaram de dono
        var semFaixas = d.Clonar();
        semFaixas.Faixas.Clear();
        ServicoBarra.ApagarPartes(tr, db, semFaixas, XDataCad.Hex(pl.ObjectId));
        if (!pl.IsWriteEnabled) pl.UpgradeOpen();
        pl.Erase();

        ServicoBarra.Atualizar(tr, a, cfg);
        ServicoBarra.Atualizar(tr, b, cfg);
        return (a.ObjectId, b.ObjectId);
    }

    /// <summary>
    /// Divide pelo comprimento comercial (seção 3.3, Enter sem ponto). Retorna as peças (a própria
    /// barra se já cabe) ou null se não houver ponto válido.
    /// </summary>
    public static List<ObjectId> DividirAutomatico(Transaction tr, Polyline pl, ConfiguracaoArmadura cfg)
    {
        double cpu = Desenho.CmPorUnidade(pl.Database, cfg);
        var d = XDataCad.LerBarra(pl);
        if (d == null || TabelaBarras.EhCordoalha(d.Aco)) return new List<ObjectId> { pl.ObjectId };
        double comercial = Comercial(d, cfg);
        var geo = GeometriaBarra.Ler(pl, cpu);
        double l0 = Traspasse(d, cfg);
        var cortes = PlanoDivisao.Automatica(geo, l0, comercial);
        if (cortes == null) return null;
        var pecas = new List<ObjectId>();
        var atual = pl;
        double deslocamento = 0; // os cortes são na barra original; cada nova peça começa em (corte − l0/2)
        int sentido = 1;         // peças alternadas: linha original, afastada, linha original…
        foreach (var c in cortes)
        {
            var (a, b) = Dividir(tr, atual, c - deslocamento, cfg, sentido);
            sentido = -sentido;
            pecas.Add(a);
            deslocamento = c - l0 / 2;
            atual = (Polyline)tr.GetObject(b, OpenMode.ForRead);
        }
        pecas.Add(atual.ObjectId);
        return pecas;
    }

    /// <summary>"Verificação de comprimento limite": divide se passar do comercial. Retorna se dividiu.</summary>
    public static bool VerificarLimite(Transaction tr, Polyline pl, ConfiguracaoArmadura cfg)
    {
        if (!cfg.Traspasse.VerificarComprimentoLimite || pl.IsErased) return false;
        var d = XDataCad.LerBarra(pl);
        if (d == null || TabelaBarras.EhCordoalha(d.Aco)) return false;
        double comercial = Comercial(d, cfg);
        if (comercial <= 0) return false;
        double total = GeometriaBarra.Ler(pl, Desenho.CmPorUnidade(pl.Database, cfg)).Sum(s => s.Comprimento);
        if (total <= comercial + 1e-6) return false;
        var r = DividirAutomatico(tr, pl, cfg);
        if (r == null) Log.Info($"Barra {XDataCad.Hex(pl.ObjectId)} passa do comprimento comercial mas não tem trecho reto para o traspasse.");
        return r != null && r.Count > 1;
    }
}
