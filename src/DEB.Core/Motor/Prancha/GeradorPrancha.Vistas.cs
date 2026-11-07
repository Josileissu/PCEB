using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using DEB.Core.Modelo;
using DEB.Core.Templates;

namespace DEB.Core.Motor.Prancha;

public sealed partial class GeradorPrancha
{
    /// <summary>Plano de corte vertical: "x" = paralelo a xz (y = C, s = x); "y" = paralelo a yz (x = C, s = y).</summary>
    private sealed class CorteInfo
    {
        public bool EmX = true;
        public double C;
        public P3 Normal => EmX ? Geo.Ey : Geo.Ex;
        public double N(P3 p) => EmX ? p.Y : p.X;
        public double S(P3 p) => EmX ? p.X : p.Y;
        public double N2((double X, double Y) p) => EmX ? p.Y : p.X;
        public double S2((double X, double Y) p) => EmX ? p.X : p.Y;
    }

    private sealed class SecaoInfo
    {
        public double A, B, MainA, MainB, ZBase, ZTopo, ZFundo;
        public List<(double S0, double S1, double Z0, double Z1)> Vazios = new List<(double, double, double, double)>();
        public List<(double S0, double S1, double Z0, double Z1)> Lastros = new List<(double, double, double, double)>();
        public List<(double S0, double S1, double Z0, double Z1)> Regs = new List<(double, double, double, double)>();
        public List<(double S0, double S1, double Z0, double Z1, CorpoConcreto Cp)> Outros = new List<(double, double, double, double, CorpoConcreto)>();
    }

    /// <summary>Plano do perfil ("x", "y", "x:95", "y=40"); sem coordenada, passa pelo centro do pilar (ou do corpo principal).</summary>
    private static CorteInfo LerCorte(Ctx c, string plano)
    {
        var k = new CorteInfo();
        string s = (plano ?? "").Trim().ToLowerInvariant();
        k.EmX = !s.StartsWith("y");
        var m = Regex.Match(s, @"[:=]\s*(-?[0-9]+(?:[.,][0-9]+)?)");
        if (m.Success && double.TryParse(m.Groups[1].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double v)) { k.C = v; return k; }
        var pil = c.M.Corpos.FirstOrDefault(cp => Papel(cp, VocabularioPrancha.Pilar));
        var ref_ = pil ?? c.Principal;
        if (ref_ != null)
        {
            var b = CaixaPlanta(ref_);
            if (!b.Vazio) k.C = k.EmX ? (b.Y0 + b.Y1) / 2 : (b.X0 + b.X1) / 2;
        }
        return k;
    }

    private static List<(double A, double B)> IntervalosCorpo(CorpoConcreto cp, CorteInfo k)
    {
        if (Circular(cp))
        {
            double d = k.N2(cp.Centro) - k.C;
            if (Math.Abs(d) >= cp.Raio) return new List<(double, double)>();
            double h = Math.Sqrt(cp.Raio * cp.Raio - d * d), s = k.S2(cp.Centro);
            return new List<(double, double)> { (s - h, s + h) };
        }
        if (cp.Planta == null || cp.Planta.Count < 3) return new List<(double, double)>();
        return Geo.Intervalos(cp.Planta, k.N2, k.S2, k.C);
    }

    private ResultadoTemplate MontarVista(Ctx c, VistaPerfil v) => v.Tipo switch
    {
        TipoVista.FormaPlanta => VistaFormaPlanta(c, v),
        TipoVista.FormaCorte => VistaFormaCorte(c, v),
        TipoVista.Planta => VistaPlanta(c, v),
        TipoVista.Corte => VistaCorte(c, v),
        TipoVista.Barras => VistaBarras(c, v),
        _ => null,
    };

    // ======================== seção (corte dos corpos) ========================

