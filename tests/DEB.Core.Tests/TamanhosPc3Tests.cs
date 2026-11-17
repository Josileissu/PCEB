using System.Linq;
using System.Text;
using DEB.Core.Documentacao;
using Xunit;

namespace DEB.Core.Tests;

/// <summary>Arquivos PIA (.pc3/.pmp) e folhas personalizadas do .pmp ("Criar folhas na impressora" com .pc3).</summary>
public class TamanhosPc3Tests
{
    // .pmp sintético no formato dos arquivos da Autodesk: "A1" com o tamanho do formato "A1 (ISO A1)", "A0" com medida errada,
    // uma folha "Previous paper size" que não casa com nada e um par com o código opcional entre parênteses
    private const string Pmp =
        "meta{\n user_defined_model_pathname=\"C:\\x\\Teste.pmp\n driver_pathname=\"C:\\drv\\pdfplot16.hdi\n locale=\"4B00409\n}\n" +
        "udm{\n calibration{\n  _x=1.0\n  _y=1.0\n }\n media{\n  size{\n" +
        "   0{\n    caps_type=2\n    name=\"UserDefinedMetric (841.00 x 594.00MM)\n    localized_name=\"A1\n" +
        "    media_description_name=\"UserDefinedMetric Landscape 841.00W x 594.00H - (0, 0) x (841, 594) =499554 MM\n    media_group=15\n    landscape_mode=TRUE\n   }\n" +
        "   1{\n    caps_type=2\n    name=\"UserDefinedMetric (1180.00 x 841.00MM)\n    localized_name=\"A0\n" +
        "    media_description_name=\"UserDefinedMetric Landscape 1180.00W x 841.00H - (0, 0) x (1180, 841) =992380 MM\n    media_group=15\n    landscape_mode=TRUE\n   }\n" +
        "   2{\n    caps_type=2\n    name=\"User-1\n    localized_name=\"Previous paper size (1121.00 x 594.00 mm)\n    media_description_name=\"User-1\n    media_group=15\n    landscape_mode=TRUE\n   }\n" +
        "  }\n  description{\n" +
        "   0{\n    caps_type=2\n    name=\"UserDefinedMetric Landscape 841.00W x 594.00H - (0, 0) x (841, 594) =499554 MM\n    media_bounds_urx=841.0\n    media_bounds_ury=594.0\n   }\n" +
        "   1{\n    caps_type=2\n    name=\"UserDefinedMetric Landscape 1180.00W x 841.00H - (0, 0) x (1180, 841) =992380 MM\n    media_bounds_urx=1180.0\n    media_bounds_ury=841.0\n   }\n" +
        "   2{\n    caps_type=2\n    name=\"User-1\n    media_bounds_urx=1121.0\n    media_bounds_ury=594.0\n    printable_area=665874.00000 (     \"12)$&!?0)\n   }\n" +
        "  }\n }\n}\n\0";

    private static ArquivoPia Arquivo() =>
        ArquivoPia.Ler(ArquivoPia.Compactar(ArquivoPia.CabecalhoPc3, Encoding.ASCII.GetBytes(Pmp)));

    [Fact]
    public void IdaEVoltaDoTextoEhExata()
    {
        var a = Arquivo();
        Assert.Equal(Pmp, a.Texto());
        Assert.True(a.TerminaEmNulo);
        Assert.Equal(Pmp, Encoding.ASCII.GetString(ArquivoPia.Descompactar(a.Gravar(), out var cab)));
        Assert.Equal(ArquivoPia.CabecalhoPc3, cab);
        Assert.False(ArquivoPia.EhPia(new byte[] { 0xFF, 0xFE, 0xFF, 0x0E, 0x50 }));  // .pc3 binário do GstarCAD
    }

    [Fact]
    public void ListaFolhasComMedidas()
    {
        var t = TamanhosPc3.Listar(Arquivo().Raiz);
        Assert.Equal(3, t.Count);
        Assert.Equal("A1", t[0].NomeLocal);
        Assert.Equal(841, t[0].Largura);
        Assert.Equal(1121, t[2].Largura);
        Assert.Equal(1121.3000488, TamanhosPc3.LerNum("1121.3000488 (    0#.%D4\"2?@)"), 7);
    }

