using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using DEB.Core.Modelo;
using DEB.Core.Motor;
using DEB.Core.Motor.Json;
using DEB.Core.Motor.Perfis;
using DEB.Core.Persistencia;
using Xunit;
using TextoJson = DEB.Core.Persistencia.Json;

namespace DEB.Core.Tests.Motor;

/// <summary>Frente A do motor: JSON próprio, serialização dos contratos, perfil de fábrica BPM e repositório de perfis.</summary>
public class PerfilJsonTests
{
    // ================================================================= JSON

    [Fact]
    public void Json_le_todos_os_tipos()
    {
        var v = TextoJson.Ler("{ \"a\": 1.5, \"b\": [true, false, null], \"c\": { \"d\": \"x\" }, \"e\": -2e3, \"f\": 0 }");
        Assert.True(v.EhObjeto);
        Assert.Equal(1.5, v["a"].Numero);
        Assert.Equal(3, v["b"].Quantidade);
        Assert.True(v["b"][0].Booleano);
        Assert.False(v["b"][1].Booleano);
        Assert.True(v["b"][2].EhNulo);
        Assert.Equal("x", v["c"]["d"].Texto);
        Assert.Equal(-2000, v["e"].Numero);
        Assert.Equal(0, v["f"].Numero);
        Assert.Null(v["nao_existe"]);
        Assert.Equal(new[] { "a", "b", "c", "e", "f" }, v.Campos.Select(c => c.Key));
    }

    [Fact]
    public void Json_aceita_bom_e_espacos()
    {
        var v = TextoJson.Ler(((char)0xFEFF) + " \r\n\t[ ]\n");
        Assert.True(v.EhLista);
        Assert.Equal(0, v.Quantidade);
    }

    [Fact]
    public void Json_escapes_ida_e_volta()
    {
        string original = "aspas \" barra \\ / nova\nlinha\r tab\t ctrl" + (char)1 + " acento ção ø " + char.ConvertFromUtf32(0x1F600);
        var texto = TextoJson.Gravar(ValorJson.NovoObjeto().Definir("s", original));
        Assert.DoesNotContain("\r", texto);
        Assert.True(texto.IndexOf((char)1) < 0);   // (DoesNotContain compara pela cultura e ignora caracteres de controle)
        Assert.Contains("\\\"", texto);
        Assert.Contains("\\\\", texto);
        Assert.Contains("\\n", texto);
        Assert.Contains("\\t", texto);
        Assert.Contains("\\" + "u0001", texto);
        Assert.Contains("ção", texto);           // UTF-8 direto, sem \u
        Assert.Equal(original, TextoJson.Ler(texto)["s"].Texto);
    }

    [Fact]
    public void Json_le_escapes_unicode()
    {
        string u = "\\" + "u";
        var v = TextoJson.Ler("[\"" + u + "00e9" + u + "00C7\", \"" + u + "d83d" + u + "de00\", \"a\\/b\\b\\f\"]");
        Assert.Equal("éÇ", v[0].Texto);
        Assert.Equal(char.ConvertFromUtf32(0x1F600), v[1].Texto);
        Assert.Equal("a/b\b\f", v[2].Texto);
    }

    [Theory]
    [InlineData("1,5")]            // número pt-BR não é número
    [InlineData("{\"a\": 1,5}")]
    [InlineData("[1,5,]")]
    [InlineData("01")]
    [InlineData(".5")]
    [InlineData("1.")]
    [InlineData("1e")]
    [InlineData("-")]
    [InlineData("+1")]
    [InlineData("NaN")]
    [InlineData("\"1,5\"x")]
    public void Json_rejeita_numeros_invalidos(string texto)
    {
        Assert.Throws<ErroJson>(() => TextoJson.Ler(texto));
    }

    [Fact]
    public void Json_numero_pt_BR_em_campo_vira_erro_e_nao_numero()
    {
        // "1,5" dentro de uma lista é lido como dois números (1 e 5) — por isso o leitor dos contratos confere o tipo.
        var l = TextoJson.Ler("[1,5]");
        Assert.Equal(2, l.Quantidade);
        var e = Assert.Throws<ErroJson>(() => TextoJson.Ler("{\"escala\": 1,5}"));
        Assert.Equal(1, e.Linha);
        Assert.Equal(14, e.Coluna);  // depois da vírgula esperava o nome de um campo
    }

