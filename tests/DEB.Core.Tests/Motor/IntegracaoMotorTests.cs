using System;
using System.IO;
using System.Linq;
using DEB.Core.Motor;
using DEB.Core.Motor.Entrada;
using DEB.Core.Motor.Fundacoes;
using DEB.Core.Motor.Json;
using DEB.Core.Motor.Perfis;
using Xunit;

namespace DEB.Core.Tests.Motor;

/// <summary>Frentes A + B + D juntas: planilha → elementos → motor (perfil de fábrica) → modelo → JSON.</summary>
[Collection("Pilar")]
public class IntegracaoMotorTests
{
    [Fact]
    public void PlanilhaGeraOsBlocosComOPerfilDeFabrica()
    {
        var imp = ImportadorBlocos.Importar(ImportadorBlocos.GerarModeloPlanilha());
        Assert.Empty(imp.Erros);
        Assert.Equal(new[] { "B28", "B15" }, imp.Blocos.Select(b => b.Nome));

        var perfil = PerfisFabrica.Obter("bpm_bloco");
        var cat = new CatalogoMotores().Registrar(new MotorBlocoEstacas());
        var sobrescritas = new System.Collections.Generic.List<Sobrescrita>();

        var b28 = cat.Gerar(imp.Blocos[0], perfil, sobrescritas);
        var p = b28.Posicao("principal_x");
        Assert.Equal(16, p.Bitola);
        Assert.Equal(46, p.Parametros["perna"], 1);
        Assert.NotNull(b28.Posicao("calice_topo"));

        var b15 = cat.Gerar(imp.Blocos[1], perfil, sobrescritas);
        Assert.Equal(3, b15.Posicoes.Count(x => x.Chave.StartsWith("principal_lado")));

        // edição na produção: perna sobrescrita e modelo gravado/relido em JSON
        Sobrescritas.Definir(sobrescritas, "principal_x", "perna", 40);
        var editado = cat.Gerar(imp.Blocos[0], perfil, sobrescritas);
        Assert.Contains("perna", editado.Posicao("principal_x").Manuais);
        var relido = SerializacaoMotor.LerModelo(SerializacaoMotor.GravarModelo(editado));
        var pr = relido.Posicao("principal_x");
        Assert.Equal(40, pr.Parametros["perna"], 6);
        Assert.Equal(editado.Posicao("principal_x").Barras.Count, pr.Barras.Count);
        Assert.Equal(editado.Posicoes.Sum(x => x.Sobrepostos.Count), relido.Posicoes.Sum(x => x.Sobrepostos.Count));
    }

    [Fact]
    public void PerfilDoUsuarioMudaSoAEscala()
    {
        var pasta = Path.Combine(Path.GetTempPath(), "deb_perfil_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(pasta);
        try
        {
            File.WriteAllText(Path.Combine(pasta, "bpm_bloco.json"), "{\"versao\": 1, \"folha\": {\"escala\": 25}}");
            var r = new RepositorioPerfis(pasta).Obter("bpm_bloco");
            var fab = PerfisFabrica.Obter("bpm_bloco");
            Assert.Equal(25, r.Perfil.Folha.Escala);
            Assert.Equal(fab.Folha.Vistas.Count, r.Perfil.Folha.Vistas.Count);
            Assert.Equal(fab.Folha.Notas, r.Perfil.Folha.Notas);
            Assert.Equal(100, r.Perfil.Padrao("fundo_calice.comprimento", 0));
        }
        finally { Directory.Delete(pasta, true); }
    }
}

/// <summary>Ponta a ponta até a prancha: planilha → motor → prancha (perfil de fábrica BPM), comparado com o template atual.</summary>
[Collection("Pilar")]
public class IntegracaoPranchaTests
{
    [Fact]
    public void B28PeloMotorTemAsMesmasBarrasDoTemplate()
    {
        var b28 = DEB.Core.Motor.Entrada.ImportadorBlocos.ExemploB28();
        var perfil = DEB.Core.Motor.Perfis.PerfisFabrica.Obter("bpm_bloco");
        var modelo = new DEB.Core.Motor.Fundacoes.MotorBlocoEstacas().Gerar(b28, perfil, new System.Collections.Generic.List<Sobrescrita>());
        var r = new DEB.Core.Motor.Prancha.GeradorPrancha().Gerar(b28, modelo, perfil);

        Assert.Equal(modelo.Posicoes.Count, r.Barras.Count);
        Assert.Equal(r.Barras.Count, r.Barras.Select(b => b.Nome).Distinct().Count());
        Assert.All(r.Faixas, f => Assert.Contains(r.Barras, b => b.Nome == f.Barra));
        Assert.Contains(r.Textos, t => t.Texto == "BLOCO B28 - 6x - 2 ø 40");
        Assert.Contains(r.Cotas, k => Math.Abs(Math.Abs(k.P2.X - k.P1.X) - 190) < 1e-6);
        Assert.Contains(r.Tabelas, t => t.Titulo.StartsWith("INFORMAÇÕES"));

        // aço total igual ao do template atual (mesmas entradas)
        var t0 = new DEB.Core.Templates.Blocos.TemplateBlocoEstacas();
        var v = new DEB.Core.Templates.ValoresTemplate(t0.Parametros);
        DEB.Core.Motor.Entrada.ConversorBloco.ParaValores(b28, v);
        var antigo = t0.Gerar(v, new DEB.Core.Configuracao.ConfiguracaoArmadura());
        double Peso(System.Collections.Generic.IEnumerable<DEB.Core.Templates.BarraTemplate> bs) =>
            bs.Sum(b => b.Bitola * b.Bitola * b.Quantidade * Math.Max(1, b.Repeticoes));
        Assert.Equal(Peso(antigo.Barras), Peso(r.Barras), 0);
    }

