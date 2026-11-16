# Folha de paginação das lajes alveolares — padrão e automação no Revit

Pedido do usuário (16/11/2026): registrar o que foi feito à mão, no CAD, na folha de paginação das lajes alveolares de um
modelo real (3 pavimentos: 1º, 2º/área técnica e cobertura). O objetivo é que o DEB.Revit gere essa folha sozinho.
Decisão **D31** (`00_escopo/escopo_organizado.md`). Complementa as pranchas de formas (D25, `pranchas_de_formas.md`) e a
paginação (D24, `src/DEB.Revit/docs/LAJE_ALVEOLAR.md`).

Os arquivos de referência (DWG das três folhas e os exports do Revit) são do escritório e ficam **fora do Git**. Os scripts
usados no CAD (Python + COM do GstarCAD) ficaram no scratchpad da sessão. O algoritmo está descrito na seção 5 para ser
reescrito no núcleo.

## 1. Ponto de partida: o que o Revit exporta hoje
A folha "PAGINAÇÃO DAS LAJES ALVEOLARES (PLN)" exportada do Revit para DWG chega assim:

| Layer do export | O que é | O que o usuário fez |
|---|---|---|
| `S-BEAM-HDLN` (linhas) | alvéolos dos painéis (linhas ocultas) | **apagou** |
| `A-FLOR` | contorno de cada laje (pano) | **apagou** |
| `S-BEAM` (linhas soltas) | contorno das peças recortadas | passou para `LAJE ALVEOLAR` |
| blocos "Laje alveolar - h16-Vnn" | painéis inteiros (contorno + alvéolos + linhas de chave) | só o contorno, no layer `LAJE ALVEOLAR` |
| blocos de vigas, escadas e consolos | peças com linhas ocultas | explodiu; tudo no layer `Linhas` (contínua) |
| `S-COLS`, `S-FSTN`, `A-WALL` | pilares, consolos, paredes | layer `Linhas` |
| `S-BEAM-IDEN` | marca de cada painel (`L01-01`…) | renumerou (seção 2.3) |
| `S-GRID`, `S-GRID-IDEN`, `S-COLS-IDEN`, `A-ANNO-DIMS` | eixos, nomes de pilar, cotas | mantidos |
| tabela no papel (`G-ANNO-SCHD`) | "PAINÉIS ALVEOLARES": tipo, comprimento, quantidade | **substituída** pelo quadro da seção 3 |

Armadilhas achadas no export:
- Algumas peças recortadas vêm com **parte do contorno no layer dos alvéolos**. Num caso, a borda de uma peça veio só como a
  última linha de alvéolo. Quem apaga os alvéolos às cegas abre o contorno.
- Peças recortadas e blocos de painel trazem uma **linha paralela 2 cm para dentro** de cada borda longitudinal (a chave,
  em ±60,25 num painel de 124,5). Quem mede a peça pelas linhas acha 2 a 4 cm a menos de largura.
- Uma das famílias de painel veio com nome sem o código "Vnn" (número do elemento no lugar).

## 2. Planta (1:50)

### 2.1 Linhas
- **Só o contorno das peças.** Nada de alvéolos, linhas de chave ou contorno do pano.
- Vigas, pilares, consolos, paredes e escadas em **linha contínua** (sem tracejado de oculto).
- **Sem as linhas de vigas e consolos que ficam inteiras debaixo das pontas dos painéis** (a face da viga sob o apoio).
  Linha que entra só em parte na peça fica.
- Eixos, nomes dos pilares e cotas entre eixos como estão.

### 2.2 Cores (peças iguais)
- Cada **grupo de peças idênticas com 2 ou mais peças** recebe uma cor **RGB** (o usuário pediu RGB, não índice) e um
  preenchimento sólido. A peça fica com a mesma cor na planta e na coluna dela no quadro.
- **Peça única (1x) fica sem cor.**
- Paleta usada (tons claros e distintos, na ordem dos grupos): 230,97,92 · 244,196,48 · 102,187,106 · 77,182,172 ·
  92,142,214 · 186,104,200 · 255,138,101 · 255,183,77 · 220,231,117 · 129,199,132 · 77,208,225 · 100,181,246 · 149,117,205 ·
  240,98,146 · 161,136,127 · 144,164,174 (e mais 16 tons ainda mais claros, se precisar).
