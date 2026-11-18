# Pilares de concreto armado — Bastos

- **Fonte**: BASTOS, P.S.S. *Pilares de Concreto Armado*, UNESP Bauru, 2024, 188 p. — `artigos/pbastos/Pilares.pdf`
  (<https://wwwp.feb.unesp.br/pbastos/concreto2/Pilares.pdf>). Base: NBR 6118:2023.
- **Conteúdo**: Parte I — flexão composta normal e oblíqua, ábacos. Parte II — cobrimento, esbeltez, contraventamento,
  excentricidades, 2ª ordem (pilar-padrão com curvatura e rigidez aproximadas), **disposições construtivas (cap. 11, p. 74–78)**,
  roteiro e exemplos (pilares intermediários, de extremidade e de canto).

## O que serve agora (verificações do pilar pré-moldado, sem cálculo)
| Regra | Item NBR 6118 | Já no PCEB? |
|---|---|---|
| Dimensão mínima 19 cm (14–19 cm com γn = 1,95 − 0,05b); área ≥ 360 cm² | 13.2.3, Tab. 13.1 | não |
| Lado maior > 5× menor → pilar-parede (18.5) | 18.4, 18.5 | não |
| 10 mm ≤ φ ≤ b/8 | 18.4.2.1 | sim (duas cópias) |
| Barra em cada vértice; circular ≥ 6 barras | 18.4.2.2 | sim (vértices) |
| Livre ≥ max(20 mm; φ, φ do feixe = φ√n ou da luva; 1,2·dmáx), também nas emendas | 18.4.2.2 | parcial (sem feixe/luva/agregado) |
| Entre eixos ≤ min(2b; 400 mm) | 18.4.2.2 | sim |
| As,mín = max(0,15·Nd/fyd; 0,004·Ac) — sem Nd só o piso 0,4 % | 17.3.5.3.1 | sim (0,4 %) |
| **As,máx = 0,08·Ac, incluindo emendas** | 17.3.5.3.2 | **errado no Revit (4 %)** |
| Estribo em toda a altura, obrigatório no cruzamento com vigas e lajes | 18.4.3 | — |
| φt ≥ max(5 mm; φ/4; φfeixe/4) | 18.4.3 | sim |
| s ≤ min(200 mm; b; 24φ CA-25 / 12φ CA-50) | 18.4.3 | sim (só 12φ) |
| φt < φ/4 admitido com s ≤ 90 000·φt²/(φ·fyk) | 18.4.3 | não |
| C55–C90: espaçamentos −50 % e ganchos ≥ 135° | 18.4.3 | não |
| Proteção contra flambagem: barras a ≤ 20φt do canto, no máx. 2 nesse trecho; demais com grampo | 18.2.4 | sim |

## O que serve depois
Dimensionamento à flexão composta (equações adimensionais, ábacos), 2ª ordem local, exemplos 13.x como casos-teste.
