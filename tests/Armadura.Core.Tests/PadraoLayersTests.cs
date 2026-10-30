using System.Collections.Generic;
using System.Linq;
using Armadura.Core.Configuracao;
using Xunit;

namespace Armadura.Core.Tests;

/// <summary>Arquivo de padrões de layer (layers.csv, pedido do usuário em 2026-10-30).</summary>
public class PadraoLayersTests
{
    [Fact]
    public void PadraoDeFabricaComoPedido()
    {
        var p = PadroesLayers.Padrao().ToDictionary(l => l.Nome);
        Assert.Equal(0.30, p["ARM-BARRA"].EspessuraMm);
        Assert.Equal(7, p["ARM-TEXTO"].Cor);
        Assert.Equal(0.05, p["ARM-TEXTO"].EspessuraMm);
        Assert.Equal(0.20, p["ARM-FORMA"].EspessuraMm);
        Assert.DoesNotContain(p.Values, l => l.Cor == 2); // nunca amarelo
    }

    [Fact]
    public void EscreveELeIgual()
    {
        var texto = PadroesLayers.Escrever(PadroesLayers.Padrao());
        Assert.Contains("ARM-BARRA;1;0,30;;", texto);
        var lidos = PadroesLayers.Ler(texto);
        Assert.Equal(PadroesLayers.Padrao().Select(l => (l.Nome, l.Cor, l.EspessuraMm, l.TipoLinha)),
                     lidos.Select(l => (l.Nome, l.Cor, l.EspessuraMm, l.TipoLinha)));
    }

    [Fact]
    public void AceitaExcelEValoresRuins()
    {
        var avisos = new List<string>();
        // tabulação, ponto decimal, espessura vazia (= 0), cor inválida (= 7 com aviso), layer repetido (vale o último)
        var l = PadroesLayers.Ler("Layer\tCor\tEspessura\nMEU-LAYER\t3\t0.5\nOUTRO\t\t\nMEU-LAYER\t4\t0,35\nRUIM\t999\t1\n", avisos);
        Assert.Equal(3, l.Count);
        Assert.Equal(4, l.Single(x => x.Nome == "MEU-LAYER").Cor);
        Assert.Equal(0.35, l.Single(x => x.Nome == "MEU-LAYER").EspessuraMm);
        Assert.Equal(0, l.Single(x => x.Nome == "OUTRO").EspessuraMm);
        Assert.Equal(7, l.Single(x => x.Nome == "RUIM").Cor);
        Assert.NotEmpty(avisos);
        // espessuras do CAD: a válida mais próxima
        Assert.Equal(5, PadroesLayers.Centesimos(0.05));
        Assert.Equal(30, PadroesLayers.Centesimos(0.3));
        Assert.Equal(35, PadroesLayers.Centesimos(0.33));
        Assert.Equal(0, PadroesLayers.Centesimos(-1));
    }
}
