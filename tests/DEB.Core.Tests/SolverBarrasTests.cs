using System;
using System.Linq;
using DEB.Core.Analise.Barras;
using DEB.Core.Pecas;
using Xunit;

namespace DEB.Core.Tests;

/// <summary>Solver de barras do núcleo (D46) contra soluções fechadas da resistência dos materiais.</summary>
public class SolverBarrasTests
{
    private static void Perto(double esperado, double obtido, double tolRel = 1e-6, double tolAbs = 1e-9) =>
        Assert.True(Math.Abs(obtido - esperado) <= Math.Max(tolAbs, tolRel * Math.Abs(esperado)), $"esperado {esperado}, obtido {obtido}");

    private static readonly MaterialBarra Mat = new MaterialBarra { E = 3000, G = 1250 };
    private static readonly SecaoBarra Sec = new SecaoBarra { A = 20 * 50, Iy = 20 * Math.Pow(50, 3) / 12, Iz = 50 * Math.Pow(20, 3) / 12, J = 99805 };

    [Fact]
    public void BalancoComCargaNaPonta()
    {
        var m = new ModeloBarras();
        int a = m.No(0, 0, 0), b = m.No(300, 0, 0);
        m.Barra(a, b, Sec, Mat);
        m.Apoio(a, ApoioNo.Engaste);
        m.Caso("P").Nodais.Add((b, new double[] { 0, 0, -10, 0, 0, 0 }));
        var r = new SolverBarras(m).Resolver()["P"];
        Perto(-10 * Math.Pow(300, 3) / (3 * Mat.E * Sec.Iy), r.Deslocamentos[b][2]);
        Perto(10, r.Reacoes[a][2]);
        // reação de momento: equilibra 10 kN a 300 cm → My = −3000 (r × F = (300,0,0)×(0,0,−10) = (0, 3000, 0))
        Perto(-3000, r.Reacoes[a][4]);
    }

    [Fact]
    public void BiapoiadaComCargaUniformeEMomentoNoMeio()
    {
        var m = new ModeloBarras();
        int a = m.No(0, 0, 0), b = m.No(600, 0, 0);
        var barra = m.Barra(a, b, Sec, Mat);
        m.Apoio(a, new[] { true, true, true, true, false, false });   // torção travada numa ponta
        m.Apoio(b, new[] { false, true, true, false, false, false });
        var c = m.Caso("q");
        c.Distribuidas.Add(new CargaDistribuida { Barra = barra.Id, Q = new Vetor3(0, 0, -0.1) });
        var s = new SolverBarras(m);
        var r = s.Resolver()["q"];
        Perto(30, r.Reacoes[a][2]);
        Perto(30, r.Reacoes[b][2]);
        // flecha no meio: 5qL⁴/(384EI), pelo esforço e pela rotação de apoio qL³/(24EI)
        Perto(0.1 * Math.Pow(600, 3) / (24 * Mat.E * Sec.Iy), Math.Abs(r.Deslocamentos[a][4]));
        var meio = s.Esforcos(r, c, barra, 300);
        Perto(-0.1 * 600 * 600 / 8, meio.My);      // convenção vetorial: My < 0 traciona embaixo (viga com z para cima)
        Perto(0, meio.Vz, 1e-6, 1e-6);
        // deformada no meio: 5qL⁴/(384EI) (Hermite com as rotações + solução particular)
        Perto(-5 * 0.1 * Math.Pow(600, 4) / (384 * Mat.E * Sec.Iy), s.Deformada(r, c, barra, 300).Z, 1e-9);
        Perto(-0.1 * 150 * (Math.Pow(600, 3) - 2 * 600 * 150 * 150 + Math.Pow(150, 3)) / (24 * Mat.E * Sec.Iy), s.Deformada(r, c, barra, 150).Z, 1e-9);
        var ponta = s.Esforcos(r, c, barra, 0.001);
        Perto(30, Math.Abs(ponta.Vz), 1e-3);
    }

    [Fact]
    public void RotulasNasPontasDaoAViga_Biapoiada()
    {
        // engastada nas duas pontas, mas com rotação ry liberada nos dois nós: flecha de biapoiada
        var m = new ModeloBarras();
        int a = m.No(0, 0, 0), meio = m.No(300, 0, 0), b = m.No(600, 0, 0);
        var b1 = m.Barra(a, meio, Sec, Mat); var b2 = m.Barra(meio, b, Sec, Mat);
        b1.LiberaI[4] = true; b2.LiberaJ[4] = true;
        m.Apoio(a, ApoioNo.Engaste); m.Apoio(b, ApoioNo.Engaste);
        m.Caso("P").Nodais.Add((meio, new double[] { 0, 0, -20, 0, 0, 0 }));
        var r = new SolverBarras(m).Resolver()["P"];
        Perto(-20 * Math.Pow(600, 3) / (48 * Mat.E * Sec.Iy), r.Deslocamentos[meio][2]);
        Perto(0, r.Reacoes[a][4], 1e-6, 1e-6);
        Perto(0, r.ForcasBarras[b1.Id][4], 1e-6, 1e-6);
    }

