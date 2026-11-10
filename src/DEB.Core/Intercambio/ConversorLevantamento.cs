using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DEB.Core.Pecas;

namespace DEB.Core.Intercambio;

/// <summary>
/// Nome de cada grupo de peças iguais (2026-11-09: "a marca é o nome da peça"). Vale a marca que as peças têm no modelo;
/// só o grupo sem marca recebe uma gerada (prefixo + número livre). Assim, acrescentar uma peça ao modelo não renumera as
/// outras — antes a exportação numerava P1, P2… pela ordem de tamanho e ignorava a marca do modelo.
/// </summary>
public static class MarcasPecas
{
    /// <param name="marcasPorGrupo">As marcas das peças de cada grupo (vazias = sem marca).</param>
    /// <param name="prefixo">Prefixo das marcas geradas ("P", "V").</param>
    /// <param name="notas">Recebe o que o usuário precisa saber: grupo com marcas diferentes, marca repetida em peças diferentes.</param>
    public static List<string> Definir(IReadOnlyList<IEnumerable<string>> marcasPorGrupo, string prefixo, List<string> notas)
    {
        var doModelo = marcasPorGrupo.Select(g => g.Select(m => (m ?? "").Trim()).Where(m => m.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(m => m, StringComparer.OrdinalIgnoreCase).ToList()).ToList();
        var usadas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var r = new string[doModelo.Count];

        // 1º as marcas do modelo (na ordem dos grupos); a repetida noutro grupo ganha um sufixo
        for (int i = 0; i < doModelo.Count; i++)
        {
            if (doModelo[i].Count == 0) continue;
            string m = doModelo[i][0];
            if (doModelo[i].Count > 1)
                notas?.Add($"Peças iguais com marcas diferentes no modelo ({string.Join(", ", doModelo[i])}): o detalhamento sai como {m}.");
            if (!usadas.Add(m))
            {
                string livre = Enumerable.Range(2, int.MaxValue - 2).Select(k => $"{m} ({k})").First(x => !usadas.Contains(x));
                notas?.Add($"A marca {m} está em peças DIFERENTES no modelo: um dos grupos sai como {livre} — corrija a marca no modelo.");
                usadas.Add(m = livre);
            }
            r[i] = m;
        }
        // depois os grupos sem marca: prefixo + o próximo número que ninguém usa (nem como marca do modelo)
        foreach (var m in doModelo.SelectMany(x => x)) usadas.Add(m);
        int n = 1;
        for (int i = 0; i < r.Length; i++)
        {
            if (r[i] != null) continue;
            while (usadas.Contains(prefixo + n.ToString(CultureInfo.InvariantCulture))) n++;
            usadas.Add(r[i] = prefixo + n.ToString(CultureInfo.InvariantCulture));
        }
        return r.ToList();
    }
}

/// <summary>
/// Peças levantadas na conferência (<see cref="PecaLevantada"/>: trechos de seção exata, consolos pelo perfil) → arquivo de
/// troca para o CAD (<see cref="ArquivoPecas"/>). É o ÚNICO caminho da exportação (2026-11-09): o que o usuário confere na
/// janela "Conferir peças" é o que vai para o JSON — antes a exportação media de novo, pela caixa envolvente e pelo volume.
/// Só sai o que o template do CAD desenha certo; o resto fica de fora com uma nota dizendo por quê.
/// </summary>
public static class ConversorLevantamento
{
    /// <summary>Área (ou trecho) abaixo desta fração da seção b × h = a peça não é a retangular que o CAD desenha.</summary>
    public const double FracaoMinima = 0.95;
    /// <summary>Trecho curto (cm) com a seção reduzida dentro da caixa da peça = furo de pino ou recorte local, ainda não lido.</summary>
    public const double TrechoLocal = 20;

