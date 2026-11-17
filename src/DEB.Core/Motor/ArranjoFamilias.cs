using System;
using System.Collections.Generic;
using System.Linq;
using DEB.Core.Calculo;
using DEB.Core.Configuracao;
using DEB.Core.Geometria.Secao;

namespace DEB.Core.Motor;

/// <summary>
/// Arranjo da armadura nas famílias de seção não retangulares (D31, respostas do dono de 16/11/2026): barras de canto de
/// cada estribo, estribos por parte e a pele da alma da I. Chamado por <see cref="SecaoBarras.Posicoes(SecaoTransversal,
/// IReadOnlyDictionary{ParteSecao, double}, double, double, int, double, double, int, int, double, string,
/// ConfiguracaoArmadura)"/>. A conta é feita na posição canônica da família e espelhada para a posição real no fim
/// (os cobrimentos são lidos pelo nome real de cada parte). Cobrimentos até o estribo; barra de canto a
/// cobrimento + φt + φ/2 das faces do estribo; eixo do estribo a cobrimento + φt/2.
/// <list type="bullet">
/// <item>T (e invertida): estribo da alma na altura toda (até a face externa da mesa) + estribo da mesa na largura toda;
/// barras nos 4 cantos de cada um (8).</item>
/// <item>L: estribo da alma na altura toda + estribo da aba na largura toda (como a mesa da T); barras nos cantos dos
/// dois (7: o canto externo comum às duas é uma barra só).</item>
/// <item>I: estribo da alma na altura toda + um estribo por mesa; 6 barras por mesa (4 na face externa, 2 fechando o
/// estribo da mesa) e a pele da alma entre as mesas (quantidade por face = nFaceH, bitola φface).</item>
/// <item>Trapezoidal: um estribo trapezoidal; cobrimento na normal de cada face; intermediárias pelos mesmos parâmetros
/// do retângulo (nFaceB no topo e no fundo, nFaceH nas inclinadas, divididas por igual entre as de canto).</item>
/// </list>
/// Ver docs/04_arquitetura/secoes.md.
/// </summary>
internal static class ArranjoFamilias
{
    /// <summary>Distância abaixo da qual duas barras são a mesma (cm).</summary>
    private const double Mesma = 1e-6;

