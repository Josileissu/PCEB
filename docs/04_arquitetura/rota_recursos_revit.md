# Rota: apresentação conforme o padrão, com os recursos do Revit

Decisão do usuário (06/11/2026): **o Revit não é o CAD**. A prancha tem de ficar conforme o padrão (PP127 / P56), mas
feita com os recursos nativos do Revit, de um jeito que o escritório consiga manter no Revit. Sempre que existir uma
anotação específica — da vista, do elemento ou da armadura — ela é usada; **texto solto só quando não houver
alternativa**.

## Famílias do escritório (`S:\Meu Drive\Arquivos\Familias Revit`, fora do Git)
Conferidas com o AutoRun `familias` em 06/11/2026 (rótulos e tipos):

| Família | Categoria | O que mostra |
|---|---|---|
| `Anotação\Identificador Vergalhão.rfa` (e `- NAut`) | Identificadores de vergalhão | tipos "1 N1 Ø C" (4 N1 ø10.0 C=300), "1 N1 Ø c/" (16 N1 ø6.3 c/20), "1 N1 Ø c/ C", com "Quebra" (2 linhas), "VAR", "QTD", "N1" |
| `Anotação\Identificador Estribo.rfa`, `Identificar Barra de Aço.rfa`, `Identificador Armadura.rfa` | Identificadores de vergalhão | "2 N1 ø10.0 c/20", "N1" |
| `Anotação\Nome de Montagem.rfa` | Identificadores de montagem | nome da montagem (P1) |
| `Marcas\Marca Corte - Seta Preenchida.rfa` | Marcas de corte | "A ⊳" (como a referência) |
| `Marcas\Marca de Nível -Triângulo.rfa` | Extremidades de nível | nome + cota |
| `Marcas\Marca de Título - Simples.rfa` / `- Seção` / `- Sem escala` | Títulos da vista | nome da vista (+ "ESC" escala) |

A API não cria rótulos dentro de famílias: novas etiquetas/títulos saem do escritório; o plugin carrega e usa pelo nome
(caminhos no padrão de apresentação, como já é feito com o carimbo).

## Elemento por elemento da prancha

| Item da prancha | Hoje | Recurso do Revit | Etapa |
|---|---|---|---|
| Barras longitudinais na ARMADURA | linhas grossas esquemáticas + texto | **detalhe de curvatura** (`RebarBendingDetail`) de cada N, na altura real, ao lado da elevação + **etiqueta** "Identificador Vergalhão" (1 N1 Ø C) | R1 |
| Faixas de estribos ("16 N2 c/15") | texto | **anotação de múltiplos vergalhões** (`MultiReferenceAnnotation`: cota da faixa + etiqueta "1 N1 Ø c/") | R2 |
| Chamadas N nas seções e no consolo | textos "N1" + linhas | **etiquetas de vergalhão** com linha de chamada (tipo "N1"), uma por conjunto/posição | R3 |
| Formas dos estribos e barras avulsas | detalhe de curvatura + texto do rótulo | detalhe de curvatura + **etiqueta no detalhe** (no lugar do texto) | R3 |
| Níveis (TOPO / NÍVEL / BASE) | linhas + textos | **níveis do projeto** visíveis na vista (cabeça "Marca de Nível -Triângulo") ou **cota de elevação** (spot) no topo/base | R4 |
| Marcas de corte | traço + texto (simples) | **marca de corte do Revit** com o tipo de cabeça "Marca Corte - Seta Preenchida" (no lugar de ocultar as marcas) | R5 |
| Títulos das vistas | textos acima das vistas | **título da viewport** ("Marca de Título - Simples"; bilíngue depende de uma família com 2 linhas ou do "Título na folha") | R6 |
| "COMPRIMENTO TOTAL DO PILAR 1000" | texto vertical | **texto da própria cota** (prefixo/abaixo da cota total da VISTA 1) | R4 |
| Cotas, tabelas, detalhes padrão | já nativos (cotas, tabelas, legendas) | — | feito |
| Informações p/ produção, notas | quadros de texto | parâmetros da montagem + etiqueta ou tabela da montagem (quando houver); notas ficam texto | R7 |

## Ordem e critérios
1. **R1** (pedido do usuário): longitudinais como detalhes de curvatura + etiqueta, trocando as linhas esquemáticas.
2. **R2/R3**: estribos e chamadas com anotações de vergalhão (somem os TextNotes de barras).
3. **R4–R6**: níveis, marcas de corte e títulos com as famílias do escritório.
4. **R7**: informações de produção por parâmetros.

Cada etapa: teste no Revit 2026 (AutoRun), PNG comparado com a PP127, sem regressão de escala nas regiões. Onde uma família do
escritório faltar no projeto, o plugin a carrega do caminho do padrão; se não achar, avisa e usa o recurso anterior.
