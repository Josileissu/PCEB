using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DEB.Core.Motor;
using DEB.Core.Motor.Componentes;
using DEB.Core.Motor.Receitas;
using Xunit;

namespace DEB.Core.Tests.Motor;

/// <summary>Frente R do motor: fórmulas, variáveis do elemento, receita em JSON, executor, repositório e validador.</summary>
public class ReceitasTests
{
    // ================================================================= componentes falsos

    /// <summary>Publica "largura" = base × fator.</summary>
    private sealed class CompPublica : IComponenteComSaidas
    {
        public string Tipo => "publica_largura";
        public IReadOnlyDictionary<string, string> Argumentos { get; } = new Dictionary<string, string> { ["base"] = "cm", ["fator"] = "adimensional" };
        public IReadOnlyCollection<string> VariaveisPublicadas { get; } = new[] { "largura" };
        public void Aplicar(ContextoMotor ctx, ArgumentosComponente args) => ctx.Variaveis["largura"] = args.Num("base", 0) * args.Num("fator", 1);
    }

    /// <summary>Cria uma posição com a chave (texto), a bitola e o comprimento (parâmetro com sobrescrita).</summary>
    private sealed class CompPosicao : IComponente
    {
        public string Tipo => "posicao";
        public IReadOnlyDictionary<string, string> Argumentos { get; } = new Dictionary<string, string> { ["chave"] = "texto", ["bitola"] = "mm", ["comprimento"] = "cm" };
        public void Aplicar(ContextoMotor ctx, ArgumentosComponente args)
        {
            var p = ctx.NovaPosicao(args.Txt("chave", "?"), "principal", args.Txt("chave", "?"), FormaBarra.Reta);
            p.Bitola = args.Num("bitola", 0);
            ctx.Par(p, "comprimento", args.Num("comprimento", 0), "receita");
            ctx.Adicionar(p);
        }
    }

    private sealed class CompExplode : IComponente
    {
        public string Tipo => "explode";
        public IReadOnlyDictionary<string, string> Argumentos { get; } = new Dictionary<string, string>();
        public void Aplicar(ContextoMotor ctx, ArgumentosComponente args) => throw new InvalidOperationException("pane simulada");
    }

    private static CatalogoComponentes Catalogo() =>
        new CatalogoComponentes().Registrar(new CompPublica()).Registrar(new CompPosicao()).Registrar(new CompExplode());

    private static ElementoBloco Bloco()
    {
        var b = new ElementoBloco { Nome = "B1", AlturaBase = 60, DiamEstaca = 40, EmbutimentoEstaca = 5, Lemb = 35 };
        b.Pilares.Add("P1"); b.Pilares.Add("P2");
        b.Armaduras.Add(new ArmaduraCalculada { Funcao = Funcoes.Principal, Bitola = 16, Quantidade = 6 });
        return b;
    }

    private static PerfilDetalhamento Perfil()
    {
        var p = new PerfilDetalhamento { Id = "teste", TipoElemento = ElementoBloco.TipoId };
        p.Padroes["cobrimento.solo"] = 4;
        p.Padroes["secundaria.perna"] = 20;
        p.Regras["principal.perna"] = "altura_base";
        return p;
    }

    private static double N(string f, IVariaveisFormula v = null) => Formula.Compilar(f).AvaliarNumero(v);

    // ================================================================= fórmulas

    [Theory]
    [InlineData("1 + 2 * 3", 7)]
    [InlineData("(1 + 2) * 3", 9)]
    [InlineData("10 - 4 - 3", 3)]
    [InlineData("12 / 3 / 2", 2)]
    [InlineData("2 ^ 3 ^ 2", 512)]
    [InlineData("-2 ^ 2", -4)]
    [InlineData("2 ^ -1", 0.5)]
    [InlineData("- -3", 3)]
    [InlineData("3 − 1", 2)]
    [InlineData("1.5e1", 15)]
    [InlineData(".5 * 4", 2)]
    [InlineData("1 + 2 < 4", 1)]
    [InlineData("1 < 2 && 2 < 1", 0)]
    [InlineData("1 < 2 || 2 < 1", 1)]
    [InlineData("!0 + 1", 2)]
    [InlineData("1 == 1 && 2 != 3", 1)]
    [InlineData("0.1 + 0.2 == 0.3", 1)]
    public void Formula_precedencia_e_operadores(string f, double esperado) => Assert.Equal(esperado, N(f), 9);

