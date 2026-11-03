using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using DEB.Core.Normas;
using DEB.Core.Tabelas;

namespace DEB.Core.Configuracao;

/// <summary>Formatos da descrição (seção 4.1).</summary>
public enum PadraoDescricao
{
    /// <summary>"4 N3 ø8.0 c/10 C=200"</summary>
    QuantidadeNumero = 1,
    /// <summary>"N3 – 4ø8.0 c/10 C=200"</summary>
    NumeroPrimeiro = 2,
    /// <summary>"4 ø8.0c/10 C=200 (N3)"</summary>
    NumeroNoFim = 3,
}

public enum ModoArredondamento { Proximo, ParaCima }

public enum UnidadeDesenho { Centimetro = 0, Automatica = 1 }

/// <summary>Configuração do plugin. Valores normativos ficam em tabelas, nunca fixos no código.</summary>
[DataContract]
public sealed class ConfiguracaoArmadura
{
    // Numeração e descrição
    [DataMember(Order = 1)] public int NumeroInicial { get; set; } = 1;
    [DataMember(Order = 2)] public string PrefixoPosicao { get; set; } = "N";
    [DataMember(Order = 3)] public string PrefixoComprimento { get; set; } = "C";
    [DataMember(Order = 4)] public PadraoDescricao PadraoDescricao { get; set; } = PadraoDescricao.QuantidadeNumero;
    /// <summary>Símbolo do diâmetro no texto do CAD ("%%c" vira ⌀ nas fontes do CAD).</summary>
    [DataMember(Order = 5)] public string SimboloDiametro { get; set; } = "%%c";
    [DataMember(Order = 6)] public bool EspelhadasIguais { get; set; } = true;
    /// <summary>Global, Individual (recomeça por detalhamento) ou Sequencial (seção 4.1).</summary>
    [DataMember(Order = 7)] public Numeracao.TipoNumeracao TipoNumeracao { get; set; } = Numeracao.TipoNumeracao.Global;

    // Comprimentos (cm)
    [DataMember(Order = 10)] public int CasasDecimais { get; set; }
    [DataMember(Order = 11)] public ModoArredondamento Arredondamento { get; set; } = ModoArredondamento.Proximo;
    /// <summary>
    /// Comprimento real (C= e relação do aço): desconta as dobras pelas curvaturas do pino (NBR 6118).
    /// false = soma das medidas externas. As cotas dos segmentos são sempre externas.
    /// </summary>
    [DataMember(Order = 12)] public bool DescontarDobras { get; set; } = true;

    // Relação e resumo do aço
    [DataMember(Order = 20)] public double AcrescimoPesoPercentual { get; set; }
    /// <summary>Padrão das tabelas novas (cada tabela guarda a sua).</summary>
    [DataMember(Order = 21)] public RelacaoAco.ApresentacaoRA ApresentacaoRA { get; set; } = RelacaoAco.ApresentacaoRA.TabelaEResumo;
    [DataMember(Order = 22)] public RelacaoAco.ApresentacaoResumo ApresentacaoResumo { get; set; } = RelacaoAco.ApresentacaoResumo.Peso;
    [DataMember(Order = 23)] public bool MostrarPesoTotal { get; set; } = true;
    [DataMember(Order = 24)] public string TituloRelacao { get; set; } = "RELAÇÃO DO AÇO";
    [DataMember(Order = 25)] public string TituloResumo { get; set; } = "RESUMO DO AÇO";
    /// <summary>Comandos que renumeram e atualizam descrições e tabelas antes de rodar (plotar, exportar, salvar, regenerar).</summary>
    [DataMember(Order = 26)] public List<string> ComandosQueAtualizamRA { get; set; } = new List<string>
    {
        "PLOT", "-PLOT", "PRINT", "PUBLISH", "PREVIEW", "EXPORTPDF", "EXPORTLAYOUT", "QSAVE", "SAVE", "SAVEAS",
        "REGEN", "REGENALL",
    };

