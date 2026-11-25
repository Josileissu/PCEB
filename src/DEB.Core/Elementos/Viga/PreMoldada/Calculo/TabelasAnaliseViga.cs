using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DEB.Core.Configuracao.Projeto;

namespace DEB.Core.Elementos.Viga.PreMoldada.Calculo;

/// <summary>
/// As tabelas da análise da viga no desenho são a ENTRADA (D51: "quero digitar direto no CAD"): o engenheiro edita as células e o
/// comando lê de volta. Cada valor é lido na unidade escrita na coluna "UN." da própria linha (a tabela continua certa se a
/// configuração de unidades mudar depois). Valores calculados (carga linear da laje, PP da viga) não são lidos.
/// </summary>
public static class TabelasAnaliseViga
{
    public const string TLaje = "CARGA DA LAJE", TViga = "CARGA DA VIGA", TPontuais = "CARGAS PONTUAIS", TParametros = "PARÂMETROS DA ANÁLISE",
        TModelos = "MODELOS (ENVOLTÓRIA)";

    public const string PApoioE = "Apoio esquerdo (da ponta)", PApoioD = "Apoio direito (da ponta)", PRotulado = "Modelo rotulado",
        PSemi = "Modelo semirrígido", PAlfaE = "αR esquerdo", PAlfaD = "αR direito", PFator = "Rigidez secante (× Eci·Ic)",
        PTorcao = "Torção (laje na aba)";

    /// <summary>Linhas da tabela de parâmetros (PARÂMETRO, VALOR, UN.).</summary>
    public static List<string[]> LinhasParametros(EntradaAnaliseViga e)
    {
        string N(double x) => Fmt.Virgula(x, "0.###");
        string S(bool b) => b ? "Sim" : "Não";
        return new List<string[]>
        {
            new[] { "PARÂMETRO", "VALOR", "UN." },
            new[] { PApoioE, N(e.ApoioE), "cm" },
            new[] { PApoioD, N(e.ApoioD), "cm" },
            new[] { PRotulado, S(e.Rotulado), "" },
            new[] { PSemi, S(e.Semirrigido), "" },
            new[] { PAlfaE, N(e.AlfaRE), "" },
            new[] { PAlfaD, N(e.AlfaRD), "" },
            new[] { PFator, N(e.FatorRigidezSecante), "" },
            new[] { PTorcao, S(e.Torcao), "" },
        };
    }

