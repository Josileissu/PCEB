# OpenFOAM

| Origem | Licença | Linguagem | Instalado aqui |
|---|---|---|---|
| Duas linhas: **OpenFOAM Foundation** (openfoam.org) e **ESI-OpenCFD** (openfoam.com) | **GPL-3** | C++; casos montados por arquivos de texto (dicionários) | não |

## Funções
- **CFD por volumes finitos**: escoamento incompressível/compressível, turbulência RANS (k-ε, k-ω SST),
  **LES/DES** (necessário para cargas de pico de vento), transferência de calor, multifásico.
- **Malha**: `blockMesh` (blocos), **`snappyHexMesh`** (malha em volta de geometria STL — prédios, galpões), importa de Gmsh e outros.
- **Paralelismo**: MPI nativo (`decomposePar`) — roda em muitos núcleos e em clusters; é aqui que o HPC importa.
- **Pós-processamento**: **ParaView** (BSD); coeficientes de pressão, forças e momentos por superfície.
- **Windows**: roda melhor em Linux; no Windows via WSL, Docker ou builds de terceiros.

## Uso em engenharia de vento
- Camada-limite atmosférica na entrada (perfis de velocidade e turbulência), coeficientes de pressão (Cp) nas faces,
  forças globais, efeito de vizinhança, conforto de pedestres.
- Exige experiência para validar (malha, turbulência, tempo de simulação) — resultado errado parece certo.

## Avaliação (provisória)
| Critério | Nota | Comentário |
|---|---|---|
| Maturidade | 5 | padrão aberto em CFD, uso industrial e acadêmico |
| Manutenção | 5 | duas linhas ativas, versões anuais |
| Documentação | 3 | tutoriais bons, curva de aprendizado alta |
| Integração | 3 | arquivos texto + executáveis; melhor em Linux/servidor |
| Aderência | 2 | pontual: formas fora da NBR 6123; o dia a dia é a norma |
| Licença | 4 | GPL: usar como programa externo (servidor de simulação) |

## Uso possível para nós
- Fase futura: obter Cp de formas atípicas e conferir coeficientes da NBR 6123; rodar na nuvem (fila de trabalhos).
