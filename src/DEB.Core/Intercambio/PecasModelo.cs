using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using DEB.Core.Persistencia;

namespace DEB.Core.Intercambio;

// =====================================================================================================================
// Peças levantadas num modelo (Revit) para detalhar no CAD (pedido do usuário, 2026-11-09): o DEB.Revit grava um JSON
// com os grupos de peças iguais (iguais ou giradas = mesma marca) e o DEB.Cad gera um detalhamento por grupo pelo template.
// Só dados neutros: medidas em cm no sistema local da peça, cotas de nível em m. Nada do Revit nem do CAD aqui.
// =====================================================================================================================

/// <summary>Consolo numa face do pilar (sistema local: seção b em x, h em y; z a partir da base).</summary>
public sealed class ConsoloPeca
{
    /// <summary>Face do template do pilar: 1 = +x, 2 = −y, 3 = −x, 4 = +y.</summary>
    public int Face { get; set; }
    /// <summary>Início e fim ao longo da face (cm): coordenada y nas faces 1 e 3, x nas faces 2 e 4.</summary>
    public double Inicio { get; set; }
    public double Fim { get; set; }
    /// <summary>Base e topo do consolo junto à face, a partir da base do pilar (cm).</summary>
    public double Base { get; set; }
    public double Topo { get; set; }
    /// <summary>Balanço (cm).</summary>
    public double Balanco { get; set; }
    /// <summary>Altura na ponta (cm); menor que a altura junto à face = trapezoidal.</summary>
    public double AlturaPonta { get; set; }

    public double Largura => Fim - Inicio;
    public double AlturaFace => Topo - Base;
}

/// <summary>Nível do modelo que corta o pilar (nome e cota em m).</summary>
public sealed class NivelPeca
{
    public string Nome { get; set; } = "";
    public double Cota { get; set; }
}

/// <summary>Peça do modelo que pertence a um grupo (id do elemento, marca que tinha, giro em relação ao representante).</summary>
public sealed class ElementoPeca
{
    public string Id { get; set; } = "";
    public string Marca { get; set; } = "";
    public int Giro { get; set; }
}

/// <summary>Grupo de pilares iguais (ou girados 90/180/270°) do modelo.</summary>
public sealed class PilarPeca
{
    public string Marca { get; set; } = "";
    public List<ElementoPeca> Elementos { get; set; } = new List<ElementoPeca>();
    /// <summary>Seção b (x), h (y) e comprimento total (cm).</summary>
    public double B { get; set; }
    public double H { get; set; }
    public double L { get; set; }
    /// <summary>Cota da base (m), no referencial dos níveis do modelo.</summary>
    public double CotaBase { get; set; }
    /// <summary>Comprimento embutido no cálice (cm); null = o modelo não informa (fica o do padrão).</summary>
    public double? Embutimento { get; set; }
    /// <summary>Níveis do modelo entre a base e o topo; null = o modelo não informa (ficam os do padrão).</summary>
    public List<NivelPeca> Niveis { get; set; }
    public List<ConsoloPeca> Consolos { get; set; } = new List<ConsoloPeca>();

    public int Quantidade => Math.Max(1, Elementos.Count);
}

/// <summary>Arquivo de troca (JSON) com as peças levantadas de um modelo.</summary>
public sealed class ArquivoPecas
{
    public const string Formato = "DEB.Pecas";
    public const int VersaoAtual = 1;

    /// <summary>Arquivo do modelo de onde vieram as peças.</summary>
    public string Origem { get; set; } = "";
    /// <summary>Data e hora do levantamento (texto ISO).</summary>
    public string Data { get; set; } = "";
    public List<PilarPeca> Pilares { get; set; } = new List<PilarPeca>();
    /// <summary>Observações do levantamento (peças ignoradas etc.).</summary>
    public List<string> Notas { get; set; } = new List<string>();

    // ------------------------------------------------------------------ gravação

    public ValorJson ParaJson()
    {
        var o = ValorJson.NovoObjeto()
            .Definir("formato", Formato).Definir("versao", VersaoAtual)
            .Definir("origem", Origem).Definir("data", Data)
            .Definir("unidades", "cm; cota_base e niveis em m");
        var notas = ValorJson.NovaLista();
        foreach (var n in Notas) notas.Adicionar(ValorJson.De(n));
        o.Definir("notas", notas);
        var pilares = ValorJson.NovaLista();
        foreach (var p in Pilares)
        {
            var jp = ValorJson.NovoObjeto().Definir("marca", p.Marca).Definir("quantidade", p.Quantidade);
            var els = ValorJson.NovaLista();
            foreach (var e in p.Elementos)
                els.Adicionar(ValorJson.NovoObjeto().Definir("id", e.Id).Definir("marca", e.Marca).Definir("giro", e.Giro));
            jp.Definir("elementos", els)
              .Definir("b", R(p.B)).Definir("h", R(p.H)).Definir("l", R(p.L))
              .Definir("cota_base", Math.Round(p.CotaBase, 4))
              .Definir("embutimento", p.Embutimento.HasValue ? ValorJson.De(R(p.Embutimento.Value)) : ValorJson.Nulo);
            if (p.Niveis == null) jp.Definir("niveis", ValorJson.Nulo);
            else
            {
                var nv = ValorJson.NovaLista();
                foreach (var n in p.Niveis) nv.Adicionar(ValorJson.NovoObjeto().Definir("nome", n.Nome).Definir("cota", Math.Round(n.Cota, 4)));
                jp.Definir("niveis", nv);
            }
            var cs = ValorJson.NovaLista();
            foreach (var c in p.Consolos)
                cs.Adicionar(ValorJson.NovoObjeto().Definir("face", c.Face)
                    .Definir("inicio", R(c.Inicio)).Definir("fim", R(c.Fim))
                    .Definir("base", R(c.Base)).Definir("topo", R(c.Topo))
                    .Definir("balanco", R(c.Balanco)).Definir("altura_ponta", R(c.AlturaPonta)));
            jp.Definir("consolos", cs);
            pilares.Adicionar(jp);
        }
        return o.Definir("pilares", pilares);
    }

