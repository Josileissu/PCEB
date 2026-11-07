using System;
using System.Collections.Generic;
using System.Linq;

namespace DEB.Core.Motor.Perfis;

/// <summary>
/// Perfis de fábrica (embutidos no programa). O usuário sobrepõe com %AppData%/PCEB/Perfis/&lt;id&gt;.json
/// (<see cref="RepositorioPerfis"/>). Cada chamada devolve uma cópia nova (pode ser alterada à vontade).
/// </summary>
public static class PerfisFabrica
{
    /// <summary>Bloco sobre estacas no padrão BPM/Marquezin (folhas 104, 115, 128, 131; BLOCOS_REQUISITOS.md).</summary>
    public const string IdBpmBloco = "bpm_bloco";

    private static readonly Dictionary<string, Func<PerfilDetalhamento>> _fabrica =
        new Dictionary<string, Func<PerfilDetalhamento>>(StringComparer.Ordinal) { [IdBpmBloco] = BpmBloco };

    /// <summary>Ids dos perfis de fábrica.</summary>
    public static IReadOnlyList<string> Ids => _fabrica.Keys.ToList();

    /// <summary>Perfil de fábrica pelo Id; null se não houver.</summary>
    public static PerfilDetalhamento Obter(string id) => id != null && _fabrica.TryGetValue(id, out var f) ? f() : null;

    public static bool Existe(string id) => id != null && _fabrica.ContainsKey(id);

    /// <summary>Todos os perfis de fábrica (cópias novas).</summary>
    public static IEnumerable<PerfilDetalhamento> Todos() => _fabrica.Values.Select(f => f());