    /// <summary>Corpos cortados no plano: contorno da união do concreto do elemento (menos os vazios), lastro e regularização
    /// hachurados, pilar interrompido acima, estacas com a quebra embaixo. null = nada cortado.</summary>
    private static SecaoInfo Secao(Ctx c, ResultadoTemplate r, CorteInfo k)
    {
        double ht = c.ht;
        var s = new SecaoInfo();
        var rects = new List<(double S0, double S1, double Z0, double Z1, bool Vazio)>();
        foreach (var cp in c.M.Corpos.Where(cp => !Externo(cp)))
            foreach (var (a, b) in IntervalosCorpo(cp, k))
            {
                double z0 = Math.Min(cp.Z0, cp.Z1), z1 = Math.Max(cp.Z0, cp.Z1);
                rects.Add((a, b, z0, z1, cp.Vazio));
                if (cp.Vazio) s.Vazios.Add((a, b, z0, z1));
                else if (cp != c.Principal) s.Outros.Add((a, b, z0, z1, cp));
            }
        var cheios = rects.Where(q => !q.Vazio).ToList();
        var estacas = c.M.Corpos.Where(cp => Papel(cp, VocabularioPrancha.Estaca)).ToList();
        if (cheios.Count == 0 && estacas.Count == 0) return null;
        foreach (var laço in Geo.ContornoUniao(rects)) Polilinha(r, "ARM-FORMA", true, laço);
        if (cheios.Count > 0)
        {
            s.A = cheios.Min(q => q.S0); s.B = cheios.Max(q => q.S1);
            s.ZBase = cheios.Min(q => q.Z0); s.ZTopo = cheios.Max(q => q.Z1);
        }
        var main = c.Principal != null ? IntervalosCorpo(c.Principal, k) : new List<(double A, double B)>();
        s.MainA = main.Count > 0 ? main.Min(q => q.A) : s.A; s.MainB = main.Count > 0 ? main.Max(q => q.B) : s.B;

        // estacas cortadas (sem nenhuma, as projeções de todas)
        var cortadas = estacas.Select(cp => (cp, iv: IntervalosCorpo(cp, k))).Where(x => x.iv.Count > 0)
            .Select(x => (Cp: x.cp, S: (x.iv[0].A + x.iv[0].B) / 2, R: Circular(x.cp) ? x.cp.Raio : (x.iv[0].B - x.iv[0].A) / 2)).ToList();
        if (cortadas.Count == 0)
            cortadas = estacas.Select(cp => { var b = CaixaPlanta(cp); double sc = k.EmX ? (b.X0 + b.X1) / 2 : (b.Y0 + b.Y1) / 2;
                    return (Cp: cp, S: sc, R: Circular(cp) ? cp.Raio : (k.EmX ? b.Largura : b.Altura) / 2); })
                .GroupBy(x => Math.Round(x.S, 3)).Select(g => g.First()).ToList();

        // lastro e regularização
        foreach (var cp in c.M.Corpos.Where(cp => Papel(cp, VocabularioPrancha.Lastro)))
            foreach (var (a, b) in IntervalosCorpo(cp, k))
            {
                double z0 = Math.Min(cp.Z0, cp.Z1), z1 = Math.Max(cp.Z0, cp.Z1);
                s.Lastros.Add((a, b, z0, z1));
                double s0 = a;
                foreach (var e in cortadas.OrderBy(x => x.S).Select(x => (x.S, x.R)).Concat(new[] { (S: double.PositiveInfinity, R: 0.0) }))
                {
                    double s1 = Math.Min(b, e.S - e.R);
                    if (s1 - s0 > 0.5)
                        r.Hachuras.Add(new HachuraTemplate { Vertices = { (s0, z0), (s1, z0), (s1, z1), (s0, z1) }, Padrao = "AR-CONC", EscalaPadrao = c.esc / 50, Layer = "ARM-HACHURA-DETALHE" });
                    s0 = Math.Max(s0, e.S + e.R);
                    if (s0 >= b) break;
                }
                Ret(r, a, z0, b, z1);
            }
        foreach (var cp in c.M.Corpos.Where(cp => Papel(cp, VocabularioPrancha.Regularizacao)))
            foreach (var (a, b) in IntervalosCorpo(cp, k))
            {
                double z0 = Math.Min(cp.Z0, cp.Z1), z1 = Math.Max(cp.Z0, cp.Z1);
                s.Regs.Add((a, b, z0, z1));
                r.Hachuras.Add(new HachuraTemplate { Vertices = { (a, z0), (b, z0), (b, z1), (a, z1) }, Padrao = "AR-CONC", EscalaPadrao = c.esc / 50, Layer = "ARM-HACHURA-DETALHE" });
            }
        if (cheios.Count == 0)
        {
            var todos = cortadas.Select(x => x.S).ToList();
            s.A = s.MainA = todos.Min() - cortadas.Max(x => x.R); s.B = s.MainB = todos.Max() + cortadas.Max(x => x.R);
            s.ZBase = estacas.Max(cp => Math.Max(cp.Z0, cp.Z1)); s.ZTopo = s.ZBase;
        }

        // pilar interrompido acima
        double stub = Math.Max(20, 4 * ht);
        foreach (var cp in c.M.Corpos.Where(cp => Papel(cp, VocabularioPrancha.Pilar)))
            foreach (var (p0, p1) in IntervalosCorpo(cp, k))
            {
                double z0 = Math.Min(cp.Z0, cp.Z1), z1 = Math.Max(cp.Z0, cp.Z1), zt = Math.Min(z1, s.ZTopo + stub);
                Linha(r, "ARM-PILAR-SECAO", (p0, z0), (p0, zt));
                Linha(r, "ARM-PILAR-SECAO", (p1, z0), (p1, zt));
                if (z0 > s.ZBase + 0.5) Linha(r, "ARM-PILAR-SECAO", (p0, z0), (p1, z0));
                if (z1 > zt + 1e-6)
                {
                    double m = (p0 + p1) / 2;
                    Linha(r, "ARM-FORMA", (p0 - 2 * ht, zt), (m - 0.3 * ht, zt), (m - 0.1 * ht, zt + 0.7 * ht),
                        (m + 0.1 * ht, zt - 0.7 * ht), (m + 0.3 * ht, zt), (p1 + 2 * ht, zt));
                }
            }

        // estacas: 4 linhas e a quebra embaixo
        double zMin = Math.Min(s.ZBase, s.Lastros.Count > 0 ? s.Lastros.Min(q => q.Z0) : s.ZBase);
        double dMax = cortadas.Count > 0 ? cortadas.Max(x => 2 * x.R) : 0;
        s.ZFundo = zMin - (cortadas.Count > 0 ? Math.Max(25, 1.2 * dMax) : 0);
        foreach (var (cp, sc, R) in cortadas)
        {
            double zTop = Math.Max(cp.Z0, cp.Z1), zBot = Math.Max(Math.Min(cp.Z0, cp.Z1), s.ZFundo);
            foreach (double d in new[] { -R, -R / 2, R / 2, R }) Linha(r, "ARM-ESTACA", (sc + d, zTop), (sc + d, zBot));
            if (Math.Min(cp.Z0, cp.Z1) < s.ZFundo - 1e-6)
                r.Contornos.Add(new ContornoTemplate { Layer = "ARM-ESTACA", Vertices = { (sc - R, zBot), (sc, zBot), (sc + R, zBot) }, Bulges = { 0.5, -0.5, 0 } });
        }
        return s;
    }