    [Fact]
    public void Json_grava_numeros_em_invariant_mesmo_com_cultura_pt_BR()
    {
        var antes = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("pt-BR");
            var l = ValorJson.NovaLista().Adicionar(ValorJson.De(1.5)).Adicionar(ValorJson.De(-0.1)).Adicionar(ValorJson.De(20))
                .Adicionar(ValorJson.De(1e20)).Adicionar(ValorJson.De(0.1 + 0.2)).Adicionar(ValorJson.De(double.NaN));
            var s = TextoJson.Gravar(l);
            Assert.Equal("[1.5, -0.1, 20, 1E+20, 0.30000000000000004, null]", s);
            var r = TextoJson.Ler(s);
            Assert.Equal(1.5, r[0].Numero);
            Assert.Equal(0.1 + 0.2, r[4].Numero);
            Assert.Equal(1e20, r[3].Numero);
        }
        finally { CultureInfo.CurrentCulture = antes; }
    }

    [Theory]
    [InlineData("{\n  \"a\": tru\n}", 2, 8)]
    [InlineData("[1 2]", 1, 4)]
    [InlineData("{\"a\" 1}", 1, 6)]
    [InlineData("{\n\"a\": \"sem fim", 2, 14)]
    [InlineData("{\"a\": 1,\n  }", 2, 3)]
    [InlineData("", 1, 1)]
    [InlineData("[1] [2]", 1, 5)]
    [InlineData("\"quebra\nlinha\"", 1, 8)]
    public void Json_erros_com_linha_e_coluna(string texto, int linha, int coluna)
    {
        var e = Assert.Throws<ErroJson>(() => TextoJson.Ler(texto));
        Assert.Equal(linha, e.Linha);
        Assert.Equal(coluna, e.Coluna);
        Assert.Contains($"linha {linha}, coluna {coluna}", e.Message);
        Assert.False(TextoJson.TentarLer(texto, out _, out var msg));
        Assert.Equal(e.Message, msg);
    }

    [Fact]
    public void Json_gravacao_indentada()
    {
        var v = ValorJson.NovoObjeto()
            .Definir("versao", 1)
            .Definir("pontos", ValorJson.NovaLista().Adicionar(ValorJson.NovaLista().Adicionar(ValorJson.De(0)).Adicionar(ValorJson.De(1.5))))
            .Definir("vazio", ValorJson.NovoObjeto())
            .Definir("lista", ValorJson.NovaLista())
            .Definir("ok", true)
            .Definir("nada", ValorJson.Nulo);
        Assert.Equal("{\n  \"versao\": 1,\n  \"pontos\": [\n    [0, 1.5]\n  ],\n  \"vazio\": {},\n  \"lista\": [],\n  \"ok\": true,\n  \"nada\": null\n}",
            TextoJson.Gravar(v));
        Assert.Equal("{\"versao\":1,\"pontos\":[[0,1.5]],\"vazio\":{},\"lista\":[],\"ok\":true,\"nada\":null}", TextoJson.Gravar(v, indentado: false));
    }

    [Fact]
    public void Json_campo_repetido_vale_o_ultimo_e_definir_mantem_posicao()
    {
        var v = TextoJson.Ler("{\"a\": 1, \"b\": 2, \"a\": 3}");
        Assert.Equal(3, v["a"].Numero);
        Assert.Equal(new[] { "a", "b" }, v.Campos.Select(c => c.Key));
    }

    [Fact]
    public void Json_arquivo_utf8_sem_bom()
    {
        var pasta = PastaTemp();
        try
        {
            var arq = Path.Combine(pasta, "sub", "a.json");
            TextoJson.GravarArquivo(arq, ValorJson.NovoObjeto().Definir("t", "SECUNDÁRIA ø"));
            var bytes = File.ReadAllBytes(arq);
            Assert.NotEqual(0xEF, bytes[0]);
            Assert.Equal("SECUNDÁRIA ø", TextoJson.LerArquivo(arq)["t"].Texto);
        }
        finally { Directory.Delete(pasta, true); }
    }

    // ================================================================= contratos: ida e volta

    private static ElementoBloco BlocoExemplo() => new ElementoBloco
    {
        Nome = "B15", Pilares = { "P1", "P67" }, Repeticoes = 24, Fck = 35, Aco = "CA-60",
        Niveis = { new NivelPilar { Pilar = "P1", CotaM = -3.43 }, new NivelPilar { Pilar = "P67", CotaM = -11.63 } },
        Armaduras =
        {
            new ArmaduraCalculada { Funcao = Funcoes.Principal, Bitola = 16, Quantidade = 5 },
            new ArmaduraCalculada { Funcao = Funcoes.CaliceTopo, Bitola = 12.5, Espacamento = 5.5 },
        },
        Arranjo = ArranjoEstacas.Triangulo, EstacasX = 3, EstacasY = 1, EspX = 150, EspY = 130, DiamEstaca = 50, DistFace = 40,
        EmbutimentoEstaca = 7, AlturaBase = 105, Lastro = 6, PilarBx = 40, PilarBy = 45, PilarEx = 1.5, PilarEy = -2.25,
        Ligacao = LigacaoPilar.CaliceEmbutido, AlturaColarinho = 30, ParedeCalice = 25, FolgaCalice = 5, Lemb = 65,
        Regularizacao = 4, Interface = InterfaceCalice.Rugosa, ArranqueNx = 3, ArranqueNy = 4, ArranqueCobrimento = 2.5,
    };

    [Fact]
    public void Elemento_bloco_ida_e_volta()
    {
        var b = BlocoExemplo();
        var texto = SerializacaoMotor.GravarElemento(b);
        Assert.StartsWith("{\n  \"versao\": 1,\n  \"tipo\": \"bloco_estacas\"", texto);
        Assert.Contains("\"arranjo\": \"Triangulo\"", texto);
        Assert.Contains("\"ligacao\": \"CaliceEmbutido\"", texto);
        Assert.Contains("\"interface\": \"Rugosa\"", texto);
        var r = Assert.IsType<ElementoBloco>(SerializacaoMotor.LerElemento(texto));
        Assert.Equal(texto, SerializacaoMotor.GravarElemento(r));
        Assert.Equal("B15", r.Nome);
        Assert.Equal(new[] { "P1", "P67" }, r.Pilares);
        Assert.Equal(-11.63, r.Niveis[1].CotaM);
        Assert.Equal(12.5, r.Armadura(Funcoes.CaliceTopo).Bitola);
        Assert.Equal(5.5, r.Armadura(Funcoes.CaliceTopo).Espacamento);
        Assert.Equal(ArranjoEstacas.Triangulo, r.Arranjo);
        Assert.Equal(LigacaoPilar.CaliceEmbutido, r.Ligacao);
        Assert.Equal(InterfaceCalice.Rugosa, r.Interface);
        Assert.Equal(-2.25, r.PilarEy);
        Assert.Equal(4, r.ArranqueNy);
    }

    [Fact]
    public void Elemento_campo_ausente_fica_com_o_padrao_e_desconhecido_e_ignorado()
    {
        var r = Assert.IsType<ElementoBloco>(SerializacaoMotor.LerElemento(
            "{\"tipo\": \"bloco_estacas\", \"nome\": \"B1\", \"campoNovo\": {\"x\": [1, 2]}, \"alturaBase\": null, \"ligacao\": \"arranque\"}"));
        var padrao = new ElementoBloco();
        Assert.Equal("B1", r.Nome);
        Assert.Equal(padrao.EspX, r.EspX);
        Assert.Equal(padrao.AlturaBase, r.AlturaBase);    // null = ausente
        Assert.Equal(padrao.Fck, r.Fck);
        Assert.Equal(LigacaoPilar.Arranque, r.Ligacao);   // enum sem diferenciar maiúsculas
        Assert.Empty(r.Armaduras);
    }

    [Theory]
    [InlineData("{\"nome\": \"B1\"}", "tipo")]
    [InlineData("{\"tipo\": \"sapata_xyz\"}", "sapata_xyz")]
    [InlineData("{\"versao\": 2, \"tipo\": \"bloco_estacas\"}", "versão 2")]
    [InlineData("{\"tipo\": \"bloco_estacas\", \"ligacao\": \"Solda\"}", "elemento.ligacao")]
    [InlineData("{\"tipo\": \"bloco_estacas\", \"ligacao\": 1}", "elemento.ligacao")]
    [InlineData("{\"tipo\": \"bloco_estacas\", \"espX\": \"120\"}", "elemento.espX")]
    [InlineData("{\"tipo\": \"bloco_estacas\", \"estacasX\": 2.5}", "inteiro")]
    [InlineData("{\"tipo\": \"bloco_estacas\", \"armaduras\": [{\"bitola\": true}]}", "elemento.armaduras[0].bitola")]
    [InlineData("[]", "objeto")]
    public void Elemento_invalido_da_FormatException_com_o_caminho(string texto, string trecho)
    {
        var e = Assert.ThrowsAny<FormatException>(() => SerializacaoMotor.LerElemento(texto));
        Assert.Contains(trecho, e.Message);
    }

    private sealed class ElementoTeste : ElementoEstrutural
    {
        public override string Tipo => "teste_frente_a";
        public double Largura { get; set; } = 10;
    }

    [Fact]
    public void Fabrica_de_elementos_aceita_novos_tipos()
    {
        SerializacaoMotor.RegistrarElemento<ElementoTeste>("teste_frente_a", () => new ElementoTeste(),
            (e, o) => o.Definir("largura", e.Largura),
            (o, e, c) => { if (o["largura"] != null) e.Largura = o["largura"].Numero; });
        Assert.Contains("teste_frente_a", SerializacaoMotor.TiposRegistrados);
        Assert.Contains(ElementoBloco.TipoId, SerializacaoMotor.TiposRegistrados);
        var t = SerializacaoMotor.GravarElemento(new ElementoTeste { Nome = "X", Largura = 33 });
        var r = Assert.IsType<ElementoTeste>(SerializacaoMotor.LerElemento(t));
        Assert.Equal(33, r.Largura);
        Assert.Equal("X", r.Nome);
    }

    [Fact]
    public void Sobrescritas_ida_e_volta()
    {
        var lista = new List<Sobrescrita>
        {
            new Sobrescrita { Posicao = "principal_x", Parametro = "perna", Valor = 68 },
            new Sobrescrita { Posicao = "calice_topo", Parametro = "espacamento", Valor = 7.5 },
        };
        var texto = SerializacaoMotor.GravarSobrescritas(lista);
        Assert.StartsWith("{\n  \"versao\": 1,", texto);
        var r = SerializacaoMotor.LerSobrescritas(texto);
        Assert.Equal(2, r.Count);
        Assert.Equal("principal_x", r[0].Posicao);
        Assert.Equal("perna", r[0].Parametro);
        Assert.Equal(68, r[0].Valor);
        Assert.Equal(7.5, r[1].Valor);
        Assert.Empty(SerializacaoMotor.LerSobrescritas("{\"versao\": 1}"));
        Assert.Empty(SerializacaoMotor.LerSobrescritas(SerializacaoMotor.GravarSobrescritas(new Sobrescrita[0])));
    }

    private static ModeloArmadura ModeloExemplo()
    {
        var m = new ModeloArmadura { TipoElemento = ElementoBloco.TipoId, Nome = "B28" };
        m.Niveis["fundo"] = 0; m.Niveis["topo_base"] = 60; m.Niveis["topo"] = 100; m.Niveis["fundo_pilar"] = 50;
        m.Avisos.Add("Embutimento 38 cm < 40 cm (NBR 9062 7.7.2.4).");
        m.Corpos.Add(new CorpoConcreto { Papel = "bloco", Planta = { (0, 0), (190, 0), (190, 70), (0, 70) }, Z0 = 0, Z1 = 60 });
        m.Corpos.Add(new CorpoConcreto { Papel = "estaca", Centro = (35, 35), Raio = 20, Z0 = -100, Z1 = 5 });
        m.Corpos.Add(new CorpoConcreto { Papel = "furo_calice", Planta = { (61, 6), (89, 6), (89, 64), (61, 64) }, Z0 = 55, Z1 = 100, Vazio = true });
        var p = new PosicaoArmadura
        {
            Chave = "principal_x", Funcao = Funcoes.Principal, Nome = "Principal inferior", Forma = FormaBarra.U, Bitola = 16, Aco = "CA-50",
            Vertices = { new P3(4, 10, 56), new P3(4, 10, 4), new P3(186, 10, 4), new P3(186, 10, 56) },
            Formatos = { FormatoSegmento.Linha, FormatoSegmento.Gancho90, FormatoSegmento.Linha },
            Distribuicao = new Distribuicao { Inicio = new P3(4, 10, 4), Fim = new P3(4, 60, 4), Quantidade = 5, Espacamento = 12.5, Repeticoes = 2 },
        };
        p.Parametros["perna"] = 68; p.Parametros["quantidade"] = 5;
        p.Regras["perna"] = "topo da base − cobrimento";
        p.Manuais.Add("perna");
        m.Posicoes.Add(p);
        var v = new PosicaoArmadura
        {
            Chave = "secundaria_var", Funcao = Funcoes.Secundaria, Forma = FormaBarra.Generica, Bitola = 10, Fechada = true,
            Vertices = { new P3(0, 0, 8), new P3(100, 0, 8) },
            ComprimentosVar = { 140.5, 120, 99.75 },
            SegmentosVar = { 1, 0 },
            VerticesVar = { new List<P3> { new P3(0, 0, 8), new P3(100, 0, 8) }, new List<P3> { new P3(0, 10, 8), new P3(80, 10, 8) } },
        };
        m.Posicoes.Add(v);
        return m;
    }

    [Fact]
    public void Modelo_ida_e_volta_com_VAR_manuais_e_enums()
    {
        var m = ModeloExemplo();
        var texto = SerializacaoMotor.GravarModelo(m);
        Assert.Contains("\"forma\": \"U\"", texto);
        Assert.Contains("\"formatos\": [\"Linha\", \"Gancho90\", \"Linha\"]", texto);
        Assert.Contains("[4, 10, 56]", texto);
        var r = SerializacaoMotor.LerModelo(texto);
        Assert.Equal(texto, SerializacaoMotor.GravarModelo(r));

        Assert.Equal(100, r.Niveis["topo"]);
        Assert.Single(r.Avisos);
        Assert.Equal(3, r.Corpos.Count);
        Assert.Equal((190.0, 70.0), r.Corpos[0].Planta[2]);
        Assert.Equal((35.0, 35.0), r.Corpos[1].Centro);
        Assert.True(r.Corpos[2].Vazio);

        var p = r.Posicao("principal_x");
        Assert.Equal(FormaBarra.U, p.Forma);
        Assert.Equal(new P3(186, 10, 56), p.Vertices[3]);
        Assert.Equal(FormatoSegmento.Gancho90, p.Formatos[1]);
        Assert.Equal(68, p.Parametros["perna"]);
        Assert.Equal("topo da base − cobrimento", p.Regras["perna"]);
        Assert.Contains("perna", p.Manuais);
        Assert.Equal(new P3(4, 60, 4), p.Distribuicao.Fim);
        Assert.Equal(12.5, p.Distribuicao.Espacamento);
        Assert.Equal(2, p.Distribuicao.Repeticoes);

        var v = r.Posicao("secundaria_var");
        Assert.True(v.Fechada);
        Assert.Equal(FormaBarra.Generica, v.Forma);
        Assert.Equal(new[] { 140.5, 120, 99.75 }, v.ComprimentosVar);
        Assert.True(v.SegmentosVar.SetEquals(new[] { 0, 1 }));
        Assert.Equal(2, v.VerticesVar.Count);
        Assert.Equal(new P3(80, 10, 8), v.VerticesVar[1][1]);
        Assert.Empty(v.Manuais);
    }

    [Fact]
    public void Modelo_campos_ausentes_ficam_com_o_padrao()
    {
        var r = SerializacaoMotor.LerModelo("{\"posicoes\": [{\"chave\": \"a\", \"desconhecido\": 1}]}");
        var p = r.Posicao("a");
        Assert.Equal("CA-50", p.Aco);
        Assert.Equal(1, p.Distribuicao.Quantidade);
        Assert.Equal(1, p.Distribuicao.Repeticoes);
        Assert.Empty(p.Vertices);
        Assert.Empty(r.Corpos);
        Assert.Throws<FormatException>(() => SerializacaoMotor.LerModelo("{\"posicoes\": [{\"vertices\": [[1, 2]]}]}"));
        Assert.Throws<FormatException>(() => SerializacaoMotor.LerModelo("{\"posicoes\": [{\"formatos\": [\"Gancho91\"]}]}"));
    }

    [Fact]
    public void Perfil_ida_e_volta()
    {
        var p = PerfisFabrica.BpmBloco();
        var texto = SerializacaoMotor.GravarPerfil(p);
        Assert.StartsWith("{\n  \"versao\": 1,\n  \"id\": \"bpm_bloco\"", texto);
        Assert.Contains("\"tipo\": \"FormaPlanta\"", texto);
        Assert.Contains("\"cobrimento.solo\": 4", texto);
        var r = SerializacaoMotor.LerPerfil(texto);
        Assert.Equal(texto, SerializacaoMotor.GravarPerfil(r));
        Assert.Equal(p.Folha.Vistas.Count, r.Folha.Vistas.Count);
        Assert.Equal(TipoVista.Corte, r.Folha.Vistas.First(v => v.Id == "corte_b").Tipo);
        Assert.Equal("y", r.Folha.Vistas.First(v => v.Id == "corte_b").Plano);
    }

    [Fact]
    public void Perfil_folha_ausente_fica_com_o_padrao()
    {
        var r = SerializacaoMotor.LerPerfil("{\"id\": \"x\", \"padroes\": {\"a.b\": 1}}");
        Assert.Equal(1, r.Padrao("a.b", 0));
        Assert.Equal(20, r.Folha.Escala);
        Assert.True(r.Folha.RelacaoAco);
        var v = SerializacaoMotor.LerPerfil("{\"folha\": {\"vistas\": [{\"id\": \"v\"}]}}").Folha.Vistas[0];
        Assert.Equal(1, v.Quadro);
        Assert.Equal(TipoVista.FormaPlanta, v.Tipo);
    }

    // ================================================================= perfil de fábrica

    [Fact]
    public void Fabrica_bpm_bloco_tem_as_chaves_da_frente_B()
    {
        var p = PerfisFabrica.Obter("bpm_bloco");
        Assert.NotNull(p);
        Assert.Equal("bpm_bloco", p.Id);
        Assert.Equal("bloco_estacas", p.TipoElemento);
        var esperado = new Dictionary<string, double>
        {
            ["cobrimento.solo"] = 4, ["cobrimento.calice"] = 2, ["secundaria.perna"] = 20, ["superior.perna"] = 20,
            ["suspensao.desconto_perna_externa"] = 6, ["arranque.pe"] = 20, ["arranque.espera"] = 0, ["calice.grampos"] = 1,
            ["principal.perna"] = 0,
        };
        Assert.Equal(esperado.OrderBy(k => k.Key), p.Padroes.OrderBy(k => k.Key));
        Assert.Equal("altura_base", p.Regra("principal.perna", ""));
        Assert.Equal("fora", p.Regra("arranque.pe_sentido", ""));
        Assert.Equal(2, p.Regras.Count);

        var f = p.Folha;
        Assert.Equal(20, f.Escala);
        Assert.Equal("PT", f.Idioma);
        Assert.Equal("BLOCO {nome} - {repeticoes}x - {estacas} ø {diametro}", f.Titulo);
        Assert.Equal(new[] { "forma_planta", "forma_corte_a", "principal", "secundaria", "superior", "calice", "corte_a", "corte_b", "lateral" },
            f.Vistas.Select(v => v.Id));
        Assert.All(f.Vistas.Take(2), v => Assert.Equal(0, v.Quadro));
        Assert.All(f.Vistas.Skip(2), v => Assert.Equal(1, v.Quadro));
        Assert.All(f.Vistas, v => Assert.Contains("|", v.Titulo));
        Assert.Equal("CORTE A-A|SECTION A-A", f.Vistas.First(v => v.Id == "corte_a").Titulo);
        Assert.Equal("x", f.Vistas.First(v => v.Id == "corte_a").Plano);
        Assert.Equal("x", f.Vistas.First(v => v.Id == "forma_corte_a").Plano);
        Assert.Equal(new[] { "principal" }, f.Vistas.First(v => v.Id == "principal").Posicoes);
        Assert.Equal(new[] { "calice_topo", "calice", "calice_vertical", "suspensao", "fundo_calice", "arranque", "arranque_estribo" },
            f.Vistas.First(v => v.Id == "calice").Posicoes);
        Assert.Equal(new[] { "lateral", "estribo_vertical" }, f.Vistas.First(v => v.Id == "lateral").Posicoes);
        Assert.Equal(new[] { "principal", "secundaria", "superior", "calice" }, f.Vistas.Where(v => v.BarrasAbaixo).Select(v => v.Id));
        // vistas não repetem lugar na folha
        Assert.Equal(f.Vistas.Count, f.Vistas.Select(v => (v.Quadro, v.Linha, v.Ordem)).Distinct().Count());
        Assert.Equal(new[] { "chaves", "raio_curvatura", "emendas", "comprimento_barras", "ancoragem_estacas", "barra_variavel" }, f.Detalhes);
        Assert.True(f.RelacaoAco);
        Assert.True(f.InformacoesProducao);
        Assert.Equal(5, f.Notas.Count);
        Assert.StartsWith("1) CONCRETO DO BLOCO: fck = {fck} MPa;", f.Notas[0]);
        Assert.Equal("5) CONFERIR TODAS AS MEDIDAS NO LOCAL ANTES DA EXECUÇÃO.", f.Notas[4]);
    }

    [Fact]
    public void Fabrica_devolve_copia_nova()
    {
        var a = PerfisFabrica.BpmBloco();
        a.Padroes["cobrimento.solo"] = 99;
        a.Folha.Vistas.Clear();
        var b = PerfisFabrica.BpmBloco();
        Assert.Equal(4, b.Padrao("cobrimento.solo", 0));
        Assert.Equal(9, b.Folha.Vistas.Count);
        Assert.Null(PerfisFabrica.Obter("nao_existe"));
    }

    // ================================================================= repositório

    private static string PastaTemp()
    {
        var p = Path.Combine(Path.GetTempPath(), "pceb_perfis_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(p);
        return p;
    }

    private static void Escrever(string pasta, string nome, string texto) =>
        File.WriteAllText(Path.Combine(pasta, nome), texto, new System.Text.UTF8Encoding(false));

    [Fact]
    public void Repositorio_sem_arquivo_devolve_a_fabrica()
    {
        var pasta = PastaTemp();
        try
        {
            var r = new RepositorioPerfis(Path.Combine(pasta, "nao_existe")).Obter("bpm_bloco");
            Assert.Empty(r.Avisos);
            Assert.True(r.DeFabrica);
            Assert.Null(r.Arquivo);
            Assert.Equal(SerializacaoMotor.GravarPerfil(PerfisFabrica.BpmBloco()), SerializacaoMotor.GravarPerfil(r.Perfil));
            Assert.Empty(new RepositorioPerfis(null).Obter("bpm_bloco").Avisos);
        }
        finally { Directory.Delete(pasta, true); }
    }

    [Fact]
    public void Repositorio_mescla_padroes_e_regras_e_mantem_a_folha()
    {
        var pasta = PastaTemp();
        try
        {
            Escrever(pasta, "bpm_bloco.json", "{\n \"versao\": 1,\n \"nome\": \"BPM do escritório\",\n" +
                " \"padroes\": {\"secundaria.perna\": 25, \"novo.valor\": 3.5},\n \"regras\": {\"arranque.pe_sentido\": \"dentro\"},\n \"extra\": 1\n}");
            var r = new RepositorioPerfis(pasta).Obter("bpm_bloco");
            Assert.Empty(r.Avisos);
            Assert.EndsWith("bpm_bloco.json", r.Arquivo);
            var p = r.Perfil;
            Assert.Equal("BPM do escritório", p.Nome);
            Assert.Equal(25, p.Padrao("secundaria.perna", 0));
            Assert.Equal(3.5, p.Padrao("novo.valor", 0));
            Assert.Equal(4, p.Padrao("cobrimento.solo", 0));          // da fábrica
            Assert.Equal(20, p.Padrao("superior.perna", 0));
            Assert.Equal("dentro", p.Regra("arranque.pe_sentido", ""));
            Assert.Equal("altura_base", p.Regra("principal.perna", "")); // da fábrica
            Assert.Equal(SerializacaoMotor.ParaJson(PerfisFabrica.BpmBloco().Folha).ToString(), SerializacaoMotor.ParaJson(p.Folha).ToString());
            Assert.Equal("bloco_estacas", p.TipoElemento);
        }
        finally { Directory.Delete(pasta, true); }
    }

    [Fact]
    public void Repositorio_folha_do_arquivo_substitui_a_inteira()
    {
        var pasta = PastaTemp();
        try
        {
            Escrever(pasta, "bpm_bloco.json", "{\"folha\": {\"escala\": 25, \"idioma\": \"PT/EN\", \"vistas\": [{\"id\": \"so_planta\", \"tipo\": \"FormaPlanta\", \"quadro\": 0}]}}");
            var p = new RepositorioPerfis(pasta).Obter("bpm_bloco").Perfil;
            Assert.Equal(25, p.Folha.Escala);
            Assert.Equal("PT/EN", p.Folha.Idioma);
            Assert.Single(p.Folha.Vistas);
            Assert.Empty(p.Folha.Detalhes);   // não veio no arquivo: padrão do contrato, não o da fábrica
            Assert.Empty(p.Folha.Notas);
            Assert.Equal(4, p.Padrao("cobrimento.solo", 0));
        }
        finally { Directory.Delete(pasta, true); }
    }

    [Theory]
    [InlineData("{\"padroes\": {\"secundaria.perna\": 25,}}")]             // vírgula sobrando
    [InlineData("{\"padroes\": {\"secundaria.perna\": 2,5}}")]             // número pt-BR
    [InlineData("{\"padroes\": {\"secundaria.perna\": \"25\"}}")]          // texto no lugar de número
    [InlineData("{\"folha\": {\"vistas\": [{\"tipo\": \"Perspectiva\"}]}}")] // enum desconhecido
    [InlineData("{\"versao\": 7}")]
    [InlineData("[1, 2]")]
    [InlineData("")]
    public void Repositorio_arquivo_invalido_usa_a_fabrica_e_avisa(string conteudo)
    {
        var pasta = PastaTemp();
        try
        {
            Escrever(pasta, "bpm_bloco.json", conteudo);
            var r = new RepositorioPerfis(pasta).Obter("bpm_bloco");
            Assert.Single(r.Avisos);
            Assert.Contains("bpm_bloco.json", r.Avisos[0]);
            Assert.Contains("fábrica", r.Avisos[0]);
            Assert.Null(r.Arquivo);
            Assert.Equal(SerializacaoMotor.GravarPerfil(PerfisFabrica.BpmBloco()), SerializacaoMotor.GravarPerfil(r.Perfil));
        }
        finally { Directory.Delete(pasta, true); }
    }

    [Fact]
    public void Repositorio_erro_de_sintaxe_avisa_linha_e_coluna()
    {
        var pasta = PastaTemp();
        try
        {
            Escrever(pasta, "bpm_bloco.json", "{\n  \"padroes\": {\n    \"secundaria.perna\": 2,5\n  }\n}");
            var r = new RepositorioPerfis(pasta).Obter("bpm_bloco");
            Assert.Contains("linha 3, coluna 27", r.Avisos[0]);
        }
        finally { Directory.Delete(pasta, true); }
    }

    [Fact]
    public void Repositorio_id_divergente_e_id_inexistente()
    {
        var pasta = PastaTemp();
        try
        {
            Escrever(pasta, "bpm_bloco.json", "{\"id\": \"outro\", \"padroes\": {\"arranque.pe\": 30}}");
            var r = new RepositorioPerfis(pasta).Obter("bpm_bloco");
            Assert.Single(r.Avisos);
            Assert.Equal("bpm_bloco", r.Perfil.Id);
            Assert.Equal(30, r.Perfil.Padrao("arranque.pe", 0));

            var n = new RepositorioPerfis(pasta).Obter("nao_existe");
            Assert.Null(n.Perfil);
            Assert.Single(n.Avisos);

            var inv = new RepositorioPerfis(pasta).Obter("..\\fora");
            Assert.NotEmpty(inv.Avisos);
        }
        finally { Directory.Delete(pasta, true); }
    }

    [Fact]
    public void Repositorio_perfil_so_do_usuario()
    {
        var pasta = PastaTemp();
        try
        {
            Escrever(pasta, "meu_bloco.json", "{\"versao\": 1, \"nome\": \"Meu\", \"tipoElemento\": \"bloco_estacas\", \"padroes\": {\"cobrimento.solo\": 5}}");
            var r = new RepositorioPerfis(pasta).Obter("meu_bloco");
            Assert.Empty(r.Avisos);
            Assert.False(r.DeFabrica);
            Assert.Equal("meu_bloco", r.Perfil.Id);
            Assert.Equal(5, r.Perfil.Padrao("cobrimento.solo", 0));
            Assert.Single(r.Perfil.Padroes);
        }
        finally { Directory.Delete(pasta, true); }
    }

    [Fact]
    public void Repositorio_exporta_a_fabrica_e_lista()
    {
        var pasta = PastaTemp();
        try
        {
            var repo = new RepositorioPerfis(Path.Combine(pasta, "Perfis"));
            Assert.True(repo.ExportarFabrica("bpm_bloco", out var arq, out var erro), erro);
            Assert.True(File.Exists(arq));
            Assert.StartsWith("{\n  \"versao\": 1,", File.ReadAllText(arq));
            Assert.False(repo.ExportarFabrica("bpm_bloco", out _, out erro));      // já existe
            Assert.Contains("já existe", erro);
            Assert.True(repo.ExportarFabrica("bpm_bloco", out _, out _, sobrescrever: true));
            Assert.False(repo.ExportarFabrica("nao_existe", out _, out _));

            var r = repo.Obter("bpm_bloco");
            Assert.Empty(r.Avisos);
            Assert.Equal(arq, r.Arquivo);
            Assert.Equal(SerializacaoMotor.GravarPerfil(PerfisFabrica.BpmBloco()), SerializacaoMotor.GravarPerfil(r.Perfil));

            Escrever(repo.Pasta, "meu.json", "{\"nome\": \"Meu perfil\", \"tipoElemento\": \"bloco_estacas\"}");
            Escrever(repo.Pasta, "quebrado.json", "{");
            var lista = repo.Listar();
            var bpm = lista.Single(i => i.Id == "bpm_bloco");
            Assert.True(bpm.DeFabrica);
            Assert.Equal(arq, bpm.Arquivo);
            Assert.Null(bpm.Aviso);
            var meu = lista.Single(i => i.Id == "meu");
            Assert.False(meu.DeFabrica);
            Assert.Equal("Meu perfil", meu.Nome);
            Assert.NotNull(lista.Single(i => i.Id == "quebrado").Aviso);
            Assert.Equal(3, lista.Count);

            Assert.Single(new RepositorioPerfis(Path.Combine(pasta, "vazia")).Listar());
        }
        finally { Directory.Delete(pasta, true); }
    }
}
