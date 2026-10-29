using System;
using System.Collections.Generic;
using System.Globalization;
using Armadura.Core.Modelo;

namespace Armadura.Core.Persistencia;

/// <summary>Um item de XData, independente do CAD (o adaptador converte para TypedValue).</summary>
public readonly struct CampoXData
{
    public const short Texto = 1000, Real = 1040, Inteiro16 = 1070, Inteiro32 = 1071, Handle = 1005;

    public short Codigo { get; }
    public object Valor { get; }
    public CampoXData(short codigo, object valor) { Codigo = codigo; Valor = valor; }

    public override string ToString() => $"{Codigo}:{Valor}";
}

public enum TipoParte { Nenhum, Barra, Descricao, Cota, Tabela, Faixa, Corte, Chamada, Rebatida, Detalhamento, Desenho }

/// <summary>Vínculo gravado nas partes: textos da barra, partes da faixa (corte/chamada), rebatidas.</summary>
public sealed class Vinculo
{
    public TipoParte Tipo { get; set; }
    /// <summary>Handle (hex) do dono: a barra (descrição, cota, rebatida) ou a faixa (corte, chamada).</summary>
    public string HandleBarra { get; set; } = "";
    /// <summary>Índice do segmento (cota).</summary>
    public int Indice { get; set; }
    /// <summary>Rebatida: deslocamento barra − rebatida (unidades do desenho).</summary>
    public double Dx { get; set; }
    public double Dy { get; set; }
}

/// <summary>
/// Codifica os dados em pares "chave (1000) + valor" depois da RegApp. Chaves desconhecidas são
/// ignoradas na leitura: versões novas do esquema continuam lendo desenhos antigos.
/// </summary>
public static class CodecDados
{
    public const string RegApp = "ARMADURA_BR";
    private const string KTipo = "TIPO", KVersao = "VER";

    private static string NomeTipo(TipoParte t) => t switch
    {
        TipoParte.Barra => "BARRA",
        TipoParte.Descricao => "DESC",
        TipoParte.Cota => "COTA",
        TipoParte.Tabela => "RA",
        TipoParte.Faixa => "FAIXA",
        TipoParte.Corte => "CORTE",
        TipoParte.Chamada => "CHAMADA",
        TipoParte.Rebatida => "REBATIDA",
        TipoParte.Detalhamento => "DET",
        TipoParte.Desenho => "DESENHO",
        _ => "",
    };

    private static TipoParte TipoDeNome(string s) => s switch
    {
        "BARRA" => TipoParte.Barra,
        "DESC" => TipoParte.Descricao,
        "COTA" => TipoParte.Cota,
        "RA" => TipoParte.Tabela,
        "FAIXA" => TipoParte.Faixa,
        "CORTE" => TipoParte.Corte,
        "CHAMADA" => TipoParte.Chamada,
        "REBATIDA" => TipoParte.Rebatida,
        "DET" => TipoParte.Detalhamento,
        "DESENHO" => TipoParte.Desenho,
        _ => TipoParte.Nenhum,
    };

    /// <summary>Lê só o tipo da parte (rápido, para filtrar).</summary>
    public static TipoParte LerTipo(IReadOnlyList<CampoXData> campos)
    {
        for (int i = 0; i + 1 < campos.Count; i++)
            if (campos[i].Codigo == CampoXData.Texto && (campos[i].Valor as string) == KTipo)
                return TipoDeNome(campos[i + 1].Valor as string);
        return TipoParte.Nenhum;
    }

    // ---------- barra ----------

