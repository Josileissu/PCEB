using System;
using System.Collections.Generic;
using System.Linq;
using DEB.Core.Analise.Barras;
using DEB.Core.Configuracao.Projeto;
using DEB.Core.Elementos.Consolo;
using DEB.Core.Intercambio;
using DEB.Core.Pecas;

namespace DEB.Core.Analise.Objetos;

// =====================================================================================================================
// Análise por objetos (D46): cada peça e cada ligação gera e guarda as suas partes no modelo de barras; os esforços voltam a
// elas. Unidades: cm, kN. Coordenadas do mundo: X, Y horizontais, Z para cima (as do Revit, convertidas em cm).
// =====================================================================================================================

/// <summary>Pilar: barras no eixo (CG da seção), nós nos níveis pedidos, engaste no topo do cálice (embutimento).</summary>
public sealed class PilarObjeto
{
    public Dono Dono { get; set; } = new Dono { Tipo = TipoDono.Pilar };
    /// <summary>Ponto do eixo (CG da seção) na base da peça (fundo do cálice), cm.</summary>
    public Vetor3 Base { get; set; }
    /// <summary>Direção do eixo (normalmente +Z).</summary>
    public Vetor3 Eixo { get; set; } = Vetor3.EixoZ;
    /// <summary>Direção, no mundo, do eixo y da seção (horizontal).</summary>
    public Vetor3 DirY { get; set; } = Vetor3.EixoX;
    /// <summary>Seção nos eixos dela (y, z); o CG vai para o eixo da barra.</summary>
    public SecaoPoligonal Secao { get; set; }
    public double Comprimento { get; set; }
    /// <summary>Comprimento embutido no cálice (cm): o engaste fica nessa altura a partir da base.</summary>
    public double Embutimento { get; set; }
    public MaterialBarra Material { get; set; }
    /// <summary>Alturas (cm a partir da base) onde o pilar precisa de nó: topo dos consolos, vigas apoiadas.</summary>
    public List<double> Niveis { get; } = new List<double>();
    /// <summary>Fator de rigidez à flexão (NBR 6118 15.7.3: 0,8 na 2ª ordem global); 1 = elástica.</summary>
    public double FatorFlexao { get; set; } = 1;

    // gerado
    public List<(double Z, int No)> Nos { get; } = new List<(double, int)>();
    public List<BarraPortico> Barras { get; } = new List<BarraPortico>();
    public Vetor3 DirZ => Eixo.Vetorial(DirY).Unitario;
    public Vetor3 Ponto(double z) => Base + Eixo.Unitario * z;

    internal void Gerar(ModeloBarras m)
    {
        var sec = PropriedadesSecao.Barra(Secao);
        var zs = new List<double> { Embutimento, Comprimento };
        zs.AddRange(Niveis.Where(z => z > Embutimento + 0.5 && z < Comprimento - 0.5));
        zs = zs.Select(z => Math.Round(z, 2)).Distinct().OrderBy(z => z).ToList();
        Nos.Clear(); Barras.Clear();
        foreach (double z in zs)
        {
            Vetor3 p = Ponto(z);
            Nos.Add((z, m.NovoNo(p.X, p.Y, p.Z, Dono, $"{Dono} z={z:0.#}")));
        }
        for (int i = 0; i + 1 < Nos.Count; i++)
        {
            var b = m.Barra(Nos[i].No, Nos[i + 1].No, sec, Material, Dono, $"{Dono} {Nos[i].Z:0}–{Nos[i + 1].Z:0}");
            b.VetorXZ = DirZ;
            b.FatorFlexao = FatorFlexao;
            Barras.Add(b);
        }
        m.Apoio(Nos[0].No, ApoioNo.Engaste, Dono);
    }

    /// <summary>Nó do pilar na altura z (o mais próximo).</summary>
    public int NoNoNivel(double z) => Nos.OrderBy(n => Math.Abs(n.Z - z)).First().No;
}

/// <summary>Viga biapoiada sobre aparelhos: barra no eixo (CG), entre os pontos de apoio; nós de apoio presos ao eixo por vínculo rígido.</summary>
public sealed class VigaObjeto
{
    public Dono Dono { get; set; } = new Dono { Tipo = TipoDono.Viga };
    /// <summary>Pontos de apoio (centro do aparelho, na face de baixo da viga), cm.</summary>
    public Vetor3 ApoioI { get; set; }
    public Vetor3 ApoioJ { get; set; }
    /// <summary>Seção nos eixos dela (y horizontal, z vertical).</summary>
    public SecaoPoligonal Secao { get; set; }
    public MaterialBarra Material { get; set; }
    /// <summary>Cargas além do peso próprio (kN/m) e concentradas (X em cm a partir do apoio I; G e Q em kN).</summary>
    public double G { get; set; }
    public double Q { get; set; }
    public List<(double X, double G, double Q)> Concentradas { get; } = new List<(double, double, double)>();
    public double FatorFlexao { get; set; } = 1;
    /// <summary>Fator da rigidez à torção (0,15 = concreto fissurado, prática usual; 1 = elástica).</summary>
    public double FatorTorcao { get; set; } = 0.15;

