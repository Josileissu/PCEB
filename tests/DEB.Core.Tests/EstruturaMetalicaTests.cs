using System;
using System.Linq;
using DEB.Core.Analise.Barras;
using DEB.Core.Analise.Dimensionamento;
using DEB.Core.Analise.Objetos;
using DEB.Core.Configuracao.Projeto;
using DEB.Core.Elementos.Metalica;
using Xunit;

namespace DEB.Core.Tests;

/// <summary>
/// Peças de aço como objetos da análise (D48): pórtico com ligações flexíveis (forma fechada) e rígidas (OpenSees:
/// Gabaritos/portico_metalico_opensees.py), viga que chega no pilar fora do eixo, viga sobre viga e o dimensionamento.
/// </summary>
public class EstruturaMetalicaTests
{
    private static readonly AcoEstrutural A572 = AcoEstrutural.Buscar("A572");

    private static EstruturaObjetos Portico(LigacaoExtremidade lig, double recuo = 0)
    {
        var e = new EstruturaObjetos();
        BarraMetalicaObjeto Pil(string n, double x) => new BarraMetalicaObjeto
        {
            Dono = new Dono { Tipo = TipoDono.Pilar, Nome = n }, PontoI = new Vetor3(x, 0, 0), PontoJ = new Vetor3(x, 0, 400),
            Perfil = PerfilI.Buscar("W250X32.7"), Aco = A572, DirAlma = Vetor3.EixoX, Base = BasePilarMetalico.Engastada,
            LigacaoI = LigacaoExtremidade.Rigida, LigacaoJ = LigacaoExtremidade.Rigida,
        };
        e.Metalicas.Add(Pil("P1", 0));
        e.Metalicas.Add(Pil("P2", 600));
        e.Metalicas.Add(new BarraMetalicaObjeto
        {
            Dono = new Dono { Tipo = TipoDono.Viga, Nome = "V1" }, PontoI = new Vetor3(recuo, 0, 400), PontoJ = new Vetor3(600 - recuo, 0, 400),
            Perfil = PerfilI.Buscar("W310X38.7"), Aco = A572, G = 10, Q = 5, LigacaoI = lig, LigacaoJ = lig,
        });
        e.Analisar(new ConfiguracaoProjeto());
        return e;
    }

    private static double[][] Est(EstruturaObjetos e, BarraMetalicaObjeto o, string caso) =>
        o.Barras.SelectMany(b => e.Estacoes(caso)[b.Id]).ToArray();

    [Fact]
    public void Viga_com_ligacoes_flexiveis_e_biapoiada()
    {
        var e = Portico(LigacaoExtremidade.Flexivel);
        var v = e.Metalicas.Single(x => x.Dono.Nome == "V1");
        var s = Est(e, v, EstruturaObjetos.CasoG);
        Assert.Equal(4500, s.Max(x => Math.Abs(x[5])), 1);          // q·L²/8 = 0,1·600²/8 kN·cm
        Assert.True(Math.Abs(s.First()[5]) < 0.01);          // rótula na ponta
        var p = e.Metalicas.Single(x => x.Dono.Nome == "P1");
        Assert.Equal(-30, Est(e, p, EstruturaObjetos.CasoG).First()[1], 3);   // compressão de q·L/2
    }

    [Fact]
    public void Portico_rigido_bate_com_o_OpenSees()
    {
        var e = Portico(LigacaoExtremidade.Rigida);
        var v = e.Metalicas.Single(x => x.Dono.Nome == "V1");
        var s = Est(e, v, EstruturaObjetos.CasoG);
        Assert.Equal(1905.96, Math.Abs(s.First()[5]), 1905.96 * 0.003);
        Assert.Equal(7.137, Math.Abs(s.First()[1]), 0.03);
    }

    [Fact]
    public void Viga_desenhada_na_face_do_pilar_vai_para_o_eixo()
    {
        var e = Portico(LigacaoExtremidade.Flexivel, recuo: 12);
        var v = e.Metalicas.Single(x => x.Dono.Nome == "V1");
        Assert.Equal(600, v.Comprimento, 3);
        Assert.Equal(e.Metalicas[0].NoJ, v.NoI);
        Assert.DoesNotContain(e.Avisos, a => a.Contains("sem pilar"));
    }

