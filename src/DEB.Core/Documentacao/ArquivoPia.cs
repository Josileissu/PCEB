using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace DEB.Core.Documentacao;

/// <summary>
/// Arquivos "PIA" da Autodesk (.ctb, .stb, .pc3, .pmp): cabeçalho ASCII <c>PIAFILEVERSION_2.0,&lt;tipo&gt;,compress\r\npmzlibcodec</c>,
/// 3 inteiros de 32 bits little-endian (Adler-32 dos bytes COMPRIMIDOS, tamanho descomprimido, tamanho comprimido) e o fluxo zlib.
/// Dentro, texto com "\n", blocos <c>chave{ … }</c> (um espaço de recuo por nível) e pares <c>chave=valor</c>; strings abrem
/// com aspas e vão até o fim da linha; o texto pode terminar em "\0". Números não exatos às vezes levam um código entre
/// parênteses (ex.: <c>1121.3000488 (    0#.%D4"2?@)</c>): 6 bits por caractere (ASCII − 32) com os 8 bytes do double em
/// little-endian e 16 bits de conferência não decifrados. O código é opcional: valores gravados só em decimal são aceitos
/// (conferido em 17/11/2026 no console do DWG TrueView 2022: folha nova "1000.3 × 707.15" listada e plotada).
/// O GstarCAD 2021 usa outro formato (binário) nos .pc3/.pmp dele.
/// </summary>
public sealed class ArquivoPia
{
    public const string Marca = "pmzlibcodec";

    /// <summary>Cabeçalho até "pmzlibcodec" (inclusive), mantido como estava.</summary>
    public byte[] Cabecalho { get; set; }
    public NoPia Raiz { get; set; } = new NoPia("");
    /// <summary>O texto terminava em "\0".</summary>
    public bool TerminaEmNulo { get; set; } = true;
    /// <summary>Formato 3.0 do AutoCAD 2025+ (JSON sem compressão, <see cref="PiaJson"/>); false = 2.0 (zlib).</summary>
    public bool Json { get; set; }
    /// <summary>JSON: os pares de fora de "data" ("subver", "version"), devolvidos iguais.</summary>
    public List<NoPia> EnvelopeJson { get; } = new List<NoPia>();

    /// <summary>Formato 3.0 (JSON)?</summary>
    public static bool EhJson(byte[] arquivo) =>
        arquivo != null && arquivo.Length > PiaJson.Versao.Length && Encoding.ASCII.GetString(arquivo, 0, PiaJson.Versao.Length) == PiaJson.Versao;

    /// <summary>É um arquivo PIA compactado (a lista de folhas de um .pc3 do GstarCAD, por exemplo, não é)?</summary>
    public static bool EhPia(byte[] arquivo) =>
        arquivo != null && arquivo.Length > 60 && Encoding.ASCII.GetString(arquivo, 0, 14) == "PIAFILEVERSION";

    /// <summary>Texto descomprimido (confere cabeçalho, Adler-32 e tamanhos; lança InvalidDataException).</summary>
    public static byte[] Descompactar(byte[] arquivo, out byte[] cabecalho)
    {
        int i = Indice(arquivo);
        if (i < 0 || !EhPia(arquivo)) throw new InvalidDataException("não é um arquivo PIA compactado (.ctb/.stb/.pc3/.pmp)");
        cabecalho = arquivo.Take(i).ToArray();
        if (arquivo.Length < i + 12) throw new InvalidDataException("arquivo truncado");
        uint adler = BitConverter.ToUInt32(arquivo, i), tamTexto = BitConverter.ToUInt32(arquivo, i + 4), tamZ = BitConverter.ToUInt32(arquivo, i + 8);
        int ini = i + 12;
        if (arquivo.Length - ini != tamZ) throw new InvalidDataException($"tamanho comprimido {tamZ} ≠ {arquivo.Length - ini}");
        if (Zlib.Adler32(arquivo, ini, (int)tamZ) != adler) throw new InvalidDataException("Adler-32 do cabeçalho não confere");
        var texto = Zlib.Descomprimir(arquivo, ini, (int)tamZ);
        if (texto.Length != tamTexto) throw new InvalidDataException($"tamanho descomprimido {tamTexto} ≠ {texto.Length}");
        return texto;
    }

    /// <summary>Cabeçalho + (Adler-32 do zlib, tamanho descomprimido, tamanho comprimido) + zlib.</summary>
    public static byte[] Compactar(byte[] cabecalho, byte[] texto)
    {
        var z = Zlib.Comprimir(texto);
        using var ms = new MemoryStream();
        ms.Write(cabecalho, 0, cabecalho.Length);
        void I(uint v) { ms.WriteByte((byte)v); ms.WriteByte((byte)(v >> 8)); ms.WriteByte((byte)(v >> 16)); ms.WriteByte((byte)(v >> 24)); }
        I(Zlib.Adler32(z, 0, z.Length));
        I((uint)texto.Length);
        I((uint)z.Length);
        ms.Write(z, 0, z.Length);
        return ms.ToArray();
    }

    private static int Indice(byte[] a)
    {
        var m = Encoding.ASCII.GetBytes(Marca);
        for (int i = 0; a != null && i + m.Length <= a.Length && i < 256; i++)
        {
            int k = 0;
            while (k < m.Length && a[i + k] == m[k]) k++;
            if (k == m.Length) return i + m.Length;
        }
        return -1;
    }

    // o texto é ASCII/ANSI; Latin-1 preserva qualquer byte na ida e volta
    private static readonly Encoding Texto8 = Encoding.GetEncoding("ISO-8859-1");

