# Trabalho em paralelo: dois computadores (proposta de 05/11/2026)

## Papéis
| Computador | Programas | Frente de trabalho |
|---|---|---|
| **PC1** (este) | Revit 2025/2026, GstarCAD, AutoCAD, TQS, Eberick, CYPE, SAP2000, Robot | **motor de detalhamento + DEB.Revit** (código, etapa E) |
| **PC2** | AutoCAD, Revit, CYPE, Eberick, SAP2000, IDEA StatiCa, Tekla, Tekla Structures (informado pelo usuário; inventário completo é a tarefa P2-2) | **estudo**: inventário, fichas do Tekla e do IDEA conferidas, `matriz_detalhamento.md`, regras por elemento (etapa D); testes do DEB.Revit no Revit de lá |

As frentes não mexem nos mesmos arquivos: o PC1 trabalha em `src/` e `tests/`, e o PC2 em `docs/01_catalogo/` e
`docs/04_arquitetura/regras_detalhamento/`.

## Ponto de encontro: o GitHub (`Josileissu/PCEB`, privado)
- **Uma branch por computador**: `pc1/...` e `pc2/...` (ex.: `pc2/estudo-tekla`). Ninguém faz commit direto na `main`.
- **Início de cada sessão**: `git pull` da `main` + rebase da própria branch.
- **Fim de cada sessão**: commit e push **da própria branch** (com aprovação do usuário).
- **Entrar na `main`**: só com aprovação do usuário (pull request ou merge pedido a um dos Claudes).

## Arquivos "quentes" (risco de conflito)
- `docs/00_escopo/escopo_organizado.md` (decisões D… e perguntas) e `01_catalogo/matriz_funcionalidades.md`.
- Regra: **só o PC1 edita esses dois**. O PC2 registra o que tiver em `docs/notas/pc2_pendencias.md`, e o PC1
  consolida no próximo merge.

## Quadro de coordenação
`docs/00_escopo/coordenacao.md`: uma tabela **tarefa | computador | situação | branch**. Cada Claude lê no início
e atualiza no fim da sessão. É o que permite um continuar de onde o outro parou.

## O que fica fora do Git e como chega ao PC2
| Item | Onde fica | No PC2 |
|---|---|---|
| Normas | `S:\Meu Drive\Normas` (Google Drive) | mesma conta do Drive → mesmo caminho |
| Padrões do escritório, pranchas-modelo (PP127), `Projeto1.rvt` | PC1, fora do Git | copiar para uma pasta do Drive (ex.: `S:\Meu Drive\PCEB_privado`) |
| Downloads de programas (`programas/*/download`) | PC1 | baixar de novo pelo link do `NOTAS.md` (só o que for usar) |
| Memória do Claude (`~/.claude/.../memory`) | **local de cada computador** | não sincroniza. O conhecimento durável vai para os `CLAUDE.md` e `docs/` do repositório |

## Preparar o PC2 (uma vez)
1. Instalar Git, GitHub CLI (`gh auth login`), .NET SDK 8 e Claude Code; entrar com a mesma conta.
2. `git clone https://github.com/Josileissu/PCEB.git` (de preferência no mesmo caminho do PC1:
   `C:\Users\<usuário>\Documents\desenvolvedor\PCEB`).
3. Conferir se o Drive está montado em `S:` e se o Tekla abre.
4. Primeira sessão no PC2: "leia o CLAUDE.md e o `docs/00_escopo/coordenacao.md` e pegue a tarefa do PC2".

## Ajustes no repositório antes de começar
- Os `CLAUDE.md` têm caminhos absolutos do PC1 (`C:\Users\SUELIO087\...`): trocar por caminhos relativos ou avisar que mudam.
- Levar para o `CLAUDE.md` do PCEB as regras que hoje só estão na memória do PC1 (ritmo devagar, instalador só quando
  pedir, sem mouse real nos testes, D19 detalhamento primeiro, testes autônomos no Revit).

## Conversa direta entre os dois Claudes (opcional)
O Claude Code consegue enviar mensagens para sessões do mesmo usuário em outros computadores quando o
**Remote Control** está ligado. Seria útil para avisos ("fiz push da branch X"), mas o Git e o quadro de
coordenação já bastam. Testar quando o PC2 estiver pronto.