    [Fact]
    public void PilarUsaOsDoisEixosDaSecao()
    {
        // pilar vertical: eixo local z = X global (vecxz padrão), então Fx flete em torno de Iy e Fy em torno de Iz
        var m = new ModeloBarras();
        int a = m.No(0, 0, 0), b = m.No(0, 0, 400);
        m.Barra(a, b, Sec, Mat);
        m.Apoio(a, ApoioNo.Engaste);
        m.Caso("X").Nodais.Add((b, new double[] { 5, 0, 0, 0, 0, 0 }));
        m.Caso("Y").Nodais.Add((b, new double[] { 0, 5, 0, 0, 0, 0 }));
        var r = new SolverBarras(m).Resolver();
        Perto(5 * Math.Pow(400, 3) / (3 * Mat.E * Sec.Iy), r["X"].Deslocamentos[b][0]);
        Perto(5 * Math.Pow(400, 3) / (3 * Mat.E * Sec.Iz), r["Y"].Deslocamentos[b][1]);
    }

    [Fact]
    public void TorcaoDoBalanco()
    {
        var m = new ModeloBarras();
        int a = m.No(0, 0, 0), b = m.No(250, 0, 0);
        var barra = m.Barra(a, b, Sec, Mat);
        barra.FatorTorcao = 0.15;
        m.Apoio(a, ApoioNo.Engaste);
        m.Caso("T").Nodais.Add((b, new double[] { 0, 0, 0, 100, 0, 0 }));
        var r = new SolverBarras(m).Resolver()["T"];
        Perto(100 * 250 / (Mat.G * Sec.J * 0.15), r.Deslocamentos[b][3]);
    }

    [Fact]
    public void BarraRigidaDoConsoloLevaOMomentoDaExcentricidade()
    {
        // pilar de 300 cm; consolo: ponto de apoio a 40 cm do eixo, preso por vínculo rígido; carga de 100 kN no apoio
        var m = new ModeloBarras();
        int base0 = m.No(0, 0, 0), topo = m.No(0, 0, 300), apoio = m.NovoNo(40, 0, 300);
        m.Barra(base0, topo, Sec, Mat);
        m.Apoio(base0, ApoioNo.Engaste);
        m.Rigido(topo, apoio);
        m.Caso("V").Nodais.Add((apoio, new double[] { 0, 0, -100, 0, 0, 0 }));
        var r = new SolverBarras(m).Resolver()["V"];
        Perto(100, r.Reacoes[base0][2]);
        Perto(-4000, r.Reacoes[base0][4]);           // 100 kN × 40 cm
        // o apoio desce o encurtamento do pilar mais a rotação do topo × 40
        double encurt = 100 * 300 / (Mat.E * Sec.A);
        double rot = 4000 * 300 / (Mat.E * Sec.Iy);   // momento constante no pilar (flexão no plano x-z → Iy)
        Perto(-(encurt + rot * 40), r.Deslocamentos[apoio][2], 1e-6);
    }

    [Fact]
    public void MolaDoAparelhoDeApoio()
    {
        // viga sobre aparelho: H no nó da viga, mola horizontal k entre o nó da viga e o do consolo (engastado)
        var m = new ModeloBarras();
        int consolo = m.No(0, 0, 0), viga = m.NovoNo(0, 0, 0);
        m.Apoio(consolo, ApoioNo.Engaste);
        m.Mola(consolo, viga, new double[] { 50, 50, 1e6, 1e8, 1e8, 1e8 });
        m.Caso("H").Nodais.Add((viga, new double[] { 10, 0, 0, 0, 0, 0 }));
        var r = new SolverBarras(m).Resolver()["H"];
        Perto(10.0 / 50, r.Deslocamentos[viga][0]);
        Perto(10, r.ForcasMolas[0][0]);
        Perto(-10, r.Reacoes[consolo][0]);
    }

    [Fact]
    public void MecanismoDizONo()
    {
        var m = new ModeloBarras();
        int a = m.No(0, 0, 0), b = m.No(600, 0, 0, nome: "ponta");
        m.Barra(a, b, Sec, Mat);
        m.Apoio(a, ApoioNo.Rotula); m.Apoio(b, ApoioNo.Rotula);   // nada trava a torção
        m.Caso("q");
        var e = Assert.Throws<ErroAnalise>(() => new SolverBarras(m).Resolver());
        Assert.Contains("mecanismo", e.Message);
        Assert.Contains("Rx", e.Message);
    }

