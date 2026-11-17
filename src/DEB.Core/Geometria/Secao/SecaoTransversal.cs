using System;
using System.Collections.Generic;
using System.Linq;
using DEB.Core.Pecas;

namespace DEB.Core.Geometria.Secao;

/// <summary>
/// Parte nomeada da seção (D28): é o que consolo, dente, furo e enrijecedor referenciam, em vez de número de face ou
/// coordenada. Os nomes valem na posição real (depois da orientação): "Topo" é sempre a face de cima. Os valores são fixos
/// (identidade estável): não renumerar nem reaproveitar.
/// </summary>
public enum ParteSecao
{
    // faces da caixa da seção
    Topo = 1, Fundo = 2, Esquerda = 3, Direita = 4,
    // faces da alma que não são faces da caixa (T, L, I)
    LateralAlmaEsquerda = 5, LateralAlmaDireita = 6,
    // regiões
    Alma = 10, Mesa = 11, MesaSuperior = 12, MesaInferior = 13, Aba = 14,
}

public enum TipoParte { Face, Regiao }

/// <summary>Uma parte nomeada com a sua geometria no plano da seção (cm): face = segmento; região = retângulo.</summary>
public sealed class ParteNomeada
{
    private ParteNomeada() { }
    public ParteSecao Parte { get; private set; }
    public TipoParte Tipo { get; private set; }
    /// <summary>Face: os dois pontos do segmento.</summary>
    public (double Y, double Z) Inicio { get; private set; }
    public (double Y, double Z) Fim { get; private set; }
    /// <summary>Região: o retângulo que ela ocupa (null nas faces).</summary>
    public FaixaSecao Regiao { get; private set; }

    internal static ParteNomeada Face(ParteSecao p, (double Y, double Z) a, (double Y, double Z) b) =>
        new ParteNomeada { Parte = p, Tipo = TipoParte.Face, Inicio = a, Fim = b };
    internal static ParteNomeada DaRegiao(ParteSecao p, FaixaSecao r) => new ParteNomeada { Parte = p, Tipo = TipoParte.Regiao, Regiao = r };

    /// <summary>A mesma parte espelhada (na caixa b × h) e com o nome trocado de lado.</summary>
    internal ParteNomeada Espelhada(OrientacaoSecao o, double b, double h)
    {
        (double, double) P((double Y, double Z) q) => (o.EspelhoHorizontal ? b - q.Y : q.Y, o.EspelhoVertical ? h - q.Z : q.Z);
        var r = new ParteNomeada { Parte = o.Renomear(Parte), Tipo = Tipo, Inicio = P(Inicio), Fim = P(Fim) };
        if (Regiao != null)
            r.Regiao = new FaixaSecao
            {
                Y0 = o.EspelhoHorizontal ? b - Regiao.Y1 : Regiao.Y0, Y1 = o.EspelhoHorizontal ? b - Regiao.Y0 : Regiao.Y1,
                Z0 = o.EspelhoVertical ? h - Regiao.Z1 : Regiao.Z0, Z1 = o.EspelhoVertical ? h - Regiao.Z0 : Regiao.Z1,
            };
        return r;
    }
}

/// <summary>
/// Orientação da seção em relação à posição canônica da família: espelho horizontal (troca esquerda e direita) e espelho
/// vertical (troca topo e fundo; "invertido"); os dois juntos = giro de 180°. Giro de 90° não entra: numa seção de pilar
/// isso é trocar b e h.
/// </summary>
public readonly struct OrientacaoSecao : IEquatable<OrientacaoSecao>
{
    public OrientacaoSecao(bool espelhoHorizontal, bool espelhoVertical) { EspelhoHorizontal = espelhoHorizontal; EspelhoVertical = espelhoVertical; }
    public bool EspelhoHorizontal { get; }
    public bool EspelhoVertical { get; }

    public static OrientacaoSecao Canonica => default;
    /// <summary>Espelho vertical (T invertido, L com aba em cima).</summary>
    public static OrientacaoSecao Invertida => new OrientacaoSecao(false, true);

    internal ParteSecao Renomear(ParteSecao p)
    {
        if (EspelhoHorizontal)
            p = p == ParteSecao.Esquerda ? ParteSecao.Direita : p == ParteSecao.Direita ? ParteSecao.Esquerda
              : p == ParteSecao.LateralAlmaEsquerda ? ParteSecao.LateralAlmaDireita : p == ParteSecao.LateralAlmaDireita ? ParteSecao.LateralAlmaEsquerda : p;
        if (EspelhoVertical)
            p = p == ParteSecao.Topo ? ParteSecao.Fundo : p == ParteSecao.Fundo ? ParteSecao.Topo
              : p == ParteSecao.MesaSuperior ? ParteSecao.MesaInferior : p == ParteSecao.MesaInferior ? ParteSecao.MesaSuperior : p;
        return p;
    }

    public bool Equals(OrientacaoSecao o) => o.EspelhoHorizontal == EspelhoHorizontal && o.EspelhoVertical == EspelhoVertical;
    public override bool Equals(object obj) => obj is OrientacaoSecao o && Equals(o);
    public override int GetHashCode() => (EspelhoHorizontal ? 1 : 0) + (EspelhoVertical ? 2 : 0);

    public override string ToString() =>
        EspelhoHorizontal && EspelhoVertical ? "girada 180°" : EspelhoVertical ? "invertida" : EspelhoHorizontal ? "espelhada" : "canônica";
}