    internal static ArranjoSecao Montar(SecaoTransversal s, IReadOnlyDictionary<ParteSecao, double> cob, double phiT,
        double phiCanto, int porCanto, int nFaceB, int nFaceH, double phiFace, string aco, ConfiguracaoArmadura cfg)
    {
        if (s.Origem == OrigemSecao.Medida)
            return ArranjoSecao.NaoDefinido($"Arranjo das longitudinais não definido para a seção {s.Familia.Nome} " +
                                            $"({s.Descricao}): seção medida, sem família (D31).");
        var m = new Montagem(s, cob, phiT, phiCanto);
        string id = s.Familia.Id;
        switch (id)
        {
            case "T": m.T(); break;
            case "L": m.L(); break;
            case "I": m.I(nFaceH, phiFace); break;
            case "trapezoidal": m.Trapezio(nFaceB, nFaceH, phiFace); break;
            default:
                return ArranjoSecao.NaoDefinido($"Arranjo das longitudinais não definido para a seção {s.Familia.Nome} ({s.Descricao}).");
        }

        // o que o retângulo tem e aqui não entra (falta definir por parte)
        if (porCanto >= 3)
            m.Avisos.Add($"Seção {s.Familia.Nome}: cantos com 3 barras (\"L\") não aplicados; 1 barra por canto de estribo (falta definir).");
        if ((id == "T" || id == "L") && (nFaceB > 0 || nFaceH > 0))
            m.Avisos.Add($"Seção {s.Familia.Nome}: intermediárias por face (nFaceB = {nFaceB}, nFaceH = {nFaceH}) não aplicadas; " +
                         "só as barras de canto dos estribos (falta definir as intermediárias por parte).");
        if (id == "I" && nFaceB > 0)
            m.Avisos.Add($"Seção I: intermediárias nas mesas (nFaceB = {nFaceB}) não aplicadas; só as 6 barras por mesa e a pele da alma.");

        if (m.Faltando.Count > 0)
        {
            string erro = "Cobrimento faltando: " + string.Join("; ", m.Faltando.Distinct()) + ".";
            m.Avisos.Add(erro);
            return ArranjoSecao.NaoDefinido(erro, m.Avisos);
        }

        // posição canônica → real
        var c = s.Familia.ContornoCanonico(n => s.Valor(n));
        double B = c.Max(p => p.Y), H = c.Max(p => p.Z);
        var o = s.Orientacao;
        (double X, double Y) Real((double Y, double Z) p) => (o.EspelhoHorizontal ? B - p.Y : p.Y, o.EspelhoVertical ? H - p.Z : p.Z);
        ParteSecao? Nome(ParteSecao? p) => p.HasValue ? o.Renomear(p.Value) : (ParteSecao?)null;

        var barras = new List<BarraNaSecao>();
        foreach (var b in m.Barras)
        {
            var p = Real((b.Y, b.Z));
            if (barras.Any(x => Math.Abs(x.X - p.X) < Mesma && Math.Abs(x.Y - p.Y) < Mesma)) continue;   // canto comum a dois estribos
            barras.Add(new BarraNaSecao(p.X, p.Y, b.Bitola, b.Grupo, parte: Nome(b.Parte)));
        }
        var estribos = new List<EstriboNaSecao>();
        foreach (var e in m.Estribos)
        {
            var eixo = e.Eixo.Select(Real).ToList();
            if (o.EspelhoHorizontal != o.EspelhoVertical) eixo.Reverse();   // um espelho só inverte o sentido: volta ao anti-horário
            double l = ComprimentoPorEixo.Calcular(eixo.Select(q => (q.X, q.Y, 0.0)).ToList(), true, phiT, aco, true, true, cfg);
            estribos.Add(new EstriboNaSecao(Nome(e.Parte), eixo, phiT, aco, l));
        }
        Verificar(s.Contorno().Contorno, barras, estribos, cfg.Dobramento.DiametroPino(phiT, aco, estribo: true), m.Avisos);
        return new ArranjoSecao(barras, null, estribos, m.Avisos);
    }

    /// <summary>
    /// Verificações de consistência (avisos, não exceções): barra fora do concreto (centro fora ou a menos de φ/2 da face),
    /// barras que se sobrepõem, estribo que não cabe (cobrimentos maiores que a parte, ou lado com menos que o pino por
    /// dentro: largura &lt; cobrimentos + 2φt + pino) e estribo fora do concreto (eixo a menos de φt/2 da face).
    /// </summary>
    private static void Verificar(List<(double Y, double Z)> contorno, List<BarraNaSecao> barras, List<EstriboNaSecao> estribos,
        double pino, List<string> avisos)
    {
        const double tol = 1e-6;
        for (int i = 0; i < barras.Count; i++)
        {
            var b = barras[i];
            double r = b.Bitola / 20;
            if (!Dentro(contorno, b.X, b.Y) || DistanciaAoContorno(contorno, b.X, b.Y) < r - tol)
                avisos.Add($"Barra {i + 1} ({b.Grupo}, ø{Fmt.N(b.Bitola)}) em ({Fmt.N(Math.Round(b.X, 2))}; {Fmt.N(Math.Round(b.Y, 2))}) " +
                           "fora do concreto (ou a menos de φ/2 da face).");
        }
        for (int i = 0; i < barras.Count; i++)
            for (int j = i + 1; j < barras.Count; j++)
            {
                double d = Math.Sqrt(Math.Pow(barras[i].X - barras[j].X, 2) + Math.Pow(barras[i].Y - barras[j].Y, 2));
                double min = barras[i].Bitola / 20 + barras[j].Bitola / 20;
                if (d < min - tol)
                    avisos.Add($"Barras {i + 1} e {j + 1} se sobrepõem: {Fmt.N(Math.Round(d, 2))} cm entre eixos < {Fmt.N(min)} cm (φ/2 + φ/2).");
            }
        foreach (var e in estribos)
        {
            string nome = e.Parte.HasValue ? $"Estribo da {e.Parte}" : "Estribo";
            double t = e.Bitola / 10;
            if (Area(e.Eixo) <= tol)
            {
                avisos.Add($"{nome}: os cobrimentos são maiores que a parte; o estribo não cabe.");
                continue;
            }
            for (int k = 0; k < e.Eixo.Count; k++)
            {
                var a = e.Eixo[k]; var z = e.Eixo[(k + 1) % e.Eixo.Count];
                double l = Math.Sqrt(Math.Pow(z.X - a.X, 2) + Math.Pow(z.Y - a.Y, 2));
                if (l - t < pino - tol)
                {
                    avisos.Add($"{nome}: lado de {Fmt.N(Math.Round(l, 2))} cm no eixo deixa {Fmt.N(Math.Round(l - t, 2))} cm por dentro, " +
                               $"menos que o pino ({Fmt.N(pino)} cm): o estribo não cabe.");
                    break;
                }
            }
            if (e.Eixo.Any(p => !Dentro(contorno, p.X, p.Y) || DistanciaAoContorno(contorno, p.X, p.Y) < t / 2 - tol))
                avisos.Add($"{nome}: fora do concreto (eixo a menos de φt/2 da face).");
        }
    }