    /// <summary>Pontos onde a viga precisa de nó (cm a partir do apoio I, no eixo): apoio de laje, de outra viga.</summary>
    public List<double> PontosInternos { get; } = new List<double>();

    // gerado
    public int NoEixoI { get; private set; }
    public int NoEixoJ { get; private set; }
    public int NoApoioI { get; private set; }
    public int NoApoioJ { get; private set; }
    /// <summary>Primeira barra (a única, sem pontos internos).</summary>
    public BarraPortico Barra => Barras[0];
    public List<BarraPortico> Barras { get; } = new List<BarraPortico>();
    /// <summary>Nós do eixo com a posição (cm a partir do apoio I).</summary>
    public List<(double T, int No)> NosEixo { get; } = new List<(double, int)>();
    public double Vao => (ApoioJ - ApoioI).Modulo;
    public Vetor3 Direcao => (ApoioJ - ApoioI).Unitario;
    /// <summary>Nó do eixo mais perto da posição t.</summary>
    public int NoNaPosicao(double t) => NosEixo.OrderBy(n => Math.Abs(n.T - t)).First().No;
    /// <summary>Altura do CG acima da face de baixo (cm).</summary>
    public double AlturaEixo => Secao.CG.Z - Secao.ZMin;

    internal void Gerar(ModeloBarras m, CasoCarga g, CasoCarga q)
    {
        var up = Vetor3.EixoZ * AlturaEixo;
        Vetor3 ei = ApoioI + up, ej = ApoioJ + up;
        NoEixoI = m.NovoNo(ei.X, ei.Y, ei.Z, Dono, Dono + " eixo I");
        NoEixoJ = m.NovoNo(ej.X, ej.Y, ej.Z, Dono, Dono + " eixo J");
        NoApoioI = m.NovoNo(ApoioI.X, ApoioI.Y, ApoioI.Z, Dono, Dono + " apoio I");
        NoApoioJ = m.NovoNo(ApoioJ.X, ApoioJ.Y, ApoioJ.Z, Dono, Dono + " apoio J");
        Barras.Clear(); NosEixo.Clear();
        double L = Vao;
        NosEixo.Add((0, NoEixoI));
        foreach (double t in PontosInternos.Where(t => t > 1 && t < L - 1).Select(t => Math.Round(t, 1)).Distinct().OrderBy(t => t))
        {
            Vetor3 p = ei + Direcao * t;
            NosEixo.Add((t, m.NovoNo(p.X, p.Y, p.Z, Dono, $"{Dono} t={t:0}")));
        }
        NosEixo.Add((L, NoEixoJ));
        var sec = PropriedadesSecao.Barra(Secao);
        for (int k = 0; k + 1 < NosEixo.Count; k++)
        {
            var b = m.Barra(NosEixo[k].No, NosEixo[k + 1].No, sec, Material, Dono, NosEixo.Count > 2 ? $"{Dono} trecho {k + 1}" : Dono.ToString());
            b.VetorXZ = Vetor3.EixoZ;
            b.FatorFlexao = FatorFlexao;
            b.FatorTorcao = FatorTorcao;
            Barras.Add(b);
            if (G != 0) g.Distribuidas.Add(new CargaDistribuida { Barra = b.Id, Q = new Vetor3(0, 0, -G / 100) });
            if (Q != 0) q.Distribuidas.Add(new CargaDistribuida { Barra = b.Id, Q = new Vetor3(0, 0, -Q / 100) });
        }
        m.Rigido(NoEixoI, NoApoioI, Dono, Dono + " altura do eixo I");
        m.Rigido(NoEixoJ, NoApoioJ, Dono, Dono + " altura do eixo J");
        foreach (var (x, cg, cq) in Concentradas)
        {
            int k = Math.Max(0, NosEixo.FindLastIndex(n => n.T <= x) );
            k = Math.Min(k, Barras.Count - 1);
            double a = x - NosEixo[k].T;
            if (cg != 0) g.Pontuais.Add(new CargaPontualBarra { Barra = Barras[k].Id, A = a, F = new Vetor3(0, 0, -cg) });
            if (cq != 0) q.Pontuais.Add(new CargaPontualBarra { Barra = Barras[k].Id, A = a, F = new Vetor3(0, 0, -cq) });
        }
    }
}

/// <summary>
/// Ligação viga–pilar sem consolo (D46): a ponta do eixo da viga presa ao nó do pilar no mesmo nível por vínculo rígido —
/// monolítica (todos os graus: viga moldada no local) ou articulada (translações e a torção da viga: pré-moldada chumbada).
/// </summary>
public sealed class LigacaoPilarViga
{
    public Dono Dono { get; set; } = new Dono { Tipo = TipoDono.Ligacao };
    public PilarObjeto Pilar { get; set; }
    public VigaObjeto Viga { get; set; }
    public bool PontaI { get; set; }
    public bool Articulada { get; set; }
    /// <summary>Altura do eixo da viga a partir da base do pilar (cm).</summary>
    public double ZNivel => (((PontaI ? Viga.ApoioI : Viga.ApoioJ) + Vetor3.EixoZ * Viga.AlturaEixo) - Pilar.Base).Ponto(Pilar.Eixo.Unitario);
    public int NoPilar { get; private set; }

