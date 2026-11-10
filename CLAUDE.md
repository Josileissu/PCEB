# PCEB — Programa de Cálculo Estrutural Brasileiro

Repositório único (GPL-3 + `LICENSE-EXCEPTION.md`; GitHub **público** `Josileissu/PCEB`). O usuário é engenheiro
estrutural (pré-moldados, in loco, metálicas). Converse, comente o código e escreva interfaces em **português**.
Ele autorizou instalar o que for preciso e prefere que eu teste sozinho (Revit/CAD), sem tocar nas janelas dele.

## Estrutura
```
docs/          estudo: 00_escopo (decisões D1… e perguntas em aberto), 01_catalogo, 02_biblioteca,
               03_casos-teste, 04_arquitetura (plano de migração), notas
src/DEB.Core/  núcleo comum sem plataforma (netstandard2.0): normas, comprimentos, numeração, catálogos
tests/         testes do núcleo (xUnit)
src/DEB.Cad/   Detalhador Estrutural Brasileiro para CAD (ex-Armadura BR): GstarCAD, AutoCAD, ZWCAD — ver o CLAUDE.md de lá
src/DEB.Revit/ Detalhador Estrutural Brasileiro para Revit (ex-StructuralDetailer) — ver o CLAUDE.md de lá
programas/     downloads de terceiros (fora do Git)
PCEB.sln       compila tudo
```

## Compilar tudo
```powershell
cd <raiz do PCEB>   # no PC1: C:\Users\SUELIO087\Documents\desenvolvedor\PCEB
& "C:\Program Files\dotnet\dotnet.exe" build PCEB.sln -c Release -p:DeployToRevit=false -p:NoWarn=MSB3277
& "C:\Program Files\dotnet\dotnet.exe" test tests\DEB.Core.Tests -c Release
```
Versões padrão: GstarCAD 2021, AutoCAD 2026, Revit 2025. Outras versões: compile o projeto com o parâmetro
próprio (ver o CLAUDE.md de cada plugin). `dotnet` não está no PATH.

## Regras do projeto
- **Devagar e organizado**: passos pequenos; registrar pedidos e decisões em `docs/00_escopo/escopo_organizado.md`;
  não baixar, codar nem decidir arquitetura sem o usuário pedir.
- **Git**: o usuário controla o que entra e sai; nada de push sem ele pedir.
- **O repositório é público** (decisão do usuário, 10/11/2026: "deixar público"): nada de nomes de clientes, obras, escritórios ou
  pessoas no que vai para o Git (código, comentários, docs, commits). Use descrições neutras ("modelo real com 168 vigas", "padrão do
  escritório"). Os arquivos privados continuam fora do Git.
- **Verificação automática**: `.github/workflows/verificar.yml` roda os testes do núcleo (pt-BR e en-US) e compila o plugin do CAD
  (AutoCAD 2026, GstarCAD 2021, ZWCAD 2026) a cada envio. O DEB.Revit fica de fora (precisa do Revit instalado).
- **Fora do Git**: normas, livros, artigos, padrões do escritório (`docs/02_biblioteca/*`), SDKs baixados,
  `info_auxiliar/` e `auxiliar_cad/` do DEB.Cad.
- **Robustez**: um comando que falha não pode fechar o Revit/CAD nem perder trabalho (`docs/00_escopo/robustez_e_estabilidade.md`).
- **Identificadores gravados em arquivos de clientes não mudam** (XData `ARMADURA_BR`, comandos `ARM*`, parâmetros `SD_`).
- Regras de detalhamento: o DEB.Cad é a referência (decisão D7); serão extraídas para um núcleo comum (etapa 5).
- Instalador só quando o usuário pedir (não gerar Setup nem subir `<Version>` por conta própria).
- **Testes sem mouse/teclado reais** (SetCursorPos, SendInput): o usuário usa o computador ao mesmo tempo e um clique
  real já caiu no Revit dele. Use `BM_CLICK`, a API ou o autoteste; capture telas com PrintWindow.
- **Regressão visual** (10/11/2026): `tests/DEB.Core.Tests/RegressaoVisualTests` compara o desenho de 11 casos dos templates, entidade por
  entidade, com `tests/DEB.Core.Tests/Referencias/*.txt` (e lista textos sobrepostos). Mudou um template de propósito? Confira as
  diferenças que o teste mostra, regrave com `$env:ARM_ATUALIZAR_REFERENCIAS = "1"` e inclua os .txt no commit. Template novo ou
  caso novo relevante = acrescentar um caso lá.
- **Outro idioma do Windows**: `$env:ARM_CULTURA = "en-US"; dotnet test tests\DEB.Core.Tests` roda os testes noutra cultura (10/11/2026: 514 ok em en-US e de-DE).
- **Reuso** (10/11/2026): contexto de desenho e funções comuns dos templates em `DEB.Core/Templates/KitDesenho.cs` (`Ctx`, `KitDesenho`);
  formatação de números em `DEB.Core/Fmt.cs` (`N` invariante, `Virgula` para a prancha, `Mm` para chaves). Não criar cópias novas.
- **Prioridade (D19)**: o motor de detalhamento vem antes das análises estruturais
  (`docs/04_arquitetura/motor_detalhamento.md`). Lembre o usuário disso se o assunto for começar análise.

## Dois computadores (ver `docs/04_arquitetura/trabalho_em_paralelo.md`)
- **Os dois computadores implementam e testam** (decisão do usuário, 10/11/2026: "podemos remover a restrição de que no PC2 não
  pode ser feito testes e implementações"). Antes: PC1 = código, PC2 = estudo. O PC2 também tem os programas de estudo (Tekla, IDEA
  StatiCa) e o Revit 2025 e 2026, os dois liberados para teste.
- Leia `docs/00_escopo/coordenacao.md` no início da sessão e atualize no fim.
- Cada computador trabalha na sua branch (`pc1/...`, `pc2/...`); a `main` só recebe o que o usuário aprovar.
- `escopo_organizado.md` e `matriz_funcionalidades.md` só o PC1 edita; o PC2 anota em `docs/notas/pc2_pendencias.md`.
- A memória do Claude é local de cada computador: regra que precisa valer nos dois vai para este arquivo.
- Arquivos privados (Teste_PM.rvt = modelo-base dos testes desde 09/11/2026, Projeto1.rvt, prancha PP127, padrões do
  escritório): no PC1 ficam em `desenvolvedor\` (pasta-mãe
  do PCEB); no PC2, numa pasta privada do Google Drive (a combinar). Normas: `S:\Meu Drive\Normas` nos dois.