    [Theory]
    [InlineData("min(3, 1, 2)", 1)]
    [InlineData("max(3, 7, 2, 5)", 7)]
    [InlineData("max(4)", 4)]
    [InlineData("abs(-2.5)", 2.5)]
    [InlineData("floor(2.7)", 2)]
    [InlineData("ceil(2.1)", 3)]
    [InlineData("ceil(3)", 3)]
    [InlineData("round(2.5)", 3)]
    [InlineData("round(2.345, 2)", 2.35)]
    [InlineData("round(1234, -2)", 1200)]
    [InlineData("sqrt(16)", 4)]
    [InlineData("se(1 > 0, 10, 20)", 10)]
    [InlineData("se(0, 10, 20)", 20)]
    public void Formula_funcoes(string f, double esperado) => Assert.Equal(esperado, N(f), 9);

    [Fact]
    public void Formula_se_e_preguicoso()
    {
        // o ramo não escolhido teria divisão por zero e variável inexistente
        Assert.Equal(5, N("se(1, 5, 1 / 0)"));
        Assert.Equal(5, N("se(0, nao_existe, 5)"));
        Assert.Equal(0, N("0 && nao_existe"));
        Assert.Equal(1, N("1 || 1 / 0"));
    }

    [Fact]
    public void Formula_variaveis_com_pontos_e_textos()
    {
        var v = new VariaveisSimples().Com("c", 4).Com("embutimento_estaca", 5).Com("perfil.cobrimento.solo", 4)
            .Com("ligacao", "CaliceColarinho").Com("h_b", 60);
        v.Regras["principal.perna"] = "altura_base";
        v.Padroes["x.y"] = 7;
        Assert.Equal(5, N("max(c, embutimento_estaca)", v));
        Assert.Equal(51, N("h_b - perfil.cobrimento.solo - max(c, embutimento_estaca)", v));
        Assert.Equal(1, N("ligacao == 'CaliceColarinho'", v));
        Assert.Equal(0, N("ligacao != 'CaliceColarinho'", v));
        Assert.Equal(1, N("'abc' < 'abd'", v));
        Assert.Equal(1, N("regra('principal.perna') == 'altura_base'", v));
        Assert.Equal(1, N("regra('nao.tem') == ''", v));
        Assert.Equal(1, N("regra('nao.tem', 'valor') == 'valor'", v));
        Assert.Equal(7, N("padrao('x.y', 3)", v));
        Assert.Equal(3, N("padrao('nao.tem', 3)", v));
        Assert.Equal(60, N("se(regra('principal.perna') == 'altura_base', h_b, 20)", v));
        var t = Formula.Calcular("se(ligacao == 'Arranque', 'arr', 'cal''ice')", v);
        Assert.True(t.EhTexto);
        Assert.Equal("cal'ice", t.Texto);

        var f = Formula.Compilar("max(c, embutimento_estaca) + regra('a.b') + padrao('p.q', 1)");
        Assert.Equal(new[] { "c", "embutimento_estaca" }, f.Variaveis.Select(r => r.Nome));
        Assert.Equal(new[] { "a.b" }, f.RegrasUsadas);
        Assert.Equal(new[] { "p.q" }, f.PadroesUsados);
    }

    [Fact]
    public void Formula_compilada_avalia_varias_vezes()
    {
        var f = Formula.Compilar("hB - c");
        Assert.Equal(56, f.AvaliarNumero(new VariaveisSimples().Com("hB", 60).Com("c", 4)));
        Assert.Equal(46, f.AvaliarNumero(new VariaveisSimples().Com("hB", 50).Com("c", 4)));
    }