    public void Gravar(string caminho) => Json.GravarArquivo(caminho, ParaJson());

    // ------------------------------------------------------------------ leitura

    public static ArquivoPecas Ler(string caminho) => DeJson(Json.LerArquivo(caminho));

    /// <summary>Lê o JSON; FormatException com o caminho do campo se algo estiver errado.</summary>
    public static ArquivoPecas DeJson(ValorJson raiz)
    {
        const string c0 = "pecas";
        LeitorJson.Objeto(raiz, c0);
        string formato = LeitorJson.Txt(raiz, "formato", "", c0);
        if (formato != Formato) throw new FormatException($"{c0}.formato: esperado \"{Formato}\" (o arquivo não é um levantamento de peças do DEB).");
        int versao = LeitorJson.Int(raiz, "versao", 0, c0);
        if (versao < 1 || versao > VersaoAtual) throw new FormatException($"{c0}.versao: {versao} não suportada (esta versão lê até {VersaoAtual}).");
        var a = new ArquivoPecas
        {
            Origem = LeitorJson.Txt(raiz, "origem", "", c0),
            Data = LeitorJson.Txt(raiz, "data", "", c0),
            Notas = LeitorJson.ListaTxt(raiz, "notas", new List<string>(), c0),
        };
        var lp = LeitorJson.Lista(raiz, "pilares", c0);
        for (int i = 0; lp != null && i < lp.Quantidade; i++)
        {
            string cp = $"{c0}.pilares[{i}]";
            var jp = LeitorJson.Objeto(lp[i], cp);
            var p = new PilarPeca
            {
                Marca = LeitorJson.Txt(jp, "marca", "", cp),
                B = LeitorJson.Num(jp, "b", 0, cp), H = LeitorJson.Num(jp, "h", 0, cp), L = LeitorJson.Num(jp, "l", 0, cp),
                CotaBase = LeitorJson.Num(jp, "cota_base", 0, cp),
                Embutimento = LeitorJson.Presente(jp, "embutimento") ? LeitorJson.Num(jp, "embutimento", 0, cp) : (double?)null,
            };
            if (p.B <= 0 || p.H <= 0 || p.L <= 0) throw new FormatException($"{cp}: b, h e l têm de ser maiores que zero.");
            var le = LeitorJson.Lista(jp, "elementos", cp);
            for (int k = 0; le != null && k < le.Quantidade; k++)
            {
                var je = LeitorJson.Objeto(le[k], $"{cp}.elementos[{k}]");
                p.Elementos.Add(new ElementoPeca
                {
                    Id = LeitorJson.Txt(je, "id", "", $"{cp}.elementos[{k}]"),
                    Marca = LeitorJson.Txt(je, "marca", "", $"{cp}.elementos[{k}]"),
                    Giro = LeitorJson.Int(je, "giro", 0, $"{cp}.elementos[{k}]"),
                });
            }
            // sem a lista de elementos, vale a quantidade informada
            if (p.Elementos.Count == 0)
                for (int k = LeitorJson.Int(jp, "quantidade", 1, cp); k > 0; k--) p.Elementos.Add(new ElementoPeca());
            var ln = LeitorJson.Lista(jp, "niveis", cp);
            if (ln != null)
            {
                p.Niveis = new List<NivelPeca>();
                for (int k = 0; k < ln.Quantidade; k++)
                {
                    var jn = LeitorJson.Objeto(ln[k], $"{cp}.niveis[{k}]");
                    p.Niveis.Add(new NivelPeca { Nome = LeitorJson.Txt(jn, "nome", "", $"{cp}.niveis[{k}]"), Cota = LeitorJson.Num(jn, "cota", 0, $"{cp}.niveis[{k}]") });
                }
            }
            var lc = LeitorJson.Lista(jp, "consolos", cp);
            for (int k = 0; lc != null && k < lc.Quantidade; k++)
            {
                string cc = $"{cp}.consolos[{k}]";
                var jc = LeitorJson.Objeto(lc[k], cc);
                var c = new ConsoloPeca
                {
                    Face = LeitorJson.Int(jc, "face", 0, cc),
                    Inicio = LeitorJson.Num(jc, "inicio", 0, cc), Fim = LeitorJson.Num(jc, "fim", 0, cc),
                    Base = LeitorJson.Num(jc, "base", 0, cc), Topo = LeitorJson.Num(jc, "topo", 0, cc),
                    Balanco = LeitorJson.Num(jc, "balanco", 0, cc),
                };
                c.AlturaPonta = LeitorJson.Num(jc, "altura_ponta", c.AlturaFace, cc);
                if (c.Face < 1 || c.Face > 4) throw new FormatException($"{cc}.face: use 1 a 4.");
                p.Consolos.Add(c);
            }
            a.Pilares.Add(p);
        }
        return a;
    }

    private static double R(double cm) => Math.Round(cm, 2);

    /// <summary>Nome sugerido do arquivo: "pecas_&lt;modelo&gt;_&lt;data&gt;.json".</summary>
    public static string NomeArquivo(string modelo, DateTime quando) =>
        "pecas_" + string.Concat((Path.GetFileNameWithoutExtension(modelo ?? "") ?? "").Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch)) +
        "_" + quando.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".json";
}
