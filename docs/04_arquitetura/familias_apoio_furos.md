# Famílias: apoio no consolo e furo dos pinos na viga (D36)

Pedido do usuário (18/11/2026): a família do consolo ganha o elemento **apoio** (neoprene, argamassa ou direto) e a família da
viga pré-moldada ganha o **furo dos pinos**. Serve às verificações do consolo (C1, C3–C5) e do dente (D8), à prancha e, na fase
de cálculo, ao Hd (NBR 9062 7.3.9). Parâmetros novos já com o prefixo `PCEB_` (D38).

Famílias de origem (do usuário, no Drive — **nunca editar o original**; trabalhar numa cópia e devolver só com o aceite dele):
`S:\Meu Drive\Arquivos\Familias Revit\Estrutura\Concreto\Viga Pré-Moldada.rfa` (consolo "Consolo" aninhado e compartilhado) e
as variantes "Viga Pré-Moldada - …" da mesma pasta.

## Consolo: elemento apoio
Sólido sobre a face superior do consolo, preso a planos de referência rotulados (flexiona com os parâmetros).

| Parâmetro | Tipo | Instância/tipo | Uso |
|---|---|---|---|
| `PCEB_Apoio` | texto com 3 valores: Neoprene, Argamassa, Direto | tipo | material, visibilidade, Hd |
| `PCEB_ApoioComprimento` | comprimento (ao longo da viga) | tipo | a (distância da carga), a2 |
| `PCEB_ApoioLargura` | comprimento (transversal) | tipo | afastamento lateral (C4) |
| `PCEB_ApoioEspessura` | comprimento (neoprene 1 cm, argamassa 2 cm, direto 0) | tipo | altura de apoio da viga |
| `PCEB_ApoioAfastamento` | comprimento: face externa do consolo → face externa do apoio (a2) | tipo | C3 |
| `PCEB_ApoioVisivel` | sim/não, fórmula `not(PCEB_Apoio = "Direto")` | — | Direto = sem sólido |

Material pelo tipo (Neoprene / Argamassa de assentamento). O **pino** fica no centro do apoio: `PCEB_PinoDiametro` e
`PCEB_PinoAltura` (barra saindo do consolo para dentro do furo da viga) — a confirmar com o usuário se o pino entra na família
do consolo ou continua só no detalhamento.

Fatores de Hd por tipo (fase de cálculo, NBR 9062 7.3.9): neoprene (elastômero) 0,16·Fd; argamassa 0,5·Fd; direto (junta a
seco) 0,8·Fd.

## Viga: furo dos pinos no dente
Vazio cilíndrico vertical atravessando o dente, nas duas pontas.

| Parâmetro | Tipo | Uso |
|---|---|---|
| `PCEB_FuroDiametro` | comprimento | prancha, folga do pino |
| `PCEB_FuroQtd` | inteiro, fórmula `if(b < 30 cm, 1, 2)` (D23), editável | D8 |
| `PCEB_FuroDistE` / `PCEB_FuroDistD` | comprimento: ponta → centro do furo | casa com o pino do consolo |
| `PCEB_FuroEntreFuros` | comprimento (2 furos) | prancha |

A exportação para o CAD (D22, `Intercambio`) passa estes valores para `FurosE/FurosD`/`FuroDistE/FuroDistD` do template da viga.

## Como fazer (pendente: Revit livre)
Uma tarefa do AutoRun (`SD_AUTORUN=familiaspceb`, numa instância de teste) abre a cópia do `.rfa`, edita a família aninhada do
consolo (`EditFamily`), cria planos de referência, cotas rotuladas, o sólido/vazio e os parâmetros, recarrega na viga e grava
`... - PCEB.rfa` na pasta de trabalho; depois `familias` (relatório) e `criarmodelo` conferem. **Neste computador (PC1) o Revit de
teste parou em "manage licensing"** em 18/11/2026: a licença estava com o Revit de outro usuário do Windows. Fazer no PC2 ou com
a licença livre.
