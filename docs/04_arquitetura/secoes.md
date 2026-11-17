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

`SecaoBarras.Posicoes(SecaoTransversal, cobrimentos por ParteSecao, …, aço do estribo, configuração)` → `ArranjoSecao`
(barras, estribos por parte, avisos, ou `Erro`). Retangular: delega para a função antiga (resultado idêntico; sem estribos
nem avisos). Chama: `TemplatePilarPreMoldado.PosicoesBarras`, só com seção retangular (o modelo 3D do Revit passou para a
armadura por linha, D32). T, L, I e trapezoidal: arranjo por família (D31, `Motor/ArranjoFamilias.cs`); nenhum template nem
comando chama ainda. Seção medida: "arranjo não definido".

## Arranjo por família (D31)

Cobrimento até o estribo; barra de canto a cobrimento + φt + φ/2 das faces do estribo; eixo do estribo a cobrimento + φt/2.
Cada barra traz a parte nomeada (`BarraNaSecao.Parte`); cada estribo traz a parte, o contorno pelo eixo (anti-horário) e o
comprimento pelo `ComprimentoPorEixo` (ganchos de 135°, D30). A conta é feita na posição canônica e espelhada para a real.

| Família | Estribos | Longitudinais (mínimo) | Intermediárias |
|---|---|---|---|
| T / T invertida | alma na altura toda (os cantos de cima ficam na face externa da mesa) + mesa na largura toda | 8: 4 cantos da alma e 4 da mesa (pontas) | não aplicadas (aviso) |
| L (4 orientações) | alma na altura toda + aba na largura toda (como a mesa da T) | 7: 4 da alma e 4 da aba, o canto externo comum é uma barra só | não aplicadas (aviso) |
| I | alma na altura toda + um por mesa | 12: por mesa, 4 na face externa (2 da alma, 2 das pontas) + 2 fechando o estribo da mesa | pele da alma: `nFaceH` por face, φface, dividida por igual entre as barras internas das mesas; `nFaceB` não aplicado (aviso) |
| Trapezoidal | um trapezoidal, cobrimento na **normal** de cada face | 4 cantos (cruzamento das paralelas às faces) | como no retângulo: `nFaceB` no topo e no fundo, `nFaceH` em cada inclinada (numa reta, dividir pelo comprimento ou pela projeção dá o mesmo) |
| Medida (poligonal) | — | "arranjo não definido" | — |

Cantos com 3 barras (`porCanto` ≥ 3) não entram em nenhuma família não retangular (aviso). A trapezoidal é a nervura da laje
nervurada (dono, 16/11); a laje nervurada não está feita.

**Cobrimento das faces internas — PROVISÓRIO** (o dono não respondeu; regra do coordenador, com aviso quando usada): o
informado para a própria parte (a face, se tem nome, ou a região); na falta, o maior das faces externas adjacentes da mesma parte.

| Família | Face interna | Própria | Na falta |
|---|---|---|---|
| T | lateral da alma | LateralAlmaEsquerda/Direita, Alma | Fundo (Topo na invertida) |
| T | face interna da mesa | Mesa | maior de Esquerda, Direita |
| L | lateral interna da alma | LateralAlma…, Alma | Topo (Fundo com a aba em cima) |
| L | face interna da aba | Aba | Direita (Esquerda com a aba à esquerda) |
| I | lateral da alma | LateralAlmaEsquerda/Direita, Alma | nenhuma: cobrimento faltando |
| I | pontas e face interna de cada mesa | MesaSuperior / MesaInferior | Topo / Fundo |

Avisos (não exceções; `ArranjoSecao.Avisos`): cobrimento faltando (face externa sem valor → também `Erro`, sem barras),
regra provisória usada, barra fora do concreto (ou a menos de φ/2 da face), barras sobrepostas, estribo que não cabe
(cobrimentos maiores que a parte, ou lado com menos que o pino por dentro: largura < cobrimentos + 2φt + pino), estribo fora
do concreto, intermediárias não aplicadas.

Prévia: com `ARM_PREVIA=<pasta>`, `tests/DEB.Core.Tests/Geometria/PreviaSecoesTests` grava `<pasta>\secoes\*.svg` (contorno,
estribos, barras numeradas, cobrimento medido em cada lado, lista e avisos). Testes: `ArranjoFamiliasTests`.

Achado: o núcleo põe os ganchos do estribo sempre no canto superior esquerdo; na trapezoidal invertida esse canto tem outro
ângulo e o comprimento muda (120 × 121 cm no caso do teste). Não mexido.

## Não feito

| O quê | Situação |
|---|---|
| Template, comando do CAD ou do Revit usando o arranjo por família | nenhum (pilar L, viga T etc. não existem como template; a viga pré-moldada tem armadura própria) |
| Intermediárias em T, L e nas mesas da I; cantos com 3 barras fora do retângulo | falta definir por parte |
| Quantidade e espaçamento da pele pela NBR 6118 (17.3.5.2.3) | o núcleo não tem função para isso (só o aviso "h > 60 sem pele" da viga); a quantidade vem de `nFaceH` |
| Grampos nas famílias compostas; espaçamento máximo entre longitudinais por parte | sem resposta do dono |
| Estribo da retangular no `ArranjoSecao` | o chamador continua fazendo (resultado da retangular não muda) |
| `SecaoComAba`, subdivisão (`TrechoPeca`), consolo | continuam com `SecaoPoligonal`, `TipoSecao` e face 1…4 |
| Componentes do motor (`SecaoLongitudinal`, `GramposSecao`, `Arranque`) | ainda chamam a função por b × h |
| Armadura por linha (D32) pela seção | `ArmaduraPorLinha.Posicoes(SecaoTransversal, cobrimentos por parte, …)`: retangular = a conta por b × h (o modelo 3D do Revit chama por aqui); outras famílias = "não definida para esta família" (falta combinar as linhas com o arranjo por família da D31) |
| Famílias e catálogos de aço | D28: quando uma entrega pedir |
| Orientação gravada em arquivo | nenhum formato grava `SecaoTransversal` ainda |
| Laje nervurada (a trapezoidal é a nervura) | não feita |

## Perguntas ainda abertas

| Família | Pergunta |
|---|---|
| Todas | Cobrimento das faces internas: confirmar a regra provisória acima. Espaçamento máximo entre longitudinais e pele valem por parte? |
| T / L | Intermediárias: por face de cada estribo? Na T invertida, a mesa (que recebe a laje) segue a regra da aba do template da viga? Na L, o estribo da aba vai na largura toda (como aqui) ou só no balanço? |
| I | Cordoalhas da mesa inferior ficam fora deste arranjo (protensão)? Intermediárias nas mesas? |
| Grampos | Nas famílias compostas, os grampos amarram as intermediárias de cada parte separadamente? |
