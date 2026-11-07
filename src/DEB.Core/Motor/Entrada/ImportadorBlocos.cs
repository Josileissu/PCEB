using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using DEB.Core.Persistencia;
using DEB.Core.Templates;
using DEB.Core.Templates.Blocos;

namespace DEB.Core.Motor.Entrada;

/// <summary>Erro ou aviso da importação, com a linha do arquivo (1 = primeira linha) e a coluna (nome do cabeçalho).</summary>
public sealed class MensagemImportacao
{
    public int Linha { get; set; }
    /// <summary>Nome da coluna como está no cabeçalho; vazio = a linha toda.</summary>
    public string Coluna { get; set; } = "";
    public string Texto { get; set; } = "";
    /// <summary>Erro = a linha não entrou; aviso = entrou (ou o problema é do cabeçalho).</summary>
    public bool Erro { get; set; }

    public override string ToString() =>
        (Erro ? "Erro" : "Aviso") + $" na linha {Linha}" + (Coluna.Length > 0 ? $", coluna \"{Coluna}\"" : "") + ": " + Texto;
}

public sealed class ResultadoImportacao
{
    public List<ElementoBloco> Blocos { get; } = new List<ElementoBloco>();
    /// <summary>Linha do arquivo de cada bloco (mesma ordem de <see cref="Blocos"/>).</summary>
    public List<int> Linhas { get; } = new List<int>();
    public List<MensagemImportacao> Mensagens { get; } = new List<MensagemImportacao>();
    public IEnumerable<MensagemImportacao> Erros => Mensagens.Where(m => m.Erro);
    public IEnumerable<MensagemImportacao> Avisos => Mensagens.Where(m => !m.Erro);
}

/// <summary>
/// Planilha de blocos (pedido de 2026-11-07: a obra tem dezenas de blocos e o engenheiro gera todos de uma vez): CSV ";" do
/// Excel pt-BR, UM BLOCO POR LINHA, vírgula decimal, listas entre aspas. Cabeçalho por nome (ordem livre; maiúsculas, acentos,
/// espaços, "_" e o que estiver entre parênteses não contam); coluna desconhecida = aviso; célula vazia = padrão (o do template
/// de bloco ou o dado); linha com erro não entra e não derruba as outras. Linhas começando com "#" são comentários.
/// </summary>
public static class ImportadorBlocos
{
    private sealed class Coluna
    {
        public string Nome = "";
        /// <summary>Parâmetro do template que dá mínimo/máximo (aviso se sair); null = sem.</summary>
        public string Parametro;
        /// <summary>O template não tem este campo (padrão 0): exporta vazio quando 0.</summary>
        public bool SemPadrao;
        /// <summary>Aplica a célula (não vazia) no elemento; devolve o erro ou null.</summary>
        public Func<ElementoBloco, string, string> Aplicar;
        public Func<ElementoBloco, string> Ler;
        /// <summary>Valor numérico aplicado (para conferir a faixa); null = não numérico.</summary>
        public Func<ElementoBloco, double?> Valor;
    }

    private static readonly List<Coluna> _colunas = CriarColunas();

    /// <summary>Nomes das colunas, na ordem do modelo.</summary>
    public static IReadOnlyList<string> Colunas => _colunas.Select(c => c.Nome).ToList();

    // ======================== colunas ========================

