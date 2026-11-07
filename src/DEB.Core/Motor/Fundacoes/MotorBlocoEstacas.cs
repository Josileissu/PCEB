using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DEB.Core.Calculo;
using DEB.Core.Configuracao;
using DEB.Core.Estribos;
using DEB.Core.Modelo;
using DEB.Core.Normas;
using static DEB.Core.Motor.Fundacoes.GeometriaPlanta;

namespace DEB.Core.Motor.Fundacoes;

/// <summary>
/// Motor de fundações — bloco sobre estacas (frente B, 2026-11-07). Mesmas regras do <c>TemplateBlocoEstacas</c> (grade
/// nx × ny ou triangular de 3 estacas; cálice com colarinho, embutido ou arranque; bloco de 1 estaca sem principal), mas a
/// saída é o modelo neutro 3D (<see cref="ModeloArmadura"/>): corpos de concreto, níveis e uma posição por família.
/// Nada de desenho (textos, cotas, vistas, faixas 2D, escala): isso é do gerador de prancha.
/// <para>Sistema local: x, y em planta (origem no canto inferior esquerdo da caixa envolvente do bloco), z para cima,
/// z = 0 no fundo da base. Vértices em cm com as MEDIDAS EXTERNAS da barra (convenção do DEB: cotas e comprimentos
/// externos, como em BarraTemplate): barras inferiores pela face de baixo, superiores pela face de cima.</para>
/// <para>CHAVES DE POSIÇÃO (estáveis; as sobrescritas apontam para elas):
/// principal_x, principal_y (grade) | principal_lado1, principal_lado2, principal_lado3 (triangular);
/// secundaria_x, secundaria_y; superior_x, superior_y; lateral; estribo_vertical_x;
/// calice_topo, calice_grampos_topo_x, calice_grampos_topo_y, calice, calice_grampos_x, calice_grampos_y,
/// calice_vertical, suspensao, fundo_calice; arranque, arranque_estribo.</para>
/// <para>PARÂMETROS (intenção) que aceitam sobrescrita: bitola em todas; quantidade/espacamento conforme a posição;
/// perna (principal, secundária, superior, grampos, suspensão), perna_externa (suspensão), comprimento_reto (famílias
/// retas sem VAR), largura/altura/gancho (verticais do cálice), comprimento (fundo do cálice), pe/espera/
/// comprimento_interno (arranque). Sobrescrita é aplicada ANTES da geometria e fica em <see cref="PosicaoArmadura.Manuais"/>.</para>
/// <para>PERFIL: Padroes "cobrimento.solo"(4), "cobrimento.calice"(2), "secundaria.perna"(20), "superior.perna"(20),
/// "suspensao.desconto_perna_externa"(6), "arranque.pe"(20), "arranque.espera"(0 = traspasse NBR 6118), "calice.grampos"(1),
/// "principal.perna"(0), "fundo_calice.comprimento"(100); Regras "principal.perna" = "altura_base" | "valor",
/// "arranque.pe_sentido" = "fora" | "dentro".</para>
/// </summary>
public sealed class MotorBlocoEstacas : IMotorDetalhamento<ElementoBloco>
{
    /// <summary>Trecho do pilar representado acima do topo do bloco (corpo "pilar"), cm.</summary>
    public const double PilarAcimaDoTopo = 50;

    private readonly ConfiguracaoArmadura _cfg;
    public MotorBlocoEstacas() : this(null) { }
    /// <param name="cfg">regras de faixa (quantidade, espaçamento inicial) e de dobramento; null = padrão.</param>
    public MotorBlocoEstacas(ConfiguracaoArmadura cfg) { _cfg = cfg ?? new ConfiguracaoArmadura(); }

    public string TipoElemento => ElementoBloco.TipoId;

    // ======================== contexto ========================

    private sealed class Linha3 { public double T, A, B; }

    /// <summary>Família de barras retas (com pernas) numa direção da planta.</summary>
    private sealed class Familia
    {
        public double Ang, Bitola, Esp, Perna, Z;
        public bool Superior;
        public List<Linha3> Linhas = new List<Linha3>();
        public int Faixas = 1, PorFaixa;
        public (double X, double Y) U => (Math.Cos(Ang), Math.Sin(Ang));
        public (double X, double Y) N => (-Math.Sin(Ang), Math.Cos(Ang));
        public (double X, double Y) Ponto(double t, double s) => (t * N.X + s * U.X, t * N.Y + s * U.Y);
    }

    private sealed class Ctx
    {
        public ElementoBloco El;
        public PerfilDetalhamento Perfil;
        public ConfiguracaoArmadura Cfg;
        public ModeloArmadura M;
        public Dictionary<(string, string), double> Sobr = new Dictionary<(string, string), double>();
        public HashSet<(string, string)> Usadas = new HashSet<(string, string)>();
        public string Aco = "CA-50";
        public double c, cc, e, D, emb, hB, hC, hT, lastro, Lemb, reg, folga, t, Bx, By, W, H, Px, Py;
        public double Cx0, Cy0, Cx1, Cy1, Ox0, Oy0, Ox1, Oy1;
        public bool Tri, Calice, Colar;
        public List<(double X, double Y)> Contorno = new List<(double, double)>();
        public List<(double X, double Y)> Estacas = new List<(double, double)>();
        public List<Familia> Inferiores = new List<Familia>();
        public double PhiLat, PhiPrinc;
        public PosicaoArmadura Lateral;
        public double Lp => Math.Max(25, 1.2 * D);
        public double ZPrinc => Inferiores.Select(f => f.Z + f.Bitola / 20).DefaultIfEmpty(c).Max();
    }

    private static string Num(double x) => CalculoComprimento.NumeroCompacto(x);
    private static string NumV(double x) => x.ToString("0.##", CultureInfo.InvariantCulture).Replace('.', ',');

    // ======================== entrada ========================

    public ModeloArmadura Gerar(ElementoBloco elemento, PerfilDetalhamento perfil, IReadOnlyList<Sobrescrita> sobrescritas)
    {
        if (elemento == null) throw new ArgumentNullException(nameof(elemento));
        var c = new Ctx
        {
            El = elemento, Perfil = perfil ?? new PerfilDetalhamento(), Cfg = _cfg,
            M = new ModeloArmadura { TipoElemento = ElementoBloco.TipoId, Nome = elemento.Nome ?? "" },
        };
        foreach (var s in sobrescritas ?? Array.Empty<Sobrescrita>())
            if (s != null) c.Sobr[(s.Posicao ?? "", s.Parametro ?? "")] = s.Valor;
        Ler(c);
        Corpos(c);
        Niveis(c);
        Armaduras(c);
        Verificar(c);
        foreach (var k in c.Sobr.Keys.Where(k => !c.Usadas.Contains(k)))
            c.M.Avisos.Add(c.M.Posicao(k.Item1) != null
                ? $"Sobrescrita ignorada: a posição \"{k.Item1}\" não tem o parâmetro \"{k.Item2}\" (ou ele não se aplica)."
                : $"Sobrescrita ignorada: a posição \"{k.Item1}\" não existe neste bloco.");
        return c.M;
    }