    internal void Gerar(ModeloBarras m)
    {
        NoPilar = Pilar.NoNoNivel(ZNivel);
        int noViga = PontaI ? Viga.NoEixoI : Viga.NoEixoJ;
        bool[] graus = { true, true, true, true, true, true };
        if (Articulada)
        {
            // rotações livres, menos a torção da viga (senão a viga gira em torno do eixo dela)
            Vetor3 d = Viga.Direcao;
            graus = new[] { true, true, true, Math.Abs(d.X) >= Math.Abs(d.Y), Math.Abs(d.Y) > Math.Abs(d.X), false };
        }
        m.Rigido(NoPilar, noViga, Dono, Dono + (Articulada ? " (articulada)" : " (monolítica)"), graus);
    }
}

/// <summary>
/// Laje em grelha (D46; padrão para laje maciça/nervurada moldada no local): malha nas duas direções sobre o contorno, faixas com a
/// espessura da laje (I = b·h³/12, torção de placa J = b·h³/6 — analogia de Hambly), cargas por m² nos nós pela área de influência,
/// nós apoiados nas vigas (projeção no eixo, translações) e nos pilares que estão sob a laje.
/// </summary>
public sealed class LajeObjeto
{
    public Dono Dono { get; set; } = new Dono { Tipo = TipoDono.Laje };
    /// <summary>Contorno no plano médio da laje (cm, mundo).</summary>
    public List<Vetor3> Contorno { get; } = new List<Vetor3>();
    public double Espessura { get; set; } = 12;
    public MaterialBarra Material { get; set; }
    /// <summary>Peso específico (kN/m³) para o peso próprio nos nós.</summary>
    public double PesoEspecifico { get; set; } = 25;
    /// <summary>Cargas além do peso próprio (kN/m²).</summary>
    public double G { get; set; }
    public double Q { get; set; }
    /// <summary>Espaçamento da malha (cm).</summary>
    public double Malha { get; set; } = 50;
    /// <summary>Direção x da malha (horizontal); nula = o maior lado do contorno.</summary>
    public Vetor3? DirX { get; set; }
    /// <summary>Teste e laje isolada: bordas com apoio simples (translações), sem vigas.</summary>
    public bool ApoiarBordas { get; set; }

    // gerado
    public Vetor3 Origem { get; private set; }
    public Vetor3 Ex { get; private set; }
    public Vetor3 Ey { get; private set; }
    public int Nx { get; private set; }
    public int Ny { get; private set; }
    public double Sx { get; private set; }
    public double Sy { get; private set; }
    /// <summary>Nó da grelha em (i, j); −1 = fora do contorno.</summary>
    public int[,] Nos { get; private set; }
    public List<BarraPortico> BarrasX { get; } = new List<BarraPortico>();
    public List<BarraPortico> BarrasY { get; } = new List<BarraPortico>();
    /// <summary>Apoios encontrados: nó da grelha → (viga, posição) ou pilar.</summary>
    private readonly List<(int I, int J, VigaObjeto Viga, double T, PilarObjeto Pilar)> _apoios = new List<(int, int, VigaObjeto, double, PilarObjeto)>();
    private bool[,] _dentro;
    public int NosLivresNaBorda { get; private set; }

    private (double X, double Y) Local(Vetor3 p) { var d = p - Origem; return (d.Ponto(Ex), d.Ponto(Ey)); }
    public Vetor3 Mundo(double x, double y) => Origem + Ex * x + Ey * y;
    public Vetor3 Ponto(int i, int j) => Mundo(i * Sx, j * Sy);

    private static bool DentroPoligono(IList<(double X, double Y)> p, double x, double y, double tol)
    {
        bool dentro = false;
        for (int i = 0, j = p.Count - 1; i < p.Count; j = i++)
        {
            var (xi, yi) = p[i]; var (xj, yj) = p[j];
            // na borda (com tolerância) conta como dentro
            double dx = xj - xi, dy = yj - yi, l2 = dx * dx + dy * dy;
            double t = l2 < 1e-12 ? 0 : Math.Max(0, Math.Min(1, ((x - xi) * dx + (y - yi) * dy) / l2));
            { double ex = x - xi - t * dx, ey = y - yi - t * dy; if (Math.Sqrt(ex * ex + ey * ey) <= tol) return true; }
            if ((yi > y) != (yj > y) && x < dx * (y - yi) / dy + xi) dentro = !dentro;
        }
        return dentro;
    }