    [Fact]
    public void Viga_sobre_viga_cria_no_e_apoia()
    {
        var e = new EstruturaObjetos();
        var w = PerfilI.Buscar("W310X38.7");
        var principal = new BarraMetalicaObjeto { Dono = new Dono { Tipo = TipoDono.Viga, Nome = "VP" }, PontoI = new Vetor3(0, 0, 0), PontoJ = new Vetor3(600, 0, 0), Perfil = w, Aco = A572 };
        var sec = new BarraMetalicaObjeto { Dono = new Dono { Tipo = TipoDono.Viga, Nome = "VS" }, PontoI = new Vetor3(300, 8, 0), PontoJ = new Vetor3(300, 500, 0), Perfil = PerfilI.Buscar("W200X15"), Aco = A572, G = 10 };
        e.Metalicas.Add(principal); e.Metalicas.Add(sec);
        e.Analisar(new ConfiguracaoProjeto());
        Assert.Equal(3, principal.NosEixo.Count);
        Assert.Equal(principal.NosEixo[1].No, sec.NoI);
        // a secundária (5 m, 0,1 kN/cm) põe 25 kN no meio da principal: M = P·L/4 = 25·600/4
        var s = Est(e, principal, EstruturaObjetos.CasoG);
        Assert.Equal(3750, s.Max(x => Math.Abs(x[5])), 3750 * 0.01);
    }

    [Fact]
    public void Dimensionamento_das_pecas_de_aco()
    {
        var e = Portico(LigacaoExtremidade.Flexivel);
        var res = DimensionamentoObjetos.Verificar(e, new ConfiguracaoProjeto());
        var v = res.Single(r => r.Dono.Nome == "V1");
        Assert.Contains(v.Verificacoes, x => x.Codigo == "MT3");
        Assert.Contains(v.Verificacoes, x => x.Codigo == "MT9");
        Assert.Contains("W310X38.7", v.Resumo[0]);
        Assert.Contains(v.Memoria.Linhas, l => l.Contains("Cb = 1,1"));    // carga uniforme, viga biapoiada destravada: Cb ≈ 1,14
        // ligações flexíveis das duas pontas: chapa de alma proposta e verificada com o cortante da ponta
        Assert.Equal(2, e.LigacoesMetalicas.Count(x => x.Tipo == DEB.Core.Elementos.Metalica.Ligacoes.TipoLigacao.ChapaAlma));
        Assert.Equal(2, e.LigacoesMetalicas.Count(x => x.Tipo == DEB.Core.Elementos.Metalica.Ligacoes.TipoLigacao.PlacaBase));
        var base1 = res.Single(r => r.Dono.Nome.StartsWith("placa de base P1"));
        Assert.Contains(base1.Verificacoes, x => x.Codigo == "PB1" && x.Situacao == DEB.Core.Normas.Verificacao.SituacaoVerificacao.Ok);
        Assert.Contains("PCEB-PB-25", base1.Resumo[0]);
        var lig = res.Single(r => r.Dono.Tipo == TipoDono.Ligacao && r.Dono.Nome.Contains("(I)"));
        Assert.Contains(lig.Verificacoes, x => x.Codigo == "LC1" && x.Situacao == DEB.Core.Normas.Verificacao.SituacaoVerificacao.Ok);
        Assert.Contains("chapa", lig.Resumo[0]);
        var p = res.Single(r => r.Dono.Nome == "P1");
        Assert.Contains(p.Verificacoes, x => x.Codigo == "MT2" && x.Situacao == DEB.Core.Normas.Verificacao.SituacaoVerificacao.Ok);
    }

