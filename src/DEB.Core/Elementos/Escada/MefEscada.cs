using System;
using System.Collections.Generic;
using System.Linq;
using DEB.Core.Analise.Mef;
using DEB.Core.Normas.Verificacao;

namespace DEB.Core.Elementos.Escada;

/// <summary>Um ponto da comparação cálculo × MEF (por metro de largura).</summary>
public sealed class PontoComparacao
{
    public double X { get; set; }
    public double MdCalculo { get; set; }
    public double VdCalculo { get; set; }
    public double NdCalculo { get; set; }
    public double FlechaCalculo { get; set; }
    public double? MdOpenSees { get; set; }
    public double? VdOpenSees { get; set; }
    public double? NdOpenSees { get; set; }
    public double? FlechaOpenSees { get; set; }
    public double? MdChapa { get; set; }
    public double? NdChapa { get; set; }
    public double? VdChapa { get; set; }
    public double? FlechaChapa { get; set; }
    public double? FlechaChapaDegraus { get; set; }
}

/// <summary>Resultado da conferência por MEF da escada: os três modelos e os pontos comparados.</summary>
public sealed class ComparacaoMefEscada
{
    public List<PontoComparacao> Pontos { get; } = new List<PontoComparacao>();
    public string ErroOpenSees { get; set; }
    public string ErroChapa { get; set; }
    public string ErroChapaDegraus { get; set; }
    public bool TemOpenSees => ErroOpenSees == null && Pontos.Any(p => p.MdOpenSees.HasValue);
    public bool TemChapa => ErroChapa == null && Pontos.Any(p => p.MdChapa.HasValue);
    public bool TemChapaDegraus => ErroChapaDegraus == null && Pontos.Any(p => p.FlechaChapaDegraus.HasValue);
    // reações de cálculo (kN/m): inferior, superior
    public (double A, double B) ReacoesCalculo { get; set; }
    public (double A, double B)? ReacoesOpenSees { get; set; }
    public (double A, double B)? ReacoesChapa { get; set; }
    public (double A, double B)? ReacoesChapaDegraus { get; set; }
    public double FlechaMaxCalculo { get; set; }
    public double? FlechaMaxOpenSees { get; set; }
    public double? FlechaMaxChapa { get; set; }
    public double? FlechaMaxChapaDegraus { get; set; }
    /// <summary>Diferença máxima do Md (%, sobre o Md,máx do cálculo).</summary>
    public double? DifMdOpenSees { get; set; }
    public double? DifMdChapa { get; set; }
    /// <summary>Diferença máxima do Md junto às quinas (concentração de tensões; não entra na verificação).</summary>
    public double? DifMdChapaQuinas { get; set; }
    /// <summary>x das quinas do eixo (para marcar no gráfico).</summary>
    public List<double> QuinasX { get; } = new List<double>();
    public List<string> Pastas { get; } = new List<string>();
    public ListaVerificacoes Verificacoes { get; } = new ListaVerificacoes();
    public List<string> Linhas { get; } = new List<string>();
}

/// <summary>
/// Conferência por elementos finitos da escada armada longitudinalmente (D42):
/// (1) OpenSees, pórtico plano no eixo quebrado (barras elásticas, Ic) — confere M, V, N, reações e a deformada;
/// (2) CalculiX, chapa da laje sem os degraus (CPS8, estado plano de tensões) — confere M e N pelos cortes (*SECTION PRINT);
/// (3) CalculiX, chapa com os degraus (CPS6 sobre a laje) — mostra quanto os degraus enrijecem (flecha e reações).
/// Mesmo esquema do cálculo: apoio fixo embaixo e móvel na horizontal em cima, cargas em projeção horizontal.
/// Esforços com a combinação de cálculo (ELU); flechas com a quase permanente e a rigidez elástica (sem fissuração).
/// </summary>
public static class MefEscada
{
    public const string CasoElu = "ELU", CasoQp = "QP";

    /// <summary>Carga vertical (kN/m² de projeção) do caso: ELU = γg·g + γq·q; QP = g + ψ2·q.</summary>
    private static bool Lateral(ResultadoEscada r) => r.Entrada.Tipo != TipoEscada.Longitudinal;

