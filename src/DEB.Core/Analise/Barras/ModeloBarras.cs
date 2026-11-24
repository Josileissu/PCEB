using System;
using System.Collections.Generic;
using System.Linq;

namespace DEB.Core.Analise.Barras;

// =====================================================================================================================
// Modelo de barras da análise por objetos (D46). Unidades: cm, kN, rad (kN/cm² para E e G). Eixos globais: X e Y horizontais,
// Z vertical para cima. Cada parte (nó, barra, mola, vínculo) guarda o DONO — a peça ou a ligação que a gerou — para os
// esforços voltarem a ela.
// =====================================================================================================================

/// <summary>Que tipo de objeto gerou a parte do modelo.</summary>
public enum TipoDono { Pilar, Viga, Ligacao, Laje, Escada, Fundacao, Outro }

/// <summary>Dono de uma parte do modelo: a peça (pilar, viga…) ou a ligação; <see cref="Id"/> é o do modelo de origem (Revit, CAD).</summary>
public sealed class Dono
{
    public TipoDono Tipo { get; set; }
    public string Id { get; set; } = "";
    /// <summary>Marca/nome para o relatório ("P3", "V12", "consolo P3 face 1").</summary>
    public string Nome { get; set; } = "";
    public override string ToString() => Nome != "" ? Nome : $"{Tipo} {Id}";
}

/// <summary>Graus de liberdade de um nó: translações e rotações em X, Y, Z.</summary>
public enum Gdl { Ux = 0, Uy = 1, Uz = 2, Rx = 3, Ry = 4, Rz = 5 }

public sealed class NoBarra
{
    public int Id { get; internal set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public Dono Dono { get; set; }
    public string Nome { get; set; } = "";
    public Vetor3 Ponto => new Vetor3(X, Y, Z);
}

/// <summary>Propriedades de rigidez da barra (cm, cm², cm⁴) no sistema local: y e z são os eixos da seção.</summary>
public sealed class SecaoBarra
{
    public double A { get; set; }
    /// <summary>Inércia em torno do eixo local y (flexão no plano x-z).</summary>
    public double Iy { get; set; }
    /// <summary>Inércia em torno do eixo local z (flexão no plano x-y).</summary>
    public double Iz { get; set; }
    /// <summary>Constante de torção (Saint-Venant).</summary>
    public double J { get; set; }
    public string Descricao { get; set; } = "";
}

/// <summary>Material elástico: E e G em kN/cm²; peso específico em kN/cm³ (peso próprio automático).</summary>
public sealed class MaterialBarra
{
    public double E { get; set; }
    public double G { get; set; }
    public double Peso { get; set; }
    public string Descricao { get; set; } = "";

