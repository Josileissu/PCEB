# Paginar laje alveolar (D24)

Código: `src/LajeAlveolar`.

Botão no painel Pré-moldados (`PaginarLajeAlveolarCommand`, base `ToolCommand`). Motor puro em **`DEB.Core/Lajes`** (`MotorPaginacao` =
port fiel do protótipo; `PaginacaoObra` = planta → sistema do pano (u = vão, v = transversal; `SistemaPano`), numeração, marcas, relatório;
`ConfigPaginacao` = medidas em metros, JSON em `%AppData%\PCEB\DEB.Revit\LajeAlveolar\config_paginacao.json`). Fluxo: pisos selecionados →
`ExtracaoLajeAlveolar` (croqui do piso, SpanDirectionAngle, topo; vigas: linhas horizontais × sólido no meio da viga nas cotas de
`leitura_viga`, RELATIVAS à base do painel; pilares: varredura a 80% da altura do painel) → `ConfigPaginacaoWindow` (família/tipo de quadro
estrutural do projeto, estratégia, medidas, catálogo de fornecedor fora do Git, fase 3, "Restaurar padrão") → `PreviaPaginacaoWindow` (planta
+ relatório + avisos) → uma transação: `InsercaoLajeAlveolar` (painel = quadro estrutural na cota do topo do painel, folgas zeradas, offsets
y/z corrigidos pela caixa medida, ajuste por recorte lateral ou tipo por largura, recortes por `NewOpening(CenterZ)`, faixa maciça = piso
"FAIXA MACIÇA h…") + complementos `IComplementoLajeAlveolar` em subtransação (`Fase3LajeAlveolar`: capa = tipo "CAPA …" com o mesmo topo,
chaves = DirectShape com o perfil medido entre os painéis, tirantes = DirectShape barra + chapas + porcas). Barra que não cabe na parte
estreita da junta: **furo em obra** (padrão, Ø = barra + folga) anotado no comentário do tirante e listado por painel no relatório.
Controle: comentário `LAJE ALVEOLAR <marca> (…, piso <id>)` — o que existe não é refeito; "Completar" mantém a numeração do piso.
Etapa pelo NOME do parâmetro (configurável). Relatório em `logs/laje_alveolar_*.txt`.
**Testado no Revit 2026 (12/11/2026)** numa CÓPIA desanexada de um modelo real (15 pisos, sem as janelas: extração → motor → inserção →
fase 3 chamados por reflexão de uma DLL de teste carregada num AssemblyLoadContext próprio): 89 peças, 1 faixa maciça, 76 chaves
(2,716 m³), 270 tirantes com furo em obra, 15 capas, 121 recortes, 0 interferências; igual ao protótipo (desvio ≤ 0,1 mm). 2ª execução
("Completar") não duplica; apagar peças e rodar de novo refaz só elas. Achados: (1) ruído de 1e-9 nas coordenadas tirava o corpo de panos
em L (limites arredondados como a grade em `ClassificarPano`); (2) recortes de pilar com borda a frações de 0,1 mm não cortavam →
coordenadas da extração arredondadas a 0,1 mm (`M4`, como o protótipo) e nova tentativa com 1 mm a mais; (3) "Completar" reconhece
também o comentário do protótipo (`pano <id>`). Falta testar as janelas WPF com o usuário.
