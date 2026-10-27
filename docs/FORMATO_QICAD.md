# Formato .cad do QiArmaduras / AltoQi Builder (estudo de 2026-10-27)

Objetivo: importar arquivos `.cad` do próprio usuário para as nossas armaduras (interoperabilidade).
Estudo feito SÓ sobre o arquivo de dados (`auxiliar_cad/Desenho 1.cad` + o `.dwg` exportado); nenhum
programa da AltoQi foi analisado. Scripts de estudo: `tools/qicad/` (Python: `esquema_cad.py`,
`objetos_cad.py`, `armaduras_cad.py`; rodar com `python -I script.py <arquivo.cad> ...`).

## Estrutura (little-endian)
1. Cabeçalho (0x00–0x38): `u32 0x27A`, …, `u32 nº de objetos + 1` em 0x0C, marcador `CA AB CA AB` em 0x21.
2. **Esquema autodescritivo** a partir de 0x39: `u32 nº de classes` e, para cada classe:
   `u32 id`, `u32 len` + nome (sem nulo), `u8 abstrata`, `i32 -1`, `u32 nPais` + ids dos pais, e 5 seções
   `u32 n` + n × (`u32 a`, `i32 tipo`, `u32 len` + nome do campo). `a` = classe do objeto referenciado.
3. **Objetos**: registros `CA AB CA AB`, `u32 tamanho`, `u32 verificação`, `u32 id da classe`, `u32 nº do
   objeto`, `u32 0`, depois os campos da hierarquia (da classe raiz à final; em cada classe, seções 0→4).

Tipos dos campos: −1 int32 · −3 double · −4 texto (`u32 len` + UTF-8/latin-1) · −10 bool (1 byte) ·
−17 int32 (componente de cor) · −6/−8/−15/−18 referência (nº do objeto, 0 = nulo) · −7/−9 lista de
referências (`u32 n` + n × nº) · −13 lista de doubles · −14 lista de textos.
Validação: o exemplo tem 3560 objetos, todos lidos sem sobrar bytes.

## Classes que importam (equivalência com o nosso modelo)
| QiArmaduras | Nosso |
|---|---|
| `TCadRebar` (← `TCadPolygon` ← `TCadElement`): `points` (TPolygonPoint x,y,z; `polygonArc` = arco), `segments`, `rebar`, `scale` (TEscala numerador/denominador) | polilinha + `DadosArmadura` |
| `TRebar`: `diameter` (CÓDIGO), `id` (N), `length`, `quantity`, `spacing`, `repetitions`, `continuous`, `stirrup`, `levels` (camadas), `truss` | bitola, N, quantidade, espaçamento, repetições, corrido, estribo, camadas, treliça |
| `TRebarSegment`: `segmentNumber`, `segmentType`, `variable` (VAR), `fixedIndication` | `Segmento` |
| `TRebarText` (`segmentNumber` 0 = descrição; n = cota do segmento n) | descrição/cotas |
| `TReflectedRebar` (`cadRebar`) | rebatida |
| `TRebarBand` (← `TCadDimension`: `pos1`, `pos2`, `finalPos`, `dimensionAngle`): `spacing`, `quantity`, `fixedQuantity`, `percInitialDistance`, `cutCallBand`, `rebar` | faixa |
| `TRebarIndication` (← `TRebarBand`) | indicação |
| `TStirrupCut` (`cutPoints`, `callPoints`, `arc`…) | corte/chamada |
| `TCadRebarList` (`presentation`, `showTotalWeight`, `details`) | tabela da RA (recalculada por nós) |
| `TCadDetail` (← `TCadSimpleDetail`: `detailName`, `repetitions`, volume, forma, classe, `elements`) | detalhamento (bloco) |
| `TCadPolygon` / `TCadLine` / `TCadText` | polilinha / linha / texto comuns |
| `TTruss`, `TCadTruss`, `TTelaSoldada`, `TCadTelaSoldada` | treliça / tela |

## Observações do exemplo
- Coordenadas em unidades de papel: medida real (cm) = coordenada × `scale.denominador` (1:50 no exemplo:
  2,338 × 50 = 117 cm).
- Estribo do QiArmaduras = o nosso: 9 vértices, ganchos no canto superior esquerdo, passagem de 2,5 cm.
  `segmentType`: 0 = reto, 1 = gancho 90°, 2 = passagem do canto, 3 = gancho 45° (dobra de 135°) — conferido
  pelos comprimentos (ponta + pino/2 + φ, as MESMAS fórmulas do plugin). 180° e curva: a descobrir.
- `diameter` é um código (0–7 CA-60, 8–23 CA-50 no exemplo; código 6 = ø5.0). A tabela código→mm não
  está no arquivo: calibrar pelos textos de descrição ("ø5.0") ou por um exemplo com todas as bitolas.

## Importador (implementado em 2026-10-27, v0.1.7)
- Core: `Importacao/LeitorQiCad.cs` (leitor genérico pelo esquema do arquivo) e `Importacao/ImportacaoQiCad.cs`
  (`ConversorQiCad`: objetos → nosso modelo, em cm). CAD: `ComandosImportacao.cs` (ARMIMPORTARCAD).
- Raiz = objeto que herda de `TDrawing` (`TRebarDrawing`); `elements` = soltos + detalhamentos; cada
  `TCadSimpleDetail.elements` = conteúdo do detalhamento. `TCadGroup` comuns são expandidos.
- Filhos (`elementChildren.children[].element`): `TRebarText` (seg 0 = descrição, n = cota), `TDimensionText`,
  `TRebarBandText`, `TRebarLeader`. Não são importados (o plugin gera os seus), só usados para posições.
- Indicação: 1º ponto da `TRebarLeader` encosta na barra, o último fica no texto. A posição gravada do
  `TRebarBandText` às vezes é ANTIGA (111 de 404 no projeto real, deslocadas ~(+375, −375)): só vale perto da chamada.
- Rebatida = cópia deslocada da barra → `ServicoRebatida` (vinculada); girada/espelhada → polilinha comum.
- Repetições do detalhamento (ex.: SAPATA S1 ×41) → multiplicam as repetições das barras.
- N que recomeça por detalhamento → o desenho passa a ter configuração própria com numeração Individual.
- Desempenho: projeto real (11 MB, 244.966 objetos) em ~2,5 s.

## Pendências para o importador
Exemplos do usuário com: ganchos 45°/180°, barra com arco, corte + chamada (TStirrupCut), faixa alinhada
vs ortogonal, todas as bitolas (tabela de códigos), camadas, treliça e tela, detalhamento com repetições.