    // Desenho
    /// <summary>Centímetro: 1 unidade = 1 cm sempre (padrão do escritório). Automática: segue o INSUNITS.</summary>
    [DataMember(Order = 29)] public UnidadeDesenho UnidadeDesenho { get; set; } = UnidadeDesenho.Centimetro;
    /// <summary>Quantos cm vale 1 unidade do desenho quando a unidade é Automática e o INSUNITS não está definido.</summary>
    [DataMember(Order = 30)] public double CmPorUnidadePadrao { get; set; } = 1.0;
    /// <summary>Escala base (1:Escala) das barras e tabelas novas; cada barra/tabela guarda a sua.</summary>
    [DataMember(Order = 31)] public double Escala { get; set; } = 10;
    /// <summary>Altura do texto no papel, em mm: 2 mm → 2 un em 1:10, 5 un em 1:25 (desenho em cm).</summary>
    [DataMember(Order = 32)] public double AlturaTextoMm { get; set; } = 2.0;
    [DataMember(Order = 33)] public double AfastamentoTextoMm { get; set; } = 1.0;
    [DataMember(Order = 34)] public string LayerBarra { get; set; } = "ARM-BARRA";
    [DataMember(Order = 35)] public short CorBarra { get; set; } = 1;
    [DataMember(Order = 36)] public string LayerTexto { get; set; } = "ARM-TEXTO";
    [DataMember(Order = 37)] public short CorTexto { get; set; } = 7; // nunca amarelo (pedido de 2026-10-30)
    [DataMember(Order = 38)] public string LayerTabela { get; set; } = "ARM-TABELA";
    [DataMember(Order = 39)] public short CorTabela { get; set; } = 7;
    /// <summary>Desenha as dobras como arcos com o raio do pino (só na tela; a geometria não muda).</summary>
    [DataMember(Order = 40)] public bool RepresentarCurvatura { get; set; } = true;
    /// <summary>Abrir a paleta de propriedades ao carregar o plugin (lembra o último ARMPALETA).</summary>
    [DataMember(Order = 41)] public bool MostrarPaleta { get; set; } = true;
    /// <summary>Abrir a paleta de comandos (botões) ao carregar o plugin (lembra o último ARMCOMANDOS).</summary>
    [DataMember(Order = 57)] public bool MostrarComandos { get; set; } = true;

    // Faixas e armadura em corte (seções 2.3, 2.4 e 5.5)
    [DataMember(Order = 42)] public string LayerFaixa { get; set; } = "ARM-FAIXA";
    [DataMember(Order = 43)] public short CorFaixa { get; set; } = 3;
    [DataMember(Order = 44)] public string LayerCorte { get; set; } = "ARM-CORTE";
    [DataMember(Order = 45)] public short CorCorte { get; set; } = 1;
    /// <summary>Regra da quantidade nas faixas. [INFERIDO: validar com o escritório]</summary>
    [DataMember(Order = 46)] public Modelo.ModoQuantidadeFaixa ModoQuantidadeFaixa { get; set; } = Modelo.ModoQuantidadeFaixa.PisoMaisUm;
    /// <summary>Espaçamento inicial padrão das faixas novas, como fração do espaçamento (0 a 1).</summary>
    [DataMember(Order = 47)] public double EspacamentoInicialFaixa { get; set; }
    /// <summary>Diâmetro do círculo do corte = bitola × ampliação.</summary>
    [DataMember(Order = 48)] public double AmpliacaoCorte { get; set; } = 1.5;
    /// <summary>Escala das faixas novas (1:n); 0 = a da barra. Muda pela opção "Escala" dos comandos de faixa.</summary>
    [DataMember(Order = 58)] public double EscalaFaixa { get; set; }
    /// <summary>Telas novas (ARMTELA) já saem identificadas: "T1 - Q-92" + dimensões na diagonal.</summary>
    [DataMember(Order = 67)] public bool TelaIdentificada { get; set; }
    /// <summary>Distância (mm no papel) entre a linha base da faixa e os círculos do corte.</summary>
    [DataMember(Order = 49)] public double AfastamentoCorteMm { get; set; } = 1.5;
    /// <summary>Círculos do corte preenchidos (sólidos) ou só o contorno.</summary>
    [DataMember(Order = 55)] public bool CortePreenchido { get; set; } = true;