    [Fact]
    public void Portico_rigido_ganha_chapas_de_topo()
    {
        var e = Portico(LigacaoExtremidade.Rigida);
        Assert.Equal(2, e.LigacoesMetalicas.Count(x => x.Tipo == DEB.Core.Elementos.Metalica.Ligacoes.TipoLigacao.ChapaTopo));
        var res = DimensionamentoObjetos.Verificar(e, new ConfiguracaoProjeto());
        var topo = res.First(r => r.Dono.Nome.StartsWith("chapa de topo"));
        Assert.Contains(topo.Verificacoes, x => x.Codigo == "CT1");
        Assert.Contains("PCEB-CT-22", topo.Resumo[0]);
    }

    [Fact]
    public void Ajustes_da_janela_mudam_o_modelo_e_voltam()
    {
        var e = Portico(LigacaoExtremidade.Flexivel);
        e.ValoresDePartida = SessaoAnalise.Valores(e);
        var s = new SessaoAnalise(e, new ConfiguracaoProjeto(), "teste", null);
        Assert.Contains("\"ajustes\"", s.Json());
        double mFlex = Est(e, e.Metalicas.Single(x => x.Dono.Nome == "V1"), EstruturaObjetos.CasoG).First()[5];
        string chave = AjustesAnalise.Chave(e.Metalicas.Single(x => x.Dono.Nome == "V1").Dono);
        foreach (var p in new[] { "ligI", "ligJ" })
            s.Receber("{\"acao\":\"ajustar\",\"chave\":\"" + chave + "\",\"prop\":\"" + p + "\",\"valor\":\"Rígida\"}");
        var v = e.Metalicas.Single(x => x.Dono.Nome == "V1");
        Assert.Equal(1905.96, System.Math.Abs(Est(e, v, EstruturaObjetos.CasoG).First()[5]), 1905.96 * 0.003);   // o pórtico rígido do OpenSees
        Assert.Equal(2, e.LigacoesMetalicas.Count(x => x.Tipo == DEB.Core.Elementos.Metalica.Ligacoes.TipoLigacao.ChapaTopo));
        Assert.True(s.Alterado);
        // modelo da biblioteca escolhido para uma ligação
        string lig = AjustesAnalise.Chave(e.LigacoesMetalicas.First(x => x.Tipo == DEB.Core.Elementos.Metalica.Ligacoes.TipoLigacao.ChapaTopo).Dono);
        s.Receber("{\"acao\":\"ajustar\",\"chave\":\"" + lig + "\",\"prop\":\"modelo\",\"valor\":\"PCEB-CT-25\"}");
        Assert.Equal("PCEB-CT-25", e.LigacoesMetalicas.Single(x => AjustesAnalise.Chave(x.Dono) == lig).Modelo.Id);
        // perfil inválido: erro, nada muda
        string r = s.Receber("{\"acao\":\"ajustar\",\"chave\":\"" + chave + "\",\"prop\":\"perfil\",\"valor\":\"XPTO\"}");
        Assert.Contains("\"erro\"", r);
        Assert.Equal("W310X38.7", v.Perfil.Nome);
        // restaurar a viga inteira: volta a flexível
        s.Receber("{\"acao\":\"restaurar\",\"chave\":\"" + chave + "\"}");
        Assert.Equal(mFlex, Est(e, v, EstruturaObjetos.CasoG).First()[5], 6);
        // prévia da janela com a viga selecionada (fora do Git): $env:PCEB_PREVIA_ANALISE = "<pasta>"
        string pasta = System.Environment.GetEnvironmentVariable("PCEB_PREVIA_ANALISE");
        if (!string.IsNullOrEmpty(pasta))
        {
            s.Receber("{\"acao\":\"ajustar\",\"chave\":\"" + chave + "\",\"prop\":\"g\",\"valor\":\"12\"}");
            string html = VisualizacaoAnalise.Pagina().Replace("<script>", "<script>window.DADOS_DEMO = " + s.Json() + ";</script><script>", System.StringComparison.Ordinal);
            int k = html.LastIndexOf("</body>", System.StringComparison.Ordinal);
            html = html.Insert(k, "<script>setTimeout(() => { const b = [...document.querySelectorAll('#lista button')].find(x => x.textContent.includes('V1')); if (b) b.click(); }, 300);</script>");
            System.IO.File.WriteAllText(System.IO.Path.Combine(pasta, "analise_ajustes.html"), html);
        }
        // gravado e relido
        var json = AjustesAnalise.ParaJson(s.Ajustes);
        Assert.Equal("PCEB-CT-25", AjustesAnalise.DeJson(json)[lig]["modelo"]);
    }

