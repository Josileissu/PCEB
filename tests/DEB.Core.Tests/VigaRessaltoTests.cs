using System;
using System.Collections.Generic;
using System.Linq;
using DEB.Core.Configuracao;
using DEB.Core.Intercambio;
using DEB.Core.Pecas;
using DEB.Core.Persistencia;
using DEB.Core.Templates;
using DEB.Core.Templates.VigaPreMoldada;
using Xunit;

namespace DEB.Core.Tests;

/// <summary>Ressalto sobre a alma (2026-11-10): leitura da seção do modelo, arquivo de troca e desenho com a armadura proposta.</summary>
[Collection("Pilar")]
public class VigaRessaltoTests
{
    private static TrechoPeca T(double x0, double x1, SecaoPoligonal s) => new TrechoPeca { X0 = x0, X1 = x1, Secao = s };
    private static SecaoPoligonal Poli(params (double, double)[] p) => new SecaoPoligonal { Contorno = p.ToList() };

    // contornos tirados do modelo real (Revit): alma 30 × 50, aba de laje de 15 rente ao topo do lado de y maior, ressalto 13 × 27,5 do outro
    private static readonly SecaoPoligonal Vao = Poli((45, 38), (30, 35), (30, 0), (0, 0), (0, 77.5), (13, 77.5), (13, 50), (45, 50));
    private static readonly SecaoPoligonal Dente = Poli((45, 38), (30, 35), (30, 25), (0, 25), (0, 77.5), (13, 77.5), (13, 50), (45, 50));

    [Fact]
    public void LeAlmaAbaERessalto()
    {
        var i = SecaoComAba.Ler(Vao);
        Assert.NotNull(i);
        Assert.Equal((30.0, 50.0, 15.0, 15.0, 12.0, 35.0), (i.B, i.H, i.AbaEsquerda, i.AbaAltura, i.AbaAlturaPonta, i.AbaBase));
        Assert.True(i.TemRessalto);
        Assert.Equal((13.0, 27.5, false), (i.RessaltoLargura, i.RessaltoAltura, i.RessaltoEsquerda));
        var d = SecaoComAba.Ler(Dente);
        Assert.Equal(25, d.H);
        Assert.Equal(i.Chave, d.Chave);
        // espelhada (aba do lado de y menor, ressalto do outro)
        var m = SecaoComAba.Ler(Poli((15, 50), (0, 53), (0, 65), (32, 65), (32, 92.5), (45, 92.5), (45, 0), (15, 0)));
        Assert.Equal((30.0, 65.0, 15.0, 13.0, true), (m.B, m.H, m.AbaDireita, m.RessaltoLargura, m.RessaltoEsquerda));
    }

    [Fact]
    public void DegrauDeUmCentimetroEAAlmaContinuando()
    {
        // viga de escada do modelo real: a alma tem 19 abaixo da aba e 20 acima dela — é a mesma alma, não um ressalto
        var s = Poli((39, 30.5), (19, 27.5), (19, 0), (0, 0), (0, 55), (20, 55), (20, 42.5), (39, 42.5));
        var i = SecaoComAba.Ler(s);
        Assert.NotNull(i);
        Assert.False(i.TemRessalto);
        Assert.Equal((19.0, 55.0, 20.0, 15.0, 12.0, 27.5), (i.B, i.H, i.AbaEsquerda, i.AbaAltura, i.AbaAlturaPonta, i.AbaBase));
        // degrau no meio da largura = T invertido (alma 10 × 70 com abas de 10 × 50), que o template já desenha — não é ressalto
        var t = SecaoComAba.Ler(Poli((0, 0), (30, 0), (30, 50), (20, 50), (20, 70), (10, 70), (10, 50), (0, 50)));
        Assert.Equal((10.0, 70.0, 10.0, 10.0, false), (t.B, t.H, t.AbaEsquerda, t.AbaDireita, t.TemRessalto));
    }

    [Fact]
    public void VigaDoModeloComRessaltoVaiParaOCadNaVersao3()
    {
        var p = new PecaLevantada
        {
            Id = "1", Marca = "V119", Tipo = TipoPeca.Viga, PreMoldada = true, Comprimento = 457,
            Trechos = { T(0, 25, Dente), T(25, 432, Vao), T(432, 457, Dente) },
        };
        ConferenciaPecas.Avaliar(p);
        Assert.NotEqual(SituacaoPeca.NaoSuportada, p.Situacao);
        var arq = ConversorLevantamento.Converter(new[] { p }, "m.rvt", "");
        var v = Assert.Single(arq.Vigas);
        Assert.Equal((30.0, 50.0, 25.0), (v.B, v.H, v.DenteE.Altura));
        Assert.Equal((AbaPeca.Esquerda, 15.0, 35.0), (v.Aba.Lado, v.Aba.Largura, v.Aba.Base));
        Assert.Equal((AbaPeca.Direita, 13.0, 27.5), (v.Ressalto.Lado, v.Ressalto.Largura, v.Ressalto.Altura));
        var json = arq.ParaJson();
        Assert.Equal(3, LeitorJson.Int(json, "versao", 0, "t"));
        var lido = ArquivoPecas.DeJson(Json.Ler(Json.Gravar(json)));
        Assert.Equal(27.5, lido.Vigas[0].Ressalto.Altura);
        // a assinatura muda com o ressalto e fica igual sem ele (o que já foi importado não vira "mudou")
        string com = AssinaturaPeca.De(v);
        v.Ressalto = null;
        Assert.NotEqual(com, AssinaturaPeca.De(v));
    }