    private static double W(ResultadoEscada r, double x, string caso)
    {
        var g = r.Geo; var k = r.Coef;
        if (Lateral(r))
        {
            // faixa de 1 m ao longo do lance: carga perpendicular p1 = p·cosα (a mesma do cálculo)
            double g1 = r.GLance * g.Cos, q1 = r.Q * g.Cos;
            return caso == CasoElu ? k.GamaG * g1 + k.GamaQ * q1 : g1 + k.Psi2 * q1;
        }
        bool lance = x > g.X1 + 1e-9 && x < g.X2 - 1e-9;
        double gg = (lance ? r.GLance : r.GPatamar) + r.GParapeito;
        return caso == CasoElu ? k.GamaG * gg + k.GamaQ * r.Q : gg + k.Psi2 * r.Q;
    }

    /// <summary>Distribui a carga em projeção nos nós (x dados, ordem crescente): cada nó recebe a carga da sua faixa horizontal.</summary>
    private static List<(int No, double Fx, double Fz)> CargasNodais(ResultadoEscada r, IList<(int No, double X)> nos, string caso)
    {
        var l = new List<(int, double, double)>();
        var ord = nos.OrderBy(n => n.X).ToList();
        double L = r.VaoCalculo;
        for (int i = 0; i < ord.Count; i++)
        {
            double a = i == 0 ? 0 : (ord[i - 1].X + ord[i].X) / 2, b = i == ord.Count - 1 ? L : (ord[i].X + ord[i + 1].X) / 2;
            a = Math.Max(0, a); b = Math.Min(L, b);
            if (b <= a) continue;
            // integra W exatamente: a carga muda só no 1º e no último espelho
            double f = 0;
            if (!Lateral(r) && r.SegmentosG.Count > 0 && r.SegmentosG.Count == r.SegmentosQ.Count)
            {
                // os mesmos trechos da estática (inclui as cargas de outras peças apoiadas nesta)
                var k = r.Coef;
                for (int s = 0; s < r.SegmentosG.Count; s++)
                {
                    var sg = r.SegmentosG[s]; double wq = r.SegmentosQ[s].W;
                    double ov = Math.Min(b, sg.X1) - Math.Max(a, sg.X0);
                    if (ov <= 0) continue;
                    f += (caso == CasoElu ? k.GamaG * sg.W + k.GamaQ * wq : sg.W + k.Psi2 * wq) * ov;
                }
                l.Add((ord[i].No, 0, -f));
                continue;
            }
            var cortes = new[] { a, b, r.Geo.X1, r.Geo.X2 }.Where(x => x >= a && x <= b).Distinct().OrderBy(x => x).ToList();
            for (int s = 0; s + 1 < cortes.Count; s++)
                f += W(r, (cortes[s] + cortes[s + 1]) / 2, caso) / 100 * (cortes[s + 1] - cortes[s]);   // kN/m² ÷ 100 × cm = kN/m de largura
            l.Add((ord[i].No, 0, -f));
        }
        // balanço: parapeito na borda livre
        if (r.Entrada.Tipo == TipoEscada.Balanco && r.Entrada.Parapeito > 0 && r.Entrada.BordasComParapeito > 0 && ord.Count > 0)
            l.Add((ord[ord.Count - 1].No, 0, -(caso == CasoElu ? r.Coef.GamaG : 1) * r.Entrada.Parapeito));
        return l;
    }

    private static double Ecs(EscadaLaje e, Viga.PreMoldada.Calculo.CoeficientesViga k)
    {
        double eci = e.Fck <= 50 ? k.AlfaE * 5600 * Math.Sqrt(e.Fck) : 21500 * k.AlfaE * Math.Pow(e.Fck / 10 + 1.25, 1.0 / 3);
        return Math.Min(1, 0.8 + 0.2 * e.Fck / 80) * eci / 10;   // kN/cm²
    }

    // ------------------------------------------------------------------ OpenSees: pórtico no eixo

