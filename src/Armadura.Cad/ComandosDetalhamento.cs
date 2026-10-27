using System;
using System.Collections.Generic;
using System.Linq;
using Armadura.Cad.Infra;
using Armadura.Cad.Relacao;
using Armadura.Core.Modelo;
using Armadura.Core.RelacaoAco;

[assembly: Rt.CommandClass(typeof(Armadura.Cad.ComandosDetalhamento))]

namespace Armadura.Cad;

/// <summary>Detalhamentos (blocos) e relação do aço parcial/bloqueio (seções 3.5 e 3.6).</summary>
public sealed class ComandosDetalhamento
{
    private const Rt.CommandFlags Flags = Rt.CommandFlags.Modal | Rt.CommandFlags.UsePickSet;

    /// <summary>ARMDETCRIAR: seleção → nome → bloco (detalhamento) no mesmo lugar.</summary>
    [Rt.CommandMethod("ARMDETCRIAR", Flags)]
    public void Criar() => Comandos.Executar("ARMDETCRIAR", (doc, ed, db, cfg) =>
    {
        var sel = ed.GetSelection(new PromptSelectionOptions { MessageForAdding = "\nSelecione o que faz parte do detalhamento:" });
        if (sel.Status != PromptStatus.OK) return;
        string nome;
        while (true)
        {
            var rn = ed.GetString(new PromptStringOptions("\nNome do detalhamento (ex.: V12):") { AllowSpaces = true });
            if (rn.Status != PromptStatus.OK) return;
            nome = rn.StringResult.Trim();
            string erro;
            using (var tr = new TransacaoSegura(db)) erro = ServicoDetalhamento.ValidarNome(db, tr, nome);
            if (erro == null) break;
            ed.WriteMessage("\n" + erro);
        }
        var rg = ed.GetString(new PromptStringOptions($"\nGrupo <{ServicoDetalhamento.GrupoSugerido(nome)}>:")
            { AllowSpaces = true, DefaultValue = ServicoDetalhamento.GrupoSugerido(nome), UseDefaultValue = true });
        if (rg.Status != PromptStatus.OK) return;
        var rp = ed.GetString(new PromptStringOptions("\nPavimento (Enter = nenhum):") { AllowSpaces = true });
        if (rp.Status != PromptStatus.OK) return;

        using var t = new TransacaoSegura(db);
        var dados = new DadosDetalhamento { Grupo = rg.StringResult.Trim(), Pavimento = rp.StringResult.Trim() };
        ServicoDetalhamento.Criar(db, t, sel.Value.GetObjectIds(), nome, dados);
        ServicoRA.Atualizar(db, t, cfg);
        t.Commit();
        ed.WriteMessage($"\nDetalhamento \"{nome}\" criado. Edite o conteúdo com BEDIT; as propriedades ficam na paleta (ARMPALETA) e em ARMDETGERENCIAR.");
    });

    /// <summary>ARMDETGERENCIAR: grade editável com todos os detalhamentos.</summary>
    [Rt.CommandMethod("ARMDETGERENCIAR")]
    public void Gerenciar() => Comandos.Executar("ARMDETGERENCIAR", (doc, ed, db, cfg) =>
    {
        List<Paleta.LinhaDetalhamento> linhas;
        using (var tr = new TransacaoSegura(db))
            linhas = ServicoDetalhamento.Todos(db, tr).Select(x => new Paleta.LinhaDetalhamento(x.btr.ObjectId, x.btr.Name, x.dados,
                ServicoDetalhamento.Insercoes(tr, x.btr))).ToList();
        if (linhas.Count == 0) { ed.WriteMessage("\nNenhum detalhamento no desenho (crie com ARMDETCRIAR)."); return; }
        using (var f = new Paleta.FormDetalhamentos(linhas))
            if (CadApp.ShowModalDialog(f) != System.Windows.Forms.DialogResult.OK) return;

        using var t = new TransacaoSegura(db);
        var erros = new List<string>();
        foreach (var l in linhas.Where(x => x.Alterado))
        {
            var btr = (BlockTableRecord)t.GetObject(l.Id, OpenMode.ForWrite);
            if (!string.Equals(btr.Name, l.Nome, StringComparison.Ordinal))
            {
                var erro = ServicoDetalhamento.ValidarNome(db, t, l.Nome, l.Id);
                if (erro != null) erros.Add($"{btr.Name}: {erro}");
                else Renomear(db, t, btr, l.Nome);
            }
            ServicoDetalhamento.Gravar(db, t, btr, l.Dados);
        }
        ServicoRA.Atualizar(db, t, cfg);
        t.Commit();
        ed.WriteMessage(erros.Count == 0 ? "\nDetalhamentos atualizados." : "\n" + string.Join("\n", erros));
    });