    public static List<CampoXData> Codificar(DadosArmadura d)
    {
        var l = new List<CampoXData>();
        void S(string k, string v) { l.Add(new CampoXData(CampoXData.Texto, k)); l.Add(new CampoXData(CampoXData.Texto, v ?? "")); }
        void H(string k, string v) { l.Add(new CampoXData(CampoXData.Texto, k)); l.Add(ValorHandle(v)); }
        void I(string k, int v) { l.Add(new CampoXData(CampoXData.Texto, k)); l.Add(new CampoXData(CampoXData.Inteiro32, v)); }
        void R(string k, double v) { l.Add(new CampoXData(CampoXData.Texto, k)); l.Add(new CampoXData(CampoXData.Real, v)); }
        void B(string k, bool v) { l.Add(new CampoXData(CampoXData.Texto, k)); l.Add(new CampoXData(CampoXData.Inteiro16, (short)(v ? 1 : 0))); }

        S(KTipo, NomeTipo(TipoParte.Barra));
        I(KVersao, DadosArmadura.VersaoEsquema);
        S("ID", d.Id.ToString("N"));
        I("N", d.Numero);
        R("BIT", d.Bitola);
        S("ACO", d.Aco);
        I("QTD", d.Quantidade);
        I("REP", d.Repeticoes);
        R("ESP", d.Espacamento);
        B("EST", d.Estribo);
        B("COR", d.Corrido);
        B("VIS", d.Visivel);
        I("CAMPOS", (int)d.Campos);
        R("ESC", d.Escala);
        S("LCK", d.AssinaturaBloqueio);
        B("NOVA", d.Nova);
        S("CAM", d.Camadas);
        S("TRL", d.Trelica);
        S("TELA", d.Tela);
        if (d.EhTela) { B("TDIS", d.TelaDistribuicao); B("TCOT", d.TelaCotas); B("TDIAG", d.TelaTextoDiagonal); }
        if (d.EhTela && d.TelaIdentificada) { B("TID", true); I("TN", d.NumeroTela); }
        foreach (var pd in d.PartesDesenho) H("PD", pd);
        foreach (var c in d.ComprimentosVar) R("CVAR", c);
        H("HDESC", d.HandleDescricao);
        B("DPOS", d.TemPosicaoDescricao);
        foreach (var q in d.QuebrasDescricao) I("QBR", q);
        R("DDX", d.DescricaoDx);
        R("DDY", d.DescricaoDy);
        foreach (var fx in d.Faixas) H("FX", fx);
        foreach (var rb in d.Rebatidas) H("RB", rb);
        I("SEG", d.Segmentos.Count);
        foreach (var s in d.Segmentos)
        {
            I("SF", (int)s.Formato); // "SF" abre um segmento novo
            B("SC", s.Cotar);
            B("SV", s.Var);
            R("SL", s.ComprimentoRegistrado);
            H("SH", s.HandleCota);
            if (s.Sobreposto) B("SO", true); // só quando marcado: não regrava as barras antigas
            if (s.CotaMovida) { B("SM", true); R("SMX", s.CotaDx); R("SMY", s.CotaDy); }
        }
        return l;
    }

