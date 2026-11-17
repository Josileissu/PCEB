using System;
using System.Collections.Generic;
using System.Linq;
using DEB.Core.Pecas;

namespace DEB.Core.Geometria.Secao;

/// <summary>Parâmetro de uma família de seção: nome curto (chave), unidade e descrição.</summary>
public sealed class ParametroSecao
{
    public ParametroSecao(string nome, string unidade, string descricao) { Nome = nome; Unidade = unidade; Descricao = descricao; }
    public string Nome { get; }
    public string Unidade { get; }
    public string Descricao { get; }
}

/// <summary>
/// Família de seção transversal de elemento linear (D28): identificador, parâmetros com nome e unidade, e a regra que gera
/// o contorno e as partes nomeadas na posição canônica (origem no canto inferior esquerdo da caixa, y na horizontal, z na
/// vertical, cm). A orientação (espelho) é aplicada por <see cref="SecaoTransversal"/>; "invertido" não é família.
/// As famílias de concreto estão em <see cref="FamiliasSecao"/>.
/// </summary>
public abstract class FamiliaSecao
{
    /// <summary>Identificador estável ("retangular", "T", "L", "I", "trapezoidal", "poligonal").</summary>
    public abstract string Id { get; }
    public abstract string Nome { get; }
    public abstract IReadOnlyList<ParametroSecao> Parametros { get; }

    /// <summary>Mensagem de erro se os valores não formam a seção; null se formam.</summary>
    internal abstract string Validar(Func<string, double> v);

    /// <summary>Contorno na posição canônica, no sentido anti-horário.</summary>
    internal abstract List<(double Y, double Z)> ContornoCanonico(Func<string, double> v);

    /// <summary>Partes nomeadas na posição canônica.</summary>
    internal abstract List<ParteNomeada> PartesCanonicas(Func<string, double> v);

    public override string ToString() => Nome;

    // atalhos para as famílias
    protected static ParametroSecao Cm(string nome, string descricao) => new ParametroSecao(nome, "cm", descricao);
    protected static ParteNomeada Face(ParteSecao p, double y0, double z0, double y1, double z1) => ParteNomeada.Face(p, (y0, z0), (y1, z1));
    protected static ParteNomeada Regiao(ParteSecao p, double y0, double z0, double y1, double z1) =>
        ParteNomeada.DaRegiao(p, new FaixaSecao { Y0 = y0, Z0 = z0, Y1 = y1, Z1 = z1 });
    protected static string Positivos(Func<string, double> v, params string[] nomes)
    {
        foreach (var n in nomes) if (!(v(n) > 0)) return $"{n} deve ser maior que zero.";
        return null;
    }
}

/// <summary>Famílias de seção de concreto da D28 e a família "poligonal" (seção medida, sem parâmetros).</summary>
public static class FamiliasSecao
{
    public static readonly FamiliaSecao Retangular = new FamiliaRetangular();
    public static readonly FamiliaSecao T = new FamiliaT();
    public static readonly FamiliaSecao L = new FamiliaL();
    public static readonly FamiliaSecao I = new FamiliaI();
    public static readonly FamiliaSecao Trapezoidal = new FamiliaTrapezoidal();
    /// <summary>Seção cuja fonte é só o polígono medido (o que não casa com nenhuma família).</summary>
    public static readonly FamiliaSecao Poligonal = new FamiliaPoligonal();

    public static IReadOnlyList<FamiliaSecao> Todas { get; } = new[] { Retangular, T, L, I, Trapezoidal, Poligonal };

