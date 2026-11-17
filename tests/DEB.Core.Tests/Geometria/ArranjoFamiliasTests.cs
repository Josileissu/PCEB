using System;
using System.Collections.Generic;
using System.Linq;
using DEB.Core.Calculo;
using DEB.Core.Configuracao;
using DEB.Core.Geometria.Secao;
using DEB.Core.Motor;
using Xunit;

namespace DEB.Core.Tests.Geometria;

/// <summary>
/// Arranjo da armadura por família (D31): barras de canto de cada estribo, estribos por parte, pele da I, trapezoidal pela
/// normal. Em todos os casos φt = 8 mm (t = 0,8 cm) e φ canto = 20 mm (φ/2 = 1,0 cm): barra de canto a c + 1,8 cm das faces
/// do estribo; eixo do estribo a c + 0,4 cm.
/// </summary>
public class ArranjoFamiliasTests
{
    private const double Tol = 1e-9;
    private static readonly ConfiguracaoArmadura Cfg = new ConfiguracaoArmadura();

    private static ArranjoSecao Arranjo(SecaoTransversal s, Dictionary<ParteSecao, double> cob, int nFaceB = 0, int nFaceH = 0, double phiFace = 10, int porCanto = 1) =>
        SecaoBarras.Posicoes(s, cob, 8, 20, porCanto, 0, 20, nFaceB, nFaceH, phiFace);

    private static void Ponto(double x, double y, BarraNaSecao b) { Assert.Equal(x, b.X, 6); Assert.Equal(y, b.Y, 6); }
    private static void Ponto(double x, double y, (double X, double Y) p) { Assert.Equal(x, p.X, 6); Assert.Equal(y, p.Y, 6); }

    // ------------------------------------------------------------------ T

    private static Dictionary<ParteSecao, double> CobT() => new Dictionary<ParteSecao, double>
    {
        [ParteSecao.Topo] = 3, [ParteSecao.Fundo] = 2.5, [ParteSecao.Esquerda] = 3.5, [ParteSecao.Direita] = 4,
        [ParteSecao.LateralAlmaEsquerda] = 3, [ParteSecao.LateralAlmaDireita] = 3, [ParteSecao.Mesa] = 2,
    };

    [Fact]
    public void TCanonica()
    {
        // T bw 20, h 60, hf 15, ae 20, ad 20 → b = 60, fundo da mesa em z = 45.
        // alma (y 20…40, altura toda): x = 20 + 3 + 1,8 = 24,8 e 40 − 3 − 1,8 = 35,2; y = 2,5 + 1,8 = 4,3 e 60 − 3 − 1,8 = 55,2
        // mesa (y 0…60, z 45…60): x = 3,5 + 1,8 = 5,3 e 60 − 4 − 1,8 = 54,2; y = 45 + 2 + 1,8 = 48,8 e 55,2
        var a = Arranjo(SecaoTransversal.T(20, 60, 15, 20, 20), CobT());
        Assert.True(a.Definido, a.Erro);
        Assert.Equal(8, a.Barras.Count);
        Ponto(24.8, 4.3, a.Barras[0]); Ponto(35.2, 4.3, a.Barras[1]); Ponto(35.2, 55.2, a.Barras[2]); Ponto(24.8, 55.2, a.Barras[3]);
        Ponto(5.3, 48.8, a.Barras[4]); Ponto(54.2, 48.8, a.Barras[5]); Ponto(54.2, 55.2, a.Barras[6]); Ponto(5.3, 55.2, a.Barras[7]);
        Assert.All(a.Barras.Take(4), b => Assert.Equal(ParteSecao.Alma, b.Parte));
        Assert.All(a.Barras.Skip(4), b => Assert.Equal(ParteSecao.Mesa, b.Parte));
        Assert.All(a.Barras, b => { Assert.Equal("canto", b.Grupo); Assert.Equal(20, b.Bitola); });
        // 4 barras na face de cima: as 2 da alma e as 2 das pontas da mesa
        Assert.Equal(4, a.Barras.Count(b => Math.Abs(b.Y - 55.2) < Tol));

        // estribos: alma (23,4; 2,9)–(36,6; 56,6); mesa (3,9; 47,4)–(55,6; 56,6)
        Assert.Equal(new ParteSecao?[] { ParteSecao.Alma, ParteSecao.Mesa }, a.Estribos.Select(e => e.Parte));
        var al = a.Estribos[0].Eixo; Ponto(23.4, 2.9, al[0]); Ponto(36.6, 2.9, al[1]); Ponto(36.6, 56.6, al[2]); Ponto(23.4, 56.6, al[3]);
        var me = a.Estribos[1].Eixo; Ponto(3.9, 47.4, me[0]); Ponto(55.6, 47.4, me[1]); Ponto(55.6, 56.6, me[2]); Ponto(3.9, 56.6, me[3]);
        Assert.Empty(a.Avisos);
        Conferir(SecaoTransversal.T(20, 60, 15, 20, 20), a);
    }

