# Exportar para o CAD (D22)

**Um levantamento só** (branch `pc2/exportar-pela-conferencia`): `Precast/PrecastExport.Build` = `PieceReview.Run` (o da conferência: seções
exatas por corte, consolos pelo perfil) → `DEB.Core/Intercambio/ConversorLevantamento` → `ArquivoPecas` (JSON "DEB.Pecas", versão 1).
O botão "Exportar para o CAD" do painel e o botão "Exportar para o CAD…" da janela "Conferir peças" gravam o MESMO arquivo
(`ExportPiecesCommand.Save`); a janela exporta o levantamento que está mostrando. Não existe mais a medição pela caixa envolvente
e pelo volume (`PerfilViga`, `AgrupadorVigas`, `PrecastExport.Beams` foram removidos).
- **Marca = nome da peça**: vale a marca que as peças têm no modelo (`MarcasPecas`); só grupo sem marca recebe P1…/V1… (número livre).
  Marcas diferentes no mesmo grupo, ou a mesma marca em peças diferentes = nota. Antes a exportação numerava pela ordem de tamanho.
- **Pilar**: b × h = caixa do pilar sem os consolos, no sistema do representante (faces 1 = +x, 2 = −y, 3 = −x, 4 = +y); o trecho
  corrente tem de ter ≥ 95% da área de b × h (chanfro de canto passa; L, circular etc. = não exportado, com nota); trecho mais fino
  (cabeça reduzida, chaves) = exportado com nota "conferir". Embutimento (parâmetro "Embutimento") entra na assinatura do grupo.
- **Viga**: [dente] + corrente + [dente], com alma retangular e, desde o VP-4 (09/11/2026), UMA aba lateral num trecho contínuo
  (`DEB.Core/Pecas/SecaoComAba`: L, T invertido, aba elevada, aba rente ao topo, fundo inclinado; a aba acima do recorte passa pelo
  dente) → `VigaPeca.Aba`, JSON versão 2 só quando há aba. Trecho curto (≤ 20 cm) de seção reduzida dentro da caixa = furo de
  pino/recorte local, ignorado com nota. I, duas saliências no mesmo lado, mísula em toda a altura ou consolo de outra viga = não
  exportada, com o motivo. Invertida = mesmo grupo (giro 180). Detalhes em `src/DEB.Cad/docs/TEMPLATES.md` (VP-4).
- **Material**: o mesmo critério para pilares e vigas — as de "Concreto pré-moldado"; se o modelo não tem nenhuma do tipo, todas, com nota.
- `uid` (UniqueId) de cada elemento vai no JSON como campo opcional (o DEB.Cad 0.1.32 instalado ignora e continua lendo).
- `PrecastSurvey` lê a geometria de cada candidato a consolo uma vez (antes: de novo para cada pilar) e descarta os distantes pela caixa.
AutoRun `exportarpecas` (`SD_EXPORT` = caminho). Só lê o modelo. `conferir` aceita `SD_CONFERIR_INDICES="16,18"` (PNG de grupos de vigas escolhidos).
O modelo real de teste (168 vigas) é do Revit 2026 (o 2025 para no aviso "versão posterior"); no 2026 o AutoRun roda e, desde 10/11/2026, o Revit de teste fecha sozinho também com arquivo LOCAL de modelo compartilhado (o AutoRun responde "não salvar" e "manter a propriedade" às duas janelas da saída; antes a resposta genérica era recusada e a instância ficava aberta). Revit do usuário aberto = DLL presa: renomeie para `.antigo`, copie e assine.
**Aberturas na alma** (relato do usuário, 10/11/2026: viga com 5 furos aparecia em 13 trechos, com seções de "h = 75" cortadas dentro
dos furos): `PieceReview.Aberturas` lê os laços INTERNOS das faces laterais planas (normal ±y) = furos (nas duas faces = passante, numa
= nicho); as bordas deles saem das estações (`Subdivisao.SemBordasDeAberturas`) e o corte é feito fora deles (`Subdivisao.PontoDeCorte`).
A elevação da janela desenha os furos; a viga fica ✖ ("o CAD ainda não desenha aberturas nem o reforço") e não é exportada. Rebaixo no
topo (viga de 20 com recorte de 10 × 15) = ressalto (mesma forma); "aba" com mais de 3/4 da altura não é aba. AutoRun `furos`: corta dois
furos numa viga do Teste_PM (NewOpening) e desfaz no fim; `conferir` aceita `SD_CONFERIR_MARCA`.
**Escolher o que exportar** (pedido do usuário, 10/11/2026): 1ª coluna da janela = caixa "exportar" por grupo (só desta exportação),
botões Todos / Nenhum / Só ✔ / Seleção do Revit por aba e a contagem embaixo. Com peças selecionadas no Revit ao abrir a janela, ela começa
marcando só os grupos delas. "Nunca exportar" (gravado no modelo) continua valendo e desmarca o grupo. Desmarcados = nota no JSON.
**Edições na janela (etapa 3, 09/11/2026)** — as únicas gravações da conferência no modelo (`Precast/PieceReviewEdits`, comando em
`TransactionMode.Manual`): "Gravar marca no modelo" (parâmetro Marca de todas as peças do grupo; a janela recusa a marca que já é de
outro grupo) e "Não exportar este grupo" (Extensible Storage, schema `DEB_ConferenciaPeca`, campo `NaoExportar`, no próprio elemento:
vale nas próximas conferências; ⊘ na lista). AutoRun `conferireditar`: grava VT1/PT1 e um "não exportar", levanta de novo, exporta e
DESFAZ tudo (TransactionGroup.RollBack) → `logs/conferireditar_*/resultado.txt` + PNG. Testado no Teste_PM. Falta: função dos
trechos (sem uso no CAD até o JSON v2), "Selecionar no modelo" e voltar para "a conferir" quando a geometria muda.

## Apoio e furos (D36, 18/11/2026)
- Consolo: `apoio` = {tipo 1 neoprene/2 argamassa/3 direto, comprimento, largura, espessura, afastamento (a2)} em cm, lido dos
  `PCEB_Apoio*` da viga em que o consolo está aninhado (`PieceReview.Apoio`). No CAD vira as colunas do apoio na tabela "Consolos".
- Viga: `furos` = {quantidade, dist_esquerda, dist_direita, diametro (mm)} dos `PCEB_Furo*` do tipo; no CAD, "Quantidade de furos
  de pino" = Digitada, `FurosE/D` (só nas pontas com dente), `FuroDistE/D` e `FuroDiam`. Família sem esses parâmetros: nada muda.
- Testado em 18/11/2026 (Revit 2025 + GstarCAD): os furos verticais no dente não atrapalham o levantamento da viga.
