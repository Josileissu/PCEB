using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace DEB.Core.Configuracao.Projeto;

public enum NivelConfiguracao { Padrao = 0, Escritorio = 1, Projeto = 2, Grupo = 3 }

/// <summary>
/// Um nível da configuração (D43): só os valores que DIFEREM do nível de cima, as justificativas dos valores fora da norma e,
/// para o escritório, o nome do perfil. Gravado em JSON legível (escritório em %AppData%\PCEB\padroes, projeto junto do modelo).
/// </summary>
[DataContract]
public sealed class NivelValores
{
    [DataMember(Order = 1)] public int Versao { get; set; } = 1;
    [DataMember(Order = 2)] public NivelConfiguracao Nivel { get; set; }
    [DataMember(Order = 3)] public string Nome { get; set; } = "";
    /// <summary>Id do campo → valor (texto invariante, ponto decimal).</summary>
    [DataMember(Order = 4)] public Dictionary<string, string> Valores { get; set; } = new Dictionary<string, string>();
    /// <summary>Id do campo → justificativa do valor fora da faixa da norma (sai no memorial).</summary>
    [DataMember(Order = 5)] public Dictionary<string, string> Justificativas { get; set; } = new Dictionary<string, string>();

    public string ParaJson()
    {
        var ser = new DataContractJsonSerializer(typeof(NivelValores), new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });
        using var ms = new MemoryStream();
        using (var w = JsonReaderWriterFactory.CreateJsonWriter(ms, Encoding.UTF8, false, true, "  ")) ser.WriteObject(w, this);
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    public static NivelValores DeJson(string json)
    {
        var ser = new DataContractJsonSerializer(typeof(NivelValores), new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(json ?? "{}"));
        var n = (NivelValores)ser.ReadObject(ms);
        n.Valores ??= new Dictionary<string, string>();
        n.Justificativas ??= new Dictionary<string, string>();
        return n;
    }
}

/// <summary>Valor fora da faixa da norma (D43): aceito, mas sem justificativa vira pendência.</summary>
public sealed class ForaDaNorma
{
    public CampoConfiguracao Campo { get; set; }
    public string Valor { get; set; } = "";
    public NivelConfiguracao Nivel { get; set; }
    public string Justificativa { get; set; } = "";
    public bool Justificado => !string.IsNullOrWhiteSpace(Justificativa);
    public string Texto => $"{Campo.Rotulo} = {Valor} fora da faixa da norma ({Campo.Referencia})" +
                           (Justificado ? $" — justificativa: {Justificativa}" : " — falta a justificativa");
}

/// <summary>
/// Configuração em vigor para um elemento (D43): padrão do PCEB (catálogo) → escritório → projeto → grupo/peça. Cada leitura diz o
/// valor e de onde ele vem. Os γ e o γn sem valor digitado acompanham o tipo de elemento (pré-fabricado ou pré-moldado,
/// NBR 9062 8.1 e 7.3.1.1).
/// </summary>
public sealed class ConfiguracaoProjeto
{
    public NivelValores Escritorio { get; set; } = new NivelValores { Nivel = NivelConfiguracao.Escritorio };
    public NivelValores Projeto { get; set; } = new NivelValores { Nivel = NivelConfiguracao.Projeto };
    public NivelValores Grupo { get; set; } = new NivelValores { Nivel = NivelConfiguracao.Grupo };

    private IEnumerable<NivelValores> DeBaixoParaCima() => new[] { Grupo, Projeto, Escritorio }.Where(n => n != null);

    /// <summary>Valor em vigor (texto invariante) e o nível de onde ele vem.</summary>
    public (string Valor, NivelConfiguracao Origem) Ler(string id)
    {
        var c = CatalogoConfiguracao.Campo(id);
        foreach (var n in DeBaixoParaCima())
            if (n.Valores.TryGetValue(id, out string v) && !string.IsNullOrWhiteSpace(v)) return (v, n.Nivel);
        return (PadraoDependente(c), NivelConfiguracao.Padrao);
    }

    public string Texto(string id) => Ler(id).Valor;

    public double Numero(string id)
    {
        string v = Texto(id);
        return double.TryParse(v.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double x) ? x
            : double.Parse(CatalogoConfiguracao.Campo(id).Padrao, CultureInfo.InvariantCulture);
    }

