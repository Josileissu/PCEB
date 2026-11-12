using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace DEB.Core.Lajes;

// =====================================================================================================================
// Paginação de vários pisos (panos) em coordenadas de planta (metros, coordenadas do modelo): converte cada piso para o
// sistema do pano, roda o motor, numera os panos e dá as marcas (painéis, faixas maciças, juntas, tirantes).
// É a camada que o comando do Revit usa (e a conferência com o protótipo).
// =====================================================================================================================

/// <summary>Viga em planta: linha de localização (P0 → P1) e seção medida em linhas horizontais (coordenada ao longo de n = (−dy, dx)).</summary>
public sealed class VigaPlanta
{
    public string Id = "", Tipo = "";
    public (double X, double Y) P0, P1;
    public SecaoViga Secao = new SecaoViga();
    public (double X, double Y) Direcao { get { double dx = P1.X - P0.X, dy = P1.Y - P0.Y, l = Math.Sqrt(dx * dx + dy * dy); return l < 1e-12 ? (1, 0) : (dx / l, dy / l); } }
    public (double X, double Y) N { get { var d = Direcao; return (-d.Y, d.X); } }
}

/// <summary>Pilar em planta: pontos da seção na altura do painel (a caixa no sistema do pano vira o recorte).</summary>
public sealed class PilarPlanta
{
    public string Id = "";
    public List<(double X, double Y)> Pontos = new List<(double, double)>();
}

/// <summary>Um piso a paginar com o que está em volta dele.</summary>
public sealed class PisoPlanta
{
    public string Id = "", Nome = "";
    /// <summary>Ângulo do vão em planta (Floor.SpanDirectionAngle), radianos.</summary>
    public double AnguloVao;
    /// <summary>Topo do piso (inclui a capa), metros.</summary>
    public double ZTopo;
    /// <summary>1º = contorno externo, demais = furos (croqui do piso).</summary>
    public List<List<(double X, double Y)>> Loops = new List<List<(double, double)>>();
    public List<VigaPlanta> Vigas = new List<VigaPlanta>();
    public List<PilarPlanta> Pilares = new List<PilarPlanta>();
}

public sealed class PecaSaida
{
    public string Marca, Tipo;
    public PecaPainel Peca;
    public (double X, double Y) EixoIni, EixoFim;
    public (double X, double Y)[] Cantos;
}

public sealed class MacicaSaida
{
    public string Marca;
    public FaixaMacica Faixa;
    public (double X, double Y)[] Cantos;
}

public sealed class JuntaSaida
{
    public string Marca, EntreA, EntreB;
    public JuntaPaineis Junta;
    public (double X, double Y) Ini, Fim;
}

public sealed class TiranteSaida
{
    public string Marca, Junta;
    public double X, Y;
    /// <summary>Direção da junta em planta (= direção do vão).</summary>
    public (double X, double Y) DirJunta;
}

public sealed class ConsoloSaida
{
    public ConsoloAColocar Consolo;
    public (double X, double Y) Ini, Fim;
    public List<string> FaixasMacicas = new List<string>();
    public List<string> PecasApoiadas = new List<string>();
}

/// <summary>Alerta de uma ponta de peça (lista de avisos ao usuário).</summary>
public sealed class AlertaPonta
{
    public string Marca, Ponta, Texto;
    public double X, Y;
}

/// <summary>Resultado de um piso, em planta.</summary>
public sealed class PanoPaginado
{
    public int Numero;
    public PisoPlanta Piso;
    public SistemaPano Sistema;
    public ResultadoPano Resultado;
    public double ZTopoPainel, ZBasePainel;
    public List<PecaSaida> Pecas = new List<PecaSaida>();
    public List<MacicaSaida> Macicas = new List<MacicaSaida>();
    public List<JuntaSaida> Juntas = new List<JuntaSaida>();
    public List<TiranteSaida> Tirantes = new List<TiranteSaida>();
    public List<ConsoloSaida> Consolos = new List<ConsoloSaida>();
    public List<AlertaPonta> AlertasPontas = new List<AlertaPonta>();
    /// <summary>Erro que impediu a paginação deste piso (null = ok).</summary>
    public string Erro;
}

