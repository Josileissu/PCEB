using System;
using System.Collections.Generic;
using System.Globalization;

namespace Armadura.Core.Modelo;

/// <summary>Tipos de faixa de armadura (seção 2.3).</summary>
public enum TipoFaixa
{
    Ortogonal = 0,
    Alinhada = 1,
    CorteCotaOrtogonal = 2,
    CorteCotaAlinhada = 3,
    CorteChamada = 4,
    CorteChamadaArco = 5,
    Chamada = 6,
    Indicacao = 7,
}

/// <summary>Regra para a quantidade de barras numa faixa (configurável).</summary>
public enum ModoQuantidadeFaixa
{
    /// <summary>n = ⌊(L − 2·e0)/s⌋ + 1</summary>
    PisoMaisUm = 0,
    /// <summary>n = ⌈(L − 2·e0)/s⌉ + 1</summary>
    TetoMaisUm = 1,
    /// <summary>n = ⌈(L − 2·e0)/s⌉</summary>
    Teto = 2,
}

/// <summary>Dados de uma faixa, gravados na entidade principal dela (cota ou texto).</summary>
public sealed class DadosFaixa
{
    public TipoFaixa Tipo { get; set; }
    /// <summary>Handle (hex) da barra.</summary>
    public string HandleBarra { get; set; } = "";
    /// <summary>Espaçamento em cm.</summary>
    public double Espacamento { get; set; } = 15;
    /// <summary>Espaçamento inicial como fração do espaçamento (0 a 1).</summary>
    public double EspacamentoInicial { get; set; }
    /// <summary>Quantidade: calculada (faixas com comprimento) ou digitada (Chamada).</summary>
    public int Quantidade { get; set; }
    /// <summary>Lado do corte: +1 à esquerda da linha base (p1→p2), −1 à direita.</summary>
    public int LadoCorte { get; set; } = 1;
    /// <summary>Ponto da linha base (corte e chamada / chamada / indicação): coordenadas do desenho.</summary>
    public double P1X { get; set; }
    public double P1Y { get; set; }
    public double P2X { get; set; }
    public double P2Y { get; set; }
    /// <summary>Corte e chamada: onde a chamada encosta (0 início, 1 centro, 2 fim).</summary>
    public int PontoChamada { get; set; } = 1;
    /// <summary>Arco (corte e chamada em arco): centro, raio, ângulos inicial/final (rad).</summary>
    public double CX { get; set; }
    public double CY { get; set; }
    public double Raio { get; set; }
    public double AnguloIni { get; set; }
    public double AnguloFim { get; set; }
    /// <summary>Pontos de referência das chamadas (Chamada/Indicação).</summary>
    public List<(double X, double Y)> Referencias { get; set; } = new List<(double, double)>();
    /// <summary>Handles das partes: círculos do corte e linhas de chamada.</summary>
    public List<string> Partes { get; set; } = new List<string>();
    /// <summary>
    /// Escala da faixa (1:n) para textos, cota e círculos do corte; 0 = a da barra. Permite distribuir
    /// numa seção 1:25 a barra desenhada numa elevação 1:50 (pedido do usuário, 2026-10-27).
    /// </summary>
    public double Escala { get; set; }

    /// <summary>Chamada, indicação e corte+chamada: oculta as linhas de chamada (os pontos ficam guardados).</summary>
    public bool OcultarLinhas { get; set; }

    /// <summary>
    /// Linhas de chamada apagadas pelo usuário (índice da referência; corte+chamada = 0): não são recriadas
    /// até ele voltar "Mostrar linha de chamada" para Sim (pedido do usuário, 2026-10-27).
    /// </summary>
    public List<int> LinhasApagadas { get; set; } = new List<int>();

    /// <summary>
    /// Repetições da faixa (ex.: a mesma distribuição em 2 camadas): a quantidade que ela soma na barra é
    /// multiplicada, e o texto vira "2x(...)" como a descrição da barra (pedido do usuário, 2026-10-27).
    /// </summary>
    public int Repeticoes { get; set; } = 1;

    /// <summary>Quantidade que a faixa soma na barra (com as repetições).</summary>
    public int QuantidadeTotal => SomaQuantidade ? Math.Max(0, Quantidade) * Math.Max(1, Repeticoes) : 0;

    /// <summary>Campos opcionais do texto da faixa (todos ligados por padrão).</summary>
    public bool MostrarQuantidade { get; set; } = true;
    public bool MostrarPosicao { get; set; } = true;
    public bool MostrarDiametro { get; set; } = true;
    public bool MostrarEspacamento { get; set; } = true;

    /// <summary>O texto deste tipo de faixa leva quantidade (a indicação só mostra a posição).</summary>
    public bool TextoTemQuantidade => Tipo != TipoFaixa.Indicacao;
    /// <summary>O texto deste tipo de faixa leva diâmetro e espaçamento (chamada e indicação não).</summary>
    public bool TextoTemDiametroEspacamento => Tipo != TipoFaixa.Indicacao && Tipo != TipoFaixa.Chamada;

