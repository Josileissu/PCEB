using System;
using System.Collections.Generic;

namespace DEB.Core.Geometria.Secao;

/// <summary>
/// Números de face da seção retangular usados hoje ↔ partes nomeadas, num lugar só (D28). Seção b (horizontal, x do pilar)
/// × h (vertical, y do pilar); "Topo" = +y e "Fundo" = −y na planta do pilar. Os campos gravados (Face inteira no JSON de
/// peças, XData, parâmetros de template) continuam com o número; a conversão é só na leitura e no uso.
/// <list type="bullet">
/// <item>numeração INTERNA (geometria, arquivo de peças, cobrimentos por face): 1 = +x (Direita), 2 = −y (Fundo),
/// 3 = −x (Esquerda), 4 = +y (Topo);</item>
/// <item>numeração APRESENTADA (prancha do pilar, o que o usuário digita): 1 = oeste (Esquerda), 2 = sul (Fundo),
/// 3 = leste (Direita), 4 = norte (Topo).</item>
/// </list>
/// </summary>
public static class FacesRetangulo
{
    private static readonly ParteSecao[] Internas = { ParteSecao.Direita, ParteSecao.Fundo, ParteSecao.Esquerda, ParteSecao.Topo };
    private static readonly ParteSecao[] Apresentadas = { ParteSecao.Esquerda, ParteSecao.Fundo, ParteSecao.Direita, ParteSecao.Topo };

    public static ParteSecao DaFaceInterna(int face) => Internas[Indice(face)];
    public static ParteSecao DaFaceApresentada(int face) => Apresentadas[Indice(face)];
    public static int FaceInterna(ParteSecao parte) => Numero(Internas, parte);
    public static int FaceApresentada(ParteSecao parte) => Numero(Apresentadas, parte);

    /// <summary>Interna → apresentada e vice-versa (troca 1 e 3); fora de 1…4 devolve o mesmo número.</summary>
    public static int TrocarNumeracao(int face) => face >= 1 && face <= 4 ? FaceApresentada(DaFaceInterna(face)) : face;

    /// <summary>Face oposta: topo ↔ fundo, esquerda ↔ direita.</summary>
    public static ParteSecao Oposta(ParteSecao parte) => parte switch
    {
        ParteSecao.Topo => ParteSecao.Fundo,
        ParteSecao.Fundo => ParteSecao.Topo,
        ParteSecao.Esquerda => ParteSecao.Direita,
        ParteSecao.Direita => ParteSecao.Esquerda,
        _ => throw new ArgumentOutOfRangeException(nameof(parte), $"{parte} não é face do retângulo."),
    };

    /// <summary>Cobrimentos por parte a partir do vetor por face interna (posições 0…3; o resto, topo e base do pilar, fica de fora).</summary>
    public static Dictionary<ParteSecao, double> Cobrimentos(IReadOnlyList<double> porFaceInterna)
    {
        if (porFaceInterna == null || porFaceInterna.Count < 4) throw new ArgumentException("Cobrimentos: são 4 faces.");
        var r = new Dictionary<ParteSecao, double>();
        for (int i = 0; i < 4; i++) r[Internas[i]] = porFaceInterna[i];
        return r;
    }

    /// <summary>Vetor por face interna (1 = +x … 4 = +y) a partir dos cobrimentos por parte; exceção se faltar face.</summary>
    public static double[] CobrimentosPorFace(IReadOnlyDictionary<ParteSecao, double> porParte)
    {
        var r = new double[4];
        for (int i = 0; i < 4; i++)
        {
            if (porParte == null || !porParte.TryGetValue(Internas[i], out r[i]))
                throw new ArgumentException($"Cobrimentos: falta o da face {Internas[i]}.");
        }
        return r;
    }

    private static int Indice(int face) =>
        face >= 1 && face <= 4 ? face - 1 : throw new ArgumentOutOfRangeException(nameof(face), $"Face {face}: use 1 a 4.");

    private static int Numero(ParteSecao[] tabela, ParteSecao parte)
    {
        int i = Array.IndexOf(tabela, parte);
        return i >= 0 ? i + 1 : throw new ArgumentOutOfRangeException(nameof(parte), $"{parte} não é face do retângulo.");
    }
}
