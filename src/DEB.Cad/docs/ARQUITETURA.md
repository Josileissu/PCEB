# Armadura — plugin de detalhamento de armaduras (GstarCAD / AutoCAD)

Especificação funcional: `info_auxiliar/prompt_port_editor_armaduras_gstarcad_autocad.txt`.
Plataforma principal: **GstarCAD 2021** (.NET Framework 4.8, API `GrxCAD.*`). Depois: AutoCAD.

> O plugin **não dimensiona** estruturas: só desenha, numera e quantifica. As decisões de
> cálculo são do engenheiro responsável.

## 1. Arquitetura

```
armadura/
  docs/                 este documento, manual do usuário
  dados/                tabelas normativas em JSON (bitolas, dobramentos) — editáveis, com fontes
  src/
    DEB.Core/      netstandard2.0 — SEM referência a CAD. Modelo, normas, comprimentos,
                        descrição, numeração, relação do aço, codificação dos dados (XData).
    DEB.Cad/       código-fonte compartilhado da camada CAD (sem .csproj próprio).
                        Usa só nomes neutros (Polyline, Editor...); o namespace vem dos
                        "global using" de cada projeto abaixo.
    DEB.GstarCAD/  net48 x64 — referências gmap.dll / gmdb.dll; GlobalUsings → GrxCAD.*
    (DEB.AutoCAD/) futuro — mesmos fontes de DEB.Cad; GlobalUsings → Autodesk.AutoCAD.*
  tests/
    DEB.Core.Tests/  xUnit (net8) — testa o núcleo sem CAD
```

A API .NET do GstarCAD espelha a do AutoCAD (guia de migração do SDK: basta trocar
`Autodesk.AutoCAD` por `GrxCAD`). Por isso a camada CAD é escrita uma vez e compilada duas vezes;
diferenças pontuais ficam em `#if GSTARCAD` / `#if AUTOCAD`.

### Fluxo de eventos (comportamento "de entidade")

```
 comando do usuário (MOVE, STRETCH, grips, ERASE, COPY, PEDIT…)
        │  Database.ObjectModified / ObjectErased / ObjectAppended
        ▼
 Sincronizador: só ENFILEIRA os ObjectId (nunca modifica dentro do callback)
        │  Document.CommandEnded  (ignorado em U/UNDO/REDO)
        ▼
 processa a fila numa transação: recalcula comprimentos, regenera cotas e descrição,
 apaga as partes órfãs, trata cópias (GUID novo) — tudo idempotente
```

Overrules:
- `DrawableOverrule` (filtro por XData `ARMADURA_BR`): esconde a barra com Visível = Não;
  depois desenhará curvaturas e destaques.
- Previstos: `GripOverrule` (faixas), `TransformOverrule`, `ObjectOverrule` (DeepClone).

## 2. Modelo de dados no DWG

Tudo em **entidades nativas** + XData da RegApp **`ARMADURA_BR`** (o desenho abre normal sem o
plugin; XData é copiada junto em COPY/MIRROR/WBLOCK/INSERT). A decisão foi XData em vez de
XRecord porque o XData acompanha a entidade em todas as operações e serve de filtro dos Overrules.
O limite de 16 KB por entidade está longe de ser atingido.

| Parte | Entidade | Dados (chave → valor, em pares 1000/valor) |
|---|---|---|
| Barra | `LWPOLYLINE` (bulge = arco) | `TIPO=BARRA`, versão do esquema, GUID, N, bitola (mm), aço, quantidade, repetições, espaçamento (cm), estribo, corrido, visível, campos da descrição, deslocamento da descrição, handle da descrição, por segmento: formato, cotar, VAR, comprimento registrado (cm), handle da cota |
| Descrição | `MTEXT` | `TIPO=DESC`, handle da barra |
| Cota do segmento | `TEXT` | `TIPO=COTA`, handle da barra, índice do segmento |
| Relação do aço | `TABLE` | `TIPO=RA`, abrangência |

Os vínculos são **bidirecionais e validados**: um texto só é parte de uma barra se a barra
apontar de volta para ele. Isso resolve cópia, colagem entre desenhos e WBLOCK sem IdMapping.

Unidades: o núcleo trabalha em **cm** (comprimentos) e **mm** (bitolas). A camada CAD converte
pela unidade do desenho (`INSUNITS`; se indefinida, a configuração — padrão cm).

## 3. Comandos (Fase 1)

