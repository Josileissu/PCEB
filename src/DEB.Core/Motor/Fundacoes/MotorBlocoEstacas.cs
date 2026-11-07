using System;
using System.Collections.Generic;
using DEB.Core.Configuracao;
using DEB.Core.Motor.Componentes;

namespace DEB.Core.Motor.Fundacoes;

/// <summary>
/// Motor de fundações — bloco sobre estacas (frente B, 2026-11-07; frente K, 2026-11-07: virou orquestrador de
/// COMPONENTES). Mesmas regras do <c>TemplateBlocoEstacas</c> (grade nx × ny ou triangular de 3 estacas; cálice com
/// colarinho, embutido ou arranque; bloco de 1 estaca sem principal), com saída no modelo neutro 3D
/// (<see cref="ModeloArmadura"/>). As regras moram nos componentes de <c>DEB.Core.Motor.Componentes</c>
/// (docs/04_arquitetura/componentes.md); aqui só a <see cref="ReceitaPadrao"/> — a sequência de componentes e os
/// argumentos — que depois sai do C# para uma receita JSON.
/// <para>Sistema local: x, y em planta (origem no canto inferior esquerdo da caixa envolvente do bloco), z para cima,
/// z = 0 no fundo da base. Vértices em cm com as MEDIDAS EXTERNAS da barra.</para>
/// <para>CHAVES DE POSIÇÃO (estáveis; as sobrescritas apontam para elas):
/// principal_x, principal_y (grade) | principal_lado1, principal_lado2, principal_lado3 (triangular);
/// secundaria_x, secundaria_y; superior_x, superior_y; lateral; estribo_vertical_x;
/// calice_topo, calice_grampos_topo_x, calice_grampos_topo_y, calice, calice_grampos_x, calice_grampos_y,
/// calice_vertical, suspensao, fundo_calice; arranque, arranque_estribo.</para>
/// <para>PERFIL: Padroes "cobrimento.solo"(4), "cobrimento.calice"(2), "secundaria.perna"(20), "superior.perna"(20),
/// "suspensao.desconto_perna_externa"(6), "arranque.pe"(20), "arranque.espera"(0 = traspasse NBR 6118), "calice.grampos"(1),
/// "principal.perna"(0), "fundo_calice.comprimento"(100); Regras "principal.perna" = "altura_base" | "valor",
/// "arranque.pe_sentido" = "fora" | "dentro".</para>
/// </summary>
public sealed class MotorBlocoEstacas : IMotorDetalhamento<ElementoBloco>
{
    /// <summary>Trecho do pilar representado acima do topo do bloco (corpo "pilar"), cm.</summary>
    public const double PilarAcimaDoTopo = 50;

    private readonly ConfiguracaoArmadura _cfg;
    public MotorBlocoEstacas() : this(null) { }
    /// <param name="cfg">regras de faixa (quantidade, espaçamento inicial) e de dobramento; null = padrão.</param>
    public MotorBlocoEstacas(ConfiguracaoArmadura cfg) { _cfg = cfg ?? new ConfiguracaoArmadura(); }

    public string TipoElemento => ElementoBloco.TipoId;

    public ModeloArmadura Gerar(ElementoBloco elemento, PerfilDetalhamento perfil, IReadOnlyList<Sobrescrita> sobrescritas)
    {
        if (elemento == null) throw new ArgumentNullException(nameof(elemento));
        var ctx = new ContextoMotor(elemento, perfil, sobrescritas, _cfg);
        foreach (var (componente, argumentos) in ReceitaPadrao(elemento, ctx))
            componente.Aplicar(ctx, argumentos());
        return ctx.Concluir();
    }

    // =================================================================================================================
    // RECEITA PADRÃO DO BLOCO (a ser substituída pela receita JSON). Cada passo = (componente, argumentos); os argumentos
    // são avaliados NA HORA do passo (Func), como fará o executor de receitas: assim um passo lê as variáveis publicadas
    // pelos anteriores (bitola_principal, z_topo_inferior, centro_apoios_x…). As variáveis do elemento e do perfil vão para
    // ctx.Variaveis no início (o bloco "variaveis" da receita).
    // =================================================================================================================

