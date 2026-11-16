# Ferramentas (painel "Ferramentas", `src/Tools`)

Os scripts originais (`auxiliar_scripts/`) foram apagados a pedido do usuário em 04/11/2026; a lógica deles está aqui.
- `ToolCommand`: base de todos — barreira de exceções (mensagem + log `logs/ferramentas_AAAAMMDD.log`), `ToolCancelled`.
- `ToolDialogs`: `FieldForm` (formulário com validação) e `CheckListWindow` (lista com Shift+clique, Todos/Nenhum/Inverter/filtro).
- `RenumberCommands`: pilares, vigas, lajes, fundações, paredes, peças. Ordem em **DEB.Core** (`Numeracao/OrdenacaoMarcas`,
  com testes); tolerância 0,50 m; entra em grupos aninhados. `Apply()` sem janela (usado pelo AutoRun).
- `ModelToolsCommands`: Selecionar por marca (`FiltroMarca` no núcleo) e Alterar material (tipo; ou instância quando a
  família tem o material na peça — caso do Projeto1).
- `CopyFromFileCommands`: fundações e montagens com vistas/folhas de outro .rvt (subtransação por montagem; não fecha
  documento que já estava aberto). `ExportCsvCommand`: 6 CSVs, mesmas colunas do ExportarTudo_Colunas.
- Janelas WPF não testadas pelo AutoRun (só a lógica): conferir com o usuário.
