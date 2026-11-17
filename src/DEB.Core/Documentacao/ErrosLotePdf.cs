using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace DEB.Core.Documentacao;

/// <summary>Causa de uma falha do lote de PDFs (ARMPDFLOTE), para a mensagem curta e o resumo do relatório.</summary>
public enum CausaErroPdf
{
    Desconhecida = 0,
    Pdf24NaoInstalada,
    ImpressoraInexistente,
    PapelInexistente,
    FormatoNaoEscolhido,
    SemMoldura,
    CtbNaoEncontrado,
    DwgNaoAbre,
    DwgEmUso,
    DwgCorrompido,
    PdfNaoGerado,
    PdfDestinoAberto,
    PdfDestinoSomenteLeitura,
    DestinoSemPermissao,
    DestinoInexistente,
    DiscoCheio,
    CadOcupado,
    PlotagemFalhou,
    Cancelado,
}

/// <summary>Etapa do lote em que a falha aconteceu.</summary>
public enum EtapaLotePdf
{
    Ler = 0,
    Abrir,
    Configurar,
    Plotar,
    Esperar,
    Copiar,
    Fechar,
}

/// <summary>Falha conhecida do lote, lançada pelo plugin com a causa já definida (a mensagem é a curta, em português).</summary>
public sealed class ErroLotePdfException : Exception
{
    public CausaErroPdf Causa { get; }
    public ErroLotePdfException(CausaErroPdf causa, string mensagem, Exception interna = null) : base(mensagem, interna) { Causa = causa; }
}

/// <summary>Diagnóstico de uma falha: mensagem curta para o usuário + detalhe técnico para o log e o "copiar detalhes".</summary>
public sealed class DiagnosticoErroPdf
{
    public CausaErroPdf Causa { get; set; }
    public EtapaLotePdf Etapa { get; set; }
    /// <summary>Mensagem curta e compreensível (o que mostrar na janela).</summary>
    public string Mensagem { get; set; } = "";
    /// <summary>O que fazer (pode ser vazio).</summary>
    public string Dica { get; set; } = "";
    public string TipoExcecao { get; set; } = "";
    public string MensagemTecnica { get; set; } = "";
    /// <summary>HRESULT da exceção (COM/Win32), 0 = nenhum.</summary>
    public int HResult { get; set; }
    /// <summary>Código de erro do Win32 (Win32Exception), 0 = nenhum.</summary>
    public int CodigoWin32 { get; set; }
    public string Pilha { get; set; } = "";

    /// <summary>"mensagem — dica" (uma linha).</summary>
    public string Resumo => string.IsNullOrEmpty(Dica) ? Mensagem : Mensagem + " — " + Dica;

    /// <summary>Bloco de texto para o log e para o "copiar detalhes" (data/hora, arquivo, folha, etapa, mensagem, exceção, HRESULT, pilha).</summary>
    public string Detalhado(string arquivo, string folha, DateTime quando)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Data/hora: " + quando.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        if (!string.IsNullOrEmpty(arquivo)) sb.AppendLine("Arquivo: " + arquivo);
        if (!string.IsNullOrEmpty(folha)) sb.AppendLine("Folha: " + folha);
        sb.AppendLine("Etapa: " + ErrosLotePdf.NomeEtapa(Etapa));
        sb.AppendLine("Causa: " + Causa + " — " + Mensagem);
        if (!string.IsNullOrEmpty(Dica)) sb.AppendLine("O que fazer: " + Dica);
        if (!string.IsNullOrEmpty(TipoExcecao)) sb.AppendLine("Exceção: " + TipoExcecao + ": " + MensagemTecnica);
        if (HResult != 0) sb.AppendLine("HRESULT: 0x" + HResult.ToString("X8", CultureInfo.InvariantCulture));
        if (CodigoWin32 != 0) sb.AppendLine("Erro Win32: " + CodigoWin32.ToString(CultureInfo.InvariantCulture));
        if (!string.IsNullOrEmpty(Pilha)) sb.AppendLine("Pilha:").AppendLine(Pilha.TrimEnd());
        return sb.ToString();
    }
}

