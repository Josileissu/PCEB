using System;
using System.Collections.Generic;
using System.Linq;
using DEB.Cad.Infra;
using DEB.Core.Configuracao;
using DEB.Core.Intercambio;
using DEB.Core.Modelo;
using DEB.Core.Templates;
using DEB.Core.Templates.PilarPreMoldado;

[assembly: Rt.CommandClass(typeof(DEB.Cad.ComandosPecas))]

namespace DEB.Cad;

/// <summary>
/// ARMIMPORTARPECAS (pedido do usuário, 2026-11-09): lê o .json exportado pelo DEB.Revit ("Exportar para o CAD") e gera um
/// detalhamento por grupo de pilares iguais pelo template "pilar-premoldado", lado a lado. Os valores partem do PADRÃO do
/// usuário (arquivo de modelos do template); o modelo do Revit troca seção, comprimento, níveis, embutimento e consolos.
/// As repetições do detalhamento = número de peças do grupo (a relação do aço multiplica). Os modelos do template não
/// são alterados (as marcas P1, P2… do Revit poderiam sobrescrever modelos do usuário com o mesmo nome).
/// </summary>
public sealed class ComandosPecas
{
    public const string TemplatePilar = "pilar-premoldado";

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
        if (arq.Pilares.Count == 0) { ed.WriteMessage("\nO arquivo não tem pilares."); return; }
        var rp = ed.GetPoint("\nPonto de inserção do primeiro pilar:");
        if (rp.Status != PromptStatus.OK) return;

        var rel = Importar(db, cfg, arq, rp.Value.TransformBy(ed.CurrentUserCoordinateSystem));
        foreach (var l in rel.Linhas) ed.WriteMessage("\n" + l);
        foreach (var a in rel.Avisos) ed.WriteMessage("\nAviso: " + a);
    });

    /// <summary>O que a importação fez: uma linha por grupo e os avisos.</summary>
    internal sealed class Relatorio
    {
        public List<string> Linhas { get; } = new List<string>();
        public List<string> Avisos { get; } = new List<string>();
        /// <summary>Nome do detalhamento criado por grupo (na ordem do arquivo; null = não gerado).</summary>
        public List<string> Detalhamentos { get; } = new List<string>();
        /// <summary>Retângulo ocupado por grupo (unidades do desenho), para conferência.</summary>
        public List<Extents3d?> Regioes { get; } = new List<Extents3d?>();
    }

    /// <summary>Gera os detalhamentos dos grupos lado a lado, a partir de <paramref name="origem"/>.</summary>
    internal static Relatorio Importar(Database db, ConfiguracaoArmadura cfg, ArquivoPecas arq, Point3d origem)
    {
        var rel = new Relatorio();
        var t = CatalogoTemplates.Obter(TemplatePilar);
        if (t == null) { rel.Avisos.Add($"O template \"{TemplatePilar}\" não está carregado."); return rel; }
        var padrao = ModelosTemplates.Obter(t).Valores(t);
        double cpu = Desenho.CmPorUnidade(db, cfg);
        double folga = 300 / cpu;
        var p0 = origem;

        rel.Linhas.Add($"{arq.Pilares.Count} grupo(s) de pilares de {(arq.Origem.Length > 0 ? arq.Origem : "(origem não informada)")}" +
                       (arq.Data.Length > 0 ? $", levantados em {arq.Data}" : "") + ":");
        foreach (var n in arq.Notas) rel.Avisos.Add("Revit: " + n);

        foreach (var p in arq.Pilares)
        {
            var avisos = new List<string>();
            var v = ConversorPilarPeca.Valores(p, padrao, TemplatePilarPreMoldado.Catalogo, avisos);
            var erros = CatalogoTemplates.Validar(t, v);
            if (erros.Count > 0)
            {
                rel.Avisos.Add($"{p.Marca}: não gerado — {string.Join(" ", erros)}");
                rel.Detalhamentos.Add(null);
                rel.Regioes.Add(null);
                continue;
            }
            var r = t.Gerar(v, cfg);
            r.DadosDetalhamento ??= new DadosDetalhamento();
            r.DadosDetalhamento.Repeticoes = p.Quantidade;
            r.DadosDetalhamento.Sistema = SistemaConstrutivo.PreMoldado;
            r.DadosDetalhamento.Grupo = "Pilares";
            var ids = string.Join(", ", p.Elementos.Select(e => e.Id).Where(s => s.Length > 0));
            r.DadosDetalhamento.Comentarios = $"Importado de {arq.Origem} ({arq.Data})" + (ids.Length > 0 ? $"; elementos {ids}" : "") + ".";

            Extents3d? ext;
            var antes = Entidades(db);
            using (var tr = db.TransactionManager.StartTransaction())
            {
                ComandosTemplate.Desenhar(tr, db, cfg, r, p0);
                tr.Commit();
            }
            ext = Extensao(db, Entidades(db).Except(antes));
            rel.Detalhamentos.Add(r.Detalhamento);
            rel.Regioes.Add(ext);

            int nConsolos = v.Tabela("Consolos").Count;
            rel.Linhas.Add($"  {p.Marca}: {p.Quantidade} peça(s), {TemplatePilarPreMoldado.Num(p.B, "0.#")}×{TemplatePilarPreMoldado.Num(p.H, "0.#")}, " +
                           $"L = {TemplatePilarPreMoldado.Num(p.L, "0.#")} cm, {nConsolos} consolo(s) → detalhamento \"{r.Detalhamento}\"" +
                           (p.Elementos.Any(e => e.Marca.Length > 0) ? $" (marcas no modelo: {string.Join(", ", p.Elementos.Select(e => e.Marca).Where(s => s.Length > 0).Distinct())})" : ""));
            rel.Avisos.AddRange(avisos);
            rel.Avisos.AddRange(r.Avisos.Select(a => $"{p.Marca}: {a}"));

            // o próximo à direita do que foi desenhado
            double x = ext.HasValue ? ext.Value.MaxPoint.X : p0.X + p.B * 10 / cpu;
            p0 = new Point3d(x + folga, origem.Y, origem.Z);
        }
        return rel;
    }

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
