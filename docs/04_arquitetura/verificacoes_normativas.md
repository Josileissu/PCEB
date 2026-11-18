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
estribos verticais. **Apoio (D36)**: a família do consolo ganha o elemento **apoio** — neoprene, argamassa ou direto — com posição e dimensões;
de lá saem a, a2 e o afastamento lateral (C1, C3–C5) e o Hd estimado na fase de cálculo (9062 7.3.9: elastômero 0,16·Fd,
argamassa 0,5·Fd, a seco 0,8·Fd). Enquanto a família não tiver o apoio, C3/C4/C5 ficam como "Conferir".
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

**Etapa 2 (18/11/2026, `VerificacoesConsolo`)**: implementadas C1 (sem a2 = limite rigoroso, "Conferir"), C2 (informativa,
a/d ≤ balanço/d), C6/C7 (bitola e espaçamento pela ancoragem), C9 (pelo eixo do tirante), C10, C11 (0,4 ou 0,5 conforme a/d),
C13 e C15 ("Conferir": leitura da taxa a confirmar no livro de comentários). Uma vez por tipo de consolo no template do CAD e
por geometria no Revit (substituíram as verificações avulsas do `Check`). Ficam para o apoio na família (PC2): C3, C4, C5; para
depois: C8 (alça vertical, o catálogo não tem), C12, C14, C16, C17.

**Apoio (18/11/2026)**: o aparelho de apoio vem da família da viga (`PCEB_Apoio*`, PC2) pelo levantamento do Revit, entra no
arquivo de peças (`consolos[].apoio`) e na tabela "Consolos" do pilar (colunas Apoio, comprimento, largura, espessura, a2). Com ele:
C1 com o a2 real (falha = "Não atende"), C2 com a = face do pilar → centro do apoio, **C3** (a2 mínimo pela ancoragem do tirante),
**C4** (afastamento lateral > cobrimento → "Conferir" o fendilhamento), **C5** (alças: c ≤ a2 ≤ c + 3φ) e C10 com "≤ a".
Atenção: o a2 padrão da família (25 mm) não atende o C3 do consolo do P61 (tirante ø16 com barra soldada, c = 3,5: mínimo 5,1 cm).

## V — Viga pré-moldada (depois do template P2-8)
| Cód. | Verificação | Limite | Referência | Falha |
|---|---|---|---|---|
| V1 | Armadura mínima de flexão | ρmín da Tab. 17.3 (%, CA-50, d/h = 0,8): C20–C30 0,150; C35 0,164; C40 0,179; C45 0,194; C50 0,208; C55 0,211; C60 0,219; C65 0,226; C70 0,233; C75 0,239; C80 0,245; C85 0,251; C90 0,256 — transcrita em `TabelasNbr.RhoMinVigaPct`; absoluto 0,15 % | 6118 17.3.5.2.1 | Não atende |
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
| D6 | **Suspensão: posição** | estribos **fechados**, envolvendo a longitudinal, **concentrados na extremidade da viga junto ao dente**, no trecho **= cobrimento + dvig/4, arredondado para baixo** (D36), medido a partir da face do dente — é a cota do detalhe da viga | 9062 7.4.6.2, Fig. 23; D36 | Não atende |
| D7 | Suspensão por barras verticais | no máximo 0,4·Fd (precisa de Fd: Conferir) | 9062 7.4.6.2 | Conferir |
| D8 | Furos de pino no dente (regra do escritório, D23) | largura < 30 cm → 1 furo; ≥ 30 cm → 2; o furo passa a existir na família da viga (D36) | D23, D36 | Não atende |

## Fora desta etapa (precisam de esforço — fase de cálculo)
As,tir, As,cost e Asus pela carga (9062 7.3.5.3–7.3.5.4, 7.3.8, 7.4.6.1); biela (7.3.4, 7.4.7); Hd (7.3.9); As,mín com Nd;
smáx de estribo e ramos pelo Vd; flechas e fissuras; situações transitórias.

## Respostas do usuário (18/11/2026, D36)
1. Reduções de cobrimento da NBR 9062 **aplicadas automaticamente e avisadas** (G2 sai como "Conferir" quando o desenho só
   atende graças a elas, com as reduções escritas na mensagem).
2. Limites que dependem de Vd: "Conferir" com o limite mais rigoroso.
3. Elemento **apoio** na família do consolo (neoprene, argamassa, direto); **furo dos pinos** na família da viga.
4. Suspensão do dente: trecho = cobrimento + dvig/4, arredondado para baixo (D6).
5. Tabela 17.3 transcrita (V1).

## Ordem e situação
Etapa 1 (**feita**, branch `feat/verificacoes-normativas`): tipo de resultado no núcleo + P1–P16 e G1–G9 (juntando as duas cópias, corrigindo P8) com testes.
Etapa 2: C1–C17. Etapa 3: painel na prévia do Revit e do CAD. Etapa 4: V e D, depois do template da viga (P2-8).