    private static void Ler(Ctx c)
    {
        var el = c.El; var pf = c.Perfil;
        c.Aco = string.IsNullOrWhiteSpace(el.Aco) ? "CA-50" : el.Aco;
        c.c = pf.Padrao("cobrimento.solo", 4); c.cc = pf.Padrao("cobrimento.calice", 2);
        c.e = el.DistFace; c.D = el.DiamEstaca; c.emb = Math.Max(0, el.EmbutimentoEstaca);
        c.hB = el.AlturaBase; c.lastro = Math.Max(0, el.Lastro);
        c.Bx = el.PilarBx; c.By = el.PilarBy;
        c.Calice = el.Ligacao != LigacaoPilar.Arranque; c.Colar = el.Ligacao == LigacaoPilar.CaliceColarinho;
        c.hC = c.Colar ? Math.Max(0, el.AlturaColarinho) : 0;
        c.hT = c.hB + c.hC;
        c.Lemb = c.Calice ? el.Lemb : 0; c.reg = c.Calice ? Math.Max(0, el.Regularizacao) : 0;
        c.folga = el.FolgaCalice; c.t = el.ParedeCalice;
        c.Tri = el.Arranjo == ArranjoEstacas.Triangulo;
        if (c.Tri)
        {
            var (cont, est) = Triangular(el.EspX, c.e);
            c.Contorno = cont; c.Estacas = est;
        }
        else
        {
            int nx = Math.Max(1, el.EstacasX), ny = Math.Max(1, el.EstacasY);
            double W = (nx - 1) * el.EspX + 2 * c.e, H = (ny - 1) * el.EspY + 2 * c.e;
            c.Contorno = new List<(double, double)> { (0, 0), (W, 0), (W, H), (0, H) };
            for (int j = 0; j < ny; j++) for (int i = 0; i < nx; i++) c.Estacas.Add((c.e + i * el.EspX, c.e + j * el.EspY));
        }
        c.W = c.Contorno.Max(p => p.X); c.H = c.Contorno.Max(p => p.Y);
        c.Px = c.Estacas.Average(p => p.X) + el.PilarEx;
        c.Py = c.Estacas.Average(p => p.Y) + el.PilarEy;
        double ax = c.Bx / 2 + c.folga, ay = c.By / 2 + c.folga;
        c.Ox0 = c.Px - ax; c.Ox1 = c.Px + ax; c.Oy0 = c.Py - ay; c.Oy1 = c.Py + ay;
        c.Cx0 = c.Ox0 - c.t; c.Cx1 = c.Ox1 + c.t; c.Cy0 = c.Oy0 - c.t; c.Cy1 = c.Oy1 + c.t;
        if (c.Colar)
        {
            // colarinho limitado ao bloco (B28: largura do colarinho = largura do bloco)
            c.Cx0 = Math.Max(c.Cx0, 0); c.Cy0 = Math.Max(c.Cy0, 0); c.Cx1 = Math.Min(c.Cx1, c.W); c.Cy1 = Math.Min(c.Cy1, c.H);
        }
    }

    private static List<(double X, double Y)> Ret(double x0, double y0, double x1, double y1) =>
        new List<(double X, double Y)> { (x0, y0), (x1, y0), (x1, y1), (x0, y1) };

    private static void Corpos(Ctx c)
    {
        var cs = c.M.Corpos;
        cs.Add(new CorpoConcreto { Papel = "bloco", Planta = c.Contorno.ToList(), Z0 = 0, Z1 = c.hB });
        if (c.Colar && c.hC > 0) cs.Add(new CorpoConcreto { Papel = "colarinho", Planta = Ret(c.Cx0, c.Cy0, c.Cx1, c.Cy1), Z0 = c.hB, Z1 = c.hT });
        double zPil = c.hT;
        if (c.Calice)
        {
            double zP = c.hT - c.Lemb, zF = zP - c.reg;
            cs.Add(new CorpoConcreto { Papel = "furo_calice", Planta = Ret(c.Ox0, c.Oy0, c.Ox1, c.Oy1), Z0 = zF, Z1 = c.hT, Vazio = true });
            if (c.reg > 0) cs.Add(new CorpoConcreto { Papel = "regularizacao", Planta = Ret(c.Ox0, c.Oy0, c.Ox1, c.Oy1), Z0 = zF, Z1 = zP });
            zPil = zP;
        }
        cs.Add(new CorpoConcreto
        {
            Papel = "pilar", Planta = Ret(c.Px - c.Bx / 2, c.Py - c.By / 2, c.Px + c.Bx / 2, c.Py + c.By / 2),
            Z0 = zPil, Z1 = c.hT + PilarAcimaDoTopo,
        });
        foreach (var p in c.Estacas)
            cs.Add(new CorpoConcreto { Papel = "estaca", Centro = p, Raio = c.D / 2, Z0 = -c.lastro - c.Lp, Z1 = c.emb });
        if (c.lastro > 0) cs.Add(new CorpoConcreto { Papel = "lastro", Planta = c.Contorno.ToList(), Z0 = -c.lastro, Z1 = 0 });
    }

    private static void Niveis(Ctx c)
    {
        var n = c.M.Niveis;
        n["fundo"] = 0;
        n["topo_base"] = c.hB;
        n["topo"] = c.hT;
        if (c.Calice) n["fundo_pilar"] = c.hT - c.Lemb;
        n["arrasamento"] = c.emb;
    }

    // ======================== intenção e sobrescritas ========================

    private static PosicaoArmadura Nova(Ctx c, string chave, string funcao, string nome, FormaBarra forma) =>
        new PosicaoArmadura { Chave = chave, Funcao = funcao, Nome = nome, Forma = forma, Aco = c.Aco };

    /// <summary>Parâmetro da intenção: a sobrescrita (se houver) manda; grava valor, regra e a marca de manual.</summary>
    private static double Par(Ctx c, PosicaoArmadura p, string nome, double calculado, string regra)
    {
        double v = calculado;
        if (c.Sobr.TryGetValue((p.Chave, nome), out double s))
        {
            v = s;
            c.Usadas.Add((p.Chave, nome));
            p.Manuais.Add(nome);
            if (s < 0) c.M.Avisos.Add($"{p.Chave}: {nome} manual = {Num(s)} < 0 — conferir.");
        }
        p.Parametros[nome] = v;
        if (!string.IsNullOrEmpty(regra)) p.Regras[nome] = regra;
        return v;
    }

    private static int ParInt(Ctx c, PosicaoArmadura p, string nome, int calculado, string regra) =>
        (int)Math.Round(Par(c, p, nome, calculado, regra));

    /// <summary>Armadura calculada da função (null = sem: função ausente ou bitola 0).</summary>
    private static ArmaduraCalculada Arm(Ctx c, string funcao)
    {
        var a = c.El.Armadura(funcao);
        return a != null && a.Bitola > 0 ? a : null;
    }

