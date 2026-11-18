# Verificações normativas do detalhamento (Etapa 0)

Pedido do usuário (18/11/2026): **sem cálculo por enquanto (D19)**, mas já conferir as questões normativas do desenho —
posição dos estribos de suspensão, armaduras mínimas, taxas geométricas, espaçamentos etc. Este documento é a lista para o
usuário aprovar antes de qualquer código. Fontes: NBR 6118:2023 e NBR 9062:2017 (texto conferido nos PDFs de
`S:\Meu Drive\Normas`), apostilas do Prof. Bastos ([fichas](../02_biblioteca/fichas/README.md)).

## Como cada verificação responde
Uma regra do núcleo (`DEB.Core`, D26) devolve: **código**, grupo, o que confere, valor usado, limite, **situação** e
**referência** (norma + item), mais uma dica de correção. O CAD e o Revit mostram a mesma lista (prévia, D26).

| Situação | Quando |
|---|---|
| **OK** | atende |
| **Conferir** | não dá para decidir sem esforço (limite depende de Vd, Nd, Fd…) ou é recomendação; mostra o limite mais rigoroso |
| **Não atende** | contraria a norma com os dados do desenho |

## Situação atual no código (achados)
- **Duas cópias** das verificações do pilar: `DEB.Core/Elementos/Pilar/PreMoldado/ArmadurasPilar.cs` (template do CAD, ~linhas
  500–545) e `DEB.Core/Elementos/Pilar/PreMoldado/Modelo3D/ColumnRebarSpec.cs` (Revit, ~160–201). Não são iguais: a taxa
  geométrica só existe na do Revit. Viram uma lista só (Etapa 1).
- **Erro**: `ColumnRebarSpec.cs:201` avisa ρ > **4 %** "incluindo traspasse"; a NBR 6118:2023, 17.3.5.3.2, diz
  **As,máx = 0,08·Ac considerando a sobreposição nas emendas** (conferido na norma, p. 151, e na apostila de pilares).
- Cálice: `Motor/Componentes/Verificacoes.cs` já verifica pela NBR 9062 (7.7.2.4, Tab. 15, Fig. 25) — entra na mesma lista.

## G — Gerais (todos os elementos)
| Cód. | Verificação | Limite | Referência | Falha |
|---|---|---|---|---|
| G1 | Cobrimento nominal pela classe de agressividade | Tab. 7.2 (Δc = 10 mm): viga/pilar 25/30/40/50; laje 20/25/35/45; contato com solo 30/40/50 mm | 6118 7.4.7.2, Tab. 7.2 | Não atende |
| G2 | Redução do pré-moldado | Δc = 5 mm (−5 mm); com fck ≥ 40 MPa e a/c ≤ 0,45 mais −5 mm, nunca < 20 mm (viga/pilar CA), 15 mm (laje CA), 25 mm (protendido), 20 mm (alveolar) | 9062 9.2.1.1.1–9.2.1.1.2 | Não atende |
| G3 | cnom ≥ φ da barra, ≥ φ√n do feixe, ≥ 0,5 φ da bainha | — | 6118 7.4.7.5 | Não atende |
| G4 | Agregado: dmáx ≤ 1,2·cnom | — | 6118 7.4.7.6 | Não atende |
| G5 | Pino de dobramento das barras (Tab. 9.1) e dos estribos (Tab. 9.2); solda perto da dobra (4φ / 20φ) | tabelas | 6118 9.4.2.3, 9.4.6 | Não atende |
| G6 | Ganchos de tração: semicircular 2φ, 45° 4φ, 90° 8φ; barra lisa só semicircular; comprimida sem gancho | — | 6118 9.4.2.1, 9.4.2.3 | Não atende |
| G7 | Ganchos de estribo: 135°/180° ponta 5φt ≥ 5 cm; 90° ponta 10φt ≥ 7 cm (não para lisa); preferência 135° (D30 já usa 135°) | — | 6118 9.4.6.1 | Não atende |
| G8 | Traspasse ≥ l0t (já calculado em `RegrasTraspasse`); proporção de barras emendadas na mesma seção (Tab. 9.3) | — | 6118 9.5.2 | Não atende / Conferir |
| G9 | Barra transversal soldada de ancoragem: φt ≥ 0,6φ; a ≥ 5φ do início da ancoragem | — | 6118 9.4.2.2 | Não atende |

