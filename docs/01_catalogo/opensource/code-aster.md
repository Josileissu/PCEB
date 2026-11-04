# code_aster

| Origem | Licença | Linguagem | Instalado aqui |
|---|---|---|---|
| EDF (Électricité de France) — code-aster.org, gitlab.com/codeaster | **GPL-3** ✓ | Fortran 90 (núcleo) ✓, C/C++ ✓, Python (comandos e scripts) ✓; build com `waf` ✓ | não (código-fonte em `programas/codeaster/download`) |

## Funções
- **Entrada**: arquivo de comandos `.comm` (Python) ✓ + malha (`.med`, `.mail`) ✓; caso de estudo descrito em `.export` ✓.
  Pré/pós normalmente pelo **salome_meca** (Salome + code_aster), distribuído em container ✓.
- **Análise**: estática linear e não linear (material, geométrica, **contato/atrito**), modal, flambagem, dinâmica no tempo
  e espectral, térmica e termomecânica, fadiga, mecânica da fratura, fluência, geomecânica (solos, poro-mecânica).
- **Elementos**: sólidos, cascas, placas, vigas, barras/cabos, discretos; **armaduras e cabos de protensão embutidos** no concreto.
- **Materiais**: biblioteca grande, inclusive **concreto com dano/fissuração** (modelos tipo Mazars, ENDO_*, BETON_*),
  retração e fluência do concreto, aço com plasticidade; integração com **MFront** (leis de material do usuário) ✓.
- **Saídas**: `.med` / `.rmed` (Salome/ParaVis), tabelas texto.
- **Dimensionamento / detalhamento**: não por norma brasileira; há pós-processamento de armadura por Eurocódigo
  (cálculo de seção de aço a partir dos esforços em cascas) — a confirmar.
- **Validação**: ~4 300 casos-teste `.comm` no repositório (`astest/`) ✓ — boa fonte de referências numéricas.

## Avaliação (provisória)
| Critério | Nota | Comentário |
|---|---|---|
| Maturidade | 5 | 30+ anos, usado pela EDF em usinas, barragens e obras de concreto |
| Manutenção | 5 | desenvolvimento contínuo, versões estáveis e de teste marcadas ✓ |
| Documentação | 3 | enorme, mas muito em francês e técnica |
| Integração | 3 | arquivo `.comm` (Python) + executável; pesado; Windows só via container/WSL ou builds de terceiros |
| Aderência | 4 | regiões locais de pré-moldado com concreto fissurado e protensão; fases (ativar/desativar) |
| Licença | 4 | GPL-3: usar como **programa externo** é tranquilo |

## Uso possível para nós
- Análise local avançada (consolos, dentes, cálices, insertos) com **concreto não linear e protensão** — vai além do CalculiX.
- Referência/validação de resultados (casos-teste oficiais).

## A confirmar
- Binários para Windows (oficialmente Linux; salome_meca em container); tempo de aprendizado; pós de armadura por norma.
- Os repositórios `validation` e `data` (dados proprietários) não vêm no zip ✓ — conferir se fazem falta.
