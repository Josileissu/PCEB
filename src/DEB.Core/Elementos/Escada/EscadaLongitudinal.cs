using System;
using System.Collections.Generic;
using System.Linq;

namespace DEB.Core.Elementos.Escada;

/// <summary>
/// Escada em laje armada longitudinalmente (D42, 1º tipo): um lance reto entre dois apoios (vigas ou lajes), com patamar
/// inferior e superior opcionais. Entrada digitada pelo engenheiro ou lida do modelo. Unidades: cm, kN, MPa.
/// Convenção da elevação: x horizontal do eixo do apoio inferior para o superior; z vertical a partir do piso acabado
/// inferior (topo do patamar inferior = 0; topo do patamar superior = <see cref="Desnivel"/>).
/// </summary>
public sealed class EscadaLongitudinal
{
    // ---------------- geometria ----------------
    /// <summary>Desnível entre os pisos acabados (cm).</summary>
    public double Desnivel { get; set; } = 288;
    /// <summary>Número de espelhos; 0 = o programa escolhe pelo <see cref="EspelhoAlvo"/>.</summary>
    public int Espelhos { get; set; }
    /// <summary>Espelho desejado (cm) quando o número de espelhos é automático.</summary>
    public double EspelhoAlvo { get; set; } = 17.5;
    /// <summary>Piso do degrau (cm).</summary>
    public double Piso { get; set; } = 28;
    /// <summary>Largura da escada (cm).</summary>
    public double Largura { get; set; } = 120;
    /// <summary>Espessura da laje do lance, medida perpendicularmente ao plano inclinado (cm).</summary>
    public double Espessura { get; set; } = 12;
    /// <summary>Espessura do patamar (cm); 0 = a mesma do lance.</summary>
    public double EspessuraPatamar { get; set; }
    /// <summary>Patamar inferior: distância horizontal do eixo do apoio inferior ao 1º espelho (cm); 0 = sem patamar.</summary>
    public double PatamarInferior { get; set; }
    /// <summary>Patamar superior: distância horizontal do último espelho ao eixo do apoio superior (cm); 0 = sem patamar.</summary>
    public double PatamarSuperior { get; set; }
    /// <summary>Largura de cada apoio (viga) na direção do vão (cm): a armadura positiva entra no apoio até a face oposta.</summary>
    public double LarguraApoio { get; set; } = 20;
    /// <summary>Altura livre disponível sob o lance (cm), se conhecida (0 = não verificar).</summary>
    public double AlturaLivre { get; set; }

    // ---------------- materiais ----------------
    public double Fck { get; set; } = 25;
    public string Aco { get; set; } = "CA-50";
    /// <summary>Cobrimento nominal (cm). Laje, classe II: 2,5 cm (NBR 6118, Tabela 7.2).</summary>
    public double Cobrimento { get; set; } = 2.5;
    public string ClasseAgressividade { get; set; } = "II";
    /// <summary>Peso específico do concreto armado (kN/m³).</summary>
    public double PesoEspecifico { get; set; } = 25;

    // ---------------- cargas (características, por m² de projeção horizontal) ----------------
    /// <summary>Revestimento (kN/m²): sem o detalhe da arquitetura, 1,0 (granilite com espelho).</summary>
    public double Revestimento { get; set; } = 1.0;
    /// <summary>Sobrecarga de uso (kN/m²): 2,5 sem acesso ao público, 3,0 com acesso (NB-5; conferir na NBR 6120:2019).</summary>
    public double Sobrecarga { get; set; } = 2.5;
    /// <summary>Peso do parapeito ou guarda-corpo apoiado na escada (kN/m, por borda).</summary>
    public double Parapeito { get; set; }
    /// <summary>Bordas com parapeito (0, 1 ou 2).</summary>
    public int BordasComParapeito { get; set; }

    public EscadaLongitudinal Copia() => (EscadaLongitudinal)MemberwiseClone();
}

