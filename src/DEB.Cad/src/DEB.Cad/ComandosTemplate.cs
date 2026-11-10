using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using DEB.Cad.Barra;
using DEB.Cad.Infra;
using DEB.Core.Configuracao;
using DEB.Core.Modelo;
using DEB.Core.Tabelas;
using DEB.Core.Templates;

[assembly: Rt.CommandClass(typeof(DEB.Cad.ComandosTemplate))]

namespace DEB.Cad;

/// <summary>
/// ARMTEMPLATE: armadura padrão de um elemento estrutural (bases de reservatório, blocos, pilares,
/// vigas, lajes). Ambiente preparado em 2026-10-27; os templates em si virão dos padrões do escritório.
/// </summary>
public sealed class ComandosTemplate
{
    [Rt.CommandMethod("ARMTEMPLATE")]
    public void ArmTemplate() => Comandos.Executar("ARMTEMPLATE", (doc, ed, db, cfg) =>
    {
        if (CatalogoTemplates.Todos.Count == 0)
        {
            ed.WriteMessage("\nTemplates de armadura — elementos previstos (aguardando os padrões do escritório em PDF/DWG):");
            foreach (var (_, nome, variacoes) in CatalogoTemplates.Categorias)
                ed.WriteMessage($"\n  • {nome}" + (variacoes.Length > 0 ? $" ({variacoes})" : ""));
            ed.WriteMessage("\nNenhum template implementado ainda.\n");
            return;
        }
        using var f = new FormTemplate();
        if (CadApp.ShowModalDialog(f) != DialogResult.OK || f.Escolhido == null) return;
        var r = ed.GetPoint("\nPonto de inserção:");
        if (r.Status != PromptStatus.OK) return;
        var resultado = f.Escolhido.Gerar(f.Valores, cfg);
        using (var tr = db.TransactionManager.StartTransaction())
        {
            var ids = Desenhar(tr, db, cfg, resultado, r.Value.TransformBy(ed.CurrentUserCoordinateSystem));
            tr.Commit();
            ed.WriteMessage($"\n{f.Escolhido.Nome}: {ids.Count} armadura(s) desenhada(s)" +
                            (resultado.Detalhamento.Length > 0 ? $" no detalhamento \"{resultado.Detalhamento}\"." : "."));
        }
        foreach (var a in resultado.Avisos) ed.WriteMessage("\nAviso: " + a);
        // todo elemento desenhado fica salvo como modelo no arquivo do template (pelo nome; o mesmo nome é atualizado)
        try
        {
            var arq = ModelosTemplates.Obter(f.Escolhido);
            string nome = string.IsNullOrWhiteSpace(f.Valores["Nome"]) ? "SEM NOME" : f.Valores["Nome"].Trim();
            arq.Salvar(nome, f.Valores, f.Escolhido.Parametros);
            ModelosTemplates.Gravar(f.Escolhido, arq);
            ed.WriteMessage($"\nModelo \"{nome}\" salvo em {ModelosTemplates.Caminho(f.Escolhido)}.");
        }
        catch (Exception ex)
        {
            Log.Erro("ARMTEMPLATE salvar modelo", ex);
            ed.WriteMessage("\nNão foi possível salvar o modelo (o arquivo está aberto no Excel?): " + ex.Message);
        }
    });