    [Fact]
    public void TInvertidaEhOEspelhoDaT()
    {
        // invertida: o "Topo" real é o topo da alma (z = 60 − 0); a barra da alma de baixo-esquerda (24,8; 4,3) vai para (24,8; 55,7)
        var s = SecaoTransversal.T(20, 60, 15, 20, 20, invertida: true);
        var a = Arranjo(s, Renomear(CobT(), false, true));
        Ponto(24.8, 60 - 4.3, a.Barras[0]);
        Ponto(5.3, 60 - 48.8, a.Barras[4]);
        Assert.Equal(ParteSecao.Mesa, a.Barras[4].Parte);
        Espelho(SecaoTransversal.T(20, 60, 15, 20, 20), CobT(), s, false, true);
        Espelho(SecaoTransversal.T(20, 60, 15, 20, 20), CobT(),
                SecaoTransversal.DaFamilia(FamiliasSecao.T, Valores(SecaoTransversal.T(20, 60, 15, 20, 20)), new OrientacaoSecao(true, false)), true, false);
    }

    [Fact]
    public void TCobrimentoInternoPelaRegraProvisoria()
    {
        // sem as laterais da alma nem a mesa: lateral da alma = Fundo (2,5); face interna da mesa = maior de Esquerda/Direita (4)
        var cob = CobT();
        cob.Remove(ParteSecao.LateralAlmaEsquerda); cob.Remove(ParteSecao.LateralAlmaDireita); cob.Remove(ParteSecao.Mesa);
        var a = Arranjo(SecaoTransversal.T(20, 60, 15, 20, 20), cob);
        Assert.True(a.Definido);
        Ponto(20 + 2.5 + 1.8, 4.3, a.Barras[0]);
        Ponto(5.3, 45 + 4 + 1.8, a.Barras[4]);
        Assert.Equal(3, a.Avisos.Count(x => x.Contains("regra provisória")));
        Assert.Contains(a.Avisos, x => x.Contains("face interna da mesa") && x.Contains("maior de Esquerda, Direita"));
    }

    // ------------------------------------------------------------------ L

    private static Dictionary<ParteSecao, double> CobL() => new Dictionary<ParteSecao, double>
    {
        [ParteSecao.Topo] = 3, [ParteSecao.Fundo] = 3, [ParteSecao.Esquerda] = 3, [ParteSecao.Direita] = 2.5,
        [ParteSecao.LateralAlmaDireita] = 3, [ParteSecao.Aba] = 2,
    };