/// <summary>
/// Tradução das falhas do lote de PDFs em mensagens claras (pedido do usuário de 17/11/2026: "quando der algum erro saber do que
/// se trata"). Olha a exceção e as internas: <see cref="ErroLotePdfException"/> (causa já definida), HRESULT do COM/Win32,
/// exceções de E/S e as mensagens de ErrorStatus do CAD (eFileSharingViolation, eDwgNeedsRecovery…), sempre com a etapa.
/// </summary>
public static class ErrosLotePdf
{
    // HRESULTs (0x8007xxxx = erro Win32 xxxx) e códigos Win32 conhecidos
    public const int HrAcessoNegado = unchecked((int)0x80070005);
    public const int HrArquivoNaoEncontrado = unchecked((int)0x80070002);
    public const int HrCaminhoNaoEncontrado = unchecked((int)0x80070003);
    public const int HrCompartilhamento = unchecked((int)0x80070020);
    public const int HrBloqueio = unchecked((int)0x80070021);
    public const int HrDiscoCheio = unchecked((int)0x80070070);
    public const int HrDiscoCheio2 = unchecked((int)0x80070027);
    public const int HrSomenteLeitura = unchecked((int)0x80070013);
    public const int HrImpressoraInvalida = unchecked((int)0x80070709);
    public const int HrChamadaRejeitada = unchecked((int)0x80010001);
    public const int HrServidorOcupado = unchecked((int)0x8001010A);
    public const int HrRpcIndisponivel = unchecked((int)0x800706BA);

    public static string NomeEtapa(EtapaLotePdf e) => e switch
    {
        EtapaLotePdf.Ler => "ler o desenho (lista)",
        EtapaLotePdf.Abrir => "abrir o desenho",
        EtapaLotePdf.Configurar => "configurar a plotagem",
        EtapaLotePdf.Plotar => "plotar",
        EtapaLotePdf.Esperar => "esperar o PDF",
        EtapaLotePdf.Copiar => "copiar o PDF para o destino",
        EtapaLotePdf.Fechar => "fechar o desenho",
        _ => e.ToString(),
    };

    /// <summary>Mensagem curta e dica padrão de cada causa.</summary>
    public static (string Mensagem, string Dica) Texto(CausaErroPdf c) => c switch
    {
        CausaErroPdf.Pdf24NaoInstalada => ("PDF24 não instalada", "instale o PDF24 Creator (impressora \"PDF24\") ou escolha outra impressora de PDF"),
        CausaErroPdf.ImpressoraInexistente => ("a impressora escolhida não existe neste computador", "escolha a PDF24 na janela"),
        CausaErroPdf.PapelInexistente => ("o papel não existe na impressora", "use \"Criar folhas na impressora\""),
        CausaErroPdf.FormatoNaoEscolhido => ("formato da folha não escolhido", "escolha o formato na coluna \"Formato\""),
        CausaErroPdf.SemMoldura => ("nenhuma moldura/carimbo encontrado no Model nem nos layouts", "confira se a moldura é uma polilinha retangular ou um bloco com o tamanho de um dos formatos da lista"),
        CausaErroPdf.CtbNaoEncontrado => ("ctb não encontrado", "escolha outro estilo de plotagem ou use " + EstiloPlotagemPadrao.NomeArquivo),
        CausaErroPdf.DwgNaoAbre => ("não foi possível abrir o DWG", "abra o arquivo no CAD para ver a mensagem dele"),
        CausaErroPdf.DwgEmUso => ("arquivo aberto por outro usuário/programa ou somente leitura", "feche-o no outro computador/programa e tente de novo"),
        CausaErroPdf.DwgCorrompido => ("o DWG está danificado ou é de versão mais nova que este CAD", "abra-o no CAD e use RECOVER/AUDIT, ou salve numa versão mais antiga"),
        CausaErroPdf.PdfNaoGerado => ("o PDF não foi gerado (PDF24 travada?)", "confira se a PDF24 mostrou alguma janela; feche-a e tente de novo"),
        CausaErroPdf.PdfDestinoAberto => ("o PDF de destino está aberto em outro programa", "feche o PDF (leitor de PDF, navegador) e exporte de novo"),
        CausaErroPdf.PdfDestinoSomenteLeitura => ("o PDF de destino é somente leitura", "tire o atributo \"somente leitura\" do arquivo ou escolha outra pasta"),
        CausaErroPdf.DestinoSemPermissao => ("pasta de destino sem permissão de escrita", "escolha outra pasta de destino"),
        CausaErroPdf.DestinoInexistente => ("pasta de destino não encontrada (rede desconectada?)", "confira a pasta/unidade de rede"),
        CausaErroPdf.DiscoCheio => ("disco cheio", "libere espaço no disco de destino ou no TEMP"),
        CausaErroPdf.CadOcupado => ("o CAD estava ocupado (outro comando ou plotagem em andamento)", "termine o que está em andamento e tente de novo"),
        CausaErroPdf.PlotagemFalhou => ("a plotagem falhou no CAD", "veja o detalhe técnico; tente plotar essa folha à mão para ver a mensagem do CAD"),
        CausaErroPdf.Cancelado => ("cancelado", ""),
        _ => ("erro inesperado", "copie os detalhes e envie para o suporte"),
    };