    /// <summary>Lê a entrada da análise das tabelas do desenho (título → linhas, com a de cabeçalho). Erros de leitura vão para a lista.</summary>
    public static EntradaAnaliseViga Ler(IEnumerable<(string Titulo, List<string[]> Linhas)> tabelas, List<string> erros = null)
    {
        erros ??= new List<string>();
        var e = new EntradaAnaliseViga();
        var porTitulo = tabelas.GroupBy(t => Norm(t.Titulo)).ToDictionary(g => g.Key, g => g.First().Linhas);
        List<string[]> Tab(string t) => porTitulo.TryGetValue(Norm(t), out var l) ? l.Skip(1).ToList() : null;
        string C(string[] l, int i) => i < l.Length ? (l[i] ?? "").Trim() : "";

        double Num(string s, string onde)
        {
            if (string.IsNullOrWhiteSpace(s)) return 0;
            if (double.TryParse(s.Trim().Replace(" ", "").Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double v)) return v;
            erros.Add($"{onde}: \"{s}\" não é número (ficou 0).");
            return 0;
        }
        double Base(Grandeza g, string valor, string un, string onde)
        {
            double v = Num(valor, onde);
            if (string.IsNullOrWhiteSpace(un)) return v;
            if (!Unidades.Aceita(g, un.Trim())) { erros.Add($"{onde}: unidade \"{un}\" não reconhecida (use {string.Join(", ", Unidades.Opcoes(g))})."); return v; }
            return Unidades.ParaBase(g, v, un.Trim());
        }

        var laje = Tab(TLaje);
        if (laje == null) erros.Add($"Tabela \"{TLaje}\" não encontrada.");
        else
            foreach (var l in laje)
            {
                var c = new CargaLaje { Valor = Base(Grandeza.CargaSuperficie, C(l, 1), C(l, 2), $"{TLaje}, {C(l, 0)}"), VaoInfluencia = Num(C(l, 3), $"{TLaje}, {C(l, 0)} (vão)") };
                switch (Norm(C(l, 0)))
                {
                    case "PP,LAJE": e.LajePP = c; break;
                    case "CP,LAJE": e.LajeCP = c; break;
                    case "SC,LAJE": e.LajeSC = c; break;
                }
            }
        var viga = Tab(TViga);
        if (viga == null) erros.Add($"Tabela \"{TViga}\" não encontrada.");
        else
            foreach (var l in viga)
            {
                switch (Norm(C(l, 0)))
                {
                    case "CP,VIGA": e.VigaCP = Base(Grandeza.CargaLinear, C(l, 1), C(l, 2), $"{TViga}, CP,viga"); break;
                    case "SC,VIGA": e.VigaSC = Base(Grandeza.CargaLinear, C(l, 1), C(l, 2), $"{TViga}, SC,viga"); break;
                }
            }
        foreach (var l in Tab(TPontuais) ?? new List<string[]>())
        {
            if (string.IsNullOrWhiteSpace(C(l, 0)) && string.IsNullOrWhiteSpace(C(l, 1)) && string.IsNullOrWhiteSpace(C(l, 2))) continue;
            string onde = $"{TPontuais}, x = {C(l, 0)}";
            e.Pontuais.Add(new CargaConcentrada
            {
                X = Num(C(l, 0), onde), G = Base(Grandeza.Forca, C(l, 1), C(l, 3), onde), Q = Base(Grandeza.Forca, C(l, 2), C(l, 3), onde),
            });
        }
        var par = Tab(TParametros);
        if (par != null)
            foreach (var l in par)
            {
                string k = C(l, 0), v = C(l, 1);
                bool Sim() => Norm(v) == "SIM" || v == "1";
                if (k == PApoioE) e.ApoioE = Num(v, k);
                else if (k == PApoioD) e.ApoioD = Num(v, k);
                else if (k == PRotulado) e.Rotulado = Sim();
                else if (k == PSemi) e.Semirrigido = Sim();
                else if (k == PAlfaE) e.AlfaRE = Faixa(Num(v, k), k);
                else if (k == PAlfaD) e.AlfaRD = Faixa(Num(v, k), k);
                else if (k == PFator) e.FatorRigidezSecante = Num(v, k);
                else if (k == PTorcao) e.Torcao = Sim();
            }
        if (!e.Rotulado && !e.Semirrigido) { erros.Add("Nenhum modelo ligado: a análise usa o rotulado."); e.Rotulado = true; }
        return e;

        // αR digitado em % (10) ou em fração (0,1)
        double Faixa(double a, string onde)
        {
            if (a > 1) a /= 100;
            if (a < 0 || a > 1) { erros.Add($"{onde}: αR fora de 0 a 1 (ficou 0,1)."); return 0.1; }
            return a;
        }
    }

    private static string Norm(string s) => (s ?? "").Trim().Replace(" ", "").ToUpperInvariant();

    /// <summary>Texto de uma célula do CAD sem a formatação do MTEXT ({\fArial|b0;500}, \P, \~…).</summary>
    public static string TextoCelula(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\\[ACcFfHhQqTtWw][^;\\{}]*;", "");
        s = s.Replace("\\P", " ").Replace("\\~", " ").Replace("\\L", "").Replace("\\l", "").Replace("\\O", "").Replace("\\o", "")
             .Replace("{", "").Replace("}", "");
        return s.Trim();
    }

}
