using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DEB.Core.Analise.Barras;
using DEB.Core.Persistencia;

namespace DEB.Core.Analise.Objetos;

public enum TipoCargaObjeto { Linear, Pontual, Superficie }

/// <summary>De onde veio a carga: lançada no PCEB (janela de análise) ou lida das cargas nativas do Revit.</summary>
public enum OrigemCarga { Pceb, Revit }

/// <summary>
/// Carga aplicada a um objeto da análise (D50, item 2): linear (vigas, kN/m, em todo o vão ou num trecho), pontual (vigas a x cm da
/// ponta I; pilares a z cm da base; kN) ou de superfície (lajes, kN/m², uniforme). Direção global: Z (+ para cima — o peso é negativo
/// no modelo; aqui o valor é positivo para baixo), X ou Y. Caso G (permanente) ou Q (variável).
/// </summary>
public sealed class CargaObjeto
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N").Substring(0, 8);
    /// <summary>Chave do objeto (tipo:id do dono, a mesma dos ajustes).</summary>
    public string Alvo { get; set; } = "";
    public TipoCargaObjeto Tipo { get; set; }
    public string Caso { get; set; } = EstruturaObjetos.CasoG;
    /// <summary>Valor: kN/m (linear), kN (pontual) ou kN/m² (superfície). Na direção Z, positivo para baixo.</summary>
    public double Valor { get; set; }
    /// <summary>Linear: início do trecho (cm a partir da ponta I); pontual: posição (cm).</summary>
    public double Posicao { get; set; }
    /// <summary>Linear: fim do trecho (cm); 0 = até a ponta J.</summary>
    public double Fim { get; set; }
    public string Direcao { get; set; } = "Z";
    public OrigemCarga Origem { get; set; } = OrigemCarga.Pceb;
    public string Descricao { get; set; } = "";
    /// <summary>Revit: id do elemento de carga nativo de onde veio ou que foi criado a partir dela.</summary>
    public string IdRevit { get; set; } = "";

    public Vetor3 Vetor(double v) => Direcao == "X" ? new Vetor3(v, 0, 0) : Direcao == "Y" ? new Vetor3(0, v, 0) : new Vetor3(0, 0, -v);

    public string Texto()
    {
        string un = Tipo == TipoCargaObjeto.Linear ? "kN/m" : Tipo == TipoCargaObjeto.Pontual ? "kN" : "kN/m²";
        string onde = Tipo == TipoCargaObjeto.Pontual ? $" a {Posicao:0} cm" : Tipo == TipoCargaObjeto.Linear && (Posicao > 0 || Fim > 0) ? $" de {Posicao:0} a {(Fim > 0 ? Fim.ToString("0") : "fim")} cm" : "";
        return $"{Caso} · {Valor:0.###} {un}{(Direcao == "Z" ? "" : " em " + Direcao)}{onde}";
    }
}

/// <summary>Aplicação das cargas aos objetos ao gerar o modelo e o JSON gravado no hospedeiro.</summary>
public static class CargasObjetos
{
    /// <summary>Tipos de carga que o objeto aceita.</summary>
    public static IReadOnlyList<TipoCargaObjeto> Aceitas(object o) => o switch
    {
        VigaObjeto _ => new[] { TipoCargaObjeto.Linear, TipoCargaObjeto.Pontual },
        BarraMetalicaObjeto b when !b.Pilar => new[] { TipoCargaObjeto.Linear, TipoCargaObjeto.Pontual },
        BarraMetalicaObjeto _ => new[] { TipoCargaObjeto.Pontual },
        PilarObjeto _ => new[] { TipoCargaObjeto.Pontual },
        LajeObjeto _ => new[] { TipoCargaObjeto.Superficie },
        _ => Array.Empty<TipoCargaObjeto>(),
    };

    /// <summary>Antes de gerar: alturas dos pilares onde precisa haver nó (cargas pontuais).</summary>
    internal static void Preparar(EstruturaObjetos e)
    {
        var obj = AjustesAnalise.Objetos(e);
        foreach (var c in e.Cargas.Where(c => c.Tipo == TipoCargaObjeto.Pontual))
            if (obj.TryGetValue(c.Alvo, out object o))
            {
                if (o is PilarObjeto p && !p.Niveis.Contains(c.Posicao)) p.Niveis.Add(c.Posicao);
                if (o is BarraMetalicaObjeto b) b.PontosInternos.Add(c.Posicao);
            }
    }

    /// <summary>Carga de superfície das lajes: somada às cargas da laje enquanto ela gera (e devolvida depois).</summary>
    internal static (double G, double Q) Superficie(EstruturaObjetos e, LajeObjeto l)
    {
        string k = AjustesAnalise.Chave(l.Dono);
        double g = e.Cargas.Where(c => c.Alvo == k && c.Tipo == TipoCargaObjeto.Superficie && c.Caso == EstruturaObjetos.CasoG).Sum(c => c.Valor);
        double q = e.Cargas.Where(c => c.Alvo == k && c.Tipo == TipoCargaObjeto.Superficie && c.Caso != EstruturaObjetos.CasoG).Sum(c => c.Valor);
        return (g, q);
    }

