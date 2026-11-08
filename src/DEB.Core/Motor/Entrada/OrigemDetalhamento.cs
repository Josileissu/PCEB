using System;
using System.Collections.Generic;
using System.Linq;
using DEB.Core.Motor.Json;
using DEB.Core.Persistencia;
using static DEB.Core.Persistencia.LeitorJson;
using TextoJson = DEB.Core.Persistencia.Json;

namespace DEB.Core.Motor.Entrada;

/// <summary>
/// O que gerou um detalhamento pelo motor: elemento (dados), perfil (id), sobreposições do perfil vindas da janela
/// (pernas, cobrimentos, escala…) e sobrescritas (edições manuais). Fica no detalhamento do CAD
/// (<see cref="Modelo.DadosDetalhamento.Motor"/>, decisão do usuário em 2026-11-07) para regenerar o desenho depois.
/// </summary>
public sealed class OrigemDetalhamento
{
    public const int Versao = 1;

    public string Perfil { get; set; } = "";
    public ElementoEstrutural Elemento { get; set; }
    public SobreposicoesPerfil SobreposicoesPerfil { get; set; }
    public List<Sobrescrita> Sobrescritas { get; set; } = new List<Sobrescrita>();

    /// <summary>JSON compacto (sem indentação: vai para o XData).</summary>
    public string Gravar()
    {
        if (Elemento == null) throw new InvalidOperationException("Origem sem elemento.");
        var o = ValorJson.NovoObjeto()
            .Definir("versao", Versao)
            .Definir("perfil", Perfil ?? "")
            .Definir("elemento", SerializacaoMotor.ParaJson(Elemento));
        if (SobreposicoesPerfil != null) o.Definir("sobreposicoes", ParaJson(SobreposicoesPerfil));
        o.Definir("sobrescritas", SerializacaoMotor.ParaJson(Sobrescritas));
        return TextoJson.Gravar(o, indentado: false);
    }

    /// <summary>Lê o JSON gravado por <see cref="Gravar"/>; lança <see cref="FormatException"/> se estiver inválido.</summary>
    public static OrigemDetalhamento Ler(string texto)
    {
        const string c = "origem";
        var o = Objeto(TextoJson.Ler(texto), c);
        int v = Int(o, "versao", Versao, c);
        if (v > Versao) throw new FormatException($"{c}.versao: versão {v} é mais nova que a suportada ({Versao}); atualize o programa.");
        var r = new OrigemDetalhamento
        {
            Perfil = Txt(o, "perfil", "", c),
            Elemento = SerializacaoMotor.ElementoDeJson(o["elemento"], c + ".elemento"),
            Sobrescritas = SerializacaoMotor.SobrescritasDeJson(o["sobrescritas"], c + ".sobrescritas"),
        };
        if (Presente(o, "sobreposicoes")) r.SobreposicoesPerfil = SobreposicoesDeJson(o["sobreposicoes"], c + ".sobreposicoes");
        return r;
    }

    /// <summary>Lê sem lançar: null + o motivo quando o texto está vazio ou inválido.</summary>
    public static OrigemDetalhamento TentarLer(string texto, out string erro)
    {
        erro = null;
        if (string.IsNullOrWhiteSpace(texto)) { erro = "o detalhamento não foi gerado pelo motor."; return null; }
        try { return Ler(texto); }
        catch (Exception e) { erro = e.Message; return null; }
    }

    private static ValorJson ParaJson(SobreposicoesPerfil s)
    {
        var p = ValorJson.NovoObjeto();
        foreach (var kv in s.Padroes.OrderBy(k => k.Key, StringComparer.Ordinal)) p.Definir(kv.Key, kv.Value);
        var g = ValorJson.NovoObjeto();
        foreach (var kv in s.Regras.OrderBy(k => k.Key, StringComparer.Ordinal)) g.Definir(kv.Key, kv.Value);
        return ValorJson.NovoObjeto().Definir("padroes", p).Definir("regras", g)
            .Definir("escala", s.Escala).Definir("idioma", s.Idioma ?? "PT").Definir("forma", s.Forma).Definir("detalhes", s.Detalhes);
    }

    private static SobreposicoesPerfil SobreposicoesDeJson(ValorJson o, string c)
    {
        Objeto(o, c);
        var s = new SobreposicoesPerfil();
        foreach (var kv in DicNum(o, "padroes", new Dictionary<string, double>(), c)) s.Padroes[kv.Key] = kv.Value;
        foreach (var kv in DicTxt(o, "regras", new Dictionary<string, string>(), c)) s.Regras[kv.Key] = kv.Value;
        s.Escala = Num(o, "escala", s.Escala, c);
        s.Idioma = Txt(o, "idioma", s.Idioma, c);
        s.Forma = Bool(o, "forma", s.Forma, c);
        s.Detalhes = Bool(o, "detalhes", s.Detalhes, c);
        return s;
    }
}
