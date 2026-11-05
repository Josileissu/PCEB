# TQS

| Fabricante | Licença | Plataforma | Instalado aqui |
|---|---|---|---|
| TQS Informática (São Paulo) | comercial, por módulos; chave **HASP/Sentinel** ✓ | Windows (Win32 nativo, 64 bits) | ✓ `C:\TQSW` (programas, 1,6 GB), `C:\TQS` (projetos) |

Levantamento de 05/11/2026 só pelos arquivos instalados (o programa não foi aberto). Fontes ✓: pastas `SUPORTE`
(critérios de fábrica por sistema), 1.141 páginas de ajuda de critérios em `SUPORTE\NGE\HTMLS`, descrições dos
executáveis, `Programacao_TQS_Python.pdf` (130 p.), ajuda do GerPrE (`gerpre.chm`), `CRITPRE.DAT` e `CRITNEO.DAT`.
Listas completas fora do Git em `programas/tqs/download/` (`tqs_criterios.txt`, `tqs_exes.txt`, `CRITPRE_utf8.txt`).

## Arquitetura do programa ✓
- **Gerenciador** (`TQS.EXE`, "Gerenciador do sistema") que chama **~550 executáveis** separados (um por etapa:
  `FPOR*` pórtico, `FGRE*` grelha, `NEDI*` editores, `RDIM*` dimensionamento, `FVIS*` visualizadores…) e ~400 DLLs.
- **Critérios em arquivos de texto por sistema** (`SUPORTE\<sistema>\*.DAT`): valores de fábrica copiados para o
  projeto e editados no gerenciador (editor de critérios `NCRITEDI`, com uma página de ajuda por critério).
- **Desenho padrão** (`DP`, arquivos `.COR`/`.DP`): níveis, cores, textos e escalas de cada desenho; máscaras de
  prancha em `.DWG`.
- **EAG — Editor de Aplicações Gráficas** (`EAGW.EXE`): editor gráfico próprio, base dos editores de formas,
  armaduras e desenhos; desenhos em `.DWG` (formato/edição do TQS; leitura e conversão pela **ODA Teigha 19.12** ✓).
- **Visualizador 3D** (`NE3D`, `TQSPLAY`, `NV3DUI`); exportação **IFC** (`NE3D2IFC`) e importação (`NIFCImport`);
  **BCF** (`NBCFIO`).
- Interface: Win32 + **Codejock 13.4** e **BCGSoft**; algumas telas novas em **.NET (WinForms/WPF)** — 154 assemblies .NET ✓.

## Sistemas (módulos) ✓
Nº de critérios configuráveis entre parênteses (prefixo da ajuda).