    /// <summary>1º passo (antes de pilares e vigas gerarem): malha, e onde vigas e pilares precisam de nó.</summary>
    internal void Preparar(IList<VigaObjeto> vigas, IList<PilarObjeto> pilares)
    {
        Origem = Contorno[0];
        Vetor3 dx = DirX ?? Enumerable.Range(0, Contorno.Count).Select(i => Contorno[(i + 1) % Contorno.Count] - Contorno[i]).OrderByDescending(v => v.Modulo).First();
        Ex = new Vetor3(dx.X, dx.Y, 0).Unitario; Ey = Vetor3.EixoZ.Vetorial(Ex).Unitario;
        var loc = Contorno.Select(Local).ToList();
        double x0 = loc.Min(p => p.X), y0 = loc.Min(p => p.Y), x1 = loc.Max(p => p.X), y1 = loc.Max(p => p.Y);
        Origem = Mundo(x0, y0); loc = Contorno.Select(Local).ToList();
        Nx = Math.Max(2, (int)Math.Ceiling((x1 - x0) / Malha)); Ny = Math.Max(2, (int)Math.Ceiling((y1 - y0) / Malha));
        Sx = (x1 - x0) / Nx; Sy = (y1 - y0) / Ny;
        _dentro = new bool[Nx + 1, Ny + 1];
        for (int i = 0; i <= Nx; i++) for (int j = 0; j <= Ny; j++) _dentro[i, j] = DentroPoligono(loc, i * Sx, j * Sy, 0.01 * Math.Min(Sx, Sy));
        _apoios.Clear();
        if (ApoiarBordas) return;
        for (int i = 0; i <= Nx; i++)
            for (int j = 0; j <= Ny; j++)
            {
                if (!_dentro[i, j]) continue;
                Vetor3 p = Ponto(i, j);
                // pilar sob o nó (dentro da seção, em planta, e a laje dentro da altura do pilar)
                var pil = pilares.FirstOrDefault(pl =>
                {
                    Vetor3 d = p - pl.Base; double z = d.Ponto(pl.Eixo.Unitario);
                    return z > pl.Embutimento && z <= pl.Comprimento + Espessura &&
                           Math.Abs(d.Ponto(pl.DirY)) <= pl.Secao.B / 2 + 1 && Math.Abs(d.Ponto(pl.DirZ)) <= pl.Secao.H / 2 + 1;
                });
                if (pil != null) { pil.Niveis.Add(Math.Min(pil.Comprimento, (p - pil.Base).Ponto(pil.Eixo.Unitario))); _apoios.Add((i, j, null, 0, pil)); continue; }
                // viga sob o nó: projeção no eixo em planta, dentro da largura (+ meia malha nas bordas) e entre os apoios
                foreach (var v in vigas)
                {
                    Vetor3 d = new Vetor3(p.X - v.ApoioI.X, p.Y - v.ApoioI.Y, 0), dir = new Vetor3(v.Direcao.X, v.Direcao.Y, 0).Unitario;
                    double t = d.Ponto(dir), dist = Math.Abs(d.Ponto(Vetor3.EixoZ.Vetorial(dir)));
                    double vaoPlanta = new Vetor3(v.ApoioJ.X - v.ApoioI.X, v.ApoioJ.Y - v.ApoioI.Y, 0).Modulo;
                    if (t < -1 || t > vaoPlanta + 1 || dist > v.Secao.B / 2 + 1) continue;
                    double tv = t / Math.Max(1e-9, vaoPlanta) * v.Vao;
                    v.PontosInternos.Add(tv);
                    _apoios.Add((i, j, v, tv, null));
                    break;
                }
            }
    }

