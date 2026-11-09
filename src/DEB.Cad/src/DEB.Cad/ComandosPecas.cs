using System;
using System.Collections.Generic;
using System.Linq;
using DEB.Cad.Infra;
using DEB.Core.Configuracao;
using DEB.Core.Intercambio;
using DEB.Core.Modelo;
using DEB.Core.Templates;
using DEB.Core.Templates.PilarPreMoldado;
using DEB.Core.Templates.VigaPreMoldada;

[assembly: Rt.CommandClass(typeof(DEB.Cad.ComandosPecas))]

namespace DEB.Cad;

/// <summary>
/// ARMIMPORTARPECAS (pedido do usuário, 2026-11-09): lê o .json exportado pelo DEB.Revit ("Exportar para o CAD") e gera um
/// detalhamento por grupo de peças iguais — pilares pelo template "pilar-premoldado" numa fila e vigas pelo
/// "viga-premoldada" na fila de baixo, lado a lado. Os valores partem do PADRÃO do usuário (arquivo de modelos de cada
/// template); o modelo do Revit troca as medidas (pilar: seção, comprimento, níveis, embutimento, consolos; viga: seção,
/// comprimento, dentes). As repetições do detalhamento = número de peças do grupo (a relação do aço multiplica). Os modelos
/// dos templates não são alterados (as marcas P1, V1… do Revit poderiam sobrescrever modelos do usuário com o mesmo nome).
/// </summary>
public sealed class ComandosPecas
{
    public const string TemplatePilar = "pilar-premoldado", TemplateViga = "viga-premoldada";

    [Rt.CommandMethod("ARMIMPORTARPECAS")]
    public void ImportarPecas() => Comandos.Executar("ARMIMPORTARPECAS", (doc, ed, db, cfg) =>
    {
        string caminho = Arquivos.Pedir(ed, false, "Peças exportadas do Revit", "Peças do DEB (*.json)|*.json", ".json");
        if (string.IsNullOrEmpty(caminho)) return;
        ArquivoPecas arq;
        try { arq = ArquivoPecas.Ler(caminho); }
        catch (Exception ex)
        {
            Log.Erro("ARMIMPORTARPECAS (leitura)", ex);
            ed.WriteMessage($"\nNão foi possível ler o arquivo: {ex.Message}");
            return;
        }
        if (arq.Pilares.Count == 0 && arq.Vigas.Count == 0) { ed.WriteMessage("\nO arquivo não tem pilares nem vigas."); return; }
        var rp = ed.GetPoint("\nPonto de inserção (canto da primeira peça):");
        if (rp.Status != PromptStatus.OK) return;

        var rel = Importar(db, cfg, arq, rp.Value.TransformBy(ed.CurrentUserCoordinateSystem));
        if (rel.CatalogosAlterados) Plugin.SalvarConfig();   // dentes/consolos criados na importação ficam no catálogo do usuário
        foreach (var l in rel.Linhas) ed.WriteMessage("\n" + l);
        foreach (var a in rel.Avisos) ed.WriteMessage("\nAviso: " + a);
    });

    /// <summary>O que a importação fez: uma linha por grupo e os avisos.</summary>
    internal sealed class Relatorio
    {
        public List<string> Linhas { get; } = new List<string>();
        public List<string> Avisos { get; } = new List<string>();
        /// <summary>Por grupo, na ordem do arquivo (pilares, depois vigas): marca, detalhamento criado (null = não gerado) e região.</summary>
        public List<(string Marca, string Detalhamento, Extents3d? Regiao)> Grupos { get; } = new List<(string, string, Extents3d?)>();
        /// <summary>A importação acrescentou dentes ou consolos aos catálogos (o comando grava a configuração).</summary>
        public bool CatalogosAlterados { get; set; }
    }

