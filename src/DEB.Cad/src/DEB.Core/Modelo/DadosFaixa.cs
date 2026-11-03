using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DEB.Core.Modelo;

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
    /// Chamada/indicação com patamar (padrão do escritório, P56): as linhas das barras se encontram num ponto ao lado do texto
    /// e um trecho horizontal liga esse ponto ao texto ("N12 ——").
    /// </summary>
    public bool Patamar { get; set; }

    /// <summary>
    /// Linhas de chamada apagadas pelo usuário (índice da referência; corte+chamada = 0): não são recriadas
    /// até ele voltar "Mostrar linha de chamada" para Sim (pedido do usuário, 2026-10-27).
    /// </summary>
    public List<int> LinhasApagadas { get; set; } = new List<int>();

    /// <summary>Quebras de linha que o usuário deu no texto da faixa (depois da palavra k; ver QuebrasTexto).</summary>
    public List<int> Quebras { get; set; } = new List<int>();

    /// <summary>
    /// Repetições da faixa (ex.: a mesma distribuição em 2 camadas): a quantidade que ela soma na barra é
    /// multiplicada, e o texto vira "2x11 N1 …" como a descrição da barra (pedido do usuário, 2026-10-27).
    /// </summary>
    public int Repeticoes { get; set; } = 1;

    /// <summary>
    /// Âncoras da última atualização (faixas de texto): posição do texto e centro do 1º círculo do corte. Se o conjunto
    /// inteiro aparece deslocado (EXPLODE de detalhamento, edição do bloco no lugar, MOVE de tudo), os pontos guardados
    /// (linha base, arco, referências) andam junto — antes as chamadas iam para as coordenadas do bloco.
    /// </summary>
    public bool TemAncora { get; set; }
    public double AncoraX { get; set; }
    public double AncoraY { get; set; }
    public bool TemCirculo0 { get; set; }
    public double Circulo0X { get; set; }
    public double Circulo0Y { get; set; }

    /// <summary>Desloca todos os pontos guardados (linha base, centro do arco, referências e âncoras).</summary>
    public void Deslocar(double dx, double dy)
    {
        P1X += dx; P1Y += dy; P2X += dx; P2Y += dy;
        if (Raio > 0) { CX += dx; CY += dy; }
        Referencias = Referencias.Select(r => (r.X + dx, r.Y + dy)).ToList();
        if (TemAncora) { AncoraX += dx; AncoraY += dy; }
        if (TemCirculo0) { Circulo0X += dx; Circulo0Y += dy; }
    }

    /// <summary>Quantidade que a faixa soma na barra (com as repetições).</summary>
    public int QuantidadeTotal => SomaQuantidade ? Math.Max(0, Quantidade) * Math.Max(1, Repeticoes) : 0;

    /// <summary>Campos opcionais do texto da faixa (todos ligados por padrão).</summary>
    public bool MostrarQuantidade { get; set; } = true;
    public bool MostrarPosicao { get; set; } = true;
    public bool MostrarDiametro { get; set; } = true;
    public bool MostrarEspacamento { get; set; } = true;
    /// <summary>Faixa em cota: o comprimento da faixa abaixo da linha de cota (pedido do usuário, 2026-10-30; liga/desliga na paleta).</summary>
    public bool MostrarComprimento { get; set; } = true;

    /// <summary>
    /// Indicação com mais dados no texto (pedido do usuário, 2026-11-03): quantidade (a digitada; 0 = a da barra), diâmetro e
    /// espaçamento (o da barra). Desligados por padrão — as indicações antigas continuam "N3". A indicação nunca soma na barra.
    /// </summary>
    public bool IndicarQuantidade { get; set; }
    public bool IndicarDiametro { get; set; }
    public bool IndicarEspacamento { get; set; }

    /// <summary>O texto deste tipo de faixa pode levar a quantidade (todos; na indicação, só se ligada).</summary>
    public bool TextoTemQuantidade => true;
    /// <summary>O texto deste tipo de faixa pode levar diâmetro e espaçamento (a chamada não).</summary>
    public bool TextoTemDiametroEspacamento => Tipo != TipoFaixa.Chamada;

    private bool Indicacao => Tipo == TipoFaixa.Indicacao;

    /// <summary>
    /// Campos do texto, um nome só para todos os tipos (pedido do usuário, 2026-11-03): na indicação valem os "Indicar…"
    /// (desligados por padrão, para as antigas continuarem "N3"); nas outras, os "Mostrar…" (ligados por padrão).
    /// </summary>
    public bool QuantidadeNoTexto
    {
        get => Indicacao ? IndicarQuantidade : MostrarQuantidade;
        set { if (Indicacao) IndicarQuantidade = value; else MostrarQuantidade = value; }
    }

    public bool DiametroNoTexto
    {
        get => Indicacao ? IndicarDiametro : MostrarDiametro;
        set { if (Indicacao) IndicarDiametro = value; else MostrarDiametro = value; }
    }

    public bool EspacamentoNoTexto
    {
        get => Indicacao ? IndicarEspacamento : MostrarEspacamento;
        set { if (Indicacao) IndicarEspacamento = value; else MostrarEspacamento = value; }
    }

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
        // repetições coladas na quantidade, sem parênteses: "2x11 N1 ø10.0 c/15" (a indicação não soma: não repete)
        string rep = f.SomaQuantidade ? Descricao.FormatadorDescricao.Repeticao(f.Repeticoes) : "";
        var campos = new List<string>();
        // indicação: a quantidade digitada (0 = a da barra) e o espaçamento da barra (ela não distribui)
        bool indic = f.Tipo == TipoFaixa.Indicacao;
        bool comQtd = f.TextoTemQuantidade && f.QuantidadeNoTexto;
        int qtd = indic && f.Quantidade <= 0 ? barra.Quantidade : f.Quantidade;
        if (comQtd) campos.Add(rep + qtd.ToString(CultureInfo.InvariantCulture));
        if (f.MostrarPosicao) campos.Add(n);
        if (f.TextoTemDiametroEspacamento && f.DiametroNoTexto) campos.Add(bit);
        double sEsp = indic ? barra.Espacamento : f.Espacamento;
        if (f.TextoTemDiametroEspacamento && f.EspacamentoNoTexto && (!indic || sEsp > 0))
            campos.Add("c/" + Calculo.CalculoComprimento.NumeroCompacto(sEsp));
        // texto vazio deixaria a faixa invisível e impossível de selecionar: fica ao menos a posição
        string texto = campos.Count > 0 ? string.Join(" ", campos) : n;
        // sem a quantidade no texto, a repetição vai na frente: "2x N1 ø10.0 c/15"
        return !comQtd && rep.Length > 0 ? rep + " " + texto : texto;
    }
}