/// <summary>De onde vem a seção: de uma família com parâmetros, ou só do polígono medido.</summary>
public enum OrigemSecao { Familia, Medida }

/// <summary>
/// Seção transversal de elemento linear (D28, etapa 5 da O-2): família + parâmetros + orientação, ou o polígono medido
/// (família "poligonal") para o que não casa com nenhuma família. O contorno sai da família (<see cref="Contorno"/>, origem
/// no canto inferior esquerdo da caixa); área, CG e inércias saem desse contorno (<see cref="SecaoPoligonal"/>).
/// Imutável. Ver docs/04_arquitetura/secoes.md.
/// </summary>
public sealed class SecaoTransversal
{
    private readonly Dictionary<string, double> _valores;
    private readonly SecaoPoligonal _medida;

    private SecaoTransversal(FamiliaSecao familia, Dictionary<string, double> valores, OrientacaoSecao orientacao, SecaoPoligonal medida)
    {
        Familia = familia; _valores = valores; Orientacao = orientacao; _medida = medida;
    }

    public FamiliaSecao Familia { get; }
    public OrientacaoSecao Orientacao { get; }
    public OrigemSecao Origem => _medida != null ? OrigemSecao.Medida : OrigemSecao.Familia;
    /// <summary>Valores dos parâmetros da família (cm), pelo nome; vazio na seção medida.</summary>
    public IReadOnlyDictionary<string, double> Valores => _valores;

    /// <summary>Valor de um parâmetro; exceção se a família não o tem.</summary>
    public double Valor(string nome) =>
        _valores.TryGetValue(nome, out var x) ? x : throw new KeyNotFoundException($"A família {Familia.Nome} não tem o parâmetro \"{nome}\".");

    /// <summary>Seção de uma família. Exceção (<see cref="ArgumentException"/>) se falta parâmetro ou os valores não formam a seção.</summary>
    public static SecaoTransversal DaFamilia(FamiliaSecao familia, IReadOnlyDictionary<string, double> valores, OrientacaoSecao orientacao = default)
    {
        if (familia == null) throw new ArgumentNullException(nameof(familia));
        if (familia == FamiliasSecao.Poligonal) throw new ArgumentException("A seção poligonal vem do polígono medido: use DaMedida.");
        var v = new Dictionary<string, double>();
        foreach (var p in familia.Parametros)
        {
            if (valores == null || !valores.TryGetValue(p.Nome, out var x)) throw new ArgumentException($"Seção {familia.Nome}: falta o parâmetro \"{p.Nome}\" ({p.Descricao}).");
            v[p.Nome] = x;
        }
        string erro = familia.Validar(n => v[n]);
        if (erro != null) throw new ArgumentException($"Seção {familia.Nome}: {erro}");
        return new SecaoTransversal(familia, v, orientacao, null);
    }

    /// <summary>Seção cuja fonte é só o polígono medido (família "poligonal"). O polígono é copiado.</summary>
    public static SecaoTransversal DaMedida(SecaoPoligonal medida)
    {
        if (medida == null || medida.Contorno.Count < 3) throw new ArgumentException("Seção medida: o contorno precisa de 3 pontos ou mais.");
        return new SecaoTransversal(FamiliasSecao.Poligonal, new Dictionary<string, double>(), default, Copia(medida));
    }

