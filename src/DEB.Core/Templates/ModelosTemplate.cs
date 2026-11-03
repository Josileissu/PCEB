using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using DEB.Core.Persistencia;

namespace DEB.Core.Templates;

/// <summary>Um modelo salvo de um template (ex.: o pilar "P61" com todos os campos preenchidos).</summary>
public sealed class ModeloTemplate
{
    public string Nome { get; set; } = "";
    public Dictionary<string, string> Valores { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Arquivo de modelos de um template (pedido do usuário, 2026-10-30): CSV com ";" (abre no Excel), uma linha por modelo e uma
/// coluna por campo. A linha "PADRÃO" dá os valores com que a janela abre; as outras são os elementos já desenhados (salvos
/// sozinhos pelo ARMTEMPLATE) ou criados à mão. Campos que faltam ficam com o padrão do plugin; colunas desconhecidas são
/// mantidas. Números com vírgula decimal (o Excel em português não troca por data).
/// </summary>
public sealed class ArquivoModelos
{
    public const string NomePadrao = "PADRÃO";

    public string TemplateId { get; set; } = "";
    public ModeloTemplate Padrao { get; set; } = new ModeloTemplate { Nome = NomePadrao };
    public List<ModeloTemplate> Modelos { get; } = new List<ModeloTemplate>();
    /// <summary>Colunas na ordem do arquivo (as do template primeiro, depois as que o template não tem mais).</summary>
    private readonly List<string> _colunasExtras = new List<string>();

    /// <summary>Arquivo novo: a linha PADRÃO com os padrões do plugin.</summary>
    public static ArquivoModelos Novo(ITemplateArmadura t)
    {
        var a = new ArquivoModelos { TemplateId = t.Id };
        foreach (var p in t.Parametros) a.Padrao.Valores[p.Nome] = p.Padrao;
        return a;
    }

    /// <summary>Valores para a janela: padrão do plugin ← linha PADRÃO ← modelo (se dado).</summary>
    public ValoresTemplate Valores(ITemplateArmadura t, string modelo = null)
    {
        var v = new ValoresTemplate(t.Parametros);
        var nomes = new HashSet<string>(t.Parametros.Select(p => p.Nome), StringComparer.OrdinalIgnoreCase);
        void Aplicar(ModeloTemplate m)
        {
            if (m == null) return;
            foreach (var kv in m.Valores) if (nomes.Contains(kv.Key) && kv.Value.Length > 0) v[kv.Key] = kv.Value;
        }
        Aplicar(Padrao);
        if (!string.IsNullOrEmpty(modelo)) Aplicar(Obter(modelo));
        return v;
    }

    public ModeloTemplate Obter(string nome) => Modelos.FirstOrDefault(m => m.Nome.Equals(nome, StringComparison.OrdinalIgnoreCase));

    /// <summary>Grava (ou substitui) o modelo com os valores atuais. Nome "PADRÃO" = a linha padrão.</summary>
    public void Salvar(string nome, ValoresTemplate v, IEnumerable<ParametroTemplate> parametros)
    {
        nome = (nome ?? "").Trim();
        if (nome.Length == 0) nome = "SEM NOME";
        var m = nome.Equals(NomePadrao, StringComparison.OrdinalIgnoreCase) ? Padrao : Obter(nome);
        if (m == null) { m = new ModeloTemplate { Nome = nome }; Modelos.Add(m); }
        foreach (var p in parametros) m.Valores[p.Nome] = v[p.Nome];
    }

    public bool Excluir(string nome) => Modelos.RemoveAll(m => m.Nome.Equals(nome, StringComparison.OrdinalIgnoreCase)) > 0;

    /// <summary>Importa outro arquivo: os modelos entram (os de mesmo nome são substituídos) e o PADRÃO dele passa a valer.</summary>
    public int Importar(ArquivoModelos outro)
    {
        foreach (var kv in outro.Padrao.Valores) Padrao.Valores[kv.Key] = kv.Value;
        foreach (var m in outro.Modelos)
        {
            Modelos.RemoveAll(x => x.Nome.Equals(m.Nome, StringComparison.OrdinalIgnoreCase));
            Modelos.Add(m);
        }
        foreach (var c in outro._colunasExtras) if (!_colunasExtras.Contains(c, StringComparer.OrdinalIgnoreCase)) _colunasExtras.Add(c);
        return outro.Modelos.Count;
    }

    // ======================== arquivo ========================

    private static bool EhNumero(ParametroTemplate p) =>
        p != null && (p.Tipo == TipoParametro.Numero || p.Tipo == TipoParametro.Inteiro || p.Tipo == TipoParametro.Bitola);

    public string Escrever(ITemplateArmadura t)
    {
        var ps = t.Parametros.ToDictionary(p => p.Nome, StringComparer.OrdinalIgnoreCase);
        var colunas = t.Parametros.Select(p => p.Nome)
            .Concat(_colunasExtras.Where(c => !ps.ContainsKey(c))).ToList();
        var sb = new StringBuilder();
        sb.AppendLine($"# template: {t.Id} — {t.Nome}. Modelos do Detalhador Estrutural Brasileiro: uma linha por elemento, separador ponto e vírgula.");
        sb.AppendLine("# A linha PADRÃO é a que abre na janela do ARMTEMPLATE; as outras são os elementos desenhados (salvos sozinhos) ou criados aqui.");
        sb.AppendLine("# Campo vazio = padrão. Listas (níveis, consolos…) ficam entre aspas: itens separados por \";\" e colunas por \"|\".");
        sb.AppendLine(Csv.Linha(new[] { "Modelo" }.Concat(colunas)));
        sb.AppendLine("#" + Csv.Linha(new[] { "(campo)" }.Concat(colunas.Select(c => ps.TryGetValue(c, out var p) ? $"{p.Grupo.Substring(Math.Min(4, p.Grupo.Length))}: {p.Rotulo}" : "(não usado)"))));
        foreach (var m in new[] { Padrao }.Concat(Modelos))
            sb.AppendLine(Csv.Linha(new[] { m.Nome }.Concat(colunas.Select(c =>
            {
                string s = m.Valores.TryGetValue(c, out var x) ? x : "";
                return ps.TryGetValue(c, out var p) && EhNumero(p) ? s.Replace('.', ',') : s;
            }))));
        return sb.ToString();
    }

    /// <summary>Lê o arquivo (o template dá os tipos para converter a vírgula decimal).</summary>
    public static ArquivoModelos Ler(string texto, ITemplateArmadura t, List<string> avisos = null)
    {
        var a = new ArquivoModelos { TemplateId = t?.Id ?? "" };
        var ps = (t?.Parametros ?? Array.Empty<ParametroTemplate>()).ToDictionary(p => p.Nome, StringComparer.OrdinalIgnoreCase);
        List<string> colunas = null;
        bool temPadrao = false;
        foreach (var linha in Csv.Registros(texto ?? ""))
        {
            if (linha.Count == 0 || (linha.Count == 1 && linha[0].Trim().Length == 0)) continue;
            string primeira = linha[0].TrimStart('﻿').Trim();
            if (primeira.StartsWith("#"))
            {
                // "# template: <id>" identifica o arquivo
                int k = primeira.IndexOf("template:", StringComparison.OrdinalIgnoreCase);
                if (k >= 0) a.TemplateId = primeira.Substring(k + 9).Trim().Split(' ', '—')[0].Trim();
                continue;
            }
            if (colunas == null)
            {
                colunas = linha.Skip(1).Select(c => c.Trim()).ToList();
                foreach (var c in colunas) if (c.Length > 0 && !ps.ContainsKey(c)) a._colunasExtras.Add(c);
                continue;
            }
            var m = new ModeloTemplate { Nome = primeira };
            if (m.Nome.Length == 0) continue;
            for (int i = 0; i < colunas.Count && i + 1 < linha.Count; i++)
            {
                if (colunas[i].Length == 0) continue;
                string v = linha[i + 1].Trim();
                if (ps.TryGetValue(colunas[i], out var p) && EhNumero(p)) v = v.Replace(',', '.');
                m.Valores[colunas[i]] = v;
            }
            if (m.Nome.Equals(NomePadrao, StringComparison.OrdinalIgnoreCase) || m.Nome.Equals("PADRAO", StringComparison.OrdinalIgnoreCase))
            {
                m.Nome = NomePadrao;
                a.Padrao = m;
                temPadrao = true;
            }
            else
            {
                if (a.Obter(m.Nome) != null) avisos?.Add($"Modelo \"{m.Nome}\" repetido no arquivo: vale o último.");
                a.Excluir(m.Nome);
                a.Modelos.Add(m);
            }
        }
        if (colunas == null) avisos?.Add("Arquivo de modelos sem cabeçalho (linha \"Modelo;…\").");
        if (!temPadrao && t != null) foreach (var p in t.Parametros) a.Padrao.Valores[p.Nome] = p.Padrao;
        return a;
    }
}