    /// <summary>Decodifica uma barra; retorna null se os campos não forem de uma barra.</summary>
    public static DadosArmadura DecodificarBarra(IReadOnlyList<CampoXData> campos)
    {
        if (LerTipo(campos) != TipoParte.Barra) return null;
        var d = new DadosArmadura();
        Segmento seg = null;
        foreach (var (k, v) in Pares(campos))
        {
            switch (k)
            {
                case "ID": if (Guid.TryParse(v as string, out var g)) d.Id = g; break;
                case "N": d.Numero = Int(v); break;
                case "BIT": d.Bitola = Real(v); break;
                case "ACO": d.Aco = v as string ?? "CA-50"; break;
                case "QTD": d.Quantidade = Int(v); break;
                case "REP": d.Repeticoes = Math.Max(1, Int(v)); break;
                case "ESP": d.Espacamento = Real(v); break;
                case "EST": d.Estribo = Int(v) != 0; break;
                case "COR": d.Corrido = Int(v) != 0; break;
                case "VIS": d.Visivel = Int(v) != 0; break;
                case "CAMPOS": d.Campos = (CamposDescricao)Int(v); break;
                case "ESC": d.Escala = Real(v); break;
                case "LCK": d.AssinaturaBloqueio = v as string ?? ""; break;
                case "NOVA": d.Nova = Int(v) != 0; break;
                case "CAM": d.Camadas = v as string ?? ""; break;
                case "TRL": d.Trelica = v as string ?? ""; break;
                case "TELA": d.Tela = v as string ?? ""; break;
                case "TDIS": d.TelaDistribuicao = Int(v) != 0; break;
                case "TCOT": d.TelaCotas = Int(v) != 0; break;
                case "TDIAG": d.TelaTextoDiagonal = Int(v) != 0; break;
                case "TID": d.TelaIdentificada = Int(v) != 0; break;
                case "TN": d.NumeroTela = Int(v); break;
                case "PD": if (v is string pd && pd.Length > 0) d.PartesDesenho.Add(pd); break;
                case "CVAR": d.ComprimentosVar.Add(Real(v)); break;
                case "HDESC": d.HandleDescricao = v as string ?? ""; break;
                case "DPOS": d.TemPosicaoDescricao = Int(v) != 0; break;
                case "QBR": if (!d.QuebrasDescricao.Contains(Int(v))) d.QuebrasDescricao.Add(Int(v)); break;
                case "DDX": d.DescricaoDx = Real(v); break;
                case "DDY": d.DescricaoDy = Real(v); break;
                case "FX": if (v is string fx && fx.Length > 0) d.Faixas.Add(fx); break;
                case "RB": if (v is string rb && rb.Length > 0) d.Rebatidas.Add(rb); break;
                case "SF": seg = new Segmento { Formato = (FormatoSegmento)Int(v) }; d.Segmentos.Add(seg); break;
                case "SC": if (seg != null) seg.Cotar = Int(v) != 0; break;
                case "SV": if (seg != null) seg.Var = Int(v) != 0; break;
                case "SL": if (seg != null) seg.ComprimentoRegistrado = Real(v); break;
                case "SH": if (seg != null) seg.HandleCota = v as string ?? ""; break;
                case "SO": if (seg != null) seg.Sobreposto = Int(v) != 0; break;
                case "SM": if (seg != null) seg.CotaMovida = Int(v) != 0; break;
                case "SMX": if (seg != null) seg.CotaDx = Real(v); break;
                case "SMY": if (seg != null) seg.CotaDy = Real(v); break;
            }
        }
        return d;
    }

    // ---------- textos e tabela ----------

    public static List<CampoXData> Codificar(Vinculo v)
    {
        var l = new List<CampoXData>
        {
            new CampoXData(CampoXData.Texto, KTipo), new CampoXData(CampoXData.Texto, NomeTipo(v.Tipo)),
            new CampoXData(CampoXData.Texto, KVersao), new CampoXData(CampoXData.Inteiro32, DadosArmadura.VersaoEsquema),
            new CampoXData(CampoXData.Texto, "HBAR"), ValorHandle(v.HandleBarra),
            new CampoXData(CampoXData.Texto, "IDX"), new CampoXData(CampoXData.Inteiro32, v.Indice),
        };
        if (v.Tipo == TipoParte.Rebatida)
        {
            l.Add(new CampoXData(CampoXData.Texto, "DX")); l.Add(new CampoXData(CampoXData.Real, v.Dx));
            l.Add(new CampoXData(CampoXData.Texto, "DY")); l.Add(new CampoXData(CampoXData.Real, v.Dy));
        }
        return l;
    }

    public static Vinculo DecodificarVinculo(IReadOnlyList<CampoXData> campos)
    {
        var tipo = LerTipo(campos);
        if (tipo == TipoParte.Nenhum || tipo == TipoParte.Barra) return null;
        var v = new Vinculo { Tipo = tipo };
        foreach (var (k, val) in Pares(campos))
        {
            if (k == "HBAR") v.HandleBarra = val as string ?? "";
            else if (k == "IDX") v.Indice = Int(val);
            else if (k == "DX") v.Dx = Real(val);
            else if (k == "DY") v.Dy = Real(val);
        }
        return v;
    }

    // ---------- faixa ----------