    // atalhos (cm)
    public static SecaoTransversal Retangular(double b, double h) =>
        DaFamilia(FamiliasSecao.Retangular, new Dictionary<string, double> { ["b"] = b, ["h"] = h });
    public static SecaoTransversal T(double bw, double h, double hf, double ae, double ad, bool invertida = false) =>
        DaFamilia(FamiliasSecao.T, new Dictionary<string, double> { ["bw"] = bw, ["h"] = h, ["hf"] = hf, ["ae"] = ae, ["ad"] = ad },
                  invertida ? OrientacaoSecao.Invertida : default);
    public static SecaoTransversal L(double bw, double h, double hf, double ba, OrientacaoSecao orientacao = default) =>
        DaFamilia(FamiliasSecao.L, new Dictionary<string, double> { ["bw"] = bw, ["h"] = h, ["hf"] = hf, ["ba"] = ba }, orientacao);
    public static SecaoTransversal I(double bw, double h, double hs, double ase, double asd, double hi, double aie, double aid) =>
        DaFamilia(FamiliasSecao.I, new Dictionary<string, double>
        {
            ["bw"] = bw, ["h"] = h, ["hs"] = hs, ["ase"] = ase, ["asd"] = asd, ["hi"] = hi, ["aie"] = aie, ["aid"] = aid,
        });
    public static SecaoTransversal Trapezoidal(double bi, double bs, double h) =>
        DaFamilia(FamiliasSecao.Trapezoidal, new Dictionary<string, double> { ["bi"] = bi, ["bs"] = bs, ["h"] = h });

    /// <summary>
    /// Contorno da seção (nova instância a cada chamada): o da família, já orientado, com origem no canto inferior esquerdo
    /// da caixa; na seção medida, uma cópia do polígono como foi medido.
    /// </summary>
    public SecaoPoligonal Contorno()
    {
        if (_medida != null) return Copia(_medida);
        var c = Familia.ContornoCanonico(n => _valores[n]);
        double b = c.Max(p => p.Y), h = c.Max(p => p.Z);
        var o = Orientacao;
        var r = c.Select(p => (o.EspelhoHorizontal ? b - p.Y : p.Y, o.EspelhoVertical ? h - p.Z : p.Z)).ToList();
        if (o.EspelhoHorizontal != o.EspelhoVertical) r.Reverse();   // um espelho só inverte o sentido: volta ao anti-horário
        return new SecaoPoligonal { Contorno = r };
    }

    /// <summary>Partes nomeadas na posição real (vazio na seção medida).</summary>
    public IReadOnlyList<ParteNomeada> Partes()
    {
        if (_medida != null) return new ParteNomeada[0];
        var c = Familia.ContornoCanonico(n => _valores[n]);
        double b = c.Max(p => p.Y), h = c.Max(p => p.Z);
        return Familia.PartesCanonicas(n => _valores[n]).Select(p => p.Espelhada(Orientacao, b, h)).OrderBy(p => p.Parte).ToList();
    }

    /// <summary>Uma parte pelo nome; null se a seção não a tem.</summary>
    public ParteNomeada Parte(ParteSecao p) => Partes().FirstOrDefault(x => x.Parte == p);

    // propriedades: do contorno
    public double B => Contorno().B;
    public double H => Contorno().H;
    public double Area => Contorno().Area;
    public (double Y, double Z) CG => Contorno().CG;
    public double Iy => Contorno().Iy;
    public double Iz => Contorno().Iz;

    /// <summary>"T: bw = 20, h = 60, hf = 15, ae = 20, ad = 20 (invertida)" — mensagens e conferência.</summary>
    public string Descricao
    {
        get
        {
            if (_medida != null) return $"{Familia.Nome} {Fmt.N(_medida.B)} × {Fmt.N(_medida.H)} (A = {Fmt.N(_medida.Area)} cm²)";
            string s = Familia.Nome + ": " + string.Join(", ", Familia.Parametros.Select(p => $"{p.Nome} = {Fmt.N(_valores[p.Nome])}"));
            return Orientacao.Equals(default(OrientacaoSecao)) ? s : $"{s} ({Orientacao})";
        }
    }

    public override string ToString() => Descricao;

    private static SecaoPoligonal Copia(SecaoPoligonal s) => new SecaoPoligonal
    {
        Contorno = s.Contorno.ToList(),
        Furos = s.Furos.Select(f => f.ToList()).ToList(),
    };
}
