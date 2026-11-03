using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Armadura.Core.Configuracao;
using Armadura.Core.Modelo;

namespace Armadura.Core.Calculo;

public static class CalculoComprimento
{
    public static double Arredondar(double valor, int casas, ModoArredondamento modo)
    {
        double f = Math.Pow(10, Math.Max(0, casas));
        // tolerância evita que 200.0000001 suba para 201 no modo "para cima"
        // tolerâncias: a geometria do CAD dá 132,4999999 onde o valor é 132,5 (meio sobe)
        return modo == ModoArredondamento.ParaCima
            ? Math.Ceiling(valor * f - 1e-6) / f
            : Math.Round(valor * f + Math.Sign(valor) * 1e-6, MidpointRounding.AwayFromZero) / f;
    }

    /// <summary>
    /// Comprimento unitário (cm), arredondado pela configuração: soma das medidas externas dos
    /// segmentos menos o desconto das dobras (<see cref="DescontoDobras"/>), quando
    /// <see cref="ConfiguracaoArmadura.DescontarDobras"/> e os dados da barra são informados.
    /// </summary>
    public static double ComprimentoUnitario(IReadOnlyList<GeoSegmento> geometria, ConfiguracaoArmadura cfg, DadosArmadura dados = null)
    {
        double soma = geometria.Sum(s => s.Comprimento);
        if (dados != null && cfg.DescontarDobras) soma -= DescontoDobras(geometria, dados, cfg.Dobramento);
        return Arredondar(soma, cfg.CasasDecimais, cfg.Arredondamento);
    }

    /// <summary>
    /// Diferença entre a soma das medidas externas e o comprimento real da barra, que segue as
    /// curvaturas (planilha "Estudo_comprimento_real_barra_vergalhao", Planilha1). Em cada dobra de
    /// deflexão δ, com pino D (NBR 6118, tabelas 9.1 e 9.2), raio externo re2 = D/2 + φ e raio na linha
    /// média re = (D + φ)/2:
    /// <list type="bullet">
    /// <item>entre segmentos retos (medidas até o encontro das faces externas): 2·re2·tan(δ/2) − re·δ;</item>
    /// <item>no gancho (medido até a face externa, ponta + re2): 2·re2 − re·δ, com δ nominal
    /// (90° → 90°, 45° → 135°, 180° → 180°), seja qual for o ângulo desenhado.</item>
    /// </list>
    /// Dobras junto a arcos não descontam (a curva já está no desenho). Treliça, tela e cordoalha: 0.
    /// </summary>
    public static double DescontoDobras(IReadOnlyList<GeoSegmento> geo, DadosArmadura d, Normas.RegrasDobramento regras)
    {
        if (geo == null || d == null || regras == null || d.Bitola <= 0) return 0;
        if (d.EhTrelica || d.EhTela || Tabelas.TabelaBarras.EhCordoalha(d.Aco)) return 0;
        double phi = d.Bitola / 10.0;
        double pino = regras.DiametroPino(d.Bitola, d.Aco, d.Estribo);
        double reMedio = (pino + phi) / 2, reExterno = pino / 2 + phi;
        FormatoSegmento Formato(int i) => i < d.Segmentos.Count ? d.Segmentos[i].Formato : FormatoSegmento.Linha;

        double total = 0;
        // i = 0 só tem dobra em barra fechada (deflexão do 1º segmento em relação ao último)
        for (int i = 0; i < geo.Count; i++)
        {
            int ant = i == 0 ? geo.Count - 1 : i - 1;
            if (i == 0 && (geo.Count < 2 || Math.Abs(geo[0].Deflexao) < 1e-4)) continue;
            if (geo[ant].Tipo == TipoSegmento.Arco || geo[i].Tipo == TipoSegmento.Arco) continue;
            var fa = Formato(ant);
            var fb = Formato(i);
            if (Segmento.EhGancho(fa) || Segmento.EhGancho(fb))
            {
                double dg = AnguloGancho(Segmento.EhGancho(fb) ? fb : fa);
                total += 2 * reExterno - reMedio * dg;
                continue;
            }
            double delta = Math.Abs(geo[i].Deflexao) * Math.PI / 180.0;
            if (delta < 1e-4) continue; // sem dobra
            total += delta > Math.PI - 1e-3
                ? 2 * reExterno - reMedio * Math.PI // volta de 180° (tan(90°) não existe)
                : 2 * reExterno * Math.Tan(delta / 2) - reMedio * delta;
        }
        return total;
    }

    /// <summary>
    /// Comprimentos reais (arredondados) de cada barra de um grupo VAR: o externo de cada uma menos o
    /// desconto das dobras da forma representativa (as barras do grupo só diferem no trecho reto).
    /// </summary>
    public static List<double> ComprimentosReaisVar(IReadOnlyList<GeoSegmento> geometria, ConfiguracaoArmadura cfg, DadosArmadura d)
    {
        var r = new List<double>();
        if (d == null || !d.EhVarLista) return r;
        double desconto = cfg.DescontarDobras ? DescontoDobras(geometria, d, cfg.Dobramento) : 0;
        foreach (var ext in d.ComprimentosVar) r.Add(Arredondar(Math.Max(0, ext - desconto), cfg.CasasDecimais, cfg.Arredondamento));
        return r;
    }

    /// <summary>Ângulo de dobra (rad) do gancho: 90° → 90°, "45°" (135° com ponta a 45°) → 135°, 180° → 180°.</summary>
    public static double AnguloGancho(FormatoSegmento f) => f switch
    {
        FormatoSegmento.Gancho45 => 3 * Math.PI / 4,
        FormatoSegmento.Gancho180 => Math.PI,
        _ => Math.PI / 2,
    };

    /// <summary>Texto da cota de um segmento ("150", "12.5" ou "VAR").</summary>
    public static string TextoCota(double comprimentoCm, bool var, ConfiguracaoArmadura cfg) =>
        var ? "VAR" : Numero(Arredondar(comprimentoCm, cfg.CasasDecimais, cfg.Arredondamento), cfg.CasasDecimais);

    /// <summary>Número com ponto decimal e as casas configuradas.</summary>
    public static string Numero(double valor, int casas) =>
        valor.ToString(casas <= 0 ? "0" : "0." + new string('0', casas), CultureInfo.InvariantCulture);

    /// <summary>Número sem zeros à direita ("15", "12.5").</summary>
    public static string NumeroCompacto(double valor) =>
        Math.Round(valor, 2).ToString("0.##", CultureInfo.InvariantCulture);
}
