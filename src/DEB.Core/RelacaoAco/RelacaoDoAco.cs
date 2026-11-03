using System;
using System.Collections.Generic;
using System.Linq;
using DEB.Core.Configuracao;
using DEB.Core.Tabelas;

namespace DEB.Core.RelacaoAco;

/// <summary>Uma barra (ou grupo de barras iguais) que entra na relação.</summary>
public sealed class ItemRA
{
    public int Numero { get; set; }
    public string Aco { get; set; }
    public double Bitola { get; set; }
    public int Quantidade { get; set; }
    /// <summary>Comprimento unitário em cm (já arredondado).</summary>
    public double ComprimentoUnitario { get; set; }
    public bool Var { get; set; }
    /// <summary>Grupo VAR: comprimento real (cm) de cada barra do grupo (uma vez; a quantidade pode multiplicar).</summary>
    public List<double> ComprimentosVar { get; set; }
    /// <summary>Comprimento total do item (cm): grupo VAR = soma das barras × (quantidade / nº de barras).</summary>
    public double ComprimentoTotal => ComprimentosVar != null && ComprimentosVar.Count > 0
        ? ComprimentosVar.Sum() * Quantidade / ComprimentosVar.Count
        : Quantidade * ComprimentoUnitario;
    public bool Corrido { get; set; }
    /// <summary>Detalhamento (bloco); "" = fora de detalhamento.</summary>
    public string Elemento { get; set; } = "";
    /// <summary>Posição nova/alterada depois do bloqueio da RA (destaque).</summary>
    public bool Nova { get; set; }
    /// <summary>Treliça: modelo (vai na coluna AÇO; DIAM fica "-"; peso pelo catálogo).</summary>
    public string Trelica { get; set; } = "";
    /// <summary>Massa linear do catálogo (kg/m) para treliças.</summary>
    public double MassaLinear { get; set; }
    /// <summary>Forma da barra (para o detalhe de dobra).</summary>
    public List<Modelo.GeoSegmento> Forma { get; set; }
    public bool Fechada { get; set; }
}

/// <summary>Tela soldada: entra só no resumo, por massa (seção 2.6).</summary>
public sealed class ItemTela
{
    public string Modelo { get; set; } = "";
    public string Aco { get; set; } = "CA-60";
    /// <summary>Área de UM painel (m²).</summary>
    public double AreaM2 { get; set; }
    public int Quantidade { get; set; } = 1;
    public double MassaM2 { get; set; }
}

public sealed class LinhaRA
{
    public string Aco { get; set; }
    public int Numero { get; set; }
    public double Bitola { get; set; }
    public int Quantidade { get; set; }
    public double ComprimentoUnitario { get; set; }
    public double ComprimentoTotal { get; set; }
    public bool Var { get; set; }
    public string Elemento { get; set; } = "";
    public bool Nova { get; set; }
    /// <summary>Treliça: a coluna DIAM mostra "-".</summary>
    public bool EhTrelica { get; set; }
    /// <summary>Forma (de uma das barras do N) para o detalhe de dobra.</summary>
    public List<Modelo.GeoSegmento> Forma { get; set; }
    public bool Fechada { get; set; }
}

public sealed class LinhaResumo
{
    public string Aco { get; set; }
    public double Bitola { get; set; }
    /// <summary>Comprimento total em metros.</summary>
    public double ComprimentoTotalM { get; set; }
    /// <summary>Peso em kg, já com o acréscimo de perdas.</summary>
    public double Peso { get; set; }
    /// <summary>Barras comerciais necessárias; null para bitolas fornecidas em rolo.</summary>
    public int? Barras { get; set; }
    /// <summary>Texto da coluna DIAM quando não é bitola ("-" na treliça, modelo na tela).</summary>
    public string Diametro { get; set; }
    /// <summary>Texto da coluna C.TOTAL quando não é comprimento (área da tela).</summary>
    public string Total { get; set; }
}

public sealed class ResultadoRA
{
    public List<LinhaRA> Linhas { get; } = new List<LinhaRA>();
    public List<LinhaResumo> Resumo { get; } = new List<LinhaResumo>();
    public Dictionary<string, double> PesoPorAco { get; } = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
    public double PesoTotal => PesoPorAco.Values.Sum();
}

/// <summary>Relação do Aço e Resumo do Aço (seções 2.8 e 4.2).</summary>
public static class RelacaoDoAco
{
    /// <summary>Chave do peso total das treliças no resumo.</summary>
    public const string GrupoTrelicas = "TRELIÇAS";