- **Cor clara sólida, sem transparência** (resposta do usuário, seção 6): a transparência faz o PDF do GstarCAD virar imagem.
  No CAD o preenchimento fica num layer próprio, atrás das linhas.

### 2.3 Numeração (marca de cada peça)
`L<pavimento><laje>-<peça>`, por exemplo `L104-07` (pavimento 1, laje 04, peça 07). O cobertura usa o 3.
- **Lajes**: primeiro a fila de cima, da esquerda para a direita; depois a fila de baixo, da esquerda para a direita. Uma laje
  que ocupa as duas filas conta na de cima. A fila é decidida pelo eixo do meio: a laje que passa acima dele é da fila de cima.
- **Peças dentro da laje**: painéis deitados (vão em x) de baixo para cima; painéis em pé (vão em y) da esquerda para a direita.
  O sentido vem de como as peças estão empilhadas: centros variando mais em y = deitados.
- Peças "a/b" do export (uma peça partida em duas) viram números seguidos.
- A marca fica no meio da peça, ao longo dela, altura de texto 14 (cm do modelo).

## 3. Quadro de paginação
Título: `QUADRO DE PAGINAÇÃO DAS LAJES ALVEOLARES - <pavimento>`. Uma **coluna por tipo de peça**:

| Linha da coluna | Conteúdo |
|---|---|
| nome | as marcas do grupo em forma compacta (3.2) |
| dados | altura do painel lida da família (`h16` → `h=16`; a capa ainda sem fonte) e a quantidade `Nx` |
| sobrecarga | `S/C= 300 Kgf/m2` |
| desenho | a peça em 1:1 (a folha imprime em 1:50), **comprimento na vertical**, com as cotas (3.3) e a cor do grupo |

### 3.1 Peças iguais
- Igual = **mesmo contorno**. Painel girado 180° em planta é a mesma peça; **espelhado não** (a face de baixo é diferente).
- O agrupamento vale **entre lajes diferentes** (pedido do usuário: L106-02 a 05 e L107-02 a 05 numa coluna só).
- Para comparar, o contorno é normalizado: deitado vira em pé (giro de 90°), vai para a origem e o menor das duas listas
  ordenadas de vértices (normal e girada 180°) é a chave.

### 3.2 Nome compacto
- Números seguidos viram faixa: `L102-01 a 05`; dois seguidos `L104-02, 03`; avulsos `L104-01, 05`.
- Lajes diferentes separadas por vírgula: `L106-01, L107-01`.
- Até 20 caracteres: uma linha, texto 12. Mais que isso: quebra automática numa largura de 200, texto 10, e a linha do nome
  cresce (17 por linha + 14).

### 3.3 Cotas da peça (estilo de cota do escritório, texto 10)
- Largura total em cima (20 acima da peça); comprimento total à esquerda (22 de afastamento).
- Se o contorno tem mais de 2 coordenadas distintas em y: **cadeia à direita** com todos os trechos. Se tem mais de 2 em x:
  **cadeia embaixo** com todos os trechos.
- O usuário depois ajeitou à mão os textos das cotas pequenas (os "5" dos recortes nas pontas). Fica como melhoria: texto de
  cota menor que ~15 cm puxado para fora.

### 3.4 Arranjo
- **16 colunas por faixa**, coluna de 210 (cm do modelo).
- Alturas: título 65,8 (texto 28,5 a 10 do topo); nome 34,4 (ou maior, 3.2); dados 40,3; folga acima da peça 45,4; folga abaixo
  35. A faixa tem a altura da maior peça dela.
- **Faixas por comprimento**: as peças mais compridas na primeira faixa. Dentro de cada faixa, ordem de nome. Assim a soma das
  alturas é a menor possível e o quadro coube na viewport (2.486 de altura em 1:50) nos três pavimentos.
- Resultado nos três pavimentos: 90 peças em 48 tipos; 89 peças em 48 tipos; 82 peças em 25 tipos.

## 4. Folha
Arranjo que o usuário montou (D29: o programa entrega as vistas e o usuário arruma; este arranjo é o padrão a reproduzir):
- planta em 1:50 no alto, largura toda;
- quadro em 1:50 embaixo, à esquerda (viewport de 67,6 × 49,7 cm);
- detalhes típicos (seção do painel, apoios, tirante) em 1:55 à direita do quadro, e um detalhe em 1:20;
- legenda de símbolos, notas (furos só pelos alvéolos etc.) e carimbo;
- título da vista `PAGINAÇÃO - +<nível> - <pavimento>`; carimbo com o número da folha e a descrição
  `PAGINAÇÃO DAS LAJES ALVEOLARES (PLN) - <pavimento>`.