## P — Pilar pré-moldado (primeiro a implementar)
| Cód. | Verificação | Limite | Referência | Falha | Hoje |
|---|---|---|---|---|---|
| P1 | Menor dimensão | ≥ 19 cm; 14–19 cm só com γn (Tab. 13.1); área ≥ 360 cm² | 6118 13.2.3 | < 14 cm ou < 360 cm²: Não atende; 14–19: Conferir | — |
| P2 | Pilar-parede | lado maior > 5× menor → regras de 18.5 | 6118 18.4.1, 18.5 | Conferir | — |
| P3 | Bitola longitudinal | 10 mm ≤ φ ≤ b/8 | 6118 18.4.2.1 | Não atende | sim (2 cópias) |
| P4 | Barra em cada vértice (circular ≥ 6) | — | 6118 18.4.2.2 | Não atende | sim |
| P5 | Espaçamento livre | ≥ max(20 mm; φ, φ√n do feixe ou φ da luva; 1,2·dmáx), também nas emendas | 6118 18.4.2.2 | Não atende | parcial |
| P6 | Espaçamento entre eixos | ≤ min(2b; 400 mm) | 6118 18.4.2.2 | Não atende | sim |
| P7 | Taxa mínima | ρ ≥ 0,4 % (piso; o mínimo completo max(0,15·Nd/fyd; 0,004·Ac) precisa de Nd) | 6118 17.3.5.3.1 | Não atende (< 0,4 %) | sim (Revit) |
| P8 | **Taxa máxima** | **ρ ≤ 8 % incluindo as emendas** | 6118 17.3.5.3.2 | Não atende | **errado (4 %)** |
| P9 | Estribo em toda a altura, inclusive no cruzamento com vigas/consolos (zonas sem buraco) | — | 6118 18.4.3 | Não atende | parcial (sobreposição de zonas) |
| P10 | Bitola do estribo | φt ≥ max(5 mm; φ/4; φfeixe/4) | 6118 18.4.3 | Não atende | sim |
| P11 | Espaçamento dos estribos | ≤ min(200 mm; b; 12φ CA-50 / 24φ CA-25) | 6118 18.4.3 | Não atende | sim (só 12φ) |
| P12 | φt < φ/4 (mesmo aço) | s ≤ 90 000·φt²/(φ·fyk) | 6118 18.4.3 | Não atende | — |
| P13 | Concreto C55 a C90 | espaçamentos reduzidos em 50 % e ganchos ≥ 135° | 6118 18.4.3 | Conferir | — |
| P14 | Proteção contra flambagem | barras a ≤ 20φt do canto, no máx. 2 nesse trecho; demais com grampo (gancho envolvendo a barra) | 6118 18.2.4 | Não atende | sim |
| P15 | Pilar vazado / tubo de águas pluviais | parede ≥ 7,5 cm (12,5 cm com água em contato); cobrimento nas duas faces | 9062 6.2.1, 6.2.2 | Não atende | — |
| P16 | Içamento (alça, furo, insertos) | regras atuais do escritório (25–35 % do topo etc.) | padrão do escritório | Conferir | sim |

