using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using DEB.Core.Documentacao;
using Xunit;

namespace DEB.Core.Tests;

/// <summary>ARMPDFLOTE: PCEB.ctb (gerador e escolha), tradução dos erros e folhas desenhadas no Model.</summary>
public class LotePdfCtbErrosTests
{
    // ---------------- PCEB.ctb ----------------

    [Fact]
    public void TextoComDescricaoVaziaEhIgualAoCtbOriginal()
    {
        // o original (fora do repositório) tem a descrição vazia: o texto gerado tem de ser byte a byte o mesmo
        var bytes = Encoding.ASCII.GetBytes(EstiloPlotagemPadrao.Texto(""));
        using var sha = SHA256.Create();
        var hex = string.Concat(sha.ComputeHash(bytes).Select(b => b.ToString("x2")));
        Assert.Equal(EstiloPlotagemPadrao.Sha256Original, hex);
        Assert.Equal(84244, bytes.Length);
    }

    [Fact]
    public void ArquivoTemCabecalhoTamanhosEAdlerCorretos()
    {
        var arq = EstiloPlotagemPadrao.Gerar();
        var cab = Encoding.ASCII.GetBytes(EstiloPlotagemPadrao.Cabecalho);
        Assert.Equal(48, cab.Length);
        Assert.True(arq.Take(48).SequenceEqual(cab));
        uint adler = BitConverter.ToUInt32(arq, 48), tam = BitConverter.ToUInt32(arq, 52), tamZ = BitConverter.ToUInt32(arq, 56);
        Assert.Equal((uint)(arq.Length - 60), tamZ);
        Assert.Equal(Zlib.Adler32(arq, 60, (int)tamZ), adler);   // Adler-32 dos bytes COMPRIMIDOS
        Assert.Equal(0x78, arq[60]);
        Assert.Equal(0xDA, arq[61]);
        var texto = EstiloPlotagemPadrao.Ler(arq);
        Assert.Equal((uint)texto.Length, tam);
    }

    [Fact]
    public void RoundTripDoArquivoDevolveOTexto()
    {
        var texto = EstiloPlotagemPadrao.Texto();
        var lido = Encoding.ASCII.GetString(EstiloPlotagemPadrao.Ler(EstiloPlotagemPadrao.Gerar()));
        Assert.Equal(texto, lido);
        Assert.EndsWith("}\n\0", lido);
    }

    [Fact]
    public void ZlibRoundTripEAdlerConhecido()
    {
        Assert.Equal(0x11E60398u, Zlib.Adler32(Encoding.ASCII.GetBytes("Wikipedia"), 0, 9)); // valor da RFC/Wikipedia
        var rnd = new Random(7);
        var dados = new byte[200_000];
        rnd.NextBytes(dados);
        Assert.True(Zlib.Descomprimir(Zlib.Comprimir(dados)).SequenceEqual(dados));
        var z = Zlib.Comprimir(dados);
        z[z.Length - 1] ^= 0xFF;
        Assert.Throws<InvalidDataException>(() => Zlib.Descomprimir(z));
    }

    [Fact]
    public void Tem255CoresEDescricaoNeutra()
    {
        var t = EstiloPlotagemPadrao.Texto();
        Assert.StartsWith("description=\"" + EstiloPlotagemPadrao.DescricaoPadrao + "\n", t);
        Assert.True(t.All(c => c < 128), "o texto do ctb tem de ser ASCII");
        int i = t.IndexOf("plot_style{", StringComparison.Ordinal);
        Assert.Equal(255, CountOf(t.Substring(i), "  name=\"Color_"));
        Assert.Equal(255, CountOf(t, "  lineweight=0\n"));
        Assert.Contains(" 254{\n  name=\"Color_255\n", t);
        Assert.Contains("custom_lineweight_table{\n 0=0.0\n", t);
        Assert.Contains(" 26=2.11\n}\n", t);
        // preto, exceto as cores listadas
        Assert.Equal(unchecked((int)0xC3000000), EstiloPlotagemPadrao.PenaDa(7).ValorCor);
        Assert.Equal(-1006698496, EstiloPlotagemPadrao.PenaDa(1).ValorCor);
        Assert.Equal(1, EstiloPlotagemPadrao.PenaDa(255).Politica);
        Assert.Equal(244, Enumerable.Range(1, 255).Count(a => EstiloPlotagemPadrao.PenaDa(a).Rgb == 0));
    }

    private static int CountOf(string t, string s)
    {
        int n = 0;
        for (int i = t.IndexOf(s, StringComparison.Ordinal); i >= 0; i = t.IndexOf(s, i + 1, StringComparison.Ordinal)) n++;
        return n;
    }

