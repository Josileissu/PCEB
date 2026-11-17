using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace DEB.Core.Documentacao;

/// <summary>
/// Tabela de penas padrão do PCEB ("PCEB.ctb", pedido do usuário de 17/11/2026): cópia das propriedades do ctb que o escritório
/// usa nas pranchas, gravada pelo ARMPDFLOTE na pasta de estilos de plotagem do CAD quando o ctb escolhido não existe nesse
/// computador. Regra: tudo sai PRETO, menos as exceções de <see cref="Excecoes"/>; espessura, tipo de linha e demais campos são
/// os mesmos nas 255 cores.
///
/// Formato do .ctb (conferido em 17/11 no arquivo do escritório e nos ctb do GstarCAD 2021; igual ao .pmp, ver
/// docs/CONVENCOES.md): cabeçalho ASCII <c>PIAFILEVERSION_2.0,CTBVER1,compress\r\npmzlibcodec</c> (48 bytes), 3 inteiros de 32 bits
/// little-endian — Adler-32 dos bytes COMPRIMIDOS (o fluxo zlib inteiro), tamanho descomprimido e tamanho comprimido — e o fluxo
/// zlib (0x78 0xDA). Dentro, texto com quebra "\n" e terminado em "\0"; strings abrem com aspas e vão até o fim da linha
/// (<c>name="Color_1</c>, sem aspas de fechamento).
/// </summary>
public static class EstiloPlotagemPadrao
{
    public const string NomeArquivo = "PCEB.ctb";
    /// <summary>Descrição gravada dentro do ctb (ASCII: o texto do ctb não tem codificação declarada).</summary>
    public const string DescricaoPadrao = "PCEB - estilo padrao de plotagem";
    public const string Cabecalho = "PIAFILEVERSION_2.0,CTBVER1,compress\r\npmzlibcodec";

    /// <summary>
    /// SHA-256 do texto descomprimido do ctb original do escritório (que tem a descrição vazia). <see cref="Texto"/> com descrição
    /// vazia tem de dar exatamente este texto (teste do núcleo); o arquivo original não vai para o repositório.
    /// </summary>
    public const string Sha256Original = "04fdf0275e630f84cf6be303001cf01892624cea13256ea84448563342671a9f";

    /// <summary>
    /// Cor: no ctb a cor vem como inteiro de 32 bits 0xC3RRGGBB (0xC3 = cor RGB atribuída). Política (<c>color_policy</c>, bits):
    /// 1 = pontilhar (dither), 2 = converter para cinza, 4 = usar a cor atribuída (sem o 4 vale a cor do objeto).
    /// </summary>
    public sealed class Pena
    {
        /// <summary>Cor ACI (1 a 255).</summary>
        public int Aci { get; }
        public int Rgb { get; }
        public int Politica { get; }
        public string Obs { get; }
        public Pena(int aci, int rgb, int politica, string obs) { Aci = aci; Rgb = rgb; Politica = politica; Obs = obs; }
        /// <summary>Valor gravado em <c>color=</c> e <c>mode_color=</c>.</summary>
        public int ValorCor => unchecked((int)(0xC3000000u | (uint)(Rgb & 0xFFFFFF)));
    }

    /// <summary>Todas as cores: preto (0x000000) e política 5 (pontilhar + cor atribuída).</summary>
    public const int RgbPadrao = 0x000000, PoliticaPadrao = 5;

    /// <summary>Cores que NÃO saem pretas (as demais 244 saem pretas).</summary>
    public static readonly IReadOnlyList<Pena> Excecoes = new[]
    {
        new Pena(1, 0xFF0000, 5, "vermelho"),
        new Pena(4, 0x00FFFF, 5, "ciano"),
        new Pena(5, 0x0000FF, 5, "azul"),
        new Pena(10, 0xFF0000, 5, "vermelho"),
        new Pena(20, 0xFF0000, 5, "vermelho"),
        new Pena(250, 0xFFFFFF, 3, "cor do objeto em tons de cinza"),
        new Pena(251, 0xFFFFFF, 1, "cor do objeto"),
        new Pena(252, 0xFFFFFF, 1, "cor do objeto"),
        new Pena(253, 0xFFFFFF, 1, "cor do objeto"),
        new Pena(254, 0xDCDCDC, 5, "cinza claro"),
        new Pena(255, 0xFFFFFF, 1, "cor do objeto"),
    };

    /// <summary>Pena da cor ACI <paramref name="aci"/> (1 a 255).</summary>
    public static Pena PenaDa(int aci) => Excecoes.FirstOrDefault(p => p.Aci == aci) ?? new Pena(aci, RgbPadrao, PoliticaPadrao, "preto");