    public static ArquivoPecas Converter(IEnumerable<PecaLevantada> pecas, string origem, string data, IEnumerable<string> notasDoLevantamento = null)
    {
        var arq = new ArquivoPecas { Origem = origem ?? "", Data = data ?? "" };
        if (notasDoLevantamento != null) arq.Notas.AddRange(notasDoLevantamento);
        var todas = pecas.ToList();
        int fora = todas.RemoveAll(p => p.NaoExportar);
        if (fora > 0) arq.Notas.Add($"{fora} peça(s) marcada(s) como \"não exportar\" na conferência ficaram de fora.");

        var pilares = PreMoldadas(todas, TipoPeca.Pilar, "pilar", "pilares", arq.Notas);
        var vigas = PreMoldadas(todas, TipoPeca.Viga, "viga", "vigas", arq.Notas);

        // ---- pilares ----
        var gp = ConferenciaPecas.Agrupar(pilares);
        var okP = new List<(GrupoPecas G, PilarPeca P)>();
        foreach (var g in gp)
        {
            var p = Pilar(g, out string motivo, arq.Notas);
            if (p == null) arq.Notas.Add($"Pilar {g.Marcas} ({g.Quantidade} peça(s)) não exportado: {motivo}");
            else okP.Add((g, p));
        }
        var marcasP = MarcasPecas.Definir(okP.Select(x => x.G.Pecas.Select(q => q.Marca)).ToList(), "P", arq.Notas);
        for (int i = 0; i < okP.Count; i++) { okP[i].P.Marca = marcasP[i]; arq.Pilares.Add(okP[i].P); }

        // ---- vigas ----
        var gv = ConferenciaPecas.Agrupar(vigas);
        var okV = new List<(GrupoPecas G, VigaPeca V)>();
        foreach (var g in gv)
        {
            var v = Viga(g, out string motivo, arq.Notas);
            if (v == null) arq.Notas.Add($"Viga {g.Marcas} ({g.Quantidade} peça(s)) não exportada: {motivo}");
            else okV.Add((g, v));
        }
        var marcasV = MarcasPecas.Definir(okV.Select(x => x.G.Pecas.Select(q => q.Marca)).ToList(), "V", arq.Notas);
        for (int i = 0; i < okV.Count; i++) { okV[i].V.Marca = marcasV[i]; arq.Vigas.Add(okV[i].V); }
        return arq;
    }

    /// <summary>
    /// As peças de material "Concreto pré-moldado"; se o modelo não tem nenhuma desse tipo (material não preenchido), todas,
    /// com nota — o mesmo critério para pilares e vigas (antes: todos os pilares, nenhuma viga).
    /// </summary>
    private static List<PecaLevantada> PreMoldadas(List<PecaLevantada> todas, TipoPeca tipo, string um, string varios, List<string> notas)
    {
        var doTipo = todas.Where(p => p.Tipo == tipo).ToList();
        var pm = doTipo.Where(p => p.PreMoldada).ToList();
        if (pm.Count > 0 || doTipo.Count == 0)
        {
            if (pm.Count < doTipo.Count) notas.Add($"{doTipo.Count - pm.Count} {(doTipo.Count - pm.Count == 1 ? um : varios)} sem o material \"Concreto pré-moldado\" ficaram de fora.");
            return pm;
        }
        notas.Add($"Nenhum(a) {um} com material \"Concreto pré-moldado\": todos(as) os(as) {varios} estruturais foram considerados(as).");
        return doTipo;
    }

    private static List<ElementoPeca> Elementos(GrupoPecas g) => g.Pecas.Select(p => new ElementoPeca
    {
        Id = p.Id, Uid = p.Uid, Marca = p.Marca ?? "", Giro = ConferenciaPecas.GiroRelativo(p, g.Representante),
    }).ToList();

    // ------------------------------------------------------------------ pilar

    private static PilarPeca Pilar(GrupoPecas g, out string motivo, List<string> notas)
    {
        motivo = null;
        var r = g.Representante;
        if (r.Trechos.Count == 0) { motivo = "a geometria não pôde ser cortada."; return null; }
        if (r.Bx <= 0 || r.By <= 0) { motivo = "seção não medida."; return null; }
        double cheia = r.Bx * r.By;
        var corrente = r.Principal.Secao;
        if (corrente.Area < FracaoMinima * cheia)
        {
            motivo = $"seção não retangular ({corrente.Classificar().Descricao}; área = {Pct(corrente.Area / cheia)} de {N(r.Bx)} × {N(r.By)}) — " +
                     "o template do CAD só desenha o pilar retangular.";
            return null;
        }
        // trechos mais finos que o corrente (cabeça reduzida, base com chaves…): o CAD desenha b × h constante — sai com nota
        foreach (var t in r.Trechos.Where(t => t.Secao.Area < FracaoMinima * cheia))
            notas.Add($"Pilar {g.Marcas}: de {N(t.X0)} a {N(t.X1)} cm a seção é menor ({t.Secao.Classificar().Descricao}); o CAD desenha " +
                      $"{N(r.Bx)} × {N(r.By)} em toda a altura — conferir.");

        var p = new PilarPeca
        {
            B = R(r.Bx), H = R(r.By), L = R(r.Comprimento), CotaBase = Math.Round(r.CotaBase, 4),
            Embutimento = r.Embutimento.HasValue ? R(r.Embutimento.Value) : (double?)null,
            Niveis = r.Niveis?.Select(n => new NivelPeca { Nome = n.Nome, Cota = Math.Round(n.Cota, 4) }).ToList(),
            Elementos = Elementos(g),
        };
        foreach (var c in r.Consolos)
        {
            var m = c.Medidas;
            p.Consolos.Add(new ConsoloPeca
            {
                Face = c.Face, Inicio = R(c.S0), Fim = R(c.S1),
                Base = R(m.BaseNaFace), Topo = R(c.Perfil.ZMax), Balanco = R(m.Balanco), AlturaPonta = R(m.AlturaPonta),
            });
        }
        return p;
    }