    [Fact]
    public void LerRecusaArquivoEstragado()
    {
        var arq = EstiloPlotagemPadrao.Gerar();
        arq[100] ^= 0x55;
        Assert.Throws<InvalidDataException>(() => EstiloPlotagemPadrao.Ler(arq));
        Assert.Throws<InvalidDataException>(() => EstiloPlotagemPadrao.Ler(Encoding.ASCII.GetBytes("qualquer coisa")));
    }

    [Fact]
    public void GarantirNaPastaNaoSobrescreve()
    {
        var pasta = Path.Combine(Path.GetTempPath(), "DEB_teste_ctb_" + Guid.NewGuid().ToString("N").Substring(0, 8));
        try
        {
            var (c1, criado1) = EstiloPlotagemPadrao.GarantirNaPasta(pasta);
            Assert.True(criado1);
            Assert.Equal(EstiloPlotagemPadrao.Gerar(), File.ReadAllBytes(c1));
            File.WriteAllText(c1, "editado pelo usuário");
            var (c2, criado2) = EstiloPlotagemPadrao.GarantirNaPasta(pasta);
            Assert.False(criado2);
            Assert.Equal(c1, c2);
            Assert.Equal("editado pelo usuário", File.ReadAllText(c2));
        }
        finally { try { Directory.Delete(pasta, true); } catch { } }
    }

    [Theory]
    [InlineData("monochrome.ctb", "", "monochrome.ctb", false)]
    [InlineData("", "monochrome.ctb", "monochrome.ctb", false)]   // "o de cada layout"
    [InlineData("escritorio.ctb", "", "PCEB.ctb", true)]         // não existe neste PC
    [InlineData("", "escritorio.ctb", "PCEB.ctb", true)]          // page setup aponta para ctb que não existe
    [InlineData("", "", "PCEB.ctb", true)]                         // folha sem ctb
    [InlineData("PCEB.ctb", "", "PCEB.ctb", false)]               // ainda não gravado: o lote grava
    [InlineData("MONOCHROME.CTB", "", "monochrome.ctb", false)]
    public void EscolheCtbOuCaiNoPadrao(string cfg, string layout, string esperado, bool aviso)
    {
        var (estilo, av) = EstiloPlotagemPadrao.Escolher(cfg, layout, new[] { "monochrome.ctb", "acad.ctb" });
        Assert.Equal(esperado, estilo);
        Assert.Equal(aviso, av != null);
        if (aviso && cfg.Length + layout.Length > 0) Assert.Contains("não encontrado", av);
    }

    // ---------------- erros ----------------

    [Fact]
    public void ErroConhecidoMantemCausaEMensagem()
    {
        var d = ErrosLotePdf.Classificar(new ErroLotePdfException(CausaErroPdf.PapelInexistente, "papel 'A0+' não existe na impressora"), EtapaLotePdf.Configurar);
        Assert.Equal(CausaErroPdf.PapelInexistente, d.Causa);
        Assert.Equal("papel 'A0+' não existe na impressora", d.Mensagem);
        Assert.Contains("Criar folhas na impressora", d.Dica);
    }

    [Theory]
    [InlineData(unchecked((int)0x80070020), EtapaLotePdf.Copiar, CausaErroPdf.PdfDestinoAberto)]
    [InlineData(unchecked((int)0x80070020), EtapaLotePdf.Abrir, CausaErroPdf.DwgEmUso)]
    [InlineData(unchecked((int)0x80070070), EtapaLotePdf.Copiar, CausaErroPdf.DiscoCheio)]
    [InlineData(unchecked((int)0x80010001), EtapaLotePdf.Plotar, CausaErroPdf.CadOcupado)]
    [InlineData(unchecked((int)0x80070709), EtapaLotePdf.Configurar, CausaErroPdf.ImpressoraInexistente)]
    public void ClassificaPorHResult(int hr, EtapaLotePdf etapa, CausaErroPdf esperado)
    {
        var ex = new IOException("falha", hr);
        var d = ErrosLotePdf.Classificar(ex, etapa);
        Assert.Equal(esperado, d.Causa);
        Assert.Equal(hr, d.HResult);
        Assert.Contains("0x" + hr.ToString("X8"), d.Detalhado("a.dwg", "Layout1", new DateTime(2026, 11, 17, 10, 0, 0)));
    }

    [Fact]
    public void ClassificaComInternaEWin32()
    {
        var com = new System.Runtime.InteropServices.COMException("Call was rejected by callee.", unchecked((int)0x80010001));
        Assert.Equal(CausaErroPdf.CadOcupado, ErrosLotePdf.Classificar(new InvalidOperationException("x", com), EtapaLotePdf.Plotar).Causa);
        var w = new Win32Exception(1801);
        var d = ErrosLotePdf.Classificar(w, EtapaLotePdf.Configurar);
        Assert.Equal(CausaErroPdf.ImpressoraInexistente, d.Causa);
        Assert.Equal(1801, d.CodigoWin32);
    }

