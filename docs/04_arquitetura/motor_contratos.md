# Motor de detalhamento — contratos e frentes (PC2, 2026-11-07)

Pedido do usuário (2026-11-07): os "templates" atuais não estão num padrão coerente — cada um é uma classe grande
que mistura regras de norma, padrão do escritório, layout da folha e desenho final (bloco: 1.300 linhas, ~80 campos).
Reorganizar conforme a D19 (`motor_detalhamento.md`), com o **bloco sobre estacas como piloto**, em frentes paralelas.

## Separação
| Camada | O que é | Onde |
|---|---|---|
| 1. Dados do elemento | o que vem do cálculo/desenho: geometria, ligação, bitolas e quantidades | `ElementoEstrutural` / `ElementoBloco` |
| 2. Motor (regras) | NBR + regras do escritório → modelo de armadura 3D | `IMotorDetalhamento<T>` |
| 3. Modelo de armadura | intenção (parâmetros + regra) · geometria calculada · sobrescritas manuais | `ModeloArmadura`, `PosicaoArmadura`, `Sobrescrita` |
| 4. Perfil do escritório | padrões (pernas, cobrimentos), regras escolhidas, vistas da folha, detalhes, idioma | `PerfilDetalhamento` (JSON) |
| 5. Prancha | vistas, cortes, faixas, chamadas, barras dobradas, tabelas — comum a todos os elementos | `IGeradorPrancha` → `ResultadoTemplate` |
| 6. CAD | desenha o `ResultadoTemplate` (já existe) e guarda elemento + sobrescritas para regenerar | DEB.Cad |

Contratos em `src/DEB.Core/Motor/Contratos.cs`. Mudar um tipo = combinar antes; acrescentar campo opcional pode.
Sobrescrita = a edição "na hora da produção" (ex.: perna 68 do B31): fica marcada como manual e sobrevive à regeneração.

## Frentes (branches a partir de `pc2/motor-detalhamento`)
| Frente | Entrega | Testes |
|---|---|---|
| A. Perfil e JSON | leitor/gravador JSON próprio (sem dependência: o plugin roda em net48 dentro do CAD), serialização dos contratos, perfil "BPM" do bloco (fábrica) + arquivo do usuário | ida e volta; perfil BPM = valores do template atual |
| B. Motor de fundações (bloco) | `MotorBlocoEstacas`: regras do `TemplateBlocoEstacas` portadas para o modelo 3D, corpos de concreto, sobrescritas | paridade com B28, B15, B31 (`BlocoEstacasTests`) |
| C. Gerador de prancha | projeção 3D → planta/cortes, faixas e chamadas, barras dobradas, título, tabelas, detalhes padrão | modelo de teste feito à mão |
| D. Entrada de dados | lista de blocos por planilha/CSV → `ElementoBloco` (vários de uma vez) + serviço de regeneração (elemento + perfil + sobrescritas) | CSV de exemplo |
| Integração (sessão principal) | ARMTEMPLATE/comando novo usando o motor; XData; paridade com o template atual; autoteste no AutoCAD | autoteste |

O PC1 (P1-2, motor de lineares do pilar) usa os mesmos contratos.
