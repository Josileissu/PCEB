using System;
using System.Collections.Generic;
using System.Linq;

namespace DEB.Core.Documentacao;

/// <summary>Tipo de um formulário de papel do Windows (FORM_INFO_1.Flags).</summary>
public enum TipoFormulario
{
    /// <summary>FORM_USER: criado por alguém (AddForm); pode ser apagado e recriado.</summary>
    Usuario = 0,
    /// <summary>FORM_BUILTIN: do próprio Windows; nunca é alterado.</summary>
    Interno = 1,
    /// <summary>FORM_PRINTER: instalado pelo driver de uma impressora; só é substituído com autorização.</summary>
    Impressora = 2,
}

/// <summary>Formulário de papel existente no servidor de impressão local (medidas em milésimos de mm, como o winspool).</summary>
public sealed class FormularioWindows
{
    public string Nome { get; set; }
    public int Largura { get; set; }
    public int Altura { get; set; }
    /// <summary>Área imprimível (ImageableArea), em milésimos de mm.</summary>
    public int Esquerda { get; set; }
    public int Topo { get; set; }
    public int Direita { get; set; }
    public int Base { get; set; }
    public TipoFormulario Tipo { get; set; }

    public FormularioWindows() { }
    public FormularioWindows(string nome, int largura, int altura, TipoFormulario tipo = TipoFormulario.Usuario)
    {
        Nome = nome; Largura = largura; Altura = altura; Direita = largura; Base = altura; Tipo = tipo;
    }

    /// <summary>Área imprimível = folha inteira.</summary>
    public bool SemMargem => Esquerda == 0 && Topo == 0 && Direita == Largura && Base == Altura;

    public override string ToString() => $"{Nome} {FormulariosPapel.Mm(Largura)} × {FormulariosPapel.Mm(Altura)} ({Tipo})";
}

/// <summary>O que fazer com um formato da lista.</summary>
public enum AcaoFormulario
{
    /// <summary>Já existe com o tamanho certo (e, se for de usuário, sem margem e na ordem).</summary>
    Manter,
    /// <summary>Não existe: criar.</summary>
    Criar,
    /// <summary>Existe como form de usuário com outro tamanho, com margem ou fora de ordem: apagar e criar de novo.</summary>
    Recriar,
    /// <summary>Existe como form de impressora (driver) com outro tamanho e o usuário autorizou: apagar e criar.</summary>
    Substituir,
    /// <summary>Conflito que não é resolvido (form do Windows, ou de impressora sem autorização).</summary>
    Pular,
}

/// <summary>Um passo do plano: um formato da lista e a ação sobre o formulário do Windows com o mesmo nome.</summary>
public sealed class PassoFormulario
{
    public FormatoFolha Formato { get; set; }
    /// <summary>Medidas desejadas em milésimos de mm.</summary>
    public int Largura { get; set; }
    public int Altura { get; set; }
    public AcaoFormulario Acao { get; set; }
    /// <summary>Formulário com o mesmo nome que já existia (null = não existia).</summary>
    public FormularioWindows Existente { get; set; }
    public string Motivo { get; set; } = "";

    public bool Apaga => Acao == AcaoFormulario.Recriar || Acao == AcaoFormulario.Substituir;
    public bool Adiciona => Acao == AcaoFormulario.Criar || Apaga;

    public string Descricao => $"{Formato.Nome} ({FormulariosPapel.Mm(Largura)} × {FormulariosPapel.Mm(Altura)} mm)";
}

/// <summary>Plano para deixar os formulários do Windows iguais à lista de formatos, na ordem crescente e sem margem.</summary>
public sealed class PlanoFormularios
{
    public List<PassoFormulario> Passos { get; } = new List<PassoFormulario>();

    /// <summary>Forms de impressora (driver) com o nome de um formato e outro tamanho (pedem autorização para substituir).</summary>
    public List<PassoFormulario> ConflitosImpressora => Passos
        .Where(p => p.Existente?.Tipo == TipoFormulario.Impressora && !FormulariosPapel.MesmoTamanho(p.Existente, p.Largura, p.Altura)).ToList();

    public bool NadaAFazer => Passos.All(p => p.Acao == AcaoFormulario.Manter || p.Acao == AcaoFormulario.Pular);

    public List<PassoFormulario> Com(AcaoFormulario a) => Passos.Where(p => p.Acao == a).ToList();