    [Theory]
    [InlineData("eFileSharingViolation", EtapaLotePdf.Abrir, CausaErroPdf.DwgEmUso)]
    [InlineData("eDwgNeedsRecovery", EtapaLotePdf.Abrir, CausaErroPdf.DwgCorrompido)]
    [InlineData("eInvalidInput", EtapaLotePdf.Abrir, CausaErroPdf.DwgNaoAbre)]
    [InlineData("eInvalidInput", EtapaLotePdf.Plotar, CausaErroPdf.PlotagemFalhou)]
    [InlineData("eInvalidInput", EtapaLotePdf.Copiar, CausaErroPdf.Desconhecida)]
    public void ClassificaErrorStatusDoCadPelaMensagem(string msg, EtapaLotePdf etapa, CausaErroPdf esperado) =>
        Assert.Equal(esperado, ErrosLotePdf.Classificar(new Exception(msg), etapa).Causa);

    [Fact]
    public void ClassificaEsGerais()
    {
        Assert.Equal(CausaErroPdf.DestinoSemPermissao, ErrosLotePdf.Classificar(new UnauthorizedAccessException("Access denied"), EtapaLotePdf.Copiar).Causa);
        Assert.Equal(CausaErroPdf.DestinoInexistente, ErrosLotePdf.Classificar(new DirectoryNotFoundException("x"), EtapaLotePdf.Copiar).Causa);
        Assert.Equal(CausaErroPdf.PdfNaoGerado, ErrosLotePdf.Classificar(new TimeoutException("x"), EtapaLotePdf.Esperar).Causa);
        Assert.Equal(CausaErroPdf.DwgNaoAbre, ErrosLotePdf.Classificar(new FileNotFoundException("x"), EtapaLotePdf.Abrir).Causa);
        var nada = ErrosLotePdf.Classificar(null, EtapaLotePdf.Plotar);
        Assert.Equal(CausaErroPdf.Desconhecida, nada.Causa);
        Assert.False(string.IsNullOrEmpty(nada.Mensagem));
    }

    [Fact]
    public void TodaCausaTemMensagemEmPortugues()
    {
        foreach (CausaErroPdf c in Enum.GetValues(typeof(CausaErroPdf)))
            Assert.False(string.IsNullOrWhiteSpace(ErrosLotePdf.Texto(c).Mensagem), c.ToString());
        Assert.Equal("nenhuma moldura/carimbo encontrado no Model nem nos layouts", ErrosLotePdf.Texto(CausaErroPdf.SemMoldura).Mensagem);
    }

    [Fact]
    public void DetalheTemEtapaArquivoFolhaEPilha()
    {
        Exception ex;
        try { throw new IOException("O arquivo está sendo usado por outro processo", unchecked((int)0x80070020)); }
        catch (Exception e) { ex = e; }
        var d = ErrosLotePdf.Classificar(ex, EtapaLotePdf.Copiar);
        var t = d.Detalhado(@"C:\obra\x.dwg", "Layout1", new DateTime(2026, 11, 17, 9, 5, 3));
        Assert.Contains("2026-11-17 09:05:03", t);
        Assert.Contains(@"C:\obra\x.dwg", t);
        Assert.Contains("Layout1", t);
        Assert.Contains("copiar o PDF para o destino", t);
        Assert.Contains("System.IO.IOException", t);
        Assert.Contains("Pilha:", t);
        Assert.Equal("o PDF de destino está aberto em outro programa", d.Mensagem);
    }

    [Fact]
    public void ResumoAgrupaPorCausa()
    {
        var erros = new[]
        {
            ErrosLotePdf.De(CausaErroPdf.PapelInexistente, EtapaLotePdf.Configurar),
            ErrosLotePdf.De(CausaErroPdf.PapelInexistente, EtapaLotePdf.Configurar),
            ErrosLotePdf.De(CausaErroPdf.DwgNaoAbre, EtapaLotePdf.Abrir),
        };
        var r = ErrosLotePdf.ResumoPorCausa(erros);
        Assert.Equal(2, r.Count);
        Assert.StartsWith("2× o papel não existe na impressora", r[0]);
    }

    // ---------------- folhas no Model ----------------

    [Theory]
    [InlineData(841, 594, "A1 (ISO A1)", 1.0)]          // mm, 1:1
    [InlineData(4205, 2970, "A1 (ISO A1)", 0.2)]        // cm, 1:50
    [InlineData(59450, 42050, "A0", 0.02)]              // mm, 1:50
    [InlineData(14863, 8410, "A0++", 0.1)]              // cm, 1:100? não: mm 1:10 → 0,1 mm/unidade
    [InlineData(148.63, 84.1, "A0++", 10.0)]            // cm, 1:1 (como o carimbo do layout)
    public void ReconheceMolduraNoModelEmEscala(double l, double a, string esperado, double fator)
    {
        var r = FolhasModeloPdf.Reconhecer(l, a, FormatosFolha.Padrao());
        Assert.Equal(esperado, r.Formato?.Nome);
        Assert.Equal(fator, r.Fator, 6);
    }