    public static ArquivoPia Ler(byte[] arquivo)
    {
        if (EhJson(arquivo))
        {
            // cabeçalho = a 1ª linha com o "\r\n"; o resto é JSON em UTF-8
            int fim = Array.IndexOf(arquivo, (byte)'\n');
            if (fim < 0) throw new InvalidDataException("arquivo PIA 3.0 sem o fim da linha do cabeçalho");
            var j = new ArquivoPia { Cabecalho = arquivo.Take(fim + 1).ToArray(), Json = true, TerminaEmNulo = false };
            j.Raiz = PiaJson.Ler(new UTF8Encoding(false).GetString(arquivo, fim + 1, arquivo.Length - fim - 1), j.EnvelopeJson);
            return j;
        }
        var texto = Texto8.GetString(Descompactar(arquivo, out var cab));
        var a = new ArquivoPia { Cabecalho = cab, TerminaEmNulo = texto.EndsWith("\0", StringComparison.Ordinal) };
        a.Raiz = NoPia.Analisar(a.TerminaEmNulo ? texto.Substring(0, texto.Length - 1) : texto);
        return a;
    }

    public string Texto() => Json ? PiaJson.Escrever(Raiz, EnvelopeJson) : Raiz.Escrever() + (TerminaEmNulo ? "\0" : "");

    public byte[] Gravar() =>
        Json ? Cabecalho.Concat(new UTF8Encoding(false).GetBytes(Texto())).ToArray() : Compactar(Cabecalho, Texto8.GetBytes(Texto()));

    /// <summary>Passa um arquivo novo para o formato 3.0 (JSON), o do .pc3 de AutoCAD 2025+ a que ele vai ser ligado.</summary>
    public void ParaJson()
    {
        if (Json) return;
        Json = true;
        TerminaEmNulo = false;
        Cabecalho = Encoding.ASCII.GetBytes(PiaJson.Versao + "\r\n");
        EnvelopeJson.Clear();
        EnvelopeJson.Add(new NoPia("subver", "\"json"));
        EnvelopeJson.Add(new NoPia("version", "0.01"));
    }

    /// <summary>Cabeçalho de um .pc3/.pmp.</summary>
    public static byte[] CabecalhoPc3 => Encoding.ASCII.GetBytes("PIAFILEVERSION_2.0,PC3VER1,compress\r\npmzlibcodec");
}

/// <summary>Nó do texto PIA: bloco (<see cref="Filhos"/>) ou par chave=valor (<see cref="Valor"/>).</summary>
public sealed class NoPia
{
    public string Chave { get; set; }
    /// <summary>null = bloco.</summary>
    public string Valor { get; set; }
    public List<NoPia> Filhos { get; } = new List<NoPia>();
    public bool Bloco => Valor == null;

    public NoPia(string chave, string valor = null) { Chave = chave; Valor = valor; }

    /// <summary>Filho (bloco ou par) pela chave; null se não houver.</summary>
    public NoPia this[string chave] => Filhos.FirstOrDefault(f => f.Chave == chave);

    /// <summary>Bloco filho (cria no fim, se não houver).</summary>
    public NoPia BlocoOuNovo(string chave)
    {
        var n = this[chave];
        if (n == null) { n = new NoPia(chave); Filhos.Add(n); }
        return n;
    }

    /// <summary>Valor de um par (null se não houver).</summary>
    public string Ler(string chave) => this[chave]?.Valor;

    /// <summary>Texto de um par string (sem as aspas de abertura).</summary>
    public string LerTexto(string chave)
    {
        var v = Ler(chave);
        return v == null ? null : v.StartsWith("\"", StringComparison.Ordinal) ? v.Substring(1) : v;
    }

    public void Definir(string chave, string valor)
    {
        var n = this[chave];
        if (n == null) Filhos.Add(new NoPia(chave, valor)); else n.Valor = valor;
    }

    public void DefinirTexto(string chave, string texto) => Definir(chave, "\"" + (texto ?? "").Replace("\n", " ").Replace("\r", " "));

    public static NoPia Analisar(string texto)
    {
        var raiz = new NoPia("");
        var pilha = new Stack<NoPia>();
        pilha.Push(raiz);
        foreach (var bruta in texto.Split('\n'))
        {
            var l = bruta.TrimStart(' ');
            if (l.Length == 0) continue;
            if (l == "}") { if (pilha.Count > 1) pilha.Pop(); continue; }
            int ig = l.IndexOf('=');
            if (ig < 0 && l.EndsWith("{", StringComparison.Ordinal))
            {
                var n = new NoPia(l.Substring(0, l.Length - 1));
                pilha.Peek().Filhos.Add(n);
                pilha.Push(n);
                continue;
            }
            if (ig < 0) throw new InvalidDataException("linha inesperada no arquivo PIA: " + l);
            pilha.Peek().Filhos.Add(new NoPia(l.Substring(0, ig), l.Substring(ig + 1)));
        }
        return raiz;
    }

    public string Escrever()
    {
        var sb = new StringBuilder();
        void W(NoPia n, int nivel)
        {
            foreach (var f in n.Filhos)
            {
                var rec = new string(' ', nivel);
                if (f.Bloco) { sb.Append(rec).Append(f.Chave).Append("{\n"); W(f, nivel + 1); sb.Append(rec).Append("}\n"); }
                else sb.Append(rec).Append(f.Chave).Append('=').Append(f.Valor).Append('\n');
            }
        }
        W(this, 0);
        return sb.ToString();
    }
}
