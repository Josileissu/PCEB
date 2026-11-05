# Pendências do PC2 para o PC1 consolidar

O PC2 não edita `escopo_organizado.md` nem `matriz_funcionalidades.md`. Decisões, perguntas e linhas de matriz
encontradas lá entram aqui; o PC1 passa para os arquivos certos no próximo merge e marca como consolidado.

| Data | Assunto | Texto proposto / pergunta | Consolidado? |
|---|---|---|---|
| 05/11/2026 | DEB.Cad: template de reservatório com reconhecimento (pedido do usuário no PC2) | Pedido: reconhecer a estrutura de um DWG de outro programa (ex.: Eberick: 4 paredes externas, 1 interna, laje de fundo, lajes) e gerar o detalhamento no padrão de `src/DEB.Cad/auxiliar_cad/reservatório` usando só as bitolas e espaçamentos do desenho original. Decisões do usuário: (1) reconhecer DIRETO, sem janela de confirmação; (2) ganchos, ancoragens e traspasses pela NBR 6118, com os valores do exemplo como padrão do escritório editável; (3) v1 com tudo: paredes, fundo, laje intermediária e tampas; (4) código feito no PC2, branch `pc2/template-reservatorio` (D19: motor de superfícies, reservatório = paredes + fundo + tampa) | não |
| 05/11/2026 | Reservatório: ordem de trabalho (decisão do usuário) | Pausar o template de reservatório (v1 na branch `pc2/template-reservatorio`). Fazer primeiro o template de LAJES, depois o de PAREDES, no esquema do pilar e da base de reservatório, trabalhando os esquemas de armação de cada elemento e o detalhe dos cantos e encontros; depois juntar no template de reservatório. Coincide com a ordem da D19 (lajes maciças → paredes → reservatórios) | não |