    /// <summary>2º passo: nós, barras, apoios nas vigas/pilares e cargas.</summary>
    internal void Gerar(ModeloBarras m, CasoCarga pp, CasoCarga g, CasoCarga q)
    {
        BarrasX.Clear(); BarrasY.Clear();
        Nos = new int[Nx + 1, Ny + 1];
        for (int i = 0; i <= Nx; i++)
            for (int j = 0; j <= Ny; j++)
            {
                Nos[i, j] = -1;
                if (!_dentro[i, j]) continue;
                Vetor3 p = Ponto(i, j);
                Nos[i, j] = m.NovoNo(p.X, p.Y, p.Z, Dono, $"{Dono} ({i},{j})");
            }
        var mat = new MaterialBarra { E = Material.E, G = Material.G, Peso = 0, Descricao = Material.Descricao };
        SecaoBarra Faixa(double b) => new SecaoBarra { A = b * Espessura, Iy = b * Math.Pow(Espessura, 3) / 12, Iz = Espessura * Math.Pow(b, 3) / 12, J = b * Math.Pow(Espessura, 3) / 6, Descricao = $"faixa {b:0.#}×{Espessura:0.#}" };
        bool Celula(int i, int j) => i >= 0 && j >= 0 && i < Nx && j < Ny && Nos[i, j] >= 0 && Nos[i + 1, j] >= 0 && Nos[i, j + 1] >= 0 && Nos[i + 1, j + 1] >= 0;
        for (int i = 0; i <= Nx; i++)
            for (int j = 0; j <= Ny; j++)
            {
                if (Nos[i, j] < 0) continue;
                // largura de influência de cada faixa: meia malha de cada lado que tem célula
                if (i < Nx && Nos[i + 1, j] >= 0)
                {
                    double b = (Celula(i, j - 1) ? Sy / 2 : 0) + (Celula(i, j) ? Sy / 2 : 0);
                    if (b > 0) { var bx = m.Barra(Nos[i, j], Nos[i + 1, j], Faixa(b), mat, Dono, $"{Dono} x({i},{j})"); bx.VetorXZ = Vetor3.EixoZ; BarrasX.Add(bx); }
                }
                if (j < Ny && Nos[i, j + 1] >= 0)
                {
                    double b = (Celula(i - 1, j) ? Sx / 2 : 0) + (Celula(i, j) ? Sx / 2 : 0);
                    if (b > 0) { var by = m.Barra(Nos[i, j], Nos[i, j + 1], Faixa(b), mat, Dono, $"{Dono} y({i},{j})"); by.VetorXZ = Vetor3.EixoZ; BarrasY.Add(by); }
                }
            }
        // cargas nos nós pela área de influência (cada célula dá ¼ da área a cada canto)
        var area = new double[Nx + 1, Ny + 1];
        for (int i = 0; i < Nx; i++) for (int j = 0; j < Ny; j++)
            if (Celula(i, j)) { double a = Sx * Sy / 4; area[i, j] += a; area[i + 1, j] += a; area[i, j + 1] += a; area[i + 1, j + 1] += a; }
        double qpp = PesoEspecifico * Espessura / 100;   // kN/m²
        for (int i = 0; i <= Nx; i++) for (int j = 0; j <= Ny; j++)
        {
            if (Nos[i, j] < 0 || area[i, j] <= 0) continue;
            double a = area[i, j] / 1e4;   // m²
            if (qpp != 0) pp.Nodais.Add((Nos[i, j], new double[] { 0, 0, -qpp * a, 0, 0, 0 }));
            if (G != 0) g.Nodais.Add((Nos[i, j], new double[] { 0, 0, -G * a, 0, 0, 0 }));
            if (Q != 0) q.Nodais.Add((Nos[i, j], new double[] { 0, 0, -Q * a, 0, 0, 0 }));
        }
        // apoios
        var tr = new[] { true, true, true, false, false, false };
        if (ApoiarBordas)
        {
            for (int i = 0; i <= Nx; i++) for (int j = 0; j <= Ny; j++)
                if (Nos[i, j] >= 0 && Borda(i, j)) m.Apoio(Nos[i, j], tr, Dono);
            return;
        }
        var apoiados = new HashSet<(int, int)>();
        foreach (var (i, j, v, t, pil) in _apoios)
        {
            int mestre = v != null ? v.NoNaPosicao(t) : pil.NoNoNivel((Ponto(i, j) - pil.Base).Ponto(pil.Eixo.Unitario));
            m.Rigido(mestre, Nos[i, j], Dono, $"{Dono} apoio ({i},{j})", tr);
            apoiados.Add((i, j));
        }
        NosLivresNaBorda = 0;
        for (int i = 0; i <= Nx; i++) for (int j = 0; j <= Ny; j++) if (Nos[i, j] >= 0 && Borda(i, j) && !apoiados.Contains((i, j))) NosLivresNaBorda++;
    }

    /// <summary>
    /// Resultados por nó da grelha: flecha uz (cm), momentos por unidade de largura mx, my (kN·m/m, positivo traciona embaixo) e
    /// torção mxy (kN·m/m), média das faixas que chegam ao nó. NaN = fora da laje.
    /// </summary>
    public (double[,] Uz, double[,] Mx, double[,] My, double[,] Mxy) Resultados(ResultadoCaso r)
    {
        int nx = Nx + 1, ny = Ny + 1;
        var uz = new double[nx, ny]; var mx = new double[nx, ny]; var my = new double[nx, ny]; var mt = new double[nx, ny];
        var cx = new int[nx, ny]; var cy = new int[nx, ny];
        var idx = new Dictionary<int, (int, int)>();
        for (int i = 0; i < nx; i++) for (int j = 0; j < ny; j++) { if (Nos[i, j] >= 0) idx[Nos[i, j]] = (i, j); uz[i, j] = Nos[i, j] >= 0 ? r.Deslocamentos[Nos[i, j]][2] : double.NaN; }
        void Soma(BarraPortico b, double[,] m, int[,] c)
        {
            double[] f = r.ForcasBarras[b.Id];
            double larg = b.Secao.A / Espessura;
            var (i0, j0) = idx[b.NoI]; var (i1, j1) = idx[b.NoJ];
            m[i0, j0] += f[4] / larg; c[i0, j0]++;          // ponta I: M interno = −f[4]; laje: m = −M/b
            m[i1, j1] += -f[10] / larg; c[i1, j1]++;
            mt[i0, j0] += Math.Abs(f[3]) / larg / 2; mt[i1, j1] += Math.Abs(f[9]) / larg / 2;
        }
        foreach (var b in BarrasX) Soma(b, mx, cx);
        foreach (var b in BarrasY) Soma(b, my, cy);
        for (int i = 0; i < nx; i++) for (int j = 0; j < ny; j++)
        {
            if (Nos[i, j] < 0) { mx[i, j] = my[i, j] = mt[i, j] = double.NaN; continue; }
            mx[i, j] = cx[i, j] > 0 ? mx[i, j] / cx[i, j] : 0;
            my[i, j] = cy[i, j] > 0 ? my[i, j] / cy[i, j] : 0;
            int n = cx[i, j] + cy[i, j]; mt[i, j] = n > 0 ? mt[i, j] / n * 2 : 0;
        }
        return (uz, mx, my, mt);
    }

