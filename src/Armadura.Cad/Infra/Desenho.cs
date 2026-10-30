using System.Collections.Generic;
using Armadura.Core.Configuracao;

namespace Armadura.Cad.Infra;

/// <summary>Unidades, camadas e percursos do desenho.</summary>
internal static class Desenho
{
    /// <summary>
    /// Quantos cm vale 1 unidade do desenho. Padrão do escritório: sempre cm (1).
    /// Com UnidadeDesenho = Automática, segue o INSUNITS (indefinido → configuração).
    /// </summary>
    public static double CmPorUnidade(Database db, ConfiguracaoArmadura cfg) =>
        cfg.UnidadeDesenho == UnidadeDesenho.Centimetro ? 1.0 : PorInsunits(db, cfg);

    private static double PorInsunits(Database db, ConfiguracaoArmadura cfg) => db.Insunits switch
    {
        UnitsValue.Millimeters => 0.1,
        UnitsValue.Centimeters => 1.0,
        UnitsValue.Decimeters => 10.0,
        UnitsValue.Meters => 100.0,
        UnitsValue.Inches => 2.54,
        UnitsValue.Feet => 30.48,
        _ => cfg.CmPorUnidadePadrao > 0 ? cfg.CmPorUnidadePadrao : 1.0,
    };

    public static ObjectId GarantirLayer(Database db, Transaction tr, string nome, short cor)
    {
        var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
        if (lt.Has(nome)) return lt[nome];
        lt.UpgradeOpen();
        var l = new LayerTableRecord { Name = nome };
        // padrão do layers.csv; fora dele, a cor dada e espessura 0,00 mm (nunca a "padrão" do CAD — pedido de 2026-10-30)
        var p = Layers.Obter(nome) ?? new Core.Configuracao.PadraoLayer { Nome = nome, Cor = cor == 2 ? (short)7 : cor };
        var id = lt.Add(l);
        tr.AddNewlyCreatedDBObject(l, true);
        Layers.Aplicar(db, tr, l, p);
        return id;
    }

    /// <summary>
    /// Entidades cuja classe é <typeparamref name="T"/> nos layouts (modelo e folhas) e, com
    /// <paramref name="incluirBlocos"/>, também nas definições de bloco (exceto xrefs).
    /// </summary>
    public static IEnumerable<ObjectId> Entidades<T>(Database db, Transaction tr, bool incluirBlocos = false) where T : Entity
    {
        var cls = Rt.RXClass.GetClass(typeof(T));
        var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
        foreach (ObjectId btrId in bt)
        {
            var btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
            if (btr.IsFromExternalReference || btr.IsDependent) continue;
            if (!btr.IsLayout && !incluirBlocos) continue;
            foreach (ObjectId id in btr)
                if (id.ObjectClass == cls || id.ObjectClass.IsDerivedFrom(cls))
                    yield return id;
        }
    }
}