    [Fact]
    public void PesoDaProducaoUsaAsRegrasDaRA()
    {
        var cfg = new DEB.Core.Configuracao.ConfiguracaoArmadura();
        var b28 = DEB.Core.Motor.Entrada.ImportadorBlocos.ExemploB28();
        var perfil = DEB.Core.Motor.Perfis.PerfisFabrica.Obter("bpm_bloco");
        var modelo = new DEB.Core.Motor.Fundacoes.MotorBlocoEstacas().Gerar(b28, perfil, new System.Collections.Generic.List<Sobrescrita>());
        var r = new DEB.Core.Motor.Prancha.GeradorPrancha(cfg).Gerar(b28, modelo, perfil);
        double esperado = r.Barras.Sum(b => DEB.Core.Templates.MedidaBarra.Peso(b, cfg));
        Assert.Contains(r.Tabelas.SelectMany(t => t.Linhas), l => l.Length > 1 && l[1] == FormatoMotor_NumV(Math.Round(esperado, 1)) + " kg");

        // o template antigo mostra o mesmo peso (mesmas barras, mesmas regras)
        var t0 = new DEB.Core.Templates.Blocos.TemplateBlocoEstacas();
        var v = new DEB.Core.Templates.ValoresTemplate(t0.Parametros);
        DEB.Core.Motor.Entrada.ConversorBloco.ParaValores(b28, v);
        var antigo = t0.Gerar(v, cfg);
        double pesoAntigo = antigo.Barras.Sum(b => DEB.Core.Templates.MedidaBarra.Peso(b, cfg));
        Assert.Contains(antigo.Tabelas.SelectMany(t => t.Linhas), l => l.Length > 1 && l[1] == FormatoMotor_NumV(Math.Round(pesoAntigo, 1)) + " kg");
    }

    private static string FormatoMotor_NumV(double x) => DEB.Core.Motor.Componentes.FormatoMotor.NumV(x);
}

[Collection("Pilar")]
public class PreviaPranchaMotorB28
{
    /// <summary>Com ARM_PREVIA definida, grava a prancha do B28 gerada pelo motor (para conferência visual).</summary>
    [Fact]
    public void Gravar()
    {
        var pasta = Environment.GetEnvironmentVariable("ARM_PREVIA");
        if (string.IsNullOrEmpty(pasta)) return;
        var b28 = DEB.Core.Motor.Entrada.ImportadorBlocos.ExemploB28();
        var perfil = DEB.Core.Motor.Perfis.PerfisFabrica.Obter("bpm_bloco");
        var modelo = new DEB.Core.Motor.Fundacoes.MotorBlocoEstacas().Gerar(b28, perfil, new System.Collections.Generic.List<Sobrescrita>());
        var r = new DEB.Core.Motor.Prancha.GeradorPrancha().Gerar(b28, modelo, perfil);
        GeradorPranchaTests.Gravar(pasta, "previa_motor_b28.json", r);
    }
}