    /// <summary>Classifica uma exceção na etapa dada.</summary>
    public static DiagnosticoErroPdf Classificar(Exception ex, EtapaLotePdf etapa)
    {
        var d = new DiagnosticoErroPdf { Etapa = etapa };
        if (ex == null) { (d.Mensagem, d.Dica) = Texto(CausaErroPdf.Desconhecida); return d; }
        d.TipoExcecao = ex.GetType().FullName;
        d.MensagemTecnica = ex.Message;
        d.Pilha = ex.ToString();
        d.HResult = ex.HResult;

        var cadeia = new List<Exception>();
        for (var e = ex; e != null && cadeia.Count < 8; e = e.InnerException) cadeia.Add(e);
        foreach (var e in cadeia)
        {
            if (e is Win32Exception w && d.CodigoWin32 == 0) d.CodigoWin32 = w.NativeErrorCode;
            if (d.HResult == 0 || d.HResult == unchecked((int)0x80004005)) d.HResult = e.HResult; // E_FAIL genérico: tenta a interna
        }

        var conhecida = cadeia.OfType<ErroLotePdfException>().FirstOrDefault();
        CausaErroPdf causa;
        string msgPropria = null;
        if (conhecida != null) { causa = conhecida.Causa; msgPropria = conhecida.Message; }
        else causa = Causa(cadeia, etapa, d.CodigoWin32);

        d.Causa = causa;
        var (m, dica) = Texto(causa);
        d.Mensagem = !string.IsNullOrWhiteSpace(msgPropria) ? msgPropria : m;
        d.Dica = dica;
        return d;
    }

    /// <summary>Diagnóstico de uma causa conhecida sem exceção (ex.: folha sem moldura na leitura).</summary>
    public static DiagnosticoErroPdf De(CausaErroPdf causa, EtapaLotePdf etapa, string mensagem = null)
    {
        var (m, dica) = Texto(causa);
        return new DiagnosticoErroPdf { Causa = causa, Etapa = etapa, Mensagem = mensagem ?? m, Dica = dica };
    }

