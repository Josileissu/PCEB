using System;
using System.Collections.Generic;
using System.Linq;
using DEB.Core.Modelo;
using DEB.Core.Tabelas;

namespace DEB.Core.Motor.Componentes;

/// <summary>
/// <c>barras_secao_longitudinal</c>: longitudinais de uma seção retangular vertical (pilar, arranque, tirante) entre
/// <c>z_inicio</c> e <c>z_fim</c> — cantos com 1 barra ou 3 em "L" (canto + vizinhas), intermediárias nas faces, emendas
/// pelo comprimento comercial (traspasse do escritório: NBR 6118, mín. 45φ, a cada 5 cm; alternadas ou na mesma seção).
/// Cálculo em <see cref="SecaoBarras"/> (o mesmo do template do pilar pré-moldado).
/// <para>Chaves: <c>{chave}_canto</c>, <c>{chave}_vizinha</c>, <c>{chave}_face_x</c>, <c>{chave}_face_y</c>; com emendas, a
/// 1ª peça tem essa chave e as outras <c>_2</c>, <c>_3</c>… (grupo A) e <c>_b1</c>, <c>_b2</c>… (grupo alternado B).</para>
/// <para>Parâmetros (aceitam sobrescrita, na chave do grupo): bitola, traspasse. Informativos: quantidade, comprimento,
/// comercial.</para>
/// </summary>
public sealed class SecaoLongitudinal : ComponenteBase
{
    public override string Tipo => "barras_secao_longitudinal";
    protected override IEnumerable<(string, string)> Docs => new[]
    {
        ("funcao", "texto: função das barras dos cantos (bitola). Padrão \"longitudinal\"."),
        ("funcao_vizinha", "texto: função das vizinhas do canto em \"L\". Padrão = funcao."),
        ("funcao_face", "texto: função das intermediárias das faces. Padrão = funcao."),
        ("funcao_estribo", "texto: função do estribo (a bitola afasta as barras da face). Padrão \"\" (usa bitola_estribo)."),
        ("bitola_estribo", "mm, quando não há funcao_estribo. Padrão 0."),
        ("chave", "texto: prefixo das chaves das posições. Padrão = funcao."),
        ("centro_x", "cm: centro da seção. Obrigatório."), ("centro_y", "cm. Obrigatório."),
        ("bx", "cm: lado em x. Obrigatório."), ("by", "cm: lado em y. Obrigatório."),
        ("cobrimento", "cm até o estribo, em todas as faces. Padrão 3."),
        ("cobrimento_xp", "cm na face +x. Padrão = cobrimento."), ("cobrimento_xm", "cm na face −x. Padrão = cobrimento."),
        ("cobrimento_yp", "cm na face +y. Padrão = cobrimento."), ("cobrimento_ym", "cm na face −y. Padrão = cobrimento."),
        ("z_inicio", "cm: ponta de baixo das barras. Obrigatório."), ("z_fim", "cm: ponta de cima. Obrigatório."),
        ("por_canto", "1 = uma barra por canto; 3 = canto em \"L\" (canto + uma vizinha em cada face). Padrão 1."),
        ("espacamento_canto", "cm entre eixos da barra do canto e das vizinhas (por_canto = 3). Padrão 5."),
        ("n_face_x", "intermediárias em cada face paralela a x (sem os cantos). Padrão 0."),
        ("n_face_y", "intermediárias em cada face paralela a y. Padrão 0."),
        ("emendas", "1 = emenda pelo comprimento comercial; 0 = barras inteiras (aviso se passar do comercial). Padrão 1."),
        ("mesma_secao", "1 = todas as emendas na mesma seção; 0 = alternadas (metade das barras deslocada). Padrão 0."),
        ("comercial", "cm; 0 = o da tabela de bitolas (limitado a 1200). Padrão 0."),
        ("fck", "MPa (traspasse). Padrão = fck do elemento."),
        ("nome", "texto: nome legível (prefixo). Padrão \"longitudinal\"."),
    };

