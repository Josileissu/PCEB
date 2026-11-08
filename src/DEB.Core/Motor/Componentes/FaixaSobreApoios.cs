using System;
using System.Collections.Generic;
using System.Linq;
using static DEB.Core.Motor.Componentes.GeometriaPlanta;
using static DEB.Core.Motor.Geometria2D;

namespace DEB.Core.Motor.Componentes;

/// <summary>
/// <c>faixa_sobre_apoios</c>: armadura concentrada em faixas sobre as linhas de apoios (principal do bloco sobre
/// estacas; também faixas sobre pilares de laje lisa, sobre estacas de radier, de sapata associada…). Uma posição por
/// direção; uma faixa (q barras a cada espaçamento, centrada na linha) por linha de apoios. Se a faixa passar da face, é
/// deslocada para dentro; se não couber, o passo é reduzido. Barras retas com pernas, VAR quando os comprimentos
/// diferem no polígono.
/// <para>Linhas: "grade" = uma linha por fila de apoios alinhados em x (barras em x) e por coluna (barras em y), quando
/// há ≥ 2 apoios na direção; "lados" = um lado por par de apoios consecutivos (triângulo de 3 estacas).</para>
/// <para>Chaves: grade → <c>{chave}_x</c>, <c>{chave}_y</c>; lados → <c>{chave}_lado1</c>, <c>_lado2</c>…</para>
/// <para>Consome Dados <c>contorno</c>, <c>apoios</c> (nomes nos argumentos) e "arranjo" (linhas = auto). Publica a maior
/// bitola (mm) em <c>publicar_bitola</c> e o topo da camada (z da face interna, cm) em <c>publicar_topo</c>.</para>
/// <para>Parâmetros (aceitam sobrescrita): bitola, quantidade, espacamento, perna, comprimento_reto (sem VAR).</para>
/// </summary>
public sealed class FaixaSobreApoios : ComponenteBase
{
    public override string Tipo => "faixa_sobre_apoios";
    public override IReadOnlyCollection<string> VariaveisPublicadas { get; } = new[] { "bitola_principal", "z_topo_inferior" };
    public override IReadOnlyCollection<string> VariaveisPublicadasCom(ArgumentosComponente a)
    {
        string f = a.Txt("funcao", Funcoes.Principal);
        return new[] { a.Txt("publicar_bitola", "bitola_" + f), a.Txt("publicar_topo", a.Bool("superior", false) ? "" : "z_topo_inferior") }
            .Where(n => n.Length > 0).ToArray();
    }
    protected override IEnumerable<(string, string)> Docs => new[]
    {
        ("funcao", "texto: função da armadura calculada (bitola, barras por faixa, espaçamento). Padrão \"principal\"."),
        ("chave", "texto: prefixo das chaves das posições. Padrão = funcao."),
        ("nome", "texto: prefixo dos nomes legíveis. Padrão = chave."),
        ("linhas", "texto: \"grade\", \"lados\" ou \"auto\" (lados se Dados arranjo = \"triangulo\"). Padrão \"auto\"."),
        ("contorno", "texto: polígono em ctx.Dados. Padrão \"contorno\"."),
        ("apoios", "texto: lista de pontos em ctx.Dados. Padrão \"estacas\"."),
        ("cobrimento", "cm. Padrão 4."),
        ("recuo_lateral", "cm a mais de recuo nas pontas e na face (ø da armadura lateral / 10). Padrão 0."),
        ("z_face", "cm: z da face externa da camada (inferior: face de baixo). Obrigatório."),
        ("superior", "1 = faixa na face de cima (pernas para baixo). Padrão 0."),
        ("empilhar_y", "1 = a direção y fica uma bitola acima (abaixo, se superior) da x; 0 = não; −1 = auto (só em grade). Padrão −1."),
        ("perna", "cm: perna nas pontas (0 = reta). Padrão 0."),
        ("regra_perna", "texto: regra gravada para a perna. Padrão \"argumento da receita\"."),
        ("limite_perna_z", "cm: z que a ponta da perna MANUAL não deve passar (aviso). Sem = não verifica."),
        ("texto_limite_perna", "texto do aviso (\"do topo da base menos o cobrimento\"). Padrão \"do limite\"."),
        ("publicar_bitola", "texto: variável com a maior bitola (mm). Padrão \"bitola_\" + funcao."),
        ("publicar_topo", "texto: variável com o z da face interna da camada (inferior: máx.; superior: mín.). Padrão \"z_topo_inferior\" (inferior) ou \"\" (superior)."),
        ("regra_faixas", "texto: regra gravada para o parâmetro \"faixas\". Padrão \"uma por linha de apoios\"."),
    };

