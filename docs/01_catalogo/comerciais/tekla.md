# Tekla Structures (+ Tekla Structural Designer, Tedds)

| Fabricante | Licença | Plataforma | Instalado |
|---|---|---|---|
| Trimble | comercial (assinatura) | Windows | **PC2**: Tekla Structures 2025 SP6 (2025.0.53788) com ambiente **Brazil**; Structural Designer 2025; Tedds 27 |

Conferência de 05/11/2026 (PC2): ✓ = visto nos arquivos instalados do ambiente Brazil
(`C:\ProgramData\Trimble\Tekla Structures\2025.0\environments\Brazil`). **O programa ainda não foi aberto**:
falta ver a interface, um modelo e uma prancha gerada (próximo passo).

## Os três produtos
- **Tekla Structures**: modelo BIM de alto detalhe, armaduras 3D, pranchas e fabricação. É o que importa para a D19.
- **Tekla Structural Designer**: análise e dimensionamento de edifícios (EC, ACI, BS…; NBR não conhecida). API `TSDAPI.dll`.
- **Tekla Tedds**: memórias de cálculo em planilhas de engenharia.

## Ambiente Brazil ✓
Pastas por função: `General`, `Concrete`, `Precast`, `Cast-in-Place`, `Steel`, `Engineering`, `Contractor`,
`ConstructionManagement`.

### Aço e dobras (`General\profil\rebar_database.inp`) ✓
- Aços: **CA-25, CA-50, CA-60** (NBR 6118) e cordoalhas/fios de protensão **NBR 7482** (CP 145 a CP 240, RB/RN,
  9,5 a 15,7 mm, 3×3,0 a 3×5,0).
- Cada bitola tem dois usos (`main` = longitudinal, `tie/stirrup` = estribo), com raio de dobra, raio do gancho e
  comprimento mínimo do gancho em 90°, 135° e 180°.

Raios gravados, em múltiplos de φ, comparados com a NBR 6118:

| Aço / uso | Raio de dobra (barra dobrada) | Raio do gancho | NBR 6118 (diâmetro do pino) | Confere? |
|---|---|---|---|---|
| CA-50 longitudinal φ < 20 | 7,5φ (= pino 15φ) | 2,5φ (= 5φ) | gancho 5φ (Tab. 9.1); barra dobrada 15φ (18.2.2) | sim |
| CA-50 longitudinal φ ≥ 20 | 7,5φ | 4φ (= 8φ) | gancho 8φ | sim |
| CA-60 longitudinal | 9φ (= 18φ) | 3φ (= 6φ) | gancho 6φ; barra dobrada 18φ | sim |
| CA-25 longitudinal | 5φ (= 10φ) | 2φ (= 4φ) | gancho 4φ; barra dobrada 10φ | sim |
| Estribo φ ≤ 10 (CA-50/60) | 1,5φ | 1,5φ (= 3φ) | 3φ (Tab. 9.2) | sim |
| Estribo CA-50 12,5 e 16 | 2,5φ | 2,5φ (= 5φ) | 4φ (10 < φ < 20) | **não**: Tekla mais conservador |
| Estribo CA-50 φ ≥ 20 | 4φ | 4φ (= 8φ) | 5φ | **não**: Tekla mais conservador |

- Ganchos de estribo: 90° com 70–100 mm (≈ 10φ ≥ 7 cm) e 135°/180° com 50 mm (5φ ≥ 5 cm): confere com a 9.4.6.1.
- Ganchos de barra longitudinal: comprimento **fixo** de 200/150/125 mm (90°/135°/180°), não 8φ/4φ/2φ como na NBR.
- **Erro na tabela**: CA-60 com bitolas "55" e "64" (raios calculados como 5,5 e 6,4 mm). Provavelmente eram 5,5 e 6,4.

### Formatos de dobra ✓
- `Concrete\rebar_shape_catalog\RebarShapeCatalog.rscx` + `RebarShapeRules.xml` (189 regras). Formatos com nomes
  curtos: L1, L2, A, A1, B, U, R, R1, TD…