    /// <summary>
    /// ARMRESERVATORIO (pedido do usuário, 2026-11-05): reconhece um reservatório desenhado por outro programa (Eberick:
    /// planta das paredes, cortes e plantas das lajes, inclusive em cm de papel) e gera o detalhamento no padrão do
    /// escritório pelo template "reservatorio", ao lado do desenho original. Direto, sem janela (decisão do usuário);
    /// o que foi lido e o que ficou no padrão saem na linha de comando.
    /// </summary>
    [Rt.CommandMethod("ARMRESERVATORIO", Rt.CommandFlags.UsePickSet)]
    public void ArmReservatorio() => Comandos.Executar("ARMRESERVATORIO", (doc, ed, db, cfg) =>
    {
        var t = CatalogoTemplates.Obter("reservatorio");
        if (t == null) { ed.WriteMessage("\nTemplate de reservatório não registrado."); return; }
        ObjectId[] ids;
        var impl = ed.SelectImplied();
        if (impl.Status == PromptStatus.OK && impl.Value != null && impl.Value.Count > 0) { ids = impl.Value.GetObjectIds(); ed.SetImpliedSelection(new ObjectId[0]); }
        else
        {
            var r = ed.GetSelection(new PromptSelectionOptions { MessageForAdding = "\nSelecione o desenho do reservatório (planta, cortes e lajes, com os títulos):" },
                new SelectionFilter(new[] { new TypedValue((int)DxfCode.Start, "LINE,LWPOLYLINE,TEXT,MTEXT") }));
            if (r.Status != PromptStatus.OK) return;
            ids = r.Value.GetObjectIds();
        }

        var linhas = new List<Core.Templates.Reservatorio.LinhaDesenho>();
        var textos = new List<Core.Templates.Reservatorio.TextoDesenho>();
        using (var tr = new TransacaoSegura(db))
        {
            foreach (var id in ids)
            {
                if (id.IsErased || !(tr.GetObject(id, OpenMode.ForRead) is Entity e)) continue;
                if (XDataCad.Tipo(e) != Core.Persistencia.TipoParte.Nenhum) continue; // já é do plugin
                switch (e)
                {
                    case Line ln:
                        linhas.Add(new Core.Templates.Reservatorio.LinhaDesenho(ln.StartPoint.X, ln.StartPoint.Y, ln.EndPoint.X, ln.EndPoint.Y, ln.Layer));
                        break;
                    case Polyline pl:
                        for (int i = 0; i < pl.NumberOfVertices - (pl.Closed ? 0 : 1); i++)
                        {
                            if (pl.GetSegmentType(i) != SegmentType.Line) continue;
                            var s = pl.GetLineSegment2dAt(i);
                            linhas.Add(new Core.Templates.Reservatorio.LinhaDesenho(s.StartPoint.X, s.StartPoint.Y, s.EndPoint.X, s.EndPoint.Y, pl.Layer));
                        }
                        break;
                    case DBText dt:
                        textos.Add(new Core.Templates.Reservatorio.TextoDesenho
                        {
                            Texto = Core.Reconhecimento.TextoArmadura.Limpar(dt.TextString), X = dt.Position.X, Y = dt.Position.Y,
                            Altura = dt.Height, Rotacao = dt.Rotation, Layer = dt.Layer,
                        });
                        break;
                    case MText mt:
                        textos.Add(new Core.Templates.Reservatorio.TextoDesenho
                        {
                            Texto = Core.Reconhecimento.TextoArmadura.Limpar(mt.Contents), X = mt.Location.X, Y = mt.Location.Y,
                            Altura = mt.TextHeight, Rotacao = mt.Rotation, Layer = mt.Layer,
                        });
                        break;
                }
            }
            tr.Commit();
        }

        var rec = Core.Templates.Reservatorio.ReconhecedorReservatorio.Reconhecer(linhas, textos);
        ed.WriteMessage("\nReconhecimento do reservatório:");
        foreach (var s in rec.Relatorio) ed.WriteMessage("\n  " + s);
        foreach (var a in rec.Avisos) ed.WriteMessage("\n  Aviso: " + a);
        Log.Info("ARMRESERVATORIO: " + string.Join(" | ", rec.Relatorio.Concat(rec.Avisos.Select(a => "Aviso: " + a))));
        if (!rec.Ok) { ed.WriteMessage("\nNada foi gerado."); return; }

        var resultado = t.Gerar(rec.Valores, cfg);
        // ao lado do original (à direita, alinhado pelo topo)
        double largura = rec.XMax - linhas.Min(l => Math.Min(l.X1, l.X2));
        var origem = new Point3d(rec.XMax + Math.Max(500, 0.2 * largura), rec.YMax, 0);
        using (var tr = db.TransactionManager.StartTransaction())
        {
            var desenhadas = Desenhar(tr, db, cfg, resultado, origem);
            tr.Commit();
            ed.WriteMessage($"\n{t.Nome}: {desenhadas.Count} armadura(s) desenhada(s)" +
                            (resultado.Detalhamento.Length > 0 ? $" no detalhamento \"{resultado.Detalhamento}\"" : "") +
                            FormattableString.Invariant($", a partir de ({origem.X:0}, {origem.Y:0})."));
        }
        foreach (var a in resultado.Avisos) ed.WriteMessage("\nAviso: " + a);
    });

