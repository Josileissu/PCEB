# Pranchas de formas no Revit (locação, montagem, cortes, escadas, caixa de elevador)

Pedido do usuário (13/11/2026, decisão D25): reproduzir no Revit a apresentação das pranchas de formas que o escritório
hoje faz no CAD e, depois, automatizar com um comando do DEB.Revit. Referências (privadas, fora do Git): um jogo de
pranchas de uma obra pré-moldada real (locação, 3 montagens, cortes) e exemplos de pranchas de escadas e de caixa de
elevador em alvenaria estrutural.

## Princípios (do pedido)
1. **Texto vem de identificador (tag) ou parâmetro**, nunca digitado na vista, sempre que o Revit permitir.
2. **Tabela = tabela do Revit (schedule)** lendo parâmetros dos elementos. Se o parâmetro não existe, o plugin cria.
3. **Elementos especiais têm prancha própria**: caixa de escada, caixa de elevador.
4. Caixa de alvenaria: **sem armadura** por enquanto (entra depois).
5. A apresentação segue **os dados do modelo** (o modelo já evoluiu em relação ao CAD de referência).

## O que cada prancha tem (lido das referências)

### A. Planta de locação (térreo) — 1:50
- Eixos com bolhas e cotas entre eixos (uma linha de cotas em cima, outra à esquerda); norte.
- Pilar: contorno com hachura. Identificador de 6 linhas à direita do pilar:
  `P1` (maior) / `40x40` / `N.T.P.: 12,00 m (907,80)` / `N.T.B.: -0,40 m (895,40)` / `N.B.P.: -1,20 m (894,60)` / `C.T.P.: 13,20 m`
  (topo do pilar, topo do bloco, base do pilar, comprimento total; nível relativo e, entre parênteses, a cota absoluta).
- Estacas/elementos sem definição: identificador `E1` + observação (`Ø A DEFINIR`).
- Escada e caixa de elevador aparecem em meio-tom, com nota.
- **Tabela de cargas na fundação**: Nome | Carga máx (tf) | Carga mín (tf) | Mx +/− (tf.m) | My +/− | Fx +/− (tf) | Fy +/−.
- Legendas fixas: convenção dos esforços, nota sobre os esforços, detalhe da fundação (cálice) 1:25, chave de cisalhamento 1:10.

### B. Planta de montagem (uma por pavimento) — 1:50
- Eixos, cotas, norte, marcas de corte (A-A, B-B, C-C).
- Pilar: `P1` / `40x40`. Viga: uma linha ao longo da peça `V101 20x50 e=-29.5` (marca, seção, elevação do topo em cm).
- Laje: círculo com `L101` / `h=23.5`, seta dupla com o sentido do vão e símbolo de nível `e=-6`.
- **Tabela de lajes**: Nome | Tipo | Altura (cm) | Elevação (cm) | Nível (cm) | Peso próprio | Adicional | Acidental | Localizada (kgf/m²).
- Legendas fixas: seções de vigas utilizadas 1:25, aparelho de apoio, capeamento, seção da laje, cantoneira, pente, ligações.

### C. Cortes — 1:50
- Níveis com nome e cota, identificadores de pilares e vigas cortados/vistos, estrutura futura em outra cor com nota em caixa.

### D. Escadas (uma prancha por grupo de escadas)
- Planta por nível 1:50, cortes 1:50, detalhe do degrau 1:20, vista 3D, planta-chave.
- Identificador do lance (`E1.L1`), numeração dos degraus, níveis dos patamares.
- **Quantitativo**: Escada | Lance | Material | Volume | Peso (+ total).

### E. Caixa de elevador em alvenaria estrutural (sem armadura nesta etapa)
- Elevação de cada parede (PAR1…) 1:25 com a modulação dos blocos e o número das fiadas.
- Plantas das fiadas típicas (1ª, 2ª, a do pavimento, a da cobertura) 1:25; pontos de graute.
- **Lista de materiais**: argamassa, graute e blocos por tipo (inteiro, meio, canaleta, canaleta J, contrafiamento L). Legenda dos blocos.

## Parâmetros (o plugin cria como parâmetros compartilhados de projeto; prefixo `SD_`, nomes não mudam depois)

| Categoria | Parâmetro | Tipo | Origem |
|---|---|---|---|
| Pilar | `SD_Secao` | texto | calculado do tipo (`40x40`) |
| Pilar | `SD_NivelTopo`, `SD_NivelTopoBloco`, `SD_NivelBase` | texto | calculado (`12,00 m (907,80)`) |
| Pilar | `SD_ComprimentoTotal` | texto | calculado (`13,20 m`) |
| Pilar | `SD_Fz_Max`, `SD_Fz_Min` | número (tf) | importado (CSV) ou digitado |
| Pilar | `SD_Mx_Pos`, `SD_Mx_Neg`, `SD_My_Pos`, `SD_My_Neg` | número (tf.m) | importado ou digitado |
| Pilar | `SD_Fx_Pos`, `SD_Fx_Neg`, `SD_Fy_Pos`, `SD_Fy_Neg` | número (tf) | importado ou digitado |
| Pilar, fundação | `SD_Observacao` | texto | digitado (`Ø A DEFINIR`) |
| Viga | `SD_Secao`, `SD_Elevacao` | texto | calculado (`30x50`, `e=-29.5`) |
| Laje (piso) | `SD_Laje_Tipo` | texto | calculado do tipo / digitado |
| Laje (piso) | `SD_Laje_Altura`, `SD_Laje_Elevacao`, `SD_Laje_Nivel` | número (cm) | calculado |
| Laje (piso) | `SD_Laje_PesoProprio`, `SD_Laje_Adicional`, `SD_Laje_Acidental`, `SD_Laje_Localizada` | número (kgf/m²) | calculado (peso próprio, por tabela do tipo) / digitado |
| Escada (lance) | `SD_Escada`, `SD_Lance`, `SD_Volume`, `SD_Peso` | texto / número | calculado da geometria |
| Parede (alvenaria) | `SD_Parede`, `SD_Alv_Inteiro`, `SD_Alv_Meio`, `SD_Alv_Canaleta`, `SD_Alv_CanaletaJ`, `SD_Alv_L`, `SD_Alv_Argamassa`, `SD_Alv_Graute` | texto / número | calculado da modulação |

