# CYPE (CYPE 3D, CYPECAD)

| Fabricante | Licença | Plataforma | Instalado aqui |
|---|---|---|---|
| CYPE Ingenieros (Alicante, Espanha) | comercial, por módulos (`checklic.exe`) | Windows (nativo, C) | ✓ **CYPE 3D 2026.c** (`C:\CYPE Ingenieros\Versão 2026\CYPE 3D`, 1,0 GB, 677 DLLs) — **só o CYPE 3D**; CYPECAD não está instalado |

Levantamento de 05/11/2026 pelos arquivos instalados (nomes, exportações e textos das DLLs); a ajuda do CYPE é online
e o programa não foi aberto. Projetos em `C:\CYPE Ingenieros\Projetos\CYPE 3D` (`.ed3`, binário próprio, cabeçalho `CYPE-2026-c`) ✓.

## Arquitetura e tecnologia ✓
- **Programa nativo em C**, sem .NET: um executável pequeno + **uma DLL por módulo/norma** (o código-fonte é `*.c` por
  módulo, como mostram as referências internas). Interface própria (painéis, faixa de opções `ribbon.dll`, diálogos).
- **3D em OpenGL com shaders GLSL** próprios (`cyopengl`: framebuffer, malhas, texturas, iluminação); remoção de
  linhas ocultas (`cyhlr`).
- Exporta **glTF** (`cygltfexp`) e **FBX** (Autodesk FBX SDK); DXF/DWG próprio (`cydwgdxf`, `dxflib`); PDF; **BC3**
  (orçamento, formato FIEBDC espanhol).
- **Open BIM / BIMserver.center**: IFC (`bimifc`, `bsifc*`), BCF (`bsbcf`), gbXML, JSON de obra; **PostgreSQL** e
  JSON nos serviços; **Git embutido** (`git.dll`: clone, commit, branch, diff) — controle de versões dos projetos.
- **OpenSees 3.2.2 embutido** (`cyopensees`, `cyopenseesbas`, com OpenSeesSP e MPI) para **análise modal, pushover e
  sismo dinâmico** — um comercial usando o motor aberto que está no nosso catálogo (`opensource/opensees.md`).
- Paralelismo: OpenMP (`vcomp`).

## Modelagem e análise (CYPE 3D) ✓
- Estruturas de barras 3D em **aço laminado e formado a frio, concreto, madeira e alumínio**; painéis; cargas e
  combinações; **vento** por norma do país; **sismo** (estático, modal, dinâmico 3D, pushover); 2ª ordem;
  **MEF** (`calcmef`, malhas); **resistência ao fogo** (`rstfuego`, setores de incêndio).

## Normas (uma DLL por país/norma) ✓
- **Brasil**: **NBR 8800** (aço), **NBR 14762** (formados a frio, edições 2001 e 2010), **NBR 7190:1997** (madeira,
  inclusive fogo), **NBR 6123** (vento, com turbulência e categorias de rugosidade), **NBR 6118** (verificações de
  concreto em barras: taxas mínimas, espaçamentos; combinações da NB-1/2003), bitolas brasileiras (`posbarrasbr`).
- Outras: ACI, AISC (inclusive 341 sísmico), ASCE, CSA S16, Eurocódigos 2/3/5/9, EHE, BAEL, DIN 18800, britânica,
  CIRSOC, COVENIN, NSR, NTC (México), IS 800/801 (Índia), China, Rússia, Chile e mais de 20 países.

## Dimensionamento e detalhamento ✓
- **Aço**: verificação de barras por norma; **ligações metálicas** (`uniones*`, soldadas e parafusadas, viga-pilar,
  pilar-viga), **placas de base** com chumbadores (`placancl`, `pernos`).
- **Concreto em barras**: armadura de **pilares** (`pilararm`, grupos e trechos de armadura) e **vigas** (tabelas de
  armadura transversal `dmtbarmvig`).
- **Fundações**: **blocos sobre estacas** de 1, 2, 3… estacas (`encepado`), **estacas e tubulões** com armadura
  (`pilotes`), sapatas e vigas de fundação (`elemcim`, `vigacim`, `planocim`).
- Desenhos de detalhamento por norma/país (`det_*`); planos e listagens (`planos`, `listados`, `htmlgen`).

## Fabricação ✓
- **Exportação para Tekla** (`cytekla`, ligações às versões 12 a 21: perfis, chapas, parafusos, cantoneiras,
  enrijecedores) e para o **Tecnometal** (detalhamento metálico brasileiro, via AutoCAD) — pontes do cálculo para a
  fabricação metálica. CIS/2 (`cypecis2`).

## API / automação
- **Não há API pública** nas DLLs (a `dllapi` só embute dados do instalador). Integração pelo ecossistema Open BIM
  (IFC, JSON, BIMserver.center).

## Pré-moldados
- Nada específico no CYPE 3D. (O CYPECAD, não instalado, tem lajes pré-fabricadas — a confirmar no PC2, se houver.)

## O que aproveitar para o nosso programa
1. **Uma DLL por norma** — modelo de organização para o PCEB: núcleo + "pacotes de norma" (NBR, e no futuro outras),
   trocáveis sem mexer no resto.
2. **OpenSees como motor dentro de um comercial** — mostra que dá para usar o OpenSees como motor externo (processo
   separado) para modal/pushover; atenção à licença (ver ficha do OpenSees).
3. **glTF** para visualização na web (three.js) e **Git para versionar projetos** — ambos encaixam no PCEB web.
4. **Exportação para Tekla e Tecnometal** — referência de como ligar cálculo a detalhamento/fabricação metálica (fase 4).
5. **Open BIM** (vários programas pequenos trocando IFC/JSON por um servidor) — alternativa ao programa monolítico.

## A confirmar
- Módulos licenciados no escritório; o que o CYPE 3D detalha de concreto na prática (abrir um exemplo).
- Se o PC2 tem o CYPECAD (lajes, pré-fabricados, pranchas de armadura de edifícios).
