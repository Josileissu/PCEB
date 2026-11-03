using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using DEB.Core.Calculo;
using DEB.Core.Configuracao;
using DEB.Core.Modelo;

namespace DEB.Core.Numeracao;

/// <summary>Barra a numerar: chave (ObjectId, handle…), dados de ordenação e assinatura da forma.</summary>
public sealed class ItemNumeracao
{
    public object Chave { get; set; }
    public string Aco { get; set; }
    public double Bitola { get; set; }
    public string Assinatura { get; set; }
    /// <summary>Ordem de criação (ex.: handle); desempata barras da mesma bitola.</summary>
    public long Ordem { get; set; }
    /// <summary>Detalhamento (bloco) da barra; "" = fora de detalhamento.</summary>
    public string Grupo { get; set; } = "";
    /// <summary>RA bloqueada e a barra não mudou desde o bloqueio: mantém <see cref="NumeroAtual"/>.</summary>
    public bool Travada { get; set; }
    public int NumeroAtual { get; set; }
}

/// <summary>Tipos de numeração da RA (seção 4.1).</summary>
public enum TipoNumeracao
{
    /// <summary>Barras iguais têm o mesmo N em todo o desenho.</summary>
    Global = 0,
    /// <summary>A numeração recomeça em cada detalhamento; igualdade só dentro dele.</summary>
    Individual = 1,
    /// <summary>Numeração por detalhamento, sem repetir números entre detalhamentos.</summary>
    Sequencial = 2,
}

public sealed class ResultadoNumeracao
{
    public Dictionary<object, int> Numeros { get; } = new Dictionary<object, int>();
    /// <summary>Barras que receberam número novo com a RA bloqueada (destacadas na tabela).</summary>
    public HashSet<object> Novas { get; } = new HashSet<object>();
    public int MaiorNumero { get; set; }
}

public static class Numerador
{
    /// <summary>
    /// Assinatura que identifica barras "iguais" (seção 4.1): aço, bitola, estribo e a sequência de
    /// segmentos (formato, tipo, comprimento arredondado, VAR) com os ângulos entre eles.
    /// Não depende de posição nem de rotação, nem do sentido em que a barra foi desenhada;
    /// com <see cref="ConfiguracaoArmadura.EspelhadasIguais"/>, também não depende de espelhamento.
    /// </summary>
    public static string Assinatura(DadosArmadura d, IReadOnlyList<GeoSegmento> geo, ConfiguracaoArmadura cfg)
    {
        // treliça: iguais = mesmo modelo e mesmo comprimento
        if (d.EhTrelica)
            return "TRL|" + Tabelas.Catalogos.Normalizar(d.Trelica).ToUpperInvariant() + "|" +
                   CalculoComprimento.Numero(CalculoComprimento.ComprimentoUnitario(geo, cfg, d), cfg.CasasDecimais);
        var cab = string.Format(CultureInfo.InvariantCulture, "{0}|{1:0.0}|{2}|",
            (d.Aco ?? "").ToUpperInvariant(), d.Bitola, d.Estribo ? "E" : "L");
        // grupo VAR: os comprimentos de todas as barras fazem parte da identidade (ordem não importa)
        if (d.EhVarLista)
            cab += "V:" + string.Join(",", d.ComprimentosVar.Select(c => CalculoComprimento.Numero(c, 1)).OrderBy(s => s, StringComparer.Ordinal)) + "|";

        string Seq(bool reverso, bool espelho)
        {
            var sb = new StringBuilder();
            int n = geo.Count;
            for (int k = 0; k < n; k++)
            {
                int i = reverso ? n - 1 - k : k;
                // percorrer ao contrário troca o sentido das curvas: inverte o sinal dos ângulos
                double sinal = (reverso ? -1 : 1) * (espelho ? -1 : 1);
                if (k > 0)
                {
                    // ângulo na junção: no sentido direto é a deflexão do segmento i; ao contrário, a do i+1
                    double defl = reverso ? geo[i + 1].Deflexao : geo[i].Deflexao;
                    sb.Append('<').Append(Graus(sinal * defl)).Append('>');
                }
                var s = geo[i];
                var meta = i < d.Segmentos.Count ? d.Segmentos[i] : new Segmento();
                sb.Append('[')
                  .Append((int)meta.Formato).Append(',')
                  .Append(s.Tipo == TipoSegmento.Arco ? 'A' : 'R').Append(',')
                  .Append(CalculoComprimento.Numero(
                      CalculoComprimento.Arredondar(s.Comprimento, cfg.CasasDecimais, cfg.Arredondamento), cfg.CasasDecimais))
                  .Append(',');
                if (s.Tipo == TipoSegmento.Arco)
                    sb.Append(Graus(sinal * s.AnguloArco));
                sb.Append(meta.Var ? ",V" : "").Append(']');
            }
            return sb.ToString();
        }

        var opcoes = new List<string> { Seq(false, false), Seq(true, false) };
        if (cfg.EspelhadasIguais) { opcoes.Add(Seq(false, true)); opcoes.Add(Seq(true, true)); }
        return cab + opcoes.OrderBy(s => s, StringComparer.Ordinal).First();
    }