    /// <summary>Grava um valor num nível; igual ao herdado = apaga (o nível só guarda o que difere).</summary>
    public void Definir(NivelConfiguracao nivel, string id, string valor, string justificativa = null)
    {
        var c = CatalogoConfiguracao.Campo(id);
        if (c.Tipo == TipoCampo.Unidade && !Unidades.Aceita(c.Grandeza.Value, valor))
            throw new ArgumentException($"Unidade \"{valor}\" não vale para {c.Rotulo}.");
        if (c.Tipo == TipoCampo.Opcao && !c.Opcoes.Contains(valor))
            throw new ArgumentException($"\"{valor}\" não é opção de {c.Rotulo}.");
        var n = Nivel(nivel);
        n.Valores.Remove(id);
        n.Justificativas.Remove(id);
        if (Texto(id) == valor) return;   // herdado já vale isso
        n.Valores[id] = valor;
        if (!string.IsNullOrWhiteSpace(justificativa)) n.Justificativas[id] = justificativa.Trim();
    }

    /// <summary>Volta o campo ao valor herdado no nível dado.</summary>
    public void Restaurar(NivelConfiguracao nivel, string id) { var n = Nivel(nivel); n.Valores.Remove(id); n.Justificativas.Remove(id); }

    public NivelValores Nivel(NivelConfiguracao nivel) => nivel switch
    {
        NivelConfiguracao.Escritorio => Escritorio,
        NivelConfiguracao.Projeto => Projeto,
        NivelConfiguracao.Grupo => Grupo,
        _ => throw new ArgumentException("O padrão do PCEB não é editável: grave no escritório."),
    };

    /// <summary>Atalho de sistema de unidades (D43): grava no nível as unidades do sistema escolhido.</summary>
    public void AplicarSistema(NivelConfiguracao nivel, string sistema)
    {
        if (!Unidades.Sistemas.TryGetValue(sistema, out var mapa)) throw new ArgumentException($"Sistema de unidades desconhecido: {sistema}");
        foreach (var kv in mapa) Definir(nivel, CatalogoConfiguracao.IdUnidade(kv.Key), kv.Value);
    }

    /// <summary>Unidade escolhida para a grandeza.</summary>
    public string Unidade(Grandeza g) => Texto(CatalogoConfiguracao.IdUnidade(g));

    /// <summary>Valor do núcleo (unidade base) como texto na unidade e no separador da configuração.</summary>
    public string Formatar(Grandeza g, double valorBase, int casas) =>
        Unidades.Texto(g, valorBase, Unidade(g), casas, Texto("apres.separador") != "Ponto");

    public List<BitolaDisponivel> Bitolas() => CatalogoConfiguracao.LerBitolas(Texto("det.bitolas"));

    /// <summary>Valores fora da faixa da norma em vigor (com ou sem justificativa).</summary>
    public List<ForaDaNorma> ForaDaNorma()
    {
        var r = new List<ForaDaNorma>();
        foreach (var c in CatalogoConfiguracao.Campos.Where(c => c.Tipo == TipoCampo.Numero && (c.Min.HasValue || c.Max.HasValue)))
        {
            var (v, origem) = Ler(c.Id);
            if (origem == NivelConfiguracao.Padrao) continue;
            double x = Numero(c.Id), min = MinDependente(c);
            if (x >= min - 1e-9 && (!c.Max.HasValue || x <= c.Max.Value + 1e-9)) continue;
            string just = origem == NivelConfiguracao.Padrao ? "" : (Nivel(origem).Justificativas.TryGetValue(c.Id, out string j) ? j : "");
            r.Add(new ForaDaNorma { Campo = c, Valor = v, Nivel = origem, Justificativa = just });
        }
        return r;
    }

    // ---- padrões que dependem do tipo de elemento (NBR 9062 8.1 e 7.3.1.1)
    private bool PreFabricado => Texto("normas.tipoElemento") == CatalogoConfiguracao.PreFabricado;

    private string PadraoDependente(CampoConfiguracao c)
    {
        if (c.Id is "normas.gamaC" or "normas.gamaS" or "normas.gamaNPerm" or "normas.gamaNVar" && PreFabricado)
            return c.Id switch { "normas.gamaC" => "1.3", "normas.gamaS" => "1.1", "normas.gamaNPerm" => "1", _ => "1.1" };
        return c.Padrao;
    }

    /// <summary>Limite inferior da norma, que no pré-moldado é o próprio valor da NBR 9062 (1,4 / 1,15 / 1,1 / 1,2).</summary>
    private double MinDependente(CampoConfiguracao c)
    {
        if (!PreFabricado && c.Id is "normas.gamaC" or "normas.gamaS" or "normas.gamaNPerm" or "normas.gamaNVar")
            return double.Parse(c.Padrao, CultureInfo.InvariantCulture);
        return c.Min ?? double.NegativeInfinity;
    }

    /// <summary>γn em vigor: permanente preponderante ou não.</summary>
    public double GamaN(bool permanentePreponderante) => Numero(permanentePreponderante ? "normas.gamaNPerm" : "normas.gamaNVar");
}
