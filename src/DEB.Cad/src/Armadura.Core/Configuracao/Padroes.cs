using System.Linq;
using System.Reflection;

namespace Armadura.Core.Configuracao;

internal static class Padroes
{
    /// <summary>Copia as propriedades públicas graváveis de <paramref name="origem"/> para <paramref name="destino"/>.</summary>
    public static void Copiar<T>(T origem, T destino)
    {
        foreach (var p in typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                     .Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0))
            p.SetValue(destino, p.GetValue(origem));
    }
}