Os textos calculados são gravados por um comando "Atualizar dados das pranchas" (o identificador do Revit não faz conta);
rodar de novo depois de mexer no modelo. Valores digitados/importados nunca são sobrescritos.

## Identificadores (famílias de anotação)
A API do Revit **não cria rótulos (labels) em família de identificador**. Portanto as famílias `.rfa` são feitas uma vez à mão
(biblioteca do escritório, fora do Git) lendo os parâmetros `SD_` acima, e o plugin só carrega e posiciona. O nome da família
e do tipo de cada papel (pilar-locação, pilar-montagem, viga, laje, fundação, lance de escada, parede) fica no JSON do padrão
de apresentação. Sem a família, o comando avisa e segue com o identificador que houver (não usa texto solto calado).

## Etapas

| # | Etapa | Entrega | Teste |
|---|---|---|---|
| 1 | **Dados**: criar os parâmetros, comando "Atualizar dados das pranchas", importar/exportar cargas por CSV | parâmetros preenchidos | AutoRun no modelo-base de testes |
| 2 | **Tabelas**: cargas na fundação, lajes (schedules com cabeçalhos agrupados, padrão de texto do projeto) | 2 tabelas | AutoRun |
| 3 | **Plantas**: preparar a vista (escala, recorte), cotas de eixos, identificadores de pilar/viga/laje, sentido do vão, símbolo de nível | locação + montagem anotadas | AutoRun + imagem exportada |
| 4 | **Folhas**: montar a prancha (planta + tabela + legendas fixas + carimbo), numeração e título por parâmetro | pranchas A, B | AutoRun |
| 5 | **Cortes**: identificadores e níveis nos cortes existentes; prancha C | prancha C | AutoRun |
| 6 | **Escadas**: marcar escadas/lances, vistas por escada (plantas, cortes, 3D), quantitativo, prancha D | prancha D | modelo com escadas |
| 7 | **Caixa de alvenaria**: modulação (motor no `DEB.Core`), elevações e fiadas, lista de materiais, prancha E (sem armadura) | prancha E | testes do núcleo + AutoRun |
| 8 | **Comando único** "Gerar pranchas de formas" (janela com o que gerar) + padrão JSON | botão na faixa | revisão do usuário |

Etapas 1–4 primeiro (uma entrega revisável); 5–8 depois da revisão do usuário.

## Cuidados
- **Modelo real em nuvem/compartilhado**: desenvolver e testar em cópia local destacada ou no modelo-base; só aplicar no
  modelo real com a ordem do usuário. O comando roda numa transação única por etapa (falhou → desfaz tudo).
- Não apagar nem renomear vistas, folhas e tabelas que já existem; o que o comando cria leva marca própria para poder atualizar.
- Rodar duas vezes não duplica anotações (o comando atualiza o que ele mesmo criou).

## Respostas do usuário (13/11/2026)
1. **Modelo real**: aplicar primeiro numa cópia local destacada; gravar no modelo real só com a ordem dele.
2. **Numeração**: vale a das pranchas de referência (CAD); as folhas que já existem no modelo com esses números são **atualizadas** (título e conteúdo), não duplicadas.
3. **Formato**: carregar a folha **A0** da biblioteca do escritório.
4. **Esforços de fundação**: o usuário passa depois (a tabela nasce vazia; importação por CSV pronta para receber).
5. **Identificadores**: o usuário adapta a biblioteca do escritório; as famílias prontas foram indexadas (índice privado, fora do Git) e o padrão JSON aponta para elas.

## Primeira emissão (pedido de 13/11/2026, tarde)
- Sai primeiro: pranchas de **montagem**, **paginação das lajes**, **cortes**, **elevações** e uma folha de **seções consideradas**.
- **Detalhes típicos** das pranchas de referência: catalogar (títulos, textos, cotas, recortes) para replicar no Revit.
- **Nota de furos**: furos nas lajes alveolares passam pelos alvéolos, com no máximo a largura do alvéolo (nota fixa configurável).
- **Fundações**: opção do padrão "esquemáticas" (padrão; nota na prancha de locação) ou "dimensionadas".
- **Esforços de fundação**: inseridos depois pelo usuário, por outra ferramenta; a tabela nasce vazia.
