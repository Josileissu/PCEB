# Pendências do PC2 para o PC1 consolidar

O PC2 não edita `escopo_organizado.md` nem `matriz_funcionalidades.md`. Decisões, perguntas e linhas de matriz
encontradas lá entram aqui; o PC1 passa para os arquivos certos no próximo merge e marca como consolidado.

| Data | Assunto | Texto proposto / pergunta | Consolidado? |
|---|---|---|---|
| 05/11/2026 | DEB.Cad: pedidos feitos no PC2 (direto na `main`, f4dfa56) | Registrar no escopo: (1) cópia de barra mantém posição e rotação do texto do original; (2) MATCHPROP copia propriedades do plugin barra→barra, faixa→faixa e tabela RA→tabela RA, sem mudar geometria; (3) faixas em cota ortogonal e alinhada nascem sem o diâmetro | não |
| 05/11/2026 | Inventário | `inventario_pc2.md`: o PC2 tem Tekla Structures 2025 (ambiente Brazil), Tekla Structural Designer 2025, Tedds, IDEA StatiCa 26, Advance Steel 2026 e mais módulos do CYPE (Consoles curtos, StruBIM). Atualizar `matriz_funcionalidades.md` se fizer sentido | não |
| 06/11/2026 | DEB.Revit: alças de içamento do pilar | Decisão do usuário: alças numa face, nos pontos da regra dos 20% (0,2 L e 0,8 L), face escolhida para os consolos ficarem para cima no transporte; com consolos nas 4 faces, face oposta ao consolo mais armado e/ou o lado maior (maior inércia no sentido do maior momento). Implementado: até 3 faces com consolo → face com mais consolos (desempate: consolos maiores, face mais estreita); 4 faces → oposta ao maior consolo, empate pela face mais estreita. Resposta do usuário (06/11): "alça no maior lado" = alça na **face menor (mais fina)**, o que dá a maior inércia no transporte | não |
| 06/11/2026 | DEB.Revit: "Marca da tabela" | A etiqueta do escritório lê a Marca da tabela; o plugin passa a gravar nela o número do vergalhão (N) da partição | não |
| 06/11/2026 | DEB.Revit: pranchas do pilar | Pedidos do usuário: níveis do modelo numa legenda da folha (não nas vistas de montagem, onde o Revit não mostra níveis); elevações niveladas até a armadura; armadura vermelha, inclusive a rebatida; cotar só consolos em corte, sem chanfros; detalhe da alça da peça no lugar da legenda da alça; quadro do consolo no nível do "ARMADURAS console" da PP127 (pendente) | não |
