using System.Collections.Generic;
using System.Linq;
using Armadura.Core.Templates;
using Armadura.Core.Templates.PilarPreMoldado;
using Xunit;

namespace Armadura.Core.Tests;

/// <summary>Arquivo de modelos dos templates (PADRÃO + elementos salvos; pedido do usuário em 2026-10-30).</summary>
[Collection("Pilar")]
public class ModelosTemplateTests
{
    private static readonly TemplatePilarPreMoldado T = new TemplatePilarPreMoldado();

    [Fact]
    public void SalvaELeOsModelosComListasENumeros()
    {
        var arq = ArquivoModelos.Novo(T);
        var v = new ValoresTemplate(T.Parametros);
        v["Nome"] = "P62";
        v["L"] = "1200.5";
        v["Consolos"] = "CD 40x23,5x25|1 e 3|592.5|0;CS 20x28,5x25|2|900|5";
        arq.Salvar("P62", v, T.Parametros);
        var texto = arq.Escrever(T);
        Assert.StartsWith("# template: pilar-premoldado", texto);
        Assert.Contains(";1200,5;", texto); // vírgula decimal: o Excel em português não vira data
        Assert.Contains("\"CD 40x23,5x25|1 e 3|592.5|0;CS 20x28,5x25|2|900|5\"", texto); // lista entre aspas

        var lido = ArquivoModelos.Ler(texto, T);
        Assert.Equal("pilar-premoldado", lido.TemplateId);
        Assert.Single(lido.Modelos);
        var w = lido.Valores(T, "P62");
        Assert.Equal("1200.5", w["L"]);
        Assert.Equal(v["Consolos"], w["Consolos"]);
        Assert.Equal(1200.5, w.Numero("L"));
        // sem modelo: PADRÃO (= padrão do plugin num arquivo novo)
        Assert.Equal(new ValoresTemplate(T.Parametros)["L"], lido.Valores(T)["L"]);
    }

    [Fact]
    public void PadraoDoArquivoValeECamposVaziosFicamComOPadraoDoPlugin()
    {
        // arquivo editado à mão: só algumas colunas, PADRAO sem acento, coluna que o template não tem
        var texto = "Modelo;Nome;B;H;CampoAntigo\nPADRAO;PX;40;;abc\nP70;P70;45,5;60;\n";
        var arq = ArquivoModelos.Ler(texto, T);
        var p = arq.Valores(T);
        Assert.Equal("PX", p["Nome"]);
        Assert.Equal("40", p["B"]);
        Assert.Equal(new ValoresTemplate(T.Parametros)["H"], p["H"]); // vazio = padrão do plugin
        var m = arq.Valores(T, "P70");
        Assert.Equal("45.5", m["B"]);
        Assert.Equal("60", m["H"]);
        Assert.Equal("P70", m["Nome"]);
        // a coluna desconhecida é mantida ao gravar
        Assert.Contains("CampoAntigo", arq.Escrever(T));
    }

    [Fact]
    public void ImportarSubstituiOsDeMesmoNomeEExcluir()
    {
        var a = ArquivoModelos.Ler("Modelo;Nome;B\nPADRÃO;P1;50\nP1;P1;50\nP2;P2;30\n", T);
        var b = ArquivoModelos.Ler("Modelo;Nome;B\nPADRÃO;PB;60\nP2;P2;35\nP3;P3;40\n", T);
        Assert.Equal(2, a.Importar(b));
        Assert.Equal(new[] { "P1", "P2", "P3" }, a.Modelos.Select(m => m.Nome).OrderBy(x => x));
        Assert.Equal("35", a.Valores(T, "P2")["B"]);
        Assert.Equal("60", a.Valores(T)["B"]); // o PADRÃO importado passa a valer
        Assert.True(a.Excluir("P1"));
        Assert.Null(a.Obter("P1"));
        // salvar com o nome PADRÃO muda a linha padrão
        var v = a.Valores(T);
        v["B"] = "70";
        a.Salvar(ArquivoModelos.NomePadrao, v, T.Parametros);
        Assert.Equal("70", a.Valores(T)["B"]);
        Assert.Equal(2, a.Modelos.Count);
    }

    [Fact]
    public void ExcelComTabulacaoEAvisos()
    {
        var avisos = new List<string>();
        var arq = ArquivoModelos.Ler("Modelo\tNome\tB\nP1\tP1\t50\nP1\tP1\t55\n", T, avisos);
        Assert.Single(arq.Modelos);
        Assert.Equal("55", arq.Valores(T, "P1")["B"]);
        Assert.Contains(avisos, x => x.Contains("repetido"));
    }
}