    /// <summary>Gera os detalhamentos: pilares numa fila a partir de <paramref name="origem"/>, vigas na fila de baixo.</summary>
    internal static Relatorio Importar(Database db, ConfiguracaoArmadura cfg, ArquivoPecas arq, Point3d origem)
    {
        var rel = new Relatorio();
        double cpu = Desenho.CmPorUnidade(db, cfg);
        double folga = 300 / cpu;
        rel.Linhas.Add($"Peças de {(arq.Origem.Length > 0 ? arq.Origem : "(origem não informada)")}" +
                       (arq.Data.Length > 0 ? $", levantadas em {arq.Data}" : "") + ":");
        foreach (var n in arq.Notas) rel.Avisos.Add("Revit: " + n);
        CompletarCatalogos(arq, rel);

        // ---- pilares ----
        double yBaixo = origem.Y;
        if (arq.Pilares.Count > 0)
        {
            var t = CatalogoTemplates.Obter(TemplatePilar);
            if (t == null) rel.Avisos.Add($"O template \"{TemplatePilar}\" não está carregado: pilares não gerados.");
            else
            {
                var padrao = ModelosTemplates.Obter(t).Valores(t);
                var fila = arq.Pilares.Select(p =>
                {
                    var avisos = new List<string>();
                    var v = ConversorPilarPeca.Valores(p, padrao, TemplatePilarPreMoldado.Catalogo, avisos);
                    string resumo = $"{p.Quantidade} peça(s), {Num(p.B)}×{Num(p.H)}, L = {Num(p.L)} cm, {v.Tabela("Consolos").Count} consolo(s)";
                    return new Grupo(p.Marca, v, p.Quantidade, p.Elementos, resumo, avisos);
                });
                yBaixo = Fila(db, cfg, t, fila, origem, folga, cpu, arq, rel);
            }
        }

        // ---- vigas: fila de baixo, com o topo abaixo dos pilares ----
        if (arq.Vigas.Count > 0)
        {
            var t = CatalogoTemplates.Obter(TemplateViga);
            if (t == null) rel.Avisos.Add($"O template \"{TemplateViga}\" não está carregado: vigas não gerados.");
            else
            {
                var padrao = ModelosTemplates.Obter(t).Valores(t);
                var fila = arq.Vigas.Select(p =>
                {
                    var avisos = new List<string>();
                    var v = ConversorVigaPeca.Valores(p, padrao, TemplateVigaPreMoldada.CatalogoDentes, avisos);
                    string resumo = $"{p.Quantidade} peça(s), {Num(p.B)}×{Num(p.H)}, L = {Num(p.L)} cm, pontas: " +
                                    (v == null ? "?" : $"{v["ExtE"]} / {v["ExtD"]}");
                    return new Grupo(p.Marca, v, p.Quantidade, p.Elementos, resumo, avisos);
                });
                var y = arq.Pilares.Count > 0 ? yBaixo - folga : origem.Y;
                Fila(db, cfg, t, fila, new Point3d(origem.X, y, origem.Z), folga, cpu, arq, rel, topoNaOrigem: true);
            }
        }
        return rel;
    }

    /// <summary>
    /// Dentes e consolos do modelo que faltam nos catálogos: criados com a geometria do modelo e a armadura padrão (opção b do
    /// usuário, 2026-11-09), com aviso para conferir no ARMDENTES/ARMCONSOLOS. Ficam na configuração do usuário (o comando grava).
    /// </summary>
    private static void CompletarCatalogos(ArquivoPecas arq, Relatorio rel)
    {
        var novosD = CatalogoAutomatico.Dentes(arq.Vigas, TemplateVigaPreMoldada.CatalogoDentes);
        if (novosD.Count > 0)
        {
            Plugin.Config.Dentes.AddRange(novosD);
            TemplateVigaPreMoldada.CatalogoDentes = Plugin.Config.Dentes;
            rel.Avisos.Add($"Criado(s) no catálogo de dentes: {string.Join(", ", novosD.Select(t => t.Nome))} — armadura padrão: conferir no ARMDENTES.");
        }
        // a almofada do consolo novo cabe no cobrimento das faces do PADRÃO do pilar
        double cob = 2.5;
        var tp = CatalogoTemplates.Obter(TemplatePilar);
        if (tp != null)
        {
            var vp = ModelosTemplates.Obter(tp).Valores(tp);
            cob = new[] { "CobFace1", "CobFace2", "CobFace3", "CobFace4" }.Select(k => vp.Numero(k)).DefaultIfEmpty(2.5).Max();
        }
        var novosC = CatalogoAutomatico.Consolos(arq.Pilares, TemplatePilarPreMoldado.Catalogo, cob);
        if (novosC.Count > 0)
        {
            Plugin.Config.Consolos.AddRange(novosC);
            TemplatePilarPreMoldado.Catalogo = Plugin.Config.Consolos;
            rel.Avisos.Add($"Criado(s) no catálogo de consolos: {string.Join(", ", novosC.Select(t => t.Nome))} — armadura padrão: conferir no ARMCONSOLOS.");
        }
        rel.CatalogosAlterados = novosD.Count + novosC.Count > 0;
    }

    private sealed class Grupo
    {
        public Grupo(string marca, ValoresTemplate v, int qtd, List<ElementoPeca> els, string resumo, List<string> avisos)
        { Marca = marca; Valores = v; Quantidade = qtd; Elementos = els; Resumo = resumo; Avisos = avisos; }
        public string Marca { get; }
        /// <summary>null = não dá para gerar (motivo nos avisos).</summary>
        public ValoresTemplate Valores { get; }
        public int Quantidade { get; }
        public List<ElementoPeca> Elementos { get; }
        public string Resumo { get; }
        public List<string> Avisos { get; }
    }