    private static ResultadoTemplate Gerar(Action<ValoresTemplate> ajustar)
    {
        var t = new TemplateVigaPreMoldada();
        var v = new ValoresTemplate(t.Parametros);
        ajustar(v);
        Assert.Empty(CatalogoTemplates.Validar(t, v));
        return t.Gerar(v, new ConfiguracaoArmadura());
    }

    internal static void V119(ValoresTemplate v)
    {
        v["Nome"] = "V119"; v["B"] = "30"; v["H"] = "50"; v["L"] = "457";
        v["Aba"] = TemplateVigaPreMoldada.AbaEsq; v["AbaLargura"] = "15"; v["AbaAltura"] = "15"; v["AbaAlturaPonta"] = "12"; v["AbaBase"] = "35";
        v["Ressalto"] = TemplateVigaPreMoldada.RessaltoDir; v["RessaltoLargura"] = "13"; v["RessaltoAltura"] = "27.5";
    }

    [Fact]
    public void DesenhaORessaltoComAArmaduraProposta()
    {
        var r = Gerar(V119);
        Assert.Contains(r.Avisos, a => a.StartsWith("Ressalto e ligação com a capa"));
        // seção do vão: alma + aba no topo (fundo inclinado) + ressalto = 8 vértices, 27,5 acima do topo da alma; cotas 13 e 27,5
        Assert.Contains(r.Contornos, k => k.Layer == "ARM-PILAR-SECAO" && k.Fechado && k.Vertices.Count == 8 && k.Vertices.Max(q => q.Y) - k.Vertices.Min(q => q.Y) == 77.5);
        foreach (var m in new[] { 13.0, 27.5 })
            Assert.Contains(r.Cotas, k => Math.Abs(Math.Sqrt(Math.Pow(k.P2.X - k.P1.X, 2) + Math.Pow(k.P2.Y - k.P1.Y, 2)) - m) < 1e-6);
        // barras: estribo do ressalto (fechado), longitudinais e a barra em L com a perna horizontal = dentro do ressalto + 70
        var est = r.Barras.Single(b => b.Nome == "ressestribo");
        Assert.True(est.Estribo);
        Assert.Equal(4, r.Barras.Single(b => b.Nome == "resslong").Quantidade);
        var l = r.Barras.Single(b => b.Nome == "ligcapa");
        Assert.Equal(3, l.Vertices.Count);
        double horizontal = Math.Abs(l.Vertices[2].X - l.Vertices[1].X), vertical = l.Vertices[1].Y - l.Vertices[0].Y;
        Assert.InRange(horizontal, 70 + 13 - 2.5 - 1.5, 70 + 13 - 2.5);   // 70 fora + o ressalto menos cobrimento e estribo
        Assert.InRange(vertical, 40 + 27.5 - 5, 40 + 27.5);                // 40 na alma + o ressalto
        // faixas na elevação (estribo do ressalto c/15 e ligação c/20) e indicações nas seções
        Assert.Contains(r.Faixas, f => f.Barra == "ressestribo" && f.Tipo == Modelo.TipoFaixa.Ortogonal && f.Espacamento == 15);
        Assert.Contains(r.Faixas, f => f.Barra == "ligcapa" && f.Tipo == Modelo.TipoFaixa.Ortogonal && f.Espacamento == 20);
        Assert.Contains(r.Faixas, f => f.Barra == "ligcapa" && f.Tipo == Modelo.TipoFaixa.Indicacao);
        Assert.Contains(r.Textos, t => t.Texto.Contains("ARMADURA RESSALTO"));
        // volume com o ressalto: 30×50×457 − recortes dos dentes padrão (DG 45x25: 5 × 25) + aba + ressalto 13 × 27,5 × 457
        var prod = r.Tabelas.SelectMany(t => t.Linhas).SelectMany(x => x).ToList();
        double vol = (30 * 50 * 457 - 2 * 30 * 5 * 25 + 15 * 13.5 * 457 + 13 * 27.5 * 457) / 1e6;
        Assert.Contains(prod, s => s != null && s.Contains(vol.ToString("0.000").Replace('.', ',')));
    }

    [Fact]
    public void RessaltoTaoLargoQuantoAVigaNaoEDesenhado()
    {
        var r = Gerar(v => { v["Ressalto"] = TemplateVigaPreMoldada.RessaltoEsq; v["RessaltoLargura"] = "40"; });
        Assert.Contains(r.Avisos, a => a.Contains("sem ressalto"));
        Assert.DoesNotContain(r.Barras, b => b.Nome.StartsWith("ress") || b.Nome == "ligcapa");
    }
}