    /// <summary>Texto da confirmação (o que vai ser criado, recriado, substituído, mantido e pulado).</summary>
    public string TextoConfirmacao()
    {
        var l = new List<string>();
        void Grupo(AcaoFormulario a, string titulo, bool comMotivo)
        {
            var ps = Com(a);
            if (ps.Count == 0) return;
            l.Add(titulo);
            foreach (var p in ps) l.Add("   " + p.Descricao + (comMotivo && p.Motivo.Length > 0 ? " — " + p.Motivo : ""));
        }
        l.Add("Formulários de papel do Windows (valem para todas as impressoras do computador), sem margem, em ordem crescente:");
        l.Add("");
        Grupo(AcaoFormulario.Criar, "Criar:", false);
        Grupo(AcaoFormulario.Recriar, "Recriar (apagar e criar de novo):", true);
        Grupo(AcaoFormulario.Substituir, "Substituir form do driver da impressora:", true);
        Grupo(AcaoFormulario.Manter, "Já estão certos (não mexe):", false);
        Grupo(AcaoFormulario.Pular, "Não vai mexer (conflito):", true);
        if (NadaAFazer) { l.Add(""); l.Add("Nada a fazer: as folhas já estão certas."); }
        else if (Passos.Any(p => p.Apaga))
        {
            l.Add("");
            l.Add("Atenção: recriar um formulário muda o código interno do papel (UserNNNNN). Desenhos que já usavam papéis da PDF24 " +
                  "podem abrir com o papel errado no page setup: confira o papel deles. O ARMPDFLOTE procura a folha pelo nome e não é afetado.");
        }
        return string.Join(Environment.NewLine, l);
    }
}

/// <summary>Acesso ao servidor de formulários (no CAD: winspool). Devolve 0 = certo, senão o código de erro do Windows.</summary>
public interface IServidorFormularios
{
    int Apagar(string nome);
    /// <summary>Cria o formulário sem margem (área imprimível = folha inteira); medidas em milésimos de mm.</summary>
    int Adicionar(string nome, int largura, int altura);
}

/// <summary>Resultado da aplicação do plano.</summary>
public sealed class ResultadoFormularios
{
    public List<string> Criados { get; } = new List<string>();
    public List<string> Recriados { get; } = new List<string>();
    public List<string> Substituidos { get; } = new List<string>();
    public List<string> Mantidos { get; } = new List<string>();
    public List<string> Pulados { get; } = new List<string>();
    public List<string> Erros { get; } = new List<string>();
    /// <summary>O Windows negou o acesso (ERROR_ACCESS_DENIED): rodar o CAD como administrador.</summary>
    public bool FaltouPermissao { get; set; }

    public string Texto()
    {
        var l = new List<string>
        {
            $"{Criados.Count} criada(s), {Recriados.Count} recriada(s), {Substituidos.Count} substituída(s), " +
            $"{Mantidos.Count} já certa(s), {Pulados.Count} pulada(s), {Erros.Count} erro(s).",
        };
        void Grupo(string t, List<string> x) { if (x.Count > 0) l.Add(t + " " + string.Join(", ", x)); }
        Grupo("Criadas:", Criados);
        Grupo("Recriadas:", Recriados);
        Grupo("Substituídas:", Substituidos);
        Grupo("Puladas:", Pulados);
        if (Erros.Count > 0) { l.Add("Erros:"); l.AddRange(Erros.Select(e => "   " + e)); }
        if (FaltouPermissao)
            l.Add("Sem permissão para mexer nos formulários do Windows: feche o CAD, abra-o como administrador " +
                  "(botão direito → Executar como administrador) e use o botão de novo.");
        return string.Join(Environment.NewLine, l);
    }
}

/// <summary>
/// Formulários de papel do Windows para a impressora de PDF (botão "Criar folhas na impressora" do ARMPDFLOTE e o script
/// tools\criar_folhas_pdf24.ps1). O Windows lista os forms de usuário na ordem em que foram criados; para a lista da impressora
/// ficar em ordem crescente, os forms de usuário da lista que estiverem fora de ordem são apagados e criados de novo, a partir do
/// primeiro fora de ordem (os anteriores ficam, para não mudar os códigos de papel sem necessidade).
/// </summary>
public static class FormulariosPapel
{
    public const int ErroAcessoNegado = 5;
    /// <summary>Diferença aceita entre medidas iguais (milésimos de mm).</summary>
    public const int Tolerancia = 50;

    public static int Milesimos(double mm) => (int)Math.Round(mm * 1000);
    public static string Mm(int milesimos) => Fmt.Virgula(milesimos / 1000.0, "0.#");

    public static bool MesmoTamanho(FormularioWindows f, int largura, int altura) =>
        f != null && Math.Abs(f.Largura - largura) <= Tolerancia && Math.Abs(f.Altura - altura) <= Tolerancia;

