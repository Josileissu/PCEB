using System;
using System.IO;
using System.Text;

namespace DEB.Core.Motor.Receitas;

/// <summary>
/// Receitas de fábrica embutidas no DEB.Core (arquivos em Motor/Receitas/Fabrica/*.json, recurso "DEB.Receitas.&lt;tipo&gt;.json").
/// <see cref="Garantir"/> registra todas no <see cref="RepositorioReceitas"/> (idempotente).
/// </summary>
public static class ReceitasFabrica
{
    public static readonly string[] Tipos = { ElementoBloco.TipoId };
    private static bool _registradas;
    private static readonly object _trava = new object();

    public static void Garantir()
    {
        lock (_trava)
        {
            if (_registradas) return;
            foreach (var t in Tipos) { var tipo = t; RepositorioReceitas.RegistrarFabrica(tipo, () => SerializacaoReceita.Ler(Texto(tipo))); }
            _registradas = true;
        }
    }

    /// <summary>Texto JSON da receita de fábrica embutida.</summary>
    public static string Texto(string tipo)
    {
        var asm = typeof(ReceitasFabrica).Assembly;
        using (var s = asm.GetManifestResourceStream("DEB.Receitas." + tipo + ".json"))
        {
            if (s == null) throw new InvalidOperationException($"Receita de fábrica \"{tipo}\" não embutida no DEB.Core.");
            using (var r = new StreamReader(s, Encoding.UTF8)) return r.ReadToEnd();
        }
    }
}