    /// <summary>Campos iguais em todas as cores (na ordem do arquivo, depois de color/mode_color/color_policy).</summary>
    private static readonly (string Campo, string Valor)[] CamposComuns =
    {
        ("physical_pen_number", "0"), ("virtual_pen_number", "0"), ("screen", "100"), ("linepattern_size", "0.5"),
        ("linetype", "31"),            // 31 = tipo de linha do objeto
        ("adaptive_linetype", "TRUE"),
        ("lineweight", "0"),           // índice na custom_lineweight_table (como no original)
        ("fill_style", "73"), ("end_style", "4"), ("join_style", "5"),
    };

    /// <summary>Espessuras (mm) da custom_lineweight_table, na ordem dos índices.</summary>
    public static readonly IReadOnlyList<string> Espessuras = new[]
    {
        "0.0", "0.05", "0.09", "0.1", "0.13", "0.15", "0.18", "0.2", "0.25", "0.3", "0.35", "0.4", "0.45", "0.5", "0.53", "0.6",
        "0.65", "0.7", "0.8", "0.9", "1.0", "1.06", "1.2", "1.4", "1.58", "2.0", "2.11",
    };

    /// <summary>Texto (descomprimido) do ctb, terminado em "\0".</summary>
    public static string Texto(string descricao = DescricaoPadrao)
    {
        var d = (descricao ?? "").Replace("\r", " ").Replace("\n", " ");
        var sb = new StringBuilder();
        void L(string s) => sb.Append(s).Append('\n');
        L("description=\"" + d);
        L("aci_table_available=TRUE");
        L("scale_factor=1.0");
        L("apply_factor=FALSE");
        L("custom_lineweight_display_units=0");
        L("aci_table{");
        for (int i = 0; i < 255; i++) L($" {i}=\"Color_{i + 1}");
        L("}");
        L("plot_style{");
        for (int i = 0; i < 255; i++)
        {
            var p = PenaDa(i + 1);
            var cor = p.ValorCor.ToString(CultureInfo.InvariantCulture);
            L($" {i}{{");
            L($"  name=\"Color_{i + 1}");
            L($"  localized_name=\"Color_{i + 1}");
            L("  description=\"");
            L("  color=" + cor);
            L("  mode_color=" + cor);
            L("  color_policy=" + p.Politica.ToString(CultureInfo.InvariantCulture));
            foreach (var (c, v) in CamposComuns) L($"  {c}={v}");
            L(" }");
        }
        L("}");
        L("custom_lineweight_table{");
        for (int i = 0; i < Espessuras.Count; i++) L($" {i}={Espessuras[i]}");
        L("}");
        sb.Append('\0');
        return sb.ToString();
    }

    /// <summary>Bytes do arquivo PCEB.ctb.</summary>
    public static byte[] Gerar(string descricao = DescricaoPadrao) => Empacotar(Encoding.ASCII.GetBytes(Texto(descricao)));

    /// <summary>Cabeçalho + (Adler-32 do zlib, tamanho descomprimido, tamanho comprimido) + zlib.</summary>
    public static byte[] Empacotar(byte[] texto)
    {
        var z = Zlib.Comprimir(texto);
        using var ms = new MemoryStream();
        var cab = Encoding.ASCII.GetBytes(Cabecalho);
        ms.Write(cab, 0, cab.Length);
        void I(uint v) { ms.WriteByte((byte)v); ms.WriteByte((byte)(v >> 8)); ms.WriteByte((byte)(v >> 16)); ms.WriteByte((byte)(v >> 24)); }
        I(Zlib.Adler32(z, 0, z.Length));
        I((uint)texto.Length);
        I((uint)z.Length);
        ms.Write(z, 0, z.Length);
        return ms.ToArray();
    }

    /// <summary>Texto descomprimido de um .ctb/.stb/.pc3 (confere cabeçalho, Adler-32 e tamanhos; lança InvalidDataException).</summary>
    public static byte[] Ler(byte[] arquivo)
    {
        var marca = Encoding.ASCII.GetBytes("pmzlibcodec");
        int i = IndexOf(arquivo, marca);
        if (i < 0 || !Encoding.ASCII.GetString(arquivo, 0, Math.Min(arquivo.Length, 14)).StartsWith("PIAFILEVERSION", StringComparison.Ordinal))
            throw new InvalidDataException("não é um arquivo PIA compactado (.ctb/.stb/.pc3)");
        i += marca.Length;
        if (arquivo.Length < i + 12) throw new InvalidDataException("arquivo truncado");
        uint adler = BitConverter.ToUInt32(arquivo, i), tamTexto = BitConverter.ToUInt32(arquivo, i + 4), tamZ = BitConverter.ToUInt32(arquivo, i + 8);
        int ini = i + 12;
        if (arquivo.Length - ini != tamZ) throw new InvalidDataException($"tamanho comprimido {tamZ} ≠ {arquivo.Length - ini}");
        if (Zlib.Adler32(arquivo, ini, (int)tamZ) != adler) throw new InvalidDataException("Adler-32 do cabeçalho não confere");
        var texto = Zlib.Descomprimir(arquivo, ini, (int)tamZ);
        if (texto.Length != tamTexto) throw new InvalidDataException($"tamanho descomprimido {tamTexto} ≠ {texto.Length}");
        return texto;
    }