    private static List<Coluna> CriarColunas()
    {
        var l = new List<Coluna>();
        void Texto(string nome, Action<ElementoBloco, string> set, Func<ElementoBloco, string> get) =>
            l.Add(new Coluna { Nome = nome, Aplicar = (e, s) => { set(e, s.Trim()); return null; }, Ler = get });
        void Num(string nome, string param, Action<ElementoBloco, double> set, Func<ElementoBloco, double> get, double min = double.NegativeInfinity) =>
            l.Add(new Coluna
            {
                Nome = nome, Parametro = param, Ler = e => Fmt(get(e)), Valor = e => get(e),
                Aplicar = (e, s) =>
                {
                    if (!Numero(s, out double x)) return $"\"{s}\" não é número.";
                    if (x < min) return $"{Fmt(x)} menor que {Fmt(min)}.";
                    set(e, x); return null;
                },
            });
        void Int(string nome, string param, Action<ElementoBloco, int> set, Func<ElementoBloco, int> get, int min = 0) =>
            l.Add(new Coluna
            {
                Nome = nome, Parametro = param, Ler = e => get(e).ToString(CultureInfo.InvariantCulture), Valor = e => get(e),
                Aplicar = (e, s) =>
                {
                    if (!Inteiro(s, out int x)) return $"\"{s}\" não é número inteiro.";
                    if (x < min) return $"{x} menor que {min}.";
                    set(e, x); return null;
                },
            });
        void Opcao<T>(string nome, Func<string, T?> ler, string opcoes, Action<ElementoBloco, T> set, Func<ElementoBloco, string> get) where T : struct =>
            l.Add(new Coluna
            {
                Nome = nome, Ler = get,
                Aplicar = (e, s) => { var x = ler(s); if (x == null) return $"\"{s}\" inválido (use {opcoes})."; set(e, x.Value); return null; },
            });

        Texto("nome", (e, s) => e.Nome = s, e => e.Nome);
        Texto("pilares", (e, s) => e.Pilares = TemplateBlocoEstacas.LerPilares(s), e => TemplateBlocoEstacas.TextoPilares(e.Pilares));
        Int("quantidade", "Quantidade", (e, x) => e.Repeticoes = x, e => e.Repeticoes);
        l.Add(new Coluna { Nome = "niveis", Aplicar = AplicarNiveis, Ler = e => TextoNiveis(e.Niveis) });
        Opcao("arranjo", ConversorBloco.LerArranjo, "grade ou triangulo", (e, x) => e.Arranjo = x,
            e => e.Arranjo == ArranjoEstacas.Triangulo ? "triangulo" : "grade");
        Int("nestx", "NEstX", (e, x) => e.EstacasX = x, e => e.EstacasX, 1);
        Int("nesty", "NEstY", (e, x) => e.EstacasY = x, e => e.EstacasY, 1);
        Num("espx", "EspX", (e, x) => e.EspX = x, e => e.EspX, 0);
        Num("espy", "EspY", (e, x) => e.EspY = x, e => e.EspY, 0);
        Num("diam_estaca", "DiamEstaca", (e, x) => e.DiamEstaca = x, e => e.DiamEstaca, 0);
        Num("dist_face", "DistFace", (e, x) => e.DistFace = x, e => e.DistFace, 0);
        Num("emb_estaca", "EmbEstaca", (e, x) => e.EmbutimentoEstaca = x, e => e.EmbutimentoEstaca, 0);
        Num("h_base", "hBase", (e, x) => e.AlturaBase = x, e => e.AlturaBase, 0);
        Num("lastro", "Lastro", (e, x) => e.Lastro = x, e => e.Lastro, 0);
        Num("pilar_bx", "PilarBx", (e, x) => e.PilarBx = x, e => e.PilarBx, 0);
        Num("pilar_by", "PilarBy", (e, x) => e.PilarBy = x, e => e.PilarBy, 0);
        Num("pilar_ex", "PilarEx", (e, x) => e.PilarEx = x, e => e.PilarEx);
        Num("pilar_ey", "PilarEy", (e, x) => e.PilarEy = x, e => e.PilarEy);
        Opcao("ligacao", ConversorBloco.LerLigacao, "colarinho, embutido ou arranque", (e, x) => e.Ligacao = x,
            e => e.Ligacao == LigacaoPilar.Arranque ? "arranque" : e.Ligacao == LigacaoPilar.CaliceEmbutido ? "embutido" : "colarinho");
        Num("h_colar", "hColar", (e, x) => e.AlturaColarinho = x, e => e.AlturaColarinho, 0);
        Num("t_parede", "tParede", (e, x) => e.ParedeCalice = x, e => e.ParedeCalice, 0);
        Num("folga", "Folga", (e, x) => e.FolgaCalice = x, e => e.FolgaCalice, 0);
        Num("lemb", "Lemb", (e, x) => e.Lemb = x, e => e.Lemb, 0);
        Num("regularizacao", "Regularizacao", (e, x) => e.Regularizacao = x, e => e.Regularizacao, 0);
        Opcao("interface", ConversorBloco.LerInterface, "lisa, rugosa ou chaves", (e, x) => e.Interface = x,
            e => e.Interface == InterfaceCalice.Lisa ? "lisa" : e.Interface == InterfaceCalice.Rugosa ? "rugosa" : "chaves");
        Num("fck", "Fck", (e, x) => e.Fck = x, e => e.Fck, 0);
        l.Add(new Coluna
        {
            Nome = "aco", Ler = e => e.Aco,
            Aplicar = (e, s) => { var a = LerAco(s); if (a == null) return $"\"{s}\" inválido (use CA-50 ou CA-60)."; e.Aco = a; return null; },
        });
        Int("arranque_nx", "ArrNx", (e, x) => e.ArranqueNx = x, e => e.ArranqueNx, 1);
        Int("arranque_ny", "ArrNy", (e, x) => e.ArranqueNy = x, e => e.ArranqueNy, 1);
        Num("arranque_cob", "ArrCob", (e, x) => e.ArranqueCobrimento = x, e => e.ArranqueCobrimento, 0);

        foreach (var m in ConversorBloco.Mapa)
        {
            string f = m.Funcao;
            ArmaduraCalculada A(ElementoBloco e)
            {
                var a = e.Armadura(f);
                if (a == null) { a = new ArmaduraCalculada { Funcao = f }; e.Armaduras.Add(a); }
                return a;
            }
            Num(f + "_bitola", m.Bitola, (e, x) => A(e).Bitola = x, e => e.Armadura(f)?.Bitola ?? 0, 0);
            Int(f + "_qtd", m.Quantidade, (e, x) => A(e).Quantidade = x, e => e.Armadura(f)?.Quantidade ?? 0);
            l[l.Count - 1].SemPadrao = m.Quantidade == null;
            Num(f + "_esp", m.Espacamento, (e, x) => A(e).Espacamento = x, e => e.Armadura(f)?.Espacamento ?? 0, 0);
            l[l.Count - 1].SemPadrao = m.Espacamento == null;
        }
        return l;
    }