## C — Consolo (pilar)
Dados que o desenho já tem: b, h (engaste), h1 (face externa), a1 (comprimento), d, tirante (φ, n, ancoragem), costura,
estribos verticais. **Falta no modelo**: posição do aparelho de apoio (a, a2, afastamento lateral) — vem da viga apoiada ou
de um campo novo; sem ele, C3/C4/C5 ficam como "Conferir".
| Cód. | Verificação | Limite | Referência | Falha |
|---|---|---|---|---|
| C1 | Altura da face externa | h1 ≥ h/2 − a2 (Fig. 17) | 9062 7.3.3.1 | Não atende |
| C2 | Classificação | a/d ≤ 0,5 muito curto; 0,5–1,0 curto; 1,0–2,0 consolo; > 2 viga (6118) | 9062 7.3.2, 7.3.5.1 | informativo |
| C3 | Distância a2 do aparelho à face | c + φ (barra transversal soldada); c + 3,5φ (alça horizontal φ < 20); c + 5φ (alça horizontal φ ≥ 20); c + 4φ (alça vertical φ ≤ 16) | 9062 7.3.3.4 | Não atende |
| C4 | Afastamento lateral do aparelho > cobrimento → armar fendilhamento | — | 9062 7.3.3.3 | Conferir |
| C5 | Alças horizontais sem armadura de fendilhamento | c ≤ a2 ≤ c + 3φ | 9062 7.3.3.5 | Conferir |
| C6 | Tirante com alças horizontais | φ ≤ min(b_mín/8; 25 mm); espaçamento ≤ min(15φ; d) | 9062 7.3.3.6 | Não atende |
| C7 | Tirante com barra transversal soldada de mesmo φ | φ ≤ min(b_mín/6; 25 mm); espaçamento ≤ min(20φ; d) | 9062 7.3.3.7 | Não atende |
| C8 | Alças verticais | proibidas para φ > 16 mm | 9062 7.3.3.16 | Não atende |
| C9 | **Tirante no quinto superior** | toda a armadura do tirante a ≤ h/5 da borda tracionada | 9062 7.3.3.11 | Não atende |
| C10 | Costura: bitola e espaçamento | φ ≤ b_mín/15; espaçamento vertical ≤ min(10 cm; a) | 9062 7.3.3.12 | Não atende |
| C11 | Costura: distribuição e quantidade | em 2/3·d junto ao tirante; As,cost/s ≥ 0,4·As,tir/d (curto) ou 0,5 (muito curto); ≥ 0,15 cm²/m·b | 9062 7.3.6 | Não atende |
| C12 | Dispensa de costura | d > 4(a + a0) → pele ρ ≥ 0,002 por face na zona 2 | 9062 7.3.3.13 | informativo |
| C13 | Taxa mecânica do tirante | ω = (As,tir/b·d)·fyk/fck ≥ 0,04 (a/d ≤ 2) | 9062 7.3.5.2 | Não atende |
| C14 | Tirante sem redução de seção até o engaste | — (exceto a/d > 2) | 9062 7.3.5.1 | Não atende |
| C15 | Estribos verticais (a/d ≤ 1, carga direta) | taxa ≥ 0,15 %·b·h | 9062 7.3.7.2 | Não atende |
| C16 | Armadura no elemento-suporte | barras no pilar com As ≥ As,tir na face do consolo | 9062 7.3.3.14, Fig. 20 | Não atende |
| C17 | Ancoragem do tirante no pilar | lb pela 6118 (no duplo, passante) | 9062 7.3.5.5 | Não atende |

## V — Viga pré-moldada (depois do template P2-8)
| Cód. | Verificação | Limite | Referência | Falha |
|---|---|---|---|---|
| V1 | Armadura mínima de flexão | ρmín da Tab. 17.3 (por fck, seção retangular; a tabela é imagem na norma — valores a transcrever e conferir) | 6118 17.3.5.2.1 | Não atende |
| V2 | Armadura máxima | As + As' ≤ 4 % Ac fora das emendas | 6118 17.3.5.2.4 | Não atende |
| V3 | Pele | h > 60 cm: 0,10 % Ac,alma por face, ≤ 5 cm²/m, s ≤ min(20 cm; d/3) | 6118 17.3.5.2.3, 18.3.5 | Não atende |
| V4 | Espaçamento livre das longitudinais | horizontal ≥ max(20 mm; φ; 1,2·dmáx); vertical ≥ max(20 mm; φ; 0,5·dmáx) | 6118 18.3.2.2 | Não atende |
| V5 | Bitola do estribo | 5 mm ≤ φt ≤ bw/10 | 6118 18.3.3.2 | Não atende |
| V6 | Espaçamento dos estribos | sem Vd: limite rigoroso 0,3d ≤ 200 mm como Conferir; 0,6d ≤ 300 mm como Não atende | 6118 18.3.3.2 | Conferir / Não atende |
| V7 | Distância entre ramos do estribo | ≤ 0,6d ≤ 350 mm (Conferir); ≤ d ≤ 800 mm (Não atende) | 6118 18.3.3.2 | idem |
| V8 | Estribo mínimo | ρsw ≥ 0,2·fctm/fywk | 6118 17.4.1.1.1 | Não atende |
| V9 | Ligação mesa-alma (seção T/L com aba) | ≥ 1,5 cm²/m | 6118 18.3.7 | Não atende |
| V10 | Suspensão viga apoiada em viga | ≥ 75 % na viga de apoio em h_apoio (h/2 de cada lado), ≤ 25 % na apoiada; sem continuidade: tudo em h/2 | 6118 18.3.6 | Conferir (sem Vd só a posição) |
| V11 | Ancoragem nos apoios sem dente | prolongar ≥ 1/3 (ou 1/4) de As,vão; comprimento mínimo de 18.3.2.4.1 a conferir | 6118 18.3.2.4 | Conferir |
| V12 | Apoio sem recorte (pré-moldada) | disposições de 7.3.3; costura Ash = Asv em 2d/3; ancoragem 2 lb | 9062 7.5, Fig. 24 | Conferir |

