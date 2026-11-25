using System;
using System.Collections.Generic;
using System.Linq;
using DEB.Cad.Infra;
using DEB.Core.Analise.Objetos;
using DEB.Core.Configuracao;
using DEB.Core.Elementos.Viga.PreMoldada;
using DEB.Core.Elementos.Viga.PreMoldada.Calculo;
using DEB.Core.Motor;

[assembly: Rt.CommandClass(typeof(DEB.Cad.ComandosAnaliseViga))]

namespace DEB.Cad;

/// <summary>
/// ARMVIGAANALISE (D51, 25/11/2026: análise e detalhamento da viga pré-moldada nativos no CAD). Desenha a análise e, abaixo dela,
/// a VIGA ARMADA pelo cálculo (etapa 4: longitudinais, negativas, estribos, aba, dente calculado — o tipo novo do dente vai para o
/// catálogo do ARMDENTES); a cada recálculo os dois são refeitos no mesmo lugar. Sem seleção (Enter / "Nova"):
/// escolhe a viga na janela dos templates (modelo salvo ou valores digitados) e desenha a análise — modelo, seções, tabelas de
/// carga e de parâmetros, envoltória de Vd e Md, flechas — no ponto dado. Selecionando uma tabela de uma análise já desenhada:
/// lê as tabelas (o engenheiro digita as cargas e os parâmetros direto nelas), recalcula e redesenha no mesmo lugar.
/// Cada entidade da análise leva o XData "PCEB_ANALISE" com a id da análise; a viga (valores do template), a origem e a escala
/// ficam num Xrecord do dicionário "PCEB_ANALISE_VIGA". O cálculo e o desenho saem do núcleo (AnaliseViga, DesenhoAnaliseViga).
/// </summary>
public sealed class ComandosAnaliseViga
{
    internal const string App = "PCEB_ANALISE", Dicionario = "PCEB_ANALISE_VIGA";
    private const double EscalaPadrao = 50;

    [Rt.CommandMethod("ARMVIGAANALISE", Rt.CommandFlags.UsePickSet)]
    public void ArmVigaAnalise() => Comandos.Executar("ARMVIGAANALISE", (doc, ed, db, cfg) =>
    {
        var op = new PromptEntityOptions("\nSelecione o detalhamento da viga ou uma tabela da análise, ou [Nova] <Nova>: ") { AllowNone = true };
        op.Keywords.Add("Nova");
        var sel = ed.GetEntity(op);
        if (sel.Status == PromptStatus.OK)
        {
            string id = IdDe(db, sel.ObjectId);
            if (id != null) { Recalcular(ed, db, cfg, id); return; }
            // um detalhamento de viga (bloco): a análise parte dos valores guardados nele (ou do modelo salvo com o nome dele)
            var v0 = ValoresDoDetalhamento(db, sel.ObjectId, out string nomeBloco);
            if (v0 == null)
            {
                ed.WriteMessage(nomeBloco == null ? "\nO objeto não é um detalhamento de viga nem uma análise (ARMVIGAANALISE)."
                    : $"\nO detalhamento \"{nomeBloco}\" não guarda os valores da viga e não há modelo salvo com esse nome: use Nova e escolha os valores.");
                return;
            }
            var ptA = ed.GetPoint("\nPonto de inserção da análise (ponta esquerda do modelo; a viga armada é refeita abaixo dela):");
            if (ptA.Status != PromptStatus.OK) return;
            AnalisarDetalhamento(ed, db, cfg, sel.ObjectId, v0, ptA.Value.TransformBy(ed.CurrentUserCoordinateSystem));
            ed.WriteMessage("\nDigite as cargas nas tabelas e rode ARMVIGAANALISE de novo; os parâmetros da viga ficam na paleta Armadura, aba \"Viga\".");
            return;
        }
        if (sel.Status != PromptStatus.None && sel.Status != PromptStatus.Keyword) return;

        using var f = new FormTemplate();
        f.Selecionar(new TemplateVigaPreMoldada().Id);
        if (CadApp.ShowModalDialog(f) != System.Windows.Forms.DialogResult.OK || f.Escolhido == null) return;
        if (f.Escolhido.Id != new TemplateVigaPreMoldada().Id) { ed.WriteMessage("\nA análise é da viga pré-moldada: escolha o template \"Viga pré-moldada\"."); return; }
        var pt = ed.GetPoint("\nPonto de inserção da análise (ponta esquerda do modelo):");
        if (pt.Status != PromptStatus.OK) return;
        string nova = Guid.NewGuid().ToString("N");
        Desenhar(ed, db, cfg, nova, f.Valores, new EntradaAnaliseViga(), pt.Value.TransformBy(ed.CurrentUserCoordinateSystem), EscalaPadrao);
        ed.WriteMessage("\nDigite as cargas e os parâmetros nas tabelas e rode ARMVIGAANALISE de novo selecionando uma delas.");
    });