- Notas específicas de um pavimento (por exemplo, "laje maciça em balanço ancorada na laje L106") são do usuário.

## 5. Como automatizar no DEB.Revit
Segue a D26: regras e números no **núcleo**, o adaptador só lê, chama e escreve.

### 5.1 Núcleo (`DEB.Core/Lajes`, novo `QuadroPaginacao`)
Entrada: lista de peças `{marca, contorno em planta (polígono), laje, nível}`. Saída: o modelo do quadro e as cores.
1. **Contorno de cada peça** (se vier de linhas, como no DWG): grade das coordenadas distintas, inundação a partir da marca,
   juntando à peça as faixas fechadas e sem marca de até 2,05 cm (as linhas de chave), e o contorno resultante sem vértices
   colineares. No Revit o contorno sai da geometria (seção 5.2), e esse passo só serve para importar DWG.
2. **Numeração** (2.3) — já existe "renumerar lajes por fileira" no módulo Formas; a regra das peças entra junto.
3. **Grupos** (3.1), **nomes** (3.2), **cores** (2.2: só grupos ≥ 2, cor pela ordem do primeiro nome do grupo).
4. **Arranjo** (3.3 e 3.4): colunas, faixas, posições de textos, contornos e cotas, em cm do modelo. Testável sem o Revit:
   comparar com os três pavimentos de referência (contagens da seção 3.4) num teste de regressão.

### 5.2 Leitura no Revit
- Painéis: quadro estrutural com marca pelo filtro de painéis já existente (`Formas.FiltroPaineis`).
- Contorno: projeção em planta do sólido do painel (face de cima, com os recortes vazios aplicados), **não** as linhas da vista.
  Isso evita os dois problemas do export (contorno no layer dos alvéolos e linhas de chave).
- Pavimento: nível do painel; laje: a marca de laje (renumerar lajes) ou o pano que contém o painel.

### 5.3 Escrita no Revit
- **Planta** (vista de paginação já criada pelo comando "Paginação das lajes"):
  - esconder os alvéolos e as linhas de chave: subcategorias da família do painel (ou linhas invisíveis na família), sem
    "mostrar linhas ocultas" para os painéis;
  - vigas e consolos sob os painéis: hoje a vista liga as linhas ocultas de propósito (`PlantaFormas.LinhasOcultas`); para esta
    folha elas saem;
  - cores: sobreposição por elemento na vista (`SetSurfaceForegroundPatternId` sólido + cor RGB do grupo) ou um parâmetro
    `SD_GrupoPaginacao` com um filtro de vista por grupo (melhor se o usuário quiser mudar a cor depois);
  - marcas: escrever a marca nova no parâmetro Marca (o identificador de painel já mostra).
- **Quadro**: hoje é uma tabela resumida (tipo × comprimento × quantidade), que não mostra os recortes. Proposta: **vista de
  desenho** gerada (linhas de detalhe, região preenchida com a cor, cotas e textos), marcada com Extensible Storage e refeita sem
  duplicar a cada execução (mesmo padrão das outras vistas do módulo). A tabela atual fica como opção.
- **Folha**: só as vistas prontas; o arranjo da seção 4 é aplicado se o usuário quiser (D29).
- AutoRun para teste: `formaspaginacao` + uma tarefa nova que grava o modelo do quadro em JSON para comparar com o CAD.

## 6. Respostas do usuário (16/11/2026)
1. **Altura do painel vem da família** (tipo "h16" → `h=16`), não do quadro antigo. A capa e a sobrecarga do texto
   `h=15+5` / `S/C= 300 Kgf/m2` ainda não têm fonte definida (configuração do comando até o usuário dizer).
2. **Cor clara sólida**, sem transparência (o PDF sai vetorial). Nos DWG de referência a cor foi trocada pela mistura 50% com
   o branco: `(c + 255) / 2` em cada canal da paleta da seção 2.2.
3. **Quadro como vista de desenho / legenda** gerada pelo comando (seção 5.3).
4. Último pavimento: **"3º pavimento"** (não "cobertura").
5. Carimbo: **"PAGINAÇÃO DAS LAJES ALVEOLARES (PLN) - <pavimento>"** em todas as folhas (o "PLANTA DE MONTAGEM" era engano).
