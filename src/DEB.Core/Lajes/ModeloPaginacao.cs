using System;
using System.Collections.Generic;
using System.Linq;

namespace DEB.Core.Lajes;

// =====================================================================================================================
// Entradas e saídas do motor de paginação (sistema local do pano, metros). Ver MotorPaginacao.
// =====================================================================================================================

/// <summary>Uma linha horizontal de leitura da seção da viga: cota z e trechos dentro do sólido, na coordenada n da viga
/// (a partir da linha de localização; n = perpendicular em planta à direção da viga).</summary>
public sealed class LinhaSecao
{
    public double Z;
    public List<(double A, double B)> Trechos = new List<(double, double)>();
}

/// <summary>Seção de uma viga medida por linhas horizontais (interseção com o sólido).</summary>
public sealed class SecaoViga
{
    public List<LinhaSecao> Linhas = new List<LinhaSecao>();
    /// <summary>Tolerância para achar a linha de uma cota pedida.</summary>
    public double ToleranciaZ = 0.005;

    /// <summary>Trechos na cota z (a linha mais próxima dentro da tolerância) ou null.</summary>
    public List<(double A, double B)> Em(double z)
    {
        LinhaSecao melhor = null; double d = double.MaxValue;
        foreach (var l in Linhas)
        {
            double x = Math.Abs(l.Z - z);
            if (x <= ToleranciaZ && x < d && l.Trechos.Count > 0) { d = x; melhor = l; }
        }
        return melhor?.Trechos;
    }

    /// <summary>Linha mais baixa com trechos (ou null se a seção está vazia).</summary>
    public LinhaSecao MaisBaixa => Linhas.Where(l => l.Trechos.Count > 0).OrderBy(l => l.Z).FirstOrDefault();
}

/// <summary>Viga que corre na direção v (transversal ao vão): é nela que as pontas dos painéis apoiam.</summary>
public sealed class VigaTransversal
{
    public string Id = "", Tipo = "";
    /// <summary>Coordenada u da linha de localização da viga.</summary>
    public double UEixo;
    /// <summary>Extensão da viga em v (Va ≤ Vb).</summary>
    public double Va, Vb;
    /// <summary>Componente u do vetor n da viga (±1).</summary>
    public double NU;
    public SecaoViga Secao = new SecaoViga();
}

/// <summary>Viga que corre na direção u (paralela ao vão): ladeia as bordas laterais do pano.</summary>
public sealed class VigaLongitudinal
{
    public string Id = "", Tipo = "";
    public double VEixo;
    public double Ua, Ub;
    /// <summary>Componente v do vetor n da viga (±1).</summary>
    public double NV;
    public SecaoViga Secao = new SecaoViga();
}

/// <summary>Pilar em planta (caixa da seção na altura do painel, no sistema do pano).</summary>
public sealed class PilarPano
{
    public string Id = "";
    public RetUV Ret;
}

/// <summary>Abertura do pano: furo do croqui ou entalhe do contorno.</summary>
public sealed class AberturaPano
{
    public RetUV Ret;
    public string Origem = "";
    /// <summary>Linha de corte (junta) no meio da abertura, quando a estratégia corta pelas aberturas.</summary>
    public double? VCorte;
}

/// <summary>Faixa em v do pano com comprimento constante (o pano em L ou com cantos recortados tem mais de uma).</summary>
public sealed class FaixaPano
{
    public double V0, V1, U0, U1;
    public bool BordaV0, BordaV1;
}

/// <summary>Leitura do contorno do pano.</summary>
public sealed class ClassificacaoPano
{
    public double U0, U1, V0, V1;
    public List<AberturaPano> Aberturas = new List<AberturaPano>();
    /// <summary>Cantos fora do contorno (recortes de forma).</summary>
    public List<RetUV> Fora = new List<RetUV>();
    public List<FaixaPano> Faixas = new List<FaixaPano>();
    public List<PilarPano> Pilares = new List<PilarPano>();
    public List<string> Alertas = new List<string>();
}