    /// <param name="porElemento">
    /// Numeração Individual/Sequencial: as linhas são por (elemento, N) e ordenadas por elemento
    /// (seção 4.1); na Global, por aço e N.
    /// </param>
    public static ResultadoRA Calcular(IEnumerable<ItemRA> itens, ConfiguracaoArmadura cfg, bool porElemento = false,
        IEnumerable<ItemTela> telas = null)
    {
        var r = new ResultadoRA();
        var lista = itens.ToList();

        // Barras com o mesmo N têm a mesma forma: somam-se as quantidades.
        var grupos = lista.GroupBy(i => (Elemento: porElemento ? i.Elemento ?? "" : "", i.Numero));
        // ordem pela posição N (pedido do usuário, 2026-10-27); por elemento: detalhamento, depois N
        var ordenados = porElemento
            ? grupos.OrderBy(g => g.Key.Elemento, StringComparer.OrdinalIgnoreCase).ThenBy(g => g.Key.Numero)
            : grupos.OrderBy(g => g.Key.Numero).ThenBy(g => g.First().Aco ?? "", StringComparer.OrdinalIgnoreCase);
        foreach (var g in ordenados)
        {
            var p = g.First();
            int qtd = g.Sum(i => i.Quantidade);
            bool trelica = !string.IsNullOrEmpty(p.Trelica);
            r.Linhas.Add(new LinhaRA
            {
                Aco = trelica ? p.Trelica : p.Aco, Numero = g.Key.Numero, Bitola = p.Bitola, Quantidade = qtd,
                ComprimentoUnitario = p.ComprimentoUnitario,
                ComprimentoTotal = g.Sum(i => i.ComprimentoTotal),
                Var = g.Any(i => i.Var),
                Elemento = g.Key.Elemento,
                Nova = g.Any(i => i.Nova),
                EhTrelica = trelica,
                Forma = g.Select(i => i.Forma).FirstOrDefault(fm => fm != null),
                Fechada = p.Fechada,
            });
        }

        double fator = 1 + cfg.AcrescimoPesoPercentual / 100.0;
        void Somar(string grupo, double peso) =>
            r.PesoPorAco[grupo ?? ""] = (r.PesoPorAco.TryGetValue(grupo ?? "", out var acc) ? acc : 0) + peso;

        // barras e cordoalhas, por aço e bitola
        foreach (var g in r.Linhas.Where(l => !l.EhTrelica)
                     .GroupBy(l => new { Aco = (l.Aco ?? "").ToUpperInvariant(), l.Bitola })
                     .OrderBy(g => g.Key.Aco).ThenBy(g => g.Key.Bitola))
        {
            double totalCm = g.Sum(l => l.ComprimentoTotal);
            var info = cfg.Barras.Obter(g.Key.Bitola, g.Key.Aco);
            double massa = cfg.Barras.MassaLinear(g.Key.Bitola, g.Key.Aco);
            double peso = Math.Round(totalCm / 100.0 * massa * fator, 1);
            int? barras = null;
            if (info == null || info.Fabricacao == Fabricacao.Barra)
            {
                double comercial = info?.ComprimentoComercial > 0 ? info.ComprimentoComercial : 1200;
                barras = (int)Math.Ceiling(totalCm / comercial - 1e-9);
            }
            var aco = g.First().Aco;
            r.Resumo.Add(new LinhaResumo
            {
                Aco = aco, Bitola = g.Key.Bitola,
                ComprimentoTotalM = Math.Round(totalCm / 100.0, 2), Peso = peso, Barras = barras,
            });
            Somar(aco, peso);
        }

        // treliças, por modelo (peso do catálogo; peças de 12 m)
        foreach (var g in lista.Where(i => !string.IsNullOrEmpty(i.Trelica))
                     .GroupBy(i => i.Trelica, StringComparer.OrdinalIgnoreCase).OrderBy(g => g.Key))
        {
            double totalCm = g.Sum(i => i.Quantidade * i.ComprimentoUnitario);
            double peso = Math.Round(totalCm / 100.0 * g.First().MassaLinear * fator, 1);
            r.Resumo.Add(new LinhaResumo
            {
                Aco = g.Key, Diametro = "-", ComprimentoTotalM = Math.Round(totalCm / 100.0, 2), Peso = peso,
                Barras = (int)Math.Ceiling(totalCm / 1200 - 1e-9),
            });
            Somar(GrupoTrelicas, peso);
        }

        // telas soldadas, por modelo (área × kg/m²); somam no aço delas (CA-60)
        foreach (var g in (telas ?? Enumerable.Empty<ItemTela>())
                     .GroupBy(t => t.Modelo, StringComparer.OrdinalIgnoreCase).OrderBy(g => g.Key))
        {
            double area = g.Sum(t => t.AreaM2 * t.Quantidade);
            double peso = Math.Round(area * g.First().MassaM2 * fator, 1);
            var aco = g.First().Aco;
            r.Resumo.Add(new LinhaResumo
            {
                Aco = aco, Diametro = g.Key, Total = area.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " m²",
                Peso = peso, Barras = g.Sum(t => t.Quantidade),
            });
            Somar(aco, peso);
        }
        return r;
    }
}