public static class PaginacaoObra
{
    /// <summary>Converte as vigas de planta para o sistema do pano: as que correm em v (apoios) e as que correm em u.</summary>
    public static (List<VigaTransversal> V, List<VigaLongitudinal> U) VigasNoPano(IEnumerable<VigaPlanta> vigas, SistemaPano s)
    {
        var tv = new List<VigaTransversal>(); var tu = new List<VigaLongitudinal>();
        foreach (var vg in vigas)
        {
            var d = vg.Direcao; var (du, dv) = s.VetorUV(d.X, d.Y);
            var a = s.UV(vg.P0.X, vg.P0.Y); var b = s.UV(vg.P1.X, vg.P1.Y);
            var n = vg.N; var (nu, nv) = s.VetorUV(n.X, n.Y);
            if (Math.Abs(dv) >= 0.99)
                tv.Add(new VigaTransversal { Id = vg.Id, Tipo = vg.Tipo, UEixo = a.U, Va = Math.Min(a.V, b.V), Vb = Math.Max(a.V, b.V), NU = nu, Secao = vg.Secao });
            if (Math.Abs(du) >= 0.99)
                tu.Add(new VigaLongitudinal { Id = vg.Id, Tipo = vg.Tipo, VEixo = a.V, Ua = a.U, Ub = b.U, NV = nv, Secao = vg.Secao });
        }
        return (tv, tu);
    }

    private static string D2(int n) => n.ToString("00", CultureInfo.InvariantCulture);
    private const string Letras = "abcdefghijklmnopqrstuvwxyz";

    /// <summary>Pagina os pisos; numera os panos a partir de <paramref name="numeroInicial"/> na ordem (x arredondado ao metro, depois y decrescente).</summary>
    public static List<PanoPaginado> Paginar(IEnumerable<PisoPlanta> pisos, ConfigPaginacao cfg, int numeroInicial = 1)
    {
        var lista = pisos.Where(p => p.Loops.Count > 0 && p.Loops[0].Count >= 3)
            .Select(p => (P: p, Cx: (p.Loops[0].Min(q => q.X) + p.Loops[0].Max(q => q.X)) / 2, Cy: (p.Loops[0].Min(q => q.Y) + p.Loops[0].Max(q => q.Y)) / 2))
            .OrderBy(t => Math.Round(t.Cx, 0, MidpointRounding.ToEven)).ThenBy(t => -t.Cy).ToList();
        var r = new List<PanoPaginado>();
        int num = numeroInicial;
        foreach (var (piso, _, _) in lista)
        {
            var pp = new PanoPaginado { Numero = num++, Piso = piso, Sistema = new SistemaPano(piso.AnguloVao) };
            pp.ZTopoPainel = piso.ZTopo - cfg.Capa;
            pp.ZBasePainel = pp.ZTopoPainel - cfg.AlturaPainel;
            try { PaginarUm(pp, cfg); }
            catch (Exception ex) { pp.Erro = ex.Message; }
            r.Add(pp);
        }
        return r;
    }