    public static Portico2D Portico(ResultadoEscada r, double passo = 5)
    {
        var g = r.Geo; var p = new Portico2D();
        double E = Ecs(r.Entrada, r.Coef);
        if (Lateral(r))
        {
            // faixa na largura: barra horizontal de y = 0 a ℓ
            double L = r.VaoCalculo; int n = Math.Max(4, (int)Math.Ceiling(L / passo));
            for (int i = 0; i <= n; i++)
            {
                p.Nos.Add((L * i / n, 0));
                if (i > 0) p.Barras.Add((i - 1, i, E, 100 * g.H, 100 * Math.Pow(g.H, 3) / 12));
            }
            bool bal = r.Entrada.Tipo == TipoEscada.Balanco;
            p.Apoios.Add((0, true, true, bal));
            if (!bal) p.Apoios.Add((n, false, true, false));
            var ns = p.Nos.Select((q, i) => (i, q.X)).ToList();
            p.Casos[CasoElu] = CargasNodais(r, ns, CasoElu);
            p.Casos[CasoQp] = CargasNodais(r, ns, CasoQp);
            return p;
        }
        foreach (var t in g.Trechos)
        {
            int n = Math.Max(2, (int)Math.Ceiling(t.Comprimento / passo));
            for (int i = 0; i <= n; i++)
            {
                if (i == 0 && p.Nos.Count > 0) continue;   // nó da quina já existe
                double u = (double)i / n;
                p.Nos.Add((t.P0.X + (t.P1.X - t.P0.X) * u, t.P0.Z + (t.P1.Z - t.P0.Z) * u));
                if (p.Nos.Count > 1)
                    p.Barras.Add((p.Nos.Count - 2, p.Nos.Count - 1, E, 100 * t.H, 100 * Math.Pow(t.H, 3) / 12));
            }
        }
        p.Apoios.Add((0, true, true, false));
        p.Apoios.Add((p.Nos.Count - 1, false, true, false));
        var nos = p.Nos.Select((n, i) => (i, n.X)).ToList();
        p.Casos[CasoElu] = CargasNodais(r, nos, CasoElu);
        p.Casos[CasoQp] = CargasNodais(r, nos, CasoQp);
        return p;
    }

    // ------------------------------------------------------------------ CalculiX: chapa (com ou sem degraus)

