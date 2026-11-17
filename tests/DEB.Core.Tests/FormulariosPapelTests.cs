using System.Collections.Generic;
using System.Linq;
using DEB.Core.Documentacao;
using Xunit;

namespace DEB.Core.Tests;

/// <summary>ARMPDFLOTE, "Criar folhas na impressora": plano dos formulários do Windows (nomes, ordem, decisões) e aplicação.</summary>
public class FormulariosPapelTests
{
    private static FormularioWindows U(string n, double l, double a) =>
        new FormularioWindows(n, FormulariosPapel.Milesimos(l), FormulariosPapel.Milesimos(a));

    /// <summary>Os forms que já estão certos neste computador, na ordem certa.</summary>
    private static List<FormularioWindows> Certos() =>
        FormatosFolha.Padrao().Select(f => U(f.Nome, f.Largura, f.Altura)).ToList();

    /// <summary>Servidor falso: guarda a lista na ordem do Windows (forms novos vão para o fim).</summary>
    private sealed class Servidor : IServidorFormularios
    {
        public readonly List<FormularioWindows> Forms;
        public readonly List<string> Chamadas = new List<string>();
        public int ErroEm = 0; // devolve este erro em toda chamada (0 = nunca)
        public Servidor(IEnumerable<FormularioWindows> f) { Forms = f.ToList(); }
        public int Apagar(string nome)
        {
            Chamadas.Add("-" + nome);
            if (ErroEm != 0) return ErroEm;
            return Forms.RemoveAll(f => f.Nome == nome) > 0 ? 0 : 1902;
        }
        public int Adicionar(string nome, int l, int a)
        {
            Chamadas.Add("+" + nome);
            if (ErroEm != 0) return ErroEm;
            if (Forms.Any(f => f.Nome == nome)) return 80;
            Forms.Add(new FormularioWindows(nome, l, a));
            return 0;
        }
    }

    [Fact]
    public void OrdemCrescente()
    {
        var embaralhado = FormatosFolha.Padrao().AsEnumerable().Reverse();
        Assert.Equal(FormatosFolha.Padrao().Select(f => f.Nome), FormulariosPapel.EmOrdem(embaralhado).Select(f => f.Nome));
    }

    [Fact]
    public void IgnoraInvalidosERepetidos()
    {
        var l = new[] { new FormatoFolha("X", 100, 100), new FormatoFolha("x", 200, 200), new FormatoFolha("", 10, 10), new FormatoFolha("Z", 0, 5) };
        var r = FormulariosPapel.EmOrdem(l);
        Assert.Single(r);
        Assert.Equal(100, r[0].Largura);
    }

    [Fact]
    public void TudoCertoNadaAFazer()
    {
        var existentes = new List<FormularioWindows> { new FormularioWindows("Letter", 215900, 279400, TipoFormulario.Interno) };
        existentes.AddRange(Certos());
        var plano = FormulariosPapel.Planejar(FormatosFolha.Padrao(), existentes, false);
        Assert.True(plano.NadaAFazer);
        Assert.All(plano.Passos, p => Assert.Equal(AcaoFormulario.Manter, p.Acao));
        Assert.Contains("Nada a fazer", plano.TextoConfirmacao());
    }

    [Fact]
    public void ComputadorNovoCriaTudoNaOrdem()
    {
        var srv = new Servidor(new FormularioWindows[0]);
        var plano = FormulariosPapel.Planejar(FormatosFolha.Padrao(), srv.Forms, false);
        Assert.All(plano.Passos, p => Assert.Equal(AcaoFormulario.Criar, p.Acao));
        var r = FormulariosPapel.Aplicar(plano, srv);
        Assert.Equal(9, r.Criados.Count);
        Assert.Empty(r.Erros);
        Assert.Equal(FormatosFolha.Padrao().Select(f => f.Nome), srv.Forms.Select(f => f.Nome));
        var a0mais = srv.Forms.Single(f => f.Nome == "A1+");
        Assert.Equal(911100, a0mais.Largura);
        Assert.Equal(594000, a0mais.Altura);
        Assert.True(a0mais.SemMargem);
        // depois de criar, o plano seguinte não tem nada a fazer
        Assert.True(FormulariosPapel.Planejar(FormatosFolha.Padrao(), srv.Forms, false).NadaAFazer);
    }