    private static double Area(List<(double X, double Y)> p)
    {
        double a = 0;
        for (int i = 0; i < p.Count; i++) { var u = p[i]; var v = p[(i + 1) % p.Count]; a += u.X * v.Y - v.X * u.Y; }
        return a / 2;
    }

    /// <summary>Ponto dentro do polígono (raio horizontal).</summary>
    internal static bool Dentro(IReadOnlyList<(double Y, double Z)> c, double y, double z)
    {
        bool dentro = false;
        for (int i = 0, j = c.Count - 1; i < c.Count; j = i++)
            if ((c[i].Z > z) != (c[j].Z > z) && y < (c[j].Y - c[i].Y) * (z - c[i].Z) / (c[j].Z - c[i].Z) + c[i].Y) dentro = !dentro;
        return dentro;
    }

    /// <summary>Menor distância do ponto aos lados do polígono.</summary>
    internal static double DistanciaAoContorno(IReadOnlyList<(double Y, double Z)> c, double y, double z)
    {
        double m = double.MaxValue;
        for (int i = 0; i < c.Count; i++)
        {
            var a = c[i]; var b = c[(i + 1) % c.Count];
            double dy = b.Y - a.Y, dz = b.Z - a.Z, l2 = dy * dy + dz * dz;
            double f = l2 < 1e-12 ? 0 : Math.Max(0, Math.Min(1, ((y - a.Y) * dy + (z - a.Z) * dz) / l2));
            double py = a.Y + f * dy - y, pz = a.Z + f * dz - z;
            m = Math.Min(m, Math.Sqrt(py * py + pz * pz));
        }
        return m;
    }

    /// <summary>Montagem na posição canônica da família (y na horizontal, z na vertical, cm).</summary>
    private sealed class Montagem
    {
        private readonly SecaoTransversal _s;
        private readonly IReadOnlyDictionary<ParteSecao, double> _cob;
        private readonly OrientacaoSecao _o;
        private readonly double _t, _phiC, _r;

        public readonly List<(ParteSecao? Parte, List<(double Y, double Z)> Eixo)> Estribos = new List<(ParteSecao?, List<(double, double)>)>();
        public readonly List<(double Y, double Z, double Bitola, string Grupo, ParteSecao? Parte)> Barras =
            new List<(double, double, double, string, ParteSecao?)>();
        public readonly List<string> Avisos = new List<string>();
        public readonly List<string> Faltando = new List<string>();

        public Montagem(SecaoTransversal s, IReadOnlyDictionary<ParteSecao, double> cob, double phiT, double phiCanto)
        {
            _s = s; _cob = cob; _o = s.Orientacao; _t = phiT / 10; _phiC = phiCanto; _r = phiCanto / 20;
        }

