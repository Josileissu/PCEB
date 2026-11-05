# Padrões de apresentação e templates de vista (convenção de nomes)

Pedido do usuário (05/11/2026): o detalhamento do pilar pré-moldado no Revit deve ficar **bem próximo** do gerado pelo
template `pilar-premoldado` do DEB.Cad (prancha-modelo PP127). Os templates de vista e as informações de
apresentação ficam guardados **no repositório PCEB** e evoluem junto com o código.

## Onde fica cada coisa
| O quê | Onde | Observação |
|---|---|---|
| Padrão de apresentação (prancha, regiões, vistas, tabelas, quadros, legendas) | `src/DEB.Revit/Padroes/<código>.json` | embutido na DLL; é o padrão de fábrica |
| Cópia editável do usuário | `%AppData%\PCEB\DEB.Revit\Padroes\<código>.json` | criada na 1ª execução; se o padrão de fábrica tiver `Versao` maior, a cópia antiga vira `.vN.bak` |
| Templates de vista | dentro do projeto Revit (`PCEB-…`) | criados pelo plugin se não existirem; o usuário pode editá-los no Revit e as próximas pranchas seguem a edição |
| Carimbo | `S:\Meu Drive\Arquivos\Familias Revit\Carimbo e Folhas\Folhas Revit.rfa` (tipo A1) | fora do Git (padrão do escritório); carregado no projeto se faltar |
| Legendas do escritório (raio de curvatura, emendas, comprimento, aterramento) | legendas do projeto Revit, pelo nome | ainda faltam no modelo de teste |
| Template do CAD (referência) | `%AppData%\PCEB\DEB.Cad\Templates\pilar-premoldado.csv` | mesma informação de peça (escalas, níveis, cobrimentos, zonas de estribos, consolos, alças…) |

## Convenção de nomes
Prefixo `PCEB-`, partes em MAIÚSCULAS separadas por hífen, sem acentos.

### Código do elemento
| Código | Elemento (D19) | Código | Elemento |
|---|---|---|---|
| `PPM` | pilar pré-moldado | `PIL` | pilar moldado in loco |
| `VPM` | viga pré-moldada | `VIG` | viga moldada in loco |
| `BAL` | baldrame | `CON` | consolo de concreto |
| `CMT` | consolo metálico | `LAJ` | laje maciça |
| `ALV` | laje alveolar (paginação/capa) | `PAR` | parede de concreto |
| `TIL` | tilt-up | `RES` | reservatório |
| `BLO` | bloco de fundação | `SAP` | sapata |

### Padrão de apresentação (arquivo JSON e campo `Codigo`)
`PCEB-<ELEMENTO>-<FORMATO>[-<VARIANTE>]` — ex.: `PCEB-PPM-A1`, `PCEB-VPM-A1`, `PCEB-PPM-A1-SEMCONSOLO`.
O campo `Versao` sobe a cada mudança do padrão de fábrica.

### Template de vista
`PCEB-<ELEMENTO>-<VISTA>` — a escala **não** entra no nome (o template não controla escala nem recorte; a prancha ajusta).
| `<VISTA>` | Uso |
|---|---|
| `FACE` | vista de uma face, só concreto, com cotas (VISTA 1…4) |
| `ARMADURA` | elevação da armadura, com rótulos das posições |
| `SECAO` | seção transversal com armadura (SEÇÃO 1-1…) |
| `CORTE` | corte transversal só de forma, com cotas (CORTE A-A…) |
| `3D` | vista 3D da peça |
| `DETALHE` | detalhes (consolo, furo, chave de cisalhamento…) |
Ex.: `PCEB-PPM-FACE`, `PCEB-PPM-SECAO`, `PCEB-CON-DETALHE`.

### Itens gerados por peça (já em uso, não mudar sem motivo)
- Vistas: `<Marca> - <título>` (ex.: `P1 - VISTA 1`, `P1 - SEÇÃO 1-1 - ARMADURA`).
- Tabelas: `<Marca> - RELAÇÃO DO AÇO`, `<Marca> - RESUMO DO AÇO`.
- Folha: número `<Marca>`, nome `ARMADURA DO PILAR / COLUMN REINFORCEMENT - <Marca>`.
- Tipos de barra criados pelo plugin: `Ø<bitola> CA-50` / `Ø<bitola> CA-60` (pinos da NBR 6118).
- Mantidos por compatibilidade com modelos já detalhados: tipo de cota `SD - Cota cm`, parâmetros `SD_*`.

## Prancha do pilar pré-moldado: PP127 × Revit
Regiões do `PCEB-PPM-A1` (frações da área útil, origem embaixo à esquerda), medidas na PP127:
vistas e elevação da armadura à esquerda (1:50); seções com armadura e cortes no alto, ao centro (1:25); relação e
resumo do aço, informações de produção e notas à direita; detalhes padrão embaixo; carimbo no canto inferior direito.

| Item da PP127 / template do CAD | Revit (05/11/2026) |
|---|---|
| Aço CA-50 nas barras ≥ 6,3 | **corrigido** (só usa tipo de barra do aço certo; senão cria `Ø… CA-50`) |
| Carimbo do escritório A1 | **feito**: carrega `Folhas Revit.rfa`; parâmetros do carimbo a mapear |
| Regiões sem sobreposição | **feito**: regiões medidas na PP127 (conferir nos PNG do AutoRun) |
| Templates de vista | **feito**: `PCEB-PPM-FACE/ARMADURA/SECAO/CORTE/3D` |
| Níveis (topo, térreo, piso, base) e comprimento total nas vistas | falta |
| Embutimento = deslocamento negativo da base da família | falta |
| Formas das barras cotadas (estribos, grampos) | parcial (dobramento esquemático) |
| Armaduras do consolo (planta, corte AA/BB, tirante, costura) | falta |
| Alça de içamento (lifting handle) | barra criada; detalhe falta |
| Reforço nos furos, chaves de cisalhamento, aterramento (SPDA) | falta (legendas/detalhes padrão) |
| Legendas padrão (raio, emendas, comprimento) | faltam no projeto de teste |
