using System;
using System.Collections.Generic;
using DEB.Core.Configuracao;
using DEB.Core.Motor.Componentes;

using DEB.Core.Motor.Receitas;

namespace DEB.Core.Motor.Fundacoes;

/// <summary>
/// Motor de fundações — bloco sobre estacas (frente B, 2026-11-07; frente K, 2026-11-07: virou orquestrador de
/// COMPONENTES). Mesmas regras do <c>TemplateBlocoEstacas</c> (grade nx × ny ou triangular de 3 estacas; cálice com
/// colarinho, embutido ou arranque; bloco de 1 estaca sem principal), com saída no modelo neutro 3D
/// (<see cref="ModeloArmadura"/>). As regras moram nos componentes de <c>DEB.Core.Motor.Componentes</c>
/// (docs/04_arquitetura/componentes.md) e a sequência deles com as fórmulas na RECEITA JSON
/// (<c>Motor/Receitas/Fabrica/bloco_estacas.json</c>, embutida; o usuário pode substituí-la em %AppData%/PCEB/Receitas).
/// <para>Sistema local: x, y em planta (origem no canto inferior esquerdo da caixa envolvente do bloco), z para cima,
/// z = 0 no fundo da base. Vértices em cm com as MEDIDAS EXTERNAS da barra.</para>
/// <para>CHAVES DE POSIÇÃO (estáveis; as sobrescritas apontam para elas):
/// principal_x, principal_y (grade) | principal_lado1, principal_lado2, principal_lado3 (triangular);
/// secundaria_x, secundaria_y; superior_x, superior_y; lateral; estribo_vertical_x;
/// calice_topo, calice_grampos_topo_x, calice_grampos_topo_y, calice, calice_grampos_x, calice_grampos_y,
/// calice_vertical, suspensao, fundo_calice; arranque, arranque_estribo.</para>
/// <para>PERFIL: Padroes "cobrimento.solo"(4), "cobrimento.calice"(2), "secundaria.perna"(20), "superior.perna"(20),
/// "suspensao.desconto_perna_externa"(6), "arranque.pe"(20), "arranque.espera"(0 = traspasse NBR 6118), "calice.grampos"(1),
/// "principal.perna"(0), "fundo_calice.comprimento"(100); Regras "principal.perna" = "altura_base" | "valor",
/// "arranque.pe_sentido" = "fora" | "dentro".</para>
/// </summary>
public sealed class MotorBlocoEstacas : IMotorDetalhamento<ElementoBloco>
{
    /// <summary>Trecho do pilar representado acima do topo do bloco (corpo "pilar"), cm.</summary>
    public const double PilarAcimaDoTopo = 50;

    private readonly MotorReceita<ElementoBloco> _motor;

    /// <summary>Receita de fábrica, sem configuração própria.</summary>
    public MotorBlocoEstacas() : this(null) { }
    /// <param name="cfg">regras de faixa (quantidade, espaçamento inicial) e de dobramento; null = padrão.</param>
    /// <param name="receita">receita a usar (ex.: a do usuário, via <see cref="RepositorioReceitas"/>); null = a de fábrica.</param>
    public MotorBlocoEstacas(ConfiguracaoArmadura cfg, Receita receita = null)
    {
        ReceitasFabrica.Garantir();
        _motor = new MotorReceita<ElementoBloco>(receita ?? RepositorioReceitas.ObterFabrica(ElementoBloco.TipoId),
            ComponentesPadrao.Catalogo(), cfg ?? new ConfiguracaoArmadura());
    }

    public string TipoElemento => ElementoBloco.TipoId;

    public ModeloArmadura Gerar(ElementoBloco elemento, PerfilDetalhamento perfil, IReadOnlyList<Sobrescrita> sobrescritas)
    {
        if (elemento == null) throw new ArgumentNullException(nameof(elemento));
        return _motor.Gerar(elemento, perfil, sobrescritas);
    }
}