    /// <summary>Resumo curto (MD5, 32 hex) de uma assinatura: cabe no XData (limite de 255 por texto).</summary>
    public static string Hash(string texto)
    {
        using var md5 = System.Security.Cryptography.MD5.Create();
        var b = md5.ComputeHash(Encoding.UTF8.GetBytes(texto ?? ""));
        return string.Concat(b.Select(x => x.ToString("x2")));
    }

    // "+ 0.0" transforma -0 em 0 (no .NET Core, (-0.0).ToString() dá "-0")
    private static string Graus(double v) => (Math.Round(v) + 0.0).ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Numeração GLOBAL: barras iguais recebem o mesmo N em todo o desenho; ordem por bitola
    /// (menor para maior), aço e ordem de criação da primeira barra de cada forma.
    /// </summary>
    public static Dictionary<object, int> NumerarGlobal(IEnumerable<ItemNumeracao> itens, int numeroInicial)
    {
        var lista = itens.ToList();
        var formas = lista
            .GroupBy(i => i.Assinatura)
            .Select(g => new { Assinatura = g.Key, g.First().Aco, g.First().Bitola, Ordem = g.Min(i => i.Ordem) })
            .OrderBy(f => f.Bitola)
            .ThenBy(f => f.Aco ?? "", StringComparer.OrdinalIgnoreCase)
            .ThenBy(f => f.Ordem)
            .ToList();

        var numeroDaForma = new Dictionary<string, int>();
        int n = numeroInicial;
        foreach (var f in formas) numeroDaForma[f.Assinatura] = n++;

        return lista.ToDictionary(i => i.Chave, i => numeroDaForma[i.Assinatura]);
    }

    /// <summary>
    /// Numeração completa: tipo (Global/Individual/Sequencial) e bloqueio (seção 4.3).
    /// Com <paramref name="bloqueada"/>, barras travadas mantêm o número; as iguais a uma travada
    /// recebem o mesmo número; as demais recebem números acima do maior já usado
    /// (<paramref name="maiorNoBloqueio"/> ou o maior do grupo) e entram em <see cref="ResultadoNumeracao.Novas"/>.
    /// </summary>
    public static ResultadoNumeracao Numerar(IEnumerable<ItemNumeracao> itens, TipoNumeracao tipo, int numeroInicial,
        bool bloqueada = false, int maiorNoBloqueio = 0)
    {
        var r = new ResultadoNumeracao();
        var lista = itens.ToList();
        bool porGrupo = tipo != TipoNumeracao.Global;
        string Chave(ItemNumeracao i) => porGrupo ? (i.Grupo ?? "") + "\u0001" + i.Assinatura : i.Assinatura;

        // domínios de números: Individual recomeça por grupo; Global e Sequencial são um só
        var dominios = tipo == TipoNumeracao.Individual
            ? lista.GroupBy(i => i.Grupo ?? "").OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase).Select(g => g.ToList()).ToList()
            : new List<List<ItemNumeracao>> { lista };

        foreach (var dom in dominios)
        {
            var numeroDaChave = new Dictionary<string, int>();
            int proximo = numeroInicial;
            if (bloqueada)
            {
                foreach (var t in dom.Where(i => i.Travada && i.NumeroAtual > 0))
                {
                    numeroDaChave[Chave(t)] = t.NumeroAtual;
                    proximo = Math.Max(proximo, t.NumeroAtual + 1);
                }
                if (tipo != TipoNumeracao.Individual) proximo = Math.Max(proximo, maiorNoBloqueio + 1);
            }

            // formas ainda sem número, na ordem: (grupo, no Sequencial) → bitola → aço → criação
            var formas = dom.Where(i => !numeroDaChave.ContainsKey(Chave(i)))
                .GroupBy(Chave)
                .Select(g => new
                {
                    Chave = g.Key, Grupo = g.First().Grupo ?? "", g.First().Aco, g.First().Bitola, Ordem = g.Min(i => i.Ordem),
                })
                .OrderBy(f => tipo == TipoNumeracao.Sequencial ? f.Grupo : "", StringComparer.OrdinalIgnoreCase)
                .ThenBy(f => f.Bitola)
                .ThenBy(f => f.Aco ?? "", StringComparer.OrdinalIgnoreCase)
                .ThenBy(f => f.Ordem);
            var novasChaves = new HashSet<string>();
            foreach (var f in formas)
            {
                numeroDaChave[f.Chave] = proximo++;
                novasChaves.Add(f.Chave);
            }

            foreach (var i in dom)
            {
                int n = numeroDaChave[Chave(i)];
                r.Numeros[i.Chave] = n;
                if (bloqueada && novasChaves.Contains(Chave(i))) r.Novas.Add(i.Chave);
                r.MaiorNumero = Math.Max(r.MaiorNumero, n);
            }
        }
        return r;
    }
}