    private static CausaErroPdf Causa(List<Exception> cadeia, EtapaLotePdf etapa, int win32)
    {
        // 1) códigos (HRESULT/Win32) de qualquer exceção da cadeia
        foreach (var e in cadeia)
        {
            int hr = e.HResult;
            int w = e is Win32Exception we ? we.NativeErrorCode : ((hr & 0xFFFF0000) == unchecked((int)0x80070000) ? hr & 0xFFFF : 0);
            if (hr == HrChamadaRejeitada || hr == HrServidorOcupado || hr == HrRpcIndisponivel) return CausaErroPdf.CadOcupado;
            if (hr == HrImpressoraInvalida || w == 1801 || w == 1797 || w == 1796) return CausaErroPdf.ImpressoraInexistente;
            if (hr == HrDiscoCheio || hr == HrDiscoCheio2 || w == 112 || w == 39) return CausaErroPdf.DiscoCheio;
            if (hr == HrCompartilhamento || hr == HrBloqueio || w == 32 || w == 33)
                return etapa == EtapaLotePdf.Copiar ? CausaErroPdf.PdfDestinoAberto : CausaErroPdf.DwgEmUso;
            if (hr == HrSomenteLeitura || w == 19)
                return etapa == EtapaLotePdf.Copiar ? CausaErroPdf.PdfDestinoSomenteLeitura : CausaErroPdf.DwgEmUso;
        }
        // 2) tipos e mensagens
        var texto = string.Join(" | ", cadeia.Select(e => e.Message ?? "")).ToLowerInvariant();
        bool Tem(params string[] t) => t.Any(x => texto.Contains(x.ToLowerInvariant()));

        if (cadeia.Any(e => e is OperationCanceledException)) return CausaErroPdf.Cancelado;
        if (cadeia.Any(e => e is TimeoutException)) return CausaErroPdf.PdfNaoGerado;
        if (Tem("eFileSharingViolation", "eFileLockedByOtherUser", "eFileLocked", "being used by another process", "sendo usado por outro processo"))
            return etapa == EtapaLotePdf.Copiar ? CausaErroPdf.PdfDestinoAberto : CausaErroPdf.DwgEmUso;
        if (Tem("eDwgNeedsRecovery", "eBadDwgHeader", "eDwgCRCError", "eDwgObjectImproperlyRead", "eDwgSentinelError",
                "eMakeMeProxy", "eFileVersionIsNewer", "eNotSupportedInDwgApi", "eIncorrectFileVersion", "eUnsupportedFileFormat"))
            return CausaErroPdf.DwgCorrompido;
        if (Tem("eInvalidPlotInfo", "eNoPlotDevice", "ePlotDeviceNotFound", "eDeviceNotFound", "eInvalidPlotConfig", "invalid printer"))
            return CausaErroPdf.ImpressoraInexistente;
        if (Tem("eInvalidMedia", "eMediaNotFound", "eInvalidCanonicalMediaName"))
            return CausaErroPdf.PapelInexistente;
        if (Tem("eInvalidStyleSheet", "ePlotStyleSheetNotFound", "eNoPlotStyle"))
            return CausaErroPdf.CtbNaoEncontrado;
        if (Tem("ePlotAlreadyStarted", "eAnotherPlotInProgress", "eIsPlotting", "already plotting", "já está plotando"))
            return CausaErroPdf.CadOcupado;

        var io = cadeia.FirstOrDefault(e => e is UnauthorizedAccessException || e is IOException);
        if (cadeia.Any(e => e is UnauthorizedAccessException) || Tem("eFileAccessErr", "eAccessDenied"))
        {
            if (etapa == EtapaLotePdf.Copiar) return CausaErroPdf.DestinoSemPermissao;
            if (etapa == EtapaLotePdf.Abrir || etapa == EtapaLotePdf.Ler) return CausaErroPdf.DwgEmUso;
        }
        if (cadeia.Any(e => e is DirectoryNotFoundException || e is DriveNotFoundException))
            return etapa == EtapaLotePdf.Copiar ? CausaErroPdf.DestinoInexistente : CausaErroPdf.DwgNaoAbre;
        if (cadeia.Any(e => e is FileNotFoundException) || Tem("eFileNotFound", "eFilerError"))
            return etapa == EtapaLotePdf.Abrir || etapa == EtapaLotePdf.Ler ? CausaErroPdf.DwgNaoAbre : CausaErroPdf.Desconhecida;
        if (io != null && etapa == EtapaLotePdf.Copiar) return CausaErroPdf.DestinoSemPermissao;

        // 3) pela etapa
        return etapa switch
        {
            EtapaLotePdf.Abrir => CausaErroPdf.DwgNaoAbre,
            EtapaLotePdf.Ler => CausaErroPdf.DwgNaoAbre,
            EtapaLotePdf.Plotar => CausaErroPdf.PlotagemFalhou,
            EtapaLotePdf.Configurar => CausaErroPdf.PlotagemFalhou,
            EtapaLotePdf.Esperar => CausaErroPdf.PdfNaoGerado,
            _ => CausaErroPdf.Desconhecida,
        };
    }

    /// <summary>Resumo do lote: "3 gerado(s), 2 com erro" + uma linha por causa ("2× o papel não existe na impressora — …").</summary>
    public static List<string> ResumoPorCausa(IEnumerable<DiagnosticoErroPdf> erros) =>
        (erros ?? Enumerable.Empty<DiagnosticoErroPdf>()).Where(e => e != null)
            .GroupBy(e => e.Causa)
            .OrderByDescending(g => g.Count())
            .Select(g => $"{g.Count()}× {g.First().Resumo}")
            .ToList();
}
