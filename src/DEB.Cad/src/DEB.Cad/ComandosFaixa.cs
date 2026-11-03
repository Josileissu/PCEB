using System;
using System.Collections.Generic;
using DEB.Cad.Barra;
using DEB.Cad.Infra;
using DEB.Core.Configuracao;
using DEB.Core.Modelo;

[assembly: Rt.CommandClass(typeof(DEB.Cad.ComandosFaixa))]

namespace DEB.Cad;

/// <summary>Comandos das faixas de armadura (seção 3.4).</summary>
public sealed class ComandosFaixa
{
    private const Rt.CommandFlags Flags = Rt.CommandFlags.Modal | Rt.CommandFlags.UsePickSet;

    [Rt.CommandMethod("ARMFAIXAORT", Flags)] public void Ortogonal() => Faixa(TipoFaixa.Ortogonal);
    [Rt.CommandMethod("ARMFAIXAALI", Flags)] public void Alinhada() => Faixa(TipoFaixa.Alinhada);
    [Rt.CommandMethod("ARMCORTECOTAORT", Flags)] public void CorteCotaOrt() => Faixa(TipoFaixa.CorteCotaOrtogonal);
    [Rt.CommandMethod("ARMCORTECOTAALI", Flags)] public void CorteCotaAli() => Faixa(TipoFaixa.CorteCotaAlinhada);
    [Rt.CommandMethod("ARMCORTECHAMADA", Flags)] public void CorteChamada() => Faixa(TipoFaixa.CorteChamada);
    [Rt.CommandMethod("ARMCORTECHAMADAARCO", Flags)] public void CorteChamadaArco() => Faixa(TipoFaixa.CorteChamadaArco);
    [Rt.CommandMethod("ARMCHAMADA", Flags)] public void Chamada() => Faixa(TipoFaixa.Chamada);
    [Rt.CommandMethod("ARMINDICACAO", Flags)] public void Indicacao() => Faixa(TipoFaixa.Indicacao);

    /// <summary>
    /// ARMREBATER (seção 3.1): a barra vai para o destino e fica uma rebatida vinculada na posição
    /// original. Uma barra por vez.
    /// </summary>
    [Rt.CommandMethod("ARMREBATER", Flags)]
    public void Rebater() => Comandos.Executar("ARMREBATER", (doc, ed, db, cfg) =>
    {
        var barraId = Comandos.SelecionarBarra(ed, db, "\nSelecione a armadura a rebater:");
        if (barraId.IsNull) return;
        var rb = ed.GetPoint("\nPonto base:");
        if (rb.Status != PromptStatus.OK) return;
        var rd = ed.GetPoint(new PromptPointOptions("\nPonto de destino da barra (a rebatida fica no lugar atual):")
            { UseBasePoint = true, BasePoint = rb.Value });
        if (rd.Status != PromptStatus.OK) return;
        var desl = Wcs(ed, rd.Value) - Wcs(ed, rb.Value);
        using var tr = new TransacaoSegura(db);
        var pl = (Polyline)tr.GetObject(barraId, OpenMode.ForWrite);
        ServicoRebatida.Rebater(tr, pl, new Vector2d(desl.X, desl.Y));
        ServicoBarra.Atualizar(tr, pl, cfg);
        tr.Commit();
        ed.WriteMessage("\nBarra rebatida: editar a rebatida ou a barra muda as duas.");
    });

    private static void Faixa(TipoFaixa tipo) => Comandos.Executar("ARMFAIXA", (doc, ed, db, cfg) =>
    {
        var barraId = Comandos.SelecionarBarra(ed, db, "\nSelecione a armadura:");
        if (barraId.IsNull) return;
        double esp;
        using (var tr = new TransacaoSegura(db))
        {
            var d = XDataCad.LerBarra(tr.GetObject(barraId, OpenMode.ForRead));
            // seção 3.4: faixas não se aplicam a treliças (nem a telas)
            if (!d.EhBarraComum) { ed.WriteMessage("\nFaixas não se aplicam a treliças e telas."); return; }
            esp = d.Espacamento > 0 ? d.Espacamento : cfg.EspacamentoCorrente > 0 ? cfg.EspacamentoCorrente : 15;
        }

        int criadas = 0;
        while (true)
        {
            var f = new DadosFaixa { Tipo = tipo, Espacamento = esp, EspacamentoInicial = cfg.EspacamentoInicialFaixa, Escala = cfg.EscalaFaixa };
            Point3d? posicao = tipo switch
            {
                TipoFaixa.CorteChamadaArco => PedirArco(ed, db, f),
                TipoFaixa.Chamada or TipoFaixa.Indicacao => PedirChamada(ed, f),
                _ => PedirLinha(ed, f),
            };
            if (posicao == null) break;
            esp = f.Espacamento;
            using (var tr = new TransacaoSegura(db))
            {
                var pl = (Polyline)tr.GetObject(barraId, OpenMode.ForWrite);
                ServicoFaixa.Criar(tr, pl, f, posicao.Value, cfg);
                tr.Commit();
            }
            criadas++;
        }
        if (criadas > 0) ed.WriteMessage($"\n{criadas} faixa(s) criada(s). A quantidade da barra agora é a soma das faixas.");
    });