    private static void Cadeia(Ctx c, ResultadoTemplate r, IEnumerable<double> vs, bool vertical, double pos)
    {
        var s = vs.Select(v => Math.Round(v, 2)).Distinct().OrderBy(v => v).ToList();
        for (int i = 0; i + 1 < s.Count; i++)
        {
            if (s[i + 1] - s[i] < 0.5) continue;
            if (vertical) Cota(c, r, (pos - 2 * c.ht, s[i]), (pos - 2 * c.ht, s[i + 1]), (pos, (s[i] + s[i + 1]) / 2), true);
            else Cota(c, r, (s[i], pos + 2 * c.ht), (s[i + 1], pos + 2 * c.ht), ((s[i] + s[i + 1]) / 2, pos), false);
        }
    }

    // ======================== forma: planta ========================

    private static void ContornoPlanta(ResultadoTemplate r, CorpoConcreto cp, string layer)
    {
        if (Circular(cp)) r.Circulos.Add(new CirculoTemplate { X = cp.Centro.X, Y = cp.Centro.Y, Raio = cp.Raio, Layer = layer });
        else if (cp.Planta != null && cp.Planta.Count >= 3)
        {
            Polilinha(r, layer, true, cp.Planta);
            if (cp.Vazio && cp.Planta.Count == 4)
            {
                Linha(r, layer, cp.Planta[0], cp.Planta[2]);
                Linha(r, layer, cp.Planta[1], cp.Planta[3]);
            }
        }
    }

