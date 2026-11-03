using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace DEB.Core.Configuracao;

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

    /// <summary>Padrão de fábrica (pedidos do usuário, 2026-10-30): barra 0,30; texto preto 0,05; forma 0,20; resto 0,00; sem amarelo; cores do P56.</summary>
    public static List<PadraoLayer> Padrao() => new List<PadraoLayer>
    {
        // cor, espessura e tipo de linha copiados dos layers do escritório no P56 (316-CON-DD-100-6113, 2026-10-30):
        // Armadura, DT-Forma, Cortes, DIM/Cotas, TEXTOS, DT-Pontilhado, DT-Barras no corte/na forma, Pilares, Insertos…
        L("ARM-BARRA", 1, 0.30, "", "Barras (Armadura)"),
        L("ARM-BARRA-2", 5, 0.30, "", "Grampos U do topo/base e pinos dos consolos (Armadura, em azul)"),
        L("ARM-TEXTO", 7, 0.05, "", "Descrições e cotas das barras (Descrição da armadura / Textos armaduras)"),
        L("ARM-FAIXA", 7, 0.09, "", "Faixas de distribuição e indicações (Cotas)"),
        L("ARM-CORTE", 7, 0.09, "", "Marcas de corte e cortes das faixas (DIM)"),
        L("ARM-TABELA", 7, 0.05, "", "Relação do aço e tabelas (Relação do aço)"),
        L("ARM-FORMA", 7, 0.20, "", "Forma das peças (DT-Forma)"),
        L("ARM-PILAR-SECAO", 6, 0.40, "", "Pilar cortado: cortes de forma, planta do consolo (Cortes)"),
        L("ARM-TEXTO-FORMA", 7, 0.30, "", "Títulos e notas dos templates (TEXTOS)"),
        L("ARM-COTA-FORMA", 7, 0.09, "", "Cotas dos templates (DIM)"),
        L("ARM-NIVEL", 9, 0.09, "DASHDOT", "Níveis (DT-Pontilhado)"),
        L("ARM-HACHURA", 7, 0.20, "", "Hachuras dos cortes de forma (DT-Forma)"),
        L("ARM-HACHURA-DETALHE", 9, 0.05, "", "Hachuras dos detalhes (Hachuras)"),
        L("ARM-BARRA-SECAO", 1, 0.15, "", "Barras em ponto nas seções (DT-Barras no corte)"),
        L("ARM-ESTRIBO-SECAO", 1, 0.15, "", "Estribos e grampos nas seções (DT-Barras no corte)"),
        L("ARM-BARRA-DETALHE", 1, 0.15, "", "Barras dos detalhes (DT-Barras na forma)"),
        L("ARM-DETALHE", 8, 0.09, "", "Linhas dos detalhes padrão (Linhas)"),
        L("ARM-ALCA", 7, 0.20, "", "Alças de içamento"),
        L("ARM-ALMOFADA", 7, 0.20, "", "Almofadas de apoio"),
        L("ARM-CHAVE", 1, 0.30, "", "Chaves de cisalhamento (Pilares)"),
        L("ARM-FURO", 7, 0.25, "", "Furos (DT-Furo)"),
        L("ARM-FURO-OCULTO", 7, 0.25, "HIDDEN", "Furos em vista (oculto)"),
        L("ARM-INSERTO", 1, 0.05, "", "Insertos metálicos (Insertos)"),
        L("ARM-PINO", 1, 0.15, "", "Pinos dos consolos nas vistas e cortes (DT-Armadura)"),
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
        sb.AppendLine("# Padrão de layers do Detalhador Estrutural Brasileiro. Edite no Excel ou no Bloco de Notas (separador: ponto e vírgula).");
        sb.AppendLine("# Cor = número ACI: 1 vermelho, 2 amarelo, 3 verde, 4 ciano, 5 azul, 6 magenta, 7 preto (branco no fundo escuro), 8 e 9 cinzas.");
        sb.AppendLine("# Espessura em mm (0 = 0,00 mm). Tipo de linha: vazio = contínua (ex.: HIDDEN, DASHED, CENTER, DASHDOT).");
        sb.AppendLine("# Vale para os layers que o plugin CRIA; para aplicar nos layers que já existem num desenho use o comando ARMLAYERS.");
        sb.AppendLine(Cabecalho);
        foreach (var l in layers)
            sb.AppendLine(Persistencia.Csv.Linha(new[] { l.Nome, l.Cor.ToString(CultureInfo.InvariantCulture),
                l.EspessuraMm.ToString("0.00", CultureInfo.GetCultureInfo("pt-BR")), l.TipoLinha, l.Descricao }));
        return sb.ToString();
    }

    /// <summary>Lê o arquivo; linhas inválidas ficam de fora (avisos em <paramref name="avisos"/>).</summary>
    public static List<PadraoLayer> Ler(string texto, List<string> avisos = null)
    {
        var r = new List<PadraoLayer>();
        bool cabecalho = false;
        int n = 0;
        // o Excel pode salvar com ";", tabulação ou vírgula conforme a configuração regional
        foreach (var reg in Persistencia.Csv.Registros(texto, aceitarVirgula: true))
        {
            n++;
            var c = reg.Select(x => x.Trim()).ToArray();
            if (c.Length == 0 || (c.Length == 1 && c[0].Length == 0) || c[0].StartsWith("#")) continue;
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
