using System.Linq;
using System.Text;
using DEB.Core.Documentacao;
using Xunit;

namespace DEB.Core.Tests;

/// <summary>.pc3/.pmp no formato 3.0 (JSON) do AutoCAD 2025+ e a lista de impressoras do ARMPDFLOTE.</summary>
public class PiaJsonTests
{
    // .pmp sintético no formato do AutoCAD 2026 (estrutura copiada de um .pmp real da AutoCAD PDF, caminhos neutros)
    private const string Pmp =
        "PIAFILEVERSION_3.0,json\r\n" +
        "{\n \"data\" : \n {\n  \"meta\" : \n  {\n   \"driver_pathname\" : \"C:\\\\drv\\\\pdfplot17.hdi\",\n   \"file_only\" : true,\n" +
        "   \"toolkit_version\" : 1,\n   \"user_defined_model_pathname\" : \"C:\\\\x\\\\Teste.pmp\"\n  },\n" +
        "  \"udm\" : \n  {\n   \"calibration\" : \n   {\n    \"_x\" : 1.0,\n    \"_y\" : 1.0\n   },\n   \"media\" : \n   {\n" +
        "    \"description\" : \n    {\n     \"0\" : \n     {\n      \"caps_type\" : 2,\n      \"dimensional\" : true,\n" +
        "      \"media_bounds_urx\" : 1121.300048828125,\n      \"media_bounds_ury\" : 594.0,\n      \"name\" : \"User-1\"\n     }\n    },\n" +
        "    \"size\" : \n    {\n     \"0\" : \n     {\n      \"caps_type\" : 2,\n      \"landscape_mode\" : true,\n" +
        "      \"localized_name\" : \"Previous paper size  (1121.30 x 594.00 mm)\",\n      \"media_description_name\" : \"User-1\",\n" +
        "      \"media_group\" : 15,\n      \"name\" : \"User-1\"\n     }\n    }\n   }\n  }\n },\n \"subver\" : \"json\",\n \"version\" : 0.01\n}";

    private static ArquivoPia Arquivo() => ArquivoPia.Ler(Encoding.ASCII.GetBytes(Pmp));

    [Fact]
    public void IdaEVoltaDoJsonEhExata()
    {
        var a = Arquivo();
        Assert.True(a.Json);
        Assert.True(ArquivoPia.EhPia(Encoding.ASCII.GetBytes(Pmp)));
        Assert.Equal(Pmp, Encoding.ASCII.GetString(a.Gravar()));
        Assert.Equal(@"C:\x\Teste.pmp", TamanhosPc3.PmpLigado(a.Raiz));
        var t = TamanhosPc3.Listar(a.Raiz).Single();
        Assert.Equal(1121.3, t.Maior, 2);
        Assert.Equal(594.0, t.Menor, 2);
    }

    [Fact]
    public void FolhasCriadasNoJsonSaoRelidas()
    {
        var a = Arquivo();
        var passos = TamanhosPc3.Conciliar(a.Raiz, FormatosFolha.Padrao());
        // a folha 1121,3 × 594 que já existia vira o "A1++" (mesmo tamanho, não duplica); as outras são criadas
        Assert.All(passos, p => Assert.Equal(p.Formato.Nome.StartsWith("A1++") ? AcaoTamanhoPc3.Renomear : AcaoTamanhoPc3.Criar, p.Acao));
        var bytes = a.Gravar();
        var texto = Encoding.UTF8.GetString(bytes);
        Assert.StartsWith("PIAFILEVERSION_3.0,json\r\n{", texto);
        Assert.Contains("\"landscape_mode\" : true", texto);   // booleano JSON, não "TRUE"
        Assert.Contains("\"media_bounds_urx\" : 841.0", texto); // número, não texto
        Assert.DoesNotContain("TRUE", texto);
        var relido = ArquivoPia.Ler(bytes);
        var folhas = TamanhosPc3.Listar(relido.Raiz);
        Assert.Equal(FormatosFolha.Padrao().Count, folhas.Count);
        // a 2ª conciliação não muda nada
        Assert.All(TamanhosPc3.Conciliar(relido.Raiz, FormatosFolha.Padrao()), p => Assert.Equal(AcaoTamanhoPc3.Manter, p.Acao));
        Assert.Equal(texto, Encoding.UTF8.GetString(relido.Gravar()));
    }

    [Fact]
    public void PmpNovoDePc3JsonSaiEmJson()
    {
        var pc3 = Arquivo();
        var pmp = TamanhosPc3.NovoPmp(pc3.Raiz, @"C:\p\PMP Files\X.pmp");
        pmp.ParaJson();
        TamanhosPc3.Conciliar(pmp.Raiz, FormatosFolha.Padrao());
        var relido = ArquivoPia.Ler(pmp.Gravar());
        Assert.True(relido.Json);
        Assert.Equal(@"C:\p\PMP Files\X.pmp", TamanhosPc3.PmpLigado(relido.Raiz));
        Assert.Equal(FormatosFolha.Padrao().Count, TamanhosPc3.Listar(relido.Raiz).Count);
        Assert.EndsWith("\"subver\" : \"json\",\n \"version\" : 0.01\n}", Encoding.UTF8.GetString(pmp.Gravar()));
    }

    [Fact]
    public void TextoComAspasEBarraVoltaIgual()
    {
        var a = Arquivo();
        a.Raiz["meta"].DefinirTexto("driver_tag_line", "PDF \"x\" C:\\y é");
        var relido = ArquivoPia.Ler(a.Gravar());
        Assert.Equal("PDF \"x\" C:\\y é", relido.Raiz["meta"].LerTexto("driver_tag_line"));
    }

    [Theory]
    [InlineData("None", true)]
    [InlineData("Nenhum", true)]
    [InlineData("", true)]
    [InlineData("DWG To PDF.pc3", false)]
    [InlineData("PDF24", false)]
    public void NenhumaImpressoraFicaForaDaLista(string d, bool fora) => Assert.Equal(fora, ImpressorasPdf.EhNenhuma(d));
}