## D — Dente Gerber
| Cód. | Verificação | Limite | Referência | Falha |
|---|---|---|---|---|
| D1 | Tirante ancorado no dente por barra transversal de mesmo φ soldada ou por alças horizontais (e 7.3.3) | — | 9062 7.4.4.1 | Não atende |
| D2 | Ancoragem do tirante na viga começa a (dvig − d) do primeiro estribo de suspensão, com lb de **má aderência** | — | 9062 7.4.4.2, Fig. 23 | Não atende |
| D3 | Estribos horizontais do dente ancorados na face externa e penetrando **1,5·lb** na viga | — | 9062 7.4.5.1 | Não atende |
| D4 | Estribos verticais no dente | taxa ≥ 0,15 %·b·h (via 7.3.7) | 9062 7.4.5.2 | Não atende |
| D5 | Regras do consolo no dente (C3, C6, C7, C9–C11) | — | 9062 7.4.5.3 | Não atende |
| D6 | **Suspensão: posição** | estribos **fechados**, envolvendo a longitudinal, **concentrados na extremidade da viga junto ao dente**; a Fig. 23 indica a faixa (cota "≤ dvig/4" junto a Rsus) — **confirmar a leitura da figura com o livro de comentários da IBRACON** | 9062 7.4.6.2, Fig. 23 | Não atende |
| D7 | Suspensão por barras verticais | no máximo 0,4·Fd (precisa de Fd: Conferir) | 9062 7.4.6.2 | Conferir |
| D8 | Furos de pino no dente (regra do escritório, D23) | largura < 30 cm → 1 furo; ≥ 30 cm → 2 | D23 | Não atende |

## Fora desta etapa (precisam de esforço — fase de cálculo)
As,tir, As,cost e Asus pela carga (9062 7.3.5.3–7.3.5.4, 7.3.8, 7.4.6.1); biela (7.3.4, 7.4.7); Hd (7.3.9); As,mín com Nd;
smáx de estribo e ramos pelo Vd; flechas e fissuras; situações transitórias.

## Perguntas ao usuário
1. **Cobrimento**: aplicar automaticamente as reduções da NBR 9062 (−5 mm e, com fck ≥ 40 e a/c ≤ 0,45, mais −5 mm) ou só
   avisar? O padrão do escritório usa qual cobrimento no pilar e na viga?
2. **Severidade dos limites que dependem de Vd** (V6, V7): começar com "Conferir" no limite rigoroso, como proposto?
3. **Aparelho de apoio no consolo** (C3–C5): ler da viga apoiada no modelo (Revit) ou criar campo no tipo de consolo?
4. **Suspensão do dente (D6)**: confirmar no livro impresso de comentários da NBR 9062 a faixa da Fig. 23.
5. **Tabela 17.3** (V1): transcrever os valores do PDF (a tabela é imagem) e conferir com a apostila de flexão.

## Ordem proposta
Etapa 1: tipo de resultado no núcleo + P1–P16 e G1–G9 (juntando as duas cópias, corrigindo P8) com testes.
Etapa 2: C1–C17. Etapa 3: painel na prévia do Revit e do CAD. Etapa 4: V e D, depois do template da viga (P2-8).