    private bool Borda(int i, int j)
    {
        if (i == 0 || j == 0 || i == Nx || j == Ny) return true;
        return Nos[i - 1, j] < 0 || Nos[i + 1, j] < 0 || Nos[i, j - 1] < 0 || Nos[i, j + 1] < 0;
    }
}

/// <summary>Como a ligação aparece no modelo (D46, item 2).</summary>
public enum RepresentacaoLigacao
{
    /// <summary>Barra rígida do eixo do pilar ao topo do consolo + mola do aparelho de apoio (padrão).</summary>
    BarraRigidaEMola,
    /// <summary>Barra rígida + articulação fixa (translações presas, rotações livres, torção da viga presa).</summary>
    BarraRigidaArticulada,
}

/// <summary>Ligação viga–pilar por consolo (D46): nó do pilar no topo do consolo → vínculo rígido até o ponto de apoio → mola do aparelho → nó de apoio da viga.</summary>
public sealed class LigacaoConsolo
{
    public Dono Dono { get; set; } = new Dono { Tipo = TipoDono.Ligacao };
    public PilarObjeto Pilar { get; set; }
    public VigaObjeto Viga { get; set; }
    /// <summary>Ponta da viga que apoia neste consolo (true = I).</summary>
    public bool PontaI { get; set; }
    /// <summary>Altura do topo do consolo a partir da base do pilar (cm).</summary>
    public double ZTopoConsolo { get; set; }
    /// <summary>Aparelho de apoio (D36): tipo e medidas (cm); null = sem informação (articulação fixa).</summary>
    public ApoioPeca Aparelho { get; set; }
    public RepresentacaoLigacao Representacao { get; set; } = RepresentacaoLigacao.BarraRigidaEMola;

    // gerado
    public int NoPilar { get; private set; }
    public int NoConsolo { get; private set; }
    public MolaLigacao Mola { get; private set; }
    public string Observacao { get; private set; } = "";

    /// <summary>Direção da viga, do apoio desta ponta para o outro (horizontal).</summary>
    public Vetor3 DirecaoViga
    {
        get { Vetor3 d = (PontaI ? Viga.ApoioJ - Viga.ApoioI : Viga.ApoioI - Viga.ApoioJ); return new Vetor3(d.X, d.Y, 0).Unitario; }
    }

    /// <summary>Rigidez do aparelho nos eixos x' (ao longo da viga), y' (transversal), z' (vertical): translações e rotações.</summary>
    public static double[] Rigidez(ApoioPeca a, double gKNcm2, RepresentacaoLigacao rep, out string obs)
    {
        const double Rigido = 1e8, Garfo = 1e10;
        bool elastomero = rep == RepresentacaoLigacao.BarraRigidaEMola && a != null && a.Tipo == ApoioPeca.Neoprene &&
                          a.Comprimento > 0 && a.Largura > 0 && a.Espessura > 0;
        if (!elastomero)
        {
            obs = a == null ? "sem aparelho informado: articulação fixa" :
                  a.Tipo == ApoioPeca.Neoprene ? "elastômero sem medidas: articulação fixa" : $"apoio {a.NomeTipo}: articulação fixa";
            return new[] { Rigido, Rigido, Rigido, Garfo, 0, 0 };
        }
        double la = a.Comprimento, lb = a.Largura, t = a.Espessura, area = la * lb;
        double kh = gKNcm2 * area / t;
        double s = area / (2 * t * (la + lb));          // fator de forma
        double ec = 5 * gKNcm2 * s * s;                  // módulo de compressão aparente (El Debs 2017, 3.3)
        double kv = ec * area / t;
        double kRot = ec * lb * Math.Pow(la, 3) / 12 / t;   // giro da viga no plano vertical dela
        double kYaw = gKNcm2 * area * (la * la + lb * lb) / 12 / t;
        obs = $"elastômero {la:0.#}×{lb:0.#}×{t:0.#} cm: Kh = {kh:0.#} kN/cm, Kv = {kv:0} kN/cm";
        return new[] { kh, kh, kv, Garfo, kRot, kYaw };
    }