    private ResultadoTemplate VistaFormaPlanta(Ctx c, VistaPerfil v)
    {
        double ht = c.ht;
        var corpos = c.M.Corpos.Where(cp => !Papel(cp, VocabularioPrancha.Lastro) && !Papel(cp, VocabularioPrancha.Regularizacao)).ToList();
        if (corpos.Count == 0) { c.Aviso($"Prancha: vista \"{v.Id}\" (forma em planta) sem corpos de concreto no modelo."); return null; }
        var r = new ResultadoTemplate();
        var estacas = new List<CorpoConcreto>();
        foreach (var cp in corpos)
        {
            if (Papel(cp, VocabularioPrancha.Estaca))
            {
                estacas.Add(cp);
                if (!Circular(cp)) { ContornoPlanta(r, cp, "ARM-FORMA"); continue; }
                double x = cp.Centro.X, y = cp.Centro.Y, R = cp.Raio;
                r.Circulos.Add(new CirculoTemplate { X = x, Y = y, Raio = R, Layer = "ARM-FORMA" });
                foreach (var (a0, a1) in new[] { (0.0, Math.PI / 2), (Math.PI, 1.5 * Math.PI) })
                {
                    var hc = new HachuraTemplate { Padrao = "SOLID", Layer = "ARM-HACHURA-DETALHE" };
                    hc.Vertices.Add((x, y));
                    for (int i = 0; i <= 12; i++) { double a = a0 + (a1 - a0) * i / 12; hc.Vertices.Add((x + R * Math.Cos(a), y + R * Math.Sin(a))); }
                    r.Hachuras.Add(hc);
                }
                Linha(r, "ARM-NIVEL", (x - R - 0.6 * ht, y), (x + R + 0.6 * ht, y));
                Linha(r, "ARM-NIVEL", (x, y - R - 0.6 * ht), (x, y + R + 0.6 * ht));
            }
            else ContornoPlanta(r, cp, Papel(cp, VocabularioPrancha.Pilar) ? "ARM-PILAR-SECAO" : "ARM-FORMA");
        }

        // cadeias: faces, estacas, corpos internos e pilar
        var lim = Geo.Limites(r, ht);
        var main = c.Principal != null ? CaixaPlanta(c.Principal) : lim;
        if (main.Vazio) main = lim;
        var centrosX = estacas.Select(cp => (CaixaPlanta(cp).X0 + CaixaPlanta(cp).X1) / 2).ToList();
        var centrosY = estacas.Select(cp => (CaixaPlanta(cp).Y0 + CaixaPlanta(cp).Y1) / 2).ToList();
        var xs = new List<double> { main.X0, main.X1 }; var ys = new List<double> { main.Y0, main.Y1 };
        xs.AddRange(centrosX); ys.AddRange(centrosY);
        foreach (var cp in corpos.Where(cp => cp != c.Principal && !cp.Vazio && (!Externo(cp) || Papel(cp, VocabularioPrancha.Pilar))))
        {
            var b = CaixaPlanta(cp);
            if (b.Vazio) continue;
            xs.Add(b.X0); xs.Add(b.X1); ys.Add(b.Y0); ys.Add(b.Y1);
        }
        double yb = Math.Min(main.Y0, lim.Y0), xr = Math.Max(main.X1, lim.X1);
        Cadeia(c, r, xs, false, yb - 3 * ht);
        var xsEst = new List<double> { main.X0, main.X1 }.Concat(centrosX).Select(q => Math.Round(q, 2)).Distinct().OrderBy(q => q).ToList();
        var xsTudo = xs.Select(q => Math.Round(q, 2)).Distinct().OrderBy(q => q).ToList();
        if (!xsEst.SequenceEqual(xsTudo)) Cadeia(c, r, xsEst, false, yb - 6.5 * ht);
        Cota(c, r, (main.X0, main.Y0), (main.X1, main.Y0), ((main.X0 + main.X1) / 2, yb - 10 * ht), false);
        Cadeia(c, r, ys, true, xr + 3 * ht);
        Cota(c, r, (main.X1, main.Y0), (main.X1, main.Y1), (xr + 7 * ht, (main.Y0 + main.Y1) / 2), true);

        // marcas dos cortes da forma
        foreach (var vc in (c.P.Folha?.Vistas ?? new List<VistaPerfil>()).Where(x => x != null && x.Tipo == TipoVista.FormaCorte))
        {
            var pt = c.Fmt.Formatar(vc.Titulo ?? "");
            int bar = pt.IndexOf('|'); if (bar >= 0) pt = pt.Substring(0, bar);
            var m = Regex.Match(pt, @"\b([A-Z0-9]{1,2})\s*-\s*\1\b");
            if (!m.Success) continue;
            string letra = m.Groups[1].Value;
            var k = LerCorte(c, vc.Plano);
            if (k.EmX)
                foreach (var x in new[] { lim.X0 - 3 * ht, xr + 10 * ht })
                {
                    Polilinha(r, "ARM-CORTE", true, new[] { (x - 0.8 * ht, k.C + 1.2 * ht), (x + 0.8 * ht, k.C + 1.2 * ht), (x, k.C) });
                    Texto(r, letra, x, k.C + 1.6 * ht, 1.2 * ht, 1, "ARM-CORTE");
                }
            else
                foreach (var y in new[] { yb - 13 * ht, lim.Y1 + 3 * ht })
                {
                    Polilinha(r, "ARM-CORTE", true, new[] { (k.C - 1.2 * ht, y + 0.8 * ht), (k.C - 1.2 * ht, y - 0.8 * ht), (k.C, y) });
                    Texto(r, letra, k.C - 1.6 * ht, y - 0.6 * ht, 1.2 * ht, 2, "ARM-CORTE");
                }
        }
        return r;
    }

    // ======================== forma: corte ========================