    // ======================== importar ========================

    /// <summary>Lê a planilha. <paramref name="padrao"/> = valores do template para as células vazias (null = os de fábrica
    /// do <see cref="TemplateBlocoEstacas"/>; o CAD pode passar a linha PADRÃO do arquivo de modelos).</summary>
    public static ResultadoImportacao Importar(string texto, ValoresTemplate padrao = null)
    {
        var r = new ResultadoImportacao();
        var t = new TemplateBlocoEstacas();
        padrao ??= new ValoresTemplate(t.Parametros);
        var parametros = t.Parametros.ToDictionary(p => p.Nome, StringComparer.OrdinalIgnoreCase);
        var porChave = new Dictionary<string, Coluna>();
        foreach (var c in _colunas) porChave[ChaveColuna(c.Nome)] = c;

        List<(int Indice, string Cabecalho, Coluna Col)> mapa = null;
        var nomes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int n = 0;
        foreach (var linha in Csv.Registros(texto ?? ""))
        {
            n++;
            if (linha.Count == 0 || linha.All(c => c.Trim().Length == 0) || linha[0].TrimStart().StartsWith("#")) continue;
            if (mapa == null)
            {
                mapa = LerCabecalho(linha, n, porChave, r);
                if (!mapa.Any(m => m.Col.Nome == "nome"))
                {
                    r.Mensagens.Add(new MensagemImportacao { Linha = n, Erro = true, Texto = "o cabeçalho não tem a coluna \"nome\" (1ª linha que não é comentário)." });
                    return r;
                }
                continue;
            }

            var e = ConversorBloco.DeValores(padrao);
            // identificação nunca vem do padrão (a linha PADRÃO tem o nome e os pilares de um bloco de exemplo)
            e.Nome = ""; e.Pilares = new List<string>(); e.Niveis = new List<NivelPilar>(); e.Repeticoes = 0;
            var msgs = new List<MensagemImportacao>();
            foreach (var (i, cab, col) in mapa)
            {
                string s = i < linha.Count ? linha[i].Trim() : "";
                if (s.Length == 0) continue;
                string erro = col.Aplicar(e, s);
                if (erro != null) { msgs.Add(new MensagemImportacao { Linha = n, Coluna = cab, Erro = true, Texto = erro }); continue; }
                if (col.Valor != null && col.Parametro != null && parametros.TryGetValue(col.Parametro, out var p))
                {
                    double x = col.Valor(e) ?? 0;
                    if (x < p.Minimo || x > p.Maximo)
                        msgs.Add(new MensagemImportacao
                        {
                            Linha = n, Coluna = cab,
                            Texto = $"{Fmt(x)} fora da faixa do template ({Fmt(p.Minimo)} a {Fmt(p.Maximo)}) — {p.Rotulo}.",
                        });
                }
            }
            if (string.IsNullOrWhiteSpace(e.Nome))
                msgs.Add(new MensagemImportacao { Linha = n, Coluna = mapa.First(m => m.Col.Nome == "nome").Cabecalho, Erro = true, Texto = "bloco sem nome." });
            r.Mensagens.AddRange(msgs);
            if (msgs.Any(m => m.Erro)) continue;
            if (nomes.TryGetValue(e.Nome, out int anterior))
                r.Mensagens.Add(new MensagemImportacao { Linha = n, Texto = $"bloco \"{e.Nome}\" repetido (também na linha {anterior})." });
            else nomes[e.Nome] = n;
            r.Blocos.Add(e);
            r.Linhas.Add(n);
        }
        if (mapa == null) r.Mensagens.Add(new MensagemImportacao { Linha = 0, Erro = true, Texto = "planilha vazia (sem cabeçalho)." });
        return r;
    }