    // ------------------------------------------------------------------ viga

    private enum Parte { Corrente, Dente, Local }

    private sealed class Pedaco
    {
        public double X0, X1, H;
        public Parte P;
        /// <summary>Chave da aba do trecho corrente ("" = sem aba).</summary>
        public string Aba = "";
        public SecaoComAba Secao;
        public bool Igual(Pedaco o) => P == o.P && Aba == o.Aba && Math.Abs(H - o.H) <= SecaoPoligonal.Tolerancia;
    }

    private static VigaPeca Viga(GrupoPecas g, out string motivo, List<string> notas)
    {
        motivo = null;
        var r = g.Representante;
        if (r.Trechos.Count == 0) { motivo = "a geometria não pôde ser cortada."; return null; }
        var s0 = r.Principal.Secao;
        var i0 = SecaoComAba.Ler(s0);
        if (i0 == null || i0.AlturaToda)
        {
            motivo = $"seção {s0.Classificar().Descricao} — o CAD desenha a retangular e a com aba lateral (L, T invertido, aba elevada ou no topo); I, duas abas no mesmo lado e mísula ainda não.";
            return null;
        }
        const double tol = SecaoPoligonal.Tolerancia;
        // caixa da peça (para reconhecer furo/recorte local): a maior entre os trechos
        double yMin = r.Trechos.Min(t => t.Secao.YMin), yMax = r.Trechos.Max(t => t.Secao.YMax),
               zMin = r.Trechos.Min(t => t.Secao.ZMin), zMax = r.Trechos.Max(t => t.Secao.ZMax);

        // cada trecho: corrente (a alma da peça, com ou sem aba), dente (só a alma, mais baixo, topo no topo = recorte embaixo) ou local
        var partes = new List<Pedaco>();
        foreach (var t in r.Trechos)
        {
            var s = t.Secao;
            var i = SecaoComAba.Ler(s);
            bool mesmaAlma = i != null && !i.AlturaToda && Math.Abs(i.B - i0.B) <= tol && Math.Abs(i.Y0 - i0.Y0) <= tol;
            if (mesmaAlma && Math.Abs(i.H - i0.H) <= tol && Math.Abs(i.Z0 - i0.Z0) <= tol)
                partes.Add(new Pedaco { X0 = t.X0, X1 = t.X1, P = Parte.Corrente, H = i0.H, Aba = i.Chave, Secao = i });
            else if (mesmaAlma && i.H < i0.H - tol && Math.Abs(s.ZMax - s0.ZMax) <= tol)   // a aba acima do recorte passa pelo dente
                partes.Add(new Pedaco { X0 = t.X0, X1 = t.X1, P = Parte.Dente, H = R(i.H), Aba = i.Chave, Secao = i });
            else if (t.Comprimento <= TrechoLocal && s.YMin >= yMin - tol && s.YMax <= yMax + tol && s.ZMin >= zMin - tol && s.ZMax <= zMax + tol)
                partes.Add(new Pedaco { X0 = t.X0, X1 = t.X1, P = Parte.Local });
            else
            {
                motivo = $"de {N(t.X0)} a {N(t.X1)} cm a seção é {s.Classificar().Descricao} (a corrente é {s0.Classificar().Descricao}) — " +
                         "mísula, recorte em cima, I ou seção variável, que o CAD ainda não desenha.";
                return null;
            }
        }
        // furo de pino / recorte local: some dentro do trecho vizinho (o anterior; no início, o seguinte), com nota
        if (partes.Any(x => x.P == Parte.Local))
        {
            notas.Add($"Viga {g.Marcas}: furo ou recorte local em " + string.Join(", ", partes.Where(x => x.P == Parte.Local).Select(x => $"{N(x.X0)}–{N(x.X1)}")) +
                      " cm não é lido (ficam os furos do PADRÃO do CAD) — conferir.");
            for (int k = 0; k < partes.Count; k++)
            {
                if (partes[k].P != Parte.Local) continue;
                int viz = k > 0 ? k - 1 : partes.FindIndex(x => x.P != Parte.Local);
                if (viz < 0) { motivo = "seção não reconhecida."; return null; }
                var o = partes[viz];
                partes[k] = new Pedaco { X0 = partes[k].X0, X1 = partes[k].X1, P = o.P, H = o.H, Aba = o.Aba, Secao = o.Secao };
            }
        }
        // junta os vizinhos iguais
        var j = new List<Pedaco>();
        foreach (var x in partes)
        {
            if (j.Count > 0 && j[j.Count - 1].Igual(x)) j[j.Count - 1].X1 = x.X1;
            else j.Add(x);
        }
        string Resumo() => string.Join(" | ", j.Select(x => $"{N(x.X0)}–{N(x.X1)} {(x.P == Parte.Dente ? "dente h " + N(x.H) + " " : "")}{(x.Aba.Length > 0 ? "com aba" : "sem aba")}"));
        // o que vale: [dente] + corrente (com um trecho de aba, no máximo) + [dente]
        bool denteE = j[0].P == Parte.Dente, denteD = j.Count > 1 && j[j.Count - 1].P == Parte.Dente;
        var meio = j.Skip(denteE ? 1 : 0).Take(j.Count - (denteE ? 1 : 0) - (denteD ? 1 : 0)).ToList();
        if (meio.Count == 0 || meio.Any(x => x.P != Parte.Corrente))
        {
            motivo = $"os trechos não formam [dente] + corrente + [dente] ({Resumo()}).";
            return null;
        }
        // a aba (a mesma, medida a partir do topo) num trecho contínuo — que pode pegar os dentes
        var comAba = j.Where(x => x.Aba.Length > 0).ToList();
        int ia = j.FindIndex(x => x.Aba.Length > 0);
        if (comAba.Count > 0 && (comAba.Select(x => x.Aba).Distinct().Count() > 1 || j.Skip(ia).Take(comAba.Count).Any(x => x.Aba.Length == 0)))
        {
            motivo = $"a aba muda ao longo da viga ou tem mais de um trecho ({Resumo()}) — o CAD desenha uma aba, num trecho só.";
            return null;
        }
        DentePeca Dente(Pedaco x) => new DentePeca { Altura = x.H, Comprimento = R(x.X1 - x.X0) };
        var v = new VigaPeca
        {
            B = R(i0.B), H = R(i0.H), L = R(r.Comprimento),
            DenteE = denteE ? Dente(j[0]) : null,
            DenteD = denteD ? Dente(j[j.Count - 1]) : null,
            Elementos = Elementos(g),
        };
        if (comAba.Count > 0)
        {
            var a = comAba[0].Secao;
            v.Aba = new AbaPeca
            {
                Lado = a.AbaEsquerda > 0 && a.AbaDireita > 0 ? AbaPeca.DoisLados : a.AbaEsquerda > 0 ? AbaPeca.Esquerda : AbaPeca.Direita,
                Largura = R(a.AbaLargura), Altura = R(a.AbaAltura), AlturaPonta = R(a.AbaAlturaPonta),
                Base = R(Math.Max(0, i0.H - a.DistanciaDoTopo - a.AbaAltura)),   // a partir do fundo da viga (a seção do dente começa mais alto)
                Inicio = R(comAba[0].X0), Fim = R(comAba[comAba.Count - 1].X1),
            };
            if (r.Consolos.Count > 0)
                notas.Add($"Viga {g.Marcas}: o consolo de apoio de outra viga ({N(v.Aba.Inicio)}–{N(v.Aba.Fim)} cm) vai como aba curta — conferir a armadura do consolo no CAD.");
        }
        else if (r.Consolos.Count > 0) { motivo = "tem consolo de apoio de outra viga com forma que o CAD ainda não desenha."; return null; }
        return v;
    }

    private static double R(double cm) => Math.Round(cm, 2);
    private static string N(double x) => x.ToString("0.#", CultureInfo.InvariantCulture);
    private static string Pct(double f) => (f * 100).ToString("0", CultureInfo.InvariantCulture) + "%";
}