    private static void Adicionar(Ctx c, PosicaoArmadura p)
    {
        if (p.Barras.Count > 0 && p.Vertices.Count == 0) p.Vertices = p.Barras[0];
        c.M.Posicoes.Add(p);
    }

    private static int Qtd(Ctx c, double vao, double esp) =>
        CalculoFaixa.Quantidade(vao, esp, c.Cfg.EspacamentoInicialFaixa, c.Cfg.ModoQuantidadeFaixa);

    private static List<double> Posicoes(Ctx c, double vao, int n, double esp) =>
        CalculoFaixa.Posicoes(vao, n, esp, c.Cfg.EspacamentoInicialFaixa);

    private static P3 Centro(List<P3> pts) => new P3(pts.Average(p => p.X), pts.Average(p => p.Y), pts.Average(p => p.Z));

    // ======================== armaduras ========================

    private static void Armaduras(Ctx c)
    {
        // a lateral entra no recuo das barras retas: a bitola efetiva (com sobrescrita) é lida antes
        var aLat = Arm(c, Funcoes.Lateral);
        if (aLat != null)
        {
            c.Lateral = Nova(c, "lateral", Funcoes.Lateral, "lateral horizontal", FormaBarra.Estribo);
            c.PhiLat = Math.Max(0, Par(c, c.Lateral, "bitola", aLat.Bitola, "cálculo (elemento)"));
        }
        Principal(c);
        double zInf = Math.Max(c.c, c.emb);
        var aS = Arm(c, Funcoes.Secundaria); var aU = Arm(c, Funcoes.Superior);
        foreach (double ang in new[] { 0.0, Math.PI / 2 })
        {
            string suf = ang > 0 ? "_y" : "_x", sufN = ang > 0 ? " y" : " x";
            if (aS != null)
            {
                var p = Nova(c, Funcoes.Secundaria + suf, Funcoes.Secundaria, "secundária" + sufN, FormaBarra.U);
                double phi = Par(c, p, "bitola", aS.Bitola, "cálculo (elemento)");
                if (phi > 0)
                {
                    var f = new Familia
                    {
                        Ang = ang, Bitola = phi, Esp = Par(c, p, "espacamento", aS.Espacamento, "cálculo (elemento)"),
                        Z = zInf + c.PhiPrinc / 10 * 2 + phi / 20 + (ang > 0 ? phi / 10 : 0),
                    };
                    f.Perna = Par(c, p, "perna", c.Perfil.Padrao("secundaria.perna", 20), "padrão do perfil (secundaria.perna)");
                    Malha(c, p, f, false);
                    if (f.Linhas.Count > 0) { c.Inferiores.Add(f); MontarFamilia(c, p, f); }
                }
            }
            if (aU != null)
            {
                var p = Nova(c, Funcoes.Superior + suf, Funcoes.Superior, "superior" + sufN, FormaBarra.U);
                double phi = Par(c, p, "bitola", aU.Bitola, "cálculo (elemento)");
                if (phi > 0)
                {
                    var f = new Familia
                    {
                        Ang = ang, Superior = true, Bitola = phi, Esp = Par(c, p, "espacamento", aU.Espacamento, "cálculo (elemento)"),
                        Z = c.hB - c.c - c.PhiLat / 10 - phi / 20 - (ang > 0 ? phi / 10 : 0),
                    };
                    f.Perna = Par(c, p, "perna", c.Perfil.Padrao("superior.perna", 20), "padrão do perfil (superior.perna)");
                    Malha(c, p, f, true);
                    if (f.Linhas.Count > 0) MontarFamilia(c, p, f);
                }
            }
        }
        Lateral(c);
        EstribosVerticais(c);
        if (c.Calice) Calice(c); else Arranque(c);
    }

    // ---------- principal: faixas sobre as estacas ----------

    private static void Principal(Ctx c)
    {
        var a = Arm(c, Funcoes.Principal);
        if (a == null) return;
        double zInf = Math.Max(c.c, c.emb);
        var faixas = new List<(double Ang, double T)>();
        if (c.Tri)
            for (int i = 0; i < 3; i++)
            {
                var pa = c.Estacas[i]; var pb = c.Estacas[(i + 1) % 3];
                double ang = Math.Atan2(pb.Y - pa.Y, pb.X - pa.X);
                if (ang < -1e-9) ang += Math.PI;
                if (ang >= Math.PI - 1e-9) ang -= Math.PI;
                faixas.Add((ang, Dot((-Math.Sin(ang), Math.Cos(ang)), pa)));
            }
        else
        {
            if (c.Estacas.Select(p => Math.Round(p.X, 3)).Distinct().Count() >= 2)
                foreach (var y in c.Estacas.Select(p => Math.Round(p.Y, 6)).Distinct()) faixas.Add((0, y));
            if (c.Estacas.Select(p => Math.Round(p.Y, 3)).Distinct().Count() >= 2)
                foreach (var x in c.Estacas.Select(p => Math.Round(p.X, 6)).Distinct()) faixas.Add((Math.PI / 2, -x));
        }
        int k = 0;
        foreach (var grupo in faixas.GroupBy(f => Math.Round(f.Ang, 6)))
        {
            k++;
            bool emY = Math.Abs(grupo.Key - Math.PI / 2) < 1e-6;
            string chave = c.Tri ? "principal_lado" + k : emY ? "principal_y" : "principal_x";
            string nome = c.Tri ? "principal lado " + k : emY ? "principal y" : "principal x";
            var p = Nova(c, chave, Funcoes.Principal, nome, FormaBarra.U);
            double phi = Par(c, p, "bitola", a.Bitola, "cálculo (elemento)");
            int q = ParInt(c, p, "quantidade", a.Quantidade, "cálculo: barras por faixa");
            double esp = Par(c, p, "espacamento", a.Espacamento, "cálculo: espaçamento na faixa");
            bool porValor = c.Perfil.Regra("principal.perna", "altura_base") == "valor" && c.Perfil.Padrao("principal.perna", 0) > 0;
            double perna = Par(c, p, "perna", porValor ? c.Perfil.Padrao("principal.perna", 0) : Math.Floor(c.hB - c.c - zInf),
                porValor ? "valor do perfil (principal.perna)" : "topo da base − cobrimento");
            if (phi <= 0 || q <= 0) continue;
            var f = new Familia
            {
                Ang = grupo.Key, Bitola = phi, Esp = esp, Perna = perna, PorFaixa = q,
                Z = zInf + phi / 20 + (emY && !c.Tri ? phi / 10 : 0),
            };
            // faixa centrada na linha de estacas; se passar da face, desloca para dentro (como o B31: 7 c/14 com face a 40)
            double lo = c.Contorno.Min(pt => Dot(f.N, pt)) + c.c + c.PhiLat / 10 + phi / 20;
            double hi = c.Contorno.Max(pt => Dot(f.N, pt)) - c.c - c.PhiLat / 10 - phi / 20;
            int faixasOk = 0;
            foreach (var (_, T) in grupo)
            {
                double t0 = T - (q - 1) / 2.0 * esp, t1 = T + (q - 1) / 2.0 * esp, d = 0;
                if (t0 < lo) d = lo - t0; else if (t1 > hi) d = hi - t1;
                double passo = q > 1 && t1 - t0 > hi - lo ? (hi - lo) / (q - 1) : esp;
                int antes = f.Linhas.Count;
                for (int i = 0; i < q; i++) AdicionarLinha(c, f, passo == esp ? t0 + d + i * esp : lo + i * passo);
                if (f.Linhas.Count > antes) faixasOk++;
            }
            if (f.Linhas.Count == 0) continue;
            f.Faixas = Math.Max(1, faixasOk);
            p.Parametros["faixas"] = f.Faixas;
            p.Regras["faixas"] = "uma por linha de estacas";
            // perna manual acima do topo da base − cobrimento
            double zFace = f.Z - phi / 20;
            if (p.Manuais.Contains("perna") && zFace + perna > c.hB - c.c + 0.5)
                c.M.Avisos.Add($"{chave}: perna manual {Num(perna)} cm passa do topo da base menos o cobrimento ({Num(Math.Floor(c.hB - c.c - zFace))} cm).");
            c.PhiPrinc = Math.Max(c.PhiPrinc, phi);
            c.Inferiores.Add(f);
            MontarFamilia(c, p, f);
        }
    }

