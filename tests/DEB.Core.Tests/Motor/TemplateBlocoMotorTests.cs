using System;
using System.Collections.Generic;
using System.Linq;
using DEB.Core.Configuracao;
using DEB.Core.Modelo;
using DEB.Core.Motor;
using DEB.Core.Motor.Entrada;
using DEB.Core.Motor.Fundacoes;
using DEB.Core.Motor.Json;
using DEB.Core.Persistencia;
using DEB.Core.Templates;
using DEB.Core.Templates.Blocos;
using Xunit;

namespace DEB.Core.Tests.Motor;

/// <summary>O bloco do ARMTEMPLATE pelo motor: mesma janela do template antigo, origem gravada no detalhamento.</summary>
[Collection("Pilar")]
public class TemplateBlocoMotorTests
{
    private static double Peso(IEnumerable<BarraTemplate> bs, ConfiguracaoArmadura cfg) => bs.Sum(b => MedidaBarra.Peso(b, cfg));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MesmaJanelaEMesmoAcoDoTemplateAntigo(bool umaEstaca)
    {
        var cfg = new ConfiguracaoArmadura();
        var antigo = new TemplateBlocoEstacas(umaEstaca);
        var novo = new TemplateBlocoMotor(umaEstaca);
        Assert.Equal(antigo.Id, novo.Id);
        Assert.Equal(antigo.Parametros.Select(p => p.Nome), novo.Parametros.Select(p => p.Nome));

        var v = new ValoresTemplate(novo.Parametros);
        var r = novo.Gerar(v, cfg);
        Assert.DoesNotContain(r.Avisos, a => a.Contains("erro inesperado") || a.Contains("inválidos"));
        Assert.NotEmpty(r.Barras);
        var r0 = antigo.Gerar(new ValoresTemplate(antigo.Parametros), cfg);
        Assert.Equal(r0.Barras.Count, r.Barras.Count);
        // mesmas barras, na mesma ordem, com o mesmo comprimento e quantidade (nomes: rótulo no antigo, chave no motor).
        // 1 estaca: o antigo reservava a camada da principal (que não existe) e subia a secundária 2,5 cm; o motor apoia a
        // secundária no cobrimento, e verticais do cálice e suspensão ficam 3 cm mais altas.
        var diferentes = umaEstaca ? new[] { "calice_vertical", "suspensao" } : new string[0];
        for (int k = 0; k < r.Barras.Count; k++)
        {
            var a = r0.Barras[k]; var b = r.Barras[k];
            Assert.Equal(a.Bitola, b.Bitola);
            Assert.Equal(a.Quantidade * Math.Max(1, a.Repeticoes), b.Quantidade * Math.Max(1, b.Repeticoes));
            double la = MedidaBarra.Unitario(a, cfg), lb = MedidaBarra.Unitario(b, cfg);
            if (diferentes.Contains(b.Nome)) Assert.InRange(lb - la, 0, 13);
            else Assert.True(Math.Abs(la - lb) <= 1, $"{b.Nome}: {la} × {lb}");
        }
        Assert.False(string.IsNullOrEmpty(r.DadosDetalhamento.Motor));
    }

    [Fact]
    public void GrampoNaoPlanoSaiPlanificadoComoNoTemplateAntigo()
    {
        var cfg = new ConfiguracaoArmadura();
        var v = ConversorBloco.Valores(ImportadorBlocos.ExemploB28());
        var b = new TemplateBlocoMotor().Gerar(v, cfg).Barras.First(x => x.Nome == "calice_vertical");
        var antigo = new TemplateBlocoEstacas().Gerar(v, cfg).Barras.First(x => x.Nome == "cálice: verticais");
        Assert.Equal(6, b.Vertices.Count);
        double Larg(BarraTemplate t) => t.Vertices.Max(p => p.X) - t.Vertices.Min(p => p.X);
        double Alt(BarraTemplate t) => t.Vertices.Max(p => p.Y) - t.Vertices.Min(p => p.Y);
        Assert.Equal(Larg(antigo), Larg(b), 3);
        Assert.Equal(Alt(antigo), Alt(b), 3);
        Assert.Equal(MedidaBarra.Unitario(antigo, cfg), MedidaBarra.Unitario(b, cfg));
    }