    /// <summary>Família pelo identificador (sem diferenciar maiúsculas); null se não existe.</summary>
    public static FamiliaSecao PorId(string id) => Todas.FirstOrDefault(f => string.Equals(f.Id, id, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Retângulo b × h. Partes: topo, fundo, esquerda, direita.</summary>
internal sealed class FamiliaRetangular : FamiliaSecao
{
    public override string Id => "retangular";
    public override string Nome => "Retangular";
    public override IReadOnlyList<ParametroSecao> Parametros { get; } = new[] { Cm("b", "largura"), Cm("h", "altura") };

    internal override string Validar(Func<string, double> v) => Positivos(v, "b", "h");

    internal override List<(double Y, double Z)> ContornoCanonico(Func<string, double> v)
    {
        double b = v("b"), h = v("h");
        return new List<(double, double)> { (0, 0), (b, 0), (b, h), (0, h) };
    }

    internal override List<ParteNomeada> PartesCanonicas(Func<string, double> v)
    {
        double b = v("b"), h = v("h");
        return new List<ParteNomeada>
        {
            Face(ParteSecao.Topo, 0, h, b, h), Face(ParteSecao.Fundo, 0, 0, b, 0),
            Face(ParteSecao.Esquerda, 0, 0, 0, h), Face(ParteSecao.Direita, b, 0, b, h),
        };
    }
}

/// <summary>
/// T: mesa em cima (posição canônica) sobre a alma; a mesa passa da alma <c>ae</c> à esquerda e <c>ad</c> à direita (os dois
/// maiores que zero; um zero é L). T invertido = esta família com espelho vertical. Mesa = a faixa inteira de altura hf;
/// alma = abaixo da mesa.
/// </summary>
internal sealed class FamiliaT : FamiliaSecao
{
    public override string Id => "T";
    public override string Nome => "T";
    public override IReadOnlyList<ParametroSecao> Parametros { get; } = new[]
    {
        Cm("bw", "largura da alma"), Cm("h", "altura total"), Cm("hf", "espessura da mesa"),
        Cm("ae", "balanço da mesa à esquerda da alma"), Cm("ad", "balanço da mesa à direita da alma"),
    };

    internal override string Validar(Func<string, double> v) =>
        Positivos(v, "bw", "h", "hf", "ae", "ad") ?? (v("hf") < v("h") ? null : "hf deve ser menor que h.");

    internal override List<(double Y, double Z)> ContornoCanonico(Func<string, double> v)
    {
        double bw = v("bw"), h = v("h"), hf = v("hf"), ae = v("ae"), b = ae + bw + v("ad"), zm = h - hf;
        return new List<(double, double)> { (ae, 0), (ae + bw, 0), (ae + bw, zm), (b, zm), (b, h), (0, h), (0, zm), (ae, zm) };
    }

    internal override List<ParteNomeada> PartesCanonicas(Func<string, double> v)
    {
        double bw = v("bw"), h = v("h"), hf = v("hf"), ae = v("ae"), b = ae + bw + v("ad"), zm = h - hf;
        return new List<ParteNomeada>
        {
            Face(ParteSecao.Topo, 0, h, b, h), Face(ParteSecao.Fundo, ae, 0, ae + bw, 0),
            Face(ParteSecao.Esquerda, 0, zm, 0, h), Face(ParteSecao.Direita, b, zm, b, h),
            Face(ParteSecao.LateralAlmaEsquerda, ae, 0, ae, zm), Face(ParteSecao.LateralAlmaDireita, ae + bw, 0, ae + bw, zm),
            Regiao(ParteSecao.Mesa, 0, zm, b, h), Regiao(ParteSecao.Alma, ae, 0, ae + bw, zm),
        };
    }
}

/// <summary>
/// L: alma à esquerda, na altura toda; aba embaixo, saindo <c>ba</c> à direita da alma (posição canônica). As outras três
/// posições são espelhos. Alma = a faixa inteira de largura bw; aba = só o que passa da alma.
/// </summary>
internal sealed class FamiliaL : FamiliaSecao
{
    public override string Id => "L";
    public override string Nome => "L";
    public override IReadOnlyList<ParametroSecao> Parametros { get; } = new[]
    {
        Cm("bw", "largura da alma"), Cm("h", "altura total"), Cm("hf", "espessura da aba"), Cm("ba", "balanço da aba além da alma"),
    };

    internal override string Validar(Func<string, double> v) =>
        Positivos(v, "bw", "h", "hf", "ba") ?? (v("hf") < v("h") ? null : "hf deve ser menor que h.");

    internal override List<(double Y, double Z)> ContornoCanonico(Func<string, double> v)
    {
        double bw = v("bw"), h = v("h"), hf = v("hf"), b = bw + v("ba");
        return new List<(double, double)> { (0, 0), (b, 0), (b, hf), (bw, hf), (bw, h), (0, h) };
    }

    internal override List<ParteNomeada> PartesCanonicas(Func<string, double> v)
    {
        double bw = v("bw"), h = v("h"), hf = v("hf"), b = bw + v("ba");
        return new List<ParteNomeada>
        {
            Face(ParteSecao.Topo, 0, h, bw, h), Face(ParteSecao.Fundo, 0, 0, b, 0),
            Face(ParteSecao.Esquerda, 0, 0, 0, h), Face(ParteSecao.Direita, b, 0, b, hf),
            Face(ParteSecao.LateralAlmaDireita, bw, hf, bw, h),
            Regiao(ParteSecao.Alma, 0, 0, bw, h), Regiao(ParteSecao.Aba, bw, 0, b, hf),
        };
    }
}

/// <summary>
/// I: mesa superior (hs, balanços ase e asd) e mesa inferior (hi, balanços aie e aid) ligadas pela alma bw; os quatro
/// balanços maiores que zero. Mesas = as faixas inteiras; alma = entre elas. Sem face "esquerda"/"direita" (cada mesa tem a
/// sua ponta): as faces laterais nomeadas são as da alma.
/// </summary>
internal sealed class FamiliaI : FamiliaSecao
{
    public override string Id => "I";
    public override string Nome => "I";
    public override IReadOnlyList<ParametroSecao> Parametros { get; } = new[]
    {
        Cm("bw", "largura da alma"), Cm("h", "altura total"),
        Cm("hs", "espessura da mesa superior"), Cm("ase", "mesa superior: balanço à esquerda"), Cm("asd", "mesa superior: balanço à direita"),
        Cm("hi", "espessura da mesa inferior"), Cm("aie", "mesa inferior: balanço à esquerda"), Cm("aid", "mesa inferior: balanço à direita"),
    };

    internal override string Validar(Func<string, double> v) =>
        Positivos(v, "bw", "h", "hs", "ase", "asd", "hi", "aie", "aid") ?? (v("hs") + v("hi") < v("h") ? null : "hs + hi deve ser menor que h.");

    internal override List<(double Y, double Z)> ContornoCanonico(Func<string, double> v)
    {
        double bw = v("bw"), h = v("h"), hs = v("hs"), hi = v("hi"), ase = v("ase"), asd = v("asd"), aie = v("aie"), aid = v("aid");
        double ya = Math.Max(ase, aie), yb = ya + bw, zs = h - hs;   // faces da alma; fundo da mesa superior
        return new List<(double, double)>
        {
            (ya - aie, 0), (yb + aid, 0), (yb + aid, hi), (yb, hi), (yb, zs), (yb + asd, zs), (yb + asd, h),
            (ya - ase, h), (ya - ase, zs), (ya, zs), (ya, hi), (ya - aie, hi),
        };
    }

    internal override List<ParteNomeada> PartesCanonicas(Func<string, double> v)
    {
        double bw = v("bw"), h = v("h"), hs = v("hs"), hi = v("hi"), ase = v("ase"), asd = v("asd"), aie = v("aie"), aid = v("aid");
        double ya = Math.Max(ase, aie), yb = ya + bw, zs = h - hs;
        return new List<ParteNomeada>
        {
            Face(ParteSecao.Topo, ya - ase, h, yb + asd, h), Face(ParteSecao.Fundo, ya - aie, 0, yb + aid, 0),
            Face(ParteSecao.LateralAlmaEsquerda, ya, hi, ya, zs), Face(ParteSecao.LateralAlmaDireita, yb, hi, yb, zs),
            Regiao(ParteSecao.MesaSuperior, ya - ase, zs, yb + asd, h), Regiao(ParteSecao.MesaInferior, ya - aie, 0, yb + aid, hi),
            Regiao(ParteSecao.Alma, ya, hi, yb, zs),
        };
    }
}

/// <summary>Trapézio simétrico: base inferior bi, base superior bs, altura h. Esquerda e direita são as faces inclinadas.</summary>
internal sealed class FamiliaTrapezoidal : FamiliaSecao
{
    public override string Id => "trapezoidal";
    public override string Nome => "Trapezoidal";
    public override IReadOnlyList<ParametroSecao> Parametros { get; } = new[]
    {
        Cm("bi", "largura no fundo"), Cm("bs", "largura no topo"), Cm("h", "altura"),
    };

    internal override string Validar(Func<string, double> v) => Positivos(v, "bi", "bs", "h");

    internal override List<(double Y, double Z)> ContornoCanonico(Func<string, double> v)
    {
        double bi = v("bi"), bs = v("bs"), h = v("h"), b = Math.Max(bi, bs);
        return new List<(double, double)> { ((b - bi) / 2, 0), ((b + bi) / 2, 0), ((b + bs) / 2, h), ((b - bs) / 2, h) };
    }

    internal override List<ParteNomeada> PartesCanonicas(Func<string, double> v)
    {
        double bi = v("bi"), bs = v("bs"), h = v("h"), b = Math.Max(bi, bs);
        return new List<ParteNomeada>
        {
            Face(ParteSecao.Topo, (b - bs) / 2, h, (b + bs) / 2, h), Face(ParteSecao.Fundo, (b - bi) / 2, 0, (b + bi) / 2, 0),
            Face(ParteSecao.Esquerda, (b - bi) / 2, 0, (b - bs) / 2, h), Face(ParteSecao.Direita, (b + bi) / 2, 0, (b + bs) / 2, h),
        };
    }
}

/// <summary>Seção medida: sem parâmetros nem partes nomeadas; o contorno é o polígono guardado na seção.</summary>
internal sealed class FamiliaPoligonal : FamiliaSecao
{
    public override string Id => "poligonal";
    public override string Nome => "Poligonal (medida)";
    public override IReadOnlyList<ParametroSecao> Parametros { get; } = new ParametroSecao[0];
    internal override string Validar(Func<string, double> v) => null;
    internal override List<(double Y, double Z)> ContornoCanonico(Func<string, double> v) => new List<(double, double)>();
    internal override List<ParteNomeada> PartesCanonicas(Func<string, double> v) => new List<ParteNomeada>();
}
