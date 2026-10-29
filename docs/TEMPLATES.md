# Templates de armadura (ambiente preparado em 2026-10-27)

Pedido do usuário: templates para os elementos com padrão estabelecido no escritório. A implementação
de cada um começa quando ele enviar os PDFs/DWGs (pastas em `info_auxiliar/templates/<elemento>/`).

## Elementos previstos (`CategoriaElemento`)
| Categoria | Variações citadas |
|---|---|
| Base circular de reservatório | fundação |
| Base retangular de reservatório | fundação |
| Bloco de 1 estaca | com/sem colarinho ou cálice; embutimento para pilar pré-moldado |
| Bloco de 2 estacas | idem |
| Pilar pré-moldado | levantamento em `docs/PILAR_PREMOLDADO_REQUISITOS.md` (2026-10-29): categorias PP-1…PP-5, normas, padrão P61/PP127 |
| Viga pré-moldada | protendida ou não |
| Pilar moldado in loco | |
| Viga moldada in loco | |
| Laje | |

## Estrutura do código
- `src/Armadura.Core/Templates/Templates.cs` (testável, sem CAD):
  - `ITemplateArmadura`: `Id`, `Nome`, `Categoria`, `Descricao`, `Parametros`, `Gerar(valores, cfg)`;
  - `ParametroTemplate` (número, inteiro, texto, opção, bitola, sim/não; grupo, unidade, limites);
  - `ValoresTemplate` (valores digitados, com leitura numérica tolerante a vírgula);
  - `ResultadoTemplate`: `Barras` (`BarraTemplate`: vértices em cm relativos à inserção, bulges,
    formatos dos segmentos/ganchos, sobrepostos, cotados, bitola, aço, quantidade, espaçamento, estribo),
    `Contornos` (linhas de forma) e `Avisos`;
  - `CatalogoTemplates`: registro, busca por categoria, `Validar`.
- `src/Armadura.Cad/ComandosTemplate.cs`: comando **ARMTEMPLATE** (janela com a árvore de elementos e os
  parâmetros num PropertyGrid dinâmico → ponto de inserção → desenho), `Desenhar` (contornos + armaduras
  pelo `ServicoBarra.Criar`). Sem templates registrados, o comando lista os elementos previstos.
- Estribos fechados em qualquer poligonal: `GeradorEstribo.FechadoPoligono` (o template monta a volta e
  escolhe o canto dos ganchos).

## Para implementar um template
1. Nova classe em `src/Armadura.Core/Templates/<Elemento>/`, implementando `ITemplateArmadura`.
2. Registrar no carregamento do plugin (`CatalogoTemplates.Registrar`).
3. Testes de unidade com as medidas de um projeto real (do PDF/DWG do usuário) e uma verificação no autoteste.
4. Próximos passos previstos: faixas geradas pelo template (distribuição), cortes, quadro de ferros por
   elemento, salvar conjuntos de valores ("predefinições") por elemento.

## Implementados
### Base de reservatório (`Templates/BaseReservatorio/TemplateBaseReservatorio.cs`, 2026-10-27)
Padrão dos projetos Assaí SPA/SRP/ASC (2024–2025). Circular ou retangular, num template só.
- Parâmetros: geometria (forma, D ou Lx/Ly, h, cobrimentos 4/5, reservatório Ø e capacidade), estacas (anéis,
  grade ou nenhuma), malha inferior e superior (aço, bitola, espaçamento, dobra: até a outra face/fixa/sem),
  faixa central reforçada (largura e bitola), borda (U: bitola, espaçamento, aba; retangular: barras de borda
  por face; circular: anel com nº de barras, bitola e emenda), malha intermediária (barras ou tela, camadas),
  barras VAR (posição única ou uma por barra), escala e nome do detalhamento.
- Distribuição SIMÉTRICA (sobra metade de cada lado); quantidade pela mesma regra das faixas.
- Circular: cada família vira um grupo VAR (`DadosArmadura.ComprimentosVar`, XData "CVAR") com tabela de
  comprimentos "L"; ou, na opção "uma posição por barra", um N para cada comprimento (simétricas juntas).
- Saída: planta de locação e formas, planta de armaduras com faixas, coluna "detalhes das barras", corte 1-1
  com indicações, tabelas "L", notas, e tudo num detalhamento (bloco) com a RA parcial ("nome no título").
- Fora do escopo (pedido do usuário): nichos de concretagem, chumbadores, tabela de cargas das estacas.
[VALIDAR com o usuário no uso: posição das faixas, tamanhos de texto, valores padrão das dobras.]

