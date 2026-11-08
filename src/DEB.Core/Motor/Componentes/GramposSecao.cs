using System;
using System.Collections.Generic;
using System.Linq;
using DEB.Core.Modelo;

namespace DEB.Core.Motor.Componentes;

/// <summary>
/// <c>grampos_secao</c>: grampos suplementares de uma seção retangular vertical (pilar, arranque) — retos de face a face
/// por fora das longitudinais (medida externa = a do estribo), com ganchos de 135° para dentro, a cada espaçamento entre
/// <c>z_inicio</c> e <c>z_fim</c>. Cada grampo em x amarra um par de intermediárias das faces y (e vice-versa); sem
/// intermediárias, um grampo no meio da seção. Mesma disposição da seção do <c>barras_secao_longitudinal</c>
/// (passe os mesmos por_canto, espacamento_canto, n_face_x/y, bitolas).
/// <para>Chaves: <c>{chave}_x</c>, <c>{chave}_y</c>. Parâmetros (aceitam sobrescrita): bitola, espacamento, quantidade,
/// comprimento_reto. Informativo: linhas (grampos por nível).</para>
/// </summary>
public sealed class GramposSecao : ComponenteBase
{
    public override string Tipo => "grampos_secao";
    protected override IEnumerable<(string, string)> Docs => new[]
    {
        ("funcao", "texto: função dos grampos (bitola, espaçamento). Padrão \"grampo\"."),
        ("chave", "texto: prefixo das chaves. Padrão = funcao."),
        ("funcao_estribo", "texto: função do estribo (bitola). Padrão \"\" (usa bitola_estribo)."),
        ("bitola_estribo", "mm. Padrão 0."),
        ("funcao_longitudinal", "texto: função das longitudinais (bitola das faces, para achar as barras). Padrão \"longitudinal\"."),
        ("centro_x", "cm. Obrigatório."), ("centro_y", "cm. Obrigatório."), ("bx", "cm. Obrigatório."), ("by", "cm. Obrigatório."),
        ("cobrimento", "cm até o estribo. Padrão 3."),
        ("cobrimento_xp", "cm. Padrão = cobrimento."), ("cobrimento_xm", "cm. Padrão = cobrimento."),
        ("cobrimento_yp", "cm. Padrão = cobrimento."), ("cobrimento_ym", "cm. Padrão = cobrimento."),
        ("por_canto", "como no barras_secao_longitudinal. Padrão 1."), ("espacamento_canto", "cm. Padrão 5."),
        ("n_face_x", "intermediárias por face paralela a x. Padrão 0."), ("n_face_y", "intermediárias por face paralela a y. Padrão 0."),
        ("direcoes", "1 = só grampos em x; 2 = em x e em y. Padrão 2."),
        ("z_inicio", "cm. Obrigatório."), ("z_fim", "cm. Obrigatório."),
        ("espacamento", "cm; 0 = o da função. Padrão 0."),
    };