    /// <summary>Uma barra da família na reta t (corda do contorno − cobrimento − lateral).</summary>
    private static void AdicionarLinha(Ctx c, Familia f, double t)
    {
        var o = (X: t * f.N.X, Y: t * f.N.Y);
        var cd = Corda(c.Contorno, o, f.U);
        if (cd == null) return;
        if (Corda(Encolher(c.Contorno, c.c + 0.5), o, f.U) == null) return;   // a reta fica fora do cobrimento
        double lat = c.PhiLat / 10;
        double a = cd.Value.A + c.c + lat, b = cd.Value.B - c.c - lat;
        if (b - a > 5) f.Linhas.Add(new Linha3 { T = t, A = a, B = b });
    }

    /// <summary>Malha: retas a cada espaçamento; a superior é interrompida no colarinho/cálice (dois trechos).</summary>
    private static void Malha(Ctx c, PosicaoArmadura p, Familia f, bool superior)
    {
        double t0 = c.Contorno.Min(pt => Dot(f.N, pt)), t1 = c.Contorno.Max(pt => Dot(f.N, pt));
        double vao = t1 - t0 - 2 * c.c;
        int n = ParInt(c, p, "quantidade", Qtd(c, vao, f.Esp), "vão (face a face − 2·cobrimento) / espaçamento");
        f.PorFaixa = n;
        bool emX = Math.Abs(f.Ang) < 1e-6;
        foreach (var s in Posicoes(c, vao, n, f.Esp))
        {
            double t = t0 + c.c + s;
            int antes = f.Linhas.Count;
            AdicionarLinha(c, f, t);
            if (!superior || !c.Calice || f.Linhas.Count == antes) continue;
            var ln = f.Linhas[f.Linhas.Count - 1];
            double coord = emX ? t : -t;   // em x: t = y; em y: N = (−1, 0) → t = −x
            double lo = emX ? (c.Colar ? c.Cy0 : c.Oy0) : (c.Colar ? c.Cx0 : c.Ox0), hi = emX ? (c.Colar ? c.Cy1 : c.Oy1) : (c.Colar ? c.Cx1 : c.Ox1);
            if (coord <= lo - c.c || coord >= hi + c.c) continue;
            double s0 = emX ? (c.Colar ? c.Cx0 : c.Ox0) : (c.Colar ? c.Cy0 : c.Oy0), s1 = emX ? (c.Colar ? c.Cx1 : c.Ox1) : (c.Colar ? c.Cy1 : c.Oy1);
            f.Linhas.RemoveAt(f.Linhas.Count - 1);
            // na direção y, s = coordenada ao longo de U = (0, 1) = y; em x, s = x
            if (s0 - c.c - ln.A > 10) f.Linhas.Add(new Linha3 { T = t, A = ln.A, B = s0 - c.c });
            if (ln.B - (s1 + c.c) > 10) f.Linhas.Add(new Linha3 { T = t, A = s1 + c.c, B = ln.B });
        }
        f.Faixas = 1;
    }

    /// <summary>Geometria 3D de uma família reta: barras, representativa, distribuição e VAR.</summary>
    private static void MontarFamilia(Ctx c, PosicaoArmadura p, Familia f)
    {
        var comps = f.Linhas.Select(l => Math.Round(l.B - l.A)).ToList();
        bool var = comps.Distinct().Count() > 1;
        double L = comps.Max();
        if (var)
        {
            p.Parametros["comprimento_reto"] = L;
            p.Regras["comprimento_reto"] = "face a face − 2·cobrimento − lateral (grupo VAR: maior barra)";
        }
        else L = Par(c, p, "comprimento_reto", L, "face a face − 2·cobrimento − lateral");
        double perna = f.Perna;
        double zh = f.Superior ? f.Z + f.Bitola / 20 : f.Z - f.Bitola / 20;   // face externa do trecho reto
        double zp = f.Superior ? zh - perna : zh + perna;
        var meios = new List<P3>();
        for (int i = 0; i < f.Linhas.Count; i++)
        {
            var l = f.Linhas[i];
            double Li = var ? comps[i] : L, m = (l.A + l.B) / 2, s0 = m - Li / 2, s1 = m + Li / 2;
            var a = f.Ponto(l.T, s0); var b = f.Ponto(l.T, s1);
            var pts = new List<P3>();
            if (perna > 0) pts.Add(new P3(a.X, a.Y, zp));
            pts.Add(new P3(a.X, a.Y, zh));
            pts.Add(new P3(b.X, b.Y, zh));
            if (perna > 0) pts.Add(new P3(b.X, b.Y, zp));
            p.Barras.Add(pts);
            var mm = f.Ponto(l.T, m);
            meios.Add(new P3(mm.X, mm.Y, zh));
        }
        if (perna <= 0) p.Forma = FormaBarra.Reta;
        p.Bitola = f.Bitola;
        p.Vertices = p.Barras[0];
        for (int i = 0; i + 1 < p.Vertices.Count; i++) p.Formatos.Add(FormatoSegmento.Linha);
        if (f.Faixas > 1 && f.Linhas.Count % f.Faixas != 0) f.Faixas = 1;   // alguma barra caiu fora: uma distribuição só
        int porFaixa = f.Linhas.Count / f.Faixas;
        // principal: passo real na faixa (pode ser menor que o pedido quando a faixa não cabe); malhas: o espaçamento
        double passo = p.Funcao == Funcoes.Principal && porFaixa > 1 ? Math.Abs(f.Linhas[1].T - f.Linhas[0].T) : f.Esp;
        p.Distribuicao = new Distribuicao
        {
            Inicio = meios[0], Fim = meios[Math.Max(0, porFaixa - 1)], Quantidade = porFaixa, Espacamento = passo, Repeticoes = f.Faixas,
        };
        if (var)
        {
            p.ComprimentosVar = comps.Select(x => x + 2 * perna).ToList();
            p.VerticesVar = p.Barras.Select(b => b.ToList()).ToList();
            p.SegmentosVar.Add(perna > 0 ? 1 : 0);
        }
        Adicionar(c, p);
    }

