using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace DEB.Core.Documentacao;

/// <summary>
/// Formato de folha de um formulário do Windows da impressora de PDF (ARMPDFLOTE). <see cref="Nome"/> é o nome LOCAL do
/// formulário (o que o CAD devolve em GetLocaleMediaName); o nome canônico (UserNNNNN) muda quando o formulário é recriado e
/// por isso nunca é guardado.
/// </summary>
[DataContract]
public sealed class FormatoFolha
{
    [DataMember(Order = 1)] public string Nome { get; set; }
    /// <summary>Largura do formulário em mm (como foi criado: paisagem nos grandes, retrato no A4).</summary>
    [DataMember(Order = 2)] public double Largura { get; set; }
    [DataMember(Order = 3)] public double Altura { get; set; }

    public FormatoFolha() { }
    public FormatoFolha(string nome, double largura, double altura) { Nome = nome; Largura = largura; Altura = altura; }

    public double Maior => Math.Max(Largura, Altura);
    public double Menor => Math.Min(Largura, Altura);
    public bool Paisagem => Largura >= Altura;

    public override string ToString() => Nome;
}

/// <summary>Resultado do reconhecimento: formato (null = não reconhecido), diferença em mm e fator de unidade aplicado.</summary>
public sealed class FormatoReconhecido
{
    public FormatoFolha Formato { get; set; }
    /// <summary>Maior diferença entre as medidas do desenho (já em mm) e as do formato.</summary>
    public double Diferenca { get; set; }
    /// <summary>mm por unidade do papel usado (1 = layout em mm; 10 = em cm…).</summary>
    public double Fator { get; set; } = 1;
}

/// <summary>Reconhecimento do formato da folha pela medida da moldura/carimbo e nomes dos PDFs do lote.</summary>
public static class FormatosFolha
{
    /// <summary>Formulários do Windows criados no escritório para a impressora PDF24 (todos sem margem; paisagem, exceto A4).</summary>
    public static List<FormatoFolha> Padrao() => new List<FormatoFolha>
    {
        new FormatoFolha("A4 (ISO A4)", 210, 297),
        new FormatoFolha("A3 (ISO A3)", 420, 295),
        new FormatoFolha("A2 (ISO A2)", 594, 420),
        new FormatoFolha("A1 (ISO A1)", 841, 594),
        new FormatoFolha("A1+", 911.1, 594),
        new FormatoFolha("A1++", 1121.3, 594),
        new FormatoFolha("A0", 1189, 841),
        new FormatoFolha("A0+", 1337.6, 841),
        new FormatoFolha("A0++", 1486.3, 841),
    };

    /// <summary>Fatores de unidade tentados (mm por unidade do papel): mm, cm, polegada, m.</summary>
    public static readonly double[] Fatores = { 1, 10, 25.4, 1000 };

    /// <summary>
    /// Formato mais próximo de um retângulo <paramref name="largura"/> × <paramref name="altura"/> (unidades do papel), comparando
    /// as medidas ordenadas (a orientação não importa). Tenta o layout em mm e, se não casar, em cm/pol/m. Fora da tolerância
    /// (mm, nas duas medidas) = Formato null.
    /// </summary>
    public static FormatoReconhecido Reconhecer(double largura, double altura, IEnumerable<FormatoFolha> formatos, double tolerancia = 10)
    {
        var lista = (formatos ?? Padrao()).Where(f => f != null && f.Maior > 0).ToList();
        double maior = Math.Max(Math.Abs(largura), Math.Abs(altura)), menor = Math.Min(Math.Abs(largura), Math.Abs(altura));
        if (maior <= 0 || lista.Count == 0) return new FormatoReconhecido();
        FormatoReconhecido melhor = null;
        foreach (var fator in Fatores)
        {
            foreach (var f in lista)
            {
                double dif = Math.Max(Math.Abs(maior * fator - f.Maior), Math.Abs(menor * fator - f.Menor));
                if (dif > tolerancia) continue;
                if (melhor == null || dif < melhor.Diferenca) melhor = new FormatoReconhecido { Formato = f, Diferenca = dif, Fator = fator };
            }
            if (melhor != null) return melhor; // o primeiro fator que casa (mm antes de cm…)
        }
        // sem formato: diferença para o mais próximo em mm, para o relatório
        var prox = lista.OrderBy(f => Math.Max(Math.Abs(maior - f.Maior), Math.Abs(menor - f.Menor))).First();
        return new FormatoReconhecido { Diferenca = Math.Max(Math.Abs(maior - prox.Maior), Math.Abs(menor - prox.Menor)) };
    }