    [Fact]
    public void ConciliaSemDuplicar()
    {
        var a = Arquivo();
        var formatos = FormatosFolha.Padrao();
        var passos = TamanhosPc3.Conciliar(a.Raiz, formatos);
        Assert.Equal(9, passos.Count);
        Assert.Equal(AcaoTamanhoPc3.Renomear, passos.Single(p => p.Formato.Nome == "A1 (ISO A1)").Acao);
        Assert.Equal(AcaoTamanhoPc3.Ajustar, passos.Single(p => p.Formato.Nome == "A0").Acao);
        // a folha antiga "Previous paper size (1121 x 594)" está a menos de 1 mm do A1++ (1121,3 x 594): reaproveitada
        Assert.Equal(AcaoTamanhoPc3.Renomear, passos.Single(p => p.Formato.Nome == "A1++").Acao);
        Assert.Equal(6, passos.Count(p => p.Acao == AcaoTamanhoPc3.Criar));
        var t = TamanhosPc3.Listar(a.Raiz);
        Assert.Equal(3 + 6, t.Count);                         // nada duplicado
        Assert.DoesNotContain(t, x => x.NomeLocal.StartsWith("Previous"));
        var a0 = t.Single(x => x.NomeLocal == "A0");
        Assert.Equal(1189, a0.Largura);
        Assert.Equal("UserDefinedMetric (1189.00 x 841.00MM)", a0.Canonico);
        var a4 = t.Single(x => x.NomeLocal == "A4 (ISO A4)");    // gravada deitada, como o assistente da Autodesk
        Assert.Equal(297, a4.Largura);
        Assert.Equal(210, a4.Altura);
        var txt = a.Texto();
        Assert.Contains("    name=\"UserDefinedMetric Landscape 1486.30W x 841.00H - (0, 0) x (1486, 841) =1249978 MM\n    media_bounds_urx=1486.3\n", txt);
        Assert.Contains("    printable_area=1249978.3", txt);
        // de novo: tudo mantido
        var outra = TamanhosPc3.Conciliar(a.Raiz, formatos);
        Assert.All(outra, p => Assert.Equal(AcaoTamanhoPc3.Manter, p.Acao));
        Assert.Contains("já tinha todas", TamanhosPc3.Resumo(outra, "X.pc3"));
    }

    [Fact]
    public void ToleranciaDeUmMmEmQualquerOrientacao()
    {
        Assert.True(TamanhosPc3.MesmoTamanho(1486.25, 841, 1486.3, 841));
        Assert.True(TamanhosPc3.MesmoTamanho(911.08, 594, 911.1, 594));
        Assert.False(TamanhosPc3.MesmoTamanho(1180, 841, 1189, 841));
        // A4 em pé na lista × A4 deitado no .pmp: reaproveita
        var a = Arquivo();
        var s = a.Raiz["udm"]["media"]["size"].Filhos[0];
        s.DefinirTexto("localized_name", "Meu A4");
        var d = a.Raiz["udm"]["media"]["description"].Filhos[0];
        d.Definir("media_bounds_urx", "297.0"); d.Definir("media_bounds_ury", "210.0");
        var p = TamanhosPc3.Conciliar(a.Raiz, new[] { new FormatoFolha("A4 (ISO A4)", 210, 297) });
        Assert.Equal(AcaoTamanhoPc3.Renomear, p[0].Acao);
        Assert.Equal("Meu A4", p[0].Antes);
    }

    [Fact]
    public void NaoRenomeiaFolhaQueJaTemNomeDeOutroFormato()
    {
        // "A1" (841 × 594) já é o nome de um formato da lista: o formato "Outro A1" do mesmo tamanho é criado à parte
        var a = Arquivo();
        var lista = new[] { new FormatoFolha("A1", 841, 594), new FormatoFolha("Outro A1", 841, 594) };
        var p = TamanhosPc3.Conciliar(a.Raiz, lista);
        Assert.Equal(AcaoTamanhoPc3.Manter, p[0].Acao);
        Assert.Equal(AcaoTamanhoPc3.Criar, p[1].Acao);
    }

    [Fact]
    public void PmpNovoLevaOMetaDoPc3EOCaminho()
    {
        var pc3 = NoPia.Analisar("meta{\n user_defined_model_pathname=\"\n driver_pathname=\"C:\\drv\\pdfplot16.hdi\n config_description=\"\n config_autospool=FALSE\n}\nmedia{\n x=1\n}\n");
        Assert.Equal("", TamanhosPc3.PmpLigado(pc3));
        var pmp = TamanhosPc3.NovoPmp(pc3, @"C:\p\PMP Files\X.pmp");
        TamanhosPc3.Ligar(pc3, @"C:\p\PMP Files\X.pmp");
        Assert.Equal(@"C:\p\PMP Files\X.pmp", TamanhosPc3.PmpLigado(pc3));
        Assert.Null(pmp.Raiz["meta"]["config_description"]);
        Assert.Equal(@"C:\p\PMP Files\X.pmp", pmp.Raiz["meta"].LerTexto("user_defined_model_pathname"));
        TamanhosPc3.Conciliar(pmp.Raiz, FormatosFolha.Padrao());
        Assert.Equal(9, TamanhosPc3.Listar(ArquivoPia.Ler(pmp.Gravar()).Raiz).Count);
    }

    [Theory]
    [InlineData(841.0, "841.0")]
    [InlineData(1486.3, "1486.3")]
    [InlineData(1121.25, "1121.25")]
    public void NumeroNoFormatoDoPmp(double v, string esperado) => Assert.Equal(esperado, TamanhosPc3.Num(v));
}