    // ---------- estribos (polígono fechado com os ganchos no canto superior esquerdo) ----------

    /// <summary>Estribo fechado no plano (u, v) pelo <see cref="GeradorEstribo.FechadoPoligono"/>; o mapa leva a 3D.</summary>
    private static void Estribo(Ctx c, PosicaoArmadura p, List<(double X, double Y)> pol, double phi, IEnumerable<Func<double, double, P3>> mapas)
    {
        int canto = GeradorEstribo.Canto(pol, PosicaoGanchos.SuperiorEsquerdo);
        double lg = Par(c, p, "gancho", c.Cfg.Dobramento.ComprimentoGancho(FormatoSegmento.Gancho90, phi, c.Aco, estribo: true),
            "gancho 90° (NBR 6118 tab. 9.2)");
        double passagem = c.Cfg.Dobramento.DiametroPino(phi, c.Aco, estribo: true) + 2 * phi / 10;
        var fo = GeradorEstribo.FechadoPoligono(pol, canto, FormatoSegmento.Gancho90, lg, passagem);
        foreach (var mapa in mapas) p.Barras.Add(fo.Vertices.Select(v => mapa(v.X, v.Y)).ToList());
        p.Formatos.AddRange(fo.Formatos);
        foreach (var s in fo.Sobrepostos) p.Sobrepostos.Add(s);
        p.Bitola = phi;
        p.Fechada = false;   // estribo fechado = forma Estribo; a poligonal (com os ganchos) é aberta
    }

    private static void Lateral(Ctx c)
    {
        var p = c.Lateral;
        if (p == null || c.PhiLat <= 0) return;
        var a = Arm(c, Funcoes.Lateral);
        double esp = Par(c, p, "espacamento", a.Espacamento, "cálculo (elemento)");
        double alt = c.hB - 2 * c.c;
        int n = ParInt(c, p, "quantidade", Qtd(c, alt, esp), "(altura da base − 2·cobrimento) / espaçamento");
        if (n <= 0) return;
        var zs = Posicoes(c, alt, n, esp).Select(z => z + c.c).ToList();
        var pol = Encolher(c.Contorno, c.c);
        p.Regras["contorno"] = "contorno do bloco − cobrimento";
        Estribo(c, p, pol, c.PhiLat, zs.Select(z => (Func<double, double, P3>)((x, y) => new P3(x, y, z))));
        var (cx, cy) = (pol.Average(q => q.X), pol.Average(q => q.Y));
        p.Distribuicao = new Distribuicao { Inicio = new P3(cx, cy, zs[0]), Fim = new P3(cx, cy, zs[zs.Count - 1]), Quantidade = zs.Count, Espacamento = esp };
        Adicionar(c, p);
    }

    private static void EstribosVerticais(Ctx c)
    {
        var a = Arm(c, Funcoes.EstriboVertical);
        if (a == null || c.Tri) return;
        var p = Nova(c, "estribo_vertical_x", Funcoes.EstriboVertical, "estribo vertical", FormaBarra.Estribo);
        double phi = Par(c, p, "bitola", a.Bitola, "cálculo (elemento)");
        double esp = Par(c, p, "espacamento", a.Espacamento, "cálculo (elemento)");
        if (phi <= 0) return;
        double vao = c.W - 2 * c.c;
        int n = ParInt(c, p, "quantidade", Qtd(c, vao, esp), "(comprimento − 2·cobrimento) / espaçamento, fora do cálice");
        var xs = Posicoes(c, vao, n, esp).Select(x => x + c.c).Where(x => !c.Calice || x < c.Cx0 || x > c.Cx1).ToList();
        if (xs.Count == 0) return;
        p.Parametros["quantidade"] = xs.Count;
        double w = c.H - 2 * c.c, h = c.hB - 2 * c.c;
        p.Parametros["largura"] = w; p.Regras["largura"] = "largura do bloco − 2·cobrimento";
        p.Parametros["altura"] = h; p.Regras["altura"] = "altura da base − 2·cobrimento";
        // plano yz: u = y, v = z
        Estribo(c, p, Ret(0, 0, w, h), phi, xs.Select(x => (Func<double, double, P3>)((u, v) => new P3(x, c.c + u, c.c + v))));
        p.Distribuicao = new Distribuicao
        {
            Inicio = new P3(xs[0], c.H / 2, c.hB / 2), Fim = new P3(xs[xs.Count - 1], c.H / 2, c.hB / 2), Quantidade = xs.Count, Espacamento = esp,
        };
        Adicionar(c, p);
    }

    // ---------- cálice ----------

