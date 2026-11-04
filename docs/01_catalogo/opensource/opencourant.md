# OpenCourant (continuação do OpenRadioss)

| Origem | Licença | Linguagem | Instalado aqui |
|---|---|---|---|
| Comunidade OpenCourant (opencourant.org, github.com/OpenCourant) — continuação do OpenRadioss (Altair) ✓ | **GNU AGPL-3** ✓ | Fortran (`.F`, `.F90`) ✓, C/C++ ✓ | não (código-fonte em `programas/opencourant/download`) |

## Funções
- **Tipo**: solver **explícito** de elementos finitos — colisão, impacto, explosão, dinâmica fortemente não linear ✓.
- **Entrada** ✓: `.rad` (Radioss), `.k`/`.key` (LS-Dyna) nativos; `.inp` (Abaqus) por conversor.
- **Análise**: dinâmica explícita, grandes deformações, contato, ALE e SPH (multifísica) ✓; precisão simples e dupla, SMP e MPI ✓.
- **Elementos/materiais**: biblioteca grande de leis de material, formulações de elemento e interfaces de contato ✓.
- **Saídas** ✓: animação e histórico no tempo; conversores para VTK, CSV e d3plot.
- **Plataforma** ✓: binários para Linux (Rocky/RHEL 8+, Ubuntu 20+) e **Windows**; containers.
- **Dimensionamento / detalhamento**: não.

## Avaliação (provisória)
| Critério | Nota | Comentário |
|---|---|---|
| Maturidade | 4 | código Radioss com décadas de uso industrial (automotivo); nome novo |
| Manutenção | 3 | projeto comunitário recente — acompanhar |
| Documentação | 3 | guias de uso/compilação no repositório ✓ |
| Integração | 3 | executável + arquivo texto; formatos LS-Dyna/Radioss |
| Aderência | 1 | fora do dia a dia de pré-moldado (impacto/explosão) |
| Licença | 3 | AGPL: usar como programa externo localmente é tranquilo; **oferecer como serviço web** obriga a abrir o código modificado |

## Uso possível para nós
- Pouco no escopo atual. Eventual: impacto de veículo em pilar, queda de peça na montagem, explosão — casos especiais.

## A confirmar
- Se vale manter no catálogo (escopo distante); situação do projeto em relação ao OpenRadioss original.