    /// <summary>Concreto da NBR 6118 8.2.8: Ecs = αi·αE·5600·√fck (MPa), G = Ecs/2,4; peso 25 kN/m³.</summary>
    public static MaterialBarra Concreto(double fckMPa, double alfaE = 1.0, double pesoKNm3 = 25)
    {
        double eci = alfaE * 5600 * Math.Sqrt(fckMPa);
        double alfaI = Math.Min(1, 0.8 + 0.2 * fckMPa / 80);
        double ecs = alfaI * eci / 10;   // kN/cm²
        return new MaterialBarra { E = ecs, G = ecs / 2.4, Peso = pesoKNm3 / 1e6, Descricao = $"C{fckMPa:0} (Ecs = {ecs * 10:0} MPa)" };
    }
}

/// <summary>Barra de pórtico espacial entre dois nós.</summary>
public sealed class BarraPortico
{
    public int Id { get; internal set; }
    public int NoI { get; set; }
    public int NoJ { get; set; }
    public SecaoBarra Secao { get; set; }
    public MaterialBarra Material { get; set; }
    /// <summary>Vetor no plano local x-z (como o vecxz do OpenSees). Nulo = Z global (barra não vertical) ou X global (vertical).</summary>
    public Vetor3? VetorXZ { get; set; }
    /// <summary>Graus liberados (rótulas) em cada ponta, no sistema LOCAL da barra (índices de <see cref="Gdl"/>).</summary>
    public bool[] LiberaI { get; set; } = new bool[6];
    public bool[] LiberaJ { get; set; } = new bool[6];
    /// <summary>Multiplica a rigidez à flexão (NBR 6118 15.7.3: 0,8 pilar, 0,4 viga… na 2ª ordem global). 1 = elástica.</summary>
    public double FatorFlexao { get; set; } = 1;
    /// <summary>Multiplica a rigidez à torção (concreto fissurado: 0,15 da elástica é usual; 1 = elástica).</summary>
    public double FatorTorcao { get; set; } = 1;
    public Dono Dono { get; set; }
    public string Nome { get; set; } = "";
}

/// <summary>
/// Mola entre dois nós (elemento de comprimento zero): rigidez por grau nos eixos da mola (kN/cm e kN·cm/rad). Eixos: x' =
/// <see cref="EixoX"/>, z' no plano de x' com <see cref="VetorXZ"/>; nulos = eixos globais. Aparelho de apoio, ligação semirrígida.
/// </summary>
public sealed class MolaLigacao
{
    public int Id { get; internal set; }
    public int NoI { get; set; }
    public int NoJ { get; set; }
    public double[] K { get; set; } = new double[6];
    public Vetor3? EixoX { get; set; }
    public Vetor3? VetorXZ { get; set; }
    public Dono Dono { get; set; }
    public string Nome { get; set; } = "";
}

/// <summary>
/// Vínculo de corpo rígido: o nó escravo segue o mestre (u_e = u_m + θ_m × r; θ_e = θ_m) nos graus marcados. Barra rígida do
/// consolo (todos os graus) e diafragma do pavimento (Ux, Uy, Rz).
/// </summary>
public sealed class VinculoRigido
{
    public int Mestre { get; set; }
    public int Escravo { get; set; }
    public bool[] Graus { get; set; } = { true, true, true, true, true, true };
    public Dono Dono { get; set; }
    public string Nome { get; set; } = "";
}

/// <summary>Apoio: graus travados e molas (kN/cm, kN·cm/rad) nos eixos globais.</summary>
public sealed class ApoioNo
{
    public int No { get; set; }
    public bool[] Travado { get; set; } = new bool[6];
    public double[] Mola { get; set; } = new double[6];
    public Dono Dono { get; set; }
    public static bool[] Engaste => new[] { true, true, true, true, true, true };
    public static bool[] Rotula => new[] { true, true, true, false, false, false };
}

/// <summary>Natureza do caso de carga, para as combinações (NBR 8681).</summary>
public enum NaturezaCaso { PesoProprio, Permanente, Variavel, Vento, Excepcional }

/// <summary>Carga distribuída uniforme numa barra, em kN/cm, de <see cref="A"/> a <see cref="B"/> (cm a partir do nó I; B ≤ 0 = até o fim).</summary>
public sealed class CargaDistribuida
{
    public int Barra { get; set; }
    public Vetor3 Q { get; set; }
    public bool Local { get; set; }
    public double A { get; set; }
    public double B { get; set; }
}

/// <summary>Força concentrada numa barra a <see cref="A"/> cm do nó I (global ou local).</summary>
public sealed class CargaPontualBarra
{
    public int Barra { get; set; }
    public Vetor3 F { get; set; }
    public bool Local { get; set; }
    public double A { get; set; }
}

public sealed class CasoCarga
{
    public string Nome { get; set; } = "";
    public NaturezaCaso Natureza { get; set; }
    /// <summary>Peso próprio das barras pelo peso específico do material (−Z).</summary>
    public bool PesoProprio { get; set; }
    /// <summary>Forças e momentos nodais (globais): Fx, Fy, Fz, Mx, My, Mz.</summary>
    public List<(int No, double[] F)> Nodais { get; } = new List<(int, double[])>();
    public List<CargaDistribuida> Distribuidas { get; } = new List<CargaDistribuida>();
    public List<CargaPontualBarra> Pontuais { get; } = new List<CargaPontualBarra>();
}

/// <summary>Modelo de barras: o que os objetos (peças e ligações) geraram.</summary>
public sealed class ModeloBarras
{
    public List<NoBarra> Nos { get; } = new List<NoBarra>();
    public List<BarraPortico> Barras { get; } = new List<BarraPortico>();
    public List<MolaLigacao> Molas { get; } = new List<MolaLigacao>();
    public List<VinculoRigido> Vinculos { get; } = new List<VinculoRigido>();
    public List<ApoioNo> Apoios { get; } = new List<ApoioNo>();
    public List<CasoCarga> Casos { get; } = new List<CasoCarga>();

