# Mapa — quero mexer em X, leio o quê

Caminhos de hoje. Regras gerais: `AGENTS.md` da raiz. Áreas e testes: `areas.md`. Comandos: `comandos.md`.

| Quero mexer em… | Leia |
|---|---|
| Regra de engenharia (comprimento, dobra, traspasse, norma) | `src/DEB.Core/Calculo`, `src/DEB.Core/Normas`; `src/DEB.Cad/docs/CONVENCOES.md` (comprimento real) |
| Template do CAD (pilar, viga, bloco, reservatório) | `src/DEB.Cad/docs/TEMPLATES.md`; requisitos em `src/DEB.Cad/docs/*_REQUISITOS.md`; `src/DEB.Core/Templates/` |
| Motor de detalhamento (contratos, receitas, padrão, prancha) | `docs/04_arquitetura/motor_detalhamento.md`, `docs/04_arquitetura/motor_contratos.md`; `src/DEB.Core/Motor/` |
| Barra, faixa, estribo, relação do aço no CAD | `src/DEB.Cad/AGENTS.md` (armadilhas) e `src/DEB.Cad/docs/CONVENCOES.md` |
| Plugin do CAD em outra versão (AutoCAD, ZWCAD) | `src/DEB.Cad/AGENTS.md`, seção "Port para AutoCAD" |
| Pilar pré-moldado no Revit | `src/DEB.Revit/AGENTS.md`; `docs/00_escopo/tarefas/P1-1.md`; `docs/04_arquitetura/padroes_apresentacao.md` |
| Revit → CAD (peças exportadas) | `src/DEB.Revit/docs/EXPORTAR_PARA_O_CAD.md`, `src/DEB.Revit/docs/CONFERIR_PECAS.md`; `src/DEB.Core/Intercambio`, `src/DEB.Core/Pecas` |
| Laje alveolar no Revit | `src/DEB.Revit/docs/LAJE_ALVEOLAR.md`; `src/DEB.Core/Lajes` |
| Pranchas de formas no Revit | `src/DEB.Revit/docs/FORMAS.md`, `docs/04_arquitetura/pranchas_de_formas.md`; `src/DEB.Core/Formas` |
| Folha nova no Revit | D29 no `AGENTS.md` da raiz: só as vistas prontas |
| Importar .cad do QiArmaduras | `src/DEB.Cad/docs/FORMATO_QICAD.md`; `src/DEB.Core/Importacao` |
| Quadro de tarefas / decisões | `docs/00_escopo/coordenacao.md`; decisões em `docs/00_escopo/escopo_organizado.md` |
| Programas de estudo (Tekla, TQS, FreeCAD…) | `docs/01_catalogo/programas.md` |