    [Theory]
    [InlineData("1 +", 4, "termina no meio")]
    [InlineData("(1 + 2", 7, "falta ')'")]
    [InlineData("1 + 2)", 6, "')' sem")]
    [InlineData("1,5", 2, "ponto")]
    [InlineData("a = 1", 3, "==")]
    [InlineData("'abc", 1, "aspa simples")]
    [InlineData("\"abc\"", 1, "aspas simples")]
    [InlineData("maxx(1, 2)", 1, "max")]
    [InlineData("round(1, 2, 3)", 1, "1 a 2")]
    [InlineData("se(1, 2)", 1, "3 argumento")]
    [InlineData("2x", 2, "use *")]
    [InlineData("a. + 1", 2, "ponto")]
    [InlineData("1 # 2", 3, "inesperado")]
    [InlineData("", 1, "vazia")]
    [InlineData("max", 1, "falta o '('")]
    public void Formula_erro_de_sintaxe_com_posicao(string f, int posicao, string trecho)
    {
        var e = Assert.Throws<ErroFormula>(() => Formula.Compilar(f));
        Assert.Equal(posicao, e.Posicao);
        Assert.Contains(trecho, e.Message);
        Assert.Contains("posição " + posicao, e.Message);
    }

    [Fact]
    public void Formula_variavel_desconhecida_sugere_a_mais_parecida()
    {
        var v = new VariaveisSimples().Com("altura_base", 60).Com("lastro", 5);
        var e = Assert.Throws<ErroFormula>(() => N("1 + altura_bse", v));
        Assert.Equal(5, e.Posicao);
        Assert.Contains("\"altura_bse\"", e.Message);
        Assert.Contains("você quis dizer \"altura_base\"", e.Message);
        var e2 = Assert.Throws<ErroFormula>(() => N("zzz", v));
        Assert.DoesNotContain("quis dizer", e2.Message);
    }

    [Fact]
    public void Formula_erros_de_avaliacao()
    {
        var v = new VariaveisSimples().Com("t", "Grade");
        Assert.Contains("divisão por zero", Assert.Throws<ErroFormula>(() => N("1 / (2 - 2)")).Message);
        Assert.Contains("texto com número", Assert.Throws<ErroFormula>(() => N("t == 1", v)).Message);
        Assert.Contains("precisa de número", Assert.Throws<ErroFormula>(() => N("t + 1", v)).Message);
        Assert.Contains("negativo", Assert.Throws<ErroFormula>(() => N("sqrt(-1)")).Message);
        Assert.Contains("preciso um número", Assert.Throws<ErroFormula>(() => N("t", v)).Message);
        Assert.Contains("verdadeiro/falso", Assert.Throws<ErroFormula>(() => Formula.Compilar("t").AvaliarCondicao(v)).Message);
    }

    // ================================================================= variáveis do elemento

    [Fact]
    public void Variaveis_do_bloco_por_reflexao()
    {
        var vars = new VariaveisElemento(Bloco(), Perfil());
        Assert.Equal(60, N("altura_base", vars));
        Assert.Equal(40, N("diam_estaca", vars));
        Assert.Equal(120, N("esp_x", vars));
        Assert.Equal(1, N("ligacao == 'CaliceColarinho'", vars));
        Assert.Equal(1, N("arranjo == 'Grade'", vars));
        Assert.Equal(1, N("tipo == 'bloco_estacas' && nome == 'B1'", vars));
        Assert.Equal(16, N("arm.principal.bitola", vars));
        Assert.Equal(6, N("arm.principal.quantidade", vars));
        Assert.Equal(0, N("arm.secundaria.bitola", vars));         // ausente → 0
        Assert.Equal(0, N("arm.calice_topo.espacamento", vars));
        Assert.Equal(2, N("n_pilares", vars));
        Assert.Equal(2, N("repeticoes", vars));                    // 0 no elemento = nº de pilares
        Assert.Equal(4, N("perfil.cobrimento.solo", vars));
        Assert.Equal(1, N("regra('principal.perna') == 'altura_base'", vars));
        Assert.Equal(55, N("altura_base - max(perfil.cobrimento.solo, embutimento_estaca)", vars));

        var e = Assert.Throws<ErroFormula>(() => N("arm.principall.bitola", vars));
        Assert.Contains("arm.principal.bitola", e.Message);
        Assert.Throws<ErroFormula>(() => N("perfil.nao_tem", vars));
    }