    [Fact]
    public void FaltandoNoMeioRecriaDaliEmDiante()
    {
        // existe tudo menos o A2: A4 e A3 ficam; do A2 em diante tudo vai para o fim, na ordem
        var srv = new Servidor(Certos().Where(f => f.Nome != "A2 (ISO A2)"));
        var plano = FormulariosPapel.Planejar(FormatosFolha.Padrao(), srv.Forms, false);
        Assert.Equal(new[] { "A4 (ISO A4)", "A3 (ISO A3)" }, plano.Com(AcaoFormulario.Manter).Select(p => p.Formato.Nome));
        Assert.Equal("A2 (ISO A2)", plano.Com(AcaoFormulario.Criar).Single().Formato.Nome);
        Assert.Equal(6, plano.Com(AcaoFormulario.Recriar).Count);
        Assert.Contains("ordem", plano.Com(AcaoFormulario.Recriar)[0].Motivo);
        Assert.Contains("PDF24", plano.TextoConfirmacao()); // aviso dos DWGs
        var r = FormulariosPapel.Aplicar(plano, srv);
        Assert.Single(r.Criados);
        Assert.Equal(6, r.Recriados.Count);
        Assert.Equal(FormatosFolha.Padrao().Select(f => f.Nome), srv.Forms.Select(f => f.Nome));
        // apaga e cria em seguida, um por um
        Assert.Equal(new[] { "+A2 (ISO A2)", "-A1 (ISO A1)", "+A1 (ISO A1)" }, srv.Chamadas.Take(3));
    }

    [Fact]
    public void ForaDeOrdemRecria()
    {
        var certos = Certos();
        var trocado = new List<FormularioWindows> { certos[1], certos[0] }; // A3 antes do A4
        trocado.AddRange(certos.Skip(2));
        var srv = new Servidor(trocado);
        var plano = FormulariosPapel.Planejar(FormatosFolha.Padrao(), srv.Forms, false);
        Assert.Equal("A4 (ISO A4)", plano.Com(AcaoFormulario.Manter).Single().Formato.Nome);
        Assert.Equal(8, plano.Com(AcaoFormulario.Recriar).Count);
        FormulariosPapel.Aplicar(plano, srv);
        Assert.Equal(FormatosFolha.Padrao().Select(f => f.Nome), srv.Forms.Select(f => f.Nome));
    }

    [Fact]
    public void TamanhoErradoOuMargemRecria()
    {
        var ex = Certos();
        ex[8] = U("A0++", 1486, 841);
        ex[7] = new FormularioWindows("A0+", 1337600, 841000) { Esquerda = 5000 };
        var plano = FormulariosPapel.Planejar(FormatosFolha.Padrao(), ex, false);
        Assert.Equal(new[] { "A0+", "A0++" }, plano.Com(AcaoFormulario.Recriar).Select(p => p.Formato.Nome));
        Assert.Contains("margem", plano.Passos[7].Motivo);
        Assert.Contains("1486", plano.Passos[8].Motivo);
    }

    [Fact]
    public void FormDoDriverComOutroTamanhoPedeAutorizacao()
    {
        // caso real: "A0" do driver em pé (841 × 1189)
        var ex = Certos().Where(f => f.Nome != "A0").ToList();
        ex.Insert(0, new FormularioWindows("A0", 841000, 1189000, TipoFormulario.Impressora));
        var sem = FormulariosPapel.Planejar(FormatosFolha.Padrao(), ex, false);
        Assert.Single(sem.ConflitosImpressora);
        var a0 = sem.Passos.Single(p => p.Formato.Nome == "A0");
        Assert.Equal(AcaoFormulario.Pular, a0.Acao);
        // A0+ e A0++ continuam certos (o A0 pulado não é form de usuário)
        Assert.Equal(AcaoFormulario.Manter, sem.Passos.Single(p => p.Formato.Nome == "A0++").Acao);

        var com = FormulariosPapel.Planejar(FormatosFolha.Padrao(), ex, true);
        Assert.Equal(AcaoFormulario.Substituir, com.Passos.Single(p => p.Formato.Nome == "A0").Acao);
        // o substituto vai para o fim: A0+ e A0++ são recriados depois dele
        Assert.Equal(new[] { "A0+", "A0++" }, com.Com(AcaoFormulario.Recriar).Select(p => p.Formato.Nome));
        var srv = new Servidor(ex);
        var r = FormulariosPapel.Aplicar(com, srv);
        Assert.Equal(new[] { "A0" }, r.Substituidos);
        var a0novo = srv.Forms.Single(f => f.Nome == "A0");
        Assert.Equal(TipoFormulario.Usuario, a0novo.Tipo);
        Assert.Equal(1189000, a0novo.Largura);
        Assert.Equal(FormatosFolha.Padrao().Select(f => f.Nome), srv.Forms.Select(f => f.Nome));
    }

