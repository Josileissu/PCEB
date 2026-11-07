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