    public static List<CampoXData> Codificar(DadosFaixa f)
    {
        var l = new List<CampoXData>();
        void S(string k, string v) { l.Add(new CampoXData(CampoXData.Texto, k)); l.Add(new CampoXData(CampoXData.Texto, v ?? "")); }
        void H(string k, string v) { l.Add(new CampoXData(CampoXData.Texto, k)); l.Add(ValorHandle(v)); }
        void I(string k, int v) { l.Add(new CampoXData(CampoXData.Texto, k)); l.Add(new CampoXData(CampoXData.Inteiro32, v)); }
        void R(string k, double v) { l.Add(new CampoXData(CampoXData.Texto, k)); l.Add(new CampoXData(CampoXData.Real, v)); }

        S(KTipo, NomeTipo(TipoParte.Faixa));
        I(KVersao, DadosArmadura.VersaoEsquema);
        H("HBAR", f.HandleBarra);
        I("FT", (int)f.Tipo);
        R("ESP", f.Espacamento);
        R("EINI", f.EspacamentoInicial);
        I("QTD", f.Quantidade);
        I("LADO", f.LadoCorte);
        R("P1X", f.P1X); R("P1Y", f.P1Y); R("P2X", f.P2X); R("P2Y", f.P2Y);
        I("PCH", f.PontoChamada);
        R("CX", f.CX); R("CY", f.CY); R("RAIO", f.Raio); R("A1", f.AnguloIni); R("A2", f.AnguloFim);
        foreach (var (x, y) in f.Referencias) { R("RX", x); R("RY", y); }
        foreach (var p in f.Partes) H("PT", p);
        if (f.Escala > 0) R("ESC", f.Escala); // 0 = escala da barra (faixas antigas)
        if (f.OcultarLinhas) I("OCL", 1);
        foreach (var i in f.LinhasApagadas) I("LAP", i);
        foreach (var q in f.Quebras) I("QBR", q);
        if (f.Repeticoes > 1) I("REP", f.Repeticoes);
        // campos do texto: só grava os desligados (faixas antigas mostram tudo)
        if (!f.MostrarQuantidade) I("MQTD", 0);
        if (!f.MostrarPosicao) I("MPOS", 0);
        if (!f.MostrarDiametro) I("MDIA", 0);
        if (!f.MostrarEspacamento) I("MESP", 0);
        return l;
    }

    public static DadosFaixa DecodificarFaixa(IReadOnlyList<CampoXData> campos)
    {
        if (LerTipo(campos) != TipoParte.Faixa) return null;
        var f = new DadosFaixa();
        double? rx = null;
        foreach (var (k, v) in Pares(campos))
        {
            switch (k)
            {
                case "HBAR": f.HandleBarra = v as string ?? ""; break;
                case "FT": f.Tipo = (TipoFaixa)Int(v); break;
                case "ESP": f.Espacamento = Real(v); break;
                case "EINI": f.EspacamentoInicial = Real(v); break;
                case "QTD": f.Quantidade = Int(v); break;
                case "LADO": f.LadoCorte = Int(v) < 0 ? -1 : 1; break;
                case "P1X": f.P1X = Real(v); break;
                case "P1Y": f.P1Y = Real(v); break;
                case "P2X": f.P2X = Real(v); break;
                case "P2Y": f.P2Y = Real(v); break;
                case "PCH": f.PontoChamada = Int(v); break;
                case "CX": f.CX = Real(v); break;
                case "CY": f.CY = Real(v); break;
                case "RAIO": f.Raio = Real(v); break;
                case "A1": f.AnguloIni = Real(v); break;
                case "A2": f.AnguloFim = Real(v); break;
                case "RX": rx = Real(v); break;
                case "RY": if (rx.HasValue) { f.Referencias.Add((rx.Value, Real(v))); rx = null; } break;
                case "PT": if (v is string p && p.Length > 0) f.Partes.Add(p); break;
                case "ESC": f.Escala = Real(v); break;
                case "OCL": f.OcultarLinhas = Int(v) != 0; break;
                case "QBR": if (!f.Quebras.Contains(Int(v))) f.Quebras.Add(Int(v)); break;
                case "LAP": if (!f.LinhasApagadas.Contains(Int(v))) f.LinhasApagadas.Add(Int(v)); break;
                case "REP": f.Repeticoes = Math.Max(1, Int(v)); break;
                case "MQTD": f.MostrarQuantidade = Int(v) != 0; break;
                case "MPOS": f.MostrarPosicao = Int(v) != 0; break;
                case "MDIA": f.MostrarDiametro = Int(v) != 0; break;
                case "MESP": f.MostrarEspacamento = Int(v) != 0; break;
            }
        }
        return f;
    }

