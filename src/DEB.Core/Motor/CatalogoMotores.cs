using System;
using System.Collections.Generic;
using System.Linq;

namespace DEB.Core.Motor;

/// <summary>
/// Registro dos motores de detalhamento por tipo de elemento (<see cref="ElementoEstrutural.Tipo"/>) e serviço de
/// regeneração: elemento + perfil + sobrescritas → <see cref="ModeloArmadura"/>, despachando para o motor do tipo.
/// </summary>
public sealed class CatalogoMotores
{
    private sealed class Registro
    {
        public Type TipoElemento;
        public Func<ElementoEstrutural, PerfilDetalhamento, IReadOnlyList<Sobrescrita>, ModeloArmadura> Gerar;
    }

    private readonly Dictionary<string, Registro> _motores = new Dictionary<string, Registro>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Registra (ou substitui) o motor do tipo <see cref="IMotorDetalhamento{T}.TipoElemento"/>.</summary>
    public CatalogoMotores Registrar<T>(IMotorDetalhamento<T> motor) where T : ElementoEstrutural
    {
        if (motor == null) throw new ArgumentNullException(nameof(motor));
        if (string.IsNullOrWhiteSpace(motor.TipoElemento))
            throw new ArgumentException($"O motor {motor.GetType().Name} não informa o tipo de elemento.", nameof(motor));
        _motores[motor.TipoElemento] = new Registro
        {
            TipoElemento = typeof(T),
            Gerar = (e, p, s) => e is T t
                ? motor.Gerar(t, p, s)
                : throw new ArgumentException($"O motor de \"{motor.TipoElemento}\" espera {typeof(T).Name}, mas recebeu {e.GetType().Name}."),
        };
        return this;
    }

    public bool Tem(string tipoElemento) => tipoElemento != null && _motores.ContainsKey(tipoElemento);

    public IReadOnlyList<string> Tipos => _motores.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();

    /// <summary>Gera o modelo de armadura do elemento com o motor do tipo dele. Perfil nulo = perfil vazio (o motor usa os
    /// padrões de fábrica); sobrescritas nulas = nenhuma.</summary>
    /// <exception cref="InvalidOperationException">Nenhum motor registrado para o tipo do elemento.</exception>
    /// <exception cref="ArgumentException">O perfil é de outro tipo de elemento.</exception>
    public ModeloArmadura Gerar(ElementoEstrutural elemento, PerfilDetalhamento perfil, IReadOnlyList<Sobrescrita> sobrescritas)
    {
        if (elemento == null) throw new ArgumentNullException(nameof(elemento));
        string tipo = elemento.Tipo ?? "";
        if (!_motores.TryGetValue(tipo, out var r))
            throw new InvalidOperationException(
                $"Não há motor de detalhamento para o tipo \"{tipo}\" ({elemento.GetType().Name} \"{elemento.Nome}\"). " +
                (_motores.Count == 0 ? "Nenhum motor registrado." : "Registrados: " + string.Join(", ", Tipos) + "."));
        perfil ??= new PerfilDetalhamento { TipoElemento = tipo };
        if (!string.IsNullOrEmpty(perfil.TipoElemento) && !perfil.TipoElemento.Equals(tipo, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"O perfil \"{perfil.Id}\" é de \"{perfil.TipoElemento}\", não de \"{tipo}\".", nameof(perfil));
        return r.Gerar(elemento, perfil, sobrescritas ?? Array.Empty<Sobrescrita>());
    }
}

/// <summary>Utilitários da lista de sobrescritas (edições manuais) de um elemento.</summary>
public static class Sobrescritas
{
    private static bool Mesma(Sobrescrita s, string posicao, string parametro) =>
        string.Equals(s.Posicao, posicao, StringComparison.Ordinal) && string.Equals(s.Parametro, parametro, StringComparison.Ordinal);

    /// <summary>Define o valor manual (substitui o que já houver para a mesma posição e parâmetro).</summary>
    public static Sobrescrita Definir(List<Sobrescrita> lista, string posicao, string parametro, double valor)
    {
        if (lista == null) throw new ArgumentNullException(nameof(lista));
        if (string.IsNullOrWhiteSpace(posicao)) throw new ArgumentException("Posição vazia.", nameof(posicao));
        if (string.IsNullOrWhiteSpace(parametro)) throw new ArgumentException("Parâmetro vazio.", nameof(parametro));
        var s = lista.Find(x => Mesma(x, posicao, parametro));
        if (s == null) { s = new Sobrescrita { Posicao = posicao, Parametro = parametro }; lista.Add(s); }
        s.Valor = valor;
        lista.RemoveAll(x => x != s && Mesma(x, posicao, parametro));
        return s;
    }

    /// <summary>Volta o parâmetro para a regra. Devolve se havia sobrescrita.</summary>
    public static bool Remover(List<Sobrescrita> lista, string posicao, string parametro) =>
        lista != null && lista.RemoveAll(x => Mesma(x, posicao, parametro)) > 0;

    /// <summary>Volta todos os parâmetros da posição para a regra. Devolve quantas foram removidas.</summary>
    public static int Limpar(List<Sobrescrita> lista, string posicao) =>
        lista == null ? 0 : lista.RemoveAll(x => string.Equals(x.Posicao, posicao, StringComparison.Ordinal));

    /// <summary>Valor manual, ou null.</summary>
    public static double? Obter(IEnumerable<Sobrescrita> lista, string posicao, string parametro) =>
        lista?.FirstOrDefault(x => Mesma(x, posicao, parametro))?.Valor;
}
