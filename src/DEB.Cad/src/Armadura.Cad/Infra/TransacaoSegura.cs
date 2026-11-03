using System;

namespace Armadura.Cad.Infra;

/// <summary>
/// Transação que faz Commit ao sair do bloco (inclusive em return/continue). No GstarCAD 2021,
/// descartar uma transação sem Commit desliga as notificações do comando seguinte; use esta
/// classe nos laços de prompts. Para desfazer de propósito, chame <see cref="Abortar"/>.
/// </summary>
internal sealed class TransacaoSegura : IDisposable
{
    private readonly Transaction _tr;
    private bool _fim;

    public TransacaoSegura(Database db) { _tr = db.TransactionManager.StartTransaction(); }

    public static implicit operator Transaction(TransacaoSegura t) => t._tr;

    public DBObject GetObject(ObjectId id, OpenMode modo) => _tr.GetObject(id, modo);

    public void AddNewlyCreatedDBObject(DBObject o, bool add) => _tr.AddNewlyCreatedDBObject(o, add);

    public void Commit() { if (_fim) return; _fim = true; _tr.Commit(); }

    public void Abortar() { if (_fim) return; _fim = true; _tr.Abort(); }

    public void Dispose()
    {
        try { Commit(); }
        catch (Exception ex) { Log.Erro("TransacaoSegura.Commit", ex); }
        _tr.Dispose();
    }
}
