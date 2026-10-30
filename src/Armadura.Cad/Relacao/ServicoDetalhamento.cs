using System;
using System.Collections.Generic;
using System.Linq;
using Armadura.Cad.Infra;
using Armadura.Core.Modelo;
using Armadura.Core.Persistencia;

namespace Armadura.Cad.Relacao;

/// <summary>Detalhamentos (seção 2.9) como blocos do CAD; os dados ficam no XData da definição.</summary>
internal static class ServicoDetalhamento
{
    public static DadosDetalhamento Ler(BlockTableRecord btr)
    {
        var c = XDataCad.Ler(btr);
        return c == null ? null : CodecDados.DecodificarDetalhamento(c);
    }

    public static void Gravar(Database db, Transaction tr, BlockTableRecord btr, DadosDetalhamento d)
    {
        XDataCad.GarantirRegApp(db, tr);
        if (!btr.IsWriteEnabled) btr.UpgradeOpen();
        XDataCad.Gravar(btr, d);
    }

    /// <summary>Nome válido e livre para um bloco novo; null se ok, senão a mensagem.</summary>
    public static string ValidarNome(Database db, Transaction tr, string nome, ObjectId ignorar = default)
    {
        if (string.IsNullOrWhiteSpace(nome)) return "Informe um nome.";
        if (nome.IndexOfAny("<>/\\\":;?*|,=`".ToCharArray()) >= 0) return "O nome tem caracteres inválidos (< > / \\ \" : ; ? * | , = `).";
        var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
        if (bt.Has(nome) && bt[nome] != ignorar) return $"Já existe um bloco chamado \"{nome}\".";
        return null;
    }

    /// <summary>
    /// Transforma as entidades num bloco (o detalhamento) e insere uma referência no mesmo lugar.
    /// As referências entre barras e suas partes são traduzidas pelo DeepClone (XData 1005).
    /// </summary>
    public static ObjectId Criar(Database db, Transaction tr, ObjectId[] ids, string nome, DadosDetalhamento dados)
    {
        if (ids.Length == 0) throw new ArgumentException("Nada selecionado.");
        var erro = ValidarNome(db, tr, nome);
        if (erro != null) throw new ArgumentException(erro);

        // ponto base: canto inferior esquerdo da seleção
        Extents3d? ext = null;
        foreach (var id in ids)
        {
            var e = (Entity)tr.GetObject(id, OpenMode.ForRead);
            try { var x = e.GeometricExtents; if (ext == null) ext = x; else { var v = ext.Value; v.AddExtents(x); ext = v; } }
            catch { /* entidade sem extensão */ }
        }
        var basePt = ext?.MinPoint ?? Point3d.Origin;
        var dono = ((Entity)tr.GetObject(ids[0], OpenMode.ForRead)).OwnerId;

        var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);
        var btr = new BlockTableRecord { Name = nome, Origin = Point3d.Origin };
        var btrId = bt.Add(btr);
        tr.AddNewlyCreatedDBObject(btr, true);

        using (var col = new ObjectIdCollection(ids.Where(i => ((Entity)tr.GetObject(i, OpenMode.ForRead)).OwnerId == dono).ToArray()))
        using (var mapa = new IdMapping())
        {
            db.DeepCloneObjects(col, btrId, mapa, false);
            var desloc = Matrix3d.Displacement(Point3d.Origin - basePt);
            foreach (IdPair par in mapa)
            {
                if (!par.IsCloned || !par.IsPrimary) continue;
                if (!(tr.GetObject(par.Value, OpenMode.ForWrite) is Entity clone)) continue;
                clone.TransformBy(desloc);
                // faixas guardam pontos absolutos no XData (linha base, arco, referências das chamadas)
                if (XDataCad.Tipo(clone) == TipoParte.Faixa) Barra.ServicoFaixa.Transformar(clone, desloc);
            }
            foreach (ObjectId id in col) tr.GetObject(id, OpenMode.ForWrite).Erase();
        }

        Gravar(db, tr, btr, dados);
        var esp = (BlockTableRecord)tr.GetObject(dono, OpenMode.ForWrite);
        var br = new BlockReference(basePt, btrId);
        br.SetDatabaseDefaults(db);
        esp.AppendEntity(br);
        tr.AddNewlyCreatedDBObject(br, true);
        return br.ObjectId;
    }

    /// <summary>Inserções do bloco nos layouts (diretas e de versões anônimas de blocos dinâmicos).</summary>
    public static int Insercoes(Transaction tr, BlockTableRecord btr)
    {
        int n = 0;
        var defs = new List<ObjectId> { btr.ObjectId };
        if (btr.IsDynamicBlock) defs.AddRange(btr.GetAnonymousBlockIds().Cast<ObjectId>());
        foreach (var defId in defs)
        {
            var def = (BlockTableRecord)tr.GetObject(defId, OpenMode.ForRead);
            foreach (ObjectId rid in def.GetBlockReferenceIds(true, false))
            {
                var r = (BlockReference)tr.GetObject(rid, OpenMode.ForRead);
                if (r.IsErased) continue;
                var owner = (BlockTableRecord)tr.GetObject(r.OwnerId, OpenMode.ForRead);
                n += owner.IsLayout ? 1 : Insercoes(tr, owner); // aninhado: × inserções do bloco pai
            }
        }
        return n;
    }

    /// <summary>Blocos que são detalhamentos: marcados pelo plugin ou que contêm armaduras.</summary>
    public static List<(BlockTableRecord btr, DadosDetalhamento dados)> Todos(Database db, Transaction tr)
    {
        var r = new List<(BlockTableRecord, DadosDetalhamento)>();
        var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
        foreach (ObjectId id in bt)
        {
            var btr = (BlockTableRecord)tr.GetObject(id, OpenMode.ForRead);
            if (btr.IsLayout || btr.IsAnonymous || btr.IsFromExternalReference || btr.IsDependent) continue;
            var d = Ler(btr);
            if (d == null)
            {
                bool temBarra = btr.Cast<ObjectId>().Any(e => e.ObjectClass.Name.Contains("Polyline")
                                                               && XDataCad.Tipo(tr.GetObject(e, OpenMode.ForRead)) == TipoParte.Barra);
                if (!temBarra) continue;
                d = new DadosDetalhamento();
            }
            r.Add((btr, d));
        }
        return r.OrderBy(x => x.Item1.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Grupo sugerido pela inicial do nome (V12 → Vigas, P5 → Pilares…).</summary>
    public static string GrupoSugerido(string nome)
    {
        if (string.IsNullOrEmpty(nome)) return "";
        switch (char.ToUpperInvariant(nome[0]))
        {
            case 'V': return "Vigas";
            case 'P': return "Pilares";
            case 'L': return "Lajes";
            case 'E': return "Escadas";
            case 'S': case 'B': case 'F': return "Fundações";
            default: return "";
        }
    }
}