    /// <summary>Formatos válidos (nome e medidas), sem nomes repetidos (vale o primeiro), em ordem crescente de área.</summary>
    public static List<FormatoFolha> EmOrdem(IEnumerable<FormatoFolha> formatos)
    {
        var vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var lista = new List<FormatoFolha>();
        foreach (var f in formatos ?? Enumerable.Empty<FormatoFolha>())
        {
            if (f == null || string.IsNullOrWhiteSpace(f.Nome) || f.Largura <= 0 || f.Altura <= 0) continue;
            if (vistos.Add(f.Nome.Trim())) lista.Add(f);
        }
        // OrderBy é estável: empate de área e de maior lado mantém a ordem da lista
        return lista.OrderBy(f => Math.Round(f.Largura * f.Altura, 3)).ThenBy(f => f.Maior).ToList();
    }

    /// <summary>
    /// Plano para a lista de <paramref name="formatos"/> diante dos forms <paramref name="existentes"/> (na ordem do EnumForms).
    /// <paramref name="substituirDaImpressora"/> = o usuário autorizou substituir forms de driver com o mesmo nome e outro tamanho.
    /// </summary>
    public static PlanoFormularios Planejar(IEnumerable<FormatoFolha> formatos, IEnumerable<FormularioWindows> existentes, bool substituirDaImpressora)
    {
        var lista = (existentes ?? Enumerable.Empty<FormularioWindows>()).Where(e => !string.IsNullOrEmpty(e?.Nome)).ToList();
        var porNome = new Dictionary<string, (FormularioWindows Form, int Pos)>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < lista.Count; i++)
            if (!porNome.ContainsKey(lista[i].Nome)) porNome[lista[i].Nome] = (lista[i], i);

        var plano = new PlanoFormularios();
        foreach (var f in EmOrdem(formatos))
        {
            var p = new PassoFormulario { Formato = f, Largura = Milesimos(f.Largura), Altura = Milesimos(f.Altura) };
            plano.Passos.Add(p);
            if (!porNome.TryGetValue(f.Nome.Trim(), out var ex)) { p.Acao = AcaoFormulario.Criar; continue; }
            p.Existente = ex.Form;
            bool igual = MesmoTamanho(ex.Form, p.Largura, p.Altura);
            switch (ex.Form.Tipo)
            {
                case TipoFormulario.Interno:
                    p.Acao = igual ? AcaoFormulario.Manter : AcaoFormulario.Pular;
                    if (!igual) p.Motivo = $"já existe como formulário do Windows com {Mm(ex.Form.Largura)} × {Mm(ex.Form.Altura)} mm (não é alterado; mude o nome no pdflote.json)";
                    break;
                case TipoFormulario.Impressora:
                    if (igual) p.Acao = AcaoFormulario.Manter;
                    else if (substituirDaImpressora)
                    {
                        p.Acao = AcaoFormulario.Substituir;
                        p.Motivo = $"o do driver tem {Mm(ex.Form.Largura)} × {Mm(ex.Form.Altura)} mm";
                    }
                    else
                    {
                        p.Acao = AcaoFormulario.Pular;
                        p.Motivo = $"form do driver da impressora com {Mm(ex.Form.Largura)} × {Mm(ex.Form.Altura)} mm (substituição não autorizada)";
                    }
                    break;
                default:
                    if (!igual) { p.Acao = AcaoFormulario.Recriar; p.Motivo = $"tinha {Mm(ex.Form.Largura)} × {Mm(ex.Form.Altura)} mm"; }
                    else if (!ex.Form.SemMargem) { p.Acao = AcaoFormulario.Recriar; p.Motivo = "tinha margem"; }
                    else p.Acao = AcaoFormulario.Manter;
                    break;
            }
        }

