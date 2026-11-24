using System;
using System.Collections.Generic;
using System.Linq;
using DEB.Core.Analise.Barras;
using DEB.Core.Elementos.Metalica;

namespace DEB.Core.Analise.Objetos;

/// <summary>Ligação de extremidade de uma barra metálica (D48): flexível (rótula: só força) ou rígida (transmite momento).</summary>
public enum LigacaoExtremidade { Flexivel, Rigida }

/// <summary>Base de um pilar metálico: engastada (placa de base com chumbadores fora da linha) ou rotulada.</summary>
public enum BasePilarMetalico { Engastada, Rotulada }

/// <summary>
/// Ligação metálica como objeto (D46/D48/D49), com o modelo da biblioteca: chapa de alma na ponta flexível de uma viga (entre a viga e
/// o suporte; no modelo de barras é a rótula da ponta) ou placa de base no pé de um pilar (no modelo, o apoio). Verificada com os
/// esforços da ponta em cada combinação.
/// </summary>
public sealed class LigacaoMetalica
{
    public Dono Dono { get; set; } = new Dono { Tipo = TipoDono.Ligacao };
    public DEB.Core.Elementos.Metalica.Ligacoes.TipoLigacao Tipo { get; set; } = DEB.Core.Elementos.Metalica.Ligacoes.TipoLigacao.ChapaAlma;
    /// <summary>Placa de base: o pilar.</summary>
    public BarraMetalicaObjeto Pilar { get; set; }
    public DadosPlacaBase Placa { get; set; }
    public BarraMetalicaObjeto Viga { get; set; }
    public bool PontaI { get; set; }
    public BarraMetalicaObjeto Suporte { get; set; }
    /// <summary>Modelo da biblioteca de ligações (D49) usado nesta ligação.</summary>
    public DEB.Core.Elementos.Metalica.Ligacoes.ModeloLigacao Modelo { get; set; }
    public DadosChapaAlma Chapa { get; set; }
    public DadosCantoneira Cantoneira { get; set; }
    public DadosChapaTopo Topo { get; set; }
}

/// <summary>
/// Barra metálica como objeto da análise (D46/D48): pilar ou viga de perfil I/H laminado ou soldado entre dois pontos do eixo,
/// com a orientação da alma, as ligações das pontas e as cargas das vigas. Os nós coincidentes com os de outra peça metálica são o
/// mesmo nó (o modelo junta pela tolerância); a viga que chega num pilar ou noutra viga é levada ao eixo dela por
/// <see cref="EstruturaObjetos.PrepararMetalicas"/>. As barras do modelo ficam com o dono gravado: os esforços voltam à peça.
/// </summary>
public sealed class BarraMetalicaObjeto
{
    public Dono Dono { get; set; } = new Dono { Tipo = TipoDono.Viga };
    public bool Pilar => Dono.Tipo == TipoDono.Pilar;
    /// <summary>Pontas do eixo (cm). Pilar: I embaixo.</summary>
    public Vetor3 PontoI { get; set; }
    public Vetor3 PontoJ { get; set; }
    public PerfilI Perfil { get; set; }
    public AcoEstrutural Aco { get; set; }
    /// <summary>Direção da alma no plano da seção (eixo local z do modelo: flexão em torno do eixo de maior inércia no plano x–z).</summary>
    public Vetor3 DirAlma { get; set; } = Vetor3.EixoZ;
    public LigacaoExtremidade LigacaoI { get; set; } = LigacaoExtremidade.Flexivel;
    public LigacaoExtremidade LigacaoJ { get; set; } = LigacaoExtremidade.Flexivel;
    /// <summary>Só pilar: apoio da base quando não há outra peça embaixo.</summary>
    public BasePilarMetalico Base { get; set; } = BasePilarMetalico.Engastada;
    /// <summary>Vigas: cargas além do peso próprio (kN/m) e concentradas (X em cm a partir da ponta I; kN).</summary>
    public double G { get; set; }
    public double Q { get; set; }
    public List<(double X, double G, double Q)> Concentradas { get; } = new List<(double, double, double)>();
    /// <summary>Comprimento destravado da mesa comprimida (cm) para a FLT; 0 = o comprimento da barra.</summary>
    public double Lb { get; set; }
    /// <summary>Do eixo da análise ao centro físico da peça (cm): no Revit a linha de locação da viga fica, por padrão, no topo.</summary>
    public Vetor3 DeslocamentoFisico { get; set; } = Vetor3.Zero;
    /// <summary>Pontos internos com nó (cm a partir de I): vigas que chegam, travamentos.</summary>
    public List<double> PontosInternos { get; } = new List<double>();

