using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace DEB.Core.Analise.Mef;

/// <summary>
/// Chapa em estado plano de tensões (x horizontal, z vertical) para o CalculiX: quadriláteros de 8 nós (CPS8) e triângulos de
/// 6 nós (CPS6), espessura = largura da faixa. Os nós são criados por coordenada (<see cref="No"/>), sem repetir: malhas
/// encostadas ficam conformes. Cortes (<see cref="Cortes"/>) = faces de elementos onde o CalculiX integra as tensões
/// (*SECTION PRINT): força normal, cortante e momento no corte. Unidades: cm, kN.
/// </summary>
public sealed class Chapa2D
{
    public List<(double X, double Z)> Nos { get; } = new List<(double, double)>();
    private readonly Dictionary<(long, long), int> _indice = new Dictionary<(long, long), int>();
    /// <summary>Elementos: nós (8 = CPS8, 6 = CPS6), em ordem anti-horária (cantos, depois os do meio dos lados).</summary>
    public List<int[]> Elementos { get; } = new List<int[]>();
    public double E { get; set; }
    public double Nu { get; set; } = 0.2;
    public double Espessura { get; set; } = 100;
    public List<(int No, bool Ux, bool Uz)> Apoios { get; } = new List<(int, bool, bool)>();
    public Dictionary<string, List<(int No, double Fx, double Fz)>> Casos { get; } = new Dictionary<string, List<(int, double, double)>>();
    /// <summary>Cortes: nome e faces (elemento, face 1…4). A normal da face aponta no sentido em que o corte "olha" (a parte de trás é a dos elementos).</summary>
    public List<(string Nome, List<(int Elem, int Face)> Faces)> Cortes { get; } = new List<(string, List<(int, int)>)>();
    /// <summary>Nós cujo deslocamento é lido (o eixo, para a deformada).</summary>
    public List<int> NosLidos { get; } = new List<int>();

    /// <summary>Índice do nó nessa coordenada (cria se não existir; tolerância 1e-4 cm).</summary>
    public int No(double x, double z)
    {
        var k = ((long)Math.Round(x * 1e4), (long)Math.Round(z * 1e4));
        if (_indice.TryGetValue(k, out int i)) return i;
        Nos.Add((x, z)); _indice[k] = Nos.Count - 1;
        return Nos.Count - 1;
    }

    /// <summary>Quadrilátero de 8 nós pelos 4 cantos (anti-horário): os do meio dos lados no meio do segmento.</summary>
    public int Quad(int a, int b, int c, int d)
    {
        int M(int p, int q) => No((Nos[p].X + Nos[q].X) / 2, (Nos[p].Z + Nos[q].Z) / 2);
        Elementos.Add(new[] { a, b, c, d, M(a, b), M(b, c), M(c, d), M(d, a) });
        return Elementos.Count - 1;
    }

    public int Tri(int a, int b, int c)
    {
        int M(int p, int q) => No((Nos[p].X + Nos[q].X) / 2, (Nos[p].Z + Nos[q].Z) / 2);
        Elementos.Add(new[] { a, b, c, M(a, b), M(b, c), M(c, a) });
        return Elementos.Count - 1;
    }

    public double AreaElemento(int e)
    {
        var n = Elementos[e]; int k = n.Length == 8 ? 4 : 3; double a = 0;
        for (int i = 0; i < k; i++) { var p = Nos[n[i]]; var q = Nos[n[(i + 1) % k]]; a += p.X * q.Z - q.X * p.Z; }
        return a / 2;
    }