    [Fact]
    public void LCanonica()
    {
        // L bw 25, h 60, hf 20, ba 30 → b = 55.
        // alma (y 0…25, altura toda): x = 3 + 1,8 = 4,8 e 25 − 3 − 1,8 = 20,2; y = 4,8 e 60 − 4,8 = 55,2
        // aba (y 0…55, z 0…20): x = 4,8 e 55 − 2,5 − 1,8 = 50,7; y = 4,8 e 20 − 2 − 1,8 = 16,2; o canto (4,8; 4,8) é comum → 7 barras
        var s = SecaoTransversal.L(25, 60, 20, 30);
        var a = Arranjo(s, CobL());
        Assert.True(a.Definido, a.Erro);
        Assert.Equal(7, a.Barras.Count);
        Ponto(4.8, 4.8, a.Barras[0]); Ponto(20.2, 4.8, a.Barras[1]); Ponto(20.2, 55.2, a.Barras[2]); Ponto(4.8, 55.2, a.Barras[3]);
        Ponto(50.7, 4.8, a.Barras[4]); Ponto(50.7, 16.2, a.Barras[5]); Ponto(4.8, 16.2, a.Barras[6]);
        Assert.Equal(ParteSecao.Alma, a.Barras[0].Parte);
        Assert.All(a.Barras.Skip(4), b => Assert.Equal(ParteSecao.Aba, b.Parte));
        // estribos: alma (3,4; 3,4)–(21,6; 56,6); aba (3,4; 3,4)–(52,1; 17,6)
        var al = a.Estribos[0].Eixo; Ponto(3.4, 3.4, al[0]); Ponto(21.6, 56.6, al[2]);
        var ab = a.Estribos[1].Eixo; Ponto(3.4, 3.4, ab[0]); Ponto(52.1, 17.6, ab[2]);
        Assert.Equal(ParteSecao.Aba, a.Estribos[1].Parte);
        Assert.Empty(a.Avisos);
        Conferir(s, a);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void LNasQuatroOrientacoesEhOEspelho(bool eh, bool ev)
    {
        var canonica = SecaoTransversal.L(25, 60, 20, 30);
        var s = SecaoTransversal.L(25, 60, 20, 30, new OrientacaoSecao(eh, ev));
        var a = Espelho(canonica, CobL(), s, eh, ev);
        // ex.: aba em cima saindo à esquerda (os dois espelhos): a ponta da aba de baixo (50,7; 4,8) vai para (4,3; 55,2)
        if (eh && ev) Ponto(55 - 50.7, 60 - 4.8, a.Barras[4]);
        Conferir(s, a);
    }

    // ------------------------------------------------------------------ I

    private static Dictionary<ParteSecao, double> CobI() => new Dictionary<ParteSecao, double>
    {
        [ParteSecao.Topo] = 3, [ParteSecao.Fundo] = 3, [ParteSecao.LateralAlmaEsquerda] = 2.5, [ParteSecao.LateralAlmaDireita] = 2.5,
        [ParteSecao.MesaSuperior] = 2.5, [ParteSecao.MesaInferior] = 3.5,
    };

    [Fact]
    public void ICanonicaComPele()
    {
        // I bw 12, h 80, hs 15, ase = asd = 19, hi 20, aie = aid = 14 → alma em y 19…31, mesa sup. y 0…50 (z 65…80), mesa inf. y 5…45 (z 0…20).
        // alma (altura toda): x = 19 + 2,5 + 1,8 = 23,3 e 31 − 2,5 − 1,8 = 26,7; y = 3 + 1,8 = 4,8 e 80 − 4,8 = 75,2
        // mesa sup.: x = 2,5 + 1,8 = 4,3 e 50 − 4,3 = 45,7; y = 65 + 2,5 + 1,8 = 69,3 e 75,2
        // mesa inf.: x = 5 + 3,5 + 1,8 = 10,3 e 45 − 5,3 = 39,7; y = 4,8 e 20 − 3,5 − 1,8 = 14,7
        // pele 2 por face, ø10: x = 19 + 2,5 + 0,8 + 0,5 = 22,8 e 31 − 3,8 = 27,2; z entre 14,7 e 69,3 → 14,7 + 54,6/3 = 32,9 e 51,1
        var s = SecaoTransversal.I(12, 80, 15, 19, 19, 20, 14, 14);
        var a = Arranjo(s, CobI(), nFaceH: 2, phiFace: 10);
        Assert.True(a.Definido, a.Erro);
        Assert.Equal(16, a.Barras.Count);
        Ponto(23.3, 4.8, a.Barras[0]); Ponto(26.7, 4.8, a.Barras[1]); Ponto(26.7, 75.2, a.Barras[2]); Ponto(23.3, 75.2, a.Barras[3]);
        Ponto(4.3, 69.3, a.Barras[4]); Ponto(45.7, 69.3, a.Barras[5]); Ponto(45.7, 75.2, a.Barras[6]); Ponto(4.3, 75.2, a.Barras[7]);
        Ponto(10.3, 4.8, a.Barras[8]); Ponto(39.7, 4.8, a.Barras[9]); Ponto(39.7, 14.7, a.Barras[10]); Ponto(10.3, 14.7, a.Barras[11]);
        var pele = a.Barras.Where(b => b.Grupo == "pele").ToList();
        Assert.Equal(4, pele.Count);
        Ponto(22.8, 32.9, pele[0]); Ponto(27.2, 32.9, pele[1]); Ponto(22.8, 51.1, pele[2]); Ponto(27.2, 51.1, pele[3]);
        Assert.All(pele, b => { Assert.Equal(10, b.Bitola); Assert.Equal(ParteSecao.Alma, b.Parte); });
        // no mínimo 6 por mesa (4 na face externa + 2 fechando o estribo da mesa)
        Assert.Equal(6, a.Barras.Count(b => b.Grupo == "canto" && b.Y > 65));
        Assert.Equal(6, a.Barras.Count(b => b.Grupo == "canto" && b.Y < 20));
        Assert.Equal(4, a.Barras.Count(b => Math.Abs(b.Y - 75.2) < Tol));
        Assert.Equal(4, a.Barras.Count(b => Math.Abs(b.Y - 4.8) < Tol));
        Assert.Equal(new ParteSecao?[] { ParteSecao.Alma, ParteSecao.MesaSuperior, ParteSecao.MesaInferior }, a.Estribos.Select(e => e.Parte));
        // estribo da mesa inferior: (5 + 3,9; 3,4)–(45 − 3,9; 20 − 3,9) = (8,9; 3,4)–(41,1; 16,1)
        var mi = a.Estribos[2].Eixo; Ponto(8.9, 3.4, mi[0]); Ponto(41.1, 16.1, mi[2]);
        Assert.Empty(a.Avisos);
        Conferir(s, a);
    }

    [Fact]
    public void IEspelhadaEhOEspelho()
    {
        // mesas diferentes para o espelho vertical trocar alguma coisa
        var c = SecaoTransversal.I(12, 80, 15, 19, 19, 20, 14, 14);
        var v = Valores(c);
        Espelho(c, CobI(), SecaoTransversal.DaFamilia(FamiliasSecao.I, v, OrientacaoSecao.Invertida), false, true, nFaceH: 2);
        Espelho(c, CobI(), SecaoTransversal.DaFamilia(FamiliasSecao.I, v, new OrientacaoSecao(true, true)), true, true, nFaceH: 2);
    }

    // ------------------------------------------------------------------ trapezoidal

    [Fact]
    public void TrapezoidalPelaNormalDaFaceInclinada()
    {
        // bi 20, bs 30, h 40 → b = 30; fundo (5; 0)–(25; 0), topo (0; 40)–(30; 40); face direita (25; 0)→(30; 40), L = √1625 = 40,3113.
        // normal para dentro da direita = (−40; 5)/L; a reta a d da face: −40(x − 25) + 5z = d·L → x = 25 + (5z − d·L)/40.
        // barras (d = 3 + 0,8 + 1,0 = 4,8): z = 4,8 → x = 25 − 4,8·(L − 5)/40 = 20,76265; z = 35,2 → x = 25 + (176 − 193,4941)/40 = 24,56265
        // estribo (d = 3,4): z = 3,4 → x = 25 − 3,4·(L − 5)/40 = 21,99854; z = 36,6 → x = 25 + (183 − 137,0584)/40 = 26,14854
        double l = Math.Sqrt(1625);
        double X(double z, double d) => 25 + (5 * z - d * l) / 40;
        var s = SecaoTransversal.Trapezoidal(20, 30, 40);
        var cob = new Dictionary<ParteSecao, double> { [ParteSecao.Topo] = 3, [ParteSecao.Fundo] = 3, [ParteSecao.Esquerda] = 3, [ParteSecao.Direita] = 3 };
        var a = Arranjo(s, cob, nFaceB: 1, nFaceH: 1, phiFace: 16);
        Assert.True(a.Definido, a.Erro);
        Assert.Equal(20.76265, X(4.8, 4.8), 5); Assert.Equal(24.56265, X(35.2, 4.8), 5);
        Ponto(30 - X(4.8, 4.8), 4.8, a.Barras[0]); Ponto(X(4.8, 4.8), 4.8, a.Barras[1]);
        Ponto(X(35.2, 4.8), 35.2, a.Barras[2]); Ponto(30 - X(35.2, 4.8), 35.2, a.Barras[3]);
        Assert.All(a.Barras.Take(4), b => Assert.Null(b.Parte));
        Assert.Single(a.Estribos);
        Assert.Null(a.Estribos[0].Parte);
        var e = a.Estribos[0].Eixo;
        Ponto(30 - X(3.4, 3.4), 3.4, e[0]); Ponto(X(3.4, 3.4), 3.4, e[1]); Ponto(X(36.6, 3.4), 36.6, e[2]); Ponto(30 - X(36.6, 3.4), 36.6, e[3]);
        Assert.Equal(21.99854, X(3.4, 3.4), 5); Assert.Equal(26.14854, X(36.6, 3.4), 5);
        // intermediárias ø16 (d = 3 + 0,8 + 0,8 = 4,6): fundo e topo no meio (x = 15); inclinadas no meio, a 4,6 da face na normal
        var fb = a.Barras.Where(b => b.Grupo == "faceB").ToList();
        Ponto(15, 4.6, fb[0]); Ponto(15, 40 - 4.6, fb[1]);
        var fh = a.Barras.Where(b => b.Grupo == "faceH").ToList();
        Assert.Equal(new ParteSecao?[] { ParteSecao.Esquerda, ParteSecao.Direita }, fh.Select(b => b.Parte));
        var dir = fh[1];
        Assert.Equal(4.6, (-40 * (dir.X - 25) + 5 * dir.Y) / l, 9);
        Assert.Equal(20 - 0.2 * 5 / l, dir.Y, 9);   // meio das de canto (z = 20), recuada 0,2 na normal (−40; 5)/L
        Assert.Empty(a.Avisos);
        Conferir(s, a);
        // espelho vertical: mesmas barras e contorno espelhados; o comprimento muda (120 × 121 cm), porque o núcleo põe os
        // ganchos sempre no canto superior esquerdo e na invertida esse canto tem outro ângulo (agudo × obtuso)
        var inv = SecaoTransversal.DaFamilia(FamiliasSecao.Trapezoidal, Valores(s), OrientacaoSecao.Invertida);
        Conferir(inv, Espelho(s, cob, inv, false, true, 1, 1, 16, mesmoComprimento: false));
    }

    // ------------------------------------------------------------------ avisos

    [Fact]
    public void CobrimentoFaltandoEhErroComAviso()
    {
        var cob = CobT(); cob.Remove(ParteSecao.Topo);
        var a = Arranjo(SecaoTransversal.T(20, 60, 15, 20, 20), cob);
        Assert.False(a.Definido);
        Assert.Contains("Cobrimento faltando", a.Erro);
        Assert.Contains("Topo", a.Erro);
        Assert.Contains(a.Avisos, x => x.Contains("Cobrimento faltando"));
        Assert.Empty(a.Barras);
        // I sem as laterais da alma: não há face externa da alma para usar
        var ci = CobI(); ci.Remove(ParteSecao.LateralAlmaDireita);
        var b = Arranjo(SecaoTransversal.I(12, 80, 15, 19, 19, 20, 14, 14), ci);
        Assert.False(b.Definido);
        Assert.Contains("LateralAlmaDireita", b.Erro);
    }

    [Fact]
    public void BarrasQueSeSobrepoem()
    {
        // ae = 1: barra da ponta da mesa em x = 3,5 + 1,8 = 5,3 e da alma em 1 + 3 + 1,8 = 5,8 → 0,5 cm entre eixos < 2
        var a = Arranjo(SecaoTransversal.T(20, 60, 15, 1, 20), CobT());
        Assert.True(a.Definido);
        Assert.Contains(a.Avisos, x => x.Contains("Barras 4 e 8 se sobrepõem"));
    }

    [Fact]
    public void EstriboQueNaoCabeEBarraForaDoConcreto()
    {
        // mesa com hf = 3: cobrimentos 2 + 3 > 3 → estribo invertido; a barra de baixo da mesa fica em z = 57 + 2 + 1,8 = 60,8 > 60
        var a = Arranjo(SecaoTransversal.T(20, 60, 3, 20, 20), CobT());
        Assert.Contains(a.Avisos, x => x.Contains("Estribo da Mesa") && x.Contains("não cabe"));
        Assert.Contains(a.Avisos, x => x.Contains("fora do concreto"));
        // alma de 6 cm com 2,5 de cada lado: lado do eixo 6 − 5 − 0,8 = 0,2; por dentro 0,2 − 0,8 < pino 2,4 (3φt, ø8)
        var b = Arranjo(SecaoTransversal.I(6, 80, 15, 19, 19, 20, 14, 14), CobI());
        Assert.Contains(b.Avisos, x => x.Contains("Estribo da Alma") && x.Contains("não cabe"));
    }

    [Fact]
    public void IntermediariasNaoAplicadasViramAviso()
    {
        var a = Arranjo(SecaoTransversal.T(20, 60, 15, 20, 20), CobT(), nFaceB: 2, nFaceH: 1, porCanto: 3);
        Assert.Equal(8, a.Barras.Count);
        Assert.Contains(a.Avisos, x => x.Contains("cantos com 3 barras"));
        Assert.Contains(a.Avisos, x => x.Contains("intermediárias por face"));
    }

    // ------------------------------------------------------------------ apoio

    /// <summary>Barras dentro do concreto (círculo inteiro), estribos dentro (eixo a φt/2 ou mais das faces), comprimentos do núcleo.</summary>
    private static void Conferir(SecaoTransversal s, ArranjoSecao a)
    {
        var c = s.Contorno().Contorno;
        foreach (var b in a.Barras)
        {
            Assert.True(Dentro(c, b.X, b.Y), $"barra ({b.X}; {b.Y}) fora do contorno");
            Assert.True(Distancia(c, b.X, b.Y) >= b.Bitola / 20 - 1e-9, $"barra ({b.X}; {b.Y}) a menos de φ/2 da face");
        }
        foreach (var e in a.Estribos)
        {
            for (int i = 0; i < e.Eixo.Count; i++)
            {
                var p = e.Eixo[i]; var q = e.Eixo[(i + 1) % e.Eixo.Count];
                for (int k = 0; k <= 10; k++)
                {
                    double x = p.X + k / 10.0 * (q.X - p.X), y = p.Y + k / 10.0 * (q.Y - p.Y);
                    Assert.True(Dentro(c, x, y) && Distancia(c, x, y) >= 0.4 - 1e-9, $"estribo da {e.Parte} sai do concreto em ({x}; {y})");
                }
            }
            double esperado = ComprimentoPorEixo.Calcular(e.Eixo.Select(p => (p.X, p.Y, 0.0)).ToList(), true, 8, "CA-60", true, true, Cfg);
            Assert.Equal(esperado, e.Comprimento, 9);
            double perimetro = e.Eixo.Select((p, i) => Math.Sqrt(Math.Pow(e.Eixo[(i + 1) % e.Eixo.Count].X - p.X, 2) + Math.Pow(e.Eixo[(i + 1) % e.Eixo.Count].Y - p.Y, 2))).Sum();
            Assert.True(e.Comprimento > perimetro, "ganchos e passagens somam ao perímetro do eixo");
        }
    }

    /// <summary>
    /// Arranjo da seção espelhada (cobrimentos com os nomes trocados) = o da canônica espelhado, barra a barra; estribos com o
    /// mesmo comprimento (se <paramref name="mesmoComprimento"/>) e o contorno espelhado.
    /// </summary>
    private static ArranjoSecao Espelho(SecaoTransversal canonica, Dictionary<ParteSecao, double> cob, SecaoTransversal espelhada, bool eh, bool ev,
        int nFaceB = 0, int nFaceH = 0, double phiFace = 10, bool mesmoComprimento = true)
    {
        var a = Arranjo(canonica, cob, nFaceB, nFaceH, phiFace);
        var e = Arranjo(espelhada, Renomear(cob, eh, ev), nFaceB, nFaceH, phiFace);
        double bb = canonica.B, hh = canonica.H;
        Assert.Equal(a.Barras.Count, e.Barras.Count);
        for (int i = 0; i < a.Barras.Count; i++)
        {
            Ponto(eh ? bb - a.Barras[i].X : a.Barras[i].X, ev ? hh - a.Barras[i].Y : a.Barras[i].Y, e.Barras[i]);
            Assert.Equal(a.Barras[i].Parte.HasValue ? Nome(a.Barras[i].Parte.Value, eh, ev) : (ParteSecao?)null, e.Barras[i].Parte);
            Assert.Equal(a.Barras[i].Grupo, e.Barras[i].Grupo);
        }
        Assert.Equal(a.Estribos.Count, e.Estribos.Count);
        for (int i = 0; i < a.Estribos.Count; i++)
        {
            if (mesmoComprimento) Assert.Equal(a.Estribos[i].Comprimento, e.Estribos[i].Comprimento, 9);
            var esperado = a.Estribos[i].Eixo.Select(p => (X: Math.Round(eh ? bb - p.X : p.X, 6), Y: Math.Round(ev ? hh - p.Y : p.Y, 6))).OrderBy(p => p.X).ThenBy(p => p.Y);
            var obtido = e.Estribos[i].Eixo.Select(p => (X: Math.Round(p.X, 6), Y: Math.Round(p.Y, 6))).OrderBy(p => p.X).ThenBy(p => p.Y);
            Assert.Equal(esperado, obtido);
        }
        return e;
    }

    private static ParteSecao Nome(ParteSecao p, bool eh, bool ev)
    {
        var troca = new Dictionary<ParteSecao, ParteSecao>();
        if (eh) { troca[ParteSecao.Esquerda] = ParteSecao.Direita; troca[ParteSecao.Direita] = ParteSecao.Esquerda; troca[ParteSecao.LateralAlmaEsquerda] = ParteSecao.LateralAlmaDireita; troca[ParteSecao.LateralAlmaDireita] = ParteSecao.LateralAlmaEsquerda; }
        if (eh && troca.TryGetValue(p, out var q)) p = q;
        troca.Clear();
        if (ev) { troca[ParteSecao.Topo] = ParteSecao.Fundo; troca[ParteSecao.Fundo] = ParteSecao.Topo; troca[ParteSecao.MesaSuperior] = ParteSecao.MesaInferior; troca[ParteSecao.MesaInferior] = ParteSecao.MesaSuperior; }
        if (ev && troca.TryGetValue(p, out var r)) p = r;
        return p;
    }

    private static Dictionary<ParteSecao, double> Renomear(Dictionary<ParteSecao, double> cob, bool eh, bool ev) =>
        cob.ToDictionary(k => Nome(k.Key, eh, ev), k => k.Value);

    private static Dictionary<string, double> Valores(SecaoTransversal s) => s.Valores.ToDictionary(k => k.Key, k => k.Value);

    private static bool Dentro(IReadOnlyList<(double Y, double Z)> c, double y, double z)
    {
        bool d = false;
        for (int i = 0, j = c.Count - 1; i < c.Count; j = i++)
            if ((c[i].Z > z) != (c[j].Z > z) && y < (c[j].Y - c[i].Y) * (z - c[i].Z) / (c[j].Z - c[i].Z) + c[i].Y) d = !d;
        return d;
    }

    private static double Distancia(IReadOnlyList<(double Y, double Z)> c, double y, double z)
    {
        double m = double.MaxValue;
        for (int i = 0; i < c.Count; i++)
        {
            var a = c[i]; var b = c[(i + 1) % c.Count];
            double dy = b.Y - a.Y, dz = b.Z - a.Z, l2 = dy * dy + dz * dz;
            double f = Math.Max(0, Math.Min(1, ((y - a.Y) * dy + (z - a.Z) * dz) / l2));
            m = Math.Min(m, Math.Sqrt(Math.Pow(a.Y + f * dy - y, 2) + Math.Pow(a.Z + f * dz - z, 2)));
        }
        return m;
    }
}
