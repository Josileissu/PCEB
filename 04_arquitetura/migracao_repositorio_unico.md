# Migração para repositório único e novo nome dos plugins

Decisões de 03/11/2026: **um único repositório (PCEB)**; os dois plugins passam a se chamar
**Detalhador Estrutural Brasileiro** — no CAD (ex-Armadura BR) e no Revit (ex-StructuralDetailer).
Ports futuros: **Blender**, **FreeCAD** e o **programa PCEB**.

## 1. Estrutura alvo (nomes de pasta são proposta)
```
PCEB/
  docs/                         estudo atual (00_escopo, 01_catalogo, 02_biblioteca, ...)
  src/
    Nucleo/                     regras e cálculos sem plataforma (nasce do Armadura.Core, netstandard2.0)
    DEB.Cad/                    ex-Armadura BR: GstarCAD, AutoCAD, ZWCAD (fonte compartilhada + um projeto por CAD)
    DEB.Revit/                  ex-StructuralDetailer
    (futuro) .Blender/ .FreeCAD/ App/
  tests/
  instalador/
  sdk/                          SDKs de terceiros que já estão no Git do Armadura BR
  LICENSE, LICENSE-EXCEPTION.md, README.md, CLAUDE.md
```

## 2. O que muda e o que NÃO muda
O nome visível muda; os identificadores gravados nos **arquivos dos clientes** ficam, para os projetos antigos
continuarem abrindo e funcionando.

| Item | Ação |
|---|---|
| Nome exibido (paleta, faixa de opções, janelas, instalador) | → **Detalhador Estrutural Brasileiro** |
| Pastas, projetos, DLLs, namespaces | → novos nomes |
| XData `ARMADURA_BR` nos DWG | **mantém** (DWGs já detalhados) |
| Comandos `ARM*` no CAD | **mantém** (hábito dos usuários; aliases) — rever depois se quiser |
| Parâmetros `SD_Aço`, `SD_Peso (kg)` nos RVT | **mantém** (recomendação; usuário não se opôs) |
| `%AppData%\ArmaduraBR`, `%AppData%\StructuralDetailer` (config, padrões, layers, templates, logs) | nova pasta; **migração automática** na 1ª execução |
| Registro do GstarCAD (autoload) e `.addin` do Revit | novos; o instalador remove os antigos |
| `AppId` do instalador Inno Setup | **mantém**, para a nova versão substituir a antiga como atualização |
| Remoto GitHub `armadura-gstarcad` (privado) | usuário decide: renomear o repositório ou criar um novo para o PCEB |

## 3. Etapas (cada uma com teste antes da próxima)
| # | Etapa | Pré-requisito |
|---|---|---|
| 0 | ✅ Commitar o trabalho pendente do Armadura BR — feito pelo usuário | — |
| 1 | ✅ Armadura BR em `src/DEB.Cad` com histórico (git subtree); materiais fora do Git copiados; build GstarCAD/AutoCAD/ZWCAD ok, 178 testes ok (03/11/2026) | — |
| 2 | ✅ StructuralDetailer em `src/DEB.Revit`; build Revit 2025/2026 ok (03/11/2026) | — |
| 3 | Estudo vai para `docs/`; CLAUDE.md unificado; build de tudo a partir do PCEB | — |
| 4 | Renomear projetos/DLLs/textos para Detalhador Estrutural Brasileiro + migração de `%AppData%` | autoteste do CAD e AutoRun do Revit passando |
| 5 | Separar o `Nucleo` (regras comuns) e o plugin Revit passar a usá-lo (decisão D7) | — |
| 6 | Instalador novo (só quando o usuário pedir) | — |

As pastas antigas `armadura/` e `StructuralDetailer/` só são apagadas depois que tudo funcionar a partir do PCEB,
e com confirmação do usuário.

## 4. Consequência dos ports para Blender e FreeCAD
Blender e FreeCAD são programados em **Python**; os plugins atuais e o núcleo estão em **C#**. Opções (decidir na fase 1
de arquitetura, não agora):
- núcleo como **serviço local** (o mesmo que servirá a interface web) — qualquer plataforma conversa por JSON;
- ponte Python ↔ .NET (pythonnet);
- reescrever partes do núcleo.
A primeira casa com a decisão D3 (web e Windows com um núcleo só) e com "o PCEB operar o Revit".
