using System;
using System.Collections.Generic;

namespace DEB.Core.Templates.PilarPreMoldado;

/// <summary>Um consolo para casar pares em nível: face (1 a 4; 1↔3 e 2↔4 opostas) e medidas em cm.</summary>
public readonly struct ConsoloNoNivel
{
    public ConsoloNoNivel(int face, double topo, double alturaFace, double centro, double largura, double balanco, double alturaPonta, string armadura = "")
    {
        Face = face; Topo = topo; AlturaFace = alturaFace; Centro = centro; Largura = largura; Balanco = balanco; AlturaPonta = alturaPonta;
        Armadura = armadura ?? "";
    }
    public int Face { get; }
    public double Topo { get; }
    public double AlturaFace { get; }
    /// <summary>Centro ao longo da face, no eixo comum às duas faces opostas (y nas faces 1 e 3, x nas 2 e 4).</summary>
    public double Centro { get; }
    public double Largura { get; }
    public double Balanco { get; }
    public double AlturaPonta { get; }
    /// <summary>Identifica a armadura (tipo do catálogo no CAD): só casam consolos com a mesma.</summary>
    public string Armadura { get; }
}

/// <summary>
/// Tirante e costura do consolo — regra única do desenho do CAD (<see cref="TemplatePilarPreMoldado"/>, detalhe "ARMADURAS
/// console") e do modelo 3D do Revit (<see cref="Modelo3D.ColumnRebarLayout"/>). Decisões do dono (D30, 16/11/2026):
/// <list type="bullet">
/// <item>consolo sozinho: a barra vai da ponta (menos o cobrimento) até por dentro das longitudinais da face oposta (face
/// oposta − cobrimento − φ do estribo − φ da longitudinal), sem colidir com elas;</item>
/// <item>consolos em faces opostas e EM NÍVEL (<see cref="ParesEmNivel"/>) "se tornam um tipo, com tirante igual": consolo
/// duplo, com tirante e costura passantes de ponta a ponta (sem barras sobrepostas). Em níveis diferentes, um por consolo.</item>
/// </list>
/// Medidas em cm, externas (convenção do CAD, D7); bitolas em mm.
/// </summary>
public static class BarrasConsolo
{
    /// <summary>
    /// Tolerância de "em nível" (cm): a mesma que já casa consolos opostos ao passar as peças do Revit para o CAD
    /// (<see cref="Intercambio.ConversorPilarPeca.Tolerancia"/>, 0,6 cm) — topo, altura, centro, largura, balanço e altura da
    /// ponta iguais dentro dela.
    /// </summary>
    public const double ToleranciaNivel = Intercambio.ConversorPilarPeca.Tolerancia;

    /// <summary>Faces opostas (numeração interna 1↔3, 2↔4).</summary>
    public static bool Opostas(int a, int b) => (a + 1) % 4 + 1 == b;

    /// <summary>
    /// Pares de consolos que formam um duplo: faces opostas, mesmo nível e mesma forma (dentro de <see cref="ToleranciaNivel"/>)
    /// e a mesma armadura. Cada consolo entra em um par no máximo (os primeiros da lista primeiro).
    /// </summary>
    public static List<(int A, int B)> ParesEmNivel(IReadOnlyList<ConsoloNoNivel> c)
    {
        var r = new List<(int, int)>();
        var usado = new bool[c.Count];
        bool Perto(double x, double y) => Math.Abs(x - y) <= ToleranciaNivel;
        for (int i = 0; i < c.Count; i++)
        {
            if (usado[i]) continue;
            for (int j = i + 1; j < c.Count; j++)
            {
                if (usado[j]) continue;
                ConsoloNoNivel a = c[i], b = c[j];
                if (!Opostas(a.Face, b.Face) || a.Armadura != b.Armadura) continue;
                if (!(Perto(a.Topo, b.Topo) && Perto(a.AlturaFace, b.AlturaFace) && Perto(a.Centro, b.Centro) &&
                      Perto(a.Largura, b.Largura) && Perto(a.Balanco, b.Balanco) && Perto(a.AlturaPonta, b.AlturaPonta))) continue;
                usado[i] = usado[j] = true;
                r.Add((i, j));
                break;
            }
        }
        return r;
    }

    /// <summary>
    /// Distância da face do pilar onde está o consolo até a face externa da barra no outro lado: a face interna das
    /// longitudinais da face oposta (consolo sozinho).
    /// </summary>
    /// <param name="profundidade">lado do pilar na direção do balanço</param>
    /// <param name="cobOposta">cobrimento da face oposta (até o estribo)</param>
    public static double FimNoPilar(double profundidade, double cobOposta, double phiEstriboMm, double phiLongMm) =>
        profundidade - cobOposta - phiEstriboMm / 10 - phiLongMm / 10;

    /// <summary>
    /// Trecho ao longo do balanço do tirante e da costura de um consolo sozinho (medida externa): da ponta do consolo menos o
    /// cobrimento até <see cref="FimNoPilar"/>.
    /// </summary>
    public static double Horizontal(double balanco, double cobPonta, double fimNoPilar) => balanco - cobPonta + fimNoPilar;

    /// <summary>Tirante e costura passantes do duplo (medida externa): de ponta a ponta, menos os cobrimentos das pontas.</summary>
    public static double Passante(double balanco1, double balanco2, double profundidade, double cobPonta1, double cobPonta2) =>
        balanco1 - cobPonta1 + profundidade + balanco2 - cobPonta2;
}