| Comando | Fluxo |
|---|---|
| `ARMBARRA` | Bitola/quantidade/espaçamento correntes → "Primeiro ponto:" → "Próximo ponto [Arco/Desfazer/Config]:" (linha elástica) → Enter termina |
| `ARMCONVERTER` | Seleciona polilinhas → viram armaduras com os valores correntes; fechadas podem virar estribo com ganchos |
| `ARMRECONHECER` | Barras de outros programas/desenho à mão, em QUALQUER layer: polilinhas e LINEs + textos ("25 N1 ø10 c/15 C=300", `Core/Reconhecimento/TextoArmadura`). Cada texto vai para a curva mais próxima (até 8 alturas; C= que confere pesa a favor), um para um; a descrição nasce no lugar do texto, a escala sai da altura do texto, medidas "35"/"42" viram as cotas no mesmo lugar (trechos sem medida ficam sem cota). Fechadas: pergunta estribo. Textos originais: Apagar/Manter. Sem texto → ficam selecionadas |
| `ARMTEXTOPADRAO` | Descrição e cotas das armaduras selecionadas voltam à posição padrão (desfaz as cotas movidas) |
| `ARMOFFSET` | Poligonal (aberta ou fechada) → armadura paralela, afastada (ex.: cobrimento) do lado clicado; fechada pode virar estribo |
| `ARMCOMANDOS` | Liga/desliga a paleta "DEB - Comandos" (botões; o GstarCAD 2021 não tem ribbon em .NET) |
| `ARMIMPORTARCAD` | Importa um .cad do QiArmaduras (AltoQi Builder): barras, estribos, faixas, indicações, rebatidas, detalhamentos (blocos), relações do aço e o desenho de forma (`docs/FORMATO_QICAD.md`) |
| `ARMTEMPLATE` | Templates de elementos (bases, blocos, pilares, vigas, lajes) — ambiente pronto, templates a implementar (`docs/TEMPLATES.md`) |
| `ARMPALETA` | Liga/desliga a paleta "Armadura" (PropertyGrid ligado à seleção; vários objetos = valores diferentes em branco; segmentos só com 1 barra). Abre sozinha ao carregar, se estava aberta |
| `ARMPALETARAPIDA` | Propriedades rápidas (como o QQ/Quick Properties): janela pequena ao lado do cursor para a seleção (pickfirst ou pede), mesmo PropertyGrid da paleta; só o 1º grupo aberto (os outros lembram o estado). Some com Esc, no X ou quando a seleção fica vazia. O atalho fica a critério do usuário (alias) |
| `ARMPROP` | Seleciona barra (ou descrição/cota) → edita bitola, aço, quantidade, espaçamento, repetições, estribo, corrido, visível |
| `ARMEDITSEG` | Clica no segmento → comprimento, formato (Linha/Gancho 45/90/180), VAR, cotar |
| `ARMRAATUALIZAR` | Renumera (RA global) e atualiza descrições e tabelas |
| `ARMRAINSERIR` | Ponto (canto superior esquerdo) ou [Apresentacao/Resumo/PesoTotal] → tabela; opções guardadas na tabela e editáveis pela paleta |
| `ARMFAIXAORT` / `ARMFAIXAALI` | (barra pré-selecionada ou pede) → ponto inicial → final → posição da cota → espaçamento; em laço. Cota nativa com "27 N3 ø5.0 c/15" |
| `ARMCORTECOTAORT` / `ARMCORTECOTAALI` | Como a faixa + lado do corte: círculos (discos) das barras em corte |
| `ARMCORTECHAMADA` / `ARMCORTECHAMADAARCO` | Linha base (ou arco/círculo + lado) → espaçamento → início/centro/fim → texto da chamada |
| `ARMCHAMADA` / `ARMINDICACAO` | Pontos de referência → texto (→ quantidade). Indicação não soma |
| `ARMESTRIBO` / `ARMESTRIBOREP` | Diálogo (formato, ramos, gancho, bitola, dimensões, seção + cobrimento) → seção → rebatido → faixas |
| `ARMREBATER` | Barra → ponto base → destino: a barra vai, fica a rebatida vinculada |
| `ARMRECOTA` | Clica no segmento → a cota volta (em laço) |
| `ARMBARRA` → `Cobrimento` | Clica nas linhas da forma; vértices nas interseções das linhas deslocadas; `Ponto` projeta um ponto livre |
| `ARMDETCRIAR` | Seleção → nome → grupo → pavimento: vira um BLOCO (detalhamento) no mesmo lugar; vínculos preservados |
| `ARMDETGERENCIAR` | Grade com todos os detalhamentos (nome, grupo, pavimento, repetições = inserções, volume, forma, classe, sistema, comentários; F5 copia para a coluna) |
| `ARMRAPARCIAL` | Blocos (Enter = desenho inteiro) → ponto: relação só com as barras desses blocos (× inserções) |
| `ARMRAASSOCIAR` | Blocos → tabela: os blocos passam a pertencer só a essa relação parcial |
| `ARMRABLOQUEAR` | Liga/desliga o bloqueio: números fixos; novas/alteradas acima do maior com "*" |
| `ARMDIVIDIR` | Barra → mostra traspasse e trechos válidos → ponto (centro do traspasse) ou Enter (automática pelo comprimento comercial); opções Concreto/Aderencia/Emendadas/Modo |
| `ARMTRELICA` | [Modelo/Quantidade/Espacamento] → 2 pontos do banzo inferior; banzo superior e sinusoide gerados |
| `ARMTELA` | [Modelo] → 2 cantos do painel; entra no resumo por massa (área × kg/m²) |
| `ARMCORTEDOBRA` | Atualiza a RA e grava `<desenho>_corte_dobra.csv` (Excel, ";") e `.html` (desenho de cada posição) ao lado do DWG |
| `ARMRAINSERIR` → `Dobra` | Liga a coluna "DETALHE DE DOBRA (DIMENSÕES EXTERNAS EM cm)" no fim da relação (também na paleta da tabela) |
| `ARMCONFIG` | Menu de configurações (janela agrupada; salvar, gravar/remover do desenho, editar JSON) |
| `-ARMCONFIG` | Versão de linha de comando: [Abrir/Recarregar/GravarNoDesenho/EditarDoDesenho/RemoverDoDesenho] |
| `ARMSOBRE` | Versão e aviso de responsabilidade |

