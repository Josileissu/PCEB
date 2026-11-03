# PCEB — Programa de Cálculo Estrutural Brasileiro (estudo)

Base de estudo para o **Programa de Cálculo Estrutural Brasileiro** descrito em `00_escopo/`:
concreto (pré-moldado e in loco), aço (laminado e formado a frio), depois steel frame e wood frame,
com normas ABNT e referências do meio nacional (ABECE, Gerdau, manuais clássicos). Por enquanto: **estudar e organizar**,
sem fixar linguagem nem código. Os plugins que já temos fazem parte do projeto:
`../StructuralDetailer` (Revit) e `../armadura` (Armadura BR, GstarCAD/AutoCAD).

## Estrutura
| Pasta | Conteúdo | Situação |
|---|---|---|
| `00_escopo/` | `requisitos_originais.md` (seu texto, intacto), `escopo_organizado.md` (componentes, sistemas, análises, fases, perguntas em aberto), `robustez_e_estabilidade.md` | feito |
| `01_catalogo/` | `criterios.md`, `matriz_funcionalidades.md`, `interfaces_e_desempenho.md`, `cfd_vento_similares.md`, `inventario_local.md`, fichas em `comerciais/`, `opensource/` e `proprios/` (Armadura BR, StructuralDetailer) | 1ª versão — lapidar |
| `02_biblioteca/` | `fontes_referencia.md` (ABECE, Gerdau, CBCA, ABCIC, livros), `normas/`, `livros/`, `artigos/`, `padroes-escritorio/` | lista feita; arquivos a reunir |
| `03_casos-teste/` | peças de referência para comparar programas | vazia |
| `04_arquitetura/` | decisões de arquitetura (web, Windows, plugins, núcleo) | vazia — fase 1 |
| `programas/` | downloads, um subdiretório por programa (`download/`, `scripts/`, `NOTAS.md`) | vazia |
| `notas/` | rascunhos e anotações soltas | rascunhos iniciais |

## Licença e versionamento
Software livre sob **GPL-3** (ver `LICENSE`), com permissão adicional para ligação com APIs fechadas
(Revit, AutoCAD, GstarCAD etc. — ver `LICENSE-EXCEPTION.md`). Vale também para o Armadura BR e o StructuralDetailer. Versionado em Git; o mantenedor controla o que entra.
Normas, livros, artigos e padrões do escritório **não** vão para o repositório (ver `02_biblioteca/normas/LEIAME.md`).

## Regras
- Downloads em pasta própria, tratados como não confiáveis; nossos scripts ficam fora dela.
- Fichas do catálogo seguem `01_catalogo/criterios.md`; ✓ marca o que foi conferido neste computador.
- Unidades: kN, m (esforços) e cm (seções/cotas), como no escritório.
