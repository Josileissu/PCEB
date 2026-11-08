using System;
using System.Collections.Generic;
using System.Linq;

namespace DEB.Core.Motor.Componentes;

/// <summary>
/// <c>verificacoes_geometria</c>: avisos de geometria — eixo do apoio (estaca) mais perto da face que o raio; pilar fora
/// do contorno (caixa envolvente). Cada verificação só roda se os seus argumentos forem dados.
/// </summary>
public sealed class VerificacoesGeometria : ComponenteBase
{
    public override string Tipo => "verificacoes_geometria";
    protected override IEnumerable<(string, string)> Docs => new[]
    {
        ("dist_face", "cm: eixo do apoio → face (com raio_apoio)."), ("raio_apoio", "cm: raio da estaca."),
        ("centro_x", "cm: centro do pilar (com centro_y, pilar_bx, pilar_by)."), ("centro_y", "cm."), ("pilar_bx", "cm."), ("pilar_by", "cm."),
        ("contorno", "texto: polígono em ctx.Dados para a verificação do pilar. Padrão \"contorno\"."),
        ("texto_pilar_fora", "texto do aviso. Padrão \"O pilar sai do elemento.\"."),
    };

    protected override void Executar(ContextoMotor ctx, ArgumentosComponente a)
    {
        if (a.Tem("dist_face") && a.Tem("raio_apoio"))
        {
            double e = a.Num("dist_face", 0), R = a.Num("raio_apoio", 0);
            if (e < R) ctx.Aviso($"Distância do eixo da estaca à face ({FormatoMotor.Num(e)} cm) menor que o raio da estaca ({FormatoMotor.Num(R)} cm).");
        }
        if (a.Tem("centro_x") && a.Tem("pilar_bx"))
        {
            var cont = Poligono(ctx, a);
            double Px = a.Num("centro_x", 0), Py = a.Num("centro_y", 0), Bx = a.Num("pilar_bx", 0), By = a.Num("pilar_by", 0);
            if (Px - Bx / 2 < cont.Min(p => p.X) || Px + Bx / 2 > cont.Max(p => p.X) || Py - By / 2 < cont.Min(p => p.Y) || Py + By / 2 > cont.Max(p => p.Y))
                ctx.Aviso(a.Txt("texto_pilar_fora", "O pilar sai do elemento."));
        }
    }
}

/// <summary>
/// <c>verificacoes_calice</c>: verificações do cálice pela NBR 9062 (separadas das armaduras): embutimento ≥ 40 cm
/// (7.7.2.4) e ≥ 1,2·h (chaves) ou 1,5·h (lisa/rugosa) (menor valor da Tabela 15); parede do colarinho ≥ 15 cm e altura
/// sob o cálice ≥ 20 cm (Fig. 25); colarinho maior que o contorno (limitado).
/// <para>Consome Dados "calice_furo" (de <c>calice_forma</c>) e <c>contorno</c>.</para>
/// </summary>
public sealed class VerificacoesCalice : ComponenteBase
{
    public override string Tipo => "verificacoes_calice";
    protected override IEnumerable<(string, string)> Docs => new[]
    {
        ("pilar_bx", "cm. Obrigatório."), ("pilar_by", "cm. Obrigatório."),
        ("embutimento", "cm: Lemb. Obrigatório."),
        ("interface", "texto: \"chaves\", \"rugosa\" ou \"lisa\". Padrão \"chaves\"."),
        ("colarinho", "1 = cálice com colarinho. Padrão 1."),
        ("parede", "cm. Padrão 21."),
        ("z_topo", "cm: topo do cálice. Obrigatório."),
        ("regularizacao", "cm. Padrão 0."),
        ("contorno", "texto: polígono em ctx.Dados. Padrão \"contorno\"."),
    };

    protected override void Executar(ContextoMotor ctx, ArgumentosComponente a)
    {
        double Lemb = a.Num("embutimento", 0), t = a.Num("parede", 21);
        bool colar = a.Bool("colarinho", true);
        double h = Math.Max(a.Num("pilar_bx", 0), a.Num("pilar_by", 0));
        if (Lemb < 40) ctx.Aviso($"Embutimento {FormatoMotor.Num(Lemb)} cm < 40 cm (NBR 9062 7.7.2.4).");
        double fator = a.Txt("interface", "chaves") == "chaves" ? 1.2 : 1.5;
        if (Lemb < fator * h)
            ctx.Aviso($"Embutimento {FormatoMotor.Num(Lemb)} cm < {FormatoMotor.NumV(fator)}·h = {FormatoMotor.Num(Math.Ceiling(fator * h))} cm (menor valor da NBR 9062 Tabela 15) — conferir com Md/(Nd·h).");
        if (colar && t < 15) ctx.Aviso($"Parede do colarinho {FormatoMotor.Num(t)} cm < 15 cm (NBR 9062 Fig. 25).");
        double fundo = a.Num("z_topo", 0) - Lemb - Math.Max(0, a.Num("regularizacao", 0));
        if (fundo < 20) ctx.Aviso($"Altura sob o cálice {FormatoMotor.Num(fundo)} cm < 20 cm (NBR 9062 Fig. 25).");
        var O = ctx.Dado<Retangulo>("calice_furo");
        var cont = ctx.Dado<List<(double X, double Y)>>(a.Txt("contorno", "contorno"));
        if (colar && O != null && cont != null)
        {
            double x0 = cont.Min(p => p.X), y0 = cont.Min(p => p.Y), x1 = cont.Max(p => p.X), y1 = cont.Max(p => p.Y);
            if (O.X0 - t < x0 - 1e-6 || O.X1 + t > x1 + 1e-6 || O.Y0 - t < y0 - 1e-6 || O.Y1 + t > y1 + 1e-6)
                ctx.Aviso("Colarinho maior que o bloco numa direção: limitado à face do bloco (como no B28).");
        }
    }
}