    /// <summary>Tolerância para juntar nós na mesma coordenada (cm).</summary>
    public double Tolerancia { get; set; } = 0.05;

    /// <summary>Nó na coordenada (reaproveita um existente a menos da tolerância).</summary>
    public int No(double x, double y, double z, Dono dono = null, string nome = "")
    {
        foreach (var n in Nos)
            if (Math.Abs(n.X - x) <= Tolerancia && Math.Abs(n.Y - y) <= Tolerancia && Math.Abs(n.Z - z) <= Tolerancia) return n.Id;
        return NovoNo(x, y, z, dono, nome);
    }

    /// <summary>Nó novo mesmo que já exista outro no lugar (as duas pontas de uma mola, por exemplo).</summary>
    public int NovoNo(double x, double y, double z, Dono dono = null, string nome = "")
    {
        var n = new NoBarra { Id = Nos.Count, X = x, Y = y, Z = z, Dono = dono, Nome = nome };
        Nos.Add(n);
        return n.Id;
    }

    public BarraPortico Barra(int i, int j, SecaoBarra s, MaterialBarra m, Dono dono = null, string nome = "")
    {
        var b = new BarraPortico { Id = Barras.Count, NoI = i, NoJ = j, Secao = s, Material = m, Dono = dono, Nome = nome };
        Barras.Add(b);
        return b;
    }

    public MolaLigacao Mola(int i, int j, double[] k, Dono dono = null, string nome = "")
    {
        var m = new MolaLigacao { Id = Molas.Count, NoI = i, NoJ = j, K = k, Dono = dono, Nome = nome };
        Molas.Add(m);
        return m;
    }

    public VinculoRigido Rigido(int mestre, int escravo, Dono dono = null, string nome = "", bool[] graus = null)
    {
        var v = new VinculoRigido { Mestre = mestre, Escravo = escravo, Dono = dono, Nome = nome };
        if (graus != null) v.Graus = graus;
        Vinculos.Add(v);
        return v;
    }

    public ApoioNo Apoio(int no, bool[] travado, Dono dono = null)
    {
        var a = new ApoioNo { No = no, Travado = travado, Dono = dono };
        Apoios.Add(a);
        return a;
    }

    public CasoCarga Caso(string nome, NaturezaCaso natureza = NaturezaCaso.Permanente)
    {
        var c = Casos.FirstOrDefault(x => x.Nome == nome);
        if (c != null) return c;
        c = new CasoCarga { Nome = nome, Natureza = natureza };
        Casos.Add(c);
        return c;
    }

    public double Comprimento(BarraPortico b) => (Nos[b.NoJ].Ponto - Nos[b.NoI].Ponto).Modulo;
}

/// <summary>Vetor 3D simples (cm ou adimensional).</summary>
public readonly struct Vetor3
{
    public double X { get; }
    public double Y { get; }
    public double Z { get; }
    public Vetor3(double x, double y, double z) { X = x; Y = y; Z = z; }
    public static readonly Vetor3 EixoX = new Vetor3(1, 0, 0), EixoY = new Vetor3(0, 1, 0), EixoZ = new Vetor3(0, 0, 1), Zero = new Vetor3(0, 0, 0);
    public double Modulo => Math.Sqrt(X * X + Y * Y + Z * Z);
    public Vetor3 Unitario { get { double m = Modulo; return m < 1e-12 ? Zero : new Vetor3(X / m, Y / m, Z / m); } }
    public static Vetor3 operator +(Vetor3 a, Vetor3 b) => new Vetor3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vetor3 operator -(Vetor3 a, Vetor3 b) => new Vetor3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vetor3 operator -(Vetor3 a) => new Vetor3(-a.X, -a.Y, -a.Z);
    public static Vetor3 operator *(Vetor3 a, double k) => new Vetor3(a.X * k, a.Y * k, a.Z * k);
    public static Vetor3 operator *(double k, Vetor3 a) => a * k;
    public double Ponto(Vetor3 b) => X * b.X + Y * b.Y + Z * b.Z;
    public Vetor3 Vetorial(Vetor3 b) => new Vetor3(Y * b.Z - Z * b.Y, Z * b.X - X * b.Z, X * b.Y - Y * b.X);
    public override string ToString() => $"({X:0.###}; {Y:0.###}; {Z:0.###})";
}