    /// <summary>Depois de gerar os objetos: cargas lineares e pontuais nas barras e nós dos casos G e Q.</summary>
    internal static void Aplicar(EstruturaObjetos e, ModeloBarras m, List<string> avisos)
    {
        var obj = AjustesAnalise.Objetos(e);
        foreach (var c in e.Cargas)
        {
            if (c.Tipo == TipoCargaObjeto.Superficie) continue;
            if (!obj.TryGetValue(c.Alvo, out object o)) { avisos.Add($"Carga {c.Texto()}: o objeto {c.Alvo} não está no modelo."); continue; }
            CasoCarga caso = m.Caso(c.Caso == EstruturaObjetos.CasoG ? EstruturaObjetos.CasoG : EstruturaObjetos.CasoQ);
            List<(double T, int No)> nos; List<BarraPortico> barras;
            switch (o)
            {
                case VigaObjeto v: nos = v.NosEixo; barras = v.Barras; break;
                case BarraMetalicaObjeto b: nos = b.NosEixo; barras = b.Barras; break;
                case PilarObjeto p:
                {
                    var f = c.Vetor(c.Valor);
                    caso.Nodais.Add((p.NoNoNivel(c.Posicao), new[] { f.X, f.Y, f.Z, 0, 0, 0 }));
                    continue;
                }
                default: avisos.Add($"Carga {c.Texto()}: {c.Alvo} não recebe esse tipo de carga."); continue;
            }
            if (barras.Count == 0) continue;
            double L = nos[nos.Count - 1].T;
            if (c.Tipo == TipoCargaObjeto.Linear)
            {
                double a = Math.Max(0, c.Posicao), b2 = c.Fim > 0 ? Math.Min(L, c.Fim) : L;
                for (int k = 0; k < barras.Count; k++)
                {
                    double t0 = nos[k].T, t1 = nos[k + 1].T;
                    if (t1 <= a + 1e-6 || t0 >= b2 - 1e-6) continue;
                    if (a > t0 + 1e-6 || b2 < t1 - 1e-6)
                    {
                        // trecho parcial na barra: força equivalente concentrada no centro do trecho (a malha passa pelos pontos internos)
                        double x0 = Math.Max(a, t0), x1 = Math.Min(b2, t1);
                        caso.Pontuais.Add(new CargaPontualBarra { Barra = barras[k].Id, A = (x0 + x1) / 2 - t0, F = c.Vetor(c.Valor / 100 * (x1 - x0)) });
                    }
                    else caso.Distribuidas.Add(new CargaDistribuida { Barra = barras[k].Id, Q = c.Vetor(c.Valor / 100) });
                }
            }
            else
            {
                double x = Math.Max(0, Math.Min(L, c.Posicao));
                int k = Math.Min(Math.Max(0, nos.FindLastIndex(n => n.T <= x)), barras.Count - 1);
                caso.Pontuais.Add(new CargaPontualBarra { Barra = barras[k].Id, A = x - nos[k].T, F = c.Vetor(c.Valor) });
            }
        }
    }

    public static string ParaJson(IEnumerable<CargaObjeto> l)
    {
        var lista = ValorJson.NovaLista();
        foreach (var c in l.Where(x => x.Origem == OrigemCarga.Pceb))
        {
            var o = ValorJson.NovoObjeto(); lista.Adicionar(o);
            o.Definir("id", c.Id); o.Definir("alvo", c.Alvo); o.Definir("tipo", c.Tipo.ToString()); o.Definir("caso", c.Caso); o.Definir("valor", c.Valor);
            o.Definir("posicao", c.Posicao); o.Definir("fim", c.Fim); o.Definir("direcao", c.Direcao); o.Definir("descricao", c.Descricao); o.Definir("idRevit", c.IdRevit);
        }
        return Json.Gravar(lista, indentado: false);
    }

    public static List<CargaObjeto> DeJson(string json)
    {
        var r = new List<CargaObjeto>();
        if (string.IsNullOrWhiteSpace(json) || !Json.TentarLer(json, out ValorJson l, out _) || l == null || !l.EhLista) return r;
        foreach (var o in l.Itens)
        {
            if (!Enum.TryParse(o["tipo"]?.Texto ?? "", out TipoCargaObjeto t)) continue;
            r.Add(new CargaObjeto
            {
                Id = o["id"]?.Texto ?? Guid.NewGuid().ToString("N").Substring(0, 8), Alvo = o["alvo"]?.Texto ?? "", Tipo = t, Caso = o["caso"]?.Texto ?? "G",
                Valor = o["valor"]?.Numero ?? 0, Posicao = o["posicao"]?.Numero ?? 0, Fim = o["fim"]?.Numero ?? 0, Direcao = o["direcao"]?.Texto ?? "Z",
                Descricao = o["descricao"]?.Texto ?? "", IdRevit = o["idRevit"]?.Texto ?? "", Origem = OrigemCarga.Pceb,
            });
        }
        return r;
    }
}
