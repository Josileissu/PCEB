using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using Armadura.Cad.Barra;
using Armadura.Cad.Infra;
using Armadura.Cad.Relacao;
using Armadura.Core.Modelo;

[assembly: Rt.CommandClass(typeof(Armadura.Cad.Testes.Desempenho))]

namespace Armadura.Cad.Testes;

/// <summary>
/// Medição de desempenho (checagem de 2026-10-30): desenha N barras em "U" com faixa e mede criar, renumerar/atualizar a RA
/// (com e sem tabela), atualizar de novo sem mudança e o processamento do Sincronizador depois de um MOVE (pelo log).
/// Uso: script com NETLOAD + ARMTESTEDESEMPENHO; N em ARM_DESEMPENHO_N (padrão 500). Resultado em logs\desempenho.txt.
/// </summary>
public sealed class Desempenho
{
    private static readonly StringBuilder Rel = new StringBuilder();

    private static void Medir(string nome, Action acao)
    {
        var sw = Stopwatch.StartNew();
        acao();
        Rel.AppendLine($"{nome}: {sw.Elapsed.TotalMilliseconds:0} ms");
        Log.Info($"desempenho: {nome}: {sw.Elapsed.TotalMilliseconds:0} ms");
    }

    [Rt.CommandMethod("ARMTESTEDESEMPENHO")]
    public void Executar() => Comandos.Executar("ARMTESTEDESEMPENHO", (doc, ed, db, cfg) =>
    {
        int n = int.TryParse(Environment.GetEnvironmentVariable("ARM_DESEMPENHO_N"), out var x) && x > 0 ? x : 500;
        Rel.Clear();
        Rel.AppendLine($"Desempenho {DateTime.Now:yyyy-MM-dd HH:mm:ss} — {n} barras em U com faixa (ARMADURA {Plugin.Versao})");
        Medir($"criar {n} barras com faixa", () =>
        {
            using var tr = db.TransactionManager.StartTransaction();
            var esp = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
            for (int i = 0; i < n; i++)
            {
                double x0 = (i % 25) * 300, y0 = (i / 25) * 400;
                var pl = new Polyline();
                pl.AddVertexAt(0, new Point2d(x0, y0 + 200), 0, 0, 0);
                pl.AddVertexAt(1, new Point2d(x0, y0), 0, 0, 0);
                pl.AddVertexAt(2, new Point2d(x0 + 150 + (i % 7) * 10, y0), 0, 0, 0);
                pl.AddVertexAt(3, new Point2d(x0 + 150 + (i % 7) * 10, y0 + 200), 0, 0, 0);
                esp.AppendEntity(pl);
                tr.AddNewlyCreatedDBObject(pl, true);
                ServicoBarra.Criar(tr, pl, new DadosArmadura { Bitola = (i % 3) switch { 0 => 10, 1 => 12.5, _ => 16 }, Quantidade = 1 }, cfg);
                var f = new DadosFaixa { Tipo = TipoFaixa.Ortogonal, Espacamento = 15, P1X = x0 + 20, P1Y = y0 + 300, P2X = x0 + 170, P2Y = y0 + 300 };
                ServicoFaixa.Criar(tr, pl, f, new Point3d(x0 + 90, y0 + 320, 0), cfg);
            }
            tr.Commit();
        });
        Medir("relação do aço: 1ª atualização (numera tudo)", () =>
        {
            using var tr = db.TransactionManager.StartTransaction();
            ServicoRA.Atualizar(db, tr, cfg);
            tr.Commit();
        });
        Medir("relação do aço: atualizar sem mudança", () =>
        {
            using var tr = db.TransactionManager.StartTransaction();
            ServicoRA.Atualizar(db, tr, cfg);
            tr.Commit();
        });
        Medir("inserir tabela da RA", () =>
        {
            using var tr = db.TransactionManager.StartTransaction();
            ServicoRA.Inserir(db, tr, new Point3d(-1500, 0, 0), cfg);
            tr.Commit();
        });
        Medir("relação do aço: atualizar com tabela", () =>
        {
            using var tr = db.TransactionManager.StartTransaction();
            ServicoRA.Atualizar(db, tr, cfg);
            tr.Commit();
        });
        Gravar();
        // MOVE de tudo e REGEN pelo roteiro: o tempo do Sincronizador sai no log ("Sincronizador: … ms")
        doc.SendStringToExecute("ARMTESTEMARCA\n_.MOVE _ALL\n\n0,0\n10,0\nARMTESTEMARCA\n_.REGEN\nARMTESTEMARCA\nARMTESTEDESEMPENHOFIM\n", true, false, true);
    });

    [Rt.CommandMethod("ARMTESTEDESEMPENHOFIM", Rt.CommandFlags.Session)]
    public void Fim()
    {
        Gravar();
        foreach (Document d in System.Linq.Enumerable.ToList(System.Linq.Enumerable.Cast<Document>(CadApp.DocumentManager))) d.CloseAndDiscard();
        CadApp.Quit();
    }

    private static void Gravar() => File.WriteAllText(Path.Combine(Log.PastaLogs, "desempenho.txt"), Rel.ToString(), Encoding.UTF8);
}