    /// <summary>Sequência de componentes que monta o bloco (ver docs/04_arquitetura/componentes.md). Preenche as
    /// variáveis da receita em <c>ctx.Variaveis</c> ao ser chamada.</summary>
    public static IReadOnlyList<(IComponente Componente, Func<ArgumentosComponente> Argumentos)> ReceitaPadrao(ElementoBloco el, ContextoMotor ctx)
    {
        var pf = ctx.Perfil;
        var v = ctx.Variaveis;
        // ---- variáveis da receita (elemento + perfil) ----
        bool calice = el.Ligacao != LigacaoPilar.Arranque, colar = el.Ligacao == LigacaoPilar.CaliceColarinho;
        v["cobrimento"] = pf.Padrao("cobrimento.solo", 4);
        v["cobrimento_calice"] = pf.Padrao("cobrimento.calice", 2);
        v["dist_face"] = el.DistFace;
        v["diametro_estaca"] = el.DiamEstaca;
        v["embutimento_estaca"] = Math.Max(0, el.EmbutimentoEstaca);
        v["altura_base"] = el.AlturaBase;
        v["lastro"] = Math.Max(0, el.Lastro);
        v["pilar_bx"] = el.PilarBx; v["pilar_by"] = el.PilarBy;
        v["calice"] = calice ? 1 : 0; v["colarinho"] = colar ? 1 : 0;
        v["altura_colarinho"] = colar ? Math.Max(0, el.AlturaColarinho) : 0;
        v["altura_total"] = v["altura_base"] + v["altura_colarinho"];
        v["embutimento"] = calice ? el.Lemb : 0;
        v["regularizacao"] = calice ? Math.Max(0, el.Regularizacao) : 0;
        v["folga"] = el.FolgaCalice; v["parede"] = el.ParedeCalice;
        v["z_inferior"] = Math.Max(v["cobrimento"], v["embutimento_estaca"]);   // face de baixo da armadura inferior

        double V(string n) => ctx.Var(n, 0);
        double B(bool b) => b ? 1 : 0;
        ArgumentosComponente Args() => new ArgumentosComponente();
        double Px() => V("centro_apoios_x") + el.PilarEx;
        double Py() => V("centro_apoios_y") + el.PilarEy;
        bool tri = el.Arranjo == ArranjoEstacas.Triangulo;
        bool pernaPorValor = pf.Regra("principal.perna", "altura_base") == "valor" && pf.Padrao("principal.perna", 0) > 0;
        double esperaPerfil = pf.Padrao("arranque.espera", 0);
        bool peDentro = pf.Regra("arranque.pe_sentido", "fora") == "dentro";
        var r = new List<(IComponente, Func<ArgumentosComponente>)>();
        void Passo(IComponente c, Func<ArgumentosComponente> a) => r.Add((c, a));

        // ---- geometria: apoios, corpos, níveis ----
        Passo(ComponentesPadrao.ApoiosEstacas, () => Args().Com("arranjo", tri ? "triangulo" : "grade")
            .Com("nx", el.EstacasX).Com("ny", el.EstacasY).Com("esp_x", el.EspX).Com("esp_y", el.EspY).Com("dist_face", V("dist_face")));
        Passo(ComponentesPadrao.CorpoPrismatico, () => Args().Com("papel", "bloco").Com("contorno", "contorno").Com("z0", 0).Com("z1", V("altura_base")));
        Passo(ComponentesPadrao.CaliceForma, () => Args().Com("ativo", V("calice"))
            .Com("centro_x", Px()).Com("centro_y", Py()).Com("pilar_bx", V("pilar_bx")).Com("pilar_by", V("pilar_by"))
            .Com("folga", V("folga")).Com("parede", V("parede")).Com("colarinho", V("colarinho"))
            .Com("z_base_colarinho", V("altura_base")).Com("z_topo", V("altura_total"))
            .Com("embutimento", V("embutimento")).Com("regularizacao", V("regularizacao")));
        Passo(ComponentesPadrao.CorpoPrismatico, () => Args().Com("papel", "pilar")
            .Com("x0", Px() - V("pilar_bx") / 2).Com("y0", Py() - V("pilar_by") / 2).Com("x1", Px() + V("pilar_bx") / 2).Com("y1", Py() + V("pilar_by") / 2)
            .Com("z0", calice ? V("altura_total") - V("embutimento") : V("altura_total")).Com("z1", V("altura_total") + PilarAcimaDoTopo));
        Passo(ComponentesPadrao.CorposCirculares, () => Args().Com("papel", "estaca").Com("pontos", "estacas").Com("raio", V("diametro_estaca") / 2)
            .Com("z0", -V("lastro") - Math.Max(25, 1.2 * V("diametro_estaca"))).Com("z1", V("embutimento_estaca")));
        Passo(ComponentesPadrao.CorpoPrismatico, () => Args().Com("ativo", B(V("lastro") > 0)).Com("papel", "lastro").Com("contorno", "contorno")
            .Com("z0", -V("lastro")).Com("z1", 0));
        Passo(ComponentesPadrao.Niveis, () =>
        {
            var a = Args().Com("fundo", 0).Com("topo_base", V("altura_base")).Com("topo", V("altura_total"));
            if (calice) a.Com("fundo_pilar", V("altura_total") - V("embutimento"));
            return a.Com("arrasamento", V("embutimento_estaca"));
        });

        // ---- armaduras da base ----
        Passo(ComponentesPadrao.BitolaEfetiva, () => Args().Com("funcao", Funcoes.Lateral).Com("posicao", "lateral").Com("variavel", "bitola_lateral"));
        Passo(ComponentesPadrao.FaixaSobreApoios, () => Args().Com("funcao", Funcoes.Principal).Com("chave", "principal").Com("nome", "principal")
            .Com("linhas", tri ? "lados" : "grade").Com("cobrimento", V("cobrimento")).Com("recuo_lateral", V("bitola_lateral") / 10)
            .Com("z_face", V("z_inferior"))
            .Com("perna", pernaPorValor ? pf.Padrao("principal.perna", 0) : Math.Floor(V("altura_base") - V("cobrimento") - V("z_inferior")))
            .Com("regra_perna", pernaPorValor ? "valor do perfil (principal.perna)" : "topo da base − cobrimento")
            .Com("limite_perna_z", V("altura_base") - V("cobrimento")).Com("texto_limite_perna", "do topo da base menos o cobrimento")
            .Com("regra_faixas", "uma por linha de estacas")
            .Com("publicar_bitola", "bitola_principal").Com("publicar_topo", "z_topo_inferior"));
        foreach (var (ang, suf) in new[] { (0.0, "x"), (90.0, "y") })
        {
            Passo(ComponentesPadrao.MalhaFace, () => Args().Com("funcao", Funcoes.Secundaria).Com("chave", "secundaria_" + suf).Com("nome", "secundária " + suf)
                .Com("angulo", ang).Com("cobrimento", V("cobrimento")).Com("recuo_lateral", V("bitola_lateral") / 10)
                .Com("z_face", V("z_inferior") + V("bitola_principal") / 10 * 2).Com("camadas", ang > 0 ? 1 : 0)
                .Com("perna", pf.Padrao("secundaria.perna", 20)).Com("regra_perna", "padrão do perfil (secundaria.perna)")
                .Com("publicar_topo", "z_topo_inferior"));
            Passo(ComponentesPadrao.MalhaFace, () => Args().Com("funcao", Funcoes.Superior).Com("chave", "superior_" + suf).Com("nome", "superior " + suf)
                .Com("angulo", ang).Com("cobrimento", V("cobrimento")).Com("recuo_lateral", V("bitola_lateral") / 10)
                .Com("z_face", V("altura_base") - V("cobrimento") - V("bitola_lateral") / 10).Com("superior", 1).Com("camadas", ang > 0 ? 1 : 0)
                .Com("perna", pf.Padrao("superior.perna", 20)).Com("regra_perna", "padrão do perfil (superior.perna)")
                .Com("abertura", calice ? "abertura" : ""));
        }
        Passo(ComponentesPadrao.ContornoLateral, () => Args().Com("funcao", Funcoes.Lateral).Com("chave", "lateral").Com("nome", "lateral horizontal")
            .Com("cobrimento", V("cobrimento")).Com("z0", 0).Com("z1", V("altura_base"))
            .Com("regra_quantidade", "(altura da base − 2·cobrimento) / espaçamento").Com("regra_contorno", "contorno do bloco − cobrimento"));
        Passo(ComponentesPadrao.EstribosVerticais, () =>
        {
            var a = Args().Com("ativo", B(!tri)).Com("funcao", Funcoes.EstriboVertical).Com("chave", "estribo_vertical_x").Com("nome", "estribo vertical")
                .Com("cobrimento", V("cobrimento")).Com("comprimento", V("largura_x")).Com("largura", V("largura_y")).Com("altura", V("altura_base"))
                .Com("regra_quantidade", "(comprimento − 2·cobrimento) / espaçamento, fora do cálice")
                .Com("regra_largura", "largura do bloco − 2·cobrimento").Com("regra_altura", "altura da base − 2·cobrimento");
            var cr = ctx.Dado<Retangulo>("calice_colarinho");
            if (calice && cr != null) a.Com("excluir_x0", cr.X0).Com("excluir_x1", cr.X1);
            return a;
        });

        // ---- ligação com o pilar ----
        Passo(ComponentesPadrao.CaliceArmaduras, () => Args().Com("ativo", V("calice")).Com("colarinho", V("colarinho"))
            .Com("cobrimento", V("cobrimento")).Com("cobrimento_calice", V("cobrimento_calice")).Com("parede", V("parede"))
            .Com("centro_x", Px()).Com("centro_y", Py()).Com("z_topo", V("altura_total")).Com("embutimento", V("embutimento"))
            .Com("regularizacao", V("regularizacao")).Com("z_topo_inferior", ctx.Var("z_topo_inferior", V("cobrimento")))
            .Com("grampos", B(pf.Padrao("calice.grampos", 1) != 0))
            .Com("desconto_perna_externa", pf.Padrao("suspensao.desconto_perna_externa", 6))
            .Com("regra_desconto", "perna − desconto do perfil (suspensao.desconto_perna_externa)")
            .Com("comprimento_fundo", pf.Padrao("fundo_calice.comprimento", 100)).Com("regra_comprimento_fundo", "padrão do perfil (fundo_calice.comprimento)"));
        Passo(ComponentesPadrao.Arranque, () => Args().Com("ativo", B(!calice))
            .Com("centro_x", Px()).Com("centro_y", Py()).Com("pilar_bx", V("pilar_bx")).Com("pilar_by", V("pilar_by"))
            .Com("nx", el.ArranqueNx).Com("ny", el.ArranqueNy).Com("cobrimento_pilar", el.ArranqueCobrimento).Com("cobrimento", V("cobrimento"))
            .Com("z_topo", V("altura_total")).Com("z_topo_inferior", ctx.Var("z_topo_inferior", V("cobrimento"))).Com("fck", el.Fck)
            .Com("regra_comprimento_interno", "topo do bloco − topo da armadura inferior")
            .Com("espera", esperaPerfil).Com("regra_espera", "padrão do perfil (arranque.espera)")
            .Com("pe", pf.Padrao("arranque.pe", 20)).Com("regra_pe", "padrão do perfil (arranque.pe)")
            .Com("pe_dentro", B(peDentro)).Com("regra_pe_sentido", peDentro ? "para dentro (perfil arranque.pe_sentido)" : "para fora (perfil arranque.pe_sentido)"));

        // ---- verificações (iguais às do template) ----
        Passo(ComponentesPadrao.VerificacoesGeometria, () => Args().Com("dist_face", V("dist_face")).Com("raio_apoio", V("diametro_estaca") / 2)
            .Com("centro_x", Px()).Com("centro_y", Py()).Com("pilar_bx", V("pilar_bx")).Com("pilar_by", V("pilar_by"))
            .Com("texto_pilar_fora", "O pilar sai do bloco."));
        Passo(ComponentesPadrao.VerificacoesCalice, () => Args().Com("ativo", V("calice"))
            .Com("pilar_bx", V("pilar_bx")).Com("pilar_by", V("pilar_by")).Com("embutimento", V("embutimento"))
            .Com("interface", el.Interface == InterfaceCalice.Chaves ? "chaves" : el.Interface == InterfaceCalice.Rugosa ? "rugosa" : "lisa")
            .Com("colarinho", V("colarinho")).Com("parede", V("parede")).Com("z_topo", V("altura_total")).Com("regularizacao", V("regularizacao")));
        Passo(ComponentesPadrao.Aviso, () => Args().Com("ativo", B(tri && ctx.Armadura(Funcoes.EstriboVertical) != null))
            .Com("texto", "Bloco triangular: estribos verticais da base não são gerados (só a lateral horizontal em volta)."));
        return r;
    }
}