    // Valores correntes (última escolha do usuário)
    [DataMember(Order = 50)] public double BitolaCorrente { get; set; } = 10.0;
    [DataMember(Order = 51)] public string AcoCorrente { get; set; } = "CA-50";
    [DataMember(Order = 52)] public int QuantidadeCorrente { get; set; } = 1;
    [DataMember(Order = 53)] public double EspacamentoCorrente { get; set; }
    /// <summary>Último cobrimento usado no "ponto no cobrimento" do ARMBARRA (cm).</summary>
    [DataMember(Order = 56)] public double CobrimentoCorrente { get; set; } = 2.5;
    /// <summary>Últimas opções do ARMESTRIBO (usadas pelo ARMESTRIBOREP).</summary>
    [DataMember(Order = 54)] public Estribos.OpcoesEstribo UltimoEstribo { get; set; } = new Estribos.OpcoesEstribo();

    // Tabelas
    [DataMember(Order = 60)] public TabelaBarras Barras { get; set; } = TabelaBarras.Padrao();
    [DataMember(Order = 61)] public RegrasDobramento Dobramento { get; set; } = new RegrasDobramento();
    [DataMember(Order = 62)] public RegrasTraspasse Traspasse { get; set; } = new RegrasTraspasse();
    [DataMember(Order = 63)] public List<ModeloTrelica> Trelicas { get; set; } = Catalogos.TrelicasPadrao();
    [DataMember(Order = 64)] public List<ModeloTela> Telas { get; set; } = Catalogos.TelasPadrao();
    [DataMember(Order = 65)] public string TrelicaCorrente { get; set; } = "TR 8644";
    [DataMember(Order = 66)] public string TelaCorrente { get; set; } = "Q-138";
    /// <summary>Consolos padrão do escritório (template do pilar pré-moldado; ARMCONSOLOS).</summary>
    [DataMember(Order = 68)] public List<Templates.PilarPreMoldado.TipoConsolo> Consolos { get; set; } = Templates.PilarPreMoldado.TipoConsolo.Padrao();
    /// <summary>Insertos metálicos padrão (template do pilar pré-moldado; ARMINSERTOS).</summary>
    [DataMember(Order = 69)] public List<Templates.PilarPreMoldado.TipoInserto> Insertos { get; set; } = Templates.PilarPreMoldado.TipoInserto.Padrao();

    /// <summary>Versão do formato da configuração (migrações automáticas).</summary>
    [DataMember(Order = 100)] public int VersaoConfig { get; set; } = VersaoAtual;
    public const int VersaoAtual = 4;

    /// <summary>A leitura aplicou alguma migração (o arquivo deve ser regravado).</summary>
    [IgnoreDataMember] public bool Migrada { get; private set; }

    /// <summary>Escala efetiva: a do objeto (&gt; 0) ou a base da configuração.</summary>
    public double EscalaEfetiva(double escalaDoObjeto) => escalaDoObjeto > 0 ? escalaDoObjeto : (Escala > 0 ? Escala : 10);

    /// <summary>Altura do texto em unidades do desenho para a escala 1:<paramref name="escala"/> (0 = base).</summary>
    public double AlturaTexto(double cmPorUnidade, double escala = 0) =>
        AlturaTextoMm / 10.0 * EscalaEfetiva(escala) / cmPorUnidade;

    public double AfastamentoTexto(double cmPorUnidade, double escala = 0) =>
        AfastamentoTextoMm / 10.0 * EscalaEfetiva(escala) / cmPorUnidade;

    // O DataContractJsonSerializer não chama o construtor: os campos ausentes no JSON
    // receberiam 0/null. Preenche os padrões antes de ler.
    [OnDeserializing]
    private void AoDesserializar(StreamingContext _)
    {
        Padroes.Copiar(new ConfiguracaoArmadura(), this);
        VersaoConfig = 1; // JSON sem "VersaoConfig" = arquivo da versão 1
    }

