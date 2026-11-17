using System;
using System.Linq;
using DEB.Core.Elementos.Pilar.PreMoldado;
using DEB.Core.Geometria.Secao;
using Xunit;

namespace DEB.Core.Tests.Geometria;

/// <summary>Número de face do retângulo ↔ parte nomeada (as duas numerações de hoje).</summary>
public class FacesRetanguloTests
{
    [Fact]
    public void NumeracaoInternaEApresentada()
    {
        Assert.Equal(new[] { ParteSecao.Direita, ParteSecao.Fundo, ParteSecao.Esquerda, ParteSecao.Topo },
                     Enumerable.Range(1, 4).Select(FacesRetangulo.DaFaceInterna));
        Assert.Equal(new[] { ParteSecao.Esquerda, ParteSecao.Fundo, ParteSecao.Direita, ParteSecao.Topo },
                     Enumerable.Range(1, 4).Select(FacesRetangulo.DaFaceApresentada));
        for (int f = 1; f <= 4; f++)
        {
            Assert.Equal(f, FacesRetangulo.FaceInterna(FacesRetangulo.DaFaceInterna(f)));
            Assert.Equal(f, FacesRetangulo.FaceApresentada(FacesRetangulo.DaFaceApresentada(f)));
            // a oposta pela parte é a mesma da regra numérica de hoje ((f + 1) % 4 + 1)
            Assert.Equal((f + 1) % 4 + 1, FacesRetangulo.FaceInterna(FacesRetangulo.Oposta(FacesRetangulo.DaFaceInterna(f))));
        }
    }

    [Fact]
    public void FaceExibidaDoPilarNaoMuda()
    {
        // a regra antiga: f == 1 ? 3 : f == 3 ? 1 : f, inclusive fora de 1…4
        foreach (int f in new[] { -1, 0, 1, 2, 3, 4, 5 })
            Assert.Equal(f == 1 ? 3 : f == 3 ? 1 : f, TemplatePilarPreMoldado.FaceExibida(f));
    }

    [Fact]
    public void CobrimentosPorParteEPorFace()
    {
        var cob = new[] { 3.0, 2.5, 4.0, 3.5, 5.0, 6.0 };   // 4 faces + topo e base do pilar
        var p = FacesRetangulo.Cobrimentos(cob);
        Assert.Equal((3.0, 2.5, 4.0, 3.5), (p[ParteSecao.Direita], p[ParteSecao.Fundo], p[ParteSecao.Esquerda], p[ParteSecao.Topo]));
        Assert.Equal(cob.Take(4), FacesRetangulo.CobrimentosPorFace(p));
        p.Remove(ParteSecao.Topo);
        Assert.Throws<ArgumentException>(() => FacesRetangulo.CobrimentosPorFace(p));
        Assert.Throws<ArgumentOutOfRangeException>(() => FacesRetangulo.DaFaceInterna(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => FacesRetangulo.FaceInterna(ParteSecao.Alma));
    }

    [Fact]
    public void FacesDoRetanguloBatemComAsPartesDaSecao()
    {
        // face interna 1 (+x) = a parte "Direita" do contrato, em y = b
        var s = SecaoTransversal.Retangular(40, 60);
        Assert.Equal(40, s.Parte(FacesRetangulo.DaFaceInterna(1)).Inicio.Y, 9);
        Assert.Equal(0, s.Parte(FacesRetangulo.DaFaceInterna(2)).Inicio.Z, 9);
        Assert.Equal(0, s.Parte(FacesRetangulo.DaFaceInterna(3)).Inicio.Y, 9);
        Assert.Equal(60, s.Parte(FacesRetangulo.DaFaceInterna(4)).Inicio.Z, 9);
    }
}