    /// <summary>Renomeia o bloco e acerta as tabelas parciais que o listavam pelo nome antigo.</summary>
    internal static void Renomear(Database db, Transaction tr, BlockTableRecord btr, string novo)
    {
        string antigo = btr.Name;
        if (!btr.IsWriteEnabled) btr.UpgradeOpen();
        btr.Name = novo;
        var cfg = ConfigDesenho.Obter(db);
        foreach (var tid in Desenho.Entidades<Table>(db, tr, incluirBlocos: true).ToList())
        {
            var t = (Table)tr.GetObject(tid, OpenMode.ForRead);
            var op = ServicoRA.Opcoes(t, cfg);
            if (op == null) continue;
            int i = op.Blocos.FindIndex(b => string.Equals(b, antigo, StringComparison.OrdinalIgnoreCase));
            if (i < 0) continue;
            op.Blocos[i] = novo;
            ServicoRA.GravarOpcoes(t, op);
        }
    }

    private static List<string> SelecionarBlocos(Editor ed, Database db, string msg)
    {
        var filtro = new SelectionFilter(new[] { new TypedValue((int)DxfCode.Start, "INSERT") });
        var r = ed.GetSelection(new PromptSelectionOptions { MessageForAdding = msg }, filtro);
        if (r.Status != PromptStatus.OK) return new List<string>();
        using var tr = new TransacaoSegura(db);
        return r.Value.GetObjectIds()
            .Select(id => tr.GetObject(id, OpenMode.ForRead)).OfType<BlockReference>().Where(b => !(b is Table))
            .Select(b => ServicoRA.NomeBloco(tr, b)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>ARMRAPARCIAL: blocos (Enter = desenho inteiro) → ponto → tabela só com eles.</summary>
    [Rt.CommandMethod("ARMRAPARCIAL", Flags)]
    public void Parcial() => Comandos.Executar("ARMRAPARCIAL", (doc, ed, db, cfg) =>
    {
        var blocos = SelecionarBlocos(ed, db, "\nSelecione os detalhamentos (blocos) da relação (Enter = desenho inteiro):");
        var rp = ed.GetPoint("\nCanto superior esquerdo da relação do aço:");
        if (rp.Status != PromptStatus.OK) return;
        using var tr = new TransacaoSegura(db);
        var op = OpcoesTabelaRA.Padrao(cfg);
        var id = ServicoRA.Inserir(db, tr, rp.Value.TransformBy(ed.CurrentUserCoordinateSystem), cfg, op);
        int outras = blocos.Count > 0 ? ServicoRA.Associar(db, tr, id, blocos, cfg) : 0;
        tr.Commit();
        ed.WriteMessage(blocos.Count == 0
            ? "\nRelação do desenho inteiro inserida."
            : $"\nRelação parcial: {string.Join(", ", blocos)}." + (outras > 0 ? $" Removidos de {outras} outra(s) relação(ões) parcial(is)." : ""));
    });

    /// <summary>ARMRAASSOCIAR: blocos → tabela: os blocos passam a pertencer só a essa relação.</summary>
    [Rt.CommandMethod("ARMRAASSOCIAR", Flags)]
    public void Associar() => Comandos.Executar("ARMRAASSOCIAR", (doc, ed, db, cfg) =>
    {
        var blocos = SelecionarBlocos(ed, db, "\nSelecione os detalhamentos (blocos):");
        if (blocos.Count == 0) return;
        var pe = new PromptEntityOptions("\nSelecione a relação do aço (tabela):");
        pe.SetRejectMessage("\nSelecione uma tabela.");
        pe.AddAllowedClass(typeof(Table), true);
        var r = ed.GetEntity(pe);
        if (r.Status != PromptStatus.OK) return;
        using var tr = new TransacaoSegura(db);
        if (ServicoRA.Opcoes((Entity)tr.GetObject(r.ObjectId, OpenMode.ForRead), cfg) == null)
        {
            ed.WriteMessage("\nEssa tabela não é uma relação do aço do plugin.");
            return;
        }
        int outras = ServicoRA.Associar(db, tr, r.ObjectId, blocos, cfg);
        tr.Commit();
        ed.WriteMessage($"\n{string.Join(", ", blocos)} associado(s) à relação." + (outras > 0 ? $" Removido(s) de {outras} outra(s)." : ""));
    });

    /// <summary>ARMRABLOQUEAR: liga/desliga o bloqueio da relação do aço (controle de revisões).</summary>
    [Rt.CommandMethod("ARMRABLOQUEAR")]
    public void Bloquear() => Comandos.Executar("ARMRABLOQUEAR", (doc, ed, db, cfg) =>
    {
        using var tr = new TransacaoSegura(db);
        bool travada = ServicoRA.AlternarBloqueio(db, tr, cfg);
        tr.Commit();
        ed.WriteMessage(travada
            ? "\nRelação do aço BLOQUEADA: os números atuais ficam fixos; barras novas ou alteradas recebem números acima do maior e aparecem com * na tabela."
            : "\nRelação do aço DESBLOQUEADA: tudo foi renumerado e os destaques removidos.");
    });
}
