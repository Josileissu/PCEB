# Pranchas de formas (D25)

Código: `src/Formas`; plano: `docs/04_arquitetura/pranchas_de_formas.md`.

Painel "Formas": Atualizar dados, Cargas (CSV), Planta de locação, Planta de montagem (base `ToolCommand`; fluxo em `ExecucaoFormas`: uma
transação por etapa — dados → tabela → vista → anotações → folha —, a que falha é desfeita e vai para `logs/formas_*.txt`).
- **Parâmetros** (`ParametrosFormas`): compartilhados de instância, GUIDs FIXOS `8e2a5c10-4b7d-4e61-9f3a-0a1b2c3dNNNN` (nunca mudar),
  criados por arquivo temporário (o do usuário é restaurado). `SD_Ordem` (inteiro) = posição da marca na ordem natural (núcleo:
  `Numeracao/ComparadorNatural`): as tabelas ordenam e filtram (&gt; 0) por ele, escondido. Textos (`DEB.Core/Formas/TextosFormas`) e CSV
  das cargas (`CsvCargas`) no núcleo, com testes (`FormasTests`).
- **Marca do comando**: Extensible Storage `DEB_Formas` (`MarcaFormas`, campo Papel: `vista-locacao:<nível>`, `tabela-cargas`,
  `id-PilarMontagem`, `cota-eixos`, `folha-…`). Rodar de novo: refaz as cotas, mantém/atualiza os identificadores, sem duplicar.
  Folha com o número pedido que já existe = adotada e atualizada (decisão do usuário, 13/11): só acrescenta o que é do comando.
- **Configuração**: seção `Formas` do padrão `PCEB-FORMAS-A0` (`%AppData%\PCEB\DEB.Revit\Padroes`; no Git só valores genéricos/vazios;
  famílias do escritório e caminhos só na cópia local). Filtros de pilar/viga (material, prefixo da marca, família): sem nenhuma peça do
  material pedido, o critério de material é ignorado com aviso.
- AutoRun: `formasdados`, `formaslocacao`, `formasmontagem` (rodam duas vezes; PNG da vista e da folha; `SD_NIVEL`, `SD_FORMAS_MARCAR=1`)
  e `formassonda` (`SD_FAMILIAS_PASTA`, `SD_SONDA_PULAR`): qual parâmetro cada rótulo das famílias de identificador lê; grava linha a
  linha (uma sondagem igual já derrubou o Revit — só em instância de teste: ler `TagText` do tipo "NV" de uma família de pilar do
  escritório causa queda nativa; a sondagem pula os tipos com " NV "). `APPDATA` trocado NÃO isola a instância de teste (o Revit usa
  a pasta conhecida do Windows): o teste usa a DLL instalada.
- Depois (13/11, pedido do usuário): paginação das lajes alveolares, cortes do comando + 4 elevações (fachada: 3 m de estrutura),
  "Seções consideradas" (tabelas por tipo), fundações esquemáticas, notas fixas em bloco, detalhes típicos = legendas criadas de DXF
  (`LegendFactory`, etapa própria com resolução de falhas). AutoRun: `formaslevantar` (inventário), `formaspaginacao`, `formascortes`,
  `formassecoes`, `formastudo`; `SD_MODELO` abre o arquivo pela API destacado do central; `SD_FORMAS_MARCAR_LAJES=1` usa o
  renumerar lajes. **Testado no Revit 2026 numa cópia destacada de um modelo real** (todas as folhas saem; ver os relatórios).
- **Anotar corte** (14/11, `Formas/CorteAnotacao.cs`, botão "Anotar corte"): cotas verticais, identificadores e cotas de nível no
  corte ativo ou nos escolhidos, tipo automático (escada se corta lances, caixa de elevador se corta paredes, senão geral) ou
  escolhido na janela. Só o cortado ou visível (amostragem da caixa projetada contra as peças mais à frente; fase posterior à da
  vista fora). Peças pela caixa do recorte (`BoundingBoxIntersectsFilter`); plano de corte = `CropBox.Max.Z`; faces de família pela
  geometria do símbolo + referência estável da instância; cota de nível em ponto dentro da face. Marcas `corte-cota`, `corte-nivel`,
  `id-corte-*`; uma transação por corte. Configuração `Formas.AnotarCorte` (tipos de cota, famílias, folgas). AutoRun
  `formascorteanotar` cria os cortes de teste A-A…G-G na cópia, anota duas vezes e lista os valores CRUS das cotas (cm, uma casa),
  as cotas de nível (m do térreo) e um diagnóstico das peças cortadas, lances e portas (`CorteAnotacao.Diagnostico`).
  Regras (14/11): fase da lista `FasesIgnorar` ("FUTURA") fica de fora; caixa: cadeia do pilar como a da fachada, base/topo da porta
  pela soleira/altura da instância (sem referência "Bottom" = face de baixo da geometria), só níveis dentro da caixa, cadeia por piso
  com as faces EXTERNAS das paredes; escada: apoio = viga cortada na ponta do lance, cadeia por lado térreo → topo de cada viga da
  faixa, início do lance = patamar (face de cima mais comprida da metade de baixo, não abaixo do topo do apoio), espelhos/pisos a
  partir da ponta do patamar, vão/espelhos/pisos só nos lances cortados. Sem os tipos do escritório: "PCEB - Cota corte (cm)" (0,1)
  e "PCEB - Cota de nível (m)" (+0,00).