    [Fact]
    public void Variaveis_calculadas_tem_prioridade_e_snake_case()
    {
        var calc = new Dictionary<string, double> { ["altura_base"] = 99, ["z_inf"] = 5 };
        var vars = new VariaveisElemento(Bloco(), null, calc);
        Assert.Equal(99, N("altura_base", vars));
        Assert.Equal(5, N("z_inf", vars));
        calc["novo"] = 3;                                           // ao vivo
        Assert.Equal(3, N("novo", vars));
        vars.Definir("modo", ValorFormula.De("calice"));
        Assert.Equal(1, N("modo == 'calice'", vars));

        Assert.Equal("altura_base", VariaveisElemento.SnakeCase("AlturaBase"));
        Assert.Equal("diam_estaca", VariaveisElemento.SnakeCase("DiamEstaca"));
        Assert.Equal("esp_x", VariaveisElemento.SnakeCase("EspX"));
        Assert.Equal("pilar_bx", VariaveisElemento.SnakeCase("PilarBx"));
        Assert.Equal("xml_texto", VariaveisElemento.SnakeCase("XMLTexto"));
        Assert.Equal("lemb", VariaveisElemento.SnakeCase("Lemb"));
    }

    // ================================================================= receita JSON

    private const string ReceitaExemplo = @"{
  ""versao"": 1, ""tipoElemento"": ""bloco_estacas"", ""descricao"": ""teste"",
  ""variaveis"": [ {""nome"": ""c"", ""formula"": ""perfil.cobrimento.solo""},
                   {""nome"": ""z_inf"", ""formula"": ""max(c, embutimento_estaca)""},
                   {""nome"": ""modo"", ""formula"": ""se(ligacao == 'Arranque', 'arranque', 'calice')""} ],
  ""passos"": [
    {""componente"": ""publica_largura"", ""argumentos"": {""base"": ""altura_base - z_inf"", ""fator"": 2}},
    {""componente"": ""posicao"", ""nome"": ""principal"", ""se"": ""arm.principal.bitola > 0"",
     ""argumentos"": {""chave"": ""'principal_x'"", ""bitola"": ""arm.principal.bitola"", ""comprimento"": ""largura + 10""}},
    {""componente"": ""posicao"", ""nome"": ""secundária x"", ""se"": ""arm.secundaria.bitola > 0"",
     ""argumentos"": {""chave"": ""'secundaria_x'"", ""bitola"": ""arm.secundaria.bitola""}}
  ],
  ""verificacoes"": [ {""condicao"": ""lemb >= 40"", ""aviso"": ""Lemb {lemb} cm < 40 cm (NBR 9062 7.7); z_inf = {z_inf}, h/3 = {terco}, {nao_existe}.""},
                      {""se"": ""modo == 'arranque'"", ""condicao"": ""0"", ""aviso"": ""não deve aparecer""} ]
}";

    [Fact]
    public void Receita_json_ida_e_volta()
    {
        var r = SerializacaoReceita.Ler(ReceitaExemplo);
        Assert.Equal("bloco_estacas", r.TipoElemento);
        Assert.Equal(3, r.Variaveis.Count);
        Assert.Equal("max(c, embutimento_estaca)", r.Variaveis[1].Formula);
        Assert.Equal(3, r.Passos.Count);
        Assert.Equal("2", r.Passos[0].Argumentos["fator"]);           // número no JSON vira fórmula
        Assert.Equal("arm.principal.bitola > 0", r.Passos[1].Se);
        Assert.Null(r.Passos[0].Se);
        Assert.Equal(new[] { "chave", "bitola", "comprimento" }, r.Passos[1].Argumentos.Keys);
        Assert.Equal(2, r.Verificacoes.Count);
        Assert.Equal("modo == 'arranque'", r.Verificacoes[1].Se);

        var txt = SerializacaoReceita.Gravar(r);
        var r2 = SerializacaoReceita.Ler(txt);
        Assert.Equal(txt, SerializacaoReceita.Gravar(r2));
        Assert.Equal("secundária x", r2.Passos[2].Nome);
        Assert.Empty(SerializacaoReceita.ErrosDeSintaxe(r2));
    }

    [Fact]
    public void Receita_json_com_erro_de_campo()
    {
        Assert.Contains("receita.passos[0].componente", Assert.Throws<FormatException>(() =>
            SerializacaoReceita.Ler("{\"passos\": [ {\"argumentos\": {}} ]}")).Message);
        Assert.Contains("receita.variaveis[0].nome", Assert.Throws<FormatException>(() =>
            SerializacaoReceita.Ler("{\"variaveis\": [ {\"nome\": \"2a\", \"formula\": \"1\"} ]}")).Message);
        Assert.Contains("mais nova", Assert.Throws<FormatException>(() => SerializacaoReceita.Ler("{\"versao\": 9}")).Message);
        Assert.Contains("linha 1", Assert.Throws<DEB.Core.Persistencia.ErroJson>(() => SerializacaoReceita.Ler("{ x }")).Message);
    }

    // ================================================================= executor

    [Fact]
    public void Executor_publica_variavel_pula_se_falso_e_verifica()
    {
        var motor = new MotorReceita<ElementoBloco>(SerializacaoReceita.Ler(ReceitaExemplo), Catalogo());
        Assert.Equal("bloco_estacas", motor.TipoElemento);
        var m = motor.Gerar(Bloco(), Perfil(), new[] { new Sobrescrita { Posicao = "principal_x", Parametro = "bitola_x", Valor = 1 } });

        // largura = (60 − max(4, 5)) × 2 = 110 → comprimento = 120
        var p = m.Posicao("principal_x");
        Assert.NotNull(p);
        Assert.Equal(16, p.Bitola);
        Assert.Equal(120, p.Parametros["comprimento"]);
        Assert.Null(m.Posicao("secundaria_x"));                    // "se" falso: passo pulado, sem aviso
        Assert.DoesNotContain(m.Avisos, a => a.Contains("secundária"));

        // verificação: Lemb 35 < 40, com {} trocado (pt-BR) e marcador desconhecido mantido
        Assert.Contains("Lemb 35 cm < 40 cm (NBR 9062 7.7); z_inf = 5, h/3 = {terco}, {nao_existe}.", m.Avisos);
        Assert.DoesNotContain("não deve aparecer", m.Avisos);
        Assert.Contains(m.Avisos, a => a.StartsWith("Sobrescrita ignorada", StringComparison.Ordinal));   // Concluir() rodou
    }

    [Fact]
    public void Executor_componente_inexistente_e_excecao_viram_aviso()
    {
        var r = new Receita { TipoElemento = ElementoBloco.TipoId }
            .Variavel("x", "1 / 0")
            .Variavel("y", "x + 1")
            .Passo(new PassoReceita { Componente = "posicoa", Nome = "errado" })
            .Passo(new PassoReceita { Componente = "explode", Nome = "bomba" })
            .Passo(new PassoReceita { Componente = "posicao" }.Arg("chave", "'depois'").Arg("bitola", "10"))
            .Passo(new PassoReceita { Componente = "posicao", Se = "nao_tem > 0" }.Arg("chave", "'nunca'"))
            .Passo(new PassoReceita { Componente = "posicao" }.Arg("chave", "'arg_ruim'").Arg("bitola", "1 +"))
            .Verificacao("lemb >= 40", "Lemb {lemb} cm < 40 cm.", se: "ligacao != 'Arranque'");
        var m = new MotorReceita<ElementoBloco>(r, Catalogo()).Gerar(Bloco(), null, null);

        Assert.Contains(m.Avisos, a => a.Contains("variável \"x\"") && a.Contains("divisão por zero"));
        Assert.Contains(m.Avisos, a => a.Contains("variável \"y\"") && a.Contains("\"x\""));
        Assert.Contains(m.Avisos, a => a.Contains("passo 1 \"errado\" (posicoa)") && a.Contains("não existe") && a.Contains("\"posicao\"?"));
        Assert.Contains(m.Avisos, a => a.Contains("passo 2 \"bomba\" (explode) falhou") && a.Contains("pane simulada"));
        Assert.NotNull(m.Posicao("depois"));                       // o resto continua
        Assert.Null(m.Posicao("nunca"));
        Assert.Contains(m.Avisos, a => a.Contains("passo 4 (posicao), condição \"se\"") && a.Contains("nao_tem"));
        Assert.Null(m.Posicao("arg_ruim"));
        Assert.Contains(m.Avisos, a => a.Contains("passo 5 (posicao), argumento \"bitola\""));
        Assert.Contains("Lemb 35 cm < 40 cm.", m.Avisos);
    }

    [Fact]
    public void Executor_avisa_tipo_divergente_e_formata_decimal_com_virgula()
    {
        var r = new Receita { TipoElemento = "pilar" }.Variavel("v", "2.5").Verificacao("v > 3", "v = {v}");
        var m = new MotorReceita<ElementoBloco>(r, new CatalogoComponentes()).Gerar(Bloco(), null, null);
        Assert.Contains(m.Avisos, a => a.Contains("é para \"pilar\""));
        Assert.Contains("v = 2,5", m.Avisos);
    }

    // ================================================================= repositório

    private static string PastaTemp()
    {
        var p = Path.Combine(Path.GetTempPath(), "pceb_receitas_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(p);
        return p;
    }

    [Fact]
    public void Repositorio_fabrica_arquivo_e_invalido()
    {
        const string tipo = "teste_receita_repo";
        RepositorioReceitas.RegistrarFabrica(tipo, () => new Receita { Descricao = "fábrica" }.Variavel("a", "1"));
        Assert.Contains(tipo, RepositorioReceitas.TiposFabrica);
        var pasta = PastaTemp();
        try
        {
            var repo = new RepositorioReceitas(pasta);
            var r = repo.Obter(tipo);
            Assert.True(r.DeFabrica);
            Assert.Equal("fábrica", r.Receita.Descricao);
            Assert.Equal(tipo, r.Receita.TipoElemento);
            Assert.Empty(r.Avisos);

            // exportar e editar: o arquivo SUBSTITUI a fábrica inteira
            Assert.True(repo.ExportarFabrica(tipo, out var arq, out var erro), erro);
            Assert.False(repo.ExportarFabrica(tipo, out _, out var erro2));
            Assert.Contains("já existe", erro2);
            File.WriteAllText(arq, "{\"versao\": 1, \"tipoElemento\": \"outro\", \"descricao\": \"usuário\", \"variaveis\": [{\"nome\": \"b\", \"formula\": \"2\"}]}");
            r = repo.Obter(tipo);
            Assert.False(r.DeFabrica);
            Assert.Equal(arq, r.Arquivo);
            Assert.Equal("usuário", r.Receita.Descricao);
            Assert.Equal(new[] { "b" }, r.Receita.Variaveis.Select(v => v.Nome));   // sem mesclar
            Assert.Equal(tipo, r.Receita.TipoElemento);
            Assert.Contains(r.Avisos, a => a.Contains("\"outro\""));

            // JSON inválido → fábrica + aviso com linha/coluna
            File.WriteAllText(arq, "{\n  \"descricao\": \"x\",\n  \"passos\": [ { \"componente\": } ]\n}");
            r = repo.Obter(tipo);
            Assert.True(r.DeFabrica);
            Assert.Equal("fábrica", r.Receita.Descricao);
            Assert.Contains(r.Avisos, a => a.Contains("linha 3") && a.Contains("coluna") && a.Contains("vale a receita de fábrica"));

            // fórmula com erro de sintaxe → fábrica + aviso com posição
            File.WriteAllText(arq, "{\"variaveis\": [{\"nome\": \"b\", \"formula\": \"max(1,\"}]}");
            r = repo.Obter(tipo);
            Assert.True(r.DeFabrica);
            Assert.Contains(r.Avisos, a => a.Contains("variaveis[0] \"b\"") && a.Contains("posição"));

            // tipo sem fábrica: só o arquivo vale; sem nada: aviso
            File.WriteAllText(Path.Combine(pasta, "so_usuario.json"), "{\"descricao\": \"meu\"}");
            Assert.Equal("meu", repo.Obter("so_usuario").Receita.Descricao);
            var nada = repo.Obter("nao_existe_tipo");
            Assert.Null(nada.Receita);
            Assert.Contains(nada.Avisos, a => a.Contains("não existe"));
            Assert.False(repo.ExportarFabrica("nao_existe_tipo", out _, out _));
            Assert.NotNull(new RepositorioReceitas(null).Obter(tipo).Receita);
            Assert.EndsWith(Path.Combine("PCEB", "Receitas"), RepositorioReceitas.PastaPadrao());
        }
        finally { try { Directory.Delete(pasta, true); } catch { } }
    }

    // ================================================================= validador

    [Fact]
    public void Validador_receita_boa_sem_erros()
    {
        var r = SerializacaoReceita.Ler(ReceitaExemplo);
        var v = ValidadorReceita.Validar(r, Catalogo(), Bloco(), Perfil());
        Assert.True(v.Valida, v.ToString());
        // {terco} e {nao_existe} no aviso não existem (atenção: um componente sem saídas declaradas poderia publicá-las)
        Assert.Contains(v.Atencoes, p => p.Local.StartsWith("verificacoes[0]", StringComparison.Ordinal) && p.Mensagem.Contains("{terco}"));
    }

    [Fact]
    public void Validador_aponta_os_problemas()
    {
        var r = new Receita { TipoElemento = ElementoBloco.TipoId }
            .Variavel("c", "perfil.cobrimento.slo")
            .Variavel("h", "altura_bse - c")
            .Variavel("q", "max(1,")
            .Passo(new PassoReceita { Componente = "posicoa" })
            .Passo(new PassoReceita { Componente = "posicao", Se = "arm.principal.bitola > 0" }.Arg("chave", "'x'").Arg("bitolla", "16"))
            .Passo(new PassoReceita { Componente = "publica_largura" }.Arg("base", "10"))
            .Passo(new PassoReceita { Componente = "posicao" }.Arg("comprimento", "largura + regra('nao.tem')"))
            .Verificacao("lemb >= 40", "");
        var v = ValidadorReceita.Validar(r, Catalogo(), Bloco(), Perfil());
        Assert.False(v.Valida);
        string Msg(string local) => string.Join(" | ", v.Problemas.Where(p => p.Local.StartsWith(local, StringComparison.Ordinal)).Select(p => p.Mensagem));

        Assert.Contains("quis dizer \"perfil.cobrimento.solo\"", Msg("variaveis[0]"));
        Assert.Contains("quis dizer \"altura_base\"", Msg("variaveis[1]"));
        Assert.Contains("posição", Msg("variaveis[2]"));
        Assert.Contains("\"posicoa\" não existe — você quis dizer \"posicao\"", Msg("passo 1"));
        Assert.Contains("não tem este argumento — você quis dizer \"bitola\"", Msg("passo 2"));
        Assert.DoesNotContain("largura", Msg("passo 4"));   // publicada pelo passo 3 (IComponenteComSaidas)
        Assert.Contains("'nao.tem'", Msg("passo 4"));        // regra ausente no perfil: atenção
        Assert.Contains("sem texto", Msg("verificacoes[0]"));
        Assert.All(v.Problemas, p => Assert.False(string.IsNullOrEmpty(p.ToString())));
    }

    [Fact]
    public void Validador_componente_sem_saidas_declara_vira_atencao()
    {
        var r = new Receita { TipoElemento = ElementoBloco.TipoId }
            .Passo(new PassoReceita { Componente = "posicao" }.Arg("chave", "'a'"))
            .Passo(new PassoReceita { Componente = "posicao" }.Arg("comprimento", "algo_publicado"));
        var v = ValidadorReceita.Validar(r, Catalogo(), Bloco(), Perfil());
        Assert.True(v.Valida, v.ToString());
        Assert.Contains(v.Atencoes, p => p.Mensagem.Contains("algo_publicado") && p.Mensagem.Contains("componente anterior"));

        // sem elemento de exemplo: só sintaxe/componentes/argumentos
        var v2 = ValidadorReceita.Validar(new Receita().Variavel("x", "qualquer_coisa + 1"), Catalogo());
        Assert.True(v2.Valida, v2.ToString());
    }
}
