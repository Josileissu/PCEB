# Seções de elemento linear (D28)

Etapa 5 da O-2 (16/11/2026). Código em `src/DEB.Core/Geometria/Secao/`; testes em `tests/DEB.Core.Tests/Geometria/`.
Seção = **família + parâmetros + orientação + partes nomeadas**; o polígono medido fica só para o que não casa com nenhuma família.

## Tipos

| Tipo | Guarda / faz |
|---|---|
| `FamiliaSecao` | identificador estável, nome, parâmetros (`ParametroSecao`: nome, unidade, descrição); gera contorno e partes na posição canônica |
| `FamiliasSecao` | as famílias: `Retangular`, `T`, `L`, `I`, `Trapezoidal`, `Poligonal`; `PorId` |
| `OrientacaoSecao` | espelho horizontal (troca esquerda/direita) e vertical (troca topo/fundo, "invertida"); os dois = 180° |
| `SecaoTransversal` | família + valores + orientação, ou o polígono medido; `Contorno()` (um `SecaoPoligonal`), `Partes()`, `Parte(p)`, `Area`, `CG`, `Iy`, `Iz`, `Descricao` |
| `ParteSecao` / `ParteNomeada` | nome da parte (valores fixos, não renumerar) e a geometria dela: face = segmento, região = retângulo |
| `ReconhecimentoSecao.Reconhecer` | `SecaoPoligonal` → `SecaoTransversal` (família, orientação e parâmetros) ou a medida |
| `FacesRetangulo` | número de face de hoje (interna e apresentada) ↔ parte nomeada; cobrimentos por parte ↔ vetor por face |

Coordenadas do `SecaoPoligonal`: y na horizontal, z na vertical, cm. O contorno da família tem origem no canto inferior
esquerdo da caixa; a área, o CG e as inércias saem dele (o cálculo é o de sempre).

## Famílias

| Família (`Id`) | Parâmetros (cm) | Posição canônica | Partes nomeadas |
|---|---|---|---|
| `retangular` | b, h | — | Topo, Fundo, Esquerda, Direita |
| `T` | bw, h, hf, ae, ad (balanços da mesa à esquerda e à direita, > 0) | mesa em cima | Topo, Fundo, Esquerda, Direita (pontas da mesa), LateralAlmaEsquerda, LateralAlmaDireita; Mesa (faixa inteira), Alma (fora da mesa) |
| `L` | bw, h, hf, ba (balanço da aba) | alma à esquerda, aba embaixo saindo à direita | Topo, Fundo, Esquerda, Direita, a lateral interna da alma (LateralAlmaDireita na canônica); Alma (altura toda), Aba (só o que passa da alma) |
| `I` | bw, h, hs, ase, asd, hi, aie, aid | — | Topo, Fundo, LateralAlmaEsquerda, LateralAlmaDireita; MesaSuperior, MesaInferior, Alma |
| `trapezoidal` | bi (fundo), bs (topo), h; simétrica | — | Topo, Fundo, Esquerda e Direita (as inclinadas) |
| `poligonal` | nenhum | — | nenhuma |

Os nomes valem na posição real: na T invertida, `Topo` é o topo da alma e a `Mesa` fica embaixo.

## Orientação

| Família | Orientações | Na resposta do reconhecimento |
|---|---|---|
| T | espelho vertical = T invertida | só o vertical; o horizontal vira troca de ae e ad |
| L | as quatro (aba embaixo/em cima × à direita/à esquerda) | as quatro |
| I, trapezoidal, retangular | espelho permitido ao gerar | canônica (o espelho vira troca de parâmetros) |

Giro de 90° não entra (no pilar é trocar b e h). "Invertido" não é família.

## As duas origens

| Origem | Como nasce | Contorno |
|---|---|---|
| Família | `SecaoTransversal.DaFamilia` ou atalhos (`Retangular`, `T`, `L`, `I`, `Trapezoidal`); valores inválidos dão `ArgumentException` com o motivo | gerado pela família |
| Medida | `SecaoTransversal.DaMedida(polígono)`; é o que o `Reconhecer` devolve quando não casa (U, caixão, mísula, furo, trapézio assimétrico) | cópia do polígono medido |

