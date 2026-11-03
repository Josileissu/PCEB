using System;
using System.Collections.Generic;
using System.Linq;

namespace Armadura.Core.Modelo;

/// <summary>Campos que aparecem no texto da descrição.</summary>
[Flags]
public enum CamposDescricao
{
    Nenhum = 0,
    Quantidade = 1,
    Numero = 2,
    Bitola = 4,
    Espacamento = 8,
    Comprimento = 16,
    Todos = Quantidade | Numero | Bitola | Espacamento | Comprimento,
}

/// <summary>Propriedades de uma armadura (barra). A geometria fica na polilinha do CAD.</summary>
public sealed class DadosArmadura
{
    public const int VersaoEsquema = 1;

    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Numeração N; 0 = barra nova, ainda não numerada ("N0").</summary>
    public int Numero { get; set; }
    /// <summary>Bitola em mm.</summary>
    public double Bitola { get; set; } = 10.0;
    public string Aco { get; set; } = "CA-50";
    public int Quantidade { get; set; } = 1;
    public int Repeticoes { get; set; } = 1;
    /// <summary>Espaçamento em cm (0 = não indicar).</summary>
    public double Espacamento { get; set; }
    public bool Estribo { get; set; }
    public bool Corrido { get; set; }
    public bool Visivel { get; set; } = true;
    public CamposDescricao Campos { get; set; } = CamposDescricao.Todos;
    /// <summary>Escala da barra (1:Escala): define a altura dos textos. 0 = escala base da configuração.</summary>
    public double Escala { get; set; }

    /// <summary>Handle (hex) do MText da descrição; vazio = sem descrição.</summary>
    public string HandleDescricao { get; set; } = "";
    /// <summary>Posição da descrição em relação ao 1º vértice (unidades do desenho).</summary>
    public double DescricaoDx { get; set; }
    public double DescricaoDy { get; set; }
    public bool TemPosicaoDescricao { get; set; }
    /// <summary>Quebras de linha que o usuário deu na descrição (depois da palavra k; ver QuebrasTexto).</summary>
    public List<int> QuebrasDescricao { get; set; } = new List<int>();

    public List<Segmento> Segmentos { get; set; } = new List<Segmento>();

    /// <summary>Handles (hex) das faixas da barra. Com faixas, a quantidade é a soma delas.</summary>
    public List<string> Faixas { get; set; } = new List<string>();

    /// <summary>Handles (hex) das armaduras rebatidas (espelhos vinculados).</summary>
    public List<string> Rebatidas { get; set; } = new List<string>();

    public bool TemFaixas => Faixas.Count > 0;

    /// <summary>Armação treliçada: nome do modelo (ex.: "TR 8644"); a polilinha é o banzo inferior.</summary>
    public string Trelica { get; set; } = "";
    /// <summary>Tela soldada: nome do modelo (ex.: "Q-138"); a polilinha fechada é o painel.</summary>
    public string Tela { get; set; } = "";
    /// <summary>
    /// Barras de comprimento variável numa posição só (ex.: malha de base circular): comprimento EXTERNO de
    /// cada barra (cm), na ordem da distribuição. A polilinha desenhada é a representativa (forma e dobras);
    /// a quantidade é o nº de itens e o comprimento real de cada uma desconta as mesmas dobras.
    /// </summary>
    public List<double> ComprimentosVar { get; set; } = new List<double>();
    public bool EhVarLista => ComprimentosVar.Count > 0;
    /// <summary>Tela: desenha os fios (distribuição do aço) dentro do painel.</summary>
    public bool TelaDistribuicao { get; set; }
    /// <summary>Tela: cotas nas dimensões do painel (largura e altura).</summary>
    public bool TelaCotas { get; set; } = true;
    /// <summary>Tela: a descrição ("1 Q-92") acompanha a diagonal do painel.</summary>
    public bool TelaTextoDiagonal { get; set; } = true;
    /// <summary>
    /// Tela identificada (padrão do escritório, 2026-10-27): "T1 - 2x Q-92" e, embaixo, "245 x 348" no meio da
    /// diagonal, que fica interrompida no texto; as dimensões vão no texto (sem cotas nos lados).
    /// </summary>
    public bool TelaIdentificada { get; set; }
    /// <summary>Tela identificada: número T (painéis iguais = mesmo T), dado pela renumeração da relação.</summary>
    public int NumeroTela { get; set; }
    /// <summary>Handles (hex) do desenho gerado (banzos/sinusoide da treliça, diagonais da tela).</summary>
    public List<string> PartesDesenho { get; set; } = new List<string>();

    public bool EhTrelica => !string.IsNullOrWhiteSpace(Trelica);
    public bool EhTela => !string.IsNullOrWhiteSpace(Tela);
    /// <summary>Barra comum (nem treliça nem tela): aceita faixas, ganchos e traspasse.</summary>
    public bool EhBarraComum => !EhTrelica && !EhTela;

    /// <summary>RA bloqueada: hash da forma quando a barra recebeu o número; vazio = não travada.</summary>
    public string AssinaturaBloqueio { get; set; } = "";
    /// <summary>Posição nova/alterada depois do bloqueio (destacada na tabela até desbloquear).</summary>
    public bool Nova { get; set; }

    /// <summary>
    /// Barras em camadas (lajes nervuradas), ex.: "3+1" = 3 numa camada e 1 na outra por nervura.
    /// Vazio = sem camadas. A quantidade (própria ou das faixas) passa a ser o nº de nervuras.
    /// </summary>
    public string Camadas { get; set; } = "";

    /// <summary>Barras por nervura (soma das camadas); 1 sem camadas.</summary>
    public int BarrasPorNervura
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Camadas)) return 1;
            int soma = 0;
            foreach (var p in Camadas.Split('+'))
                if (int.TryParse(p.Trim(), out var n) && n > 0) soma += n;
            return soma > 0 ? soma : 1;
        }
    }

    /// <summary>Alguma parte da barra é "VAR" (ou é um grupo de comprimentos variáveis).</summary>
    public bool TemVar => EhVarLista || Segmentos.Any(s => s.Var);

    /// <summary>Quantidade que entra na RA (sem faixas, por enquanto).</summary>
    public int QuantidadeTotal(int repeticoesDetalhamento = 1) =>
        Math.Max(0, Quantidade) * BarrasPorNervura * Math.Max(1, Repeticoes) * Math.Max(1, repeticoesDetalhamento);

    /// <summary>
    /// Ajusta a lista de segmentos ao número de segmentos da geometria. Se o número mudou
    /// (vértice inserido/removido), mantém os dados do início e cria segmentos novos como "Linha".
    /// </summary>
    public void AjustarSegmentos(int quantidade)
    {
        while (Segmentos.Count > quantidade) Segmentos.RemoveAt(Segmentos.Count - 1);
        while (Segmentos.Count < quantidade) Segmentos.Add(new Segmento());
    }

    public DadosArmadura Clonar()
    {
        var c = (DadosArmadura)MemberwiseClone();
        c.Segmentos = Segmentos.Select(s => s.Clonar()).ToList();
        c.Faixas = new List<string>(Faixas);
        c.Rebatidas = new List<string>(Rebatidas);
        c.PartesDesenho = new List<string>(PartesDesenho);
        c.ComprimentosVar = new List<double>(ComprimentosVar);
        return c;
    }
}