    private static List<(int, string, Coluna)> LerCabecalho(List<string> linha, int n, Dictionary<string, Coluna> porChave, ResultadoImportacao r)
    {
        var mapa = new List<(int, string, Coluna)>();
        var usadas = new HashSet<Coluna>();
        for (int i = 0; i < linha.Count; i++)
        {
            string cab = linha[i].Trim();
            if (cab.Length == 0) continue;
            string k = ChaveColuna(cab);
            if (_apelidos.TryGetValue(k, out var a)) k = a;
            if (!porChave.TryGetValue(k, out var col))
            {
                r.Mensagens.Add(new MensagemImportacao { Linha = n, Coluna = cab, Texto = "coluna desconhecida: ignorada." });
                continue;
            }
            if (!usadas.Add(col))
            {
                r.Mensagens.Add(new MensagemImportacao { Linha = n, Coluna = cab, Texto = $"coluna \"{col.Nome}\" repetida: vale a primeira." });
                continue;
            }
            mapa.Add((i, cab, col));
        }
        return mapa;
    }

    /// <summary>Outros nomes aceitos no cabeçalho (já na forma de <see cref="ChaveColuna"/>).</summary>
    private static readonly Dictionary<string, string> _apelidos = new Dictionary<string, string>
    {
        ["bloco"] = "nome", ["pilar"] = "pilares", ["nivel"] = "niveis", ["qtd"] = "quantidade", ["repeticoes"] = "quantidade",
        ["diametroestaca"] = "diamestaca", ["hcolarinho"] = "hcolar", ["tparedes"] = "tparede",
    };

    /// <summary>Chave de comparação do cabeçalho: sem acentos, minúsculas, sem o que está entre parênteses, só letras e dígitos.</summary>
    internal static string ChaveColuna(string s)
    {
        s = ConversorBloco.Chave(s);
        int k = s.IndexOf('(');
        if (k >= 0) s = s.Substring(0, k);
        return new string(s.Where(char.IsLetterOrDigit).ToArray());
    }

