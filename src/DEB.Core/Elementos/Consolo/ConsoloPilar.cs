using System.Collections.Generic;
using System.Linq;

namespace DEB.Core.Elementos.Consolo;

/// <summary>
/// Consolo de um nível (uma ou mais faces), com a armadura padrão do escritório (P61/PP127). O nome vem de quando só o pilar
/// tinha consolo; o tipo é do consolo (D27) e o nome fica (é usado pelos plugins e testes).
/// </summary>
public sealed class ConsoloPilar
{
    public int Slot { get; set; }
    /// <summary>Nome do tipo no catálogo de consolos padrão.</summary>
    public string Tipo { get; set; } = "";
    /// <summary>O tipo é duplo (tirante passante nas faces opostas).</summary>
    public bool TipoDuplo { get; set; }
    /// <summary>Letra do corte de forma (B, C…).</summary>
    public string Letra { get; set; } = "B";
    /// <summary>Grupo de consolos mostrados no mesmo corte (topos a menos de 50 cm), numerado de cima para baixo.</summary>
    public int GrupoCorte { get; set; }
    /// <summary>O corte deste grupo é desenhado; falso = o grupo repete um corte já apresentado (mesma letra, sem desenho novo).</summary>
    public bool CorteDesenhado { get; set; } = true;
    public List<int> Faces { get; set; } = new List<int>();
    /// <summary>Distância da base do pilar ao topo do consolo (cm).</summary>
    public double ZTopo { get; set; }
    /// <summary>Largura (ao longo da face), balanço, altura no pilar e na ponta, deslocamento lateral (cm).</summary>
    public double B { get; set; }
    public double A { get; set; }
    public double H { get; set; }
    public double H1 { get; set; }
    public double Desl { get; set; }
    public int TirN { get; set; }
    public double TirPhi { get; set; }
    public bool Laco { get; set; }
    public int CostN { get; set; }
    public double CostPhi { get; set; }
    public int VertN { get; set; }
    public double VertPhi { get; set; }
    public int InfN { get; set; }
    public double InfPhi { get; set; }
    public double InfAnc { get; set; }
    public double InfRetorno { get; set; }
    public double InfDescida { get; set; }
    public double TirPerna { get; set; }
    public int PinoN { get; set; }
    public double PinoPhi { get; set; }
    public double PinoAcima { get; set; }
    public double PinoEmb { get; set; }
    public double PinoPe { get; set; }
    public double AlmA { get; set; }
    public double AlmB { get; set; }
    public double AlmE { get; set; }

    /// <summary>
    /// Consolos opostos desta linha estão em nível (<see cref="BarrasConsolo.ParesEmNivel"/>, D30): formam um duplo mesmo que o
    /// tipo seja simples. Preenchido por quem lê a lista de consolos do elemento.
    /// </summary>
    public bool EmNivel { get; set; }

    /// <summary>Pares de faces opostas (tirante passante) e faces sozinhas (tirante ancorado no pilar).</summary>
    public List<(int F1, int F2)> Pares => !(TipoDuplo || EmNivel) ? new List<(int, int)>()
        : new[] { (1, 3), (2, 4) }.Where(p => Faces.Contains(p.Item1) && Faces.Contains(p.Item2)).ToList();
    public List<int> Sozinhas => Faces.Where(f => !Pares.Any(p => p.F1 == f || p.F2 == f)).ToList();
    public bool Duplo => Pares.Count > 0;

    /// <summary>Volume dos consolos deste nível (m³).</summary>
    public double Volume => Faces.Count * B * A * (H + H1) / 2 / 1e6;
}