    private ResultadoTemplate VistaFormaCorte(Ctx c, VistaPerfil v)
    {
        double ht = c.ht;
        var r = new ResultadoTemplate();
        var k = LerCorte(c, v.Plano);
        var s = Secao(c, r, k);
        if (s == null) { c.Aviso($"Prancha: vista \"{v.Id}\" (corte da forma) não corta nenhum corpo."); return null; }
        double xl = s.MainA - 4 * ht;
        foreach (var (a, b, z0, z1) in s.Lastros.Take(1)) Cota(c, r, (s.MainA, z0), (s.MainA, z1), (xl, (z0 + z1) / 2), true);
        if (c.Principal != null)
        {
            double z0 = Math.Min(c.Principal.Z0, c.Principal.Z1), z1 = Math.Max(c.Principal.Z0, c.Principal.Z1);
            Cota(c, r, (s.MainA, z0), (s.MainA, z1), (xl, (z0 + z1) / 2), true);
            foreach (var o in s.Outros.Where(o => o.Z0 >= z1 - 0.5))
                Cota(c, r, (o.S0, o.Z0), (o.S0, o.Z1), (o.S0 - 4 * ht, (o.Z0 + o.Z1) / 2), true);
            if (s.ZTopo > z1 + 0.5 || s.ZBase < z0 - 0.5)
                Cota(c, r, (s.MainA, s.ZBase), (s.MainA, s.ZTopo), (xl - 3.5 * ht, (s.ZBase + s.ZTopo) / 2), true);
        }
        foreach (var vz in s.Vazios)
        {
            var zs = new List<double> { s.ZBase, vz.Z0, vz.Z1 };
            zs.AddRange(s.Regs.Where(g => g.S0 < vz.S1 && g.S1 > vz.S0).Select(g => g.Z1));
            Cadeia(c, r, zs, true, vz.S1 + 5 * ht);
        }
        foreach (var (a, b, z0, z1) in s.Lastros.Take(1))
            if (VocabularioPrancha.TemRotulo(c.P, c.Tipo, "papel." + VocabularioPrancha.Lastro))
                Linhas(c, r, c.Rot("papel." + VocabularioPrancha.Lastro), s.MainA - 2 * ht, z0 - 3 * ht, 0.8 * ht, 2,
                    new Dictionary<string, string> { ["e"] = Num(z1 - z0) });
        foreach (var (a, b, z0, z1) in s.Regs.Take(1))
            if (VocabularioPrancha.TemRotulo(c.P, c.Tipo, "papel." + VocabularioPrancha.Regularizacao))
                Linhas(c, r, c.Rot("papel." + VocabularioPrancha.Regularizacao), (a + b) / 2, z0 - 2 * ht, 0.7 * ht, 1,
                    new Dictionary<string, string> { ["e"] = Num(z1 - z0) });
        Niveis(c, r, Math.Max(s.B, s.MainB) + 8 * ht);
        return r;
    }

    /// <summary>Níveis à direita do corte (só os que têm rótulo "nivel.&lt;nome&gt;"), um texto por pilar.</summary>
    private static void Niveis(Ctx c, ResultadoTemplate r, double x)
    {
        double ht = c.ht;
        string refNome = c.Fmt.Formatar(c.Rot("nivel_referencia", "topo"));
        bool temRef = c.M.Niveis.TryGetValue(refNome, out double zRef);
        var lin = (c.E?.Niveis ?? new List<NivelPilar>()).Where(n => n != null).ToList();
        if (lin.Count > 0 && !temRef) c.Aviso($"Prancha: o modelo não tem o nível \"{refNome}\" (referência dos níveis por pilar); cotas não escritas.");
        foreach (var kv in c.M.Niveis.Where(kv => VocabularioPrancha.TemRotulo(c.P, c.Tipo, "nivel." + kv.Key)).OrderByDescending(kv => kv.Value))
        {
            double z = kv.Value;
            Linha(r, "ARM-NIVEL", (x - 6 * ht, z), (x + 14 * ht, z));
            Polilinha(r, "ARM-NIVEL", true, new[] { (x - 0.7 * ht, z + 1.2 * ht), (x + 0.7 * ht, z + 1.2 * ht), (x, z) });
            var rot = c.L(c.Rot("nivel." + kv.Key));
            double y = z + 1.1 * ht + 1.2 * ht * rot.Length + 0.2 * ht + 1.3 * ht * Math.Max(0, (temRef ? lin.Count : 0) - 1);
            if (temRef)
                foreach (var l in lin)
                {
                    Texto(r, $"{l.Pilar}={NumV(l.CotaM - (zRef - z) / 100)} m", x + 1.5 * ht, y, 0.8 * ht);
                    y -= 1.3 * ht;
                }
            double yr = z + 1.1 * ht + 1.2 * ht * (rot.Length - 1);
            foreach (var l in rot) { Texto(r, l, x + 1.5 * ht, yr, 0.8 * ht); yr -= 1.2 * ht; }
        }
    }

    // ======================== armadura: planta ========================