    /// <summary>Lê as tabelas da análise <paramref name="id"/>, apaga o desenho dela e desenha de novo com a entrada lida.</summary>
    internal static void Recalcular(Editor ed, Database db, ConfiguracaoArmadura cfg, string id)
    {
        var dados = LerRegistro(db, id);
        if (dados == null) { ed?.WriteMessage("\nOs dados desta análise não estão no desenho (dicionário PCEB_ANALISE_VIGA)."); return; }
        var tabelas = new List<(string, List<string[]>)>();
        var ids = Entidades(db, id);
        using (var tr = db.TransactionManager.StartTransaction())
        {
            foreach (var oid in ids)
                if (tr.GetObject(oid, OpenMode.ForRead) is Table t)
                {
                    var linhas = new List<string[]>();
                    for (int l = 1; l < t.Rows.Count; l++)
                        linhas.Add(Enumerable.Range(0, t.Columns.Count).Select(c => TabelasAnaliseViga.TextoCelula(t.Cells[l, c].TextString)).ToArray());
                    tabelas.Add((TabelasAnaliseViga.TextoCelula(t.Cells[0, 0].TextString), linhas));
                }
            tr.Commit();
        }
        var erros = new List<string>();
        var ent = TabelasAnaliseViga.Ler(tabelas, erros);
        foreach (var e in erros) ed?.WriteMessage("\nTabela: " + e);
        Apagar(db, ids);
        Desenhar(ed, db, cfg, id, dados.Value.Valores, ent, dados.Value.Origem, dados.Value.Escala);
    }