    [OnDeserialized]
    private void AoTerminar(StreamingContext _)
    {
        Barras ??= TabelaBarras.Padrao();
        Dobramento ??= new RegrasDobramento();
        ComandosQueAtualizamRA ??= new ConfiguracaoArmadura().ComandosQueAtualizamRA;
        UltimoEstribo ??= new Estribos.OpcoesEstribo();
        Traspasse ??= new RegrasTraspasse();
        if (Trelicas == null || Trelicas.Count == 0) Trelicas = Catalogos.TrelicasPadrao();
        if (Telas == null || Telas.Count == 0) Telas = Catalogos.TelasPadrao();
        if (Consolos == null || Consolos.Count == 0) Consolos = Templates.PilarPreMoldado.TipoConsolo.Padrao();
        if (Insertos == null || Insertos.Count == 0) Insertos = Templates.PilarPreMoldado.TipoInserto.Padrao();
        if (VersaoConfig < 2)
        {
            // v2 (2026-10-26): desenho em cm e escala base 1:10, pedidos pelo usuário
            UnidadeDesenho = UnidadeDesenho.Centimetro;
            Escala = 10;
            AlturaTextoMm = 2.0;
            VersaoConfig = 2;
            Migrada = true;
        }
        if (VersaoConfig < 3)
        {
            // v3 (2026-10-26): cordoalhas CP-190 como armadura, pedido do usuário
            Barras.AcrescentarCordoalhas();
            VersaoConfig = 3;
            Migrada = true;
        }
        if (VersaoConfig < 4)
        {
            // v4 (2026-10-27): REGEN/REGENALL também recalculam descrições e tabelas, pedido do usuário
            foreach (var c in ComandosRegen)
                if (!ComandosQueAtualizamRA.Exists(x => string.Equals(x, c, StringComparison.OrdinalIgnoreCase)))
                    ComandosQueAtualizamRA.Add(c);
            VersaoConfig = 4;
            Migrada = true;
        }
    }

    private static readonly string[] ComandosRegen = { "REGEN", "REGENALL" };

    /// <summary>Cópia profunda (via JSON).</summary>
    public ConfiguracaoArmadura Clonar() => DeJson(ParaJson());

    public string ParaJson()
    {
        var ser = new DataContractJsonSerializer(typeof(ConfiguracaoArmadura),
            new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });
        using var ms = new MemoryStream();
        using (var w = JsonReaderWriterFactory.CreateJsonWriter(ms, Encoding.UTF8, false, true, "  "))
            ser.WriteObject(w, this);
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    public static ConfiguracaoArmadura DeJson(string json)
    {
        var ser = new DataContractJsonSerializer(typeof(ConfiguracaoArmadura));
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return (ConfiguracaoArmadura)ser.ReadObject(ms);
    }

    /// <summary>Lê o arquivo; se não existir ou estiver inválido, cria/usa o padrão.</summary>
    public static ConfiguracaoArmadura Carregar(string caminho, Action<string> aviso = null)
    {
        try
        {
            if (File.Exists(caminho))
            {
                var texto = File.ReadAllText(caminho, Encoding.UTF8);
                var lida = DeJson(texto);
                // arquivo de versão antiga: grava já migrado
                if (lida.Migrada)
                {
                    try { lida.Salvar(caminho); } catch { /* sem permissão: segue migrado em memória */ }
                    aviso?.Invoke("Configuração migrada para a versão " + VersaoAtual + " (desenho em cm, escala base 1:10).");
                }
                return lida;
            }
        }
        catch (Exception ex)
        {
            aviso?.Invoke($"Configuração inválida ({caminho}): {ex.Message}. Usando o padrão.");
            return new ConfiguracaoArmadura();
        }
        var cfg = new ConfiguracaoArmadura();
        try { cfg.Salvar(caminho); } catch { /* pasta sem permissão: segue com o padrão em memória */ }
        return cfg;
    }

    public void Salvar(string caminho)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(caminho));
        File.WriteAllText(caminho, ParaJson(), new UTF8Encoding(false));
    }
}