    private ResultadoTemplate VistaPlanta(Ctx c, VistaPerfil v)
    {
        double ht = c.ht;
        var infos = c.Pos.Where(i => Filtra(v, i.P)).ToList();
        if (infos.Count == 0) { c.Aviso($"Prancha: vista \"{v.Id}\" sem posições para mostrar."); return null; }
        var r = new ResultadoTemplate();
        var pts3 = infos.SelectMany(i => i.Inst).SelectMany(x => x).ToList();
        double zMin = pts3.Min(p => p.Z), zMax = pts3.Max(p => p.Z);
        foreach (var cp in c.M.Corpos)
        {
            if (Papel(cp, VocabularioPrancha.Lastro) || Papel(cp, VocabularioPrancha.Regularizacao) || Papel(cp, VocabularioPrancha.Pilar)) continue;
            if (Papel(cp, VocabularioPrancha.Estaca)) { ContornoPlanta(r, cp, "ARM-ESTACA"); continue; }
            double z0 = Math.Min(cp.Z0, cp.Z1), z1 = Math.Max(cp.Z0, cp.Z1);
            if (cp == c.Principal || (z0 <= zMax + 10 && z1 >= zMin - 10)) ContornoPlanta(r, cp, "ARM-FORMA");
        }
        var pend = new List<(Info I, List<(double X, double Y)> Refs)>();
        foreach (var i in infos)
        {
            bool casa = i.Casa == v.Id && !i.Dobrada;
            var proj = i.Inst.Select(inst => Geometria2D.SemRepetidos(inst.Select(p => (p.X, p.Y)))).ToList();
            var rep = Geometria2D.SemRepetidos(i.P.Vertices.Select(p => (p.X, p.Y)));
            if (proj.All(pp => Geometria2D.Extensao(pp) < 0.5))
            {
                foreach (var pp in proj) Ponto(r, pp[0].X, pp[0].Y, i.P.Bitola);
                pend.Add((i, proj.Select(pp => pp[0]).Take(3).ToList()));
                continue;
            }
            var desenhados = new List<(double X, double Y)>();
            if (casa)
            {
                r.Barras.Add(CriarBarraNaVista(c, i, i.P.Vertices, p => (p.X, p.Y), Geo.Ex, Geo.Ey));
                desenhados.Add(Geometria2D.Centro(i.P.TemArcos ? Geometria2D.SemRepetidos(proj[0]) : rep));
            }
            foreach (var pp in proj)
            {
                var cen = Geometria2D.Centro(pp);
                if (desenhados.Any(d => Geometria2D.Dist(d, cen) < 0.5)) continue;
                desenhados.Add(cen);
                Polilinha(r, "ARM-BARRA-DETALHE", i.P.Fechada, pp);
            }
            if (i.Var) TextosVar(c, r, i, p => (p.X, p.Y));
            var d = i.P.Distribuicao;
            var dp = (X: d.Fim.X - d.Inicio.X, Y: d.Fim.Y - d.Inicio.Y);
            var ms = Geometria2D.MaiorSegmento(rep);
            if (d.Quantidade > 1 && Math.Sqrt(dp.X * dp.X + dp.Y * dp.Y) > 0.5 && ms != null)
            {
                var (a, b, u) = ms.Value;
                var q0 = (X: a.X + 0.3 * (b.X - a.X), Y: a.Y + 0.3 * (b.Y - a.Y));
                var q1 = (X: q0.X + dp.X, Y: q0.Y + dp.Y);
                bool eixo = Math.Abs(u.X) < 1e-6 || Math.Abs(u.Y) < 1e-6;
                r.Faixas.Add(new FaixaTemplate
                {
                    Barra = i.Nome, Tipo = eixo ? TipoFaixa.Ortogonal : TipoFaixa.Alinhada, Espacamento = d.Espacamento, Escala = c.esc,
                    MostrarDiametro = false, Repeticoes = Math.Max(1, d.Repeticoes), P1 = q0, P2 = q1,
                    Posicao = ((q0.X + q1.X) / 2 - 2 * ht * u.X, (q0.Y + q1.Y) / 2 - 2 * ht * u.Y),
                });
            }
            else if (!casa && ms != null)
                pend.Add((i, new List<(double X, double Y)> { ((ms.Value.A.X + ms.Value.B.X) / 2, (ms.Value.A.Y + ms.Value.B.Y) / 2) }));
        }
        ColocarIndicacoesDireita(c, r, pend);
        if (v.BarrasAbaixo) BarrasAbaixo(c, r, v);
        return r;
    }