    /// <summary>Colunas da malha (topo e fundo) ao longo do eixo: nas quinas, as interseções das faces dos dois trechos.</summary>
    private static List<((double X, double Z) Topo, (double X, double Z) Fundo, (double X, double Z) Eixo)> Colunas(ResultadoEscada r, double passoPatamar)
    {
        var g = r.Geo; var cols = new List<((double, double), (double, double), (double, double))>();
        (double, double) Desloca((double X, double Z) p, TrechoEscada t, double d)
        {
            double a = t.Angulo; return (p.X - Math.Sin(a) * d, p.Z + Math.Cos(a) * d);
        }
        (double X, double Z) Inter((double X, double Z) p1, double a1, (double X, double Z) p2, double a2)
        {
            double c1 = Math.Cos(a1), s1 = Math.Sin(a1), c2 = Math.Cos(a2), s2 = Math.Sin(a2);
            double den = c1 * s2 - s1 * c2;
            double tt = ((p2.X - p1.X) * s2 - (p2.Z - p1.Z) * c2) / den;
            return (p1.X + c1 * tt, p1.Z + s1 * tt);
        }
        for (int ti = 0; ti < g.Trechos.Count; ti++)
        {
            var t = g.Trechos[ti];
            double a = t.Angulo;
            // início
            (double, double) topo0, fundo0;
            // pontas: coluna vertical no x do apoio (topo na face de cima, fundo na de baixo)
            (double, double) NaVertical((double X, double Z) p, double d, double x) { var q = ((double X, double Z))Desloca(p, t, d); return (x, q.Z + (x - q.X) * Math.Tan(a)); }
            if (ti == 0) { topo0 = NaVertical(t.P0, t.H / 2, t.P0.X); fundo0 = NaVertical(t.P0, -t.H / 2, t.P0.X); }
            else
            {
                var u = g.Trechos[ti - 1];
                topo0 = Inter(Desloca(u.P0, u, u.H / 2), u.Angulo, Desloca(t.P0, t, t.H / 2), a);
                fundo0 = Inter(Desloca(u.P0, u, -u.H / 2), u.Angulo, Desloca(t.P0, t, -t.H / 2), a);
            }
            (double, double) topo1, fundo1;
            if (ti == g.Trechos.Count - 1) { topo1 = NaVertical(t.P1, t.H / 2, t.P1.X); fundo1 = NaVertical(t.P1, -t.H / 2, t.P1.X); }
            else
            {
                var u = g.Trechos[ti + 1];
                topo1 = Inter(Desloca(t.P0, t, t.H / 2), a, Desloca(u.P0, u, u.H / 2), u.Angulo);
                fundo1 = Inter(Desloca(t.P0, t, -t.H / 2), a, Desloca(u.P0, u, -u.H / 2), u.Angulo);
            }
            if (ti == 0) cols.Add((topo0, fundo0, t.P0));
            // colunas intermediárias: no lance pelos topos (cantos internos dos degraus e meios dos pisos), nos patamares a cada passo
            var tops = new List<(double X, double Z)>();
            if (t.Inclinado)
            {
                double tg = Math.Tan(g.Alfa);
                for (int k2 = -2; k2 <= 2 * g.N + 2; k2++)
                {
                    double xt = g.X1 + k2 * g.Piso / 2;
                    tops.Add((xt, (xt - g.X1) * tg));
                }
            }
            else
            {
                int n = Math.Max(1, (int)Math.Ceiling(Math.Abs(t.P1.X - t.P0.X) / passoPatamar));
                for (int i = 1; i < n; i++) { double xi = t.P0.X + (t.P1.X - t.P0.X) * i / n; tops.Add((xi, t.P0.Z + t.H / 2)); }
            }
            double margem = Math.Max(2, 0.4 * t.H);
            foreach (var tp in tops.OrderBy(q => q.X))
            {
                if (tp.X <= topo0.Item1 + margem || tp.X >= topo1.Item1 - margem) continue;
                var fd = (tp.X + Math.Sin(a) * t.H, tp.Z - Math.Cos(a) * t.H);
                if (fd.Item1 <= fundo0.Item1 + margem / 2 || fd.Item1 >= fundo1.Item1 - margem / 2) continue;
                var eixo = ((tp.X + fd.Item1) / 2, (tp.Z + fd.Item2) / 2);
                cols.Add((tp, fd, eixo));
            }
            cols.Add((topo1, fundo1, t.P1));
        }
        return cols;
    }