    protected override void Executar(ContextoMotor ctx, ArgumentosComponente a)
    {
        string funcao = a.Txt("funcao", Funcoes.Principal);
        var arm = ctx.Armadura(funcao);
        if (arm == null) return;
        var contorno = Poligono(ctx, a);
        var apoios = ctx.Dado<List<(double X, double Y)>>(a.Txt("apoios", "estacas")) ?? new List<(double X, double Y)>();
        string modo = a.Txt("linhas", "auto");
        if (modo == "auto") modo = ctx.Dado<string>("arranjo") == "triangulo" ? "lados" : "grade";
        bool lados = modo == "lados";
        string chaveBase = a.Txt("chave", funcao), nomeBase = a.Txt("nome", chaveBase);
        double cob = a.Num("cobrimento", 4), recuo = a.Num("recuo_lateral", 0), zFace0 = a.Num("z_face", cob);
        bool sup = a.Bool("superior", false);
        int emp = a.Int("empilhar_y", -1);
        bool empilhar = emp < 0 ? !lados : emp > 0;
        string regraPerna = a.Txt("regra_perna", "argumento da receita");
        string pubBit = a.Txt("publicar_bitola", "bitola_" + funcao), pubTopo = a.Txt("publicar_topo", sup ? "" : "z_topo_inferior");

        var faixas = new List<(double Ang, double T)>();
        if (lados)
            for (int i = 0; i < apoios.Count && apoios.Count >= 2; i++)
            {
                var pa = apoios[i]; var pb = apoios[(i + 1) % apoios.Count];
                if (apoios.Count == 2 && i == 1) break;
                double ang = Math.Atan2(pb.Y - pa.Y, pb.X - pa.X);
                if (ang < -1e-9) ang += Math.PI;
                if (ang >= Math.PI - 1e-9) ang -= Math.PI;
                faixas.Add((ang, Dot((-Math.Sin(ang), Math.Cos(ang)), pa)));
            }
        else
        {
            if (apoios.Select(p => Math.Round(p.X, 3)).Distinct().Count() >= 2)
                foreach (var y in apoios.Select(p => Math.Round(p.Y, 6)).Distinct()) faixas.Add((0, y));
            if (apoios.Select(p => Math.Round(p.Y, 3)).Distinct().Count() >= 2)
                foreach (var x in apoios.Select(p => Math.Round(p.X, 6)).Distinct()) faixas.Add((Math.PI / 2, -x));
        }
        int k = 0;
        foreach (var grupo in faixas.GroupBy(f => Math.Round(f.Ang, 6)))
        {
            k++;
            bool emY = Math.Abs(grupo.Key - Math.PI / 2) < 1e-6;
            string chave = lados ? chaveBase + "_lado" + k : emY ? chaveBase + "_y" : chaveBase + "_x";
            string nome = lados ? nomeBase + " lado " + k : emY ? nomeBase + " y" : nomeBase + " x";
            var p = ctx.NovaPosicao(chave, funcao, nome, FormaBarra.U);
            double phi = ctx.Par(p, "bitola", arm.Bitola, "cálculo (elemento)");
            int q = ctx.ParInt(p, "quantidade", arm.Quantidade, "cálculo: barras por faixa");
            double esp = ctx.Par(p, "espacamento", arm.Espacamento, "cálculo: espaçamento na faixa");
            double perna = ctx.Par(p, "perna", a.Num("perna", 0), regraPerna);
            if (phi <= 0 || q <= 0) continue;
            double pilha = emY && empilhar ? phi / 10 : 0;
            var f = new FamiliaRetas
            {
                Ang = grupo.Key, Bitola = phi, Esp = esp, Perna = perna, PorFaixa = q, Superior = sup, PassoReal = true,
                CobrimentoNormal = a.Bool("cobrimento_normal", true),
                Z = sup ? zFace0 - phi / 20 - pilha : zFace0 + phi / 20 + pilha,
            };
            // faixa centrada na linha de apoios; se passar da face, desloca para dentro (como o B31: 7 c/14 com face a 40)
            double lo = contorno.Min(pt => Dot(f.N, pt)) + cob + recuo + phi / 20;
            double hi = contorno.Max(pt => Dot(f.N, pt)) - cob - recuo - phi / 20;
            int faixasOk = 0;
            foreach (var (_, T) in grupo)
            {
                double t0 = T - (q - 1) / 2.0 * esp, t1 = T + (q - 1) / 2.0 * esp, d = 0;
                if (t0 < lo) d = lo - t0; else if (t1 > hi) d = hi - t1;
                double passo = q > 1 && t1 - t0 > hi - lo ? (hi - lo) / (q - 1) : esp;
                int antes = f.Linhas.Count;
                for (int i = 0; i < q; i++) f.AdicionarLinha(contorno, cob, recuo, passo == esp ? t0 + d + i * esp : lo + i * passo);
                if (f.Linhas.Count > antes) faixasOk++;
            }
            if (f.Linhas.Count == 0) continue;
            f.Faixas = Math.Max(1, faixasOk);
            p.Parametros["faixas"] = f.Faixas;
            p.Regras["faixas"] = a.Txt("regra_faixas", "uma por linha de apoios");
            if (p.Manuais.Contains("perna") && a.Tem("limite_perna_z"))
            {
                double lim = a.Num("limite_perna_z", 0), txt = 0;
                string texto = a.Txt("texto_limite_perna", "do limite");
                bool passa;
                if (sup) { double zf = f.Z + phi / 20; passa = zf - perna < lim - 0.5; txt = Math.Floor(zf - lim); }
                else { double zf = f.Z - phi / 20; passa = zf + perna > lim + 0.5; txt = Math.Floor(lim - zf); }
                if (passa) ctx.Aviso($"{chave}: perna manual {FormatoMotor.Num(perna)} cm passa {texto} ({FormatoMotor.Num(txt)} cm).");
            }
            PublicarMax(ctx, pubBit, phi);
            if (!sup) PublicarMax(ctx, pubTopo, f.Z + phi / 20);
            else PublicarMin(ctx, pubTopo, f.Z - phi / 20);
            f.Montar(ctx, p);
        }
    }
}