    // gerado
    public List<(double T, int No)> NosEixo { get; } = new List<(double, int)>();
    public List<BarraPortico> Barras { get; } = new List<BarraPortico>();
    public double Comprimento => (PontoJ - PontoI).Modulo;
    public Vetor3 Direcao => (PontoJ - PontoI).Unitario;
    public int NoI => NosEixo[0].No;
    public int NoJ => NosEixo[NosEixo.Count - 1].No;

    /// <summary>Seção do modelo: Iy local = Ix do perfil (flexão no plano da alma), Iz local = Iy do perfil.</summary>
    public SecaoBarra SecaoModelo() => new SecaoBarra { A = Perfil.A, Iy = Perfil.Ix, Iz = Perfil.Iy, J = Perfil.J, Descricao = Perfil.Nome };

    public static MaterialBarra Material(AcoEstrutural a) =>
        new MaterialBarra { E = a.E / 10, G = a.G / 10, Peso = 78.5 / 1e6, Descricao = $"{a.Nome} (fy = {a.Fy:0} MPa)" };

    internal void Gerar(ModeloBarras m, CasoCarga g, CasoCarga q)
    {
        NosEixo.Clear(); Barras.Clear();
        double L = Comprimento;
        var ts = new List<double> { 0, L };
        ts.AddRange(PontosInternos.Where(t => t > 1 && t < L - 1));
        ts = ts.Select(t => Math.Round(t, 1)).Distinct().OrderBy(t => t).ToList();
        foreach (double t in ts)
        {
            Vetor3 p = PontoI + Direcao * t;
            NosEixo.Add((t, m.No(p.X, p.Y, p.Z, Dono, $"{Dono} t={t:0}")));
        }
        var sec = SecaoModelo();
        var mat = Material(Aco);
        // a alma no plano local x–z: componente de DirAlma perpendicular ao eixo
        Vetor3 alma = DirAlma - Direcao * DirAlma.Ponto(Direcao);
        if (alma.Modulo < 1e-6) alma = Math.Abs(Direcao.Z) > 0.9 ? Vetor3.EixoX : Vetor3.EixoZ;
        for (int k = 0; k + 1 < NosEixo.Count; k++)
        {
            var b = m.Barra(NosEixo[k].No, NosEixo[k + 1].No, sec, mat, Dono, NosEixo.Count > 2 ? $"{Dono} trecho {k + 1}" : Dono.ToString());
            b.VetorXZ = alma.Unitario;
            // ligação flexível: rótula à flexão nos dois planos (a torção fica)
            if (k == 0 && LigacaoI == LigacaoExtremidade.Flexivel) { b.LiberaI[4] = true; b.LiberaI[5] = true; }
            if (k == NosEixo.Count - 2 && LigacaoJ == LigacaoExtremidade.Flexivel) { b.LiberaJ[4] = true; b.LiberaJ[5] = true; }
            Barras.Add(b);
            if (!Pilar)
            {
                if (G != 0) g.Distribuidas.Add(new CargaDistribuida { Barra = b.Id, Q = new Vetor3(0, 0, -G / 100) });
                if (Q != 0) q.Distribuidas.Add(new CargaDistribuida { Barra = b.Id, Q = new Vetor3(0, 0, -Q / 100) });
            }
        }
        foreach (var (x, cg, cq) in Concentradas)
        {
            int k = Math.Min(Math.Max(0, NosEixo.FindLastIndex(n => n.T <= x)), Barras.Count - 1);
            double a = x - NosEixo[k].T;
            if (cg != 0) g.Pontuais.Add(new CargaPontualBarra { Barra = Barras[k].Id, A = a, F = new Vetor3(0, 0, -cg) });
            if (cq != 0) q.Pontuais.Add(new CargaPontualBarra { Barra = Barras[k].Id, A = a, F = new Vetor3(0, 0, -cq) });
        }
    }

    /// <summary>Distância (cm) do ponto ao eixo da barra e a posição t da projeção (pode cair fora de 0…L).</summary>
    public (double Dist, double T) Projecao(Vetor3 p)
    {
        double t = (p - PontoI).Ponto(Direcao);
        Vetor3 pe = PontoI + Direcao * t;
        return ((p - pe).Modulo, t);
    }
}
