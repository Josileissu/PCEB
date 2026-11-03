# Robustez e estabilidade — evitar crashes e perda de trabalho

Objetivo: um comando que falha **mostra a mensagem, desfaz o que fez e deixa o programa aberto**. Nunca fecha
a janela, o Revit ou o CAD, e nunca corrompe o arquivo do usuário.

## 1. Por que programas fecham
| Causa | Exemplo | Defesa |
|---|---|---|
| Exceção não tratada | erro num comando sobe até o Revit/CAD | captura no ponto de entrada de **todo** comando, botão e evento |
| Erro de memória nativa | **já vimos**: `Solid` guardado sem o dono → SEHException no Revit | não guardar objetos nativos; copiar os dados para objetos nossos |
| Mexer no documento na hora errada | alterar entidades dentro de um evento do CAD; mudar o Revit fora de uma transação | só **enfileirar** no evento e processar depois (padrão já usado no Armadura BR: `CommandEnded`; no Revit: `ExternalEvent`/Idling) |
| Thread errada | API do Revit/CAD chamada de outra thread | API só na thread principal; cálculo pesado em outra thread, resultado volta pela fila |
| Laço infinito / travamento | janela congela, usuário mata o processo | limites de iteração, botão cancelar, cálculo fora da thread da interface |
| Falta de memória | modelo ou malha grande | estimar tamanho antes; processar em lotes |
| Dados inválidos | seção com dimensão zero, barra sem comprimento | validar a entrada antes de calcular (com mensagem clara) |
| Biblioteca externa (solver) cai | CalculiX/OpenSees aborta | rodar solver como **processo separado**: se ele cair, só ele cai |

## 2. Princípios para o programa e os plugins
1. **Isolamento**: cálculo pesado e solvers em processo separado do Revit/CAD/interface.
2. **Transações**: cada comando é tudo-ou-nada (Revit `Transaction`/`TransactionGroup`, CAD `Transaction` + undo).
3. **Barreira de exceções** em cada ponto de entrada: registra no log, mostra mensagem em português, desfaz.
4. **Log e diagnóstico**: log por execução com pilha (já existe: `detalhamento_*.log` no StructuralDetailer,
   `%AppData%\ArmaduraBR\logs`); estudar *minidump* para crashes nativos.
5. **Salvamento**: autosave/recuperação no programa próprio; nos plugins, nunca salvar o arquivo do usuário sozinho.
6. **Validação** de entrada e de resultado (verificações de sanidade: equilíbrio de reações, pesos plausíveis).
7. **Testes automáticos** dentro do Revit/CAD (já temos AutoRun e ARMTESTE*) + testes do núcleo sem plataforma.
8. **Versões de formato**: arquivos e XData com número de versão, para abrir projetos antigos sem quebrar.

## 3. O que já existe nos nossos plugins
- StructuralDetailer: logs com pilha, AutoRun, armadilhas registradas no `CLAUDE.md`.
- Armadura BR: sincronizador que só enfileira nos eventos, processamento idempotente, autoteste com contagem de
  verificações, XData versionado.

## 4. A estudar
- Como Revit e AutoCAD tratam exceções de plugins (o que derruba o processo e o que só mostra erro).
- Journals do Revit e relatórios de erro do CAD para descobrir a causa de fechamentos.
- Como os comerciais (TQS, Eberick, SAP2000) recuperam trabalho após falha.
