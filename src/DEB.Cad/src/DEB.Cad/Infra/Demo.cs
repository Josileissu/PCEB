using System;
using System.Globalization;
using System.IO;
using Microsoft.Win32;

namespace DEB.Cad.Infra;

/// <summary>
/// Versão de demonstração (pedido do usuário, 2026-10-30): compilada com <c>-p:Demo=true</c> (constante DEMO), funciona por
/// 1 hora a partir da primeira vez que o plugin é carregado no computador. O início fica no registro e num arquivo (vale o
/// mais antigo); relógio voltado para trás conta como expirado. Depois de expirar, os comandos e a paleta não alteram nada;
/// os desenhos continuam abrindo e sendo exibidos normalmente.
/// </summary>
internal static class Demo
{
    // readonly (e não const): sem avisos de "código inacessível" nas duas compilações
#if DEMO
    public static readonly bool Ativa = true;
#else
    public static readonly bool Ativa = false;
#endif
    public static readonly TimeSpan Duracao = TimeSpan.FromHours(1);
    private const string Chave = @"Software\ArmaduraBR\Demo";
    private static string Arquivo => Path.Combine(Log.PastaDados, "demo.dat");
    private static DateTime? _inicio;

    /// <summary>Grava o início na primeira carga (chamado no Initialize).</summary>
    public static void Iniciar()
    {
        if (!Ativa) return;
        var lidos = new[] { LerRegistro(), LerArquivo() };
        DateTime agora = DateTime.UtcNow, inicio = agora;
        foreach (var d in lidos) if (d.HasValue && d.Value < inicio) inicio = d.Value;
        _inicio = inicio;
        string txt = inicio.Ticks.ToString(CultureInfo.InvariantCulture);
        try { using var k = Registry.CurrentUser.CreateSubKey(Chave); k?.SetValue("Inicio", txt); } catch { }
        try { Directory.CreateDirectory(Log.PastaDados); File.WriteAllText(Arquivo, txt); } catch { }
        Log.Info($"demonstração: início {inicio:u}, restam {Math.Max(0, (inicio + Duracao - agora).TotalMinutes):0} min.");
    }

    /// <summary>Tempo que falta (zero quando expirou). Fora da versão demo: infinito.</summary>
    public static TimeSpan Restante
    {
        get
        {
            if (!Ativa) return TimeSpan.MaxValue;
            if (_inicio == null) Iniciar();
            var agora = DateTime.UtcNow;
            if (agora < _inicio.Value.AddMinutes(-5)) return TimeSpan.Zero; // relógio voltado
            var r = _inicio.Value + Duracao - agora;
            return r > TimeSpan.Zero ? r : TimeSpan.Zero;
        }
    }

    public static bool Expirada => Ativa && Restante <= TimeSpan.Zero;

    public const string Mensagem = "A demonstração do Detalhador Estrutural Brasileiro terminou (1 hora de uso). Para continuar, fale com a Marquezin.";

    public static string TextoRestante => $"Detalhador Estrutural Brasileiro — DEMONSTRAÇÃO: restam {Math.Max(0, (int)Math.Ceiling(Restante.TotalMinutes))} min.";

    private static DateTime? LerRegistro()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(Chave);
            return Converter(k?.GetValue("Inicio") as string);
        }
        catch { return null; }
    }

    private static DateTime? LerArquivo()
    {
        try { return File.Exists(Arquivo) ? Converter(File.ReadAllText(Arquivo)) : null; }
        catch { return null; }
    }

    private static DateTime? Converter(string s) =>
        long.TryParse((s ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var t) && t > 0 && t < DateTime.MaxValue.Ticks
            ? new DateTime(t, DateTimeKind.Utc) : (DateTime?)null;
}