/// <summary>Trecho do eixo da escada na elevação (patamar ou lance).</summary>
public sealed class TrechoEscada
{
    public string Nome { get; set; } = "";
    public bool Inclinado { get; set; }
    /// <summary>Início e fim no eixo (x, z em cm).</summary>
    public (double X, double Z) P0 { get; set; }
    public (double X, double Z) P1 { get; set; }
    /// <summary>Espessura perpendicular ao eixo (cm).</summary>
    public double H { get; set; }
    public double Angulo => Math.Atan2(P1.Z - P0.Z, P1.X - P0.X);
    public double Comprimento => Math.Sqrt(Math.Pow(P1.X - P0.X, 2) + Math.Pow(P1.Z - P0.Z, 2));
    public double ZEm(double x) => Math.Abs(P1.X - P0.X) < 1e-9 ? P0.Z : P0.Z + (x - P0.X) * (P1.Z - P0.Z) / (P1.X - P0.X);
}

/// <summary>Geometria resolvida da escada: espelhos, ângulo, eixo (para a estática e o MEF) e o contorno real com os degraus.</summary>
public sealed class GeometriaEscada
{
    public int N { get; set; }
    public double Espelho { get; set; }
    public double Piso { get; set; }
    public double Alfa { get; set; }
    public double Cos => Math.Cos(Alfa);
    public double Sen => Math.Sin(Alfa);
    /// <summary>Projeção horizontal do lance, (n − 1)·piso (cm).</summary>
    public double LanceHorizontal { get; set; }
    /// <summary>x do 1º e do último espelho (cm).</summary>
    public double X1 { get; set; }
    public double X2 { get; set; }
    /// <summary>Vão horizontal entre os eixos dos apoios (cm).</summary>
    public double Vao { get; set; }
    public double H { get; set; }
    public double Hp { get; set; }
    /// <summary>Trechos do eixo (patamar inferior, lance, patamar superior), em ordem.</summary>
    public List<TrechoEscada> Trechos { get; } = new List<TrechoEscada>();
    /// <summary>Contorno da peça na elevação (cm), anti-horário, com os degraus: para a prévia, o MEF de chapa e a família.</summary>
    public List<(double X, double Z)> Contorno { get; } = new List<(double, double)>();
    /// <summary>Quina inferior do lance com o patamar superior (onde a face de baixo é reentrante): x, z da face inferior.</summary>
    public (double X, double Z)? QuinaReentrante { get; set; }
    /// <summary>Quina da face inferior do lance com o patamar inferior (saliente).</summary>
    public (double X, double Z)? QuinaInferior { get; set; }

    public TrechoEscada TrechoEm(double x) =>
        Trechos.FirstOrDefault(t => x >= Math.Min(t.P0.X, t.P1.X) - 1e-9 && x <= Math.Max(t.P0.X, t.P1.X) + 1e-9) ?? Trechos.Last();

