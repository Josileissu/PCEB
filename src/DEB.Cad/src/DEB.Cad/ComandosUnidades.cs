using System;
using System.Collections.Generic;
using DEB.Cad.Infra;
using DEB.Core.Configuracao;

[assembly: Rt.CommandClass(typeof(DEB.Cad.ComandosUnidades))]

namespace DEB.Cad;

/// <summary>
/// ARMUNIDADES (pedido do usuário, 2026-11-10: "desenho no CAD em metros e a apresentação das dimensões dessa barra em milímetros"):
/// escolhe, PARA ESTE DESENHO, em que unidade ele é feito (1 unidade do CAD = 1 cm, 1 m ou 1 mm) e em que unidade as medidas das
/// barras são escritas (cotas, C=, c/, comprimento das faixas e a relação do aço). Grava no DWG (registro DEB_UNIDADES) e reescreve
/// as descrições, cotas e tabelas. A GEOMETRIA não é convertida: a unidade do desenho tem de ser a que ele já usa.
/// </summary>
public sealed class ComandosUnidades
{
    private static readonly (string Palavra, UnidadeDesenho Valor)[] Desenhos =
        { ("Centimetro", UnidadeDesenho.Centimetro), ("Metro", UnidadeDesenho.Metro), ("Milimetro", UnidadeDesenho.Milimetro) };
    private static readonly (string Palavra, UnidadeApresentacao Valor)[] Apresentacoes =
        { ("Centimetro", UnidadeApresentacao.Centimetro), ("Metro", UnidadeApresentacao.Metro), ("Milimetro", UnidadeApresentacao.Milimetro) };

    internal static string Nome(UnidadeDesenho u) => u switch
    {
        UnidadeDesenho.Metro => "metro", UnidadeDesenho.Milimetro => "milímetro", UnidadeDesenho.Automatica => "automática (INSUNITS)", _ => "centímetro",
    };

    internal static string Nome(UnidadeApresentacao u) => u switch
    {
        UnidadeApresentacao.Metro => "metro", UnidadeApresentacao.Milimetro => "milímetro", _ => "centímetro",
    };

    [Rt.CommandMethod("ARMUNIDADES")]
    public void ArmUnidades() => Comandos.Executar("ARMUNIDADES", (doc, ed, db, cfg) =>
    {
        ed.WriteMessage($"\nEste desenho: feito em {Nome(cfg.UnidadeDesenho)}, medidas escritas em {Nome(cfg.UnidadeApresentacao)}" +
                        (ConfigDesenho.UnidadesDoDesenho(db) == null ? " (padrão da configuração)." : "."));
        var d = Perguntar(ed, "\nUnidade do DESENHO (1 unidade do CAD = …)", Desenhos, cfg.UnidadeDesenho == UnidadeDesenho.Automatica ? UnidadeDesenho.Centimetro : cfg.UnidadeDesenho);
        if (d == null) return;
        var a = Perguntar(ed, "\nUnidade em que as medidas das barras são ESCRITAS", Apresentacoes, cfg.UnidadeApresentacao);
        if (a == null) return;
        Aplicar(db, d.Value, a.Value, ed);
    });

    /// <summary>Grava as unidades no desenho e reescreve descrições, cotas, faixas e tabelas (o autoteste chama direto).</summary>
    internal static void Aplicar(Database db, UnidadeDesenho desenho, UnidadeApresentacao apresentacao, Editor ed = null)
    {
        var antes = ConfigDesenho.Obter(db);
        bool mudouDesenho = antes.UnidadeDesenho != desenho;
        using (var tr = db.TransactionManager.StartTransaction())
        {
            ConfigDesenho.GravarUnidades(db, tr, desenho, apresentacao);
            var cfg = ConfigDesenho.Obter(db);
            if (Relacao.ServicoRA.TemBarras(db, tr)) Relacao.ServicoRA.Atualizar(db, tr, cfg);
            tr.Commit();
        }
        ed?.WriteMessage($"\nDesenho em {Nome(desenho)}; medidas escritas em {Nome(apresentacao)}. Descrições, cotas e tabelas atualizadas.");
        if (mudouDesenho)
            ed?.WriteMessage("\nAtenção: a geometria NÃO foi convertida. A unidade do desenho tem de ser a que ele já usa " +
                             "(as barras existentes passam a ser medidas na unidade nova).");
    }

    private static T? Perguntar<T>(Editor ed, string msg, (string Palavra, T Valor)[] opcoes, T atual) where T : struct
    {
        var op = new PromptKeywordOptions(msg + " [" + string.Join("/", Array.ConvertAll(opcoes, o => o.Palavra)) + "]", string.Join(" ", Array.ConvertAll(opcoes, o => o.Palavra)))
        { AllowNone = true };
        foreach (var o in opcoes) if (EqualityComparer<T>.Default.Equals(o.Valor, atual)) op.Keywords.Default = o.Palavra;
        var r = ed.GetKeywords(op);
        if (r.Status != PromptStatus.OK) return null;
        foreach (var o in opcoes) if (string.Equals(o.Palavra, r.StringResult, StringComparison.OrdinalIgnoreCase)) return o.Valor;
        return null;
    }
}
