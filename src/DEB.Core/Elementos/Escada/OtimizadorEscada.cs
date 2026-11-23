using System;
using System.Collections.Generic;
using System.Linq;
using DEB.Core.Elementos.Viga.PreMoldada.Calculo;
using DEB.Core.Normas.Verificacao;

namespace DEB.Core.Elementos.Escada;

/// <summary>Uma opção do otimizador: a entrada, o resultado e o consumo da escada inteira.</summary>
public sealed class OpcaoEscada
{
    public EscadaLongitudinal Entrada { get; set; }
    public ResultadoEscada Resultado { get; set; }
    /// <summary>Concreto da escada inteira (m³).</summary>
    public double Concreto { get; set; }
    /// <summary>Aço da escada inteira (kg), pelo comprimento das barras propostas.</summary>
    public double Aco { get; set; }
    public double Custo { get; set; }
    public double Blondel => 2 * Resultado.Geo.Espelho + Resultado.Geo.Piso;
    public string Texto => $"{Resultado.Geo.N} × {Fmt.Virgula(Resultado.Geo.Espelho, "0.0#")} / {Fmt.Virgula(Resultado.Geo.Piso, "0")} · h = {Fmt.Virgula(Entrada.Espessura, "0")} · " +
                           $"patamares {Fmt.Virgula(Entrada.PatamarInferior, "0")}/{Fmt.Virgula(Entrada.PatamarSuperior, "0")}";
}

/// <summary>Faixas de busca e preços do otimizador (valores de partida editáveis).</summary>
public sealed class CriteriosOtimizacao
{
    public double EspelhoMin { get; set; } = 16;
    public double EspelhoMax { get; set; } = 18.5;
    public double PisoMin { get; set; } = 26;
    public double PisoMax { get; set; } = 32;
    public double BlondelMin { get; set; } = 61;
    public double BlondelMax { get; set; } = 65;
    public double EspessuraMin { get; set; } = 10;
    public double EspessuraMax { get; set; } = 22;
    /// <summary>Patamar mínimo que sobra em cada ponta (cm) quando a escada tem patamar.</summary>
    public double PatamarMin { get; set; } = 80;
    /// <summary>Preço do concreto lançado (R$/m³) e do aço cortado e dobrado (R$/kg).</summary>
    public double PrecoConcreto { get; set; } = 650;
    public double PrecoAco { get; set; } = 9;
    public int Maximo { get; set; } = 12;
}

/// <summary>
/// Otimizador da escada armada longitudinalmente (D42): com o desnível e o vão entre os apoios fixos, percorre o número de
/// espelhos, o piso e a espessura; a sobra do vão vira patamar (dividida na mesma proporção dos patamares digitados; sem patamar,
/// o vão acompanha o lance). Fica só o
/// que passa em todas as verificações de cálculo (K) e na geometria (espelho, piso e Blondel nas faixas). Ordem: custo de
/// concreto + aço; empate, menos espessura.
/// </summary>
public static class OtimizadorEscada
{
    public static double KgPorMetro(double phiMm) => 0.00617 * phiMm * phiMm;

    public static (double Concreto, double Aco) Consumo(ResultadoEscada r)
    {
        double area = 0; var k = r.Geo.Contorno;
        for (int i = 0; i < k.Count; i++) { var a = k[i]; var b = k[(i + 1) % k.Count]; area += a.X * b.Z - b.X * a.Z; }
        double vol = Math.Abs(area) / 2 / 1e4 * r.Entrada.Largura / 100;
        double kg = r.Barras.Sum(b => b.Quantidade * b.Comprimento / 100 * KgPorMetro(b.Bitola));
        return (vol, kg);
    }

    public static List<OpcaoEscada> Otimizar(EscadaLongitudinal baseEntrada, CriteriosOtimizacao c = null, CoeficientesViga k = null)
    {
        c ??= new CriteriosOtimizacao();
        var g0 = GeometriaEscada.De(baseEntrada);
        double vao = g0.Vao, H = baseEntrada.Desnivel;
        double pi0 = Math.Max(0, baseEntrada.PatamarInferior), ps0 = Math.Max(0, baseEntrada.PatamarSuperior);
        double frac = pi0 + ps0 > 0 ? pi0 / (pi0 + ps0) : 0.5;
        bool comPatamar = pi0 + ps0 > 0;
        var ops = new List<OpcaoEscada>();
        int nMin = (int)Math.Ceiling(H / c.EspelhoMax - 1e-9), nMax = (int)Math.Floor(H / c.EspelhoMin + 1e-9);
        for (int n = Math.Max(2, nMin); n <= nMax; n++)
        {
            double e = H / n;
            for (double p = c.PisoMin; p <= c.PisoMax + 1e-9; p += 1)
            {
                double bl = 2 * e + p;
                if (bl < c.BlondelMin - 1e-9 || bl > c.BlondelMax + 1e-9) continue;
                double lance = (n - 1) * p, sobra = vao - lance;
                if (sobra < -1e-6) continue;
                double pi = 0, ps = 0;
                if (comPatamar)
                {
                    pi = Math.Round(sobra * frac); ps = sobra - pi;
                    if ((pi0 > 0 && pi < c.PatamarMin) || (ps0 > 0 && ps < c.PatamarMin)) continue;
                    if (pi0 <= 0) { ps = sobra; pi = 0; }
                    if (ps0 <= 0) { pi = sobra; ps = 0; }
                }
                // sem patamar o vão acompanha o lance (os apoios vão junto)
                OpcaoEscada melhorH = null;
                for (double h = c.EspessuraMin; h <= c.EspessuraMax + 1e-9; h += 1)
                {
                    var ent = baseEntrada.Copia();
                    ent.Espelhos = n; ent.Piso = p; ent.Espessura = h;
                    ent.EspessuraPatamar = baseEntrada.EspessuraPatamar > 0 ? h : 0;   // patamar com a espessura do lance
                    ent.PatamarInferior = pi; ent.PatamarSuperior = ps;
                    ResultadoEscada r;
                    try { r = CalculoEscada.Calcular(ent, k); } catch (Exception) { continue; }
                    if (r.Erros.Count > 0 || r.Principal.Esp <= 0) continue;
                    if (r.Verificacoes.Any(v => v.Codigo.StartsWith("K") && v.Situacao != SituacaoVerificacao.Ok)) continue;
                    var (conc, aco) = Consumo(r);
                    var op = new OpcaoEscada { Entrada = ent, Resultado = r, Concreto = conc, Aco = aco, Custo = conc * c.PrecoConcreto + aco * c.PrecoAco };
                    if (melhorH == null || op.Custo < melhorH.Custo - 1e-6) melhorH = op;
                    // mais espessura só encarece depois que tudo passa e o aço já está no mínimo: para quando o custo sobe duas vezes
                    if (melhorH != op && op.Custo > melhorH.Custo * 1.15) break;
                }
                if (melhorH != null) ops.Add(melhorH);
            }
        }
        return ops.OrderBy(o => o.Custo).ThenBy(o => o.Entrada.Espessura).Take(c.Maximo).ToList();
    }
}