    /// <summary>
    /// Desenha os grupos lado a lado a partir de <paramref name="p0"/>; devolve o y mais baixo desenhado.
    /// <paramref name="topoNaOrigem"/>: o ponto é o TOPO da fila (cada peça descida pela altura do que o template gera).
    /// </summary>
    private static double Fila(Database db, ConfiguracaoArmadura cfg, ITemplateArmadura t, IEnumerable<Grupo> grupos, Point3d p0,
        double folga, double cpu, ArquivoPecas arq, Relatorio rel, bool topoNaOrigem = false)
    {
        double x = p0.X, yMin = p0.Y;
        foreach (var g in grupos)
        {
            rel.Avisos.AddRange(g.Avisos);
            if (g.Valores == null) { rel.Grupos.Add((g.Marca, null, null)); continue; }
            var erros = CatalogoTemplates.Validar(t, g.Valores);
            if (erros.Count > 0)
            {
                rel.Avisos.Add($"{g.Marca}: não gerado — {string.Join(" ", erros)}");
                rel.Grupos.Add((g.Marca, null, null));
                continue;
            }
            var r = t.Gerar(g.Valores, cfg);
            r.DadosDetalhamento ??= new DadosDetalhamento();
            r.DadosDetalhamento.Repeticoes = g.Quantidade;
            r.DadosDetalhamento.Sistema = SistemaConstrutivo.PreMoldado;
            var ids = string.Join(", ", g.Elementos.Select(e => e.Id).Where(s => s.Length > 0));
            r.DadosDetalhamento.Comentarios = $"Importado de {arq.Origem} ({arq.Data})" + (ids.Length > 0 ? $"; elementos {ids}" : "") + ".";

            double y = topoNaOrigem ? p0.Y - Topo(r) / cpu : p0.Y;
            var antes = Entidades(db);
            using (var tr = db.TransactionManager.StartTransaction())
            {
                ComandosTemplate.Desenhar(tr, db, cfg, r, new Point3d(x, y, p0.Z));
                tr.Commit();
            }
            var ext = Extensao(db, Entidades(db).Except(antes));
            rel.Grupos.Add((g.Marca, r.Detalhamento, ext));
            var marcas = g.Elementos.Select(e => e.Marca).Where(s => s.Length > 0).Distinct().ToList();
            rel.Linhas.Add($"  {g.Marca}: {g.Resumo} → detalhamento \"{r.Detalhamento}\"" +
                           (marcas.Count > 0 ? $" (marcas no modelo: {string.Join(", ", marcas)})" : ""));
            rel.Avisos.AddRange(r.Avisos.Select(a => $"{g.Marca}: {a}"));

            // a próxima à direita do que foi desenhado
            if (ext.HasValue) { x = ext.Value.MaxPoint.X + folga; yMin = Math.Min(yMin, ext.Value.MinPoint.Y); }
            else x += 1000 / cpu;
        }
        return yMin;
    }

    /// <summary>Ponto mais alto (cm, relativo à origem do template) do que o template gerou: forma, textos, barras e relação do aço.</summary>
    private static double Topo(ResultadoTemplate r) =>
        r.Contornos.SelectMany(c => c.Vertices.Select(v => v.Y))
            .Concat(r.Textos.Select(q => q.Y))
            .Concat(r.Barras.SelectMany(b => b.Vertices.Select(v => v.Y)))
            .Concat(r.PosicaoRA.HasValue ? new[] { r.PosicaoRA.Value.Y } : Array.Empty<double>())
            .DefaultIfEmpty(0).Max();

    private static string Num(double x) => TemplatePilarPreMoldado.Num(x, "0.#");

    private static HashSet<ObjectId> Entidades(Database db)
    {
        using var tr = db.TransactionManager.StartTransaction();
        var esp = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);
        var s = new HashSet<ObjectId>(esp.Cast<ObjectId>());
        tr.Commit();
        return s;
    }

    private static Extents3d? Extensao(Database db, IEnumerable<ObjectId> ids)
    {
        Extents3d? e = null;
        using var tr = db.TransactionManager.StartTransaction();
        foreach (var id in ids)
        {
            try
            {
                var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (ent == null) continue;
                var g = ent.GeometricExtents;
                if (e.HasValue) { var u = e.Value; u.AddExtents(g); e = u; } else e = g;
            }
            catch { /* entidade sem extensão (texto vazio etc.) */ }
        }
        tr.Commit();
        return e;
    }
}