    private static void PaginarUm(PanoPaginado pp, ConfigPaginacao cfg)
    {
        var S = pp.Sistema; var piso = pp.Piso;
        var loops = piso.Loops.Select(l => (IReadOnlyList<PontoUV>)l.Select(q => S.UV(q.X, q.Y)).ToList()).ToList();
        var pil = piso.Pilares.Where(p => p.Pontos.Count > 0).Select(p => new PilarPano { Id = p.Id, Ret = S.CaixaUV(p.Pontos) }).ToList();
        var (vv, vu) = VigasNoPano(piso.Vigas, S);
        var R = MotorPaginacao.PaginarPano(loops, vv, pil, cfg, vu);
        pp.Resultado = R;
        var ins = cfg.Insercao;
        string n2 = D2(pp.Numero);

        var ps = R.Paineis.OrderBy(q => q.V0).ToList();
        for (int i = 0; i < ps.Count; i++)
        {
            var q = ps[i];
            q.Marca = $"{ins.PrefixoPainel}{n2}-{D2(i + 1)}";
            for (int k = 0; k < q.Pecas.Count; k++)
            {
                var pc = q.Pecas[k];
                pc.Marca = q.Marca + (q.Pecas.Count > 1 ? Letras[k % Letras.Length].ToString() : "");
                double vm = (pc.V0 + pc.V1) / 2;
                var sai = new PecaSaida
                {
                    Marca = pc.Marca, Peca = pc, Tipo = q.Tipo + (q.Pecas.Count > 1 ? "+peca_dividida" : ""),
                    EixoIni = S.XY(pc.U0, vm), EixoFim = S.XY(pc.U1, vm), Cantos = S.Cantos(new RetUV(pc.U0, pc.V0, pc.U1, pc.V1)),
                };
                pp.Pecas.Add(sai);
                if (!string.IsNullOrEmpty(pc.Ini?.Alerta)) pp.AlertasPontas.Add(new AlertaPonta { Marca = pc.Marca, Ponta = "início", Texto = pc.Ini.Alerta, X = sai.EixoIni.X, Y = sai.EixoIni.Y });
                if (!string.IsNullOrEmpty(pc.Fim?.Alerta)) pp.AlertasPontas.Add(new AlertaPonta { Marca = pc.Marca, Ponta = "fim", Texto = pc.Fim.Alerta, X = sai.EixoFim.X, Y = sai.EixoFim.Y });
            }
        }
        for (int k = 0; k < R.FaixasMacicas.Count; k++)
        {
            var m = R.FaixasMacicas[k];
            m.Marca = $"{ins.PrefixoMacica}{n2}-{D2(k + 1)}";
            pp.Macicas.Add(new MacicaSaida { Marca = m.Marca, Faixa = m, Cantos = S.Cantos(m.Ret) });
        }
        foreach (var c in R.Consolos)
        {
            var cs = new ConsoloSaida { Consolo = c };
            if (c.EixoViga == 'u') { cs.Ini = S.XY(c.De, c.Face); cs.Fim = S.XY(c.Ate, c.Face); }
            else { cs.Ini = S.XY(c.Face, c.De); cs.Fim = S.XY(c.Face, c.Ate); }
            cs.FaixasMacicas = c.Faixas.Select(i => R.FaixasMacicas[i].Marca).ToList();
            cs.PecasApoiadas = c.Pecas.Select(q => q.Marca).ToList();
            pp.Consolos.Add(cs);
        }
        var dirJ = S.XY(1, 0);
        for (int k = 0; k < R.Juntas.Count; k++)
        {
            var j = R.Juntas[k];
            j.Marca = $"{ins.PrefixoJunta}{n2}-{D2(k + 1)}";
            double um = (j.U0 + j.U1) / 2;
            string PecaDe(FaixaPainel q) => q.Pecas.FirstOrDefault(pc => pc.U0 - 1e-4 <= um && um <= pc.U1 + 1e-4)?.Marca ?? q.Marca;
            pp.Juntas.Add(new JuntaSaida { Marca = j.Marca, Junta = j, EntreA = PecaDe(j.Esq), EntreB = PecaDe(j.Dir), Ini = S.XY(j.U0, j.V), Fim = S.XY(j.U1, j.V) });
            for (int m = 0; m < j.Tirantes.Count; m++)
            {
                var (x, y) = S.XY(j.Tirantes[m], j.V);
                pp.Tirantes.Add(new TiranteSaida { Marca = $"{j.Marca}-T{m + 1}", Junta = j.Marca, X = x, Y = y, DirJunta = dirJ });
            }
        }
    }

    // ------------------------------------------------------------------ relatório (texto, português)

    private static string Cm(double m) => Fmt.Virgula(m * 100, "0.0");
    private static string M(double m) => Fmt.Virgula(m, "0.000");

