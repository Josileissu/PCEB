# Pré-moldados

Material da disciplina 2182 do Prof. Bastos (<https://wwwp.feb.unesp.br/pbastos/pag_pre-moldados.htm>), em
`artigos/pbastos/`, mais a NBR 9062:2017 (`S:\Meu Drive\Normas\Pré-Moldado`).

## Fontes
| Material | Arquivo | O que tem |
|---|---|---|
| Apresentação do cap. 2 do livro de El Debs (*Concreto pré-moldado: fundamentos e aplicações*), 2018, 266 lâminas | `Pre-Moldados_Livro_Mounir_Cap._2.pptx` | princípios de projeto, forma dos elementos, análise, colapso progressivo, **tolerâncias e folgas (2.5)**, **cobrimento (2.6)**, **situações transitórias (2.7)**, estabilidade global (2.8) |
| Curso básico ABCIC, 2015, 227 p. | `C._Basico_Abcic.pdf` | visão geral do setor, sistemas, produção e montagem (pouco texto técnico) |
| Curso *Estruturas pré-fabricadas: do projeto à obra pronta* | `Curso_Estruturas_Iria.pdf` | concepção, ligações, lajes, NBR 9062 |
| Manual fib (versão brasileira, 2002), 129 p. | `Manual_Fib.pdf` | modulação, padronização, tolerâncias, sistemas estruturais, estabilização, diafragma, **ligações e consolos (4.6)**, pórticos |
| Manual técnico Munte (2007), 20 p. | `Manual_Munte.pdf` | prática de fábrica: consolos de pilar (retangular = ½ altura da viga, com dente; trapezoidal 50 cm para viga I), pilar com tubo de águas pluviais de 150 mm, vigas calha, escadas, lajes alveolares (apoios, recortes, capa) |
| Manual de lajes Munte, 2 p. | `Manual_Lajes_Munte.pdf` | tabela de lajes |

## O que serve agora (verificações sem cálculo)
- **Cobrimento** (NBR 9062, 9.2.1.1): Δc = 5 mm no pré-moldado; com fck ≥ 40 MPa e a/c ≤ 0,45, mais −5 mm, respeitando os
  mínimos de 15 mm (laje CA), **20 mm (vigas/pilares CA)**, 25 mm (protendido), 15 mm (delgadas protendidas), 20 mm (alveolar).
- **Cálice** (7.7.5.6): cobrimento da face interna das paredes pode ser 1 cm menor. Pilar vazado/pluvial (6.2): parede ≥ 7,5 cm
  (12,5 cm com água em contato).
- **Consolos** (7.3.3, 7.3.5, 7.3.6, 7.3.7) e **dentes Gerber** (7.4.4–7.4.6): ver as regras em
  `docs/04_arquitetura/verificacoes_normativas.md`. Ponto pedido pelo usuário: a **suspensão do dente** resiste a toda a carga
  e fica **concentrada na extremidade da viga junto ao dente**, em estribos fechados que envolvem a longitudinal; barras
  verticais ancoradas absorvem no máximo 0,4 Fd (7.4.6.2).
- **Tolerâncias e folgas** (El Debs 2.5, Munte): comprimento das peças = vão − folgas − tolerâncias; a família de viga do
  modelo de teste já usa `folga_esq`/`folga_dir`.

## O que serve depois
Situações transitórias (coeficiente dinâmico na desforma, transporte e montagem; PCI: 3 para insertos, 4 para içamento,
5 para dispositivos reutilizáveis — conferir com a NBR 9062, 5.3), estabilidade global, ligações semirrígidas.
