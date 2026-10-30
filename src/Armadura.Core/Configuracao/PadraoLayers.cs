using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Armadura.Core.Configuracao;

/// <summary>Padrão de um layer criado pelo plugin: cor (ACI), espessura (mm), tipo de linha e descrição.</summary>
public sealed class PadraoLayer
{
    public string Nome { get; set; } = "";
    public short Cor { get; set; } = 7;
    /// <summary>Espessura em mm; nunca "padrão" do CAD (pedido do usuário, 2026-10-30): sem valor = 0,00 mm.</summary>
    public double EspessuraMm { get; set; }
    /// <summary>Vazio = Continuous.</summary>
    public string TipoLinha { get; set; } = "";
    public string Descricao { get; set; } = "";
}

/// <summary>
/// Arquivo de padrões de layer (layers.csv, na pasta de dados do plugin): uma linha por layer, separador ";", editável no
/// Excel ou no Bloco de Notas. Linhas com "#" são comentários; a 1ª linha sem "#" é o cabeçalho.
/// </summary>
public static class PadroesLayers
{
    /// <summary>Espessuras aceitas pelo CAD (centésimos de mm).</summary>
    public static readonly int[] Espessuras = { 0, 5, 9, 13, 15, 18, 20, 25, 30, 35, 40, 50, 53, 60, 70, 80, 90, 100, 106, 120, 140, 158, 200, 211 };

    /// <summary>Espessura válida mais próxima, em centésimos de mm (0,05 mm → 5; negativo/inválido → 0).</summary>
    public static int Centesimos(double mm)
    {
        if (double.IsNaN(mm) || mm <= 0) return 0;
        int c = (int)Math.Round(mm * 100);
        return Espessuras.OrderBy(e => Math.Abs(e - c)).First();
    }

    /// <summary>Padrão de fábrica (pedido do usuário, 2026-10-30): barra 0,30; texto preto 0,05; forma 0,20; resto 0,00; sem amarelo.</summary>
    public static List<PadraoLayer> Padrao() => new List<PadraoLayer>
    {
        L("ARM-BARRA", 1, 0.30, "", "Barras (armaduras)"),
        L("ARM-TEXTO", 7, 0.05, "", "Descrições e cotas das barras"),
        L("ARM-FAIXA", 3, 0, "", "Faixas de distribuição e indicações"),
        L("ARM-CORTE", 1, 0, "", "Cortes das faixas e marcas de corte"),
        L("ARM-TABELA", 7, 0, "", "Relação do aço e tabelas"),
        L("ARM-FORMA", 8, 0.20, "", "Forma das peças (templates)"),
        L("ARM-TEXTO-FORMA", 7, 0, "", "Textos dos templates (títulos, notas)"),
        L("ARM-COTA-FORMA", 8, 0, "", "Cotas dos templates"),
        L("ARM-NIVEL", 9, 0, "", "Níveis"),
        L("ARM-HACHURA", 8, 0, "", "Hachuras dos cortes"),
        L("ARM-HACHURA-DETALHE", 8, 0, "", "Hachuras dos detalhes"),
        L("ARM-BARRA-SECAO", 8, 0, "", "Barras em ponto nas seções"),
        L("ARM-ESTRIBO-SECAO", 8, 0, "", "Estribos e grampos nas seções"),
        L("ARM-BARRA-DETALHE", 8, 0, "", "Barras dos detalhes padrão"),
        L("ARM-DETALHE", 8, 0, "", "Linhas dos detalhes padrão"),
        L("ARM-ALCA", 8, 0, "", "Alças de içamento"),
        L("ARM-ALMOFADA", 8, 0, "", "Almofadas de apoio"),
        L("ARM-CHAVE", 8, 0, "", "Chaves de cisalhamento"),
        L("ARM-FURO", 8, 0, "", "Furos"),
        L("ARM-FURO-OCULTO", 8, 0, "HIDDEN", "Furos em vista (oculto)"),
        L("ARM-INSERTO", 8, 0, "", "Insertos metálicos"),
        L("ARM-PINO", 8, 0, "", "Pinos dos consolos"),
        L("ARM-TANQUE", 4, 0, "", "Base de reservatório: tanque"),
        L("ARM-ESTACA", 5, 0, "", "Base de reservatório: estacas"),
    };