    /// <summary>Comprimento do trecho variável de cada barra VAR escrito na ponta dele ("L" das folhas BPM).</summary>
    private static void TextosVar(Ctx c, ResultadoTemplate r, Info i, Func<P3, (double X, double Y)> proj)
    {
        double ht = c.ht;
        foreach (var inst in i.Inst)
        {
            int k = -1;
            if (i.P.SegmentosVar != null && i.P.SegmentosVar.Count > 0) k = i.P.SegmentosVar.Min();
            if (k < 0 || k + 1 >= inst.Count)
            {
                double best = -1;
                for (int j = 0; j + 1 < inst.Count; j++) { double l = (inst[j + 1] - inst[j]).Comprimento; if (l > best) { best = l; k = j; } }
            }
            if (k < 0 || k + 1 >= inst.Count) continue;
            double L = (inst[k + 1] - inst[k]).Comprimento;
            var a = proj(inst[k]); var b = proj(inst[k + 1]);
            double dl = Geometria2D.Dist(a, b);
            if (dl < 1e-6) continue;
            var u = ((b.X - a.X) / dl, (b.Y - a.Y) / dl);
            double ang = Math.Atan2(u.Item2, u.Item1);
            if (ang > Math.PI / 2 + 1e-6 || ang < -Math.PI / 2 - 1e-6) { (a, b) = (b, a); u = (-u.Item1, -u.Item2); ang = Math.Atan2(u.Item2, u.Item1); }
            Texto(r, Num(Math.Round(L)), b.X + 0.6 * ht * u.Item1 + 0.3 * ht * Math.Sin(ang), b.Y + 0.6 * ht * u.Item2 - 0.3 * ht * Math.Cos(ang), 0.6 * ht, 0, "ARM-TEXTO", ang);
        }
    }

    private static void ColocarIndicacoesDireita(Ctx c, ResultadoTemplate r, List<(Info I, List<(double X, double Y)> Refs)> pend)
    {
        if (pend.Count == 0) return;
        var lim = Geo.Limites(r, c.ht);
        double x = lim.X1 + 4 * c.ht, y = lim.Y1 - 2 * c.ht;
        foreach (var (i, refs) in pend) { Indicacao(c, r, i, (x, y), refs.Select(q => (q.X, q.Y))); y -= 3 * c.ht; }
    }

    /// <summary>Formas dobradas das posições desta vista, uma embaixo da outra, abaixo do conteúdo.</summary>
    private static void BarrasAbaixo(Ctx c, ResultadoTemplate r, VistaPerfil v)
    {
        var lim = Geo.Limites(r, c.ht);
        double y = lim.Y0 - 6 * c.ht;
        foreach (var i in c.Pos.Where(i => i.Casa == v.Id && i.Dobrada))
        {
            var b = Dobrada(c, i, lim.X0, y);
            if (b.Vertices.Count == 0) continue;
            r.Barras.Add(b);
            double h = b.Vertices.Max(q => q.Y) - b.Vertices.Min(q => q.Y);
            y -= h + 9 * c.ht;
        }
    }

    // ======================== armadura: corte ========================