    /// <summary>Resumo legível da paginação (prévia e relatório final).</summary>
    public static string Relatorio(IList<PanoPaginado> panos, ConfigPaginacao cfg, bool detalhado = true)
    {
        var sb = new StringBuilder();
        sb.AppendLine("PAGINAÇÃO DE LAJES ALVEOLARES");
        sb.AppendLine($"Estratégia: {ConfigPaginacao.NomeEstrategia(cfg.Estrategia)}   módulo {Cm(cfg.Modulo)} cm (painel {Cm(cfg.LarguraPainel)} + junta {Cm(cfg.Junta)})   ajuste mínimo {Cm(cfg.AjusteMin)} cm");
        sb.AppendLine($"Suportes de instalações: {(cfg.SuportesInstalacoes ? "ligados" : "desligados")}");
        int nPecas = panos.Sum(p => p.Pecas.Count), nMac = panos.Sum(p => p.Macicas.Count), nJ = panos.Sum(p => p.Juntas.Count), nT = panos.Sum(p => p.Tirantes.Count);
        int nAl = panos.Sum(p => p.AlertasPontas.Count), nCons = panos.Sum(p => p.Consolos.Count);
        sb.AppendLine($"TOTAL: {panos.Count} pano(s), {nPecas} peça(s) de painel, {nMac} faixa(s) maciça(s), {nJ} junta(s), {nT} tirante(s), {nAl} ponta(s) com alerta, {nCons} consolo(s) a colocar");
        var porTipo = panos.Where(p => p.Resultado != null).SelectMany(p => p.Resultado.Paineis).GroupBy(q => q.Tipo).OrderBy(g => g.Key);
        sb.AppendLine("Faixas de painel por tipo: " + string.Join(", ", porTipo.Select(g => $"{g.Key} = {g.Count()}")));
        sb.AppendLine();
        foreach (var p in panos)
        {
            string vao = Fmt.N(p.Sistema.Angulo * 180 / Math.PI, "0.#");
            sb.AppendLine($"== Pano {p.Numero}: piso {p.Piso.Id} {p.Piso.Nome}  (vão a {vao}°)");
            if (p.Erro != null) { sb.AppendLine("   ERRO: " + p.Erro); sb.AppendLine(); continue; }
            var R = p.Resultado;
            sb.AppendLine($"   {R.Paineis.Count} faixa(s) de painel, {p.Pecas.Count} peça(s), {p.Macicas.Count} maciça(s), {p.Juntas.Count} junta(s), {p.Tirantes.Count} tirante(s)");
            if (detalhado)
                foreach (var s in p.Pecas)
                {
                    var pc = s.Peca;
                    string ap(ApoioPonta a) => a == null ? "?" : a.Consolo ? "consolo" : a.Apoio.HasValue ? Cm(a.Apoio.Value) : "-";
                    sb.AppendLine($"   {s.Marca,-10} {s.Tipo,-34} b = {Cm(pc.Largura),6} cm  L = {M(pc.Comprimento)} m  apoios {ap(pc.Ini)} / {ap(pc.Fim)} cm" +
                                  (pc.Recortes.Count > 0 ? $"  recortes: {string.Join(", ", pc.Recortes.Select(r => r.Tipo))}" : ""));
                }
            foreach (var m in p.Macicas)
                sb.AppendLine($"   {m.Marca,-10} FAIXA MACIÇA {Cm(m.Faixa.Ret.LarguraV)} x {M(m.Faixa.Ret.LarguraU)} m ({m.Faixa.Motivo})" + (m.Faixa.Alertas.Count > 0 ? "  ! " + string.Join("; ", m.Faixa.Alertas) : ""));
            foreach (var a in R.Alertas) sb.AppendLine("   alerta: " + a);
            foreach (var a in p.AlertasPontas) sb.AppendLine($"   ALERTA {a.Marca} ({a.Ponta}): {a.Texto}");
            sb.AppendLine();
        }
        return sb.ToString();
    }

    /// <summary>Lista de avisos ao usuário (pontas, consolos a colocar, faixas maciças com alerta, erros).</summary>
    public static List<string> Avisos(IList<PanoPaginado> panos)
    {
        var r = new List<string>();
        foreach (var p in panos)
        {
            if (p.Erro != null) { r.Add($"Pano {p.Numero} (piso {p.Piso.Id}): não paginado — {p.Erro}"); continue; }
            foreach (var a in p.AlertasPontas) r.Add($"{a.Marca} ({a.Ponta}): {a.Texto}");
            foreach (var m in p.Macicas) foreach (var a in m.Faixa.Alertas) r.Add($"{m.Marca}: {a}");
            foreach (var c in p.Consolos)
                r.Add($"Pano {p.Numero}: apoio em consolo a colocar na viga {c.Consolo.Viga} ({c.Consolo.VigaTipo}), {Fmt.Virgula(c.Consolo.Extensao, "0.00")} m" +
                      (c.FaixasMacicas.Count > 0 ? " — faixa(s) " + string.Join(", ", c.FaixasMacicas) : "") +
                      (c.PecasApoiadas.Count > 0 ? " — peça(s) " + string.Join(", ", c.PecasApoiadas.Distinct()) : ""));
        }
        return r;
    }
}