    /// <summary>Monta a chapa: camadas pela espessura; com <paramref name="degraus"/>, triângulos dos degraus sobre o lance.</summary>
    public static Chapa2D Chapa(ResultadoEscada r, bool degraus, int camadas = 4, double passoPatamar = 10)
    {
        var g = r.Geo; var c = new Chapa2D { E = Ecs(r.Entrada, r.Coef), Nu = 0.2, Espessura = 100 };
        if (Lateral(r)) return ChapaFaixa(r, c, camadas);
        var cols = Colunas(r, passoPatamar);
        int nz = camadas;
        var ids = new List<int[]>();
        foreach (var col in cols)
        {
            var v = new int[nz + 1];
            for (int j = 0; j <= nz; j++)
            {
                double u = (double)j / nz;
                v[j] = c.No(col.Fundo.X + (col.Topo.X - col.Fundo.X) * u, col.Fundo.Z + (col.Topo.Z - col.Fundo.Z) * u);
            }
            ids.Add(v);
        }
        for (int i = 0; i + 1 < cols.Count; i++)
        {
            var faces = new List<(int, int)>();
            for (int j = 0; j < nz; j++)
            {
                int e = c.Quad(ids[i][j], ids[i + 1][j], ids[i + 1][j + 1], ids[i][j + 1]);
                faces.Add((e, 2));   // face 2 = lado da coluna i+1
            }
            if (i + 1 < cols.Count - 1) c.Cortes.Add(("C" + (i + 1), faces));
        }
        // eixo: nó do meio (com nz par) de cada coluna
        int meio = nz / 2;
        foreach (var v in ids) c.NosLidos.Add(nz % 2 == 0 ? v[meio] : c.No((c.Nos[v[meio]].X + c.Nos[v[meio + 1]].X) / 2, (c.Nos[v[meio]].Z + c.Nos[v[meio + 1]].Z) / 2));
        if (degraus)
        {
            double tg = Math.Tan(g.Alfa);
            bool ps = g.Vao - g.X2 > 1e-9;
            int ultimo = ps ? g.N : g.N - 1;
            for (int i = 1; i <= ultimo; i++)
            {
                double xr = g.X1 + (i - 1) * g.Piso;
                var a = (xr, (i - 1) * g.Espelho); var b = (xr + g.Piso, i * g.Espelho); var ap = (xr, i * g.Espelho);
                var mab = ((a.Item1 + b.Item1) / 2, (a.Item2 + b.Item2) / 2);
                var mbc = ((b.Item1 + ap.Item1) / 2, (b.Item2 + ap.Item2) / 2);
                var mca = ((ap.Item1 + a.Item1) / 2, (ap.Item2 + a.Item2) / 2);
                int A = c.No(a.Item1, a.Item2), B = c.No(b.Item1, b.Item2), C = c.No(ap.Item1, ap.Item2);
                int Mab = c.No(mab.Item1, mab.Item2), Mbc = c.No(mbc.Item1, mbc.Item2), Mca = c.No(mca.Item1, mca.Item2);
                c.Tri(A, Mab, Mca); c.Tri(Mab, B, Mbc); c.Tri(Mca, Mbc, C); c.Tri(Mab, Mbc, Mca);
            }
        }
        // apoios no eixo das pontas; cargas no topo da laje (a mesma faixa horizontal do cálculo)
        c.Apoios.Add((c.NosLidos[0], true, true));
        c.Apoios.Add((c.NosLidos[c.NosLidos.Count - 1], false, true));
        var topos = ids.Select(v => (v[nz], c.Nos[v[nz]].X)).ToList();
        c.Casos[CasoElu] = CargasNodais(r, topos, CasoElu);
        c.Casos[CasoQp] = CargasNodais(r, topos, CasoQp);
        return c;
    }

    /// <summary>Chapa da faixa na largura (ℓ × h): transversal apoiada no meio da espessura das pontas; balanço com a borda do apoio travada.</summary>
    private static Chapa2D ChapaFaixa(ResultadoEscada r, Chapa2D c, int nz)
    {
        double L = r.VaoCalculo, h = r.Geo.H; int nx = Math.Max(8, (int)Math.Ceiling(L / 5));
        bool bal = r.Entrada.Tipo == TipoEscada.Balanco;
        var ids = new List<int[]>();
        for (int i = 0; i <= nx; i++)
        {
            var v = new int[nz + 1];
            for (int j = 0; j <= nz; j++) v[j] = c.No(L * i / nx, h * j / nz - h / 2);
            ids.Add(v);
        }
        for (int i = 0; i < nx; i++)
        {
            var faces = new List<(int, int)>();
            for (int j = 0; j < nz; j++) faces.Add((c.Quad(ids[i][j], ids[i + 1][j], ids[i + 1][j + 1], ids[i][j + 1]), 2));
            if (i + 1 < nx) c.Cortes.Add(("C" + (i + 1), faces));
        }
        foreach (var v in ids) c.NosLidos.Add(v[nz / 2]);
        if (bal)
            for (int j = 0; j <= 2 * nz; j++) c.Apoios.Add((c.No(0, h * j / (2 * nz) - h / 2), true, true));
        else { c.Apoios.Add((ids[0][nz / 2], true, true)); c.Apoios.Add((ids[nx][nz / 2], false, true)); }
        var topos = ids.Select(v => (v[nz], c.Nos[v[nz]].X)).ToList();
        c.Casos[CasoElu] = CargasNodais(r, topos, CasoElu);
        c.Casos[CasoQp] = CargasNodais(r, topos, CasoQp);
        return c;
    }

    // ------------------------------------------------------------------ comparação

