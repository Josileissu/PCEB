using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Armadura.Core.Importacao;

/// <summary>Referência a outro objeto do arquivo (0 = nula).</summary>
public readonly struct QiRef
{
    public QiRef(int numero) { Numero = numero; }
    public int Numero { get; }
    public bool Nula => Numero == 0;
    public override string ToString() => "#" + Numero;
}

/// <summary>Um objeto do .cad: classe, hierarquia e valores dos campos (pelo nome).</summary>
public sealed class QiObjeto
{
    internal QiObjeto(QiDocumento doc, int numero, string classe, IReadOnlyList<string> hierarquia)
    {
        Documento = doc; Numero = numero; Classe = classe; Hierarquia = hierarquia;
    }

    public QiDocumento Documento { get; }
    public int Numero { get; }
    public string Classe { get; }
    /// <summary>Classes da raiz até a própria (ex.: TCadElement, TCadPolygon, TCadRebar).</summary>
    public IReadOnlyList<string> Hierarquia { get; }
    public Dictionary<string, object> Campos { get; } = new Dictionary<string, object>(StringComparer.Ordinal);

    public bool E(string classe) => Hierarquia.Contains(classe);

    public int Int(string campo) => Campos.TryGetValue(campo, out var v) && v is int i ? i : 0;
    public double Dbl(string campo) => Campos.TryGetValue(campo, out var v) && v is double d ? d : 0;
    public bool Bool(string campo) => Campos.TryGetValue(campo, out var v) && v is bool b && b;
    public string Str(string campo) => Campos.TryGetValue(campo, out var v) && v is string s ? s : "";

    /// <summary>Objeto referenciado pelo campo (null se nulo ou ausente).</summary>
    public QiObjeto Obj(string campo) =>
        Campos.TryGetValue(campo, out var v) && v is QiRef r && !r.Nula ? Documento.Obter(r.Numero) : null;

    /// <summary>Objetos de um campo-lista.</summary>
    public IEnumerable<QiObjeto> Lista(string campo) =>
        Campos.TryGetValue(campo, out var v) && v is List<QiRef> l
            ? l.Where(r => !r.Nula).Select(r => Documento.Obter(r.Numero)).Where(o => o != null)
            : Enumerable.Empty<QiObjeto>();

    public override string ToString() => $"#{Numero} {Classe}";
}

/// <summary>Arquivo .cad lido: todos os objetos e as classes do esquema.</summary>
public sealed class QiDocumento
{
    internal readonly Dictionary<int, QiObjeto> Objetos = new Dictionary<int, QiObjeto>();

    public IEnumerable<QiObjeto> Todos => Objetos.Values;
    public int Classes { get; internal set; }
    public QiObjeto Obter(int numero) => Objetos.TryGetValue(numero, out var o) ? o : null;
    public IEnumerable<QiObjeto> DaClasse(string classe) => Objetos.Values.Where(o => o.Classe == classe);

    /// <summary>Raiz do desenho (TRebarDrawing/TDrawing: a que tem a lista "elements" e não é grupo).</summary>
    public QiObjeto Raiz => Objetos.Values.FirstOrDefault(o => o.E("TDrawing"));
}

/// <summary>
/// Leitor do formato .cad do QiArmaduras/AltoQi Builder (estudo em docs/FORMATO_QICAD.md): esquema
/// autodescritivo (classes e campos) seguido dos registros de objeto. Só lê; nunca grava o arquivo.
/// </summary>
public static class LeitorQiCad
{
    private const uint Marcador = 0xABCAABCA; // bytes CA AB CA AB

    private sealed class Campo
    {
        public string Nome = "";
        public int Tipo;
    }

    private sealed class Classe
    {
        public string Nome = "";
        public List<int> Pais = new List<int>();
        public List<Campo>[] Secoes = new List<Campo>[5];
    }

    public static QiDocumento Ler(string caminho) => Ler(File.ReadAllBytes(caminho));

