# Proposta de decisões D26 a D28 — organização do núcleo

> Rascunho de 15/11/2026 para o usuário revisar. **Não é fonte de verdade**: só vale depois de
> aprovado e copiado para `docs/00_escopo/escopo_organizado.md` (que só o PC1 edita).
> Substitui as propostas D26/D27 de `resumo_planejamento.md`.

## Linhas prontas para a tabela de decisões

| # | Tema | Decisão |
|---|---|---|
| D26 | Núcleo único e prévia (15/11/2026) | Toda regra de engenharia, número e texto de prancha mora no **`DEB.Core`**. O DEB.Cad e o DEB.Revit só fazem três coisas: **ler** do programa hospedeiro, **chamar** o núcleo e **escrever** o resultado. Toda operação que cria ou altera geometria segue **modelo no núcleo → prévia → aceite do usuário → aplicação**, e a prévia e o resultado saem do **mesmo modelo** (nunca de dois cálculos); referência: paginação de laje alveolar (D24). Ficam fora da prévia as operações de um clique sem geometria nova. Fica no adaptador o que é próprio do hospedeiro (XData, famílias, Extensible Storage, a janela da prévia). Primeiro caso: pilar pré-moldado no Revit pelas regras do núcleo, com teste de paridade antes. |
| D27 | Organização do núcleo (15/11/2026) | A pasta é o **elemento** que o engenheiro nomeia (viga, pilar, laje, consolo, fundação, reservatório…); subtipos ficam dentro (pilar pré-moldado, metálico, parede). A **dimensão** (barra, superfície, volume) é declarada no contrato do elemento e escolhe o algoritmo; não vira pasta. **Composição**: consolo, conexão e treliça são conjuntos que referenciam outros elementos; o elemento não conhece quem o usa. Pasta nova só nasce junto com código (sem pastas vazias). A reorganização é feita por partes: primeiro `Templates/`, o resto quando for tocado. "Perfil" passa a significar perfil metálico; o padrão de detalhamento do escritório passa a se chamar **padrão**. |
| D28 | Seções, produtos e sistemas (15/11/2026) | Três níveis, cada um com o seu mecanismo. **(1) Seção de elemento linear = família + parâmetros + partes nomeadas.** Concreto: retangular, T, L, I, trapezoidal; "invertido" é orientação da mesma família. Aço: I/H (W, H, HP, I), U, Ue, Z, tubo (circular, quadrado, retangular), trilho; as dimensões vêm de **catálogo em dados** (uma tabela por fabricante ou norma). A família gera o contorno para desenhar e conferir; a seção poligonal medida fica só para o que não casa com nenhuma família. Consolo, dente, enrijecedor de alma e abertura são **acréscimos ao longo do eixo**, presos a uma parte nomeada da seção. **(2) Produto de superfície = peça de catálogo + paginação sobre um pano**: lajes (alveolar, steel deck, vigota com enchimento, nervurada), painéis e telhas; o motor de paginação da D24 é generalizado para "peça de largura útil sobre pano com recortes". **(3) Sistema = receita que gera elementos** a partir de poucos parâmetros (treliça, joist, conexão). Cadastro de catálogo é dado e pode crescer; família ou motor novo só entra quando uma entrega pedir. Catálogos em PDF e marcas ficam fora do Git. |
| D29 | Montagem de folhas no Revit (15/11/2026) | Para folha nova, o programa (e a IA que o desenvolve) entrega **só as vistas prontas**; o usuário organiza a folha à mão; depois o arranjo dele é **lido** (posição, escala e ordem das vistas) e vira o padrão a reproduzir. Não se gasta sessão tentando acertar o arranjo por tentativa. Começa no próximo projeto em que for preciso. |

## Ordem de execução combinada

| # | Etapa | Toca código? |
|---|---|---|
| 1 | Limpeza pública: nomes fora dos arquivos publicados, `.gitignore`, mesclar a limpeza do quadro | não |
| 2 | Leitura barata: quadro como índice, `CLAUDE.md` de Cad e Revit enxugados, `docs/mapa/`, `AGENTS.md` | não |
| 3 | Pilar no Revit pelas regras do núcleo (teste de paridade, depois a troca) | sim |
| 4 | `Templates/` dividido por elemento | só move |
| 5 | Interface de seção com as duas origens (família e polígono) e partes nomeadas | sim |
| 6 | SDK fora do Git, `global.json`, versão num lugar só, `.gitattributes` | configuração |

## Respostas do usuário (15/11/2026)

| Pergunta | Resposta | Consequência |
|---|---|---|
| O Revit só modela e exporta (D22) ou também detalha? | **O Revit também detalha tudo** | paridade entre as duas interfaces vira regra; a D22 continua valendo como caminho alternativo, não como o único |
| Limpar o histórico do Git (nomes e SDK)? | **Limpar tudo o que for possível** | reescreve o histórico: combinar com o outro computador, refazer os clones e os worktrees; antes, tudo mesclado na `main` |
| Obra de linha de base para medir produtividade? | **Ainda não há**; por ora a medida vem da obra em andamento em que o programa mais é usado | registrar como pendência; a métrica começa quando houver um caso escolhido |