    private static Point3d Wcs(Editor ed, Point3d ucs) => ucs.TransformBy(ed.CurrentUserCoordinateSystem);

    /// <summary>Faixas com linha base: ponto inicial, final, posição, espaçamento e (corte) lado.</summary>
    private static Point3d? PedirLinha(Editor ed, DadosFaixa f)
    {
        var r1 = PrimeiroPonto(ed, f, "\nPonto inicial da faixa (Enter termina)");
        if (r1 == null) return null;
        var r2 = ed.GetPoint(new PromptPointOptions("\nPonto final:") { UseBasePoint = true, BasePoint = r1.Value });
        if (r2.Status != PromptStatus.OK) return null;
        var p1 = Wcs(ed, r1.Value);
        var p2 = Wcs(ed, r2.Value);
        f.P1X = p1.X; f.P1Y = p1.Y; f.P2X = p2.X; f.P2Y = p2.Y;

        Point3d posicao;
        if (f.TemCota)
        {
            var rp = ed.GetPoint(new PromptPointOptions("\nPosição da cota:") { UseBasePoint = true, BasePoint = r2.Value });
            if (rp.Status != PromptStatus.OK) return null;
            posicao = Wcs(ed, rp.Value);
        }
        else posicao = p2;

        var s = Comandos.PedirReal(ed, "Espaçamento (cm)", f.Espacamento, 0.1);
        if (s == null) return null;
        f.Espacamento = s.Value;

        if (f.TemCorte)
        {
            var rl = ed.GetPoint(new PromptPointOptions("\nLado do corte (clique do lado em que ficam as barras):")
                { UseBasePoint = true, BasePoint = r2.Value });
            if (rl.Status != PromptStatus.OK) return null;
            var pl = Wcs(ed, rl.Value);
            double cruz = (p2.X - p1.X) * (pl.Y - p1.Y) - (p2.Y - p1.Y) * (pl.X - p1.X);
            f.LadoCorte = cruz >= 0 ? 1 : -1;
        }

        if (f.Tipo == TipoFaixa.CorteChamada)
        {
            var pk = new PromptKeywordOptions("\nChamada aponta para") { AppendKeywordsToMessage = true };
            Comandos.AdicionarPalavras(pk.Keywords, "Inicio", "Centro", "Fim");
            pk.Keywords.Default = "Centro";
            pk.AllowNone = true;
            var rk = ed.GetKeywords(pk);
            if (rk.Status != PromptStatus.OK && rk.Status != PromptStatus.None) return null;
            f.PontoChamada = rk.StringResult == "Inicio" ? 0 : rk.StringResult == "Fim" ? 2 : 1;
            var rt = ed.GetPoint(new PromptPointOptions("\nPosição do texto da chamada:") { UseBasePoint = true, BasePoint = r2.Value });
            if (rt.Status != PromptStatus.OK) return null;
            posicao = Wcs(ed, rt.Value);
        }
        return posicao;
    }

    private static string TextoEscala(double escala) =>
        escala > 0 ? "1:" + Core.Calculo.CalculoComprimento.NumeroCompacto(escala) : "da barra";

    /// <summary>
    /// 1º ponto da faixa com a opção [Escala]: a escala da faixa pode diferir da barra (ex.: barra na
    /// elevação 1:50, distribuição na seção 1:25). A escolha vale também para as próximas faixas.
    /// </summary>
    private static Point3d? PrimeiroPonto(Editor ed, DadosFaixa f, string mensagem)
    {
        while (true)
        {
            var po = new PromptPointOptions($"{mensagem} — escala {TextoEscala(f.Escala)}") { AllowNone = true, AppendKeywordsToMessage = true };
            po.Keywords.Add("Escala");
            var r = ed.GetPoint(po);
            if (r.Status == PromptStatus.Keyword) { if (!PedirEscala(ed, f)) return null; continue; }
            return r.Status == PromptStatus.OK ? r.Value : (Point3d?)null;
        }
    }

    /// <summary>Pergunta a escala da faixa (0 = a da barra) e guarda como padrão das próximas.</summary>
    private static bool PedirEscala(Editor ed, DadosFaixa f)
    {
        var po = new PromptDoubleOptions("\nEscala da faixa 1:n (0 = a mesma da barra)")
        {
            AllowNegative = false, AllowZero = true, AllowNone = true, DefaultValue = f.Escala, UseDefaultValue = true,
        };
        var r = ed.GetDouble(po);
        if (r.Status != PromptStatus.OK && r.Status != PromptStatus.None) return false;
        if (r.Status == PromptStatus.OK) f.Escala = r.Value;
        Plugin.Config.EscalaFaixa = f.Escala;
        Plugin.SalvarConfig();
        ConfigDesenho.SincronizarCorrentes();
        ed.WriteMessage($"\nFaixas novas na escala {TextoEscala(f.Escala)}.");
        return true;
    }