    // ---------- detalhamento (no bloco) ----------

    public static List<CampoXData> Codificar(DadosDetalhamento d)
    {
        var l = new List<CampoXData>();
        void S(string k, string v) { l.Add(new CampoXData(CampoXData.Texto, k)); l.Add(new CampoXData(CampoXData.Texto, v ?? "")); }
        void I(string k, int v) { l.Add(new CampoXData(CampoXData.Texto, k)); l.Add(new CampoXData(CampoXData.Inteiro32, v)); }
        void R(string k, double v) { l.Add(new CampoXData(CampoXData.Texto, k)); l.Add(new CampoXData(CampoXData.Real, v)); }
        S(KTipo, NomeTipo(TipoParte.Detalhamento));
        I(KVersao, DadosArmadura.VersaoEsquema);
        S("GRP", d.Grupo); S("PAV", d.Pavimento); S("CLS", d.Classe);
        R("VOL", d.Volume); R("FORMA", d.AreaForma);
        I("SIS", (int)d.Sistema); S("OBS", d.Comentarios); I("NUM", d.Numero);
        return l;
    }

    public static DadosDetalhamento DecodificarDetalhamento(IReadOnlyList<CampoXData> campos)
    {
        if (LerTipo(campos) != TipoParte.Detalhamento) return null;
        var d = new DadosDetalhamento();
        foreach (var (k, v) in Pares(campos))
        {
            switch (k)
            {
                case "GRP": d.Grupo = v as string ?? ""; break;
                case "PAV": d.Pavimento = v as string ?? ""; break;
                case "CLS": d.Classe = v as string ?? ""; break;
                case "VOL": d.Volume = Real(v); break;
                case "FORMA": d.AreaForma = Real(v); break;
                case "SIS": d.Sistema = (SistemaConstrutivo)Int(v); break;
                case "OBS": d.Comentarios = v as string ?? ""; break;
                case "NUM": d.Numero = Int(v); break;
            }
        }
        return d;
    }
    // ---------- tabela da relação do aço ----------

    public static List<CampoXData> Codificar(RelacaoAco.OpcoesTabelaRA o)
    {
        var l = new List<CampoXData>
        {
            new CampoXData(CampoXData.Texto, KTipo), new CampoXData(CampoXData.Texto, NomeTipo(TipoParte.Tabela)),
            new CampoXData(CampoXData.Texto, KVersao), new CampoXData(CampoXData.Inteiro32, DadosArmadura.VersaoEsquema),
            new CampoXData(CampoXData.Texto, "APRES"), new CampoXData(CampoXData.Inteiro32, (int)o.Apresentacao),
            new CampoXData(CampoXData.Texto, "RESUMO"), new CampoXData(CampoXData.Inteiro32, (int)o.Resumo),
            new CampoXData(CampoXData.Texto, "PTOTAL"), new CampoXData(CampoXData.Inteiro16, (short)(o.MostrarPesoTotal ? 1 : 0)),
            new CampoXData(CampoXData.Texto, "ESC"), new CampoXData(CampoXData.Real, o.Escala),
        };
        // RA parcial: nomes dos blocos (o nome sobrevive a cópias entre desenhos; o handle não)
        foreach (var b in o.Blocos) { l.Add(new CampoXData(CampoXData.Texto, "BLK")); l.Add(new CampoXData(CampoXData.Texto, b)); }
        l.Add(new CampoXData(CampoXData.Texto, "DOBRA")); l.Add(new CampoXData(CampoXData.Inteiro16, (short)(o.DetalheDobra ? 1 : 0)));
        l.Add(new CampoXData(CampoXData.Texto, "TX")); l.Add(new CampoXData(CampoXData.Real, o.PosX));
        l.Add(new CampoXData(CampoXData.Texto, "TY")); l.Add(new CampoXData(CampoXData.Real, o.PosY));
        foreach (var p in o.Partes) { l.Add(new CampoXData(CampoXData.Texto, "PD")); l.Add(ValorHandle(p)); }
        l.Add(new CampoXData(CampoXData.Texto, "COLEL")); l.Add(new CampoXData(CampoXData.Inteiro16, (short)o.ColunaElemento));
        l.Add(new CampoXData(CampoXData.Texto, "TITEL")); l.Add(new CampoXData(CampoXData.Inteiro16, (short)(o.NomeNoTitulo ? 1 : 0)));
        return l;
    }

