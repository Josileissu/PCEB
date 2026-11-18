# Força cortante em vigas — Bastos

- **Fonte**: BASTOS, P.S.S. *Dimensionamento de Vigas de Concreto Armado à Força Cortante*, UNESP Bauru, 2023, 81 p. —
  `artigos/pbastos/Cortante.pdf` (<https://wwwp.feb.unesp.br/pbastos/concreto2/Cortante.pdf>). Base: NBR 6118:2023.
- **Conteúdo**: treliça clássica e generalizada, modelos de cálculo I e II, armadura mínima (5.10), equações simplificadas
  por fck (Tab. 5.3), **disposições construtivas (5.12)**, redução da cortante junto aos apoios (5.13), **armadura de
  suspensão (5.14)**, cinco exemplos numéricos.

## O que serve agora
| Regra | Item NBR 6118 |
|---|---|
| 5 mm ≤ φt ≤ bw/10; barra lisa ≤ 12 mm; tela ≥ 4,2 mm | 18.3.3.2 |
| s mínimo: passagem do vibrador (Bastos sugere φvibrador + 1 cm) | 18.3.3.2 |
| smáx = 0,6d ≤ 300 mm se Vd ≤ 0,67 VRd2; senão 0,3d ≤ 200 mm (sem Vd: usar o limite rigoroso como aviso) | 18.3.3.2 |
| Entre ramos: d ≤ 800 mm se Vd ≤ 0,20 VRd2; senão 0,6d ≤ 350 mm | 18.3.3.2 |
| Traspasse de estribo só em tela ou barra de alta aderência | 18.3.3.2 |
| Gancho 135°/semicircular: ponta 5φt ≥ 5 cm; 90°: 10φt ≥ 7 cm (não para lisa); preferência 135° | 9.4.6.1 |
| Pino de dobramento do estribo (Tab. 9.2): φ ≤ 10 → 3φt; 10–20 → 5φt (CA-50); ≥ 20 → 8φt (CA-50) | 9.4.6, Tab. 9.2 |
| **Suspensão viga-viga**: ≥ 75 % na viga de apoio em h_apoio (h/2 de cada lado do cruzamento); ≤ 25 % na apoiada em h_apoiada; viga que não continua: tudo em h/2; redução (1 − hd/h_apoio) com faces superiores coincidentes | 18.3.6 |
| ρsw,mín = 0,2·fctm/fywk | 17.4.1.1.1 |

Para **dente Gerber** a suspensão segue a NBR 9062, 7.4.6 (ver [pre_moldados.md](pre_moldados.md)).

## O que serve depois
VRd2, Vc, Asw pelos modelos I e II; exemplos 1 a 5 como casos-teste do dimensionamento.