    private static int IndexOf(byte[] a, byte[] m)
    {
        for (int i = 0; i + m.Length <= a.Length && i < 256; i++)
        {
            int k = 0;
            while (k < m.Length && a[i + k] == m[k]) k++;
            if (k == m.Length) return i;
        }
        return -1;
    }

    /// <summary>
    /// Grava PCEB.ctb na pasta de estilos de plotagem se ele ainda não existir (um PCEB.ctb existente nunca é sobrescrito: o
    /// usuário pode tê-lo editado). Devolve o caminho e se foi criado agora.
    /// </summary>
    public static (string Caminho, bool Criado) GarantirNaPasta(string pasta)
    {
        if (string.IsNullOrWhiteSpace(pasta)) throw new ArgumentException("pasta de estilos de plotagem não informada");
        var caminho = Path.Combine(pasta, NomeArquivo);
        if (File.Exists(caminho)) return (caminho, false);
        Directory.CreateDirectory(pasta);
        var tmp = caminho + ".tmp";
        File.WriteAllBytes(tmp, Gerar());
        try { File.Move(tmp, caminho); }
        catch (IOException) when (File.Exists(caminho)) { File.Delete(tmp); return (caminho, false); } // outro CAD gravou antes
        return (caminho, true);
    }

    /// <summary>
    /// Estilo de plotagem de uma folha: o escolhido na janela (<paramref name="configurado"/>; vazio = o do page setup do layout,
    /// <paramref name="doLayout"/>). Se ele não existir na lista do CAD (<paramref name="disponiveis"/>) ou estiver vazio, vale
    /// PCEB.ctb, com o aviso para a janela/relatório. Lista null = não deu para ler: confia no nome pedido.
    /// </summary>
    public static (string Estilo, string Aviso) Escolher(string configurado, string doLayout, IEnumerable<string> disponiveis)
    {
        var pedido = !string.IsNullOrWhiteSpace(configurado) ? configurado.Trim() : (doLayout ?? "").Trim();
        if (pedido.Length == 0) return (NomeArquivo, "folha sem estilo de plotagem; usado " + NomeArquivo);
        if (string.Equals(pedido, NomeArquivo, StringComparison.OrdinalIgnoreCase)) return (NomeArquivo, null);
        if (disponiveis == null) return (pedido, null);
        var achado = disponiveis.FirstOrDefault(d => string.Equals(d, pedido, StringComparison.OrdinalIgnoreCase)
                                                     || string.Equals(Path.GetFileName(d ?? ""), pedido, StringComparison.OrdinalIgnoreCase));
        return achado != null ? (achado, null) : (NomeArquivo, $"ctb \"{pedido}\" não encontrado neste computador; usado {NomeArquivo}");
    }
}

/// <summary>zlib (RFC 1950) sobre o DeflateStream do .NET (que só faz o deflate cru): cabeçalho 0x78 0xDA e Adler-32 no fim.</summary>
public static class Zlib
{
    public static uint Adler32(byte[] dados, int ini, int n)
    {
        const uint Mod = 65521;
        uint a = 1, b = 0;
        int fim = ini + n;
        while (ini < fim)
        {
            int bloco = Math.Min(5552, fim - ini);
            for (int k = 0; k < bloco; k++) { a += dados[ini++]; b += a; }
            a %= Mod; b %= Mod;
        }
        return (b << 16) | a;
    }

    public static byte[] Comprimir(byte[] dados)
    {
        using var ms = new MemoryStream();
        ms.WriteByte(0x78); ms.WriteByte(0xDA);
        using (var ds = new DeflateStream(ms, CompressionLevel.Optimal, true)) ds.Write(dados, 0, dados.Length);
        uint ad = Adler32(dados, 0, dados.Length);
        ms.WriteByte((byte)(ad >> 24)); ms.WriteByte((byte)(ad >> 16)); ms.WriteByte((byte)(ad >> 8)); ms.WriteByte((byte)ad);
        return ms.ToArray();
    }

    public static byte[] Descomprimir(byte[] z, int ini, int n)
    {
        if (n < 6 || (z[ini] & 0x0F) != 8 || ((z[ini] << 8) | z[ini + 1]) % 31 != 0) throw new InvalidDataException("cabeçalho zlib inválido");
        using var entrada = new MemoryStream(z, ini + 2, n - 6);
        using var ds = new DeflateStream(entrada, CompressionMode.Decompress);
        using var saida = new MemoryStream();
        ds.CopyTo(saida);
        var res = saida.ToArray();
        int p = ini + n - 4;
        uint esperado = ((uint)z[p] << 24) | ((uint)z[p + 1] << 16) | ((uint)z[p + 2] << 8) | z[p + 3];
        if (Adler32(res, 0, res.Length) != esperado) throw new InvalidDataException("Adler-32 do zlib não confere");
        return res;
    }

    public static byte[] Descomprimir(byte[] z) => Descomprimir(z, 0, z.Length);
}