    protected override void Executar(ContextoMotor ctx, ArgumentosComponente a)
    {
        string funcao = a.Txt("funcao", "grampo");
        var arm = ctx.Armadura(funcao);
        if (arm == null) return;
        string chave = a.Txt("chave", funcao);
        double cx = a.Num("centro_x", 0), cy = a.Num("centro_y", 0), bx = a.Num("bx", 0), by = a.Num("by", 0);
        double z0 = a.Num("z_inicio", 0), z1 = a.Num("z_fim", 0);
        if (bx <= 0 || by <= 0 || z1 - z0 <= 0) return;
        double c = a.Num("cobrimento", 3);
        var cob = new[] { a.Num("cobrimento_xp", c), a.Num("cobrimento_ym", c), a.Num("cobrimento_xm", c), a.Num("cobrimento_yp", c) };
        string fEst = a.Txt("funcao_estribo", "");
        double phiT = fEst.Length > 0 ? ctx.Armadura(fEst)?.Bitola ?? 0 : a.Num("bitola_estribo", 0);
        double phiL = ctx.Armadura(a.Txt("funcao_longitudinal", "longitudinal"))?.Bitola ?? 10;
        int porCanto = a.Int("por_canto", 1) >= 3 ? 3 : 1;
        var secao = SecaoBarras.Posicoes(bx, by, cob, phiT, phiL, porCanto, a.Num("espacamento_canto", 5), phiL,
            Math.Max(0, a.Int("n_face_x", 0)), Math.Max(0, a.Int("n_face_y", 0)), phiL);
        // grampo em x amarra as intermediárias das faces ±x (grupo faceH: mesmo y); em y, as das faces ±y (faceB: mesmo x)
        List<double> Linhas(string grupo, Func<BarraNaSecao, double> coord, double meio)
        {
            var l = secao.Where(s => s.Grupo == grupo).Select(coord).Select(v => Math.Round(v, 6)).Distinct().OrderBy(v => v).ToList();
            return l.Count > 0 ? l : new List<double> { meio };
        }
        int dirs = Math.Max(1, Math.Min(2, a.Int("direcoes", 2)));
        foreach (bool emX in dirs == 1 ? new[] { true } : new[] { true, false })
        {
            var p = ctx.NovaPosicao($"{chave}_{(emX ? "x" : "y")}", funcao, $"grampo suplementar ({(emX ? "x" : "y")})", FormaBarra.Grampo);
            double phi = ctx.Par(p, "bitola", arm.Bitola, "cálculo (elemento)");
            if (phi <= 0) continue;
            double espArg = a.Num("espacamento", 0);
            double esp = ctx.Par(p, "espacamento", espArg > 0 ? espArg : arm.Espacamento > 0 ? arm.Espacamento : 15,
                espArg > 0 ? "argumento da receita" : arm.Espacamento > 0 ? "cálculo (elemento)" : "padrão 15 cm");
            int n = ctx.ParInt(p, "quantidade", ctx.Quantidade(z1 - z0, esp), "trecho / espaçamento");
            if (n <= 0) continue;
            var zs = ctx.Posicoes(z1 - z0, n, esp).Select(s => z0 + s).ToList();
            // medida externa = a do estribo: eixo a cobrimento + φ/2 das faces
            double ext = emX ? bx - cob[0] - cob[2] : by - cob[1] - cob[3];
            double L = ctx.Par(p, "comprimento_reto", ext, "medida externa do estribo");
            var linhas = emX ? Linhas("faceH", s => s.Y, by / 2) : Linhas("faceB", s => s.X, bx / 2);
            p.Parametros["linhas"] = linhas.Count; p.Regras["linhas"] = "um grampo por par de intermediárias das faces (ou no meio)";
            double lg = ctx.Cfg.Dobramento.ComprimentoGancho(FormatoSegmento.Gancho45, phi, ctx.Aco, estribo: true);
            double meio = emX ? cob[2] + ext / 2 : cob[1] + ext / 2;   // centro do trecho reto (coordenada da seção)
            double r = phi / 20;
            foreach (var z in zs)
                foreach (var t in linhas)
                {
                    // ganchos para o centro da seção
                    if (emX)
                    {
                        double y = cy - by / 2 + t, sg = t <= by / 2 + 1e-9 ? 1 : -1;
                        double xa = cx - bx / 2 + meio - L / 2 + r, xb = cx - bx / 2 + meio + L / 2 - r;
                        p.Barras.Add(BarrasUtil.Grampo(new P3(xa, y, z), new P3(xb, y, z), new P3(0, sg, 0), lg, true));
                    }
                    else
                    {
                        double x = cx - bx / 2 + t, sg = t <= bx / 2 + 1e-9 ? 1 : -1;
                        double ya = cy - by / 2 + meio - L / 2 + r, yb = cy - by / 2 + meio + L / 2 - r;
                        p.Barras.Add(BarrasUtil.Grampo(new P3(x, ya, z), new P3(x, yb, z), new P3(sg, 0, 0), lg, true));
                    }
                }
            p.Bitola = phi;
            p.Formatos.AddRange(new[] { FormatoSegmento.Gancho45, FormatoSegmento.Linha, FormatoSegmento.Gancho45 });
            var b0 = p.Barras[0];
            var m0 = new P3((b0[1].X + b0[2].X) / 2, (b0[1].Y + b0[2].Y) / 2, zs[0]);
            p.Distribuicao = new Distribuicao
            {
                Inicio = m0, Fim = new P3(m0.X, m0.Y, zs[zs.Count - 1]), Quantidade = zs.Count, Espacamento = esp, Repeticoes = linhas.Count,
            };
            ctx.Adicionar(p);
        }
    }
}
