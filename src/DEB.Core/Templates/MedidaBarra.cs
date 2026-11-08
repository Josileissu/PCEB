using System;
using System.Collections.Generic;
using System.Linq;
using DEB.Core.Calculo;
using DEB.Core.Configuracao;
using DEB.Core.Estribos;
using DEB.Core.Modelo;

namespace DEB.Core.Templates;

/// <summary>
/// Comprimento e peso de uma <see cref="BarraTemplate"/> pelas MESMAS regras que o CAD usa na barra desenhada
/// (e portanto na relação do aço): segmentos da polilinha (com arcos), desconto das dobras e arredondamento da
/// configuração (<see cref="CalculoComprimento.ComprimentoUnitario"/>), massa da tabela de bitolas da configuração.
/// Assim a tabela "informações p/ produção" da prancha bate com a RA da mesma folha.
/// </summary>
public static class MedidaBarra
{
    /// <summary>Segmentos e dados equivalentes aos que o CAD grava ao desenhar a barra (ComandosTemplate.Desenhar).</summary>
    public static (List<GeoSegmento> geo, DadosArmadura dados) Montar(BarraTemplate b)
    {
        if (b == null) throw new ArgumentNullException(nameof(b));
        var v = new List<VerticeEstribo>(b.Vertices.Count);
        for (int i = 0; i < b.Vertices.Count; i++)
            v.Add(new VerticeEstribo(b.Vertices[i].X, b.Vertices[i].Y, i < b.Bulges.Count ? b.Bulges[i] : 0));
        var geo = v.Count >= 2 ? ComprimentoPorEixo.Segmentos(v, b.Fechada) : new List<GeoSegmento>();
        var d = new DadosArmadura
        {
            Bitola = b.Bitola, Aco = string.IsNullOrWhiteSpace(b.Aco) ? "CA-50" : b.Aco, Estribo = b.Estribo,
            Tela = b.Tela ?? "", ComprimentosVar = new List<double>(b.ComprimentosVar),
        };
        for (int s = 0; s < geo.Count; s++)
        {
            bool sob = b.Sobrepostos.Contains(s);
            geo[s].Sobreposto = sob;
            d.Segmentos.Add(new Segmento
            {
                Formato = s < b.Formatos.Count ? b.Formatos[s] : FormatoSegmento.Linha, Sobreposto = sob, Var = b.SegmentosVar.Contains(s),
            });
        }
        return (geo, d);
    }

    /// <summary>Comprimento unitário (cm), como o CAD calcula para a barra desenhada.</summary>
    public static double Unitario(BarraTemplate b, ConfiguracaoArmadura cfg)
    {
        var (geo, d) = Montar(b);
        return geo.Count == 0 ? 0 : CalculoComprimento.ComprimentoUnitario(geo, cfg, d);
    }

    /// <summary>Comprimento total (cm) de uma peça: unitário × quantidade × repetições (grupo VAR: soma dos reais).</summary>
    public static double Total(BarraTemplate b, ConfiguracaoArmadura cfg)
    {
        var (geo, d) = Montar(b);
        if (geo.Count == 0) return 0;
        int rep = Math.Max(1, b.Repeticoes);
        if (d.EhVarLista) return CalculoComprimento.ComprimentosReaisVar(geo, cfg, d).Sum() * rep;
        return CalculoComprimento.ComprimentoUnitario(geo, cfg, d) * Math.Max(1, b.Quantidade) * rep;
    }

    /// <summary>Massa linear (kg/m) da bitola/aço pela tabela da configuração (reserva: massa teórica).</summary>
    public static double MassaLinear(double bitola, string aco, ConfiguracaoArmadura cfg) =>
        cfg?.Barras != null ? cfg.Barras.MassaLinear(bitola, string.IsNullOrWhiteSpace(aco) ? "CA-50" : aco)
                            : Tabelas.TabelaBarras.MassaTeorica(bitola);

    /// <summary>Peso (kg) de uma peça da barra (sem as repetições do elemento).</summary>
    public static double Peso(BarraTemplate b, ConfiguracaoArmadura cfg) =>
        Total(b, cfg) / 100 * MassaLinear(b.Bitola, b.Aco, cfg);
}