## 4. Fases

1. **MVP (atual)**: tabela de bitolas, regras de dobramento configuráveis, ARMBARRA (retas e
   arcos), cotas por segmento, descrição vinculada, ARMPROP, ARMEDITSEG (comprimento, ganchos,
   VAR), RA global + resumo (TABLE), atualizar RA, persistência no DWG.
   Paleta de propriedades, atualização automática da RA antes de plotar/exportar/salvar
   (lista `ComandosQueAtualizamRA`), configuração gravada no DWG (Xrecord `ARMADURA_BR_CONFIG`
   no NOD; ARMCONFIG → GravarNoDesenho/EditarDoDesenho/RemoverDoDesenho) e tipos de apresentação
   da tabela (Tabela e resumo | Somente tabela | Somente resumo; resumo Peso | Barras | Barras e peso;
   padrão = só peso) — FASE 1 CONCLUÍDA.
2. Estribos, rebater, faixas, armadura em corte, ponto no cobrimento, reinicializar cota — CONCLUÍDA.
   Desenho sempre em cm; escala por barra/tabela (base 1:10; texto = 0,2 × escala).
3. Detalhamentos, RA individual/sequencial/parcial, bloquear RA, camadas — CONCLUÍDA.
   Detalhamento = BLOCO do CAD (dados no XData da definição; repetições = nº de inserções, também
   aninhadas). RA parcial = barras dos blocos listados na tabela, ou do bloco em que a tabela está.
   Vínculos no XData como 1005: o CAD os traduz em BLOCK/COPY/WBLOCK/INSERT.
4. Traspasse (dividir barra), treliças, telas soldadas, cordoalhas CP-190 — CONCLUÍDA.
5. RA multi-arquivo, relatórios DOCX/HTML/XLSX/CSV, corte e dobra, reconhecimento de DWG.
6. Port para AutoCAD (projeto `DEB.AutoCAD`, mesmos fontes).

## 5. Testes

- **Núcleo** (`dotnet test`): ganchos por bitola/aço, comprimentos, arredondamento, descrição nos
  3 formatos, igualdade de barras (inversão, espelho), numeração global, RA e resumo, codificação
  dos dados (ida e volta).
- **Dentro do GstarCAD**: comando `ARMTESTE` (script automático) cria → edita → copia → apaga →
  atualiza RA e grava um relatório em `%AppData%\PCEB\DEB.Cad\logs`.
- **Aceitação**: critérios da seção 11 da especificação, conferidos pelo engenheiro.

## Itens a validar pelo engenheiro  [VALIDAR NBR]

- Comprimento do segmento de gancho = ponta reta mínima + raio do pino + φ (medida externa).
- Comprimento unitário = soma das medidas nominais dos segmentos, arredondada a 0 casas (cm).
- Barras espelhadas são consideradas iguais (mesmo N).
