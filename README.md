# PCEB — Programa de Cálculo Estrutural Brasileiro

Programa livre de cálculo estrutural **brasileiro**: concreto (pré-moldado e in loco), aço (laminado e formado a frio),
depois steel frame e wood frame — com normas ABNT e referências do meio nacional (ABECE, Gerdau, manuais clássicos).
Fluxo: análise → dimensionamento → detalhamento → fabricação → documentação.

Hoje o repositório reúne o estudo e os plugins **DEB — Detalhador Estrutural Brasileiro** para CAD e Revit.
Estratégia (decisões em `docs/00_escopo/escopo_organizado.md`): o Revit é o modelador 3D principal agora; depois,
o programa PCEB próprio (web + Windows, um núcleo só), que também poderá operar o Revit.

## Estrutura
| Pasta | Conteúdo |
|---|---|
| `docs/00_escopo/` | requisitos originais, escopo organizado (decisões e perguntas em aberto), robustez |
| `docs/01_catalogo/` | fichas de programas comerciais, open source e próprios; matriz; interfaces e desempenho; vento/CFD |
| `docs/02_biblioteca/` | referências; normas, livros e padrões ficam **fora do Git** (ver `normas/LEIAME.md`) |
| `docs/03_casos-teste/` | peças de referência para comparar programas |
| `docs/04_arquitetura/` | decisões de arquitetura e plano de migração |
| `docs/notas/` | rascunhos |
| `src/DEB.Cad/` | DEB para GstarCAD / AutoCAD / ZWCAD (ex-Armadura BR) |
| `src/DEB.Revit/` | DEB para Revit 2025/2026 (ex-StructuralDetailer) |
| `programas/` | downloads de terceiros (fora do Git) |
| `PCEB.sln` | solução com todos os projetos |

## Compilar
```powershell
dotnet build PCEB.sln -c Release -p:DeployToRevit=false
dotnet test src\DEB.Cad\tests\Armadura.Core.Tests -c Release
```

## Licença
Software livre sob **GPL-3** (`LICENSE`), com permissão adicional para ligação com APIs proprietárias
(Revit, AutoCAD, GstarCAD, ZWCAD, Robot, CSI, TQS — `LICENSE-EXCEPTION.md`).
Normas e demais materiais com direitos autorais não são distribuídos.