    private static void Calice(Ctx c)
    {
        double cb = c.c;
        double wF = c.Colar ? c.Cx1 - c.Cx0 : c.Ox1 - c.Ox0 + 2 * c.t, hF = c.Colar ? c.Cy1 - c.Cy0 : c.Oy1 - c.Oy0 + 2 * c.t;
        double xw0 = c.Colar ? c.Cx0 : c.Ox0 - c.t, yw0 = c.Colar ? c.Cy0 : c.Oy0 - c.t;
        double w = wF - 2 * cb, h = hF - 2 * cb;
        var aT = Arm(c, Funcoes.CaliceTopo); var aR = Arm(c, Funcoes.Calice);
        var pT = aT != null ? Nova(c, "calice_topo", Funcoes.CaliceTopo, "cálice: horizontais do topo", FormaBarra.Estribo) : null;
        var pR = aR != null ? Nova(c, "calice", Funcoes.Calice, "cálice: horizontais", FormaBarra.Estribo) : null;
        double phiT = pT != null ? Par(c, pT, "bitola", aT.Bitola, "cálculo (elemento)") : 0;
        double phiR = pR != null ? Par(c, pR, "bitola", aR.Bitola, "cálculo (elemento)") : 0;
        int nT = pT != null && phiT > 0 ? ParInt(c, pT, "quantidade", aT.Quantidade, "cálculo (concentradas no topo, NBR 9062 7.7.4.3)") : 0;
        double espT = pT != null ? Par(c, pT, "espacamento", aT.Espacamento, "cálculo (elemento)") : 0;
        double zTopo = c.hT - cb - Math.Max(phiT, phiR) / 20;
        var zT = new List<double>();
        for (int i = 0; i < nT; i++) zT.Add(zTopo - i * espT);
        var zR = new List<double>();
        double espR = 0;
        if (pR != null && phiR > 0)
        {
            espR = Par(c, pR, "espacamento", aR.Espacamento, "cálculo (elemento)");
            double zIni = nT > 0 ? zT[zT.Count - 1] - espR : zTopo, zFim = c.hT - c.Lemb;
            if (zIni > zFim)
            {
                int n = ParInt(c, pR, "quantidade", Qtd(c, zIni - zFim, espR), "do topo concentrado até a base do pilar / espaçamento");
                zR = Posicoes(c, zIni - zFim, n, espR).Select(z => zIni - z).ToList();
            }
        }
        bool gr = c.Perfil.Padrao("calice.grampos", 1) != 0;
        var anel = Ret(xw0 + cb, yw0 + cb, xw0 + cb + w, yw0 + cb + h);
        var (acx, acy) = (xw0 + wF / 2, yw0 + hF / 2);
        foreach (var (p, phi, zs, esp, suf) in new[] { (pT, phiT, zT, espT, "_topo"), (pR, phiR, zR, espR, "") })
        {
            if (p == null || zs.Count == 0) continue;
            p.Parametros["largura"] = w; p.Regras["largura"] = "colarinho − 2·cobrimento";
            p.Parametros["altura"] = h; p.Regras["altura"] = "colarinho − 2·cobrimento";
            Estribo(c, p, anel, phi, zs.Select(z => (Func<double, double, P3>)((x, y) => new P3(x, y, z))));
            p.Distribuicao = new Distribuicao { Inicio = new P3(acx, acy, zs[0]), Fim = new P3(acx, acy, zs[zs.Count - 1]), Quantidade = zs.Count, Espacamento = esp };
            Adicionar(c, p);
            if (!gr) continue;
            foreach (bool emX in new[] { true, false })
            {
                string chave = "calice_grampos" + suf + (emX ? "_x" : "_y");
                var g = Nova(c, chave, p.Funcao, "cálice: grampos" + (suf.Length > 0 ? " do topo" : "") + (emX ? " (x)" : " (y)"), FormaBarra.U);
                double phiG = Par(c, g, "bitola", phi, "= horizontais" + (suf.Length > 0 ? " do topo" : ""));
                double pg = Par(c, g, "perna", Math.Round(c.t - cb - c.cc), "parede − cobrimento − cobrimento do cálice");
                double L = Par(c, g, "comprimento_reto", emX ? w : h, "colarinho − 2·cobrimento");
                if (phiG <= 0) continue;
                g.Parametros["quantidade"] = zs.Count; g.Parametros["espacamento"] = esp;
                double x0 = xw0 + cb, y0 = yw0 + cb, x1 = x0 + w, y1 = y0 + h;
                foreach (int lado in new[] { 0, 1 })
                    foreach (var z in zs)
                    {
                        List<P3> pts;
                        if (emX)
                        {
                            double xa = acx - L / 2, xb = acx + L / 2, yy = lado == 0 ? y0 : y1, sg = lado == 0 ? 1 : -1;
                            pts = new List<P3> { new P3(xa, yy + sg * pg, z), new P3(xa, yy, z), new P3(xb, yy, z), new P3(xb, yy + sg * pg, z) };
                        }
                        else
                        {
                            double ya = acy - L / 2, yb = acy + L / 2, xx = lado == 0 ? x0 : x1, sg = lado == 0 ? 1 : -1;
                            pts = new List<P3> { new P3(xx + sg * pg, ya, z), new P3(xx, ya, z), new P3(xx, yb, z), new P3(xx + sg * pg, yb, z) };
                        }
                        g.Barras.Add(pts);
                    }
                g.Bitola = phiG;
                g.Formatos.AddRange(new[] { FormatoSegmento.Linha, FormatoSegmento.Linha, FormatoSegmento.Linha });
                var ref0 = emX ? new P3(acx, y0, 0) : new P3(x0, acy, 0);
                g.Distribuicao = new Distribuicao
                {
                    Inicio = new P3(ref0.X, ref0.Y, zs[0]), Fim = new P3(ref0.X, ref0.Y, zs[zs.Count - 1]), Quantidade = zs.Count, Espacamento = esp, Repeticoes = 2,
                };
                Adicionar(c, g);
            }
        }

        // verticais em grampo nas paredes: da face de cima até acima da principal
        double zPrinc = c.ZPrinc;
        double phiMax = Math.Max(phiT, phiR);
        double altCalc = Math.Round(c.hT - cb - zPrinc), largCalc = Math.Max(4, Math.Round(c.t - cb - c.cc - 2 * phiMax / 10));
        var aV = Arm(c, Funcoes.CaliceVertical);
        if (aV != null)
        {
            var p = Nova(c, "calice_vertical", Funcoes.CaliceVertical, "cálice: verticais", FormaBarra.U);
            double phiV = Par(c, p, "bitola", aV.Bitola, "cálculo (elemento)");
            int nV = ParInt(c, p, "quantidade", aV.Quantidade, "cálculo: por parede");
            double larg = Par(c, p, "largura", largCalc, "parede − cobrimento − cobrimento do cálice − 2·ø horizontais");
            double alt = Par(c, p, "altura", altCalc, "topo − cobrimento − topo da armadura inferior");
            if (phiV > 0 && nV > 0)
            {
                double g = Par(c, p, "gancho", Math.Round(c.Cfg.Dobramento.ComprimentoGancho(FormatoSegmento.Gancho90, phiV, c.Aco, false)), "gancho 90° (NBR 6118 tab. 9.1)");
                double zt = c.hT - cb, zb = zt - alt;
                // paredes: y0 (de baixo), y1, x0 (esquerda), x1 — (ponto da face externa, direção para dentro, tangente)
                var paredes = new[]
                {
                    (Ext: (X: 0.0, Y: yw0 + cb), In: (X: 0.0, Y: 1.0), Tan: (X: 1.0, Y: 0.0), Ini: xw0, Comp: wF),
                    (Ext: (X: 0.0, Y: yw0 + hF - cb), In: (X: 0.0, Y: -1.0), Tan: (X: 1.0, Y: 0.0), Ini: xw0, Comp: wF),
                    (Ext: (X: xw0 + cb, Y: 0.0), In: (X: 1.0, Y: 0.0), Tan: (X: 0.0, Y: 1.0), Ini: yw0, Comp: hF),
                    (Ext: (X: xw0 + wF - cb, Y: 0.0), In: (X: -1.0, Y: 0.0), Tan: (X: 0.0, Y: 1.0), Ini: yw0, Comp: hF),
                };
                foreach (var pw in paredes)
                    for (int i = 0; i < nV; i++)
                    {
                        double s = pw.Ini + pw.Comp * (i + 1.0) / (nV + 1);
                        var o = (X: pw.Ext.X + pw.Tan.X * s, Y: pw.Ext.Y + pw.Tan.Y * s);   // face externa (a coordenada tangente vem de s)
                        var q = (X: o.X + pw.In.X * larg, Y: o.Y + pw.In.Y * larg);
                        p.Barras.Add(new List<P3>
                        {
                            new P3(o.X + pw.Tan.X * g, o.Y + pw.Tan.Y * g, zt), new P3(o.X, o.Y, zt), new P3(o.X, o.Y, zb),
                            new P3(q.X, q.Y, zb), new P3(q.X, q.Y, zt), new P3(q.X + pw.Tan.X * g, q.Y + pw.Tan.Y * g, zt),
                        });
                    }
                p.Bitola = phiV;
                for (int i = 0; i < 5; i++) p.Formatos.Add(FormatoSegmento.Linha);
                double espV = wF / (nV + 1);
                p.Distribuicao = new Distribuicao
                {
                    Inicio = Centro(p.Barras[0]), Fim = Centro(p.Barras[nV - 1]), Quantidade = nV, Espacamento = espV, Repeticoes = 4,
                };
                p.Parametros["espacamento"] = espV; p.Regras["espacamento"] = "parede dividida em quantidade + 1";
                Adicionar(c, p);
            }
        }

        // suspensão (NBR 9062 7.7.3.7): desce numa parede, passa sob o pilar e sobe na oposta (plano xz)
        var aS = Arm(c, Funcoes.Suspensao);
        if (aS != null)
        {
            var p = Nova(c, "suspensao", Funcoes.Suspensao, "suspensão", FormaBarra.Generica);
            double phiS = Par(c, p, "bitola", aS.Bitola, "cálculo (elemento)");
            int nS = ParInt(c, p, "quantidade", aS.Quantidade, "cálculo (elemento)");
            double espS = Par(c, p, "espacamento", aS.Espacamento, "cálculo (elemento)");
            double fundo = Par(c, p, "largura_fundo", Math.Round(c.Ox1 - c.Ox0 + 2 * cb), "abertura do cálice + 2·cobrimento");
            double larg = Par(c, p, "aba", largCalc, "= largura das verticais do cálice");
            double hi = Par(c, p, "perna", altCalc, "topo − cobrimento − topo da armadura inferior");
            double ho = Par(c, p, "perna_externa", hi - c.Perfil.Padrao("suspensao.desconto_perna_externa", 6),
                "perna − desconto do perfil (suspensao.desconto_perna_externa)");
            if (phiS > 0 && nS > 0)
            {
                double xi0 = c.Ox0 - cb, xi1 = xi0 + fundo, zt = c.hT - cb, zb = zt - hi, zo = zt - ho;
                for (int i = 0; i < nS; i++)
                {
                    double y = c.Py + (i - (nS - 1) / 2.0) * espS;
                    p.Barras.Add(new List<P3>
                    {
                        new P3(xi0 - larg, y, zo), new P3(xi0 - larg, y, zt), new P3(xi0, y, zt), new P3(xi0, y, zb),
                        new P3(xi1, y, zb), new P3(xi1, y, zt), new P3(xi1 + larg, y, zt), new P3(xi1 + larg, y, zo),
                    });
                }
                p.Bitola = phiS;
                for (int i = 0; i < 7; i++) p.Formatos.Add(FormatoSegmento.Linha);
                p.Distribuicao = new Distribuicao
                {
                    Inicio = new P3((xi0 + xi1) / 2, p.Barras[0][0].Y, zb), Fim = new P3((xi0 + xi1) / 2, p.Barras[nS - 1][0].Y, zb), Quantidade = nS, Espacamento = espS,
                };
                if (ho > hi) c.M.Avisos.Add("suspensao: perna externa maior que a interna — conferir.");
                Adicionar(c, p);
            }
        }

        // barras retas sob a base do pilar (fundo do cálice), em x, distribuídas em y
        var aF = Arm(c, Funcoes.FundoCalice);
        if (aF != null)
        {
            var p = Nova(c, "fundo_calice", Funcoes.FundoCalice, "sob a base do pilar", FormaBarra.Reta);
            double phiF = Par(c, p, "bitola", aF.Bitola, "cálculo (elemento)");
            int nF = ParInt(c, p, "quantidade", aF.Quantidade, "cálculo (elemento)");
            double L = Par(c, p, "comprimento", c.Perfil.Padrao("fundo_calice.comprimento", 100), "padrão do perfil (fundo_calice.comprimento)");
            double espF = Par(c, p, "espacamento", aF.Espacamento > 0 ? aF.Espacamento : nF > 1 ? (hF - 2 * cb) / (nF - 1) : 0,
                aF.Espacamento > 0 ? "cálculo (elemento)" : "largura do cálice − 2·cobrimento dividida igualmente");
            if (phiF > 0 && nF > 0)
            {
                double z = c.hT - c.Lemb - c.reg - cb;
                for (int i = 0; i < nF; i++)
                {
                    double y = c.Py + (i - (nF - 1) / 2.0) * espF;
                    p.Barras.Add(new List<P3> { new P3(c.Px - L / 2, y, z), new P3(c.Px + L / 2, y, z) });
                }
                p.Bitola = phiF;
                p.Formatos.Add(FormatoSegmento.Linha);
                p.Distribuicao = new Distribuicao
                {
                    Inicio = new P3(c.Px, p.Barras[0][0].Y, z), Fim = new P3(c.Px, p.Barras[nF - 1][0].Y, z), Quantidade = nF, Espacamento = espF,
                };
                Adicionar(c, p);
            }
        }
    }