Etapa 1, o que entrou: `DEB.Core/Normas/Verificacao/` (`ResultadoVerificacao`, `ListaVerificacoes`, `TabelasNbr` com as
Tabelas 7.2 e 17.3, `RegrasCobrimento` G1–G4, `VerificacoesPilar` P1–P14); o template do CAD (`ArmadurasPilar`) e a armadura do
Revit (`ColumnRebarSpec.Check`) chamam as mesmas regras; taxa máxima corrigida para 8 % com emendas; parâmetros novos
"Classe de agressividade" e "a/c ≤ 0,45" no template do pilar (Revit: `ExposureClass`, `FckMPa`, `WaterCement045`,
`AggregateMm`, `LongSteel`, `StirrupSteel` no modelo de armadura — ainda sem campo na janela, vão na Etapa 3);
testes em `VerificacoesNormativasTests`. Ficaram para depois: P9 (estribo no cruzamento com vigas, precisa da posição das
vigas), P15 (pilar vazado), G5–G9 (já parcialmente em `RegrasDobramento`/`RegrasTraspasse`; entram com o relatório).

## Relatórios e memoriais (proposta para discutir)
Os mesmos `ResultadoVerificacao` alimentam quatro saídas, do mais curto ao mais completo; o modelo do relatório mora no núcleo
(D26) e os plugins só mandam gravar:

| Nível | Onde aparece | Conteúdo |
|---|---|---|
| 1. **Avisos na prévia** | janela do Revit e prévia do CAD (Etapa 3) | selo geral (OK / Conferir / Não atende), contador por grupo, uma linha por falha com a dica |
| 2. **Relatório de verificação da peça** | arquivo por grupo de peças iguais (HTML para ver e imprimir em PDF pela PDF24; CSV/JSON para dados) | cabeçalho (marca, quantidade, seção, comprimento, materiais, classe de agressividade, cobrimento exigido com as reduções); tabela por grupo: código, verificação, valor, limite, situação, norma; **todas** as verificações, inclusive as OK — o "porquê" de cada número |
| 3. **Memorial de detalhamento da obra** | um documento por obra | premissas (normas e edições, materiais, classe, cobrimentos, regras do escritório), índice das peças com o selo de cada uma, lista das pendências "Conferir" com **aceite do engenheiro e comentário** (rastreabilidade: quem aceitou, quando, por quê) |
| 4. **Memorial de cálculo** (fase de cálculo) | idem | cada verificação com fórmula → "onde:" → números substituídos → resultado (formato visto no Gtool3D e no Precast Design); o `ResultadoVerificacao` ganha os campos `Formula` e `Memoria` |

Respostas (D37): HTML, só português; um relatório por grupo de peças iguais; aceite das pendências num arquivo da obra.

### Nível 2 implementado (18/11/2026)
- Núcleo: `DEB.Core/Documentacao/RelatorioVerificacao.cs` — `RelatorioGrupo` (marca, elemento, dados, verificações),
  `RelatorioHtml.Gerar/Gravar` (uma página sem arquivos externos; selo geral; pendências no topo; tabelas por grupo com valor,
  limite, situação e norma; impressão em PDF pelo navegador ou PDF24) e `AceitesObra` (`PCEB_aceites.json` na pasta da obra:
  marca, código, descrição, valor, engenheiro, data, comentário — o aceite só vale enquanto descrição e valor forem os mesmos).
- Arquivo: `<pasta da obra>\Relatórios PCEB\Verificação - <marca>.html`.
- CAD: `ARMTEMPLATE` grava na pasta do DWG (desenho sem nome: Documentos\PCEB); `ARMIMPORTARPECAS`, na pasta do arquivo de peças.
- Revit: o detalhamento dos pilares grava um por grupo na pasta do .rvt (testar no PC2).
- Só o pilar pré-moldado preenche as verificações por enquanto (`ResultadoTemplate.Verificacoes`/`DadosRelatorio`).
- **Tela do aceite** (18/11/2026): comando `ARMACEITES` no CAD (botão "Aceites" da paleta, grupo Geral). Lista as pendências
  "Conferir" de todos os relatórios da pasta da obra (a do DWG, ou outra escolhida na janela); o engenheiro marca, comenta e
  grava. O núcleo (`DEB.Core/Documentacao/RelatoriosObra.cs`) guarda os dados de cada grupo em `Relatórios PCEB\dados\<marca>.json`
  (gravados junto com o HTML), aplica as decisões no `PCEB_aceites.json`, regrava os relatórios e o índice
  `Relatórios PCEB\Verificações da obra.html` (selo de cada peça, pendências em aberto e aceitas). "Não atende" não pode ser
  aceito: corrige-se a peça. Como lê a pasta, serve também para os relatórios gravados pelo Revit (pasta do .rvt).
- Falta: quadro resumido na prancha.
