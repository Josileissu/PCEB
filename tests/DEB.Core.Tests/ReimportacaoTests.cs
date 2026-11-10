using System.Collections.Generic;
using System.Linq;
using DEB.Core.Configuracao;
using DEB.Core.Intercambio;
using DEB.Core.Templates;
using DEB.Core.Templates.PilarPreMoldado;
using DEB.Core.Templates.VigaPreMoldada;
using Xunit;

namespace DEB.Core.Tests;

/// <summary>Reimportação das peças do Revit (2026-11-10): assinatura dos dados do modelo e a marca "conferir" na prancha.</summary>
[Collection("Pilar")]   // usa os catálogos estáticos
public class ReimportacaoTests
{
    private static VigaPeca Viga() => new VigaPeca
    {
        Marca = "V121", B = 30, H = 50, L = 457, DenteE = new DentePeca { Altura = 25, Comprimento = 25 },
        Aba = new AbaPeca { Lado = AbaPeca.DoisLados, Largura = 15, Altura = 15, AlturaPonta = 12, Base = 35, Inicio = 0, Fim = 457 },
        Elementos = { new ElementoPeca { Id = "1" }, new ElementoPeca { Id = "2" } },
    };

    [Fact]
    public void AssinaturaMudaSoComOsDadosDoModelo()
    {
        string a = AssinaturaPeca.De(Viga());
        Assert.Equal(8, a.Length);
        Assert.Equal(a, AssinaturaPeca.De(Viga()));
        // a marca das peças e a ordem dos elementos não mudam os dados; um milímetro a menos que 0,5 mm também não
        var v = Viga(); v.Marca = "OUTRA"; v.Elementos.Reverse(); v.L += 0.02;
        Assert.Equal(a, AssinaturaPeca.De(v));
        foreach (var muda in new System.Action<VigaPeca>[] { x => x.L += 1, x => x.Aba.AlturaPonta = 10, x => x.Aba = null, x => x.DenteE = null,
                     x => x.Elementos.Add(new ElementoPeca { Id = "3" }) })
        {
            var m = Viga(); muda(m);
            Assert.NotEqual(a, AssinaturaPeca.De(m));
        }
        var p = new PilarPeca { B = 40, H = 40, L = 600, Consolos = { new ConsoloPeca { Face = 1, Inicio = 10, Fim = 30, Base = 550, Topo = 573.5, Balanco = 25, AlturaPonta = 23.5 } } };
        string ap = AssinaturaPeca.De(p);
        p.Consolos[0].Topo = 580;
        Assert.NotEqual(ap, AssinaturaPeca.De(p));
    }

    [Fact]
    public void ComentarioGuardaALeAAssinatura()
    {
        string c = AssinaturaPeca.Comentario("modelo.rvt", "2026-11-10T10:00:00", new[] { "472581", "", "472582" }, "0A1B2C3D");
        Assert.Equal("Importado de modelo.rvt (2026-11-10T10:00:00); elementos 472581, 472582; dados #0A1B2C3D.", c);
        Assert.Equal("0A1B2C3D", AssinaturaPeca.DoComentario(c));
        Assert.True(AssinaturaPeca.Importado(c));
        // importado antes da assinatura existir: é importado, mas sem assinatura (conta como "mudou")
        Assert.True(AssinaturaPeca.Importado("Importado de modelo.rvt (2026-11-09); elementos 1."));
        Assert.Null(AssinaturaPeca.DoComentario("Importado de modelo.rvt (2026-11-09); elementos 1."));
        Assert.False(AssinaturaPeca.Importado("Gerado pelo template."));
    }

    [Fact]
    public void DenteCriadoNaImportacaoLevaOAvisoNaPranchaAteSerConferido()
    {
        var t = new TemplateVigaPreMoldada();
        var antes = TemplateVigaPreMoldada.CatalogoDentes;
        try
        {
            var novos = CatalogoAutomatico.Dentes(new[] { new VigaPeca { B = 20, H = 50, L = 600, DenteE = new DentePeca { Altura = 30, Comprimento = 20 } } }, antes);
            TemplateVigaPreMoldada.CatalogoDentes = antes.Concat(novos).ToList();
            var v = new ValoresTemplate(t.Parametros);
            v["H"] = "50"; v["ExtE"] = novos[0].Nome; v["ExtD"] = TemplateVigaPreMoldada.Reta;
            var r = t.Gerar(v, new ConfiguracaoArmadura());
            var aviso = r.Textos.Where(x => x.Layer == MarcaConferir.Layer && x.Texto.Contains("CONFERIR")).ToList();
            Assert.Single(aviso);
            Assert.Contains(novos[0].Nome, aviso[0].Texto);
            // o usuário conferiu e trocou a descrição do tipo: o aviso some
            novos[0].Descricao = "Conferido em 10/11";
            r = t.Gerar(v, new ConfiguracaoArmadura());
            Assert.DoesNotContain(r.Textos, x => x.Texto.Contains("CONFERIR A ARMADURA"));
        }
        finally { TemplateVigaPreMoldada.CatalogoDentes = antes; }
    }

    [Fact]
    public void ConsoloCriadoNaImportacaoLevaOAvisoNoPilar()
    {
        var t = new TemplatePilarPreMoldado();
        var antes = TemplatePilarPreMoldado.Catalogo;
        try
        {
            var p = new PilarPeca { Marca = "PX", B = 40, H = 40, L = 600, Consolos = { new ConsoloPeca { Face = 1, Inicio = 0, Fim = 40, Base = 300, Topo = 340, Balanco = 35, AlturaPonta = 40 } } };
            var novos = CatalogoAutomatico.Consolos(new[] { p }, antes);
            Assert.Single(novos);
            TemplatePilarPreMoldado.Catalogo = antes.Concat(novos).ToList();
            var avisos = new List<string>();
            var v = ConversorPilarPeca.Valores(p, new ValoresTemplate(t.Parametros), TemplatePilarPreMoldado.Catalogo, avisos);
            var r = t.Gerar(v, new ConfiguracaoArmadura());
            Assert.Contains(r.Textos, x => x.Layer == MarcaConferir.Layer && x.Texto.Contains(novos[0].Nome) && x.Texto.Contains("CONFERIR"));
            // sem tipos pendentes, nada
            Assert.DoesNotContain(t.Gerar(new ValoresTemplate(t.Parametros), new ConfiguracaoArmadura()).Textos, x => x.Texto.Contains("CONFERIR A ARMADURA"));
        }
        finally { TemplatePilarPreMoldado.Catalogo = antes; }
    }
}