    [Fact]
    public void CombinacaoSomaOsCasos()
    {
        var m = new ModeloBarras();
        int a = m.No(0, 0, 0), b = m.No(300, 0, 0);
        m.Barra(a, b, Sec, Mat);
        m.Apoio(a, ApoioNo.Engaste);
        m.Caso("G").Nodais.Add((b, new double[] { 0, 0, -10, 0, 0, 0 }));
        m.Caso("Q").Nodais.Add((b, new double[] { 0, 0, -5, 0, 0, 0 }));
        var r = new SolverBarras(m).Resolver();
        var c = ResultadoCaso.Combinar("ELU", new[] { (r["G"], 1.4), (r["Q"], 1.4) });
        Perto(21, c.Reacoes[a][2]);
    }

    [Fact]
    public void PesoProprioPeloMaterial()
    {
        var mat = MaterialBarra.Concreto(40);
        Perto(0.9 * 5600 * Math.Sqrt(40) / 10, mat.E);   // αi = 0,8 + 0,2·40/80 = 0,9; Eci = 5600·√40 MPa
        var m = new ModeloBarras();
        int a = m.No(0, 0, 0), b = m.No(500, 0, 0);
        m.Barra(a, b, Sec, mat);
        m.Apoio(a, ApoioNo.Engaste);
        m.Caso("PP", NaturezaCaso.PesoProprio).PesoProprio = true;
        var r = new SolverBarras(m).Resolver()["PP"];
        Perto(25e-6 * 1000 * 500, r.Reacoes[a][2]);   // 25 kN/m³ × 0,1 m² × 5 m = 12,5 kN
    }

    [Fact]
    public void PropriedadesDaSecao()
    {
        // retângulo 20 × 50: J tabelado (β = 0,249 para b/t = 2,5) = 99 600 cm⁴
        var r = SecaoPoligonal.Retangulo(20, 50);
        Perto(99600, PropriedadesSecao.J(r), 0.005);
        Perto(0, PropriedadesSecao.Iyz(r), 1e-9, 1e-6);
        // quadrado 40 × 40: β = 0,1406
        Perto(0.1406 * Math.Pow(40, 4), PropriedadesSecao.J(SecaoPoligonal.Retangulo(40, 40)), 0.005);
        // L: o produto de inércia some no eixo principal
        var l = new SecaoPoligonal { Contorno = { (0, 0), (40, 0), (40, 15), (15, 15), (15, 60), (0, 60) } };
        var (i1, i2, alfa) = PropriedadesSecao.Principal(l);
        var (yg, zg) = l.CG;
        double ca = Math.Cos(alfa), sa = Math.Sin(alfa);
        var girada = new SecaoPoligonal { Contorno = l.Contorno.Select(p => ((p.Y - yg) * ca + (p.Z - zg) * sa, -(p.Y - yg) * sa + (p.Z - zg) * ca)).ToList() };
        Perto(0, PropriedadesSecao.Iyz(girada), 1e-6, 1e-3);
        Perto(i1, Math.Max(girada.Iy, girada.Iz), 1e-9);
        Perto(i2, Math.Min(girada.Iy, girada.Iz), 1e-9);
        // torção conferida com o sectionproperties 3.10 (malha de 2 cm, elementos de 6 nós)
        Perto(99747.9, PropriedadesSecao.J(r), 0.005);
        Perto(88731.3, PropriedadesSecao.J(l), 0.02);
        var t = new SecaoPoligonal { Contorno = { (15, 0), (35, 0), (35, 40), (50, 40), (50, 52), (0, 52), (0, 40), (15, 40) } };
        Perto(131426.4, PropriedadesSecao.J(t), 0.02);
        var ti = new SecaoPoligonal { Contorno = { (0, 0), (60, 0), (60, 20), (40, 20), (40, 70), (20, 70), (20, 20), (0, 20) } };
        Perto(288561.7, PropriedadesSecao.J(ti), 0.02);
        var trap = new SecaoPoligonal { Contorno = { (0, 0), (30, 0), (25, 40), (5, 40) } };
        Perto(126452.6, PropriedadesSecao.J(trap), 0.02);
        // inércias e produto da L (sectionproperties: Ixx = 411 038,6; Iyy = 142 288,6; Ixy = −119 117,65)
        Perto(411038.60, l.Iy, 1e-6); Perto(142288.60, l.Iz, 1e-6); Perto(-119117.65, PropriedadesSecao.Iyz(l), 1e-6);
    }