        private double V(string n) => _s.Valor(n);
        /// <summary>Nome real (depois do espelho) de uma parte da posição canônica; o espelho é a sua própria inversa.</summary>
        private ParteSecao Real(ParteSecao p) => _o.Renomear(p);
        private bool Tem(ParteSecao canonica, out double c)
        {
            c = 0;
            return _cob != null && _cob.TryGetValue(Real(canonica), out c);
        }

        /// <summary>Cobrimento de uma face da caixa (obrigatório).</summary>
        private double Externa(ParteSecao face)
        {
            if (Tem(face, out double c)) return c;
            Faltando.Add(Real(face).ToString());
            return 0;
        }

        /// <summary>
        /// Cobrimento de face interna (lateral da alma sob a mesa, face interna da mesa ou da aba). REGRA PROVISÓRIA do
        /// coordenador (D31; o dono não respondeu): o informado para a própria parte (a face, se tem nome, ou a região);
        /// na falta, o maior das faces externas adjacentes da mesma parte — e um aviso dizendo qual foi usado.
        /// </summary>
        private double Interna(string descricao, ParteSecao[] proprias, ParteSecao[] adjacentes)
        {
            foreach (var p in proprias) if (Tem(p, out double c)) return c;
            var achadas = new List<(ParteSecao P, double C)>();
            foreach (var p in adjacentes) if (Tem(p, out double c)) achadas.Add((Real(p), c));
            if (achadas.Count == 0)
            {
                Faltando.Add(descricao + (adjacentes.Length > 0 ? $" (nem {string.Join(", ", adjacentes.Select(Real))})" : ""));
                return 0;
            }
            double m = achadas.Max(a => a.C);
            Avisos.Add($"Cobrimento de {descricao} não informado: usado {Fmt.N(m)} cm, " +
                       $"o {(achadas.Count > 1 ? "maior de " : "de ")}{string.Join(", ", achadas.Select(a => a.P))} " +
                       "(regra provisória, a confirmar com o dono).");
            return m;
        }

        /// <summary>Estribo retangular na região (y0, z0)–(y1, z1) com os cobrimentos de cada lado, e as 4 barras de canto.</summary>
        private void Estribo(ParteSecao? parte, double y0, double z0, double y1, double z1, double cE, double cD, double cF, double cT)
        {
            double a = _t / 2, d = _t + _r;
            Estribos.Add((parte, new List<(double, double)> { (y0 + cE + a, z0 + cF + a), (y1 - cD - a, z0 + cF + a), (y1 - cD - a, z1 - cT - a), (y0 + cE + a, z1 - cT - a) }));
            foreach (var (y, z) in new[] { (y0 + cE + d, z0 + cF + d), (y1 - cD - d, z0 + cF + d), (y1 - cD - d, z1 - cT - d), (y0 + cE + d, z1 - cT - d) })
                Barras.Add((y, z, _phiC, "canto", parte));
        }

        /// <summary>T, mesa em cima: estribo da alma na altura toda (os 2 cantos de cima ficam na face de cima da mesa) e estribo da mesa.</summary>
        public void T()
        {
            double bw = V("bw"), h = V("h"), hf = V("hf"), ae = V("ae"), b = ae + bw + V("ad"), zm = h - hf;
            double cT = Externa(ParteSecao.Topo), cF = Externa(ParteSecao.Fundo), cE = Externa(ParteSecao.Esquerda), cD = Externa(ParteSecao.Direita);
            double cLe = Interna(Real(ParteSecao.LateralAlmaEsquerda).ToString(), new[] { ParteSecao.LateralAlmaEsquerda, ParteSecao.Alma }, new[] { ParteSecao.Fundo });
            double cLd = Interna(Real(ParteSecao.LateralAlmaDireita).ToString(), new[] { ParteSecao.LateralAlmaDireita, ParteSecao.Alma }, new[] { ParteSecao.Fundo });
            double cMi = Interna("face interna da mesa (lado da alma)", new[] { ParteSecao.Mesa }, new[] { ParteSecao.Esquerda, ParteSecao.Direita });
            Estribo(ParteSecao.Alma, ae, 0, ae + bw, h, cLe, cLd, cF, cT);
            Estribo(ParteSecao.Mesa, 0, zm, b, h, cE, cD, cMi, cT);
        }

