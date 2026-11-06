# Detalhes padrão: do DWG do escritório para DXF (e daí para legendas do Revit)

Os detalhes padrão da prancha de pilar (raio de dobra, emendas, comprimento das barras, chaves de cisalhamento,
espaçamento das barras, alça de içamento, reforço de furos, aterramento) foram tirados do DWG de referência do P56
(`316-CON-DD-100-6113-DET-BAS-R02.dwg`, fora do Git). Cada detalhe vira um DXF simples, em cm, na escala da legenda,
com os layers `PCEB-ARMADURA`, `PCEB-CONTORNO`, `PCEB-FINA`, `PCEB-TRACEJADA`, `PCEB-COTA`, `PCEB-TEXTO` e
`PCEB-PREENCHIMENTO`. O plugin (`Presentation/LegendFactory.cs`) cria a legenda do Revit a partir dele na primeira vez
que uma prancha precisa dela; depois a legenda fica no projeto.

Os DXF (e os DWG equivalentes, para editar no CAD) ficam em `%AppData%\PCEB\DEB.Revit\Padroes\Detalhes\` — padrão do
escritório, fora do Git. O padrão de apresentação aponta cada um em `Legendas[].Arquivo` / `Escala`.

## Refazer a extração
1. DWG → DXF ASCII sem abrir o CAD do usuário (copie o DWG para uma pasta de trabalho antes):
   `accoreconsole.exe /i "<dwg>" /s "<scr>" /l en-US`, com o script
   `_.FILEDIA` `0` `_.DXFOUT` `<saida>.dxf` `_V` `_2013` `16` `_.QUIT` `_Y` (um item por linha).
2. `python -I extrair.py <saida>.dxf <pasta>` — recorta cada detalhe pela janela da viewport da folha (lista
   `DETALHES` no início do script), expande blocos e cotas, descarta a moldura/carimbo e grava `<pasta>\*.dxf` +
   `detalhes.json` (tamanhos).
3. Conferência: abrir cada DXF no accoreconsole, `SAVEAS` 2018 (gera o DWG) e `-PLOT` com `PublishToWeb PNG.pc3`.

Sem bibliotecas externas (`dxf.py` é um leitor mínimo). Hachuras com padrão (barras hachuradas das emendas) não são
convertidas; preenchimentos sólidos sim.
