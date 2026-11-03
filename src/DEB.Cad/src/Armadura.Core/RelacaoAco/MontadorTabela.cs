using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Armadura.Core.Calculo;
using Armadura.Core.Configuracao;

namespace Armadura.Core.RelacaoAco;

/// <summary>O que a tabela mostra (seção 2.8).</summary>
public enum ApresentacaoRA { TabelaEResumo = 0, SomenteTabela = 1, SomenteResumo = 2 }

/// <summary>Colunas do resumo do aço (seção 5.1).</summary>
public enum ApresentacaoResumo { Peso = 0, Barras = 1, BarrasEPeso = 2 }

/// <summary>Coluna ELEMENTO (nome do bloco/detalhamento) na relação do aço.</summary>
public enum ColunaElemento
{
    /// <summary>Só com numeração Individual ou Sequencial (comportamento anterior).</summary>
    Automatica = 0,
    Sempre = 1,
    Nunca = 2,
}

/// <summary>Opções de UMA tabela (gravadas na própria tabela; o padrão vem da configuração).</summary>
public sealed class OpcoesTabelaRA
{
    public ColunaElemento ColunaElemento { get; set; } = ColunaElemento.Automatica;
    /// <summary>Título com o(s) nome(s) do(s) detalhamento(s) da tabela parcial: "RELAÇÃO DO AÇO - V1".</summary>
    public bool NomeNoTitulo { get; set; }

    /// <summary>A tabela mostra a coluna ELEMENTO, dado o tipo de numeração do desenho.</summary>
    public bool ComElemento(Numeracao.TipoNumeracao numeracao) => ColunaElemento switch
    {
        ColunaElemento.Sempre => true,
        ColunaElemento.Nunca => false,
        _ => numeracao != Numeracao.TipoNumeracao.Global,
    };

    public ApresentacaoRA Apresentacao { get; set; } = ApresentacaoRA.TabelaEResumo;
    public ApresentacaoResumo Resumo { get; set; } = ApresentacaoResumo.Peso;
    public bool MostrarPesoTotal { get; set; } = true;
    /// <summary>Escala da tabela (1:Escala) para a altura do texto; 0 = escala base.</summary>
    public double Escala { get; set; }
    /// <summary>
    /// RA PARCIAL: nomes dos blocos (detalhamentos) que a tabela lista. Vazia = desenho inteiro
    /// (ou, se a tabela estiver dentro de um bloco, só aquele bloco).
    /// </summary>
    public List<string> Blocos { get; set; } = new List<string>();

    public bool Parcial => Blocos.Count > 0;

    /// <summary>Coluna "DETALHE DE DOBRA" no fim da relação, com a forma de cada posição.</summary>
    public bool DetalheDobra { get; set; }
    /// <summary>Handles (hex) do desenho dos detalhes (polilinhas e textos sobre a tabela).</summary>
    public List<string> Partes { get; set; } = new List<string>();
    /// <summary>Posição da tabela quando os detalhes foram desenhados (para acompanhar um MOVE).</summary>
    public double PosX { get; set; }
    public double PosY { get; set; }

    public static OpcoesTabelaRA Padrao(ConfiguracaoArmadura cfg) => new OpcoesTabelaRA
    {
        Apresentacao = cfg.ApresentacaoRA,
        Resumo = cfg.ApresentacaoResumo,
        MostrarPesoTotal = cfg.MostrarPesoTotal,
        Escala = cfg.Escala,
    };
}

/// <summary>Conteúdo da tabela, independente do CAD: linhas de texto e células a mesclar.</summary>
public sealed class ConteudoTabela
{
    public ConteudoTabela(int colunas) { Colunas = colunas; }

    public int Colunas { get; }
    public List<string[]> Linhas { get; } = new List<string[]>();
    /// <summary>Mesclas horizontais: (linha, coluna inicial, coluna final).</summary>
    public List<(int Linha, int C1, int C2)> Mesclas { get; } = new List<(int, int, int)>();
    /// <summary>Linhas de título (texto em destaque).</summary>
    public HashSet<int> Titulos { get; } = new HashSet<int>();
    /// <summary>Coluna do detalhe de dobra (−1 = sem).</summary>
    public int ColunaDetalhe { get; internal set; } = -1;
    /// <summary>Linhas da tabela que levam o desenho da forma, e a posição correspondente.</summary>
    public Dictionary<int, LinhaRA> LinhasComForma { get; } = new Dictionary<int, LinhaRA>();