        /// <summary>L, alma à esquerda e aba embaixo: estribo da alma na altura toda e estribo da aba na largura toda (como a mesa da T).</summary>
        public void L()
        {
            double bw = V("bw"), h = V("h"), hf = V("hf"), b = bw + V("ba");
            double cT = Externa(ParteSecao.Topo), cF = Externa(ParteSecao.Fundo), cE = Externa(ParteSecao.Esquerda), cD = Externa(ParteSecao.Direita);
            double cLd = Interna(Real(ParteSecao.LateralAlmaDireita).ToString(), new[] { ParteSecao.LateralAlmaDireita, ParteSecao.Alma }, new[] { ParteSecao.Topo });
            double cAb = Interna("face interna da aba (lado da alma)", new[] { ParteSecao.Aba }, new[] { ParteSecao.Direita });
            Estribo(ParteSecao.Alma, 0, 0, bw, h, cE, cLd, cF, cT);
            Estribo(ParteSecao.Aba, 0, 0, b, hf, cE, cD, cF, cAb);
        }

        /// <summary>
        /// I: estribo da alma na altura toda (cantos nas faces externas das duas mesas) e um estribo por mesa; pele da alma
        /// entre as barras internas das mesas, <paramref name="nPele"/> por face.
        /// </summary>
        public void I(int nPele, double phiPele)
        {
            double bw = V("bw"), h = V("h"), hs = V("hs"), hi = V("hi"), ase = V("ase"), asd = V("asd"), aie = V("aie"), aid = V("aid");
            double ya = Math.Max(ase, aie), yb = ya + bw, zs = h - hs;
            double cT = Externa(ParteSecao.Topo), cF = Externa(ParteSecao.Fundo);
            double cLe = Interna(Real(ParteSecao.LateralAlmaEsquerda).ToString(), new[] { ParteSecao.LateralAlmaEsquerda, ParteSecao.Alma }, new ParteSecao[0]);
            double cLd = Interna(Real(ParteSecao.LateralAlmaDireita).ToString(), new[] { ParteSecao.LateralAlmaDireita, ParteSecao.Alma }, new ParteSecao[0]);
            // pontas e face interna de cada mesa: sem nome na família; a própria mesa ou, na falta, a face externa dela
            double cMs = Interna($"pontas e face interna da {Real(ParteSecao.MesaSuperior)}", new[] { ParteSecao.MesaSuperior }, new[] { ParteSecao.Topo });
            double cMi = Interna($"pontas e face interna da {Real(ParteSecao.MesaInferior)}", new[] { ParteSecao.MesaInferior }, new[] { ParteSecao.Fundo });
            Estribo(ParteSecao.Alma, ya, 0, yb, h, cLe, cLd, cF, cT);
            Estribo(ParteSecao.MesaSuperior, ya - ase, zs, yb + asd, h, cMs, cMs, cMs, cT);
            Estribo(ParteSecao.MesaInferior, ya - aie, 0, yb + aid, hi, cMi, cMi, cF, cMi);
            if (nPele <= 0) return;
            // pele: alinhada à lateral da alma (cobrimento + φt + φpele/2), dividida por igual entre as barras internas das mesas
            double rp = phiPele / 20, z0 = hi - cMi - _t - _r, z1 = zs + cMs + _t + _r;
            for (int k = 1; k <= nPele; k++)
            {
                double z = z0 + k * (z1 - z0) / (nPele + 1);
                Barras.Add((ya + cLe + _t + rp, z, phiPele, "pele", ParteSecao.Alma));
                Barras.Add((yb - cLd - _t - rp, z, phiPele, "pele", ParteSecao.Alma));
            }
        }