    [Fact]
    public void FormDoDriverComMesmoTamanhoFica()
    {
        var ex = Certos();
        ex[6] = new FormularioWindows("A0", 1189000, 841000, TipoFormulario.Impressora);
        var plano = FormulariosPapel.Planejar(FormatosFolha.Padrao(), ex, false);
        Assert.Empty(plano.ConflitosImpressora);
        Assert.True(plano.NadaAFazer);
    }

    [Fact]
    public void FormDoWindowsNuncaMexe()
    {
        var ex = new List<FormularioWindows> { new FormularioWindows("A4 (ISO A4)", 210000, 297000, TipoFormulario.Interno),
                                               new FormularioWindows("A3 (ISO A3)", 297000, 420000, TipoFormulario.Interno) };
        var plano = FormulariosPapel.Planejar(FormatosFolha.Padrao(), ex, true);
        Assert.Equal(AcaoFormulario.Manter, plano.Passos[0].Acao);
        Assert.Equal(AcaoFormulario.Pular, plano.Passos[1].Acao);
        Assert.Contains("formulário do Windows", plano.Passos[1].Motivo);
        var srv = new Servidor(ex);
        FormulariosPapel.Aplicar(plano, srv);
        Assert.DoesNotContain(srv.Chamadas, c => c.Contains("A3") || c.Contains("A4"));
    }

    [Fact]
    public void SemPermissaoParaNoPrimeiro()
    {
        var srv = new Servidor(new FormularioWindows[0]) { ErroEm = 5 };
        var plano = FormulariosPapel.Planejar(FormatosFolha.Padrao(), srv.Forms, false);
        var r = FormulariosPapel.Aplicar(plano, srv);
        Assert.True(r.FaltouPermissao);
        Assert.Single(srv.Chamadas);
        Assert.Single(r.Erros);
        Assert.Equal(8, r.Pulados.Count);
        Assert.Contains("administrador", r.Texto());
    }

    [Fact]
    public void ListaDoUsuarioNoJson()
    {
        // quem editar o pdflote.json cria os seus
        var meus = new[] { new FormatoFolha("A0", 1189, 841), new FormatoFolha("Folha 50x70", 700, 500) };
        var plano = FormulariosPapel.Planejar(meus, new FormularioWindows[0], false);
        Assert.Equal(new[] { "Folha 50x70", "A0" }, plano.Passos.Select(p => p.Formato.Nome));
        Assert.Equal(700000, plano.Passos[0].Largura);
    }

    [Fact]
    public void InstrucoesDoPc3()
    {
        var t = FormulariosPapel.InstrucoesPc3(FormatosFolha.Padrao(), ImpressorasPdf.AutoCadPdf);
        Assert.Contains("Custom Paper Sizes", t);
        Assert.Contains("911,1", t);
        Assert.True(t.IndexOf("A4 (ISO A4)") < t.IndexOf("A0++"));
    }

    [Theory]
    [InlineData("PDF24", TipoImpressoraPdf.Pdf24)]
    [InlineData("AutoCAD PDF (High Quality Print).pc3", TipoImpressoraPdf.Pc3)]
    [InlineData("Microsoft Print to PDF", TipoImpressoraPdf.Outra)]
    [InlineData("", TipoImpressoraPdf.Outra)]
    public void TipoDaImpressora(string nome, TipoImpressoraPdf tipo) => Assert.Equal(tipo, ImpressorasPdf.Tipo(nome));

    [Fact]
    public void ImpressoraDoAutoCadLembradaAParte()
    {
        var arq = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "deb_pdflote_impressora.json");
        var c = new ConfigLotePdf();
        Assert.Equal("PDF24", c.ImpressoraLote);
        c.NoAutoCad = true;
        Assert.Equal(ImpressorasPdf.AutoCadPdf, c.ImpressoraLote);
        c.ImpressoraLote = "PDF24";
        c.Salvar(arq);
        var l = ConfigLotePdf.Carregar(arq);
        Assert.Equal("PDF24", l.ImpressoraAutoCad);
        Assert.Equal("PDF24", l.Impressora);
        System.IO.File.Delete(arq);
        // json antigo, sem o campo: padrão
        System.IO.File.WriteAllText(arq, "{\"Impressora\":\"PDF24\"}");
        Assert.Equal(ImpressorasPdf.AutoCadPdf, ConfigLotePdf.Carregar(arq).ImpressoraAutoCad);
        System.IO.File.Delete(arq);
    }
}
