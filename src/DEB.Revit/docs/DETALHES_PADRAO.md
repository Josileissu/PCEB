# Detalhes padrão (legendas)

- DXFs em `%AppData%\PCEB\DEB.Revit\Padroes\Detalhes\` (fora do Git), extraídos do DWG do P56 com
  `tools/detalhes_dxf` (README lá). `LegendFactory` cria a legenda que falta (linhas/textos/regiões nativas; estilos
  "PCEB - Detalhe …") duplicando uma legenda do projeto; sem nenhuma, copia uma do `Folha.ArquivoLegendaBase`
  (`EST_RVT_Residência.rte` tem 5) — a API não cria legenda do zero. Legenda existente nunca é refeita.
- AutoRun: fecha sozinho as janelas do Revit (`DialogBoxShowing`, ex.: aviso do modelo analítico ao abrir um .rte
  antigo) e `contarlegendas` lista as legendas de arquivos (`SD_FAMILIAS`). Abrir arquivo antigo pela API: use
  `LegendFactory.OpenQuietly` (descarta os avisos da atualização, que senão param numa janela).
- AutoCAD 2026 está no PC2: `accoreconsole.exe` converte DWG↔DXF e plota PNG sem janela (ver o README).