    private static PadraoLayer L(string n, short cor, double mm, string tipo, string desc) =>
        new PadraoLayer { Nome = n, Cor = cor, EspessuraMm = mm, TipoLinha = tipo, Descricao = desc };

    public const string Cabecalho = "Layer;Cor;Espessura (mm);Tipo de linha;Descrição";

    /// <summary>Texto do arquivo (com as instruções em comentário).</summary>
    public static string Escrever(IEnumerable<PadraoLayer> layers)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Padrão de layers do Armadura BR. Edite no Excel ou no Bloco de Notas (separador: ponto e vírgula).");
        sb.AppendLine("# Cor = número ACI: 1 vermelho, 2 amarelo, 3 verde, 4 ciano, 5 azul, 6 magenta, 7 preto (branco no fundo escuro), 8 e 9 cinzas.");
        sb.AppendLine("# Espessura em mm (0 = 0,00 mm). Tipo de linha: vazio = contínua (ex.: HIDDEN, DASHED, CENTER, DASHDOT).");
        sb.AppendLine("# Vale para os layers que o plugin CRIA; para aplicar nos layers que já existem num desenho use o comando ARMLAYERS.");
        sb.AppendLine(Cabecalho);
        foreach (var l in layers)
            sb.AppendLine(string.Join(";", l.Nome, l.Cor.ToString(CultureInfo.InvariantCulture),
                l.EspessuraMm.ToString("0.00", CultureInfo.GetCultureInfo("pt-BR")), l.TipoLinha, l.Descricao));
        return sb.ToString();
    }

    /// <summary>Lê o arquivo; linhas inválidas ficam de fora (avisos em <paramref name="avisos"/>).</summary>
    public static List<PadraoLayer> Ler(string texto, List<string> avisos = null)
    {
        var r = new List<PadraoLayer>();
        bool cabecalho = false;
        int n = 0;
        foreach (var bruta in (texto ?? "").Replace("\r\n", "\n").Split('\n'))
        {
            n++;
            var linha = bruta.Trim().TrimStart('﻿');
            if (linha.Length == 0 || linha.StartsWith("#")) continue;
            // o Excel pode salvar com vírgula ou tabulação conforme a configuração regional
            char sep = linha.Contains(';') ? ';' : linha.Contains('\t') ? '\t' : ',';
            var c = linha.Split(sep).Select(x => x.Trim().Trim('"')).ToArray();
            if (!cabecalho) { cabecalho = true; if (c[0].Equals("Layer", StringComparison.OrdinalIgnoreCase)) continue; }
            if (c[0].Length == 0) continue;
            var l = new PadraoLayer { Nome = c[0] };
            if (c.Length > 1 && short.TryParse(c[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var cor) && cor >= 1 && cor <= 255) l.Cor = cor;
            else avisos?.Add($"layers.csv, linha {n}: cor inválida em {c[0]} (usado 7).");
            if (c.Length > 2 && c[2].Length > 0)
            {
                if (double.TryParse(c[2].Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var mm) && mm >= 0) l.EspessuraMm = mm;
                else avisos?.Add($"layers.csv, linha {n}: espessura inválida em {c[0]} (usado 0,00).");
            }
            if (c.Length > 3) l.TipoLinha = c[3];
            if (c.Length > 4) l.Descricao = c[4];
            r.RemoveAll(x => x.Nome.Equals(l.Nome, StringComparison.OrdinalIgnoreCase));
            r.Add(l);
        }
        return r;
    }
}