    /// <summary>Monta a geometria da entrada. Erros de entrada (espelho nulo etc.) vão para <paramref name="erros"/>.</summary>
    public static GeometriaEscada De(EscadaLongitudinal e, List<string> erros = null)
    {
        erros ??= new List<string>();
        var g = new GeometriaEscada();
        int n = e.Espelhos > 0 ? e.Espelhos : (int)Math.Max(2, Math.Round(e.Desnivel / Math.Max(1, e.EspelhoAlvo)));
        g.N = n;
        g.Espelho = e.Desnivel / n;
        g.Piso = e.Piso;
        if (e.Piso <= 0 || e.Desnivel <= 0) { erros.Add("Piso e desnível precisam ser positivos."); g.Piso = Math.Max(1, e.Piso); }
        g.Alfa = Math.Atan2(g.Espelho, g.Piso);
        g.LanceHorizontal = (n - 1) * g.Piso;
        g.H = e.Espessura;
        g.Hp = e.EspessuraPatamar > 0 ? e.EspessuraPatamar : e.Espessura;
        double pi = Math.Max(0, e.PatamarInferior), ps = Math.Max(0, e.PatamarSuperior);
        g.X1 = pi;
        g.X2 = pi + g.LanceHorizontal;
        g.Vao = g.X2 + ps;
        double tg = Math.Tan(g.Alfa), c = Math.Cos(g.Alfa), H = e.Desnivel, h = g.H, hp = g.Hp;

        // linha dos cantos internos dos degraus: z = (x − X1)·tgα; face inferior do lance: essa linha baixada h/cosα;
        // eixo do lance: baixada h/(2cosα). Patamares: eixo a hp/2 abaixo do piso.
        double ZEixoLance(double x) => (x - g.X1) * tg - h / (2 * c);
        double ZFundoLance(double x) => (x - g.X1) * tg - h / c;
        // quinas do eixo (interseção do eixo do lance com o do patamar)
        double xk1 = pi > 0 ? g.X1 + (h / (2 * c) - hp / 2) / tg : 0;
        // patamar superior: piso em z = H; o último espelho fica em X2 e sobe de (n−1)e até H
        double xk2 = ps > 0 ? g.X1 + (H - hp / 2 + h / (2 * c)) / tg : g.Vao;
        if (pi > 0 && xk1 < 0) { erros.Add("Patamar inferior curto demais para a espessura: aumente o patamar."); xk1 = 0; }
        if (ps > 0 && xk2 > g.Vao) { erros.Add("Patamar superior curto demais para a espessura: aumente o patamar."); xk2 = g.Vao; }
        if (pi > 0) g.Trechos.Add(new TrechoEscada { Nome = "Patamar inferior", P0 = (0, -hp / 2), P1 = (xk1, -hp / 2), H = hp });
        g.Trechos.Add(new TrechoEscada { Nome = "Lance", Inclinado = true, P0 = (xk1, pi > 0 ? -hp / 2 : ZEixoLance(0)), P1 = (xk2, ps > 0 ? H - hp / 2 : ZEixoLance(g.Vao)), H = h });
        if (ps > 0) g.Trechos.Add(new TrechoEscada { Nome = "Patamar superior", P0 = (xk2, H - hp / 2), P1 = (g.Vao, H - hp / 2), H = hp });

        // contorno real (anti-horário a partir do canto inferior esquerdo)
        var k = g.Contorno;
        // face inferior: patamar inferior → lance → patamar superior
        double xb1 = pi > 0 ? g.X1 + (h / c - hp) / tg : 0;          // quina da face inferior com o patamar inferior
        double xb2 = ps > 0 ? g.X1 + (H - hp + h / c) / tg : g.Vao;  // quina da face inferior com o patamar superior
        if (pi > 0) { k.Add((0, -hp)); k.Add((xb1, -hp)); g.QuinaInferior = (xb1, -hp); }
        else k.Add((0, ZFundoLance(0)));
        if (ps > 0) { k.Add((xb2, H - hp)); k.Add((g.Vao, H - hp)); g.QuinaReentrante = (xb2, H - hp); }
        else k.Add((g.Vao, ZFundoLance(g.Vao)));
        // face superior, da direita para a esquerda: piso do patamar superior, degraus, piso do patamar inferior
        // (sem patamar superior o último espelho é a face da ponta, encostada no apoio)
        k.Add((g.Vao, H));
        for (int i = n; i >= 1; i--)
        {
            double xr = g.X1 + (i - 1) * g.Piso;   // espelho i (1 = o primeiro), de (i−1)·e até i·e; piso i à direita dele
            k.Add((xr, i * g.Espelho));
            k.Add((xr, (i - 1) * g.Espelho));
        }
        if (pi > 0) k.Add((0, 0));
        LimparContorno(k);
        return g;
    }

    /// <summary>Tira pontos repetidos e pontos no meio de um segmento reto (colineares).</summary>
    public static void LimparContorno(List<(double X, double Z)> k)
    {
        bool mudou = true;
        while (mudou && k.Count > 3)
        {
            mudou = false;
            for (int i = 0; i < k.Count && k.Count > 3; i++)
            {
                var a = k[(i - 1 + k.Count) % k.Count]; var b = k[i]; var c = k[(i + 1) % k.Count];
                double cruz = (b.X - a.X) * (c.Z - a.Z) - (b.Z - a.Z) * (c.X - a.X);
                bool igual = Math.Abs(b.X - a.X) < 1e-6 && Math.Abs(b.Z - a.Z) < 1e-6;
                if (igual || Math.Abs(cruz) < 1e-6) { k.RemoveAt(i); mudou = true; break; }
            }
        }
    }
}