    public string EntradaCalculix(string caso)
    {
        var ci = CultureInfo.InvariantCulture;
        string F(double v) => v.ToString("R", ci);
        var sb = new StringBuilder();
        sb.AppendLine("** Gerado pelo PCEB (DEB.Core/Analise/Mef/Chapa2D) — conferência por MEF");
        sb.AppendLine("*NODE");
        for (int i = 0; i < Nos.Count; i++) sb.AppendLine($"{i + 1},{F(Nos[i].X)},{F(Nos[i].Z)},0");
        foreach (int tipo in new[] { 8, 6 })
        {
            var els = Elementos.Select((n, i) => (n, i)).Where(t => t.n.Length == tipo).ToList();
            if (els.Count == 0) continue;
            sb.AppendLine($"*ELEMENT,TYPE={(tipo == 8 ? "CPS8" : "CPS6")},ELSET=CHAPA");
            foreach (var (n, i) in els) sb.AppendLine($"{i + 1}," + string.Join(",", n.Select(x => x + 1)));
        }
        foreach (var (nome, faces) in Cortes)
        {
            sb.AppendLine($"*SURFACE,NAME={nome}");
            foreach (var (e, f) in faces) sb.AppendLine($"{e + 1},S{f}");
        }
        sb.AppendLine("*NSET,NSET=APOIOS");
        foreach (var a in Apoios) sb.AppendLine((a.No + 1).ToString());
        if (NosLidos.Count > 0)
        {
            sb.AppendLine("*NSET,NSET=EIXO");
            foreach (int n in NosLidos) sb.AppendLine((n + 1).ToString());
        }
        sb.AppendLine("*MATERIAL,NAME=CONCRETO");
        sb.AppendLine("*ELASTIC");
        sb.AppendLine($"{F(E)},{F(Nu)}");
        sb.AppendLine("*SOLID SECTION,ELSET=CHAPA,MATERIAL=CONCRETO");
        sb.AppendLine(F(Espessura));
        sb.AppendLine("*BOUNDARY");
        foreach (var a in Apoios)
        {
            if (a.Ux) sb.AppendLine($"{a.No + 1},1,1");
            if (a.Uz) sb.AppendLine($"{a.No + 1},2,2");
        }
        sb.AppendLine("*STEP");
        sb.AppendLine("*STATIC");
        sb.AppendLine("*CLOAD");
        foreach (var (n, fx, fz) in Casos[caso])
        {
            if (Math.Abs(fx) > 0) sb.AppendLine($"{n + 1},1,{F(fx)}");
            if (Math.Abs(fz) > 0) sb.AppendLine($"{n + 1},2,{F(fz)}");
        }
        if (NosLidos.Count > 0) { sb.AppendLine("*NODE PRINT,NSET=EIXO"); sb.AppendLine("U"); }
        sb.AppendLine("*NODE PRINT,NSET=APOIOS");
        sb.AppendLine("RF");
        int k = 0;
        foreach (var (nome, _) in Cortes) { sb.AppendLine($"*SECTION PRINT,SURFACE={nome},NAME=SP{++k}"); sb.AppendLine("SOF,SOM"); }
        sb.AppendLine("*END STEP");
        return sb.ToString();
    }

    /// <summary>Roda um caso no CalculiX e lê deslocamentos, reações e os cortes.</summary>
    public ResultadoChapa2D RodarCalculix(string caso, FerramentasMef ferr = null, string pasta = null)
    {
        ferr ??= FerramentasMef.Padrao;
        var r = new ResultadoChapa2D();
        if (!ferr.TemCalculix) { r.Erro = "CalculiX indisponível: falta o ccx (variável PCEB_CCX ou pasta programas/calculix)."; return r; }
        pasta ??= FerramentasMef.NovaPasta("calculix");
        File.WriteAllText(Path.Combine(pasta, "chapa.inp"), EntradaCalculix(caso), new UTF8Encoding(false));
        var (cod, saida) = ferr.Rodar(ferr.Ccx, "chapa", pasta);
        r.Pasta = pasta; r.Log = saida;
        string dat = Path.Combine(pasta, "chapa.dat");
        if (cod != 0 || !File.Exists(dat) || new FileInfo(dat).Length == 0)
        {
            r.Erro = $"CalculiX terminou com código {cod}: " + string.Join(" | ", saida.Split('\n').Where(l => l.Contains("ERROR")).Take(3));
            return r;
        }
        LerDat(File.ReadAllLines(dat), r, Cortes.Select(c => c.Nome).ToList());
        return r;
    }