- Zonas de estribo (`standard_estribo.rst.zones`) e emenda automática (`AutomaticSplicingTool`).

### Componentes de pré-moldado do ambiente ✓
- Customizados: **Conexão_Viga_Gerber**, **Gerber** (dente), **Grampo**, **Recorte Laje Pi** (apoio simples e duplo).
- Os componentes de sistema (consolo, cálice, insertos, alveolares…) vêm do programa, não do ambiente (conferir
  no catálogo de aplicações e componentes ao abrir).
- Detalhes de prancha prontos (`General\Drawing Details`): chumbadores, stud bolts, **detalhe de rugosidade**,
  **instruções de içamento de lajes**.

### Pranchas ✓
- Configurações de desenho de armadura por formato **A1 a A4** (`Concrete\drawing_settings\Armadura_A*`), marcas de
  barra e estribo (`anotacao_barra.note`, `anotacao_estribo.note`), desenho-mestre `Desenhos_elementos_concreto`.
- Tabelas da prancha (`.tpl`): **lista de barras**, **tabela-resumo** (também por nome), **detalhes de dobra**,
  "Lista de Armaduras e Detalhes de Dobra – Pré-fabricados", "Lista de Peças – Pré-fabricados", carimbos e
  formatos A0–A4, lista de revisões, lista de material de forma e concreto.

### Relatórios de pré-moldado (`Precast\reports`) ✓
- Armaduras: lista de barras, por tipo, dobras, malhas, **treliças**, comprimento extra de fabricação, roscas,
  numeração das armaduras.
- Peças: lista de elementos, **lajes alveolares**, estacas, elementos de montagem, peças avulsas.
- Fábrica e montagem: centros de gravidade, controle de produção e expedição, fases, **loteamento**, resumo de
  matéria-prima, comparação das marcas dos elementos.

## Fabricação / CAM — **ponto mais forte**
- Pré-moldado: **Unitechnik**, **PXML**, **BVBS** (dobra de barras), ELiPLAN, HMS; aço: DSTV/NC, DXF de chapas.
  (Não conferido no PC2 ainda.)

## API / automação ✓
- **Tekla Open API (.NET)** em `C:\Program Files\Tekla Structures\2025.0\bin`: `Tekla.Structures.Model.dll`
  (peças, armaduras, cast units), `.Drawing.dll` (pranchas), `.Plugins.dll` (componentes próprios),
  `.Analysis.dll`, `.Catalogs.dll` (aços, perfis, formatos de dobra). Versão 2025.0.53788.
- Documentação no developer.tekla.com (não vem instalada).

## O que aproveitar no motor de detalhamento (D19)
- **Tabela de aço por uso** (longitudinal × estribo) com raio de dobra, raio do gancho e gancho mínimo: é o mesmo
  dado que o `DEB.Core` precisa. Conferir com o `Dobramento` do DEB.Cad, principalmente os estribos de 12,5 a 20.
- **Cast unit** + numeração de peças iguais e **comparação de marcas** (= "similares" do núcleo).
- Lista de barras, tabela-resumo e detalhes de dobra **separados por peça pré-moldada** (= quadro de aço da PP127).
- Relatórios de fábrica (CG, içamento, loteamento, expedição): camada transversal de pré-moldado do motor.
- Dente Gerber e grampo como componentes paramétricos: referência para o motor 4 (ligações).

## A confirmar (ao abrir o programa)
- Licença ativa no PC2 e se abre com o ambiente Brazil → papel "Precast" ou "Concrete".
- Gerar uma prancha de pilar pré-moldado e comparar com a PP127 (vistas, cortes, quadro, marcas).
- Componentes de sistema de pré-moldado disponíveis (consolo, cálice, insertos de içamento).
- Formatos CAM que as fábricas do escritório pedem (BVBS? Unitechnik?).