    public static QiDocumento Ler(byte[] dados)
    {
        if (dados.Length < 0x60 || BitConverter.ToUInt32(dados, 0x21) != Marcador)
            throw new InvalidDataException("Não é um arquivo .cad do QiArmaduras (cabeçalho desconhecido).");
        int p = 0x39;
        int nClasses = I32(dados, ref p);
        if (nClasses <= 0 || nClasses > 5000) throw new InvalidDataException("Esquema do .cad inválido.");
        var classes = new Dictionary<int, Classe>();
        for (int c = 0; c < nClasses; c++)
        {
            int id = I32(dados, ref p);
            var cl = new Classe { Nome = Texto(dados, ref p) };
            p += 1; // abstrata
            p += 4; // -1
            int nPais = I32(dados, ref p);
            for (int k = 0; k < nPais; k++) cl.Pais.Add(I32(dados, ref p));
            for (int s = 0; s < 5; s++)
            {
                int n = I32(dados, ref p);
                if (n < 0 || n > 1000) throw new InvalidDataException($"Esquema do .cad inválido (classe {cl.Nome}).");
                cl.Secoes[s] = new List<Campo>(n);
                for (int k = 0; k < n; k++)
                {
                    p += 4; // classe referenciada
                    int tipo = I32(dados, ref p);
                    cl.Secoes[s].Add(new Campo { Tipo = tipo, Nome = Texto(dados, ref p) });
                }
            }
            classes[id] = cl;
        }

        var doc = new QiDocumento { Classes = classes.Count };
        var hierarquias = new Dictionary<int, List<Classe>>();
        List<Classe> Hier(int id)
        {
            if (hierarquias.TryGetValue(id, out var h)) return h;
            h = new List<Classe>();
            if (classes.TryGetValue(id, out var cl))
            {
                foreach (var pai in cl.Pais) h.AddRange(Hier(pai));
                h.Add(cl);
            }
            hierarquias[id] = h;
            return h;
        }

        while (p + 24 <= dados.Length)
        {
            if (BitConverter.ToUInt32(dados, p) != Marcador)
                throw new InvalidDataException($"Registro inesperado na posição 0x{p:X}.");
            p += 4;
            int tamanho = I32(dados, ref p);
            p += 4; // verificação
            int cid = I32(dados, ref p);
            int numero = I32(dados, ref p);
            p += 4;
            int fim = p + tamanho;
            var h = Hier(cid);
            if (h.Count == 0) throw new InvalidDataException($"Classe {cid} fora do esquema.");
            var o = new QiObjeto(doc, numero, h[h.Count - 1].Nome, h.Select(x => x.Nome).ToList());
            foreach (var cl in h)
                foreach (var secao in cl.Secoes)
                    foreach (var campo in secao)
                    {
                        var v = Valor(dados, ref p, campo.Tipo);
                        if (!o.Campos.ContainsKey(campo.Nome)) o.Campos[campo.Nome] = v;
                        else o.Campos[cl.Nome + "." + campo.Nome] = v;
                    }
            if (p != fim) throw new InvalidDataException($"Objeto {o} lido com {p - fim} byte(s) de diferença.");
            doc.Objetos[numero] = o;
        }
        return doc;
    }

    private static object Valor(byte[] d, ref int p, int tipo)
    {
        switch (tipo)
        {
            case -1:
            case -17: return I32(d, ref p);
            case -3: { var v = BitConverter.ToDouble(d, p); p += 8; return v; }
            case -4: return Texto(d, ref p);
            case -10: return d[p++] != 0;
            case -6: case -8: case -15: case -18: return new QiRef(I32(d, ref p));
            case -7:
            case -9:
            {
                int n = Contagem(d, ref p);
                var l = new List<QiRef>(n);
                for (int i = 0; i < n; i++) l.Add(new QiRef(I32(d, ref p)));
                return l;
            }
            case -13:
            {
                int n = Contagem(d, ref p);
                var l = new List<double>(n);
                for (int i = 0; i < n; i++) { l.Add(BitConverter.ToDouble(d, p)); p += 8; }
                return l;
            }
            case -14:
            {
                int n = Contagem(d, ref p);
                var l = new List<string>(n);
                for (int i = 0; i < n; i++) l.Add(Texto(d, ref p));
                return l;
            }
            default: throw new InvalidDataException($"Tipo de campo desconhecido ({tipo}) no .cad.");
        }
    }

    private static int I32(byte[] d, ref int p)
    {
        if (p + 4 > d.Length) throw new InvalidDataException("Arquivo .cad truncado.");
        int v = BitConverter.ToInt32(d, p);
        p += 4;
        return v;
    }

    private static int Contagem(byte[] d, ref int p)
    {
        int n = I32(d, ref p);
        if (n < 0 || n > 10_000_000) throw new InvalidDataException("Lista com tamanho inválido no .cad.");
        return n;
    }

    private static readonly Encoding Latin1 = Encoding.GetEncoding(28591);
    private static readonly Encoding Utf8Estrito = new UTF8Encoding(false, true);

    private static string Texto(byte[] d, ref int p)
    {
        int n = I32(d, ref p);
        if (n < 0 || p + n > d.Length) throw new InvalidDataException("Texto com tamanho inválido no .cad.");
        string s;
        try { s = Utf8Estrito.GetString(d, p, n); }
        catch (DecoderFallbackException) { s = Latin1.GetString(d, p, n); }
        p += n;
        return s;
    }
}