    // ---------- arranque do pilar moldado in loco ----------

    private static void Arranque(Ctx c)
    {
        var a = Arm(c, Funcoes.Arranque);
        if (a == null) return;
        var el = c.El;
        var p = Nova(c, "arranque", Funcoes.Arranque, "arranque", FormaBarra.Generica);
        var aE = Arm(c, Funcoes.ArranqueEstribo);
        var pE = aE != null ? Nova(c, "arranque_estribo", Funcoes.ArranqueEstribo, "estribo do pilar no bloco", FormaBarra.Estribo) : null;
        double phiA = Par(c, p, "bitola", a.Bitola, "cálculo (elemento)");
        double phiE = pE != null ? Par(c, pE, "bitola", aE.Bitola, "cálculo (elemento)") : 0;
        if (phiA <= 0) return;
        double cp = el.ArranqueCobrimento;
        double d = cp + phiE / 10 + phiA / 20;
        int ax = Math.Max(1, el.ArranqueNx), ay = Math.Max(1, el.ArranqueNy);
        var xs = new List<double>(); var ys = new List<double>();
        for (int i = 0; i < ax; i++) xs.Add(ax == 1 ? c.Px : c.Px - c.Bx / 2 + d + i * (c.Bx - 2 * d) / (ax - 1));
        for (int j = 0; j < ay; j++) ys.Add(ay == 1 ? c.Py : c.Py - c.By / 2 + d + j * (c.By - 2 * d) / (ay - 1));
        var pontos = new List<(double X, double Y)>();
        for (int i = 0; i < ax; i++) for (int j = 0; j < ay; j++)
            if (i == 0 || j == 0 || i == ax - 1 || j == ay - 1) pontos.Add((xs[i], ys[j]));
        var tr = new RegrasTraspasse { Fck = el.Fck, PercentualEmendado = 100 };
        double interno = Par(c, p, "comprimento_interno", Math.Round(c.hT - c.ZPrinc), "topo do bloco − topo da armadura inferior");
        double esperaPerfil = c.Perfil.Padrao("arranque.espera", 0);
        double traspasse = Math.Ceiling(tr.Comprimento(phiA, c.Aco) / 5) * 5;
        double espera = Par(c, p, "espera", esperaPerfil > 0 ? esperaPerfil : traspasse,
            esperaPerfil > 0 ? "padrão do perfil (arranque.espera)" : "traspasse NBR 6118 (100 % emendado), a cada 5 cm");
        double pe = Par(c, p, "pe", c.Perfil.Padrao("arranque.pe", 20), "padrão do perfil (arranque.pe)");
        bool dentro = c.Perfil.Regra("arranque.pe_sentido", "fora") == "dentro";
        p.Regras["pe_sentido"] = dentro ? "para dentro (perfil arranque.pe_sentido)" : "para fora (perfil arranque.pe_sentido)";
        p.Parametros["quantidade"] = pontos.Count; p.Regras["quantidade"] = "barras por face (com os cantos) no contorno do pilar";
        double zf = c.hT - interno, zt = c.hT + espera;
        foreach (var (x, y) in pontos)
        {
            double nx = c.Bx > 0 ? (x - c.Px) / (c.Bx / 2) : 0, ny = c.By > 0 ? (y - c.Py) / (c.By / 2) : 0;
            double sg = dentro ? 1 : -1;
            var dir = Math.Abs(nx) >= Math.Abs(ny)
                ? (X: (Math.Abs(nx) < 1e-9 ? 1 : Math.Sign(c.Px - x) * sg), Y: 0.0)
                : (X: 0.0, Y: Math.Sign(c.Py - y) * sg);
            var pts = new List<P3>();
            if (pe > 0) pts.Add(new P3(x + dir.X * pe, y + dir.Y * pe, zf));
            pts.Add(new P3(x, y, zf));
            pts.Add(new P3(x, y, zt));
            p.Barras.Add(pts);
        }
        p.Bitola = phiA;
        for (int i = 0; i + 1 < p.Barras[0].Count; i++) p.Formatos.Add(FormatoSegmento.Linha);
        p.Distribuicao = new Distribuicao
        {
            Inicio = new P3(pontos[0].X, pontos[0].Y, zf), Fim = new P3(pontos[pontos.Count - 1].X, pontos[pontos.Count - 1].Y, zf), Quantidade = pontos.Count,
        };
        Adicionar(c, p);
        // verificações da ancoragem / espera (NBR 6118)
        double lb = Math.Ceiling(tr.Lb(phiA, c.Aco));
        if (interno < lb)
            c.M.Avisos.Add($"Arranque: trecho reto no bloco {Num(interno)} cm < lb = {Num(lb)} cm; com o pé (gancho) a NBR 6118 9.4.2.5 permite 0,7·lb = {Num(Math.Ceiling(0.7 * lb))} cm — conferir.");
        if (p.Manuais.Contains("espera") && espera < traspasse)
            c.M.Avisos.Add($"Arranque: espera manual {Num(espera)} cm < traspasse da NBR 6118 ({Num(traspasse)} cm) — conferir.");

        if (pE == null || phiE <= 0) return;
        int ne = ParInt(c, pE, "quantidade", aE.Quantidade, "cálculo (elemento)");
        if (ne <= 0) return;
        double zTop = c.hT - c.c - phiE / 20, zBot = Math.Min(zTop, zf + 5);
        double espE = Par(c, pE, "espacamento", aE.Espacamento > 0 ? aE.Espacamento : ne > 1 ? Math.Max(0, zTop - zBot) / (ne - 1) : 0,
            aE.Espacamento > 0 ? "cálculo (elemento)" : "do topo − cobrimento até 5 cm acima do pé, dividido igualmente");
        var zs = Enumerable.Range(0, ne).Select(i => zTop - i * espE).ToList();
        var pol = Ret(c.Px - c.Bx / 2 + cp, c.Py - c.By / 2 + cp, c.Px + c.Bx / 2 - cp, c.Py + c.By / 2 - cp);
        pE.Parametros["largura"] = c.Bx - 2 * cp; pE.Regras["largura"] = "pilar − 2·cobrimento do pilar";
        pE.Parametros["altura"] = c.By - 2 * cp; pE.Regras["altura"] = "pilar − 2·cobrimento do pilar";
        Estribo(c, pE, pol, phiE, zs.Select(z => (Func<double, double, P3>)((x, y) => new P3(x, y, z))));
        pE.Distribuicao = new Distribuicao { Inicio = new P3(c.Px, c.Py, zs[0]), Fim = new P3(c.Px, c.Py, zs[zs.Count - 1]), Quantidade = ne, Espacamento = espE };
        Adicionar(c, pE);
    }

