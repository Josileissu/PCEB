using System;
using System.Collections.Generic;
using System.Linq;
using DEB.Core.Motor;
using DEB.Core.Motor.Componentes;
using DEB.Core.Motor.Entrada;
using DEB.Core.Motor.Fundacoes;
using DEB.Core.Motor.Json;
using DEB.Core.Motor.Perfis;
using DEB.Core.Motor.Receitas;
using Xunit;

namespace DEB.Core.Tests.Motor;

/// <summary>Receita JSON de fábrica do bloco: gravada e relida (como o arquivo do usuário) dá o mesmo modelo; e é válida.
/// (A paridade com o template antigo está no MotorBlocoTests.)</summary>
[Collection("Pilar")]
public class ReceitaBlocoTests
{
    public static IEnumerable<object[]> Casos()
    {
        ElementoBloco B(Action<ElementoBloco> a) { var e = ImportadorBlocos.ExemploB28(); a(e); return e; }
        yield return new object[] { "B28", ImportadorBlocos.ExemploB28() };
        yield return new object[] { "B15", ImportadorBlocos.ExemploB15() };
        yield return new object[] { "B31", B(e => { e.EstacasY = 2; e.EspX = 150; e.EspY = 150; e.DistFace = 40; e.DiamEstaca = 50; e.PilarBx = 60; e.PilarBy = 60; e.ParedeCalice = 30; e.AlturaBase = 110; e.AlturaColarinho = 50; e.Lemb = 95; }) };
        yield return new object[] { "arranque", B(e => e.Ligacao = LigacaoPilar.Arranque) };
        yield return new object[] { "embutido", B(e => { e.Ligacao = LigacaoPilar.CaliceEmbutido; e.Lemb = 45; }) };
        yield return new object[] { "1 estaca", B(e => { e.EstacasX = 1; e.EspX = 0; e.DiamEstaca = 50; e.AlturaBase = 70; e.Ligacao = LigacaoPilar.CaliceEmbutido; e.Lemb = 45; }) };
        yield return new object[] { "excentrico", B(e => { e.PilarEx = 10; e.PilarEy = -5; e.Interface = InterfaceCalice.Lisa; }) };
        yield return new object[] { "sem lastro", B(e => { e.Lastro = 0; e.Regularizacao = 0; }) };
    }

    private static PerfilDetalhamento PerfilValor()
    {
        var p = PerfisFabrica.Obter("bpm_bloco");
        p.Regras["principal.perna"] = "valor"; p.Padroes["principal.perna"] = 33; p.Regras["arranque.pe_sentido"] = "dentro";
        return p;
    }

    [Theory]
    [MemberData(nameof(Casos))]
    public void ReceitaIgualAoMotor(string caso, ElementoBloco el)
    {
        ReceitasFabrica.Garantir();
        var receita = RepositorioReceitas.ObterFabrica(ElementoBloco.TipoId);
        Assert.NotNull(receita);
        var relida = SerializacaoReceita.Ler(SerializacaoReceita.Gravar(receita));
        var motorReceita = new MotorReceita<ElementoBloco>(relida, ComponentesPadrao.Catalogo());
        var sobr = new List<Sobrescrita> { new Sobrescrita { Posicao = "principal_x", Parametro = "perna", Valor = 40 } };
        foreach (var perfil in new[] { PerfisFabrica.Obter("bpm_bloco"), PerfilValor() })
        {
            var a = SerializacaoMotor.GravarModelo(new MotorBlocoEstacas().Gerar(el, perfil, sobr));
            var b = SerializacaoMotor.GravarModelo(motorReceita.Gerar(el, perfil, sobr));
            Assert.True(a == b, $"{caso}: receita relida difere da de fábrica.\n{Primeira(a, b)}");
        }
    }

    [Fact]
    public void ReceitaDeFabricaEValida()
    {
        ReceitasFabrica.Garantir();
        var r = ValidadorReceita.Validar(RepositorioReceitas.ObterFabrica(ElementoBloco.TipoId), ComponentesPadrao.Catalogo(), ImportadorBlocos.ExemploB28(), PerfisFabrica.Obter("bpm_bloco"));
        Assert.True(r.Valida, string.Join("\n", r.Problemas));
    }

    private static string Primeira(string a, string b)
    {
        var la = a.Split('\n'); var lb = b.Split('\n');
        for (int i = 0; i < Math.Min(la.Length, lb.Length); i++) if (la[i] != lb[i]) return $"linha {i + 1}:\n motor:   {la[i].Trim()}\n receita: {lb[i].Trim()}";
        return $"tamanhos {la.Length} × {lb.Length}";
    }
}