    /// <summary>
    /// Pórtico pré-moldado conferido no OpenSees (openseespy no Python 3.12; script em tests/DEB.Core.Tests/Gabaritos/portico_premoldado_opensees.py): dois pilares 40×40
    /// engastados, consolos por vínculo rígido (rigidLink beam) até o ponto de apoio, viga 20×50 com o eixo 25 cm acima do apoio
    /// (vínculo rígido), aparelhos como molas zeroLength (k = 50, 50, 1e5, 1e7, 10, 10), carga de 0,3 kN/cm na viga, 5 kN em X no
    /// topo do P1 e 3 kN em Y no topo do P2.
    /// </summary>
    [Fact]
    public void PorticoPreMoldadoIgualAoOpenSees()
    {
        var mat = new MaterialBarra { E = 3000, G = 1250 };
        var pil = new SecaoBarra { A = 1600, Iy = Math.Pow(40, 4) / 12, Iz = Math.Pow(40, 4) / 12, J = 360533 };
        var vig = new SecaoBarra { A = 1000, Iy = 20 * Math.Pow(50, 3) / 12, Iz = 50 * Math.Pow(20, 3) / 12, J = 99805 };
        var m = new ModeloBarras();
        double[][] xyz =
        {
            new double[] { 0, 0, 0 }, new double[] { 0, 0, 350 }, new double[] { 0, 0, 400 },
            new double[] { 600, 0, 0 }, new double[] { 600, 0, 350 }, new double[] { 600, 0, 400 },
            new double[] { 25, 0, 350 }, new double[] { 575, 0, 350 }, new double[] { 25, 0, 350 }, new double[] { 575, 0, 350 },
            new double[] { 25, 0, 375 }, new double[] { 575, 0, 375 },
        };
        foreach (var p in xyz) m.NovoNo(p[0], p[1], p[2]);
        int N(int opensees) => opensees - 1;
        m.Apoio(N(1), ApoioNo.Engaste); m.Apoio(N(4), ApoioNo.Engaste);
        m.Barra(N(1), N(2), pil, mat); m.Barra(N(2), N(3), pil, mat); m.Barra(N(4), N(5), pil, mat); m.Barra(N(5), N(6), pil, mat);
        var viga = m.Barra(N(11), N(12), vig, mat);
        m.Rigido(N(2), N(7)); m.Rigido(N(5), N(8)); m.Rigido(N(11), N(9)); m.Rigido(N(12), N(10));
        double[] k = { 50, 50, 1e5, 1e7, 10, 10 };
        m.Mola(N(7), N(9), (double[])k.Clone()); m.Mola(N(8), N(10), (double[])k.Clone());
        var c = m.Caso("1");
        c.Distribuidas.Add(new CargaDistribuida { Barra = viga.Id, Q = new Vetor3(0, 0, -0.3) });
        c.Nodais.Add((N(3), new double[] { 5, 0, 0, 0, 0, 0 }));
        c.Nodais.Add((N(6), new double[] { 0, 3, 0, 0, 0, 0 }));
        var r = new SolverBarras(m).Resolver()["1"];

        void Vet(double[] esperado, double[] obtido, double tol)
        {
            for (int i = 0; i < esperado.Length; i++) Perto(esperado[i], obtido[i], tol, 1e-5 * Math.Max(1, esperado.Max(Math.Abs)));   // o OpenSees imprime 6 casas
        }
        Vet(new[] { 3.158978, -0.000005, 82.499988, 64.736550, -1206.880005, -0.001509 }, r.Reacoes[N(1)], 1e-4);
        Vet(new[] { -8.158978, -2.999995, 82.500012, 1135.263450, -793.112674, -0.001509 }, r.Reacoes[N(4)], 1e-4);
        Vet(new[] { 1.99215672e-01, 7.96554995e-03, -6.01562411e-03 }, r.Deslocamentos[N(3)].Take(3).ToArray(), 1e-4);
        Vet(new[] { 6.63423439e-02, 8.33337130e-02, -1.55182800e-02 }, r.Deslocamentos[N(12)].Take(3).ToArray(), 1e-4);
        Vet(new[] { 8.158978, -0.000005, 82.499988, 64.734664, -203.996915, -0.001383, -8.158978, 0.000005, 82.500012, -64.734664, 204.003626, -0.001383 },
            r.ForcasBarras[viga.Id], 1e-4);
        Perto(-8.158978, r.ForcasMolas[0][0], 1e-4);   // OpenSees dá a força resistente no nó I (k·(uI − uJ)); aqui F = k·(uJ − uI)
    }
}