    public static ComparacaoMefEscada Comparar(ResultadoEscada r, FerramentasMef ferr = null, bool openSees = true, bool calculix = true)
    {
        ferr ??= FerramentasMef.Padrao;
        var cmp = new ComparacaoMefEscada();
        var k = r.Coef;
        double L = r.Geo.Vao;
        foreach (var p in r.Diagrama.Where((p, i) => i % 5 == 0))
            cmp.Pontos.Add(new PontoComparacao { X = p.X, MdCalculo = p.Md, VdCalculo = p.Vd, NdCalculo = p.Nd, FlechaCalculo = p.FlechaElastica });
        cmp.ReacoesCalculo = (k.GamaG * r.RaG + k.GamaQ * r.RaQ, k.GamaG * r.RbG + k.GamaQ * r.RbQ);
        cmp.FlechaMaxCalculo = r.FlechaElasticaMax;

        if (openSees)
        {
            var mod = Portico(r);
            var res = mod.RodarOpenSees(ferr);
            if (res.Pasta != null) cmp.Pastas.Add(res.Pasta);
            if (!res.Ok) cmp.ErroOpenSees = res.Erro;
            else
            {
                var elu = res.Casos[CasoElu]; var qp = res.Casos[CasoQp];
                foreach (var pt in cmp.Pontos)
                {
                    int b = Barra(mod, pt.X);
                    var (ni, nj) = (mod.Barras[b].I, mod.Barras[b].J);
                    double x0 = mod.Nos[ni].X, x1 = mod.Nos[nj].X, u = x1 - x0 > 1e-9 ? (pt.X - x0) / (x1 - x0) : 0;
                    var fi = elu.InternoI(b); var fj = elu.InternoJ(b);
                    pt.MdOpenSees = (fi.M + (fj.M - fi.M) * u) / 100;
                    pt.VdOpenSees = fi.V + (fj.V - fi.V) * u;
                    pt.NdOpenSees = fi.N + (fj.N - fi.N) * u;
                    double ui = -qp.Deslocamentos[ni].Uz, uj = -qp.Deslocamentos[nj].Uz;
                    pt.FlechaOpenSees = ui + (uj - ui) * u;
                }
                var ra = elu.Reacoes[0];
                double rbz = elu.Reacoes.TryGetValue(mod.Nos.Count - 1, out var rb) ? rb.Fz : 0;
                cmp.ReacoesOpenSees = (ra.Fz, rbz);
                cmp.FlechaMaxOpenSees = qp.Deslocamentos.Values.Max(d => -d.Uz);
            }
        }
        if (calculix)
        {
            foreach (bool comDegraus in Lateral(r) ? new[] { false } : new[] { false, true })
            {
                var ch = Chapa(r, comDegraus);
                var re = ch.RodarCalculix(CasoElu, ferr);
                var rq = re.Ok ? ch.RodarCalculix(CasoQp, ferr) : re;
                if (re.Pasta != null) cmp.Pastas.Add(re.Pasta);
                string erro = !re.Ok ? re.Erro : !rq.Ok ? rq.Erro : null;
                if (comDegraus) cmp.ErroChapaDegraus = erro; else cmp.ErroChapa = erro;
                if (erro != null) continue;
                // deformada pelo eixo (nós lidos)
                var eixo = ch.NosLidos.Select(n => (ch.Nos[n].X, Uz: rq.Deslocamentos.TryGetValue(n, out var d) ? -d.Uz : 0)).OrderBy(t => t.X).ToList();
                double FlechaEm(double x)
                {
                    var a = eixo.LastOrDefault(t => t.X <= x); var b = eixo.FirstOrDefault(t => t.X >= x);
                    if (b.X - a.X < 1e-9) return a.Uz;
                    return a.Uz + (b.Uz - a.Uz) * (x - a.X) / (b.X - a.X);
                }
                var rea = re.Reacoes;
                double xMeio = r.VaoCalculo / 2;
                // o RF do CalculiX não inclui a carga aplicada direto num nó travado: soma por equilíbrio
                var aplicada = ch.Casos[CasoElu].GroupBy(q => q.No).ToDictionary(gq => gq.Key, gq => gq.Sum(q => q.Fz));
                double Reacao(int no) => (rea.TryGetValue(no, out var q) ? q.Fz : 0) - (aplicada.TryGetValue(no, out double fz) ? fz : 0);
                double ra = ch.Apoios.Where(a => ch.Nos[a.No].X < xMeio).Sum(a => Reacao(a.No));
                double rb = ch.Apoios.Where(a => ch.Nos[a.No].X >= xMeio).Sum(a => Reacao(a.No));
                if (comDegraus)
                {
                    foreach (var pt in cmp.Pontos) pt.FlechaChapaDegraus = FlechaEm(pt.X);
                    cmp.ReacoesChapaDegraus = (ra, rb);
                    cmp.FlechaMaxChapaDegraus = eixo.Max(t => t.Uz);
                }
                else
                {
                    var cortes = ch.Cortes.Select(c2 => re.Cortes.TryGetValue(c2.Nome, out var cc) ? cc : null).Where(cc => cc != null).OrderBy(cc => cc.Xg).ToList();
                    foreach (var pt in cmp.Pontos)
                    {
                        pt.FlechaChapa = FlechaEm(pt.X);
                        if (cortes.Count == 0) continue;
                        var a = cortes.LastOrDefault(cc => cc.Xg <= pt.X) ?? cortes[0];
                        var b = cortes.FirstOrDefault(cc => cc.Xg >= pt.X) ?? cortes[cortes.Count - 1];
                        double u = b.Xg - a.Xg > 1e-9 ? (pt.X - a.Xg) / (b.Xg - a.Xg) : 0;
                        if (pt.X < cortes[0].Xg || pt.X > cortes[cortes.Count - 1].Xg) continue;   // perto do apoio (sem corte)
                        pt.MdChapa = (a.M + (b.M - a.M) * u) / 100;
                        pt.NdChapa = a.N + (b.N - a.N) * u;
                        // cortante = dM/ds entre os cortes vizinhos (a integração das tensões de cisalhamento do *SECTION PRINT
                        // superestima V com elementos quadráticos; a derivada do momento é exata no trecho reto)
                        double ds = Math.Sqrt(Math.Pow(b.Xg - a.Xg, 2) + Math.Pow(b.Zg - a.Zg, 2));
                        pt.VdChapa = ds > 1e-9 ? (b.M - a.M) / ds : a.V;
                    }
                    cmp.ReacoesChapa = (ra, rb);
                    cmp.FlechaMaxChapa = eixo.Max(t => t.Uz);
                }
            }
        }
        // resumo e verificações
        double mmax = Math.Max(1e-9, r.MdMax);
        if (cmp.TemOpenSees) cmp.DifMdOpenSees = cmp.Pontos.Where(p => p.MdOpenSees.HasValue).Max(p => Math.Abs(p.MdOpenSees.Value - p.MdCalculo)) / mmax * 100;
        if (cmp.TemChapa)
        {
            // longe das quinas (concentração de tensões na chapa) e dos apoios: uma espessura e meia
            var quinas = r.Geo.Trechos.Skip(1).Select(t => t.P0.X).ToList();
            double faixa = 1.5 * Math.Max(r.Geo.H, r.Geo.Hp);
            cmp.QuinasX.AddRange(quinas);
            var longe = cmp.Pontos.Where(p => p.MdChapa.HasValue && quinas.All(q => Math.Abs(p.X - q) > faixa)).ToList();
            cmp.DifMdChapa = longe.Count == 0 ? 0 : longe.Max(p => Math.Abs(p.MdChapa.Value - p.MdCalculo)) / mmax * 100;
            var junto = cmp.Pontos.Where(p => p.MdChapa.HasValue && quinas.Any(q => Math.Abs(p.X - q) <= faixa)).ToList();
            cmp.DifMdChapaQuinas = junto.Count == 0 ? 0 : junto.Max(p => Math.Abs(p.MdChapa.Value - p.MdCalculo)) / mmax * 100;
        }
        string N(double x, string f = "0.##") => Fmt.Virgula(x, f);
        cmp.Linhas.Add($"Cálculo: Md,máx = {N(r.MdMax, "0.00")} kN·m/m; reações de cálculo {N(cmp.ReacoesCalculo.A, "0.00")} e {N(cmp.ReacoesCalculo.B, "0.00")} kN/m; flecha elástica (QP, Ic) {N(cmp.FlechaMaxCalculo, "0.000")} cm.");
        if (cmp.TemOpenSees)
        {
            cmp.Linhas.Add($"OpenSees ({(Lateral(r) ? "faixa de 1 m na largura" : "pórtico no eixo")}): diferença máx. do Md {N(cmp.DifMdOpenSees.Value, "0.0")} %; reações {N(cmp.ReacoesOpenSees.Value.A, "0.00")} e {N(cmp.ReacoesOpenSees.Value.B, "0.00")} kN/m; flecha {N(cmp.FlechaMaxOpenSees.Value, "0.000")} cm.");
            cmp.Verificacoes.Add(Kc("M1", "Momento fletor: cálculo × OpenSees", $"{N(cmp.DifMdOpenSees.Value, "0.0")} %", "≤ 3 %", cmp.DifMdOpenSees.Value > 3));
            double df = Math.Abs(cmp.FlechaMaxOpenSees.Value - cmp.FlechaMaxCalculo) / Math.Max(1e-9, cmp.FlechaMaxCalculo) * 100;
            cmp.Verificacoes.Add(Kc("M2", "Flecha elástica: cálculo × OpenSees", $"{N(df, "0.0")} %", "≤ 5 %", df > 5));
        }
        else cmp.Linhas.Add("OpenSees: " + (cmp.ErroOpenSees ?? "não rodado."));
        if (cmp.TemChapa)
        {
            cmp.Linhas.Add($"CalculiX ({(Lateral(r) ? "chapa da faixa" : "chapa da laje, sem degraus")}): diferença máx. do Md nos cortes {N(cmp.DifMdChapa.Value, "0.0")} % (junto às quinas, {N(cmp.DifMdChapaQuinas ?? 0, "0.0")} %: concentração de tensões); reações {N(cmp.ReacoesChapa.Value.A, "0.00")} e {N(cmp.ReacoesChapa.Value.B, "0.00")} kN/m; flecha {N(cmp.FlechaMaxChapa.Value, "0.000")} cm.");
            cmp.Verificacoes.Add(Kc("M3", "Momento fletor: cálculo × CalculiX (chapa)", $"{N(cmp.DifMdChapa.Value, "0.0")} %", "≤ 5 %", cmp.DifMdChapa.Value > 5));
        }
        else cmp.Linhas.Add("CalculiX (chapa): " + (cmp.ErroChapa ?? "não rodado."));
        if (cmp.TemChapaDegraus)
        {
            double ganho = cmp.FlechaMaxChapa.HasValue && cmp.FlechaMaxChapaDegraus > 0 ? cmp.FlechaMaxChapa.Value / cmp.FlechaMaxChapaDegraus.Value : 0;
            cmp.Linhas.Add($"CalculiX (chapa com os degraus): flecha {N(cmp.FlechaMaxChapaDegraus.Value, "0.000")} cm" +
                           (ganho > 0 ? $" — os degraus deixam a escada {N(ganho, "0.0")} vezes mais rígida que a laje sozinha (o cálculo despreza, a favor da segurança)." : "."));
        }
        else if (!Lateral(r)) cmp.Linhas.Add("CalculiX (com degraus): " + (cmp.ErroChapaDegraus ?? "não rodado."));
        return cmp;
    }

    private static int Barra(Portico2D p, double x)
    {
        for (int b = 0; b < p.Barras.Count; b++)
        {
            double x0 = p.Nos[p.Barras[b].I].X, x1 = p.Nos[p.Barras[b].J].X;
            if (x >= Math.Min(x0, x1) - 1e-9 && x <= Math.Max(x0, x1) + 1e-9) return b;
        }
        return p.Barras.Count - 1;
    }

    private static ResultadoVerificacao Kc(string cod, string desc, string valor, string limite, bool falha) => new ResultadoVerificacao
    {
        Codigo = cod, Grupo = "Escada — Conferência por MEF", Descricao = desc, Valor = valor, Limite = limite, Referencia = "MEF (D42)",
        Situacao = falha ? SituacaoVerificacao.Conferir : SituacaoVerificacao.Ok,
        Dica = falha ? "Diferença acima do esperado: confira o modelo (apoios, cargas) antes de usar o resultado." : "",
    };
}