    /// <summary>
    /// Bloco sobre estacas, padrão BPM. Chaves consumidas pelo motor do bloco (frente B):
    /// Padroes — cobrimento.solo, cobrimento.calice, secundaria.perna, superior.perna, suspensao.desconto_perna_externa,
    /// arranque.pe, arranque.espera (0 = traspasse NBR 6118), calice.grampos (1 = sim), principal.perna (só com a regra "valor");
    /// Regras — principal.perna ("altura_base" | "valor"), arranque.pe_sentido ("fora" | "dentro").
    /// Notas (marcadores trocados pela prancha): "{fck}", "{aco}" (do elemento), "{cobrimento.solo}", "{cobrimento.calice}"
    /// (dos Padroes); "{calice:…}" = trecho que só entra quando a ligação é por cálice (pode conter outros marcadores).
    /// </summary>
    public static PerfilDetalhamento BpmBloco()
    {
        var p = new PerfilDetalhamento
        {
            Id = IdBpmBloco,
            Nome = "Bloco sobre estacas — padrão BPM/Marquezin",
            TipoElemento = ElementoBloco.TipoId,
            Padroes = new Dictionary<string, double>
            {
                ["cobrimento.solo"] = 4,
                ["cobrimento.calice"] = 2,
                ["secundaria.perna"] = 20,
                ["superior.perna"] = 20,
                ["suspensao.desconto_perna_externa"] = 6,
                ["arranque.pe"] = 20,
                ["arranque.espera"] = 0,
                ["calice.grampos"] = 1,
                ["principal.perna"] = 0,
            },
            Regras = new Dictionary<string, string>
            {
                ["principal.perna"] = "altura_base",
                ["arranque.pe_sentido"] = "fora",
            },
        };

        var f = p.Folha;
        f.Escala = 20;
        f.Idioma = "PT";
        f.Titulo = "BLOCO {nome} - {repeticoes}x - {estacas} ø {diametro}";

        // Quadro 0 — forma (à esquerda)
        f.Vistas.Add(new VistaPerfil { Id = "forma_planta", Titulo = "PLANTA|PLAN", Tipo = TipoVista.FormaPlanta, Quadro = 0, Linha = 0, Ordem = 0 });
        f.Vistas.Add(new VistaPerfil { Id = "forma_corte_a", Titulo = "CORTE A-A|SECTION A-A", Tipo = TipoVista.FormaCorte, Plano = "x", Quadro = 0, Linha = 1, Ordem = 0 });

        // Quadro 1 — armadura. Linha 0: vistas superiores, cálice, lateral; linha 1: cortes (abaixo das vistas).
        f.Vistas.Add(new VistaPerfil
        {
            Id = "principal", Titulo = "VISTA SUPERIOR - ARMADURA INFERIOR PRINCIPAL|TOP VIEW - MAIN BOTTOM REINFORCEMENT",
            Tipo = TipoVista.Planta, Posicoes = { Funcoes.Principal }, BarrasAbaixo = true, Quadro = 1, Linha = 0, Ordem = 0,
        });
        f.Vistas.Add(new VistaPerfil
        {
            Id = "secundaria", Titulo = "VISTA SUPERIOR - ARMADURA INFERIOR SECUNDÁRIA|TOP VIEW - SECONDARY BOTTOM REINFORCEMENT",
            Tipo = TipoVista.Planta, Posicoes = { Funcoes.Secundaria }, BarrasAbaixo = true, Quadro = 1, Linha = 0, Ordem = 1,
        });
        f.Vistas.Add(new VistaPerfil
        {
            Id = "superior", Titulo = "VISTA SUPERIOR - ARMADURA SUPERIOR|TOP VIEW - TOP REINFORCEMENT",
            Tipo = TipoVista.Planta, Posicoes = { Funcoes.Superior }, BarrasAbaixo = true, Quadro = 1, Linha = 0, Ordem = 2,
        });
        f.Vistas.Add(new VistaPerfil
        {
            Id = "calice", Titulo = "CÁLICE|SOCKET", Tipo = TipoVista.Planta,
            Posicoes = { Funcoes.CaliceTopo, Funcoes.Calice, Funcoes.CaliceVertical, Funcoes.Suspensao, Funcoes.FundoCalice, Funcoes.Arranque, Funcoes.ArranqueEstribo },
            BarrasAbaixo = true, Quadro = 1, Linha = 0, Ordem = 3,
        });
        f.Vistas.Add(new VistaPerfil { Id = "corte_a", Titulo = "CORTE A-A|SECTION A-A", Tipo = TipoVista.Corte, Plano = "x", Quadro = 1, Linha = 1, Ordem = 0 });
        f.Vistas.Add(new VistaPerfil { Id = "corte_b", Titulo = "CORTE B-B|SECTION B-B", Tipo = TipoVista.Corte, Plano = "y", Quadro = 1, Linha = 1, Ordem = 1 });
        // A folha atual desenha a lateral e o estribo vertical já como barras dobradas (sem planta): tipo Barras.
        f.Vistas.Add(new VistaPerfil
        {
            Id = "lateral", Titulo = "ARM. LATERAL HORIZONTAL|HORIZONTAL SIDE REINFORCEMENT", Tipo = TipoVista.Barras,
            Posicoes = { Funcoes.Lateral, Funcoes.EstriboVertical }, Quadro = 1, Linha = 0, Ordem = 4,
        });

        f.Detalhes.AddRange(new[] { "chaves", "raio_curvatura", "emendas", "comprimento_barras", "ancoragem_estacas", "barra_variavel" });
        f.RelacaoAco = true;
        f.InformacoesProducao = true;
        // Notas do TemplateBlocoEstacas.Detalhes (o "NOTAS:" do cabeçalho fica com a prancha).
        f.Notas.AddRange(new[]
        {
            "1) CONCRETO DO BLOCO: fck = {fck} MPa;",
            "2) COTAS EM CENTÍMETROS E NÍVEIS EM METROS;",
            "3) AÇO {aco};",
            "4) COBRIMENTOS: ELEMENTOS EM CONTATO COM O SOLO {cobrimento.solo} cm{calice:; INTERNO DO CÁLICE {cobrimento.calice} cm};",
            "5) CONFERIR TODAS AS MEDIDAS NO LOCAL ANTES DA EXECUÇÃO.",
        });
        return p;
    }
}