    internal void Add(params string[] c)
    {
        var l = new string[Colunas];
        for (int i = 0; i < Colunas; i++) l[i] = i < c.Length ? c[i] ?? "" : "";
        Linhas.Add(l);
    }

    internal int Ultima => Linhas.Count - 1;
}

/// <summary>
/// Monta a Relação do Aço e o Resumo (o CAD só desenha). Grade de 6 colunas, ou 7 com ELEMENTO.
/// Relação: [ELEMENTO |] AÇO | N | DIAM (mm) | QUANT | UNIT (cm) | C.TOTAL (cm).
/// Resumo:  AÇO | DIAM (mm) | C.TOTAL (m) | PESO (kg) e/ou BARRAS (colunas mescladas para caber).
/// </summary>
public static class MontadorTabela
{
    public const string MarcaNova = "*";

    private static string F(double v, string fmt) => v.ToString(fmt, CultureInfo.InvariantCulture);

    /// <param name="comElemento">coluna ELEMENTO (ver <see cref="OpcoesTabelaRA.ComElemento"/>)</param>
    /// <param name="detalhamentos">nomes dos detalhamentos da tabela parcial (título com <see cref="OpcoesTabelaRA.NomeNoTitulo"/>)</param>
    public static ConteudoTabela Montar(ResultadoRA r, OpcoesTabelaRA op, ConfiguracaoArmadura cfg, bool comElemento = false,
        IEnumerable<string> detalhamentos = null)
    {
        var t = new ConteudoTabela(comElemento ? 7 : 6);
        int ultima = t.Colunas - 1;
        int cabecalhoRelacao = -1;
        var linhasDados = new Dictionary<int, LinhaRA>();
        var nomes = (detalhamentos ?? Enumerable.Empty<string>()).Where(n => !string.IsNullOrWhiteSpace(n))
            .OrderBy(n => n, System.StringComparer.OrdinalIgnoreCase).ToList();
        string sufixo = op.NomeNoTitulo && nomes.Count > 0 ? " - " + string.Join(", ", nomes) : "";

        if (op.Apresentacao != ApresentacaoRA.SomenteResumo)
        {
            t.Add(cfg.TituloRelacao + sufixo);
            sufixo = "";
            t.Mesclas.Add((t.Ultima, 0, ultima));
            t.Titulos.Add(t.Ultima);
            var cab = new List<string> { "AÇO", "N", "DIAM (mm)", "QUANT", "UNIT (cm)", "C.TOTAL (cm)" };
            if (comElemento) cab.Insert(0, "ELEMENTO");
            t.Add(cab.ToArray());
            cabecalhoRelacao = t.Ultima;
            foreach (var l in r.Linhas)
            {
                linhasDados[t.Linhas.Count] = l;
                var c = new List<string>
                {
                    l.Aco, l.Numero.ToString(CultureInfo.InvariantCulture) + (l.Nova ? MarcaNova : ""), l.EhTrelica ? "-" : F(l.Bitola, "0.0"),
                    l.Quantidade.ToString(CultureInfo.InvariantCulture),
                    l.Var ? "VAR" : CalculoComprimento.Numero(l.ComprimentoUnitario, cfg.CasasDecimais),
                    CalculoComprimento.Numero(l.ComprimentoTotal, cfg.CasasDecimais),
                };
                if (comElemento) c.Insert(0, string.IsNullOrEmpty(l.Elemento) ? "-" : l.Elemento);
                t.Add(c.ToArray());
            }
            if (r.Linhas.Any(l => l.Nova))
            {
                t.Add($"{MarcaNova} posição nova ou alterada após o bloqueio da relação");
                t.Mesclas.Add((t.Ultima, 0, ultima));
            }
        }

        if (op.Apresentacao != ApresentacaoRA.SomenteTabela)
        {
            bool peso = op.Resumo != ApresentacaoResumo.Barras;
            bool barras = op.Resumo != ApresentacaoResumo.Peso;

            t.Add(cfg.TituloResumo + sufixo); // só resumo: o nome vai no título dele
            t.Mesclas.Add((t.Ultima, 0, ultima));
            t.Titulos.Add(t.Ultima);

            // C.TOTAL ocupa das colunas 2 até ultima-2; PESO e BARRAS dividem as 2 últimas
            // (ou um deles ocupa as duas)
            int fimTotal = ultima - 2;
            string[] Linha(string aco, string diam, string total, string p, string b)
            {
                var l = new string[t.Colunas];
                for (int i = 0; i < l.Length; i++) l[i] = "";
                l[0] = aco; l[1] = diam; l[2] = total;
                if (peso && barras) { l[ultima - 1] = p; l[ultima] = b; }
                else l[ultima - 1] = peso ? p : b;
                return l;
            }
            void MesclarResumo()
            {
                if (fimTotal > 2) t.Mesclas.Add((t.Ultima, 2, fimTotal));
                if (!(peso && barras)) t.Mesclas.Add((t.Ultima, ultima - 1, ultima));
            }

            t.Add(Linha("AÇO", "DIAM (mm)", "C.TOTAL (m)", "PESO (kg)", "BARRAS"));
            MesclarResumo();
            foreach (var s in r.Resumo)
            {
                t.Add(Linha(s.Aco, s.Diametro ?? F(s.Bitola, "0.0"), s.Total ?? F(s.ComprimentoTotalM, "0.00"), F(s.Peso, "0.0"),
                    s.Barras?.ToString(CultureInfo.InvariantCulture) ?? "rolo"));
                MesclarResumo();
            }

            if (op.MostrarPesoTotal && peso)
            {
                void Total(string rotulo, double valor)
                {
                    var l = new string[t.Colunas];
                    for (int i = 0; i < l.Length; i++) l[i] = "";
                    l[0] = rotulo;
                    l[ultima - 1] = F(valor, "0.0");
                    t.Add(l);
                    t.Mesclas.Add((t.Ultima, 0, ultima - 2));
                    t.Mesclas.Add((t.Ultima, ultima - 1, ultima));
                }
                foreach (var kv in r.PesoPorAco.OrderBy(k => k.Key)) Total("PESO TOTAL " + kv.Key + " (kg)", kv.Value);
                if (r.PesoPorAco.Count > 1) Total("PESO TOTAL (kg)", r.PesoTotal);
                if (cfg.AcrescimoPesoPercentual > 0)
                {
                    t.Add($"Pesos com acréscimo de {F(cfg.AcrescimoPesoPercentual, "0.#")}% para perdas");
                    t.Mesclas.Add((t.Ultima, 0, ultima));
                }
            }
        }
        return op.DetalheDobra && cabecalhoRelacao >= 0 ? ComDetalhe(t, cabecalhoRelacao, linhasDados) : t;
    }

    public const string TituloDetalhe = "DETALHE DE DOBRA (DIMENSÕES EXTERNAS EM cm)";

    /// <summary>
    /// Mesma tabela + uma coluna no fim. Na relação, a coluna é o detalhe de dobra (cabeçalho e uma
    /// célula por posição); nas demais linhas a última célula é estendida até ela.
    /// </summary>
    private static ConteudoTabela ComDetalhe(ConteudoTabela b, int cabecalho, Dictionary<int, LinhaRA> dados)
    {
        int velha = b.Colunas - 1, nova = b.Colunas;
        var t = new ConteudoTabela(b.Colunas + 1) { ColunaDetalhe = nova };
        for (int i = 0; i < b.Linhas.Count; i++)
        {
            var l = b.Linhas[i].Concat(new[] { i == cabecalho ? TituloDetalhe : "" }).ToArray();
            t.Linhas.Add(l);
        }
        foreach (var x in b.Titulos) t.Titulos.Add(x);
        foreach (var kv in dados) t.LinhasComForma[kv.Key] = kv.Value;

        bool DaRelacao(int linha) => linha == cabecalho || dados.ContainsKey(linha);
        var cobrem = new HashSet<int>();
        foreach (var (l, c1, c2) in b.Mesclas)
        {
            if (c2 == velha && !DaRelacao(l)) { t.Mesclas.Add((l, c1, nova)); cobrem.Add(l); }
            else t.Mesclas.Add((l, c1, c2));
        }
        // linhas sem mescla na última coluna (fora da relação): a última célula se estende
        for (int i = 0; i < b.Linhas.Count; i++)
            if (!DaRelacao(i) && !cobrem.Contains(i)) t.Mesclas.Add((i, velha, nova));
        return t;
    }
}