    internal void Gerar(ModeloBarras m, double gKNcm2)
    {
        NoPilar = Pilar.NoNoNivel(ZTopoConsolo);
        int noApoioViga = PontaI ? Viga.NoApoioI : Viga.NoApoioJ;
        var pv = m.Nos[noApoioViga];
        // topo do consolo sob o aparelho: o mesmo ponto em planta, na altura do consolo
        Vetor3 topo = Pilar.Ponto(ZTopoConsolo);
        NoConsolo = m.NovoNo(pv.X, pv.Y, topo.Z, Dono, Dono + " topo do consolo");
        m.Rigido(NoPilar, NoConsolo, Dono, Dono + " consolo");
        Mola = m.Mola(NoConsolo, noApoioViga, Rigidez(Aparelho, gKNcm2, Representacao, out string obs), Dono, Dono + " aparelho");
        Mola.EixoX = DirecaoViga;
        Mola.VetorXZ = Vetor3.EixoZ;
        Observacao = obs;
    }
}

/// <summary>Esforços de uma ligação por combinação: o que a viga põe no consolo.</summary>
public sealed class EsforcoLigacao
{
    public string Combinacao { get; set; } = "";
    /// <summary>Força vertical para baixo no consolo (kN).</summary>
    public double V { get; set; }
    /// <summary>Força horizontal ao longo da viga (kN; + puxa o consolo para a viga) e transversal.</summary>
    public double H { get; set; }
    public double Ht { get; set; }
}

/// <summary>Esforços num nível do pilar (kN, kN·cm): N de compressão positivo; momentos nos eixos y e z da seção.</summary>
public sealed class EsforcoPilar
{
    public string Combinacao { get; set; } = "";
    public double Z { get; set; }
    public double N { get; set; }
    public double Vy { get; set; }
    public double Vz { get; set; }
    public double My { get; set; }
    public double Mz { get; set; }
}

/// <summary>Combinação (NBR 8681): nome, tipo e fatores por caso.</summary>
public sealed class CombinacaoCasos
{
    public string Nome { get; set; } = "";
    public string Tipo { get; set; } = "ELU";
    public List<(string Caso, double Fator)> Fatores { get; } = new List<(string, double)>();
}

/// <summary>A estrutura montada por objetos: gera o modelo, analisa, combina e devolve os esforços a cada objeto.</summary>
public sealed class EstruturaObjetos
{
    public const string CasoPP = "PP", CasoG = "G", CasoQ = "Q";
    public List<PilarObjeto> Pilares { get; } = new List<PilarObjeto>();
    public List<VigaObjeto> Vigas { get; } = new List<VigaObjeto>();
    public List<LigacaoConsolo> Ligacoes { get; } = new List<LigacaoConsolo>();
    public List<LigacaoPilarViga> LigacoesDiretas { get; } = new List<LigacaoPilarViga>();
    public List<LajeObjeto> Lajes { get; } = new List<LajeObjeto>();
    public ModeloBarras Modelo { get; private set; }
    public Dictionary<string, ResultadoCaso> Casos { get; private set; }
    public Dictionary<string, ResultadoCaso> Combinacoes { get; } = new Dictionary<string, ResultadoCaso>();
    public List<CombinacaoCasos> DefinicaoCombinacoes { get; } = new List<CombinacaoCasos>();
    public List<string> Avisos { get; } = new List<string>();
    private SolverBarras _solver;

    /// <summary>Gera o modelo de barras a partir dos objetos (pilares antes, para os nós dos níveis existirem).</summary>
    public ModeloBarras Gerar(ConfiguracaoProjeto cfg = null)
    {
        cfg ??= new ConfiguracaoProjeto();
        var m = new ModeloBarras();
        var pp = m.Caso(CasoPP, NaturezaCaso.PesoProprio); pp.PesoProprio = true;
        var g = m.Caso(CasoG, NaturezaCaso.Permanente);
        var q = m.Caso(CasoQ, NaturezaCaso.Variavel);
        foreach (var l in Ligacoes) if (!l.Pilar.Niveis.Contains(l.ZTopoConsolo)) l.Pilar.Niveis.Add(l.ZTopoConsolo);
        foreach (var l in LigacoesDiretas) l.Pilar.Niveis.Add(l.ZNivel);
        foreach (var v in Vigas) v.PontosInternos.Clear();
        foreach (var la in Lajes) la.Preparar(Vigas, Pilares);
        foreach (var p in Pilares) p.Gerar(m);
        foreach (var v in Vigas) v.Gerar(m, g, q);
        foreach (var l in LigacoesDiretas) l.Gerar(m);
        foreach (var la in Lajes)
        {
            la.Gerar(m, pp, g, q);
            if (la.NosLivresNaBorda > 0) Avisos.Add($"{la.Dono}: {la.NosLivresNaBorda} nós da borda sem viga nem pilar embaixo (borda livre).");
        }
        double gEl = cfg.Numero("acoes.gElastomero") / 10;   // MPa → kN/cm²
        foreach (var l in Ligacoes)
        {
            l.Gerar(m, gEl);
            if (l.Observacao.Contains("articulação")) Avisos.Add($"{l.Dono}: {l.Observacao}.");
        }
        // viga sem ligação numa ponta: apoio articulado fixo no próprio nó (não fica solta)
        foreach (var v in Vigas)
            foreach (bool i in new[] { true, false })
                if (!Ligacoes.Any(l => l.Viga == v && l.PontaI == i) && !LigacoesDiretas.Any(l => l.Viga == v && l.PontaI == i))
                {
                    m.Apoio(i ? v.NoApoioI : v.NoApoioJ, new[] { true, true, true, true, false, false }, v.Dono);
                    Avisos.Add($"{v.Dono}: ponta {(i ? "I" : "J")} sem consolo ligado — apoio articulado fixo.");
                }
        Modelo = m;
        DefinicaoCombinacoes.Clear();
        DefinicaoCombinacoes.AddRange(Combinacoes8681(cfg));
        return m;
    }

