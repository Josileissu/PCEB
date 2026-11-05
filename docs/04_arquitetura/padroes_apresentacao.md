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

## Referência do escritório: pilar padrão do template do CAD
Material do usuário (05/11/2026) em `src/DEB.Cad/auxiliar_cad/pilar_padrão/` (fora do Git): DWGs 316-CON-AB-100-6098 e
316-CON-DD-100-6113, e imagens (organização da folha e do modelo, P56/P61, seções, consolos simples/duplo, chaves de
cisalhamento, reforço do furo, cordoalha de içamento, raio/emenda/comprimento, informação para produção).

O que o padrão tem e o Revit ainda não faz (ordem de impacto visual):
1. **Títulos acima das vistas**: "VISTA 1 / (VIEW 1) / Esc. 1:50" centrados em cima de cada vista (não a etiqueta do Revit
   embaixo), e o nome da peça (P61) grande à esquerda; aviso dos insertos sublinhado embaixo dos títulos.
2. **Linhas de nível** traço-ponto atravessando todas as vistas, com ▽, cota (+11,005 / 3,025 / 0,00 / −2,90) e nome
   bilíngue à esquerda (TOPO PILAR, WAREHOUSE, TÉRREO, BASE PILAR); "COMPRIMENTO TOTAL DO PILAR: 1390,5" vertical.
3. **Elevação da armadura esquemática** ("explodida"): cada posição longitudinal como uma linha separada, lado a lado,
   com rótulo vertical "4 N17 ø25.0 C=1200" e cotas das emendas; grampos U (topo/base) desenhados como forma cotada
   (31/88/87); estribos numa linha de cota à direita com as zonas "2x10 N2 c/10 + 20 N4 c/5", "71 N1 c/15", "26 N4 c/10";
   marcas das seções 1, 2, 3 nas alturas.
4. **Seções com chamadas N** de cada barra (N7, N9, N11, N12… com linhas de chamada) e, embaixo das seções, as formas
   dos estribos e grampos ("46 N4 ø8.0 C=184", "2x40 N2 ø6.3 c/10 C=62").
5. **Quadro "ARMADURAS console LxHxC – n un." (1:25)**: PLANTA, CORTE AA e CORTE BB cotados, com chamadas N, e as formas
   de **todas** as barras do consolo: tirante (com barra de ancoragem soldada), estribos verticais, estribos horizontais,
   armadura inferior, pinos ø20 (e "Solda"). Versão "consoles … – duplo" para consolos opostos. **O modelo de armadura
   do Revit hoje só tem tirante e costura** — falta ampliar o `ColumnRebarLayout` (o template do DEB.Cad já tem).
6. Detalhes padrão: chaves de cisalhamento na base (DETALHE 01), reforço nos furos (com relação/resumo do aço próprios),
   alça de içamento (cordoalha), aterramento/SPDA (avisos ATENÇÃO bilíngues), insertos (25CB660) no topo.
7. Cotas acumuladas à direita das vistas (consolos, furos, alças) e o furo de montagem ø100 cotado.

## Prancha do pilar pré-moldado: PP127 × Revit
Regiões do `PCEB-PPM-A1` (frações da área útil, origem embaixo à esquerda), medidas na PP127:
vistas e elevação da armadura à esquerda (1:50); seções com armadura e cortes no alto, ao centro (1:25); relação e
resumo do aço, informações de produção e notas à direita; detalhes padrão embaixo; carimbo no canto inferior direito.

| Item da PP127 / template do CAD | Revit (05/11/2026) |
|---|---|
| Aço CA-50 nas barras ≥ 6,3 | **corrigido** (só usa tipo de barra do aço certo; senão cria `Ø… CA-50`) |
| Carimbo do escritório A1 | **feito**: carrega `Folhas Revit.rfa`; o título da peça vai no **Nome da folha** (decisão do usuário, 05/11/2026) |
| Regiões sem sobreposição | **feito**: regiões medidas na PP127 (conferir nos PNG do AutoRun) |
| Templates de vista | **feito**: `PCEB-PPM-FACE/ARMADURA/SECAO/CORTE/3D` |
| Títulos acima das vistas (negrito sublinhado + EN + "Esc. 1:n") | **feito** (`TituloAcima`; viewport "PCEB - Sem título") |
| Níveis (topo, níveis, base) e comprimento total | **feito**: nomes e cotas na VISTA 1; linha traço-ponto ("Linha de centro") em todas as vistas de face e na armadura |
| Embutimento = deslocamento negativo da base da família | **feito** |
| Elevação da armadura esquemática ("explodida") | **feito** (`"Esquematica": true` na vista Armadura): uma linha grossa por posição longitudinal com rótulo vertical; linha de cota dos trechos de estribos (vertical se cabe, senão horizontal); concreto oculto e armadura 3D em branco na vista (as formas precisam da barra visível). Falta: grampos U desenhados na altura, cotas das emendas |
| Formas das barras cotadas (estribos, grampos) | parcial (RebarBendingDetail ao lado da elevação esquemática, cotas em cm) |
| Armaduras do consolo (planta, corte AA/BB, tirante, costura) | falta |
| Alça de içamento (lifting handle) | barra criada; detalhe falta |
| Reforço nos furos, chaves de cisalhamento, aterramento (SPDA) | falta (legendas/detalhes padrão) |
| Legendas padrão (raio, emendas, comprimento, aterramento) | **o usuário vai criar** as legendas do escritório (05/11/2026); o plugin já as coloca pelo nome quando existirem |