    /// <summary>Corte e chamada em arco: arco/círculo, lado, espaçamento, posição do texto.</summary>
    private static Point3d? PedirArco(Editor ed, Database db, DadosFaixa f)
    {
        PromptEntityResult re;
        while (true)
        {
            var pe = new PromptEntityOptions($"\nSelecione o arco ou círculo (Enter termina) — escala {TextoEscala(f.Escala)}") { AllowNone = true, AppendKeywordsToMessage = true };
            pe.SetRejectMessage("\nSelecione um arco ou um círculo.");
            pe.AddAllowedClass(typeof(Arc), true);
            pe.AddAllowedClass(typeof(Circle), true);
            pe.Keywords.Add("Escala");
            re = ed.GetEntity(pe);
            if (re.Status == PromptStatus.Keyword) { if (!PedirEscala(ed, f)) return null; continue; }
            if (re.Status != PromptStatus.OK) return null;
            break;
        }
        using (var tr = new TransacaoSegura(db))
        {
            var c = (Curve)tr.GetObject(re.ObjectId, OpenMode.ForRead);
            if (c is Arc a)
            {
                f.CX = a.Center.X; f.CY = a.Center.Y; f.Raio = a.Radius;
                f.AnguloIni = a.StartAngle;
                f.AnguloFim = a.EndAngle < a.StartAngle ? a.EndAngle + 2 * Math.PI : a.EndAngle;
            }
            else if (c is Circle ci)
            {
                f.CX = ci.Center.X; f.CY = ci.Center.Y; f.Raio = ci.Radius;
                f.AnguloIni = Math.PI / 2;
                f.AnguloFim = Math.PI / 2 + 2 * Math.PI;
            }
        }
        var pk = new PromptKeywordOptions("\nBarras do lado") { AppendKeywordsToMessage = true, AllowNone = true };
        Comandos.AdicionarPalavras(pk.Keywords, "Interno", "Externo");
        pk.Keywords.Default = "Interno";
        var rk = ed.GetKeywords(pk);
        if (rk.Status != PromptStatus.OK && rk.Status != PromptStatus.None) return null;
        f.LadoCorte = rk.StringResult == "Externo" ? 1 : -1;
        var s = Comandos.PedirReal(ed, "Espaçamento (cm, medido no arco)", f.Espacamento, 0.1);
        if (s == null) return null;
        f.Espacamento = s.Value;
        var rt = ed.GetPoint("\nPosição do texto da chamada:");
        return rt.Status == PromptStatus.OK ? Wcs(ed, rt.Value) : (Point3d?)null;
    }

    /// <summary>Chamada/Indicação: pontos de referência, posição do texto e (chamada) quantidade.</summary>
    private static Point3d? PedirChamada(Editor ed, DadosFaixa f)
    {
        var refs = new List<Point3d>();
        while (true)
        {
            if (refs.Count == 0)
            {
                var r0 = PrimeiroPonto(ed, f, "\nPonto de referência na barra (Enter termina)");
                if (r0 == null) return null;
                refs.Add(Wcs(ed, r0.Value));
                continue;
            }
            var r = ed.GetPoint(new PromptPointOptions("\nOutro ponto de referência (Enter para posicionar o texto):") { AllowNone = true });
            if (r.Status == PromptStatus.None) break;
            if (r.Status != PromptStatus.OK) return null;
            refs.Add(Wcs(ed, r.Value));
        }
        foreach (var p in refs) f.Referencias.Add((p.X, p.Y));
        PromptPointResult rt;
        while (true)
        {
            // [SemLinha]/[ComLinha]: só o texto, sem as linhas de chamada (também muda depois, na paleta)
            var po = new PromptPointOptions("\nPosição do texto da chamada" + (f.OcultarLinhas ? " (sem linha de chamada)" : "")) { AppendKeywordsToMessage = true };
            po.Keywords.Add(f.OcultarLinhas ? "ComLinha" : "SemLinha");
            rt = ed.GetPoint(po);
            if (rt.Status == PromptStatus.Keyword) { f.OcultarLinhas = !f.OcultarLinhas; continue; }
            break;
        }
        if (rt.Status != PromptStatus.OK) return null;
        if (f.Tipo == TipoFaixa.Chamada)
        {
            var q = Comandos.PedirInteiro(ed, "Quantidade", Math.Max(1, refs.Count), 1);
            if (q == null) return null;
            f.Quantidade = q.Value;
        }
        return Wcs(ed, rt.Value);
    }
}
