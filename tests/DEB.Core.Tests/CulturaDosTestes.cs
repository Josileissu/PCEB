using System;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace DEB.Core.Tests;

/// <summary>
/// Roda os testes como se o Windows estivesse noutro idioma (2026-11-10): o programa é livre e vai rodar em máquinas que não são
/// pt-BR — número com ponto ou vírgula no lugar errado só aparece assim. Uso:
///   $env:ARM_CULTURA = "en-US"; dotnet test tests\DEB.Core.Tests
/// Sem a variável, vale a cultura do computador (pt-BR nas máquinas do projeto).
/// </summary>
internal static class CulturaDosTestes
{
    [ModuleInitializer]
    internal static void Aplicar()
    {
        string nome = Environment.GetEnvironmentVariable("ARM_CULTURA");
        if (string.IsNullOrWhiteSpace(nome)) return;
        var c = CultureInfo.GetCultureInfo(nome.Trim());
        CultureInfo.DefaultThreadCurrentCulture = c;
        CultureInfo.DefaultThreadCurrentUICulture = c;
        CultureInfo.CurrentCulture = c;
        CultureInfo.CurrentUICulture = c;
    }
}
