using System;
using System.Globalization;

namespace DEB.Core;

/// <summary>
/// Formatação de números num lugar só (2026-11-10, reuso): antes cada arquivo tinha a sua cópia de "N", "Num" e "M".
/// Três usos diferentes, que não devem ser misturados:
///   <see cref="N"/>  — mensagens, notas e valores de parâmetros (ponto decimal, cultura invariante);
///   <see cref="Virgula"/> — textos da PRANCHA em português (vírgula decimal);
///   <see cref="Mm"/> — chaves e assinaturas (medida em cm arredondada ao milímetro, sem decimal).
/// Os arquivos mantêm um atalho local de uma linha (N, Num, M) que chama estas funções com o formato daquele arquivo.
/// </summary>
public static class Fmt
{
    /// <summary>Número com ponto decimal e cultura invariante ("0.#" = uma casa, sem zeros à direita).</summary>
    public static string N(double x, string formato = "0.#") => x.ToString(formato, CultureInfo.InvariantCulture);

    /// <summary>Número com vírgula decimal, para os textos da prancha.</summary>
    public static string Virgula(double x, string formato) => N(x, formato).Replace('.', ',');

    /// <summary>Medida em cm arredondada ao milímetro, como inteiro (para chaves e assinaturas: 25,04 cm → "250").</summary>
    public static string Mm(double cm) => Math.Round(cm * 10).ToString("0", CultureInfo.InvariantCulture);

    /// <summary>Fração como porcentagem inteira (0,953 → "95%").</summary>
    public static string Pct(double fracao) => N(fracao * 100, "0") + "%";
}