    /// <summary>A linha de chamada da referência <paramref name="indice"/> deve ser desenhada.</summary>
    public bool MostraLinha(int indice) => !OcultarLinhas && !LinhasApagadas.Contains(indice);

    /// <summary>A faixa tem linhas de chamada (texto ligado por linhas à barra ou ao corte).</summary>
    public bool TemLinhasDeChamada => Tipo == TipoFaixa.Chamada || Tipo == TipoFaixa.Indicacao
                                      || Tipo == TipoFaixa.CorteChamada || Tipo == TipoFaixa.CorteChamadaArco;

    /// <summary>Escala efetiva: a da faixa ou, sem ela, a da barra.</summary>
    public double EscalaEfetiva(double escalaDaBarra) => Escala > 0 ? Escala : escalaDaBarra;

    public bool TemCota => Tipo <= TipoFaixa.CorteCotaAlinhada;
    public bool TemCorte => Tipo >= TipoFaixa.CorteCotaOrtogonal && Tipo <= TipoFaixa.CorteChamadaArco;
    public bool QuantidadeDigitada => Tipo == TipoFaixa.Chamada;
    /// <summary>Indicação só aponta a barra: não soma quantidade.</summary>
    public bool SomaQuantidade => Tipo != TipoFaixa.Indicacao;
}

public static class CalculoFaixa
{
    /// <summary>Quantidade de barras distribuídas em <paramref name="comprimento"/> (cm) com espaçamento s.</summary>
    public static int Quantidade(double comprimento, double espacamento, double fracaoInicial, ModoQuantidadeFaixa modo,
        bool fechada = false)
    {
        if (espacamento <= 0 || comprimento <= 0) return comprimento > 0 ? 1 : 0;
        if (fechada) return Math.Max(1, (int)Math.Round(comprimento / espacamento)); // circunferência: sem "+1"
        double e0 = Math.Max(0, Math.Min(1, fracaoInicial)) * espacamento;
        double util = comprimento - 2 * e0;
        if (util < 0) return 1;
        double q = util / espacamento;
        const double tol = 1e-6; // 400/15 exato não vira 27,0000001
        return modo switch
        {
            ModoQuantidadeFaixa.TetoMaisUm => (int)Math.Ceiling(q - tol) + 1,
            ModoQuantidadeFaixa.Teto => Math.Max(1, (int)Math.Ceiling(q - tol)),
            _ => (int)Math.Floor(q + tol) + 1,
        };
    }

    /// <summary>Distâncias (cm, a partir do início) das barras na faixa.</summary>
    public static List<double> Posicoes(double comprimento, int n, double espacamento, double fracaoInicial, bool fechada = false)
    {
        var r = new List<double>();
        if (n <= 0) return r;
        if (fechada)
        {
            double passoCirc = comprimento / n;
            for (int i = 0; i < n; i++) r.Add(i * passoCirc);
            return r;
        }
        if (n == 1) { r.Add(comprimento / 2); return r; }
        double e0 = Math.Max(0, Math.Min(1, fracaoInicial)) * espacamento;
        double util = Math.Max(0, comprimento - 2 * e0);
        // barras a cada s a partir de e0; se a regra de arredondamento pôs barra além do fim,
        // redistribui por igual para nenhuma ficar fora da faixa
        double passo = (n - 1) * espacamento <= util + 1e-6 ? espacamento : util / (n - 1);
        for (int i = 0; i < n; i++) r.Add(e0 + i * passo);
        return r;
    }
}

/// <summary>Textos das faixas.</summary>
public static class TextoFaixa
{
    public static string Formatar(DadosFaixa f, DadosArmadura barra, Configuracao.ConfiguracaoArmadura cfg)
    {
        string n = cfg.PrefixoPosicao + barra.Numero.ToString(CultureInfo.InvariantCulture);
        string bit = cfg.SimboloDiametro + barra.Bitola.ToString("0.0", CultureInfo.InvariantCulture);
        string esp = "c/" + Calculo.CalculoComprimento.NumeroCompacto(f.Espacamento);
        var campos = new List<string>();
        if (f.TextoTemQuantidade && f.MostrarQuantidade) campos.Add(f.Quantidade.ToString(CultureInfo.InvariantCulture));
        if (f.MostrarPosicao) campos.Add(n);
        if (f.TextoTemDiametroEspacamento && f.MostrarDiametro) campos.Add(bit);
        if (f.TextoTemDiametroEspacamento && f.MostrarEspacamento) campos.Add(esp);
        // texto vazio deixaria a faixa invisível e impossível de selecionar: fica ao menos a posição
        string texto = campos.Count > 0 ? string.Join(" ", campos) : n;
        // repetições como na descrição da barra (a indicação não soma quantidade: não repete)
        return f.Repeticoes > 1 && f.SomaQuantidade
            ? f.Repeticoes.ToString(CultureInfo.InvariantCulture) + "x(" + texto + ")"
            : texto;
    }
}