### Pilar pré-moldado PP-1 (`Templates/PilarPreMoldado/TemplatePilarPreMoldado.cs`) — em construção
Requisitos e regras de norma: `docs/PILAR_PREMOLDADO_REQUISITOS.md`. Verdadeira grandeza (cm); a escala de cada
região só dá a altura dos textos/cotas (a folha é montada com viewports, como no DWG do P61). Seção b (x) × h (y);
face 1 = +x, 2 = −y, 3 = −x, 4 = +y; a VISTA n olha a face n (1 e 3 mostram h, 2 e 4 mostram b).
- **Etapa 1 (2026-10-29, feita)**: 4 vistas lado a lado (passo = maior lado + 20 alturas de texto), níveis (topo e base
  sempre + lista "NOME/NAME=cota"), cadeia de cotas entre níveis e "COMPRIMENTO TOTAL DO PILAR: <>", embutimento com
  chaves (linhas a cada passo/dente nas 4 vistas, cota na VISTA 2, avisos da tab. 15 e do mínimo de 40 cm), chanfros,
  marcas do corte A-A, corte A-A com as vistas em volta, notas (cobrimento por face, tolerâncias da tab. 2, insertos) e
  informações p/ produção (volume da forma, peso a 2500 kgf/m³). Idioma PT, EN ou PT/EN.
  Quadro de produção EM TABELA (modelo do usuário, 2026-10-29): título "PT\P(EN)", rótulo à esquerda e valor com
  unidade à direita ("fcj = 28 MPa", "1,209 m³", "3,023 ton.", "1 un."); linha da montagem só com fcj > 0.
  `TabelaTemplate` ganhou larguras/alinhamentos por coluna e quebra de linha na célula (`TabelaTemplate.Quebra`).
  GstarCAD 2021: `HorizontalCellMargin`/`VerticalCellMargin` não têm efeito e a altura mínima da linha vem do estilo;
  o espaço inicial afasta o texto da borda esquerda, o final é descartado [pendente: estilo de tabela próprio].
- **Seções e cortes (plano, 2026-10-29)**: incrementais — gerados onde há o que mostrar: corte de forma no topo
  (insertos/gabarito) e um corte por nível de consolo (B-B, C-C…, hachurado); seções de armadura onde o arranjo das
  longitudinais muda (topo com grampos U, trecho corrente, cada redução/emenda, base) e no consolo; marcas nas vistas
  e na elevação das armaduras; seções iguais não se repetem.
- **Etapa 2 (2026-10-29, feita)** — `ArmadurasPilar.cs` (partial): longitudinais por parâmetros (canto com 1 barra ou 3
  em "L" a 5 cm, intermediárias por face b/h; centro do canto = cobrimento da face + φt + φ/2, o P61 dá 5,55), emendas
  ALTERNADAS (metade das barras de cada grupo começa com peça menor: emendas afastadas ≥ 1,3 l0) ou NA MESMA SEÇÃO, ou
  sem emendas (rolo; aviso se a bitola só existe em barras); traspasse = NBR 6118 com o fck do pilar (50 % ou 100 %
  emendadas) e nunca < 45φ, arredondado a 5 cm, com cota e NOTA PT/EN ao lado ("AJUSTAR SE NECESSÁRIO"); estribos
  fechados (ganchos de 90° no canto superior esquerdo, medidas externas = seção − cobrimentos) por zona (base, corrente,
  topo; um N por bitola), faixas na elevação sem o diâmetro ("71 N1 c/15"); grampos suplementares (uma ou duas
  direções; quadrado = um N com "2x"); grampos U no topo e na base (duas direções; a 2ª recuada pelo canto em "L");
  seções 1-1/2-2/3-3 com as barras em ponto (diâmetro real), estribo, grampos, pernas dos U e UMA INDICAÇÃO POR BARRA
  (textos em ordem dos dois lados, sem cruzar); formas dos estribos/grampos abaixo das seções; RA do detalhamento à
  direita. Avisos da NBR 6118 (18.4.2.1, 18.4.2.2, 18.4.3, 18.2.4). Descrição das barras: `BarraTemplate.PosicaoDescricao`
  e `RotacaoDescricao` (na vertical, ao lado da barra, como no P61).
  Correção no plugin: a INDICAÇÃO não passa mais o espaçamento dela (15) para a barra (`ServicoFaixa.Criar`).
- Próximas: 3 consolos (entram no volume e
  no CG); 4 alças pelo CG, furo/alça de topo, inserto, detalhes padrão; 5 entrada por tabela/Excel/janela.