    [Fact]
    public void Cargas_lancadas_na_janela_entram_na_analise()
    {
        var e = Portico(LigacaoExtremidade.Flexivel);
        // a janela recebe as cargas na unidade da configuração: aqui o projeto está em kN (o padrão do PCEB é tf e kgf/m)
        var cfg = new ConfiguracaoProjeto();
        foreach (var g in new[] { Grandeza.Forca, Grandeza.CargaLinear, Grandeza.CargaSuperficie })
            cfg.Definir(NivelConfiguracao.Projeto, CatalogoConfiguracao.IdUnidade(g), g == Grandeza.Forca ? "kN" : g == Grandeza.CargaLinear ? "kN/m" : "kN/m²");
        var s = new SessaoAnalise(e, cfg, "teste", null);
        var v = e.Metalicas.Single(x => x.Dono.Nome == "V1");
        var p1 = e.Metalicas.Single(x => x.Dono.Nome == "P1"); var p2 = e.Metalicas.Single(x => x.Dono.Nome == "P2");
        string kv = AjustesAnalise.Chave(v.Dono), kp = AjustesAnalise.Chave(p1.Dono);
        Assert.Contains("\"tiposCarga\"", s.Json());
        // +10 kN/m permanente na viga inteira: o momento de G dobra (0,2·600²/8)
        s.Receber("{\"acao\":\"carga\",\"op\":\"adicionar\",\"alvo\":\"" + kv + "\",\"tipo\":\"Linear\",\"caso\":\"G\",\"valor\":\"10\"}");
        Assert.Equal(9000, Est(e, v, EstruturaObjetos.CasoG).Max(x => System.Math.Abs(x[5])), 2);
        // 10 kN horizontais no topo do P1 (caso Q): a viga leva metade ao P2; cada base engastada fica com 5·400 kN·cm
        s.Receber("{\"acao\":\"carga\",\"op\":\"adicionar\",\"alvo\":\"" + kp + "\",\"tipo\":\"Pontual\",\"caso\":\"Q\",\"valor\":\"10\",\"posicao\":\"400\",\"direcao\":\"X\"}");
        double m1 = System.Math.Abs(Est(e, p1, EstruturaObjetos.CasoQ).First()[5]), m2 = System.Math.Abs(Est(e, p2, EstruturaObjetos.CasoQ).First()[5]);
        Assert.Equal(2000, m1, 20.0); Assert.Equal(2000, m2, 20.0);
        Assert.True(s.CargasAlteradas);
        // tipo errado para o objeto: erro; remover a carga da viga volta ao momento original
        Assert.Contains("\"erro\"", s.Receber("{\"acao\":\"carga\",\"op\":\"adicionar\",\"alvo\":\"" + kp + "\",\"tipo\":\"Superficie\",\"valor\":\"1\"}"));
        string id = e.Cargas.First(c => c.Alvo == kv).Id;
        s.Receber("{\"acao\":\"carga\",\"op\":\"remover\",\"id\":\"" + id + "\"}");
        Assert.Equal(4500, Est(e, v, EstruturaObjetos.CasoG).Max(x => System.Math.Abs(x[5])), 2);
        // gravação: só as do PCEB
        e.Cargas.Add(new CargaObjeto { Alvo = kv, Tipo = TipoCargaObjeto.Linear, Valor = 3, Origem = OrigemCarga.Revit });
        var lidas = CargasObjetos.DeJson(CargasObjetos.ParaJson(e.Cargas));
        Assert.Single(lidas);
        Assert.Equal("X", lidas[0].Direcao);
    }
}