/// <summary>Apoio de uma ponta de painel (ou de peça).</summary>
public sealed class ApoioPonta
{
    public string Viga, VigaTipo;
    /// <summary>Apoio na aba (m); null quando não se aplica (consolo).</summary>
    public double? Apoio;
    public double? UEixoViga, UFace, UFimAba, Aba, ContornoPiso, PisoAlemDaFace;
    /// <summary>Ponta apoiada em consolo a colocar pelo usuário.</summary>
    public bool Consolo;
    /// <summary>Problema (sem apoio, apoio curto…) — lista de avisos ao usuário.</summary>
    public string Alerta;
    /// <summary>Informação (ex.: apoio em consolo a colocar).</summary>
    public string Aviso;
}

/// <summary>Recorte numa peça: abertura (entalhe) ou pilar.</summary>
public sealed class RecortePeca
{
    public string Tipo = "";   // "abertura" | "pilar"
    public RetUV Ret;
    public string Ref;
}

/// <summary>Peça de painel (o que é fabricado e inserido): uma faixa de painel pode virar mais de uma peça se um recorte a atravessa.</summary>
public sealed class PecaPainel
{
    public double U0, U1, V0, V1;
    public ApoioPonta Ini, Fim;
    public List<RecortePeca> Recortes = new List<RecortePeca>();
    public string Marca;
    public FaixaPainel Painel;
    public double Largura => V1 - V0;
    public double Comprimento => U1 - U0;
}

/// <summary>Faixa de painel (uma linha de painéis na largura) antes da divisão em peças.</summary>
public sealed class FaixaPainel
{
    public double U0, U1, V0, V1;
    /// <summary>padrao | ajuste | abertura | abertura+ajuste, com "+entalhe_abertura" quando recebe entalhe.</summary>
    public string Tipo = "";
    public ApoioPonta Ini, Fim;
    public FaixaPano Faixa;
    public List<RecortePeca> Recortes = new List<RecortePeca>();
    public List<PecaPainel> Pecas = new List<PecaPainel>();
    public string Marca;
    public double Largura => V1 - V0;
}

/// <summary>Faixa maciça moldada no local (etapa 2).</summary>
public sealed class FaixaMacica
{
    public RetUV Ret;
    public string Motivo = "";
    public List<string> Alertas = new List<string>();
    public string Marca;
}

/// <summary>Junta (chave de cisalhamento) entre duas faixas de painel.</summary>
public sealed class JuntaPaineis
{
    public double V, U0, U1;
    public FaixaPainel Esq, Dir;
    /// <summary>Posições u dos tirantes nesta junta.</summary>
    public List<double> Tirantes = new List<double>();
    public string Marca;
    public double Comprimento => U1 - U0;
}

/// <summary>Apoio em consolo a colocar pelo usuário (faixa maciça junto a viga, peça cortada por furo junto a viga).</summary>
public sealed class ConsoloAColocar
{
    public string Viga, VigaTipo;
    /// <summary>'u' = viga que corre em u (face em v); 'v' = viga que corre em v (face em u).</summary>
    public char EixoViga;
    public double De, Ate, Face;
    public List<int> Faixas = new List<int>();
    public List<PecaPainel> Pecas = new List<PecaPainel>();
    public List<string> Motivos = new List<string>();
    internal string Motivo;
    public double Extensao => Ate - De;
}

/// <summary>Resultado da paginação de um pano.</summary>
public sealed class ResultadoPano
{
    public ClassificacaoPano Geo;
    public List<FaixaPainel> Paineis = new List<FaixaPainel>();
    public List<FaixaMacica> FaixasMacicas = new List<FaixaMacica>();
    public List<JuntaPaineis> Juntas = new List<JuntaPaineis>();
    public List<PontoUV> Tirantes = new List<PontoUV>();
    public List<ConsoloAColocar> Consolos = new List<ConsoloAColocar>();
    public List<string> Alertas = new List<string>();
    public IEnumerable<PecaPainel> Pecas => Paineis.SelectMany(p => p.Pecas);
}