    /// <summary>Roda a análise, desenha, marca as entidades e grava o registro (viga, origem, escala).</summary>
    internal static List<ObjectId> Desenhar(Editor ed, Database db, ConfiguracaoArmadura cfg, string id, ValoresTemplate v, EntradaAnaliseViga ent,
        Point3d origem, double escala)
    {
        ResultadoAnaliseViga r;
        try { r = AnaliseViga.Rodar(v, ent); }
        catch (ArgumentException ex) { ed?.WriteMessage("\nAnálise: " + ex.Message); return new List<ObjectId>(); }
        // o dimensionamento sai da mesma análise: a memória (no espaço reservado) e a viga armada vêm do mesmo resultado (D26)
        ResultadoCalculoViga calc = null;
        try
        {
            var cat = TemplateVigaPreMoldada.CatalogoDentes;
            calc = CalculoViga.Calcular(v, AnaliseViga.CargasParaCalculo(ent, v, cat), null, cat, r);
        }
        catch (Exception ex) { Log.Erro("ARMVIGAANALISE cálculo", ex); ed?.WriteMessage("\nDimensionamento: " + ex.Message); }
        var des = DesenhoAnaliseViga.Gerar(r, ent, v, UnidadesAnalise.DoCad(cfg), null, escala, cfg.AlturaTextoMm, calc);
        List<ObjectId> ids;
        using (var tr = db.TransactionManager.StartTransaction())
        {
            ids = new List<ObjectId>();
            ComandosTemplate.Desenhar(tr, db, cfg, des, origem, atualizarRA: false, entidades: ids);
            GarantirRegApp(db, tr);
            foreach (var oid in ids)
            {
                var e = (Entity)tr.GetObject(oid, OpenMode.ForWrite);
                using var rb = new ResultBuffer(new TypedValue((int)DxfCode.ExtendedDataRegAppName, App), new TypedValue((int)DxfCode.ExtendedDataAsciiString, id));
                e.XData = rb;
            }
            GravarRegistro(db, tr, id, v, origem, escala);
            tr.Commit();
        }
        // ---- a viga armada pelo cálculo, abaixo da análise (D51, etapa 4: "a viga vai ser atualizada/desenhada/detalhada/corrigida")
        try
        {
            double minY = des.Contornos.SelectMany(c => c.Vertices).Select(q => q.Y).Concat(des.Textos.Select(t => t.Y)).DefaultIfEmpty(0).Min();
            double cpu = Desenho.CmPorUnidade(db, cfg);
            var pt = new Point3d(origem.X, origem.Y + (minY - 4 * escala) / cpu, origem.Z);
            double fundo = calc != null ? ArmarViga(ed, db, cfg, id, v, calc, pt) : pt.Y;
            // memória de cálculo ABAIXO da viga armada (pedido do usuário, 25/11/2026), marcada com a id da análise
            var mem = DesenhoAnaliseViga.Memoria(r, ent, calc, v, UnidadesAnalise.DoCad(cfg), escala, cfg.AlturaTextoMm);
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var idsMem = new List<ObjectId>();
                ComandosTemplate.Desenhar(tr, db, cfg, mem, new Point3d(origem.X, fundo - 6 * escala / cpu, origem.Z), atualizarRA: false, entidades: idsMem);
                foreach (var oid in idsMem)
                {
                    var e = (Entity)tr.GetObject(oid, OpenMode.ForWrite);
                    using var rb = new ResultBuffer(new TypedValue((int)DxfCode.ExtendedDataRegAppName, App), new TypedValue((int)DxfCode.ExtendedDataAsciiString, id));
                    e.XData = rb;
                }
                tr.Commit();
            }
        }
        catch (Exception ex)
        {
            Log.Erro("ARMVIGAANALISE detalhamento", ex);
            ed?.WriteMessage("\nNão foi possível desenhar a viga armada: " + ex.Message);
        }
        if (ed != null)
        {
            var u = UnidadesAnalise.DoCad(cfg);
            ed.WriteMessage($"\nAnálise da viga {v["Nome"]}: Md,máx = {u.Texto("kN·m", r.MdMax)}; Md,mín = {u.Texto("kN·m", r.MdMin)}; " +
                            $"Vd,máx = {u.Texto("kN", r.VdMax)} ({string.Join(" + ", r.Modelos.Select(m => m.Nome))}).");
            foreach (var a in des.Avisos) ed.WriteMessage("\nAviso: " + a);
        }
        return ids;
    }

    /// <summary>
    /// Calcula a viga com a envoltória da análise, grava os dentes calculados no catálogo do usuário (ARMDENTES; mesmo nome =
    /// substitui) e desenha o detalhamento da proposta em <paramref name="pt"/>; o bloco do detalhamento leva a id da análise. O de
    /// um desenho anterior da mesma análise já foi apagado com as outras entidades dela (e a definição do bloco sem inserções sai).
    /// </summary>
    /// <returns>O y (unidades do desenho) do fundo da viga armada desenhada, para a memória ir abaixo dela.</returns>
    internal static double ArmarViga(Editor ed, Database db, ConfiguracaoArmadura cfg, string id, ValoresTemplate v, ResultadoCalculoViga calc, Point3d pt)
    {
        double fundo = pt.Y;
        var cat = TemplateVigaPreMoldada.CatalogoDentes;
        if (calc.DentesPropostos.Count > 0)
        {
            // o catálogo do usuário + os tipos que a viga usa (se faltarem) + os calculados
            var baseCat = Plugin.Config.Dentes.Concat(cat.Where(t => Plugin.Config.Dentes.All(u => u.Nome != t.Nome))).ToList();
            var novo = calc.CatalogoComPropostos(baseCat);
            Plugin.Config.Dentes = novo;
            TemplateVigaPreMoldada.CatalogoDentes = novo;
            Plugin.SalvarConfig();
            ed?.WriteMessage($"\nCatálogo de dentes (ARMDENTES): {string.Join(", ", calc.DentesPropostos.Select(t => t.Nome))} gravado(s).");
        }
        var res = new TemplateVigaPreMoldada().Gerar(calc.Proposta, cfg);
        res.PosicaoRA = null;   // a relação do aço das tabelas que já existem é atualizada; nenhuma tabela nova por recálculo
        using (var tr = db.TransactionManager.StartTransaction())
        {
            ComandosTemplate.Desenhar(tr, db, cfg, res, pt, atualizarRA: true, template: new TemplateVigaPreMoldada(), valores: calc.Proposta);
            tr.Commit();
        }
        // a referência nova do bloco só aparece em GetBlockReferenceIds depois do commit (armadilha do AGENTS.md): marca noutra transação
        using (var tr = db.TransactionManager.StartTransaction())
        {
            GarantirRegApp(db, tr);
            if (!string.IsNullOrEmpty(res.Detalhamento))
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                if (bt.Has(res.Detalhamento))
                {
                    var def = (BlockTableRecord)tr.GetObject(bt[res.Detalhamento], OpenMode.ForRead);
                    foreach (ObjectId rid in def.GetBlockReferenceIds(true, false))
                    {
                        if (rid.IsErased) continue;
                        var br = (Entity)tr.GetObject(rid, OpenMode.ForWrite);
                        try { fundo = Math.Min(fundo, br.GeometricExtents.MinPoint.Y); } catch (Exception) { }
                        using var rb = new ResultBuffer(new TypedValue((int)DxfCode.ExtendedDataRegAppName, App), new TypedValue((int)DxfCode.ExtendedDataAsciiString, id));
                        br.XData = rb;
                    }
                }
            }
            tr.Commit();
        }
        ed?.WriteMessage($"\nViga armada: {string.Join("; ", calc.ResumoProposta())}.");
        foreach (var a in calc.Avisos.Concat(res.Avisos).Distinct()) ed?.WriteMessage("\nAviso: " + a);
        int conferir = calc.Verificacoes.Count(x => x.Situacao != Core.Normas.Verificacao.SituacaoVerificacao.Ok);
        if (conferir > 0) ed?.WriteMessage($"\n{conferir} verificação(ões) para conferir ou que não atendem (memória de cálculo, abaixo da viga).");
        return fundo;
    }

    /// <summary>
    /// Valores de viga pré-moldada de um detalhamento selecionado: os guardados no bloco (ARMTEMPLATE, importação e ARMVIGAANALISE a
    /// partir da 0.1.50) ou, nos antigos, o modelo salvo com o nome do bloco. Null se não é viga ou não há valores.
    /// </summary>
    internal static ValoresTemplate ValoresDoDetalhamento(Database db, ObjectId oid, out string nomeBloco)
    {
        nomeBloco = null;
        using var tr = db.TransactionManager.StartTransaction();
        try
        {
            if (!(tr.GetObject(oid, OpenMode.ForRead) is BlockReference br)) return null;
            var btr = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);
            nomeBloco = btr.Name;
            var g = ValoresNoBloco.Ler(tr, btr);
            var t = new TemplateVigaPreMoldada();
            if (g != null) return g.Value.TemplateId == t.Id ? g.Value.Valores : null;
            var arq = ModelosTemplates.Obter(t);
            return arq.Obter(btr.Name) != null ? arq.Valores(t, btr.Name) : null;
        }
        finally { tr.Commit(); }
    }

    /// <summary>A análise a partir de um detalhamento: o bloco sai (a viga armada é refeita abaixo da análise) e a análise entra em <paramref name="pt"/>.</summary>
    internal static string AnalisarDetalhamento(Editor ed, Database db, ConfiguracaoArmadura cfg, ObjectId bloco, ValoresTemplate v, Point3d pt)
    {
        string id = Guid.NewGuid().ToString("N");
        Apagar(db, new[] { bloco });
        Desenhar(ed, db, cfg, id, v, new EntradaAnaliseViga(), pt, EscalaPadrao);
        return id;
    }

    /// <summary>A viga do objeto selecionado para a paleta: (id da análise ou bloco do detalhamento, valores); null se não é viga.</summary>
    internal static (string Analise, ObjectId Bloco, ValoresTemplate Valores)? VigaDoObjeto(Database db, ObjectId oid)
    {
        string id = IdDe(db, oid);
        if (id != null) { var reg = LerRegistro(db, id); return reg == null ? ((string, ObjectId, ValoresTemplate)?)null : (id, ObjectId.Null, reg.Value.Valores); }
        var v = ValoresDoDetalhamento(db, oid, out _);
        return v == null ? ((string, ObjectId, ValoresTemplate)?)null : (null, oid, v);
    }

    /// <summary>
    /// Atualiza a viga com valores novos (paleta "Viga"): numa análise, grava os valores e recalcula tudo no lugar (análise, viga
    /// armada, memória); num detalhamento solto, redesenha o detalhamento no lugar dele (a origem acompanha o bloco, se foi movido).
    /// </summary>
    internal static void AtualizarViga(Editor ed, Database db, ConfiguracaoArmadura cfg, string analise, ObjectId bloco, ValoresTemplate v)
    {
        var erros = CatalogoTemplates.Validar(new TemplateVigaPreMoldada(), v);
        if (erros.Count > 0) { ed?.WriteMessage("\nValores inválidos: " + string.Join(" ", erros)); return; }
        if (analise != null)
        {
            var reg = LerRegistro(db, analise);
            if (reg == null) { ed?.WriteMessage("\nA análise não está mais no desenho."); return; }
            using (var tr = db.TransactionManager.StartTransaction())
            {
                GravarRegistro(db, tr, analise, v, reg.Value.Origem, reg.Value.Escala);
                tr.Commit();
            }
            Recalcular(ed, db, cfg, analise);
            return;
        }
        if (bloco.IsNull || bloco.IsErased) { ed?.WriteMessage("\nO detalhamento não está mais no desenho."); return; }
        Point3d origem;
        using (var tr = db.TransactionManager.StartTransaction())
        {
            var br = (BlockReference)tr.GetObject(bloco, OpenMode.ForRead);
            var g = ValoresNoBloco.Ler(tr, (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead));
            origem = br.Position - (g?.Deslocamento ?? new Vector3d());
            tr.Commit();
        }
        Apagar(db, new[] { bloco });
        var t = new TemplateVigaPreMoldada();
        var res = t.Gerar(v, cfg);
        res.PosicaoRA = null;
        using (var tr = db.TransactionManager.StartTransaction())
        {
            ComandosTemplate.Desenhar(tr, db, cfg, res, origem, atualizarRA: true, template: t, valores: v);
            tr.Commit();
        }
        ed?.WriteMessage($"\nDetalhamento \"{res.Detalhamento}\" atualizado.");
        foreach (var a in res.Avisos) ed?.WriteMessage("\nAviso: " + a);
    }

    /// <summary>ARMVIGAATUALIZAR: aplica os valores editados na paleta "Viga" (o botão "Atualizar viga" chama este comando).</summary>
    [Rt.CommandMethod("ARMVIGAATUALIZAR")]
    public void ArmVigaAtualizar() => Comandos.Executar("ARMVIGAATUALIZAR", (doc, ed, db, cfg) =>
    {
        var p = Paleta.PaletaViga.Pendente;
        Paleta.PaletaViga.Pendente = null;
        if (p == null) { ed.WriteMessage("\nNada para atualizar: edite os parâmetros na paleta Armadura, aba \"Viga\"."); return; }
        AtualizarViga(ed, db, cfg, p.Value.Analise, p.Value.Bloco, p.Value.Valores);
    });

    // ------------------------------------------------------------------ marcação e registro no desenho

    internal static string IdDe(Database db, ObjectId oid)
    {
        using var tr = db.TransactionManager.StartTransaction();
        var o = tr.GetObject(oid, OpenMode.ForRead);
        using var rb = o.GetXDataForApplication(App);
        tr.Commit();
        return rb?.AsArray().Where(tv => tv.TypeCode == (short)DxfCode.ExtendedDataAsciiString).Select(tv => tv.Value as string).FirstOrDefault();
    }

    /// <summary>Entidades da análise <paramref name="id"/> no espaço do modelo e nos layouts.</summary>
    internal static List<ObjectId> Entidades(Database db, string id)
    {
        var l = new List<ObjectId>();
        using var tr = db.TransactionManager.StartTransaction();
        var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
        foreach (ObjectId btrId in bt)
        {
            var btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
            if (!btr.IsLayout) continue;
            foreach (ObjectId oid in btr)
            {
                var o = tr.GetObject(oid, OpenMode.ForRead);
                using var rb = o.GetXDataForApplication(App);
                if (rb != null && rb.AsArray().Any(tv => tv.TypeCode == (short)DxfCode.ExtendedDataAsciiString && (tv.Value as string) == id)) l.Add(oid);
            }
        }
        tr.Commit();
        return l;
    }

    private static void Apagar(Database db, IEnumerable<ObjectId> ids)
    {
        using var tr = db.TransactionManager.StartTransaction();
        var defs = new List<ObjectId>();
        foreach (var oid in ids)
        {
            if (oid.IsErased) continue;
            var o = tr.GetObject(oid, OpenMode.ForWrite);
            if (o is BlockReference br) defs.Add(br.BlockTableRecord);
            o.Erase();
        }
        // o detalhamento refeito volta com o mesmo nome: a definição antiga sai se ninguém mais a usa
        foreach (var d in defs.Distinct())
        {
            if (d.IsNull || d.IsErased) continue;
            var btr = (BlockTableRecord)tr.GetObject(d, OpenMode.ForRead, true);
            if (btr.IsLayout || btr.IsErased) continue;
            // inserções que restam (as desta análise já foram apagadas acima)
            bool usada = btr.GetBlockReferenceIds(true, false).Cast<ObjectId>().Any(rid => !rid.IsErased);
            if (!usada) { btr.UpgradeOpen(); btr.Erase(); }
        }
        tr.Commit();
    }

    private static void GarantirRegApp(Database db, Transaction tr)
    {
        var t = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
        if (t.Has(App)) return;
        t.UpgradeOpen();
        var r = new RegAppTableRecord { Name = App };
        t.Add(r);
        tr.AddNewlyCreatedDBObject(r, true);
    }

    // Xrecord da análise: 10 = origem, 40 = escala; depois, para cada parâmetro do template, 1 = nome e 3 = valor em pedaços de 200
    internal static void GravarRegistro(Database db, Transaction tr, string id, ValoresTemplate v, Point3d origem, double escala)
    {
        var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForWrite);
        DBDictionary dic;
        if (nod.Contains(Dicionario)) dic = (DBDictionary)tr.GetObject(nod.GetAt(Dicionario), OpenMode.ForWrite);
        else { dic = new DBDictionary(); nod.SetAt(Dicionario, dic); tr.AddNewlyCreatedDBObject(dic, true); }
        var tvs = new List<TypedValue> { new TypedValue((int)DxfCode.XCoordinate, origem), new TypedValue((int)DxfCode.Real, escala) };
        tvs.AddRange(ValoresNoBloco.Parametros(v));
        using var rb = new ResultBuffer(tvs.ToArray());
        if (dic.Contains(id)) ((Xrecord)tr.GetObject(dic.GetAt(id), OpenMode.ForWrite)).Data = rb;
        else { var xr = new Xrecord { Data = rb }; dic.SetAt(id, xr); tr.AddNewlyCreatedDBObject(xr, true); }
    }

    internal static (ValoresTemplate Valores, Point3d Origem, double Escala)? LerRegistro(Database db, string id)
    {
        using var tr = db.TransactionManager.StartTransaction();
        try
        {
            var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
            if (!nod.Contains(Dicionario)) return null;
            var dic = (DBDictionary)tr.GetObject(nod.GetAt(Dicionario), OpenMode.ForRead);
            if (!dic.Contains(id)) return null;
            var xr = (Xrecord)tr.GetObject(dic.GetAt(id), OpenMode.ForRead);
            using var rb = xr.Data;
            var arr = rb.AsArray();
            var v = ValoresNoBloco.LerParametros(arr, new TemplateVigaPreMoldada().Parametros);
            Point3d origem = arr.Where(t => t.TypeCode == (short)DxfCode.XCoordinate).Select(t => (Point3d)t.Value).FirstOrDefault();
            double escala = arr.Where(t => t.TypeCode == (short)DxfCode.Real).Select(t => Convert.ToDouble(t.Value)).DefaultIfEmpty(EscalaPadrao).First();
            return (v, origem, escala);
        }
        finally { tr.Commit(); }
    }
}