    private sealed class Grupo
    {
        public string Nome, Funcao, Chave;
        public double Bitola;
        public List<(double X, double Y)> Pontos = new List<(double X, double Y)>();
    }

    protected override void Executar(ContextoMotor ctx, ArgumentosComponente a)
    {
        string funcao = a.Txt("funcao", "longitudinal");
        var arm = ctx.Armadura(funcao);
        if (arm == null) return;
        string chave = a.Txt("chave", funcao), nome = a.Txt("nome", "longitudinal");
        string fViz = a.Txt("funcao_vizinha", funcao), fFace = a.Txt("funcao_face", funcao);
        double cx = a.Num("centro_x", 0), cy = a.Num("centro_y", 0), bx = a.Num("bx", 0), by = a.Num("by", 0);
        double z0 = a.Num("z_inicio", 0), z1 = a.Num("z_fim", 0);
        if (bx <= 0 || by <= 0 || z1 - z0 <= 0)
        {
            ctx.Aviso($"{Tipo} ({chave}): seção {FormatoMotor.Num(bx)}×{FormatoMotor.Num(by)} ou trecho {FormatoMotor.Num(z0)}–{FormatoMotor.Num(z1)} inválido.");
            return;
        }
        double c = a.Num("cobrimento", 3);
        var cob = new[] { a.Num("cobrimento_xp", c), a.Num("cobrimento_ym", c), a.Num("cobrimento_xm", c), a.Num("cobrimento_yp", c) };
        string fEst = a.Txt("funcao_estribo", "");
        double phiT = fEst.Length > 0 ? ctx.Armadura(fEst)?.Bitola ?? 0 : a.Num("bitola_estribo", 0);
        int porCanto = a.Int("por_canto", 1) >= 3 ? 3 : 1;
        double sCanto = a.Num("espacamento_canto", 5);
        int nFx = Math.Max(0, a.Int("n_face_x", 0)), nFy = Math.Max(0, a.Int("n_face_y", 0));

        // bitolas (sobrescrita na chave do grupo) antes das posições: a bitola do canto afasta as barras da face
        double Bitola(string sufixo, string f)
        {
            var ar = ctx.Armadura(f) ?? arm;
            return ctx.Sobrescrita($"{chave}_{sufixo}", "bitola") ?? ar.Bitola;
        }
        double phiC = Bitola("canto", funcao);
        double phiV = porCanto == 3 ? Bitola("vizinha", fViz) : phiC;
        double phiFx = nFx > 0 ? Bitola("face_x", fFace) : phiC, phiFy = nFy > 0 ? Bitola("face_y", fFace) : phiC;
        if (phiC <= 0) return;
        // as faces x e y podem ter bitolas diferentes (sobrescrita): cada uma sai da sua chamada
        var secao = SecaoBarras.Posicoes(bx, by, cob, phiT, phiC, porCanto, sCanto, phiV, nFx, 0, phiFx)
            .Concat(SecaoBarras.Posicoes(bx, by, cob, phiT, phiC, porCanto, sCanto, phiV, 0, nFy, phiFy).Where(s => s.Grupo == "faceH"))
            .ToList();

        var grupos = new List<Grupo>();
        foreach (var (g, sufixo, f, nomeG) in new[]
                 {
                     ("canto", "canto", funcao, "cantos"), ("vizinha", "vizinha", fViz, "vizinhas do canto"),
                     ("faceB", "face_x", fFace, "faces x"), ("faceH", "face_y", fFace, "faces y"),
                 })
        {
            var bs = secao.Where(s => s.Grupo == g).ToList();
            if (bs.Count == 0) continue;
            grupos.Add(new Grupo
            {
                Nome = $"{nome} ({nomeG})", Funcao = f, Chave = $"{chave}_{sufixo}", Bitola = bs[0].Bitola,
                Pontos = bs.Select(s => (cx - bx / 2 + s.X, cy - by / 2 + s.Y)).ToList(),
            });
        }

        bool emendas = a.Bool("emendas", true), mesma = a.Bool("mesma_secao", false);
        double fck = a.Num("fck", ctx.Elemento.Fck);
        foreach (var g in grupos)
        {
            var info = ctx.Cfg.Barras.Obter(g.Bitola, ctx.Aco);
            double comercialArg = a.Num("comercial", 0);
            double comercial = comercialArg > 0 ? comercialArg
                : info != null && info.ComprimentoComercial > 0 ? Math.Min(info.ComprimentoComercial, 1200) : 1200;
            double vao = z1 - z0;
            var p0 = ctx.NovaPosicao(g.Chave, g.Funcao, g.Nome, FormaBarra.Reta);
            ctx.Par(p0, "bitola", g.Bitola, "cálculo (elemento)");
            double lap = SecaoBarras.Traspasse(g.Bitola, ctx.Aco, fck, mesma, ctx.Cfg.Barras);
            lap = ctx.Par(p0, "traspasse", lap, mesma ? "NBR 6118 (100 % na mesma seção), mín. 45φ, a cada 5 cm"
                                                      : "NBR 6118 (50 % emendado), mín. 45φ, a cada 5 cm");
            List<PecaEmendada> pecas;
            if (!emendas || vao <= comercial + 1e-6)
            {
                if (!emendas && vao > comercial && (info == null || info.Fabricacao != Fabricacao.Rolo))
                    ctx.Aviso($"{g.Chave}: ø{FormatoMotor.Num(g.Bitola)} sem emendas ({FormatoMotor.Num(vao)} cm) passa do comercial " +
                              $"({FormatoMotor.Num(comercial)} cm) — confira a disponibilidade em rolo.");
                pecas = new List<PecaEmendada> { new PecaEmendada(new PecaBarra(z0, z1), g.Pontos.Count, false) };
            }
            else pecas = SecaoBarras.Emendadas(z0, z1, g.Pontos.Count, comercial, lap, mesma);

            // grupo A: os pontos de índice par (alternados ao longo do contorno); B: os ímpares
            var pontosA = mesma ? g.Pontos : g.Pontos.Where((_, i) => i % 2 == 0).ToList();
            var pontosB = mesma ? new List<(double X, double Y)>() : g.Pontos.Where((_, i) => i % 2 == 1).ToList();
            int kA = 0, kB = 0;
            foreach (var pe in pecas)
            {
                bool b = pe.Alternada;
                var pts = pe.Quantidade == g.Pontos.Count ? g.Pontos : b ? pontosB : pontosA;
                PosicaoArmadura p;
                if (!b && kA == 0) p = p0;
                else
                {
                    string k = b ? $"{g.Chave}_b{kB + 1}" : $"{g.Chave}_{kA + 1}";
                    p = ctx.NovaPosicao(k, g.Funcao, g.Nome, FormaBarra.Reta);
                    p.Parametros["bitola"] = g.Bitola;
                    p.Parametros["traspasse"] = lap;
                }
                if (b) kB++; else kA++;
                p.Bitola = g.Bitola;
                p.Parametros["quantidade"] = pts.Count; p.Regras["quantidade"] = "barras do grupo na seção" + (pecas.Count > 1 && !mesma ? " (metade alternada)" : "");
                p.Parametros["comprimento"] = pe.Peca.Comprimento;
                p.Regras["comprimento"] = pecas.Count > 1 ? "peça emendada (comprimento comercial e traspasse)" : "z_fim − z_inicio";
                p.Parametros["comercial"] = comercial;
                foreach (var (x, y) in pts) p.Barras.Add(new List<P3> { new P3(x, y, pe.Peca.Ini), new P3(x, y, pe.Peca.Fim) });
                p.Formatos.Add(FormatoSegmento.Linha);
                p.Distribuicao = new Distribuicao
                {
                    Inicio = new P3(pts[0].X, pts[0].Y, pe.Peca.Ini), Fim = new P3(pts[pts.Count - 1].X, pts[pts.Count - 1].Y, pe.Peca.Ini),
                    Quantidade = pts.Count,
                };
                ctx.Adicionar(p);
            }
        }
    }
}