    [Fact]
    public void ModelSemFormatoNaoReconhece() =>
        Assert.Null(FolhasModeloPdf.Reconhecer(1000, 1000, FormatosFolha.Padrao()).Formato);

    [Fact]
    public void SelecionaMoldurasExternasEDescartaPequenas()
    {
        var c = new[]
        {
            new CandidatoMoldura(new CaixaPdf(0, 0, 4205, 2970), "bloco CARIMBO"),        // A1 1:50 (cm)
            new CandidatoMoldura(new CaixaPdf(10, 10, 4195, 2960), "polilinha no bloco"),  // dentro do carimbo
            new CandidatoMoldura(new CaixaPdf(5000, 0, 9205, 2970), "polilinha"),          // 2ª folha à direita
            new CandidatoMoldura(new CaixaPdf(0, 4000, 4205, 6970), "polilinha"),          // folha de cima
            new CandidatoMoldura(new CaixaPdf(100, 100, 142, 129.7), "polilinha"),          // seção 42 x 29,7: A3 1:1 cm, mas dentro da folha
            new CandidatoMoldura(new CaixaPdf(-900, -900, -858, -870.3), "polilinha"),      // idem, solta: pequena demais
            new CandidatoMoldura(new CaixaPdf(0, 0, 1000, 1000), "polilinha"),              // quadrado: nenhum formato
        };
        var m = FolhasModeloPdf.Selecionar(c, FormatosFolha.Padrao());
        Assert.Equal(3, m.Count);
        Assert.Equal(4000, m[0].Caixa.Y0);      // de cima para baixo
        Assert.Equal(0, m[1].Caixa.X0);         // depois da esquerda para a direita
        Assert.Equal("bloco CARIMBO", m[1].Origem);
        Assert.Equal(5000, m[2].Caixa.X0);
        Assert.All(m, x => Assert.Equal("A1 (ISO A1)", x.Reconhecido.Formato.Nome));
    }

    [Fact]
    public void RegraLayoutComMolduraIgnoraModel()
    {
        var lay = new[]
        {
            new LayoutLido { Nome = "Layout1", Reconhecido = true },
            new LayoutLido { Nome = "Layout2", Vazio = true },
            new LayoutLido { Nome = "Rascunho" },
        };
        Assert.False(FolhasModeloPdf.PrecisaModelo(lay));
        var p = FolhasModeloPdf.Decidir(lay, new[] { new MolduraReconhecida() });
        Assert.False(p.UsarModelo);
        Assert.False(p.SemMoldura);
        Assert.Contains("Layout1", p.LayoutsMarcados);
        Assert.Contains("Rascunho", p.LayoutsMarcados);
        Assert.DoesNotContain("Layout2", p.LayoutsMarcados);
    }

    [Fact]
    public void RegraSemLayoutUsaModel()
    {
        Assert.True(FolhasModeloPdf.PrecisaModelo(Array.Empty<LayoutLido>()));
        var p = FolhasModeloPdf.Decidir(Array.Empty<LayoutLido>(), new[] { new MolduraReconhecida(), new MolduraReconhecida() });
        Assert.True(p.UsarModelo);
        Assert.Empty(p.LayoutsMarcados);
        // layouts vazios (só viewport) ou sem moldura: também o Model
        var p2 = FolhasModeloPdf.Decidir(new[] { new LayoutLido { Nome = "Layout1", Vazio = true }, new LayoutLido { Nome = "X" } }, new[] { new MolduraReconhecida() });
        Assert.True(p2.UsarModelo);
        Assert.Empty(p2.LayoutsMarcados);
        Assert.Equal("Model 2", FolhasModeloPdf.Rotulo(2, 3));
        Assert.Equal("Model", FolhasModeloPdf.Rotulo(1, 1));
    }

    [Fact]
    public void RegraSemMolduraEmLugarNenhum()
    {
        var p = FolhasModeloPdf.Decidir(new[] { new LayoutLido { Nome = "Layout1", Vazio = true } }, Array.Empty<MolduraReconhecida>());
        Assert.True(p.SemMoldura);
        Assert.False(p.UsarModelo);
        Assert.True(FolhasModeloPdf.Decidir(null, null).SemMoldura);
        // layout com conteúdo mas sem moldura: entra com "?" (não é erro)
        var p2 = FolhasModeloPdf.Decidir(new[] { new LayoutLido { Nome = "Layout1" } }, Array.Empty<MolduraReconhecida>());
        Assert.False(p2.SemMoldura);
        Assert.Contains("Layout1", p2.LayoutsMarcados);
    }
}
