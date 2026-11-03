# TQS

| Fabricante | Licença | Plataforma | Instalado aqui |
|---|---|---|---|
| TQS Informática (Brasil) | comercial, por módulos | Windows | ✓ `C:\TQSW` (programas), `C:\TQS` (projetos) |

## Modelagem
- Modelador estrutural de edifícios por pavimentos (formas), pilares, vigas, lajes, fundações. ✓ (pasta FORMAS)
- Modelos de exemplo locais: `MODPREO`, `Mod_PrCA_01`, `CPQ04 - Blocos`, `TESTEALV`. ✓

## Análise
- Pórtico espacial (ELU/ELS), grelha de lajes, lajes por MEF.
- Estabilidade global: γz, P-Δ, efeitos de 2ª ordem; não linearidade física aproximada (NBR 6118).
- Interação solo-estrutura (SISE). ✓ (pasta SISE)
- Situações de construção (pré-moldados: fases de montagem).

## Dimensionamento (NBR)
- NBR 6118 (vigas, pilares com 2ª ordem local, lajes), NBR 9062 (pré-moldados), fundações (blocos, sapatas, estacas),
  alvenaria estrutural (ALVEST ✓), protendido, escadas ✓, elementos especiais ✓.

## Detalhamento
- Armação automática de vigas ✓, pilares ✓, lajes ✓, fundações ✓, escadas ✓, com edição gráfica (editores de armadura).
- Desenho padrão (DP ✓) configurável — análogo ao nosso "padrão de apresentação".

## Fabricação / CAM
- Pré-moldados (PREFAB ✓): peças, numeração, fichas/pranchas de fabricação. Exportação CAM a confirmar.

## Documentação
- Pranchas DWG/DXF próprias (editor gráfico TQS), tabelas de ferro, quantitativos.

## Importa / exporta
- DWG/DXF, IFC, integração BIM com Revit (plugin), PLT.

## API / automação
- **API Python oficial** ✓: `C:\TQSW\EXEC\PYTHON\TQSPythonInterface-1.2.7-py310-none-any.whl`,
  manual `Programacao_TQS_Python.pdf`, exemplos `TSTBUILD/TSTGEO/TSTFER/TSTDWG/TSTGRID/TSTEXEC.PY` ✓.
  Pelos nomes: edifício, geometria, ferros, desenhos, grelha, execução de processos.
- Wheel para Python 3.10 (no PC há 3.14/3.15).

## Pré-moldados
- Módulo PREFAB ✓ (pasta `SUPORTE-PY\PREFAB` com `CRITPRE.DAT` — critérios de pré-moldados).
  Referência nacional mais próxima do que queremos.

## O que aproveitar
- Organização por **critérios** (arquivos DAT por módulo) e desenho padrão — modelo para nossos padrões.
- API Python para ler esforços/geometria e comparar com nossos resultados.

## A confirmar
- Módulos licenciados no escritório; o que o PREFAB detalha (consolos, dentes, içamento); exportação CAM.
- Ler `Programacao_TQS_Python.pdf`.