    // ======================== verificações (iguais às do template) ========================

    private static void Verificar(Ctx c)
    {
        var avisos = c.M.Avisos;
        double R = c.D / 2;
        if (c.e < R) avisos.Add($"Distância do eixo da estaca à face ({Num(c.e)} cm) menor que o raio da estaca ({Num(R)} cm).");
        if (c.Px - c.Bx / 2 < 0 || c.Px + c.Bx / 2 > c.W || c.Py - c.By / 2 < 0 || c.Py + c.By / 2 > c.H) avisos.Add("O pilar sai do bloco.");
        if (c.Calice)
        {
            double h = Math.Max(c.Bx, c.By);
            if (c.Lemb < 40) avisos.Add($"Embutimento {Num(c.Lemb)} cm < 40 cm (NBR 9062 7.7.2.4).");
            double fator = c.El.Interface == InterfaceCalice.Chaves ? 1.2 : 1.5;
            if (c.Lemb < fator * h)
                avisos.Add($"Embutimento {Num(c.Lemb)} cm < {NumV(fator)}·h = {Num(Math.Ceiling(fator * h))} cm (menor valor da NBR 9062 Tabela 15) — conferir com Md/(Nd·h).");
            if (c.Colar && c.t < 15) avisos.Add($"Parede do colarinho {Num(c.t)} cm < 15 cm (NBR 9062 Fig. 25).");
            double fundo = c.hT - c.Lemb - c.reg;
            if (fundo < 20) avisos.Add($"Altura sob o cálice {Num(fundo)} cm < 20 cm (NBR 9062 Fig. 25).");
            if (c.Colar && (c.Ox0 - c.t < -1e-6 || c.Ox1 + c.t > c.W + 1e-6 || c.Oy0 - c.t < -1e-6 || c.Oy1 + c.t > c.H + 1e-6))
                avisos.Add("Colarinho maior que o bloco numa direção: limitado à face do bloco (como no B28).");
        }
        if (c.Tri && Arm(c, Funcoes.EstriboVertical) != null)
            avisos.Add("Bloco triangular: estribos verticais da base não são gerados (só a lateral horizontal em volta).");
    }
}
