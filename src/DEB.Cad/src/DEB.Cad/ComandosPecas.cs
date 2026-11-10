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

        var rel = Importar(db, cfg, arq, rp.Value.TransformBy(ed.CurrentUserCoordinateSystem), mudaram =>
        {
            // reimportação: peças que já estão no desenho e mudaram no modelo
            ed.WriteMessage($"\n{mudaram.Count} detalhamento(s) deste desenho mudaram no modelo: {string.Join(", ", mudaram)}.");
            var op = new PromptKeywordOptions("\nSubstituir pelos novos (o que foi ajustado à mão neles se perde) ou manter os dois? [Substituir/Manter]", "Substituir Manter")
            { AllowNone = true };
            op.Keywords.Default = "Manter";
            var rk = ed.GetKeywords(op);
            return rk.Status == PromptStatus.OK && rk.StringResult == "Substituir";
        });
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
        /// <summary>Reimportação: grupos sem mudança no modelo (não redesenhados) e grupos substituídos no lugar.</summary>
        public List<string> Mantidos { get; } = new List<string>();
        public List<string> Substituidos { get; } = new List<string>();
        /// <summary>Resposta do usuário para os que mudaram: substituir (true) ou manter os dois.</summary>
        public bool Substituir { get; set; }
    }

    /// <summary>Gera os detalhamentos: pilares numa fila a partir de <paramref name="origem"/>, vigas na fila de baixo.</summary>
    /// <param name="perguntarSubstituir">
    /// Reimportação: chamado UMA vez com os detalhamentos importados antes que mudaram no modelo; true = substituir no lugar,
    /// false (ou null) = manter os dois (o novo entra como "nome (2)"). Os que não mudaram nunca são redesenhados.
    /// </param>
    internal static Relatorio Importar(Database db, ConfiguracaoArmadura cfg, ArquivoPecas arq, Point3d origem,
        Func<IReadOnlyList<string>, bool> perguntarSubstituir = null)
    {
        var rel = new Relatorio();
        var mudaram = arq.Pilares.Select(p => (p.Marca, A: AssinaturaPeca.De(p))).Concat(arq.Vigas.Select(v => (v.Marca, A: AssinaturaPeca.De(v))))
            .Where(x => { var ex = Existente(db, ComandosTemplate.NomeDeBloco(x.Marca)); return ex.Existe && AssinaturaPeca.Importado(ex.Comentarios) && AssinaturaPeca.DoComentario(ex.Comentarios) != x.A; })
            .Select(x => x.Marca).ToList();
        rel.Substituir = mudaram.Count > 0 && perguntarSubstituir != null && perguntarSubstituir(mudaram);
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
                    return new Grupo(p.Marca, v, p.Quantidade, p.Elementos, resumo, avisos) { Assinatura = AssinaturaPeca.De(p) };
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
                    return new Grupo(p.Marca, v, p.Quantidade, p.Elementos, resumo, avisos) { Assinatura = AssinaturaPeca.De(p) };
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
        /// <summary>Assinatura dos dados do modelo (<see cref="AssinaturaPeca"/>), gravada no comentário do detalhamento.</summary>
        public string Assinatura { get; set; } = "";
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
            // reimportação: o que já está no desenho com os mesmos dados do modelo fica como está (com os ajustes do usuário)
            string nomeBloco = ComandosTemplate.NomeDeBloco(g.Marca);
            var ex = Existente(db, nomeBloco);
            bool importado = ex.Existe && AssinaturaPeca.Importado(ex.Comentarios);
            if (importado && AssinaturaPeca.DoComentario(ex.Comentarios) == g.Assinatura)
            {
                rel.Linhas.Add($"  {g.Marca}: sem mudança no modelo — detalhamento \"{nomeBloco}\" mantido como está");
                rel.Grupos.Add((g.Marca, nomeBloco, null));
                rel.Mantidos.Add(g.Marca);
                continue;
            }
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
            r.DadosDetalhamento.Comentarios = AssinaturaPeca.Comentario(arq.Origem, arq.Data, g.Elementos.Select(e => e.Id), g.Assinatura);

            double y = topoNaOrigem ? p0.Y - Topo(r) / cpu : p0.Y;
            double xDesenho = x;
            // mudou no modelo e o usuário mandou substituir: apaga o antigo (com a relação do aço dele) e desenha o novo com o canto
            // superior esquerdo no mesmo lugar — a posição que o usuário tinha dado na prancha se mantém
            Point3d? canto = null;
            if (importado && rel.Substituir)
            {
                canto = Apagar(db, cfg, nomeBloco);
                rel.Substituidos.Add(g.Marca);
            }
            bool noLugar = canto.HasValue;
            var antes = Entidades(db);
            using (var tr = db.TransactionManager.StartTransaction())
            {
                ComandosTemplate.Desenhar(tr, db, cfg, r, new Point3d(xDesenho, y, p0.Z));
                tr.Commit();
            }
            var novas = Entidades(db).Except(antes).ToList();
            if (noLugar) Reposicionar(db, cfg, novas, r.Detalhamento, canto.Value);
            var ext = Extensao(db, novas);
            rel.Grupos.Add((g.Marca, r.Detalhamento, ext));
            var marcas = g.Elementos.Select(e => e.Marca).Where(s => s.Length > 0).Distinct().ToList();
            rel.Linhas.Add($"  {g.Marca}: {g.Resumo} → detalhamento \"{r.Detalhamento}\"" + (rel.Substituidos.Contains(g.Marca) ? " (substituiu o anterior)" : "") +
                           (marcas.Count > 0 ? $" (marcas no modelo: {string.Join(", ", marcas)})" : ""));
            rel.Avisos.AddRange(r.Avisos.Select(a => $"{g.Marca}: {a}"));
            // a marca é a do modelo: importar de novo depois de mudar o modelo NÃO substitui o detalhamento que já existe
            if (!string.Equals(r.Detalhamento, nomeBloco, StringComparison.OrdinalIgnoreCase))
                rel.Avisos.Add(importado
                    ? $"{g.Marca}: MUDOU no modelo desde a última importação; o novo entrou como \"{r.Detalhamento}\" e o antigo continua no desenho — confira e apague o que não vale mais."
                    : $"{g.Marca}: o desenho já tinha um detalhamento com esse nome (não veio de uma importação); o novo entrou como \"{r.Detalhamento}\".");
            // o que foi posto no lugar do antigo não entra na fila
            if (noLugar) continue;

            // a próxima à direita do que foi desenhado
            if (ext.HasValue) { x = ext.Value.MaxPoint.X + folga; yMin = Math.Min(yMin, ext.Value.MinPoint.Y); }
            else x += 1000 / cpu;
        }
        return yMin;
    }

    /// <summary>O detalhamento (bloco) com este nome: existe? com que comentário?</summary>
    private static (bool Existe, string Comentarios) Existente(Database db, string nome)
    {
        using var tr = db.TransactionManager.StartTransaction();
        var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
        (bool, string) r = (false, "");
        if (nome.Length > 0 && bt.Has(nome) && !bt[nome].IsErased)
            r = (true, Relacao.ServicoDetalhamento.Ler((BlockTableRecord)tr.GetObject(bt[nome], OpenMode.ForRead))?.Comentarios ?? "");
        tr.Commit();
        return r;
    }

    /// <summary>
    /// Apaga um detalhamento importado antes: as inserções do bloco, as relações do aço que eram SÓ dele e a definição.
    /// Devolve o canto superior esquerdo da primeira inserção (para o novo entrar no mesmo lugar); null se não havia inserção.
    /// </summary>
    private static Point3d? Apagar(Database db, ConfiguracaoArmadura cfg, string nome)
    {
        Point3d? canto = null;
        using var tr = db.TransactionManager.StartTransaction();
        var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
        if (!bt.Has(nome)) { tr.Commit(); return null; }
        var btr = (BlockTableRecord)tr.GetObject(bt[nome], OpenMode.ForWrite);
        foreach (ObjectId rid in btr.GetBlockReferenceIds(true, false))
        {
            if (rid.IsErased || !(tr.GetObject(rid, OpenMode.ForWrite) is BlockReference br)) continue;
            if (!canto.HasValue)
                try { var e = br.GeometricExtents; canto = new Point3d(e.MinPoint.X, e.MaxPoint.Y, br.Position.Z); }
                catch { canto = br.Position; }
            br.Erase();
        }
        foreach (ObjectId lid in bt)
        {
            var espaco = (BlockTableRecord)tr.GetObject(lid, OpenMode.ForRead);
            if (!espaco.IsLayout) continue;
            foreach (ObjectId id in espaco)
            {
                if (id.IsErased || !(tr.GetObject(id, OpenMode.ForRead) is Table t)) continue;
                var op = Relacao.ServicoRA.Opcoes(t, cfg);
                if (op == null || op.Blocos.Count != 1 || !string.Equals(op.Blocos[0], nome, StringComparison.OrdinalIgnoreCase)) continue;
                t.UpgradeOpen();
                Relacao.ServicoRA.ApagarDetalhes(tr, t, cfg);
                t.Erase();
            }
        }
        // o conteúdo antes da definição: assim cada barra e cada parte fica marcada como apagada (o Sincronizador pula as apagadas;
        // apagando só a definição, ele ainda tentava atualizar as barras de dentro e parava num eWasErased)
        foreach (ObjectId id in btr)
            if (!id.IsErased) tr.GetObject(id, OpenMode.ForWrite).Erase();
        btr.Erase();
        tr.Commit();
        return canto;
    }

    /// <summary>
    /// Leva o detalhamento recém-desenhado (a inserção do bloco e a relação do aço dele) para o lugar do antigo: o canto superior
    /// esquerdo da inserção nova vai para <paramref name="canto"/>, o da antiga.
    /// </summary>
    private static void Reposicionar(Database db, ConfiguracaoArmadura cfg, List<ObjectId> novas, string nomeBloco, Point3d canto)
    {
        using var tr = db.TransactionManager.StartTransaction();
        BlockReference br = null;
        foreach (var id in novas)
            if (!id.IsErased && tr.GetObject(id, OpenMode.ForRead) is BlockReference b && string.Equals(b.Name, nomeBloco, StringComparison.OrdinalIgnoreCase)) { br = b; break; }
        if (br == null) { tr.Commit(); return; }
        Extents3d e;
        try { e = br.GeometricExtents; } catch { tr.Commit(); return; }
        var desloc = Matrix3d.Displacement(new Vector3d(canto.X - e.MinPoint.X, canto.Y - e.MaxPoint.Y, 0));
        foreach (var id in novas)
        {
            if (id.IsErased || !(tr.GetObject(id, OpenMode.ForWrite) is Entity ent)) continue;
            ent.TransformBy(desloc);
            if (ent is Table t) Relacao.ServicoRA.AcompanharMovimento(tr, t, cfg);
        }
        tr.Commit();
    }

    /// <summary>Ponto mais alto (cm, relativo à origem do template) do que o template gerou: forma, textos, barras e relação do aço.</summary>
    private static double Topo(ResultadoTemplate r) =>
        r.Contornos.SelectMany(c => c.Vertices.Select(v => v.Y))
            .Concat(r.Textos.Select(q => q.Y))
            .Concat(r.Barras.SelectMany(b => b.Vertices.Select(v => v.Y)))
            .Concat(r.PosicaoRA.HasValue ? new[] { r.PosicaoRA.Value.Y } : Array.Empty<double>())
            .DefaultIfEmpty(0).Max();

    private static string Num(double x) => KitDesenho.Num(x, "0.#");

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
