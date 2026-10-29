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