    private ResultadoTemplate VistaCorte(Ctx c, VistaPerfil v)
    {
        double ht = c.ht;
        var r = new ResultadoTemplate();
        var k = LerCorte(c, v.Plano);
        var s = Secao(c, r, k);
        var infos = c.Pos.Where(i => Filtra(v, i.P)).ToList();
        if (s == null && infos.Count == 0) { c.Aviso($"Prancha: vista \"{v.Id}\" (corte) sem nada para mostrar."); return null; }
        var esq = new List<(Info I, List<(double X, double Y)> Refs, double Z)>();
        var dir = new List<(Info I, List<(double X, double Y)> Refs)>();
        var topo = new List<(Info I, (double X, double Y) Ref)>();
        foreach (var i in infos)
        {
            bool casa = i.Casa == v.Id && !i.Dobrada;
            if (Geo.VerdadeiraGrandeza(i.Plano, k.Normal))
            {
                // no plano: a barra mais perto do corte em verdadeira grandeza
                int iInst = Enumerable.Range(0, i.Inst.Count).OrderBy(j => Math.Abs(i.Inst[j].Average(p => k.N(p)) - k.C)).First();
                var inst = i.Inst[iInst];
                var pts = inst.Select(p => (k.S(p), p.Z)).ToList();
                if (casa && i.P.TemArcos && iInst < i.InstBruta.Count)
                    r.Barras.Add(CriarBarraNaVista(c, i, i.InstBruta[iInst], p => (k.S(p), p.Z), k.EmX ? Geo.Ex : Geo.Ey, Geo.Ez));
                else if (casa) r.Barras.Add(CriarBarra(c, i, pts));
                else
                {
                    var pp = Geometria2D.SemRepetidos(pts);
                    Polilinha(r, "ARM-BARRA-DETALHE", i.P.Fechada, pp);
                    // referência no trecho mais à direita (a chamada fica à direita do corte)
                    int nseg = i.P.Fechada && pp.Count > 2 ? pp.Count : pp.Count - 1;
                    var meios = Enumerable.Range(0, Math.Max(0, nseg)).Select(j => (X: (pp[j].X + pp[(j + 1) % pp.Count].X) / 2, Y: (pp[j].Y + pp[(j + 1) % pp.Count].Y) / 2)).ToList();
                    if (meios.Count > 0)
                        dir.Add((i, new List<(double X, double Y)> { meios.OrderByDescending(q => Math.Round(q.X, 3)).ThenByDescending(q => q.Y).First() }));
                }
            }
            else if (i.P.Forma == FormaBarra.Estribo && !i.Plano.Reta && Math.Abs(Geo.Dot(i.Plano.N, k.Normal)) < 1e-3)
            {
                // estribo de perfil: uma linha por estribo
                (double X, double Y)? primeiro = null;
                foreach (var inst in i.Inst)
                {
                    var pp = inst.Select(p => (X: k.S(p), Y: p.Z)).OrderBy(q => q.X).ThenBy(q => q.Y).ToList();
                    var a = pp.First(); var b = pp.Last();
                    if (Geometria2D.Dist(a, b) < 1e-6) continue;
                    Linha(r, "ARM-ESTRIBO-SECAO", a, b);
                    primeiro ??= (a.X + 0.7 * (b.X - a.X), a.Y + 0.7 * (b.Y - a.Y));
                }
                if (primeiro != null) topo.Add((i, primeiro.Value));
            }
            else
            {
                // atravessadas: pontos
                var pts = new List<(double X, double Y)>();
                foreach (var inst in i.Inst)
                {
                    int n = inst.Count, nseg = i.P.Fechada && n > 2 ? n : n - 1;
                    for (int j = 0; j < nseg; j++)
                    {
                        var a = inst[j]; var b = inst[(j + 1) % n];
                        double na = k.N(a) - k.C, nb = k.N(b) - k.C;
                        if (Math.Abs(na) < 1e-9 && Math.Abs(nb) < 1e-9) continue;
                        if ((na < 0 && nb >= 0) || (na >= 0 && nb < 0))
                        {
                            double t = na / (na - nb);
                            var q = a + (b - a) * t;
                            var p2 = (k.S(q), q.Z);
                            if (!pts.Any(o => Geometria2D.Dist(o, p2) < 0.05)) pts.Add(p2);
                        }
                    }
                }
                foreach (var (x, z) in pts) Ponto(r, x, z, i.P.Bitola);
                if (pts.Count > 0) esq.Add((i, pts.OrderBy(q => q.X).Take(3).ToList(), pts.Average(q => q.Y)));
            }
        }
        var lim = Geo.Limites(r, ht);
        double a0 = s?.A ?? lim.X0, b0 = s != null ? Math.Max(s.B, s.MainB) : lim.X1;
        double zMeio = s != null ? (s.ZBase + s.ZTopo) / 2 : (lim.Y0 + lim.Y1) / 2, zTopo = s?.ZTopo ?? lim.Y1;
        int kInf = 0, kSup = 0;
        foreach (var (i, refs, z) in esq.OrderBy(q => q.Z))
        {
            double ty = z > zMeio ? z + (1.5 + 1.8 * kSup++) * ht : z - (1.5 + 1.8 * kInf++) * ht;
            Indicacao(c, r, i, (a0 - 5 * ht, ty), refs.Select(q => (q.X, q.Y)));
        }
        double yd = double.PositiveInfinity;
        foreach (var (i, refs) in dir.OrderByDescending(q => q.Refs[0].Y))
        {
            yd = Math.Min(yd - 3 * ht, refs[0].Y + ht);
            Indicacao(c, r, i, (b0 + 5 * ht, yd), refs.Select(q => (q.X, q.Y)));
        }
        int kt = 0;
        foreach (var (i, rf) in topo) { Indicacao(c, r, i, (rf.X - 3 * ht, zTopo + (3 + 2.5 * kt++) * ht), new[] { (rf.X, rf.Y) }); }
        if (v.BarrasAbaixo) BarrasAbaixo(c, r, v);
        return r;
    }

    // ======================== barras ========================

    private ResultadoTemplate VistaBarras(Ctx c, VistaPerfil v)
    {
        var infos = c.Pos.Where(i => i.Casa == v.Id && i.Dobrada).ToList();
        if (infos.Count == 0) { c.Aviso($"Prancha: vista \"{v.Id}\" (barras) sem posições para mostrar."); return null; }
        var r = new ResultadoTemplate();
        double x = 0;
        foreach (var i in infos)
        {
            var b = Dobrada(c, i, x, 0);
            if (b.Vertices.Count == 0) continue;
            r.Barras.Add(b);
            double w = b.Vertices.Max(q => q.X) - b.Vertices.Min(q => q.X);
            x += Math.Max(w, 6 * c.ht) + 14 * c.ht;
        }
        return r;
    }
}