    /// <summary>Lê o .dat do CalculiX: blocos "displacements", "forces" e "statistics for surface set".</summary>
    public static void LerDat(string[] linhas, ResultadoChapa2D r, List<string> cortes)
    {
        var ci = CultureInfo.InvariantCulture;
        string bloco = ""; string corte = null; int sub = 0;
        double[] Nums(string l) => l.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).Select(x => double.Parse(x, NumberStyles.Float, ci)).ToArray();
        for (int i = 0; i < linhas.Length; i++)
        {
            string l = linhas[i].Trim();
            if (l.Length == 0) continue;
            if (l.StartsWith("displacements")) { bloco = "U"; continue; }
            if (l.StartsWith("forces")) { bloco = "RF"; continue; }
            if (l.StartsWith("statistics for surface set"))
            {
                bloco = "S"; sub = 0;
                string nome = l.Substring("statistics for surface set".Length).Trim().Split(' ')[0];
                corte = cortes.FirstOrDefault(c => c.Equals(nome, StringComparison.OrdinalIgnoreCase)) ?? nome;
                r.Cortes[corte] = new CorteChapa();
                continue;
            }
            if (bloco == "S")
            {
                if (l.StartsWith("total surface force")) { sub = 1; continue; }
                if (l.StartsWith("center of gravity")) { sub = 2; continue; }
                if (l.StartsWith("moment about the center")) { sub = 3; continue; }
                if (l.StartsWith("area,")) { sub = 4; continue; }
                double[] v;
                try { v = Nums(l); } catch (FormatException) { continue; }
                var c = r.Cortes[corte];
                if (sub == 1 && v.Length >= 3) { c.Fx = v[0]; c.Fz = v[1]; }
                else if (sub == 2 && v.Length >= 6) { c.Xg = v[0]; c.Zg = v[1]; c.Nx = v[3]; c.Nz = v[4]; }
                else if (sub == 3 && v.Length >= 3) c.Mg = v[2];
                else if (sub == 4 && v.Length >= 1) c.Area = v[0];
                continue;
            }
            if (bloco == "U" || bloco == "RF")
            {
                double[] v;
                try { v = Nums(l); } catch (FormatException) { continue; }
                if (v.Length < 3) continue;
                int n = (int)v[0] - 1;
                if (bloco == "U") r.Deslocamentos[n] = (v[1], v[2]); else r.Reacoes[n] = (v[1], v[2]);
            }
        }
        r.Completo = true;
    }
}

/// <summary>Esforços num corte da chapa: força e momento que a parte da frente (sentido da normal) faz sobre a de trás.</summary>
public sealed class CorteChapa
{
    public double Fx { get; set; }
    public double Fz { get; set; }
    public double Xg { get; set; }
    public double Zg { get; set; }
    /// <summary>Normal média do corte (sai da parte dos elementos).</summary>
    public double Nx { get; set; }
    public double Nz { get; set; }
    public double Mg { get; set; }
    public double Area { get; set; }
    /// <summary>Normal (tração +): força ao longo da normal do corte.</summary>
    public double N => Fx * Nx + Fz * Nz;
    /// <summary>Cortante (convenção de viga: positiva quando a parte da frente puxa a de trás para baixo do eixo): −F·t, t = normal girada +90°.</summary>
    public double V => -(Fx * -Nz + Fz * Nx);
    /// <summary>Momento (tração embaixo +), em relação ao centro do corte.</summary>
    public double M => Mg;
}

public sealed class ResultadoChapa2D
{
    public Dictionary<int, (double Ux, double Uz)> Deslocamentos { get; } = new Dictionary<int, (double, double)>();
    public Dictionary<int, (double Fx, double Fz)> Reacoes { get; } = new Dictionary<int, (double, double)>();
    public Dictionary<string, CorteChapa> Cortes { get; } = new Dictionary<string, CorteChapa>();
    public string Erro { get; set; }
    public string Pasta { get; set; }
    public string Log { get; set; }
    public bool Completo { get; set; }
    public bool Ok => Erro == null && Completo;
}
