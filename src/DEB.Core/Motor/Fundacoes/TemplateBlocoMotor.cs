using System;
using System.Collections.Generic;
using System.Linq;
using DEB.Core.Configuracao;
using DEB.Core.Modelo;
using DEB.Core.Motor.Entrada;
using DEB.Core.Motor.Perfis;
using DEB.Core.Motor.Prancha;
using DEB.Core.Motor.Receitas;
using DEB.Core.Templates;
using DEB.Core.Templates.Blocos;

namespace DEB.Core.Motor.Fundacoes;

/// <summary>
/// O bloco sobre estacas no ARMTEMPLATE, gerado pelo MOTOR (2026-11-07): mesma janela, mesmos parâmetros e mesmo arquivo
/// de modelos do <see cref="TemplateBlocoEstacas"/> (Id igual), mas o desenho sai de
/// janela → <see cref="ConversorBloco"/> → perfil + receita (fábrica ou arquivo do usuário) → <see cref="MotorBlocoEstacas"/>
/// → <see cref="GeradorPrancha"/>. O que gerou o desenho fica em <see cref="DadosDetalhamento.Motor"/>
/// (<see cref="OrigemDetalhamento"/>) para regenerar depois com as edições manuais.
/// </summary>
public sealed class TemplateBlocoMotor : ITemplateArmadura
{
    public const string PerfilPadrao = "bpm_bloco";

    private readonly TemplateBlocoEstacas _janela;
    private readonly string _pastaPerfis, _pastaReceitas;

    /// <param name="pastaPerfis">pasta dos perfis do usuário (null = só fábrica); o CAD passa <see cref="RepositorioPerfis.PastaPadrao"/></param>
    /// <param name="pastaReceitas">pasta das receitas do usuário (null = só fábrica)</param>
    public TemplateBlocoMotor(bool umaEstaca = false, string pastaPerfis = null, string pastaReceitas = null)
    {
        _janela = new TemplateBlocoEstacas(umaEstaca);
        _pastaPerfis = pastaPerfis;
        _pastaReceitas = pastaReceitas;
    }

    /// <summary>Com as pastas do usuário em %AppData%\PCEB (o que o CAD usa).</summary>
    public static TemplateBlocoMotor DoUsuario(bool umaEstaca = false) =>
        new TemplateBlocoMotor(umaEstaca, RepositorioPerfis.PastaPadrao(), RepositorioReceitas.PastaPadrao());

    public string Id => _janela.Id;
    public string Nome => _janela.Nome;
    public CategoriaElemento Categoria => _janela.Categoria;
    public string Descricao => _janela.Descricao;
    public IReadOnlyList<ParametroTemplate> Parametros => _janela.Parametros;

    public ResultadoTemplate Gerar(ValoresTemplate valores, ConfiguracaoArmadura cfg)
    {
        ElementoBloco e;
        SobreposicoesPerfil sob;
        try { e = ConversorBloco.DeValores(valores, out sob); }
        catch (Exception ex)
        {
            var r = new ResultadoTemplate();
            r.Avisos.Add("Bloco: valores da janela inválidos (" + ex.Message + ").");
            return r;
        }
        return Gerar(new OrigemDetalhamento { Perfil = PerfilPadrao, Elemento = e, SobreposicoesPerfil = sob }, cfg);
    }

    /// <summary>Gera (ou regenera) a partir da origem gravada no detalhamento: elemento + perfil + sobrescritas.</summary>
    public ResultadoTemplate Gerar(OrigemDetalhamento origem, ConfiguracaoArmadura cfg)
    {
        if (origem?.Elemento == null) throw new ArgumentNullException(nameof(origem));
        cfg ??= new ConfiguracaoArmadura();
        var avisos = new List<string>();
        string idPerfil = string.IsNullOrWhiteSpace(origem.Perfil) ? PerfilPadrao : origem.Perfil;
        var rp = new RepositorioPerfis(_pastaPerfis).Obter(idPerfil);
        avisos.AddRange(rp.Avisos);
        var perfil = rp.Perfil ?? PerfisFabrica.Obter(PerfilPadrao);
        origem.SobreposicoesPerfil?.AplicarEm(perfil);

        ReceitasFabrica.Garantir();
        var rr = new RepositorioReceitas(_pastaReceitas).Obter(origem.Elemento.Tipo);
        avisos.AddRange(rr.Avisos);

        ResultadoTemplate r;
        try
        {
            var modelo = new MotorBlocoEstacas(cfg, rr.Receita).Gerar((ElementoBloco)origem.Elemento, perfil, origem.Sobrescritas);
            r = new GeradorPrancha(cfg).Gerar(origem.Elemento, modelo, perfil);
            foreach (var a in modelo.Avisos) if (!r.Avisos.Contains(a)) r.Avisos.Add(a);
        }
        catch (Exception ex)
        {
            r = new ResultadoTemplate();
            r.Avisos.Add("Bloco: erro inesperado no motor (" + ex.GetType().Name + ": " + ex.Message + ").");
            return r;
        }
        r.Avisos.InsertRange(0, avisos.Where(a => !r.Avisos.Contains(a)));
        r.DadosDetalhamento ??= new DadosDetalhamento();
        try { r.DadosDetalhamento.Motor = origem.Gravar(); }
        catch (Exception ex) { r.Avisos.Add("Bloco: a origem do desenho não foi gravada (" + ex.Message + "); não dará para regenerar."); }
        return r;
    }
}