    /// <summary>Formato pelo nome (sem diferenciar maiúsculas).</summary>
    public static FormatoFolha PorNome(IEnumerable<FormatoFolha> formatos, string nome) =>
        string.IsNullOrEmpty(nome) ? null : formatos?.FirstOrDefault(f => string.Equals(f.Nome, nome, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Nome do PDF de uma folha: o nome do DWG; com mais de um layout do MESMO desenho no lote, " - &lt;layout&gt;" no fim.
    /// Caracteres inválidos em nome de arquivo viram "_".
    /// </summary>
    public static string NomePdf(string nomeDesenho, string layout, bool variosLayouts)
    {
        var baseNome = Path.GetFileNameWithoutExtension(nomeDesenho ?? "desenho");
        var nome = variosLayouts ? $"{baseNome} - {layout}" : baseNome;
        foreach (var c in Path.GetInvalidFileNameChars()) nome = nome.Replace(c, '_');
        return nome.Trim() + ".pdf";
    }
}

/// <summary>Como escolher a área plotada.</summary>
public enum AreaPlotLote
{
    /// <summary>Janela no retângulo da moldura reconhecida (ou na extensão do layout, se não houver moldura).</summary>
    Moldura = 0,
    /// <summary>Extensão do layout (PlotType Extents).</summary>
    Extensao = 1,
    /// <summary>Limites do layout (PlotType Layout).</summary>
    Layout = 2,
}

/// <summary>Escolhas da janela do ARMPDFLOTE (lembradas entre usos): %AppData%\PCEB\DEB.Cad\pdflote.json.</summary>
[DataContract]
public sealed class ConfigLotePdf
{
    [DataMember(Order = 1)] public string Impressora { get; set; } = "PDF24";
    /// <summary>Tabela de penas (.ctb) do lote; vazio = a do page setup de cada layout (a escolha da janela fica gravada aqui).</summary>
    [DataMember(Order = 2)] public string EstiloPlotagem { get; set; } = "";
    [DataMember(Order = 3)] public double Tolerancia { get; set; } = 10;
    [DataMember(Order = 4)] public AreaPlotLote Area { get; set; } = AreaPlotLote.Moldura;
    /// <summary>Vazio = pasta de cada DWG.</summary>
    [DataMember(Order = 5)] public string PastaDestino { get; set; } = "";
    [DataMember(Order = 6)] public bool NaPastaDoDesenho { get; set; } = true;
    [DataMember(Order = 7)] public string UltimaPastaDwg { get; set; } = "";
    [DataMember(Order = 8)] public bool Subpastas { get; set; }
    [DataMember(Order = 9)] public bool SobrescreverTudo { get; set; }
    [DataMember(Order = 10)] public bool GravarNoLayout { get; set; }
    /// <summary>Segundos de espera pelo PDF de cada folha.</summary>
    [DataMember(Order = 11)] public int EsperaSegundos { get; set; } = 180;
    [DataMember(Order = 12)] public List<FormatoFolha> Formatos { get; set; } = FormatosFolha.Padrao();
    [DataMember(Order = 13)] public bool AbrirPastaNoFim { get; set; }
    /// <summary>Impressora do lote no AutoCAD (lembrada à parte; no GstarCAD/ZWCAD vale <see cref="Impressora"/>).</summary>
    [DataMember(Order = 14)] public string ImpressoraAutoCad { get; set; } = ImpressorasPdf.AutoCadPdf;

    /// <summary>O hospedeiro é o AutoCAD (definido pelo comando; não é gravado).</summary>
    [IgnoreDataMember] public bool NoAutoCad { get; set; }

    /// <summary>Impressora usada pelo lote neste CAD: <see cref="ImpressoraAutoCad"/> no AutoCAD, senão <see cref="Impressora"/>.</summary>
    [IgnoreDataMember]
    public string ImpressoraLote
    {
        get => NoAutoCad ? ImpressoraAutoCad : Impressora;
        set { if (NoAutoCad) ImpressoraAutoCad = value; else Impressora = value; }
    }

    [OnDeserializing]
    private void AoDesserializar(StreamingContext _)
    {
        var p = new ConfigLotePdf();
        Impressora = p.Impressora; EstiloPlotagem = ""; Tolerancia = p.Tolerancia; Area = p.Area;
        PastaDestino = p.PastaDestino; NaPastaDoDesenho = p.NaPastaDoDesenho; UltimaPastaDwg = p.UltimaPastaDwg;
        EsperaSegundos = p.EsperaSegundos; Formatos = null; ImpressoraAutoCad = p.ImpressoraAutoCad;
    }

    [OnDeserialized]
    private void AoTerminar(StreamingContext _)
    {
        if (Formatos == null || Formatos.Count == 0) Formatos = FormatosFolha.Padrao();
        if (Tolerancia <= 0) Tolerancia = 10;
        if (EsperaSegundos < 10) EsperaSegundos = 180;
        if (string.IsNullOrWhiteSpace(Impressora)) Impressora = "PDF24";
        if (string.IsNullOrWhiteSpace(ImpressoraAutoCad)) ImpressoraAutoCad = ImpressorasPdf.AutoCadPdf;
    }

    public static ConfigLotePdf Carregar(string caminho)
    {
        try
        {
            if (File.Exists(caminho))
            {
                var ser = new DataContractJsonSerializer(typeof(ConfigLotePdf));
                using var ms = new MemoryStream(Encoding.UTF8.GetBytes(File.ReadAllText(caminho, Encoding.UTF8)));
                return (ConfigLotePdf)ser.ReadObject(ms);
            }
        }
        catch { /* arquivo estragado: padrão */ }
        return new ConfigLotePdf();
    }

    public void Salvar(string caminho)
    {
        var ser = new DataContractJsonSerializer(typeof(ConfigLotePdf));
        using var ms = new MemoryStream();
        using (var w = JsonReaderWriterFactory.CreateJsonWriter(ms, Encoding.UTF8, false, true, "  "))
            ser.WriteObject(w, this);
        Directory.CreateDirectory(Path.GetDirectoryName(caminho));
        File.WriteAllBytes(caminho, ms.ToArray());
    }
}