    /// <summary>
    /// Combinações PROVISÓRIAS (o usuário deixou as combinações para depois, 24/11): ELU normal com o γ da configuração
    /// (permanentes desfavoráveis e favoráveis), ELS frequente (ψ1) e quase permanente (ψ2), com uma só ação variável.
    /// </summary>
    public static List<CombinacaoCasos> Combinacoes8681(ConfiguracaoProjeto cfg)
    {
        double gg = cfg.Numero("normas.gamaG"), gq = cfg.Numero("normas.gamaQ"), p1 = cfg.Numero("normas.psi1"), p2 = cfg.Numero("normas.psi2");
        CombinacaoCasos C(string nome, string tipo, double fg, double fq)
        {
            var c = new CombinacaoCasos { Nome = nome, Tipo = tipo };
            c.Fatores.Add((CasoPP, fg)); c.Fatores.Add((CasoG, fg)); if (fq != 0) c.Fatores.Add((CasoQ, fq));
            return c;
        }
        return new List<CombinacaoCasos>
        {
            C($"ELU 1: {gg:0.##}·G + {gq:0.##}·Q", "ELU", gg, gq),
            C($"ELU 2: 1,0·G + {gq:0.##}·Q", "ELU", 1.0, gq),
            C($"ELU 3: {gg:0.##}·G", "ELU", gg, 0),
            C($"ELS freq.: G + {p1:0.##}·Q", "ELS", 1.0, p1),
            C($"ELS q. perm.: G + {p2:0.##}·Q", "ELS", 1.0, p2),
        };
    }

    /// <summary>Gera (se preciso), analisa os casos e monta as combinações.</summary>
    public void Analisar(ConfiguracaoProjeto cfg = null)
    {
        if (Modelo == null) Gerar(cfg);
        _solver = new SolverBarras(Modelo);
        Casos = _solver.Resolver();
        Combinacoes.Clear();
        foreach (var c in DefinicaoCombinacoes)
            Combinacoes[c.Nome] = ResultadoCaso.Combinar(c.Nome, c.Fatores.Select(f => (Casos[f.Caso], f.Fator)));
    }

    /// <summary>O que a viga põe no consolo, num resultado (caso ou combinação): mola nos eixos dela (x' ao longo da viga, z' vertical).</summary>
    public EsforcoLigacao Esforco(LigacaoConsolo l, ResultadoCaso r)
    {
        double[] f = r.ForcasMolas[l.Mola.Id];   // F = k·(u_viga − u_consolo): o que o consolo recebe é +F
        return new EsforcoLigacao { Combinacao = r.Nome, V = -f[2], H = f[0], Ht = f[1] };
    }

    /// <summary>
    /// Carga do consolo para o cálculo (D44 etapa 1): Vg e Hg dos casos permanentes, Vq e Hq do variável — o cálculo do consolo
    /// aplica γn e γf da configuração (NBR 9062 7.3.1.1), com o mínimo de Hd.
    /// </summary>
    public CargaConsolo CargaDoConsolo(LigacaoConsolo l)
    {
        var pp = Esforco(l, Casos[CasoPP]); var g = Esforco(l, Casos[CasoG]); var q = Esforco(l, Casos[CasoQ]);
        return new CargaConsolo
        {
            Vg = pp.V + g.V, Vq = Math.Max(0, q.V), Hg = pp.H + g.H, Hq = q.H,
            Origem = $"análise por objetos: {l.Viga.Dono}, ponta {(l.PontaI ? "I" : "J")} ({l.Observacao})",
        };
    }

    /// <summary>Esforços do pilar nos nós (abaixo e acima de cada nível), num resultado: eixos da seção (y = DirY, z = DirZ).</summary>
    public List<EsforcoPilar> Esforcos(PilarObjeto p, ResultadoCaso r)
    {
        var l = new List<EsforcoPilar>();
        foreach (var b in p.Barras)
        {
            double[] f = r.ForcasBarras[b.Id];
            double zi = p.Nos.First(n => n.No == b.NoI).Z, zj = p.Nos.First(n => n.No == b.NoJ).Z;
            // ponta I: esforço interno = −(força sobre a barra); ponta J: = +(força sobre a barra)
            l.Add(new EsforcoPilar { Combinacao = r.Nome, Z = zi, N = f[0], Vy = -f[1], Vz = -f[2], My = -f[4], Mz = -f[5] });
            l.Add(new EsforcoPilar { Combinacao = r.Nome, Z = zj, N = -f[6], Vy = f[7], Vz = f[8], My = f[10], Mz = f[11] });
        }
        return l;
    }
}