    /// <summary>Importa um arquivo de modelos para o arquivo do template; devolve a mensagem para o usuário (null = cancelado).</summary>
    internal static string Importar(ITemplateArmadura t, string caminho, bool perguntar)
    {
        var avisos = new List<string>();
        var outro = ModelosTemplates.LerArquivo(caminho, t, avisos);
        if (outro.TemplateId.Length > 0 && !outro.TemplateId.Equals(t.Id, StringComparison.OrdinalIgnoreCase))
        {
            string m = $"O arquivo é do template \"{outro.TemplateId}\", não de \"{t.Id}\".";
            if (!perguntar || MessageBox.Show(m + " Importar assim mesmo?", "Armadura", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return perguntar ? null : m;
        }
        var arq = ModelosTemplates.Obter(t);
        int n = arq.Importar(outro);
        ModelosTemplates.Gravar(t, arq);
        return $"{n} modelo(s) importado(s) de {Path.GetFileName(caminho)} para {t.Nome}; o PADRÃO do arquivo também passou a valer." +
               (avisos.Count > 0 ? "\n" + string.Join("\n", avisos) : "");
    }

    /// <summary>Exporta o arquivo de modelos do template (cópia).</summary>
    internal static string Exportar(ITemplateArmadura t, string caminho)
    {
        var arq = ModelosTemplates.Obter(t);
        File.WriteAllText(caminho, arq.Escrever(t), new System.Text.UTF8Encoding(true));
        return $"{arq.Modelos.Count} modelo(s) de {t.Nome} exportados para {caminho}.";
    }

    private static string PedirArquivo(Editor ed, bool salvar, string titulo, string sugestao) =>
        Arquivos.Pedir(ed, salvar, titulo, "Modelos (*.csv)|*.csv|Todos os arquivos|*.*", ".csv", sugestao);

    /// <summary>Escolhe o template pela 1ª palavra do nome (Pilar, Base…).</summary>
    private static ITemplateArmadura PedirTemplate(Editor ed)
    {
        var ts = CatalogoTemplates.Todos;
        if (ts.Count == 1) return ts[0];
        string Chave(ITemplateArmadura t) => new string(t.Nome.Split(' ')[0].Normalize(System.Text.NormalizationForm.FormD)
            .Where(ch => char.IsLetterOrDigit(ch)).ToArray());
        var po = new PromptKeywordOptions("\nTemplate") { AppendKeywordsToMessage = true };
        foreach (var t in ts) po.Keywords.Add(Chave(t));
        po.Keywords.Default = Chave(ts.FirstOrDefault(t => t.Id == "pilar-premoldado") ?? ts[0]);
        po.AllowNone = true;
        var r = ed.GetKeywords(po);
        if (r.Status != PromptStatus.OK && r.Status != PromptStatus.None) return null;
        string k = r.Status == PromptStatus.None ? po.Keywords.Default : r.StringResult;
        return ts.FirstOrDefault(t => Chave(t) == k);
    }

    /// <summary>ARMTEMPLATEIMPORTAR: traz os modelos de um arquivo (de outro computador, de um colega…) para o arquivo do template.</summary>
    [Rt.CommandMethod("ARMTEMPLATEIMPORTAR")]
    public void ArmTemplateImportar() => Comandos.Executar("ARMTEMPLATEIMPORTAR", (doc, ed, db, cfg) =>
    {
        var caminho = PedirArquivo(ed, false, "Importar modelos de template", "");
        if (caminho == null) return;
        var t = ModelosTemplates.TemplateDoArquivo(caminho) ?? PedirTemplate(ed);
        if (t == null) return;
        ed.WriteMessage("\n" + Importar(t, caminho, perguntar: false) + "\n");
    }, suspender: false);

    /// <summary>ARMTEMPLATEEXPORTAR: salva uma cópia do arquivo de modelos de um template.</summary>
    [Rt.CommandMethod("ARMTEMPLATEEXPORTAR")]
    public void ArmTemplateExportar() => Comandos.Executar("ARMTEMPLATEEXPORTAR", (doc, ed, db, cfg) =>
    {
        var t = PedirTemplate(ed);
        if (t == null) return;
        var caminho = PedirArquivo(ed, true, "Exportar modelos — " + t.Nome, t.Id + ".csv");
        if (caminho == null) return;
        ed.WriteMessage("\n" + Exportar(t, caminho) + "\n");
    }, suspender: false);

    /// <summary>ARMCONSOLOS: catálogo de consolos padrão do escritório (usado pelo template do pilar pré-moldado).</summary>
    [Rt.CommandMethod("ARMCONSOLOS")]
    public void ArmConsolos() => Comandos.Executar("ARMCONSOLOS", (doc, ed, db, cfg) =>
    {
        using var f = Paleta.Catalogos.Consolos(Plugin.Config.Consolos);
        if (CadApp.ShowModalDialog(f) != DialogResult.OK) return;
        Plugin.Config.Consolos = f.Resultado;
        Plugin.SalvarConfig();
        Core.Templates.PilarPreMoldado.TemplatePilarPreMoldado.Catalogo = Plugin.Config.Consolos;
        ed.WriteMessage($"\n{f.Resultado.Count} consolo(s) padrão salvos: " + string.Join(", ", f.Resultado.Select(t => t.Nome)) + ".");
    }, suspender: false);

    /// <summary>ARMDENTES: catálogo de dentes Gerber padrão (usado pelo template da viga pré-moldada).</summary>
    [Rt.CommandMethod("ARMDENTES")]
    public void ArmDentes() => Comandos.Executar("ARMDENTES", (doc, ed, db, cfg) =>
    {
        using var f = Paleta.Catalogos.Dentes(Plugin.Config.Dentes);
        if (CadApp.ShowModalDialog(f) != DialogResult.OK) return;
        Plugin.Config.Dentes = f.Resultado;
        Plugin.SalvarConfig();
        Core.Templates.VigaPreMoldada.TemplateVigaPreMoldada.CatalogoDentes = Plugin.Config.Dentes;
        ed.WriteMessage($"\n{f.Resultado.Count} dente(s) padrão salvos: " + string.Join(", ", f.Resultado.Select(t => t.Nome)) + ".");
    }, suspender: false);

    /// <summary>ARMINSERTOS: catálogo de insertos metálicos padrão (usado pelo template do pilar pré-moldado).</summary>
    [Rt.CommandMethod("ARMINSERTOS")]
    public void ArmInsertos() => Comandos.Executar("ARMINSERTOS", (doc, ed, db, cfg) =>
    {
        using var f = Paleta.Catalogos.Insertos(Plugin.Config.Insertos);
        if (CadApp.ShowModalDialog(f) != DialogResult.OK) return;
        Plugin.Config.Insertos = f.Resultado;
        Plugin.SalvarConfig();
        Core.Templates.PilarPreMoldado.TemplatePilarPreMoldado.CatalogoInsertos = Plugin.Config.Insertos;
        ed.WriteMessage($"\n{f.Resultado.Count} inserto(s) padrão salvos: " + string.Join(", ", f.Resultado.Select(t => t.Nome)) + ".");
    }, suspender: false);

    /// <summary>
    /// Desenha o resultado de um template no espaço corrente: forma, armaduras (com grupos VAR e telas),
    /// faixas e indicações, textos, cotas e tabelas; com <see cref="ResultadoTemplate.Detalhamento"/>, tudo
    /// vira um bloco e a relação do aço parcial dele é inserida. Retorna as armaduras criadas (antes do bloco).
    /// </summary>
    internal static List<ObjectId> Desenhar(Transaction tr, Database db, ConfiguracaoArmadura cfg, ResultadoTemplate r, Point3d origem)
    {
        double cpu = Desenho.CmPorUnidade(db, cfg);
        var esp = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
        var todos = new List<ObjectId>();
        Point2d P(double x, double y) => new Point2d(origem.X + x / cpu, origem.Y + y / cpu);
        Point3d P3(double x, double y) { var q = P(x, y); return new Point3d(q.X, q.Y, origem.Z); }
        ObjectId Layer(string nome, short cor) => Desenho.GarantirLayer(db, tr, nome, cor);
        T Add<T>(T e, ObjectId layer) where T : Entity
        {
            e.SetDatabaseDefaults(db);
            e.LayerId = layer;
            esp.AppendEntity(e);
            tr.AddNewlyCreatedDBObject(e, true);
            todos.Add(e.ObjectId);
            return e;
        }
        Polyline Poli(IList<(double X, double Y)> pts, IList<double> bulges, bool fechada)
        {
            var pl = new Polyline();
            for (int i = 0; i < pts.Count; i++) pl.AddVertexAt(i, P(pts[i].X, pts[i].Y), i < bulges.Count ? bulges[i] : 0, 0, 0);
            pl.Closed = fechada;
            pl.Elevation = origem.Z;
            return pl;
        }
        short CorDoLayer(string nome) => nome switch { "ARM-TANQUE" => 4, "ARM-ESTACA" => 8, "ARM-CORTE" => 1, "ARM-TEXTO-FORMA" => 7, "ARM-NIVEL" => 9,
            "ARM-CORDOALHA" => 5, "ARM-CORDOALHA-ISOLADA" => 1, "ARM-ISOLAMENTO" => 7, "ARM-INSERTO" => 1, _ => 8 };

        // ---- forma, textos, cotas ----
        foreach (var c in r.Contornos) Add(Poli(c.Vertices, c.Bulges, c.Fechado), Layer(c.Layer, CorDoLayer(c.Layer)));
        foreach (var c in r.Circulos) Add(new Circle(P3(c.X, c.Y), Vector3d.ZAxis, c.Raio / cpu), Layer(c.Layer, CorDoLayer(c.Layer)));
        foreach (var t in r.Textos)
        {
            var p = P3(t.X, t.Y);
            var txt = new DBText { TextString = t.Texto, Height = t.Altura / cpu, Rotation = t.Angulo, Position = p };
            if (db.Textstyle.IsValid) txt.TextStyleId = db.Textstyle;
            if (t.Alinhamento != 0)
            {
                txt.HorizontalMode = t.Alinhamento == 1 ? TextHorizontalMode.TextCenter : TextHorizontalMode.TextRight;
                txt.AlignmentPoint = p;
            }
            Add(txt, Layer(t.Layer, CorDoLayer(t.Layer)));
            if (t.Alinhamento != 0) txt.AdjustAlignment(db);
        }
        foreach (var c in r.Cotas)
        {
            var dim = Add(new RotatedDimension(c.Angulo, P3(c.P1.X, c.P1.Y), P3(c.P2.X, c.P2.Y), P3(c.Posicao.X, c.Posicao.Y), c.Texto, db.Dimstyle),
                Layer("ARM-COTA-FORMA", 8));
            // estilo de cota do escritório (ESTRUT50, ESTRUT25…) na escala da cota
            dim.DimensionStyle = EstilosCota.Garantir(db, tr, c.Escala > 0 ? c.Escala : r.Escala, cpu, cfg);
        }
        foreach (var t in r.Tabelas) Tabela(tr, db, esp, t, P3(t.X, t.Y), cfg, cpu, r.Escala, todos);

        // ---- detalhes típicos da biblioteca do escritório (DWG inseridos como bloco, empilhados na coluna à direita;
        //      decisão do usuário, 2026-11-08: nunca desenhados por código) ----
        if (r.DetalhesBiblioteca.Count > 0 && r.ColunaDetalhes.HasValue)
        {
            var btab = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            double cx = r.ColunaDetalhes.Value.X, cy = r.ColunaDetalhes.Value.Y;
            foreach (var nomeDet in r.DetalhesBiblioteca)
            {
                string arq = BibliotecaDetalhes.Arquivo(nomeDet);
                if (!File.Exists(arq)) { r.Avisos.Add($"Detalhe típico \"{nomeDet}\" não está na biblioteca ({arq}): não inserido."); continue; }
                string nomeBloco = "DET_" + nomeDet;
                ObjectId defId;
                try
                {
                    if (btab.Has(nomeBloco)) defId = btab[nomeBloco];
                    else
                    {
                        using var lado = new Database(false, true);
                        lado.ReadDwgFile(arq, FileOpenMode.OpenForReadAndAllShare, true, "");
                        lado.CloseInput(true);
                        defId = db.Insert(nomeBloco, lado, true);
                    }
                }
                catch (Exception ex)
                {
                    Log.Erro("Template.DetalheBiblioteca", ex);
                    r.Avisos.Add($"Detalhe típico \"{nomeDet}\" não pôde ser lido ({ex.Message}).");
                    continue;
                }
                // unidades da folha (mm do papel) × escala do desenho; os outros já estão em cm
                double fator = (BibliotecaDetalhes.EmUnidadesDeFolha(nomeDet) ? r.Escala / 10 : 1) * BibliotecaDetalhes.FatorArquivo(nomeDet) / cpu;
                var br = Add(new BlockReference(P3(cx, cy), defId) { ScaleFactors = new Scale3d(fator) }, Layer("ARM-DETALHE-TIPICO", 7));
                Extents3d ext;
                try { ext = br.GeometricExtents; } catch { continue; }
                var canto = P3(cx, cy);
                br.TransformBy(Matrix3d.Displacement(new Vector3d(canto.X - ext.MinPoint.X, canto.Y - ext.MaxPoint.Y, 0)));
                cy -= (ext.MaxPoint.Y - ext.MinPoint.Y) * cpu + r.Escala / 2;
            }
        }
        foreach (var hc in r.Hachuras)
        {
            try
            {
                var hat = Add(new Hatch(), Layer(hc.Layer, 8));
                hat.SetHatchPattern(HatchPatternType.PreDefined, string.IsNullOrEmpty(hc.Padrao) ? "ANSI31" : hc.Padrao);
                hat.PatternScale = hc.EscalaPadrao > 0 ? hc.EscalaPadrao / cpu : 1;
                hat.Associative = false;
                var pts = new Point2dCollection();
                var bul = new DoubleCollection();
                foreach (var (x, y) in hc.Vertices) { pts.Add(P(x, y)); bul.Add(0); }
                pts.Add(P(hc.Vertices[0].X, hc.Vertices[0].Y)); bul.Add(0);
                hat.AppendLoop(HatchLoopTypes.Default, pts, bul);
                hat.EvaluateHatch(true);
            }
            catch (Rt.Exception ex) { Log.Erro("Template.Hachura", ex); }
        }

        // ---- armaduras ----
        var ids = new List<ObjectId>();
        var porNome = new Dictionary<string, ObjectId>(StringComparer.Ordinal);
        var layerBarra = Layer(cfg.LayerBarra, cfg.CorBarra);
        foreach (var b in r.Barras)
        {
            var pl = Add(Poli(b.Vertices, b.Bulges, b.Fechada), layerBarra);
            var d = new DadosArmadura
            {
                Bitola = b.Bitola, Aco = b.Aco, Quantidade = Math.Max(1, b.Quantidade), Espacamento = b.Espacamento, Estribo = b.Estribo,
                Escala = b.Escala > 0 ? b.Escala : r.Escala, Repeticoes = Math.Max(1, b.Repeticoes), Corrido = b.Corrido,
                ComprimentosVar = new List<double>(b.ComprimentosVar),
            };
            if (!string.IsNullOrWhiteSpace(b.Tela))
            {
                var m = cfg.Telas.Tela(b.Tela);
                d.Tela = m?.Nome ?? b.Tela;
                d.Bitola = m?.PhiLongitudinal ?? 5;
                d.Aco = m?.Aco ?? "CA-60";
                d.TelaCotas = false;
            }
            int n = GeometriaBarra.NumeroSegmentos(pl);
            for (int s = 0; s < n; s++)
            {
                var f = s < b.Formatos.Count ? b.Formatos[s] : FormatoSegmento.Linha;
                bool sob = b.Sobrepostos.Contains(s);
                d.Segmentos.Add(new Segmento
                {
                    Formato = f, Sobreposto = sob, Var = b.SegmentosVar.Contains(s),
                    Cotar = !Segmento.EhGancho(f) && !sob && (b.Cotados.Count == 0 || b.Cotados.Contains(s)),
                });
            }
            ServicoBarra.Criar(tr, pl, d, cfg);
            if (!string.IsNullOrEmpty(b.Layer)) pl.LayerId = Layer(b.Layer, 5);
            if (b.PosicaoDescricao.HasValue)
            {
                // descrição onde o template pediu (ex.: na vertical, ao lado da barra); o plugin guarda a posição
                var dd = XDataCad.LerBarra(pl);
                var desc = ServicoBarra.Parte<MText>(tr, db, dd.HandleDescricao, Core.Persistencia.TipoParte.Descricao, XDataCad.Hex(pl.ObjectId));
                if (desc != null)
                {
                    desc.UpgradeOpen();
                    desc.Attachment = AttachmentPoint.MiddleCenter;
                    desc.Location = P3(b.PosicaoDescricao.Value.X, b.PosicaoDescricao.Value.Y);
                    desc.Rotation = b.RotacaoDescricao;
                    ServicoBarra.Atualizar(tr, pl, cfg);
                }
            }
            ids.Add(pl.ObjectId);
            if (!string.IsNullOrEmpty(b.Nome)) porNome[b.Nome] = pl.ObjectId;
        }

        // ---- faixas e indicações ----
        foreach (var f in r.Faixas)
        {
            if (!porNome.TryGetValue(f.Barra ?? "", out var bid)) continue;
            var a = P(f.P1.X, f.P1.Y);
            var b = P(f.P2.X, f.P2.Y);
            var dados = new DadosFaixa
            {
                Tipo = f.Tipo, Espacamento = f.Espacamento > 0 ? f.Espacamento : 15, Escala = f.Escala > 0 ? f.Escala : r.Escala,
                P1X = a.X, P1Y = a.Y, P2X = b.X, P2Y = b.Y,
                Referencias = f.Referencias.Select(x => { var q = P(x.X, x.Y); return (q.X, q.Y); }).ToList(),
                Repeticoes = Math.Max(1, f.Repeticoes), MostrarDiametro = f.MostrarDiametro, MostrarComprimento = f.MostrarComprimento, Patamar = f.Patamar,
            };
            ServicoFaixa.Criar(tr, (Polyline)tr.GetObject(bid, OpenMode.ForWrite), dados, P3(f.Posicao.X, f.Posicao.Y), cfg);
        }

        if (string.IsNullOrWhiteSpace(r.Detalhamento)) return ids;

        // ---- detalhamento (bloco) com tudo, e a relação do aço parcial dele ----
        foreach (var id in ids) todos.AddRange(PartesDaBarra(tr, db, (Polyline)tr.GetObject(id, OpenMode.ForRead)));
        // o nome pode vir da marca do modelo do Revit ("V1/2"): caracteres que o CAD não aceita em nome de bloco viram "-"
        string nomeBase = NomeDeBloco(r.Detalhamento);
        string nome = nomeBase;
        for (int k = 2; Relacao.ServicoDetalhamento.ValidarNome(db, tr, nome) != null && k < 1000; k++) nome = $"{nomeBase} ({k})";
        var dadosDet = r.DadosDetalhamento ?? new DadosDetalhamento();
        if (string.IsNullOrWhiteSpace(dadosDet.Grupo)) dadosDet.Grupo = Relacao.ServicoDetalhamento.GrupoSugerido(nome);
        if (string.IsNullOrWhiteSpace(dadosDet.Comentarios)) dadosDet.Comentarios = "Gerado pelo template.";
        Relacao.ServicoDetalhamento.Criar(db, tr, todos.Distinct().Where(i => !i.IsErased).ToArray(), nome, dadosDet);
        r.Detalhamento = nome;
        if (r.PosicaoRA.HasValue)
        {
            var op = Core.RelacaoAco.OpcoesTabelaRA.Padrao(cfg);
            op.Escala = r.Escala;
            op.Blocos.Add(nome);
            op.NomeNoTitulo = true;
            Relacao.ServicoRA.Inserir(db, tr, P3(r.PosicaoRA.Value.X, r.PosicaoRA.Value.Y), cfg, op);
        }
        else Relacao.ServicoRA.Atualizar(db, tr, cfg);
        return ids;
    }

    /// <summary>Nome de bloco para um detalhamento: sem as pontas em branco e com "-" no lugar dos caracteres que o CAD não aceita.</summary>
    internal static string NomeDeBloco(string nome) =>
        string.Concat((nome ?? "").Trim().Select(ch => "<>/\\\":;?*|,=`".IndexOf(ch) >= 0 ? '-' : ch));

    /// <summary>Descrição, cotas, faixas (e partes) e desenho gerado de uma barra.</summary>
    private static IEnumerable<ObjectId> PartesDaBarra(Transaction tr, Database db, Polyline pl)
    {
        var d = XDataCad.LerBarra(pl);
        if (d == null) yield break;
        foreach (var h in new[] { d.HandleDescricao }.Concat(d.Segmentos.Select(s => s.HandleCota)).Concat(d.Rebatidas).Concat(d.PartesDesenho))
        {
            var id = XDataCad.IdDoHandle(db, h);
            if (!id.IsNull && !id.IsErased) yield return id;
        }
        foreach (var hf in d.Faixas)
        {
            var fid = XDataCad.IdDoHandle(db, hf);
            if (fid.IsNull || fid.IsErased) continue;
            yield return fid;
            foreach (var hp in ServicoFaixa.Ler((Entity)tr.GetObject(fid, OpenMode.ForRead))?.Partes ?? new List<string>())
            {
                var pid = XDataCad.IdDoHandle(db, hp);
                if (!pid.IsNull && !pid.IsErased) yield return pid;
            }
        }
    }

    /// <summary>Tabela de texto simples (título mesclado + linhas), com o canto superior esquerdo em <paramref name="p"/>.</summary>
    private static void Tabela(Transaction tr, Database db, BlockTableRecord esp, TabelaTemplate t, Point3d p, ConfiguracaoArmadura cfg,
        double cpu, double escala, List<ObjectId> todos)
    {
        int cols = Math.Max(1, t.Linhas.Count == 0 ? 1 : t.Linhas.Max(l => l.Length));
        double h = cfg.AlturaTexto(cpu, t.Escala > 0 ? t.Escala : escala);
        var tab = new Table { Position = p, LayerId = Desenho.GarantirLayer(db, tr, cfg.LayerTabela, cfg.CorTabela) };
        if (db.Tablestyle.IsValid) tab.TableStyle = db.Tablestyle;
        esp.AppendEntity(tab);
        tr.AddNewlyCreatedDBObject(tab, true);
        tab.SetSize(t.Linhas.Count + 1, cols);
        // margem do texto dentro das células (sem ela o texto encosta nas bordas)
        try { tab.HorizontalCellMargin = 0.5 * h; tab.VerticalCellMargin = 0.3 * h; }
        catch (Rt.Exception ex) { Log.Erro("Tabela.Margens", ex); }
        for (int c = 0; c < cols; c++) tab.SetColumnWidth(c, (c < t.Larguras.Count && t.Larguras[c] > 0 ? t.Larguras[c] : 6) * h);
        // mescla ANTES de escrever (no GstarCAD, mesclar depois zera a altura do texto)
        if (cols > 1) tab.MergeCells(CellRange.Create(tab, 0, 0, 0, cols - 1));
        CellAlignment Alinhamento(int c) => (c < t.Alinhamentos.Count ? t.Alinhamentos[c] : 1) switch
        {
            0 => CellAlignment.MiddleLeft,
            2 => CellAlignment.MiddleRight,
            _ => CellAlignment.MiddleCenter,
        };
        void Cel(int l, int c, string s, CellAlignment al)
        {
            var cel = tab.Cells[l, c];
            cel.TextHeight = h;
            cel.TextString = (s ?? "").Replace(TabelaTemplate.Quebra, "\\P");
            try { tab.SetTextHeight(l, c, h); } catch (Rt.Exception) { }
            // margens da célula (a propriedade global da tabela não tem efeito no GstarCAD 2021)
            try
            {
                tab.SetMargin(l, c, CellMargins.Left, 0.6 * h);
                tab.SetMargin(l, c, CellMargins.Right, 0.6 * h);
                tab.SetMargin(l, c, CellMargins.Top, 0.3 * h);
                tab.SetMargin(l, c, CellMargins.Bottom, 0.3 * h);
            }
            catch (Rt.Exception ex) { Log.Erro("Tabela.SetMargin", ex); }
            cel.Alignment = al;
        }
        Cel(0, 0, t.Titulo, Alinhamento(0) == CellAlignment.MiddleLeft ? CellAlignment.MiddleLeft : CellAlignment.MiddleCenter);
        for (int l = 0; l < t.Linhas.Count; l++)
            for (int c = 0; c < cols; c++) Cel(l + 1, c, c < t.Linhas[l].Length ? t.Linhas[l][c] : "", Alinhamento(c));
        int NLinhas(string s) => (s ?? "").Split('\n').Length;
        tab.SetRowHeight(0, TabelaTemplate.AlturaLinha(NLinhas(t.Titulo)) * h);
        for (int l = 0; l < t.Linhas.Count; l++) tab.SetRowHeight(l + 1, TabelaTemplate.AlturaLinha(t.Linhas[l].Max(NLinhas)) * h);
        todos.Add(tab.ObjectId);
    }
}

/// <summary>Escolha do template e dos valores dos parâmetros.</summary>
internal sealed class FormTemplate : Form
{
    public ITemplateArmadura Escolhido { get; private set; }
    public ValoresTemplate Valores { get; private set; }
    /// <summary>Modelos do arquivo do template (PADRÃO + elementos salvos).</summary>
    public ComboBox Modelos { get; }
    private readonly TreeView _lista;

    /// <summary>Autoteste: escolhe o template pela id (como um clique na lista).</summary>
    internal void Selecionar(string id) =>
        _lista.SelectedNode = _lista.Nodes.Cast<TreeNode>().SelectMany(n => n.Nodes.Cast<TreeNode>())
            .FirstOrDefault(n => (n.Tag as ITemplateArmadura)?.Id == id);

    private void Tentar(Action a)
    {
        try { a(); }
        catch (Exception ex) { Log.Erro("ARMTEMPLATE modelos", ex); MessageBox.Show(ex.Message, "Armadura", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    public FormTemplate()
    {
        Text = "Armadura — templates de elementos";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(840, 600);
        ShowInTaskbar = false;
        MinimizeBox = false;

        var lista = new TreeView { Dock = DockStyle.Left, Width = 240, HideSelection = false };
        _lista = lista;
        foreach (var (cat, nome, _) in CatalogoTemplates.Categorias)
        {
            var no = lista.Nodes.Add(nome);
            var ts = CatalogoTemplates.DaCategoria(cat).ToList();
            if (ts.Count == 0) no.Nodes.Add("(aguardando o padrão)").ForeColor = SystemColors.GrayText;
            foreach (var t in ts) no.Nodes.Add(new TreeNode(t.Nome) { Tag = t });
        }
        lista.ExpandAll();
        var grid = new PropertyGrid { Dock = DockStyle.Fill, ToolbarVisible = false, PropertySort = PropertySort.Categorized };
        var desc = new Label { Dock = DockStyle.Top, Height = 44, Padding = new Padding(6), Text = "Escolha um template à esquerda." };
        var botoes = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40, FlowDirection = System.Windows.Forms.FlowDirection.RightToLeft, Padding = new Padding(4) };
        var cancelar = new Button { Text = "Cancelar", Width = 90, Height = 28, DialogResult = DialogResult.Cancel };
        var ok = new Button { Text = "Desenhar", Width = 90, Height = 28, Enabled = false };
        botoes.Controls.Add(cancelar);
        botoes.Controls.Add(ok);
        CancelButton = cancelar;

        // modelos salvos (arquivo do template): escolher, salvar como padrão, excluir, importar, exportar, abrir
        var barra = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 66, Padding = new Padding(4), Enabled = false };
        barra.Controls.Add(new Label { Text = "Modelo:", AutoSize = true, Margin = new Padding(3, 8, 3, 3) });
        Modelos = new ComboBox { Width = 230, DropDownStyle = ComboBoxStyle.DropDownList };
        barra.Controls.Add(Modelos);
        Button B(string t, string dica) { var b = new Button { Text = t, AutoSize = true, Height = 26 }; new ToolTip().SetToolTip(b, dica); barra.Controls.Add(b); return b; }
        var salvarPadrao = B("Salvar como padrão", "Os valores atuais passam a ser a linha PADRÃO (com que a janela abre).");
        var excluir = B("Excluir modelo", "Tira o modelo escolhido do arquivo.");
        var importar = B("Importar…", "Traz os modelos (e o PADRÃO) de outro arquivo; modelos de mesmo nome são substituídos.");
        var exportar = B("Exportar…", "Salva uma cópia do arquivo de modelos deste template.");
        var abrir = B("Abrir arquivo", "Abre o arquivo de modelos no Excel/Bloco de Notas.");

        void Mostrar(string modelo)
        {
            Valores = ModelosTemplates.Obter(Escolhido).Valores(Escolhido, modelo);
            grid.SelectedObject = new ParametrosEditaveis(Escolhido, Valores);
        }
        void Recarregar(string selecionar = null)
        {
            var arq = ModelosTemplates.Obter(Escolhido);
            Modelos.Items.Clear();
            Modelos.Items.Add(ArquivoModelos.NomePadrao);
            foreach (var m in arq.Modelos.OrderBy(m => m.Nome, StringComparer.CurrentCultureIgnoreCase)) Modelos.Items.Add(m.Nome);
            int i = selecionar == null ? 0 : Modelos.Items.IndexOf(Modelos.Items.Cast<string>().FirstOrDefault(x => x.Equals(selecionar, StringComparison.OrdinalIgnoreCase)) ?? "");
            Modelos.SelectedIndex = Math.Max(0, i); // dispara Mostrar
        }
        Modelos.SelectedIndexChanged += (s, e) =>
        {
            if (Escolhido == null) return;
            var nome = Modelos.SelectedItem as string;
            Mostrar(nome == ArquivoModelos.NomePadrao ? null : nome);
            excluir.Enabled = nome != ArquivoModelos.NomePadrao;
        };
        salvarPadrao.Click += (s, e) => Tentar(() =>
        {
            var arq = ModelosTemplates.Obter(Escolhido);
            arq.Salvar(ArquivoModelos.NomePadrao, Valores, Escolhido.Parametros);
            ModelosTemplates.Gravar(Escolhido, arq);
            MessageBox.Show("Valores salvos como PADRÃO deste template.", "Armadura");
        });
        excluir.Click += (s, e) => Tentar(() =>
        {
            var nome = Modelos.SelectedItem as string;
            if (nome == null || nome == ArquivoModelos.NomePadrao) return;
            if (MessageBox.Show($"Excluir o modelo \"{nome}\" do arquivo?", "Armadura", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            var arq = ModelosTemplates.Obter(Escolhido);
            arq.Excluir(nome);
            ModelosTemplates.Gravar(Escolhido, arq);
            Recarregar();
        });
        importar.Click += (s, e) => Tentar(() =>
        {
            using var d = new OpenFileDialog { Title = "Importar modelos — " + Escolhido.Nome, Filter = "Modelos (*.csv)|*.csv|Todos os arquivos|*.*" };
            if (d.ShowDialog(this) != DialogResult.OK) return;
            var msg = ComandosTemplate.Importar(Escolhido, d.FileName, perguntar: true);
            if (msg != null) { MessageBox.Show(msg, "Armadura"); Recarregar(); }
        });
        exportar.Click += (s, e) => Tentar(() =>
        {
            using var d = new SaveFileDialog { Title = "Exportar modelos — " + Escolhido.Nome, Filter = "Modelos (*.csv)|*.csv", FileName = Escolhido.Id + ".csv" };
            if (d.ShowDialog(this) != DialogResult.OK) return;
            MessageBox.Show(ComandosTemplate.Exportar(Escolhido, d.FileName), "Armadura");
        });
        abrir.Click += (s, e) => Tentar(() =>
        {
            ModelosTemplates.Obter(Escolhido);
            Arquivos.Abrir(ModelosTemplates.Caminho(Escolhido));
        });

        lista.AfterSelect += (s, e) =>
        {
            Escolhido = e.Node.Tag as ITemplateArmadura;
            ok.Enabled = barra.Enabled = Escolhido != null;
            if (Escolhido == null) { grid.SelectedObject = null; Modelos.Items.Clear(); desc.Text = "Escolha um template à esquerda."; return; }
            desc.Text = Escolhido.Descricao;
            try { Recarregar(); }
            catch (Exception ex)
            {
                // arquivo de modelos com problema: abre com o padrão do plugin
                Log.Erro("Modelos do template", ex);
                Valores = new ValoresTemplate(Escolhido.Parametros);
                grid.SelectedObject = new ParametrosEditaveis(Escolhido, Valores);
                MessageBox.Show("Não foi possível ler o arquivo de modelos: " + ex.Message, "Armadura", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        };
        ok.Click += (s, e) =>
        {
            var erros = CatalogoTemplates.Validar(Escolhido, Valores);
            if (erros.Count > 0) { MessageBox.Show(string.Join("\n", erros), "Armadura", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            DialogResult = DialogResult.OK;
            Close();
        };

        var direita = new Panel { Dock = DockStyle.Fill };
        direita.Controls.Add(grid);
        direita.Controls.Add(barra);
        direita.Controls.Add(desc);
        Controls.Add(direita);
        Controls.Add(lista);
        Controls.Add(botoes);
    }
}

/// <summary>Parâmetros de um template como propriedades do PropertyGrid (descritores dinâmicos).</summary>
internal sealed class ParametrosEditaveis : CustomTypeDescriptor
{
    private readonly ITemplateArmadura _t;
    private readonly ValoresTemplate _v;

    public ParametrosEditaveis(ITemplateArmadura t, ValoresTemplate v) { _t = t; _v = v; }

    public override PropertyDescriptorCollection GetProperties(Attribute[] attributes) => GetProperties();

    public override PropertyDescriptorCollection GetProperties() =>
        new PropertyDescriptorCollection(_t.Parametros.Select(p => (PropertyDescriptor)new Descritor(p, _v)).ToArray());

    public override object GetPropertyOwner(PropertyDescriptor pd) => this;

    private sealed class Descritor : PropertyDescriptor, Paleta.IParametroDescritor
    {
        private readonly ParametroTemplate _p;
        private readonly ValoresTemplate _v;

        public ParametroTemplate Parametro => _p;

        public Descritor(ParametroTemplate p, ValoresTemplate v) : base(p.Nome, Atributos(p)) { _p = p; _v = v; }

        private static Attribute[] Atributos(ParametroTemplate p)
        {
            var a = new List<Attribute>
            {
                new CategoryAttribute(p.Grupo), new DescriptionAttribute(p.Descricao),
                new DisplayNameAttribute(p.Unidade.Length > 0 ? $"{p.Rotulo} ({p.Unidade})" : p.Rotulo),
            };
            // lista (consolos, níveis, insertos): botão "…" que abre a janela
            if (p.Tipo == TipoParametro.Tabela) a.Add(new EditorAttribute(typeof(Paleta.EditorTabela), typeof(System.Drawing.Design.UITypeEditor)));
            return a.ToArray();
        }

        public override Type ComponentType => typeof(ParametrosEditaveis);
        public override bool IsReadOnly => false;
        public override Type PropertyType => typeof(string);
        public override bool CanResetValue(object c) => true;
        public override void ResetValue(object c) => _v[_p.Nome] = _p.Padrao;
        public override bool ShouldSerializeValue(object c) => false;
        public override object GetValue(object c) => _v[_p.Nome];

        public override void SetValue(object c, object valor)
        {
            var s = (valor as string ?? "").Trim();
            if (_p.Tipo == TipoParametro.Numero || _p.Tipo == TipoParametro.Inteiro || _p.Tipo == TipoParametro.Bitola)
            {
                if (!double.TryParse(s.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                    throw new ArgumentException("Informe um número.");
            }
            _v[_p.Nome] = _p.Tipo == TipoParametro.Tabela ? (valor as string ?? "") : s;
        }

        public override TypeConverter Converter => _p.Tipo switch
        {
            TipoParametro.Tabela => new Paleta.ResumoTabelaConverter(),
            TipoParametro.Opcao => new Lista(_p.Opcoes),
            TipoParametro.SimNao => new Lista(new List<string> { "Sim", "Não" }),
            _ => base.Converter,
        };
    }

    private sealed class Lista : StringConverter
    {
        private readonly List<string> _opcoes;
        public Lista(List<string> opcoes) { _opcoes = opcoes; }
        public override bool GetStandardValuesSupported(ITypeDescriptorContext c) => true;
        public override bool GetStandardValuesExclusive(ITypeDescriptorContext c) => true;
        public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext c) => new StandardValuesCollection(_opcoes);
    }
}