| Sistema | O que faz (pelos critérios, executáveis e arquivos) |
|---|---|
| **Edifício / Formas** (`EDI` 13, `FOR` 97, `FORD` 26) | modelo por pavimentos: pilares, vigas, lajes (maciças, nervuradas, treliçadas convencionais e com minipainéis), cargas, combinações (inclusive ACI 318-19 ✓), vento (V0, Ca, nº de casos), viga de transição, verificação de cobrimento e fck mínimos; níveis de desenho das formas |
| **Pórtico espacial** (`POR` 29) | não linearidade física por coeficientes (vigas, pilares, lajes), P-Δ, deslocamentos máximos no topo e entre pisos, correção de torsores, transferência de esforços das grelhas |
| **Grelha** (`GRE` 40, `LJP` 29, `LJN` 19) | grelha de pavimento; lajes planas (`LJP`) e nervuradas (`LJN`) por analogia de grelha: capitéis, plastificação, molas, Wood & Armer, seção T |
| **Lajes** (`LAJ` 179) | dimensionamento e armação de lajes: ancoragem, decalagem, caranguejos, distribuição, cisalhamento (modelos, α, θ), fissuração (`FCALFISS`), vibração/ELS de pavimento (`FELSPAV`) |
| **Vigas** (`VIG` 237 — o maior) | dimensionamento e detalhamento: alojamento e agrupamento de barras por tabelas, decalagem, ancoragem, armadura lateral, dentes Gerber (`HMINDENTE`), **furos em vigas** (`VFUROS`) |
| **Pilares** (`PIL` 160) | 2ª ordem local (1/r, κ), comprimentos equivalentes, armadura mín./máx., grampos (inclusive em S), **curvas N-M** (`PMCURV`), arranques (`PARRANQUE`), bloco de transição (`CBLTRPIL`) |
| **Fundações** (`BLO` 137, `SAP` 51, SISE) | **blocos** de 1 a 24 estacas com bielas e tirantes (exemplos por arranjo), **sapatas** (tombamento, deslizamento, área comprimida), **radier estaqueado** (`SRADIERESTUI`), **interação solo-estrutura** (SISE, `SCALCMOLA`) |
| **Escadas** (`ESC` 54) | processo simplificado (`ESCS`), tabelas de alojamento |
| **Elementos especiais** (`ESPECIAIS`) | elementos fora dos padrões (vigas/pilares especiais, transições) |
| **Alvenaria estrutural** (`ALVEST`; `ALVD/ALVAE/ALVAC/ALVLD/ALVPM/ALVEG/ALVUC` 59) | paredes, efeito arco, propagação de cargas, graute, transferência de vento e γz |
| **Paredes de concreto** (`PrConcra`, `CRIT_PRCA`) | provável sistema de paredes de concreto moldadas (portas, janelas, legendas próprias) — a confirmar |
| **Madeira** (`MADEIRA`) | painéis, garfos, montantes — provável sistema de **fôrmas de madeira** — a confirmar |
| **Pré-moldados** (`PREFAB`, `CRITPRE` 776 linhas, `CRITNEO`) | ver seção própria abaixo |
| **Metálicas** (`METALCHECKUI`, `MIX-Metalicas.pdf`) | verificação de aço (estruturas mistas/metálicas), ligações (`NSCONNECT`) — a confirmar alcance |
| **Ferramentas** | orçamento (`NORCA`, `TQSORCA`), relatórios (`NREPORTUI`), comparação de modelos, plotagem (`TQSVIEW`), memória de cálculo |
| **Normas** (`NORMAS`) | NBR 6118:2003 e 2014, NB-1/78, **ACI 318-05 e 318-19**, **CIRSOC** (Argentina); versões para AR, BO, CO e PY (`SUPORTE-AR/BO/CO/PY`) |

## Pré-moldados (PREFAB) ✓
- **Critérios** (`CRITPRE.DAT`): γc/γs, **folga de vigas (2 cm)**, altura de solidarização, aparelho de apoio, extensão
  de apoio sobre consolo, **alças de içamento e furos de levantamento** (posição relativa e absoluta), comprimento
  máximo (12 m) e **peso máximo (24 tf)** da peça, saída de água pluvial, **escalas** (pilares 1:50 longitudinal e
  1:25 transversal — as mesmas da PP127), feixes de barras, **emenda por solda** (bitola mínima 20 mm), alojamento de
  bitolas, verificações de **saque, transporte e levantamento**, **NBR 9062** (1985 a 2014), **consolos** (γn, força
  horizontal mínima, interface monolítica/rugosa/lisa, taxa mínima, tirante soldado/alça horizontal/vertical, bitolas
  mínimas, pinos), espaçamento de estribos (geral, região do consolo, região da fundação), **protensão** (cordoalhas e
  fios CP-190 RB com área, carga e tensões; perdas e pré-dimensionamento por combinação).
- **Neoprene** (`CRITNEO.DAT`): critérios NEOPREX, **Mounir 2017** e **NBR 9062:2017**; elastômero simples e fretado,
  limites de tensão por dimensão, distorção, rotação, formatos padrão.
- **Calculadoras**: consolos (`RCCONS` "Calculadora de consolos", `REDTCONS`), neoprene (`RNEOPRENCALC`),
  alças (`RALCACALC`), dimensionamento de vigas (`RDIMVIG2CALC`).
- **Máscaras de desenho**: pilar, viga, laje, alças (3 tipos), consolo, superfície rugosa, solda (`MASPRE*.DWG`, `PREO_*.DWG`).
- **GerPrE** (gerenciamento da produção, `gerpre.chm`): cadastro de filiais, clientes, obras, desenhos e
  localizações; importação dos desenhos e materiais do TQS para uma área gerenciada; consistência das posições;
  **lotes de exportação** para a fábrica; banco de dados em servidor; plugin TQSREF.