    /// <summary>Opções da tabela; campos ausentes (tabelas antigas) ficam com os valores de <paramref name="padrao"/>.</summary>
    public static RelacaoAco.OpcoesTabelaRA DecodificarTabela(IReadOnlyList<CampoXData> campos, RelacaoAco.OpcoesTabelaRA padrao)
    {
        if (LerTipo(campos) != TipoParte.Tabela) return null;
        var o = new RelacaoAco.OpcoesTabelaRA
        {
            Apresentacao = padrao.Apresentacao, Resumo = padrao.Resumo, MostrarPesoTotal = padrao.MostrarPesoTotal,
            Escala = padrao.Escala,
        };
        foreach (var (k, v) in Pares(campos))
        {
            if (k == "APRES") o.Apresentacao = (RelacaoAco.ApresentacaoRA)Int(v);
            else if (k == "RESUMO") o.Resumo = (RelacaoAco.ApresentacaoResumo)Int(v);
            else if (k == "PTOTAL") o.MostrarPesoTotal = Int(v) != 0;
            else if (k == "ESC") o.Escala = Real(v);
            else if (k == "BLK" && v is string b && b.Length > 0) o.Blocos.Add(b);
            else if (k == "DOBRA") o.DetalheDobra = Int(v) != 0;
            else if (k == "TX") o.PosX = Real(v);
            else if (k == "TY") o.PosY = Real(v);
            else if (k == "PD" && v is string p && p.Length > 0) o.Partes.Add(p);
            else if (k == "COLEL") o.ColunaElemento = (RelacaoAco.ColunaElemento)Int(v);
            else if (k == "TITEL") o.NomeNoTitulo = Int(v) != 0;
        }
        return o;
    }

    // ---------- configuração gravada no desenho (Xrecord no NOD) ----------

    /// <summary>Divide o JSON em pedaços de até 250 caracteres (limite seguro de texto em Xrecord).</summary>
    public static List<string> Fatiar(string texto, int tamanho = 250)
    {
        var r = new List<string>();
        for (int i = 0; i < texto.Length; i += tamanho)
            r.Add(texto.Substring(i, Math.Min(tamanho, texto.Length - i)));
        return r;
    }

    // ---------- auxiliares ----------

    /// <summary>
    /// Referência a outro objeto: código 1005 (o CAD traduz o handle ao copiar, criar bloco, WBLOCK e
    /// INSERT); vazia vira texto (1000 "").
    /// </summary>
    private static CampoXData ValorHandle(string h) =>
        string.IsNullOrEmpty(h) ? new CampoXData(CampoXData.Texto, "") : new CampoXData(CampoXData.Handle, h);

    private static IEnumerable<(string, object)> Pares(IReadOnlyList<CampoXData> campos)
    {
        for (int i = 0; i + 1 < campos.Count; i += 2)
        {
            if (campos[i].Codigo != CampoXData.Texto) { i--; continue; } // dessincronizado: avança 1
            yield return ((string)campos[i].Valor, campos[i + 1].Valor);
        }
    }

    private static int Int(object v) => v switch
    {
        int i => i,
        short s => s,
        long l => (int)l,
        double d => (int)Math.Round(d),
        string s when int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var r) => r,
        _ => 0,
    };

    private static double Real(object v) => v switch
    {
        double d => d,
        int i => i,
        short s => s,
        string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var r) => r,
        _ => 0,
    };
}