        // ordem: os forms de usuário que ficam têm de vir antes dos criados e na ordem da lista; do 1º fora de ordem em diante, recria
        int ultimaPos = -1;
        bool quebrou = false;
        foreach (var p in plano.Passos)
        {
            bool ficaUsuario = p.Adiciona || (p.Acao == AcaoFormulario.Manter && p.Existente?.Tipo == TipoFormulario.Usuario);
            if (!ficaUsuario) continue;
            if (p.Acao != AcaoFormulario.Manter) { quebrou = true; continue; } // criado/recriado vai para o fim
            int pos = porNome[p.Formato.Nome.Trim()].Pos;
            if (!quebrou && pos > ultimaPos) { ultimaPos = pos; continue; }
            quebrou = true;
            p.Acao = AcaoFormulario.Recriar;
            p.Motivo = "para ficar na ordem";
        }
        return plano;
    }

    /// <summary>
    /// Aplica o plano em ordem: para cada passo, apaga (recriar/substituir) e cria em seguida — assim a ordem final é a da lista e
    /// um erro deixa no máximo um formulário para trás. Sem permissão (erro 5), para no primeiro.
    /// </summary>
    public static ResultadoFormularios Aplicar(PlanoFormularios plano, IServidorFormularios servidor)
    {
        var r = new ResultadoFormularios();
        foreach (var p in plano.Passos)
        {
            if (r.FaltouPermissao) { r.Pulados.Add(p.Formato.Nome + " (sem permissão)"); continue; }
            if (p.Acao == AcaoFormulario.Manter) { r.Mantidos.Add(p.Formato.Nome); continue; }
            if (p.Acao == AcaoFormulario.Pular) { r.Pulados.Add(p.Formato.Nome); continue; }
            if (p.Apaga)
            {
                int e = servidor.Apagar(p.Existente?.Nome ?? p.Formato.Nome);
                if (e != 0)
                {
                    r.Erros.Add($"{p.Formato.Nome}: não apagou o formulário antigo (erro {e})");
                    if (e == ErroAcessoNegado) r.FaltouPermissao = true;
                    continue;
                }
            }
            int c = servidor.Adicionar(p.Formato.Nome.Trim(), p.Largura, p.Altura);
            if (c != 0)
            {
                r.Erros.Add($"{p.Formato.Nome}: não criou (erro {c})" + (p.Apaga ? " — o formulário antigo já foi apagado" : ""));
                if (c == ErroAcessoNegado) r.FaltouPermissao = true;
                continue;
            }
            (p.Acao == AcaoFormulario.Criar ? r.Criados : p.Acao == AcaoFormulario.Recriar ? r.Recriados : r.Substituidos).Add(p.Formato.Nome);
        }
        return r;
    }

    /// <summary>
    /// Instruções para criar as folhas à mão como tamanhos personalizados de uma impressora .pc3 do AutoCAD (o arquivo PMP não é
    /// gravado pelo plugin: o formato tem números codificados que ainda não foram conferidos num AutoCAD).
    /// </summary>
    public static string InstrucoesPc3(IEnumerable<FormatoFolha> formatos, string pc3)
    {
        var l = new List<string>
        {
            $"Crie as folhas como tamanhos de papel personalizados da \"{pc3}\":",
            "",
            "1. Comando PLOTTERMANAGER → abra \"" + pc3 + "\".",
            "2. Aba \"Device and Document Settings\" → \"Custom Paper Sizes\" → \"Add…\".",
            "3. \"Start from scratch\" → largura e altura abaixo em Millimeters → margens (Printable Area) todas 0 →",
            "   nome EXATAMENTE como abaixo → Finish. Repita para cada folha, nesta ordem (crescente).",
            "4. OK e salve as alterações na .pc3 (o AutoCAD grava no arquivo .pmp dela).",
            "",
        };
        foreach (var f in EmOrdem(formatos))
            l.Add($"   {f.Nome,-14} {Fmt.Virgula(f.Largura, "0.#"),8} × {Fmt.Virgula(f.Altura, "0.#")} mm");
        l.Add("");
        l.Add("Depois feche e abra o ARMPDFLOTE para conferir: as linhas sem a folha na impressora avisam \"crie as folhas na impressora\".");
        return string.Join(Environment.NewLine, l);
    }
}

/// <summary>Como a impressora escolhida no ARMPDFLOTE salva o PDF.</summary>
public enum TipoImpressoraPdf
{
    /// <summary>PDF24: salvamento automático temporário (registro).</summary>
    Pdf24,
    /// <summary>Configuração .pc3 do CAD (ex.: AutoCAD PDF): plotar para arquivo.</summary>
    Pc3,
    /// <summary>Outra impressora do Windows: o lote não salva sem diálogo.</summary>
    Outra,
}

public static class ImpressorasPdf
{
    /// <summary>Impressora padrão do lote no AutoCAD (plotar para arquivo, sem diálogo).</summary>
    public const string AutoCadPdf = "AutoCAD PDF (High Quality Print).pc3";
    public const string Pdf24 = "PDF24";

    public static TipoImpressoraPdf Tipo(string impressora)
    {
        if (string.IsNullOrWhiteSpace(impressora)) return TipoImpressoraPdf.Outra;
        if (impressora.IndexOf("PDF24", StringComparison.OrdinalIgnoreCase) >= 0) return TipoImpressoraPdf.Pdf24;
        if (impressora.Trim().EndsWith(".pc3", StringComparison.OrdinalIgnoreCase)) return TipoImpressoraPdf.Pc3;
        return TipoImpressoraPdf.Outra;
    }
}