## Modelo de armadura: "ferros inteligentes" ✓ (cap. 5 do manual Python)
Referência direta para o **modelo de armadura neutro** do nosso motor (D19):
- **Tipos**: ferro reto (trecho principal + até 2 dobras de cada lado, por faces externas); **ferro genérico**
  (poligonal com trechos retos e arcos, pelo eixo); **estribo de viga** (por um canto da seção + cobrimento; normal,
  fechado para torção ou aberto; 2, 4 ou 6 ramos); **estribo genérico de pilar** (pelos centros das barras
  longitudinais abraçadas; fechado, aberto, grampo, circular); grampo de viga; **faixa múltipla**.
- **Atributos**: posição (N), quantidade, **multiplicador** (ex.: 3 nervuras × 2 barras), bitola, espaçamento,
  ganchos 90/135/180° em cada ponta, positivo/negativo, cobrimento, lance do pilar, comentário.
- **Raio de dobra e ganchos** são atributos; o comprimento total (com descontos) é calculado pela biblioteca conforme
  critérios (`DESARM.DAT`).
- **Faixas de distribuição** associadas ao ferro (mudar a faixa muda a quantidade); várias faixas por ferro;
  **faixa de comprimento variável** (gera a tabela de ferros variáveis); **faixa de múltiplas posições** (pilares:
  uma faixa para estribos e grampos da mesma seção).
- **Identificação de posição** nos cortes, renumerada junto; várias linhas de desenho para o mesmo ferro.
- Texto de descrição padrão: `3x2 N2 Ø6.3 C/20 C=362`.

## API / automação ✓
**Python oficial** (`TQSPythonInterface-1.2.7`, wheel para Python 3.10), módulos:
`TQSUtil` · `TQSGeo` · **`TQSDwg`** (desenhar, cotar, ler e editar desenhos, níveis, blocos, xrefs, plotagem;
**`SmartRebar`** e `GlobalRebar` para ferros) · `TQSJan` (janelas, wxPython) · **`TQSEag`** (extensões dentro do
editor gráfico, com menus próprios `.PYMEN`) · **`TQSBuild`** (ler e gravar o edifício: projeto, modelo, pavimentos,
materiais, cobrimentos, cargas, sincronização com o GerPrE) · `TQSGrid` (planilhas) · **`TQSExec`** (rodar as
etapas de processamento em lote). Exemplos `TST*.PY` e `EAGFOR.PY` ✓.

## Importa / exporta ✓
DWG/DXF (ODA), IFC (exporta e importa), BCF, PLT, CSV/planilhas (TQSGrid), lotes de exportação do GerPrE.

## Projetos de exemplo ✓
`C:\TQS`: `MODPREO` (pré-moldado), `Mod_PrCA_01` (paredes de concreto?), `CPQ04 - Blocos`, `TESTEALV` (alvenaria),
`ModTela`, `Mod-Padrão`.

## O que aproveitar para o nosso programa
1. **Critérios por sistema em arquivos de texto**, com padrão de fábrica e cópia por projeto, e **uma página de ajuda
   por critério** — confirma o desenho dos nossos perfis JSON (D19); a ajuda por critério é um bom padrão a copiar.
2. **Modelo de ferro inteligente** (tipos, faixas, multiplicador, ganchos e raio como atributos, comprimento calculado
   pela biblioteca) — usar como checklist do modelo de armadura neutro do `DEB.Core`.
3. **Lista de critérios do PREFAB** como checklist do perfil "pilar/viga pré-moldada": içamento, saque, transporte,
   peso e comprimento máximos, consolos, neoprene, solda, protensão.
4. **Etapas como processos separados + execução em lote** (`TQSExec`) — modelo para rodar o PCEB em fila (nuvem).
5. **GerPrE**: a ponte projeto → fábrica (obras, desenhos, lotes) é parte do escopo "fabricação" do PCEB.
6. Com a API Python dá para **ler o edifício e os ferros** de um projeto TQS e comparar com o nosso detalhamento.

## A confirmar
- Versão instalada (`VERSAO.DAT` é binário) e **módulos licenciados** no escritório.
- Abrir o `MODPREO` no TQS e ver as pranchas de pré-moldado geradas (comparar com a PP127).
- Alcance de Paredes de concreto (`PrConcra`), Madeira (fôrmas?) e Metálicas.
- Exportação CAM / máquinas de corte e dobra.