    private static string AplicarNiveis(ElementoBloco e, string s)
    {
        var lista = new List<NivelPilar>();
        foreach (var item in s.Split(new[] { ';', '/', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string it = item.Trim();
            if (it.Length == 0) continue;
            int k = it.IndexOfAny(new[] { '=', '|' });
            if (k <= 0) return $"nível \"{it}\" inválido (use \"P1=-3,43; P67=-11,63\").";
            string pilar = it.Substring(0, k).Trim();
            string cota = it.Substring(k + 1).Trim();
            if (cota.EndsWith("m", StringComparison.OrdinalIgnoreCase)) cota = cota.Substring(0, cota.Length - 1);
            if (!Numero(cota, out double z)) return $"cota \"{cota}\" do pilar {pilar} não é número.";
            lista.Add(new NivelPilar { Pilar = pilar, CotaM = z });
        }
        e.Niveis = lista;
        return null;
    }

    private static string TextoNiveis(List<NivelPilar> ns) =>
        string.Join("; ", (ns ?? new List<NivelPilar>()).Select(x => x.Pilar + "=" + Fmt(x.CotaM)));

    private static string LerAco(string s)
    {
        string k = new string(ConversorBloco.Chave(s).Where(char.IsLetterOrDigit).ToArray());
        return k == "ca50" || k == "50" ? "CA-50" : k == "ca60" || k == "60" ? "CA-60" : null;
    }

    // ======================== números (vírgula do Excel pt-BR; aceita "ø16", "c/15", "cm", "mm") ========================

    internal static bool Numero(string s, out double x)
    {
        s = (s ?? "").Trim().Replace(" ", "").Replace(" ", "");
        foreach (var p in new[] { "ø", "Ø", "φ", "c/", "C/" }) if (s.StartsWith(p)) s = s.Substring(p.Length);
        foreach (var u in new[] { "mm", "cm" }) if (s.EndsWith(u, StringComparison.OrdinalIgnoreCase)) s = s.Substring(0, s.Length - u.Length);
        return double.TryParse(s.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out x) && !double.IsNaN(x) && !double.IsInfinity(x);
    }

    private static bool Inteiro(string s, out int i)
    {
        i = 0;
        if (!Numero(s, out double x) || Math.Abs(x - Math.Round(x)) > 1e-9 || Math.Abs(x) > int.MaxValue) return false;
        i = (int)Math.Round(x);
        return true;
    }

    private static string Fmt(double x) => double.IsInfinity(x) ? (x > 0 ? "∞" : "−∞") : x.ToString("0.######", CultureInfo.InvariantCulture).Replace('.', ',');

    // ======================== exportar / modelo ========================

    /// <summary>Planilha com os blocos dados (cabeçalho completo; números com vírgula).</summary>
    public static string Exportar(IEnumerable<ElementoBloco> blocos, IEnumerable<string> comentarios = null)
    {
        var sb = new StringBuilder();
        foreach (var c in comentarios ?? Enumerable.Empty<string>()) sb.AppendLine("# " + c.Replace(";", ","));
        sb.AppendLine(Csv.Linha(_colunas.Select(c => c.Nome)));
        foreach (var e in blocos ?? Enumerable.Empty<ElementoBloco>())
            sb.AppendLine(Csv.Linha(_colunas.Select(c => c.SemPadrao && (c.Valor?.Invoke(e) ?? 0) == 0 ? "" : c.Ler(e) ?? "")));
        return sb.ToString();
    }

    /// <summary>Modelo para o usuário preencher no Excel: cabeçalho, o B28 (grade de 2 estacas, colarinho) e o B15 (triangular).</summary>
    public static string GerarModeloPlanilha() => Exportar(new[] { ExemploB28(), ExemploB15() }, new[]
    {
        "Planilha de blocos sobre estacas do Detalhador Estrutural Brasileiro: um bloco por linha, separador ponto e vírgula.",
        "Célula vazia = padrão do template. Números com vírgula. Medidas em cm, bitolas em mm, níveis (topo do bloco) em m.",
        "pilares: P1, P2 e P3. niveis: pilar=cota (m) separados por ponto e vírgula ou barra (ex.: P1=-3,43 / P67=-11,63). arranjo: grade ou triangulo. ligacao: colarinho, embutido ou arranque. interface: lisa, rugosa ou chaves.",
        "Armaduras: <funcao>_bitola, <funcao>_qtd (0 = pelo espaçamento) e <funcao>_esp (0 = pela quantidade). Pernas, cobrimentos, escala e idioma vêm do perfil do escritório.",
        "Linhas começando com # são comentários.",
    });

    /// <summary>B28 (BPM, folha 128): 2 estacas ø40 a 120, e = 35, base 55, colarinho 40, parede 21, pilar 30 × 20, Lemb 50.</summary>
    public static ElementoBloco ExemploB28()
    {
        var e = ConversorBloco.DeValores(new ValoresTemplate(new TemplateBlocoEstacas().Parametros));
        e.Nome = "B28";
        e.Pilares = new List<string> { "P106", "P118", "P119", "P120", "P121", "P122" };
        e.Repeticoes = 0;
        e.Niveis = new List<NivelPilar> { new NivelPilar { Pilar = "P106", CotaM = -11.43 } };
        e.Arranjo = ArranjoEstacas.Grade; e.EstacasX = 2; e.EstacasY = 1; e.EspX = 120; e.EspY = 120;
        e.DiamEstaca = 40; e.DistFace = 35; e.AlturaBase = 55;
        e.Ligacao = LigacaoPilar.CaliceColarinho; e.AlturaColarinho = 40; e.ParedeCalice = 21; e.PilarBx = 30; e.PilarBy = 20; e.Lemb = 50;
        Arm(e, Funcoes.Principal, 16, 5, 5);
        Arm(e, Funcoes.Secundaria, 10, 0, 15);
        Arm(e, Funcoes.Superior, 8, 0, 12);
        Arm(e, Funcoes.Lateral, 8, 0, 15);
        Arm(e, Funcoes.EstriboVertical, 8, 0, 12);
        Arm(e, Funcoes.CaliceTopo, 12.5, 3, 5.5);
        Arm(e, Funcoes.Calice, 10, 0, 12);
        Arm(e, Funcoes.CaliceVertical, 10, 2, 0);
        Arm(e, Funcoes.Suspensao, 10, 4, 12);
        Arm(e, Funcoes.FundoCalice, 10, 7, 0);
        return e;
    }

    /// <summary>B15 (BPM, folha 115): 3 estacas ø50 em triângulo de lado 150, e = 40, base 105, colarinho 30, parede 25,
    /// pilar 40 × 40, Lemb 65, 24 peças; cálice com 5 ø16 c/4 no topo.</summary>
    public static ElementoBloco ExemploB15()
    {
        var e = ExemploB28();
        e.Nome = "B15";
        e.Pilares = new List<string>();
        e.Repeticoes = 24;
        e.Niveis = new List<NivelPilar>();
        e.Arranjo = ArranjoEstacas.Triangulo; e.EspX = 150; e.EspY = 150;
        e.DiamEstaca = 50; e.DistFace = 40; e.AlturaBase = 105;
        e.AlturaColarinho = 30; e.ParedeCalice = 25; e.PilarBx = 40; e.PilarBy = 40; e.Lemb = 65;
        Arm(e, Funcoes.CaliceTopo, 16, 5, 4);
        Arm(e, Funcoes.EstriboVertical, 0, 0, 12);   // triangular: sem estribos verticais
        return e;
    }

    private static void Arm(ElementoBloco e, string funcao, double bitola, int qtd, double esp)
    {
        var a = e.Armadura(funcao);
        if (a == null) { a = new ArmaduraCalculada { Funcao = funcao }; e.Armaduras.Add(a); }
        a.Bitola = bitola; a.Quantidade = qtd; a.Espacamento = esp;
    }
}
