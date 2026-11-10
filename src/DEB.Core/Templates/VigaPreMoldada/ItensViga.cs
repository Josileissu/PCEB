using System;
using System.Collections.Generic;
using System.Linq;
using DEB.Core.Modelo;
using DEB.Core.Templates.PilarPreMoldado;

namespace DEB.Core.Templates.VigaPreMoldada;

/// <summary>
/// Etapa 4 da viga (2026-11-08): alça de içamento na relação do aço (cordoalha CP-190, ancorada no fundo, como o usuário
/// decidiu para o pilar) com o detalhe ALÇA DE IÇAMENTO 1:20, verificação de estabilidade lateral (NBR 9062 6.1.6) e os
/// detalhes típicos da biblioteca do escritório (DWG inseridos pelo CAD, nunca desenhados por código).
/// </summary>
public sealed partial class TemplateVigaPreMoldada
{
    /// <summary>Embutimento das pernas da alça: o digitado, ou (0) até o fundo — h menos o cobrimento do fundo.</summary>
    public static double EmbutimentoAlcaViga(ValoresTemplate v)
    {
        double dado = v.Numero("AlcaEmb");
        return dado > 0 ? dado : Math.Max(10, v.Numero("H") - v.Numero("CobFundo"));
    }

    /// <summary>
    /// NBR 9062 6.1.6 (sem a carga crítica conhecida): L/bf ≤ 50 e L·h/bf² ≤ 500, com L = distância entre as alças (6.1.2) e
    /// bf a menor largura da região comprimida (aqui, b). Devolve os dois índices.
    /// </summary>
    public static (double Esbeltez, double Indice) EstabilidadeLateral(double L, double b, double h) => (L / b, L * h / (b * b));

    /// <summary>ALÇA DE IÇAMENTO 1:20 (forma da cordoalha com o embutimento, a altura livre e a face do concreto); devolve o y abaixo.</summary>
    private static double DetalheAlca(Ctx c, ValoresTemplate v, double x, double yTopo)
    {
        double esc = 20, ht = c.hV / c.escV * esc;
        double phi = v.Numero("AlcaBitola"), emb = EmbutimentoAlcaViga(v), acima = v.Numero("AlcaAcima"), e = v.Numero("AlcaEntre"), pe = v.Numero("AlcaPe");
        double y = yTopo;
        foreach (var t in c.Linhas("ALÇA DE IÇAMENTO", "LIFTING HANDLE")) { c.Texto(t, x, y, 1.2 * ht); y -= 2 * ht; }
        c.Texto("ESC. 1:20", x, y, 0.8 * ht);
        double y0 = y - 4 * ht - emb - acima, xa = x + pe + 2 * ht;
        var bt = new BarraTemplate
        {
            Nome = "alca", Bitola = phi, Aco = "CP-190", Quantidade = Math.Max(1, v.Inteiro("AlcaPernas")), Repeticoes = 2, Escala = esc,
            Vertices = { (xa - pe, y0), (xa, y0), (xa, y0 + emb + acima - e / 2), (xa + e, y0 + emb + acima - e / 2), (xa + e, y0), (xa + e + pe, y0) },
            Bulges = { 0, 0, -1, 0, 0, 0 },
        };
        if (pe <= 0) { bt.Vertices.RemoveAt(5); bt.Vertices.RemoveAt(0); bt.Bulges.Clear(); bt.Bulges.AddRange(new[] { 0.0, -1, 0, 0 }); }
        c.R.Barras.Add(bt);
        c.Linha(xa - pe - 2 * ht, y0 + emb, xa + e + pe + 2 * ht, y0 + emb, "ARM-NIVEL");
        c.Cota(xa + e + pe + ht, y0, xa + e + pe + ht, y0 + emb, xa + e + pe + 2.5 * ht, y0 + emb / 2, Math.PI / 2, esc);
        c.Cota(xa + e + pe + ht, y0 + emb, xa + e + pe + ht, y0 + emb + acima, xa + e + pe + 2.5 * ht, y0 + emb + acima / 2, Math.PI / 2, esc);
        c.Texto(c.Idioma == IdiomaDesenho.EN ? "(strand CP-190)" : "(cordoalha CP-190)", xa + e / 2, y0 - 6 * ht, 0.8 * ht, 1);
        return y0 - 10 * ht;
    }
}