        /// <summary>
        /// Trapezoidal: estribo trapezoidal pelas faces; cobrimento na NORMAL de cada face (as linhas paralelas às faces se
        /// cruzam nos cantos). Intermediárias como no retângulo: nFaceB no fundo e no topo, nFaceH em cada inclinada,
        /// divididas por igual entre as de canto (numa reta, dividir pelo comprimento ou pela projeção dá o mesmo).
        /// </summary>
        public void Trapezio(int nB, int nH, double phiF)
        {
            double bi = V("bi"), bs = V("bs"), h = V("h"), b = Math.Max(bi, bs);
            var v = new List<(double Y, double Z)> { ((b - bi) / 2, 0), ((b + bi) / 2, 0), ((b + bs) / 2, h), ((b - bs) / 2, h) };
            var faces = new[] { ParteSecao.Fundo, ParteSecao.Direita, ParteSecao.Topo, ParteSecao.Esquerda };   // lado i: v[i] → v[i + 1]
            var c = faces.Select(Externa).ToArray();
            Estribos.Add((null, Deslocado(v, c.Select(x => x + _t / 2).ToArray())));
            var k = Deslocado(v, c.Select(x => x + _t + _r).ToArray());   // 0 fundo-esq., 1 fundo-dir., 2 topo-dir., 3 topo-esq.
            foreach (var p in k) Barras.Add((p.Y, p.Z, _phiC, "canto", null));
            double delta = phiF / 20 - _r;   // a intermediária fica a cobrimento + φt + φface/2 da face
            (double Y, double Z) Ponto((double Y, double Z) a, (double Y, double Z) z, double f, int lado)
            {
                var n = NormalInterna(v, lado);
                return (a.Y + f * (z.Y - a.Y) + n.Y * delta, a.Z + f * (z.Z - a.Z) + n.Z * delta);
            }
            for (int j = 1; j <= nB; j++)
            {
                double f = (double)j / (nB + 1);
                var p = Ponto(k[0], k[1], f, 0); Barras.Add((p.Y, p.Z, phiF, "faceB", ParteSecao.Fundo));
                var q = Ponto(k[3], k[2], f, 2); Barras.Add((q.Y, q.Z, phiF, "faceB", ParteSecao.Topo));
            }
            for (int j = 1; j <= nH; j++)
            {
                double f = (double)j / (nH + 1);
                var p = Ponto(k[0], k[3], f, 3); Barras.Add((p.Y, p.Z, phiF, "faceH", ParteSecao.Esquerda));
                var q = Ponto(k[1], k[2], f, 1); Barras.Add((q.Y, q.Z, phiF, "faceH", ParteSecao.Direita));
            }
        }

        /// <summary>Normal unitária para dentro do lado i (v[i] → v[i + 1]) de um polígono anti-horário.</summary>
        private static (double Y, double Z) NormalInterna(List<(double Y, double Z)> v, int i)
        {
            var a = v[i]; var b = v[(i + 1) % v.Count];
            double dy = b.Y - a.Y, dz = b.Z - a.Z, l = Math.Sqrt(dy * dy + dz * dz);
            return (-dz / l, dy / l);
        }

        /// <summary>
        /// Polígono convexo anti-horário com cada lado i levado d[i] para dentro, na normal: vértice i = cruzamento das
        /// paralelas aos lados i − 1 e i.
        /// </summary>
        private static List<(double Y, double Z)> Deslocado(List<(double Y, double Z)> v, double[] d)
        {
            int n = v.Count;
            var r = new List<(double Y, double Z)>(n);
            for (int i = 0; i < n; i++)
            {
                int j = (i - 1 + n) % n;
                var n1 = NormalInterna(v, j); var n2 = NormalInterna(v, i);
                // n·p = n·v + d em cada uma das duas paralelas
                double c1 = n1.Y * v[j].Y + n1.Z * v[j].Z + d[j], c2 = n2.Y * v[i].Y + n2.Z * v[i].Z + d[i];
                double det = n1.Y * n2.Z - n2.Y * n1.Z;
                r.Add(((c1 * n2.Z - c2 * n1.Z) / det, (n1.Y * c2 - n2.Y * c1) / det));
            }
            return r;
        }
    }
}