    [Fact]
    public void OrigemGravadaRegeneraOMesmoDesenhoEComEdicaoManual()
    {
        var cfg = new ConfiguracaoArmadura();
        var t = new TemplateBlocoMotor();
        var v = ConversorBloco.Valores(ImportadorBlocos.ExemploB28());
        v["PrincPerna"] = "35";   // padrão do escritório digitado na janela → sobreposição do perfil
        var r = t.Gerar(v, cfg);

        var o = OrigemDetalhamento.Ler(r.DadosDetalhamento.Motor);
        Assert.Equal(TemplateBlocoMotor.PerfilPadrao, o.Perfil);
        Assert.Equal(SerializacaoMotor.GravarElemento(ConversorBloco.DeValores(v)), SerializacaoMotor.GravarElemento(o.Elemento));
        Assert.Equal(35, o.SobreposicoesPerfil.Padroes["principal.perna"]);

        var de_novo = t.Gerar(o, cfg);
        Assert.Equal(r.Barras.Count, de_novo.Barras.Count);
        Assert.Equal(Peso(r.Barras, cfg), Peso(de_novo.Barras, cfg), 6);

        // edição na produção: a sobrescrita entra na origem e no desenho
        Sobrescritas.Definir(o.Sobrescritas, "principal_x", "perna", 20);
        var editado = t.Gerar(o, cfg);
        Assert.True(Peso(editado.Barras, cfg) < Peso(r.Barras, cfg));
        Assert.Single(OrigemDetalhamento.Ler(editado.DadosDetalhamento.Motor).Sobrescritas);
    }

    [Fact]
    public void OrigemSobreviveAoXDataDoDetalhamento()
    {
        var r = new TemplateBlocoMotor().Gerar(ConversorBloco.Valores(ImportadorBlocos.ExemploB28()), new ConfiguracaoArmadura());
        var d = r.DadosDetalhamento;
        d.Motor = d.Motor.Replace("\"nome\":\"B28\"", "\"nome\":\"BLOCO ÁÉÇÃ\"");
        var campos = CodecDados.Codificar(d);
        Assert.All(campos.Where(c => c.Valor is string), c => Assert.True(System.Text.Encoding.UTF8.GetByteCount((string)c.Valor) <= 255));
        var lido = CodecDados.DecodificarDetalhamento(campos);
        Assert.Equal(d.Motor, lido.Motor);
        Assert.Equal("BLOCO ÁÉÇÃ", OrigemDetalhamento.Ler(lido.Motor).Elemento.Nome);
        // detalhamento antigo, sem origem
        Assert.Equal("", CodecDados.DecodificarDetalhamento(CodecDados.Codificar(new DadosDetalhamento())).Motor);
        Assert.Null(OrigemDetalhamento.TentarLer("", out var erro));
        Assert.NotNull(erro);
    }

    [Fact]
    public void RegeneracaoListaParametrosEGravaEdicoes()
    {
        var cfg = new ConfiguracaoArmadura();
        var r = new TemplateBlocoMotor().Gerar(ConversorBloco.Valores(ImportadorBlocos.ExemploB28()), cfg);
        var o = OrigemDetalhamento.Ler(r.DadosDetalhamento.Motor);
        Assert.True(RegeneracaoMotor.Suporta(o));

        var ps = RegeneracaoMotor.Parametros(o, cfg);
        var perna = ps.Single(p => p.Posicao == "principal_x" && p.Parametro == "perna");
        Assert.Null(perna.Manual);
        Assert.True(perna.PelaRegra > 20);
        Assert.Equal(ps.Count, ps.Select(p => (p.Posicao, p.Parametro)).Distinct().Count());

        // edição → sobrescrita; o desenho regenerado muda e a origem nova guarda a edição
        perna.Manual = 20;
        Assert.Equal(1, RegeneracaoMotor.Aplicar(o, ps));
        Assert.Equal(0, RegeneracaoMotor.Aplicar(o, ps));        // nada mudou de novo
        var r2 = RegeneracaoMotor.Gerar(o, cfg);
        Assert.True(Peso(r2.Barras, cfg) < Peso(r.Barras, cfg));
        var o2 = OrigemDetalhamento.Ler(r2.DadosDetalhamento.Motor);
        var perna2 = RegeneracaoMotor.Parametros(o2, cfg).Single(p => p.Posicao == "principal_x" && p.Parametro == "perna");
        Assert.Equal(20, perna2.Manual);
        Assert.Equal(perna.PelaRegra, perna2.PelaRegra);       // a regra é calculada sem as edições

        // valor igual à regra (ou vazio) volta para a regra
        perna2.Manual = perna2.PelaRegra;
        Assert.Equal(1, RegeneracaoMotor.Aplicar(o2, new[] { perna2 }));
        Assert.Empty(o2.Sobrescritas);
        Assert.Equal(Peso(r.Barras, cfg), Peso(RegeneracaoMotor.Gerar(o2, cfg).Barras, cfg), 6);
    }
}