Reconhecimento: retangular, T, L e I pelo `SecaoPoligonal.Classificar()` (mesmas faixas e tolerância de 0,5 cm; o
`Classificar` e o enum `TipoSecao` não mudaram); a trapezoidal é procurada entre as que ele chama de poligonais.

Aço (D28, não feito): família de aço (I/H, U, Ue, Z, tubo, trilho) com parâmetros nomeados como as de concreto, valores lidos
de um catálogo em dados (uma linha por designação, em mm) e contorno com arcos de concordância — seria uma terceira origem
("catálogo"), e o `SecaoPoligonal`, que só tem segmentos, precisaria aproximar os arcos. Só quando uma entrega pedir.

## Números de face do retângulo (`FacesRetangulo`)

| Parte | Face interna (arquivo de peças, cobrimentos `cob[]`) | Face apresentada (prancha do pilar) |
|---|---|---|
| Direita (+x) | 1 | 3 (leste) |
| Fundo (−y) | 2 | 2 (sul) |
| Esquerda (−x) | 3 | 1 (oeste) |
| Topo (+y) | 4 | 4 (norte) |

Os campos gravados (Face inteira no JSON de peças, XData, parâmetros de template) continuam com o número; a conversão é só
na leitura e no uso. `TemplatePilarPreMoldado.FaceExibida` chama esta tabela.

## Barras longitudinais pela seção

`SecaoBarras.Posicoes(SecaoTransversal, cobrimentos por ParteSecao, …)` → `ArranjoSecao` (barras, ou `Erro`). Retangular:
delega para a função antiga (resultado idêntico). Chamam a nova: `TemplatePilarPreMoldado.PosicoesBarras` e o modelo 3D do
Revit (`ColumnRebarLayout.LongitudinalPositions`). Outras famílias: "arranjo não definido", sem barras.

## Não feito

| O quê | Situação |
|---|---|
| Arranjo de barras em T, L, I, trapezoidal e medida | depende das perguntas abaixo |
| `SecaoComAba`, subdivisão (`TrechoPeca`), consolo | continuam com `SecaoPoligonal`, `TipoSecao` e face 1…4 |
| Componentes do motor (`SecaoLongitudinal`, `GramposSecao`, `Arranque`) e as listas do template do pilar (`PosicoesLinhas`) | ainda chamam a função por b × h |
| Famílias e catálogos de aço | D28: quando uma entrega pedir |
| Orientação gravada em arquivo | nenhum formato grava `SecaoTransversal` ainda |

## Perguntas para o dono (arranjo das longitudinais)

| Família | Perguntas |
|---|---|
| Todas | Cobrimento das faces que não são da caixa (lateral da alma sob a mesa, face de cima da aba): o da lateral, o do topo ou um próprio? Espaçamento máximo entre longitudinais e barra de pele valem por parte? |
| T / T invertida | Barras de canto nos 4 cantos da alma e também nas pontas da mesa? Estribo fechado só na alma e outro (ou grampo) na mesa? Barras na mesa até as pontas ou só na largura da alma? Na T invertida, a mesa (que recebe a laje) segue a regra da aba do template da viga? |
| L | Barra no canto reentrante (encontro alma-aba)? Um estribo poligonal em L ou estribo da alma + estribo/grampo da aba? Barras na ponta da aba: quantas? |
| I | Quantas barras por mesa e onde (cantos de cada mesa)? O estribo da alma entra nas mesas? Cordoalhas da mesa inferior ficam fora deste arranjo (protensão)? |
| Trapezoidal | Cobrimento medido na normal da face inclinada? Estribo trapezoidal? Intermediárias divididas pelo comprimento da face inclinada ou pela projeção? |
| Medida (poligonal) | Recusar, ou o usuário posiciona as barras à mão? |
| Grampos | Nas famílias compostas, os grampos amarram as intermediárias de cada parte separadamente? |
