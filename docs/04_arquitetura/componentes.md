# Componentes do motor (frente K, PC2, 2026-11-07)

Pedido do usuário (2026-11-07): elemento = composição de **componentes reutilizáveis** descrita numa **receita** em dados.
Contrato em `src/DEB.Core/Motor/Componentes/ContratosComponentes.cs` (`IComponente`, `ContextoMotor`, `ArgumentosComponente`).
Implementação em `src/DEB.Core/Motor/Componentes/` (catálogo: `ComponentesPadrao.Catalogo()`). O executor de receitas JSON é
da frente R; enquanto ele não chega, `MotorBlocoEstacas.ReceitaPadrao` monta a mesma sequência em C#.

Regras comuns: todo componente aceita `ativo` (0 = pula). Argumentos = números (`Numeros`) ou textos (`Textos`); unidades cm/mm/graus
indicadas em `IComponente.Argumentos`. Textos de regra (`regra_*`) só vão para `PosicaoArmadura.Regras`. Variáveis publicadas:
`ComponenteBase.VariaveisPublicadas` (padrão) e `VariaveisPublicadasCom(args)` (nomes configuráveis).

## Componentes

| Componente | O que faz | Argumentos principais | Consome | Publica |
|---|---|---|---|---|
| `apoios_estacas` | contorno + apoios: grade nx×ny ou triângulo de 3 (hexágono); ou forma dada (circular \| retangular) com apoios centrados em grade ou anéis (+ central) | arranjo (grade/triangulo/aneis/nenhum), nx, ny, esp_x, esp_y, dist_face, forma, diametro, lx, ly, lados, anel1_n/raio/angulo, anel2_*, central | — | Dados `contorno`, `estacas`, `arranjo` (+ `forma`); Var `largura_x`, `largura_y`, `centro_apoios_x/y`, `triangular`, `n_apoios`, `circular`, `raio` |
| `corpo_prismatico` | corpo de concreto (planta × z0..z1), vazio opcional | papel, contorno \| retangulo \| x0..y1, z0, z1, vazio | o polígono/retângulo nomeado | corpo |
| `corpos_circulares` | um cilindro por ponto (estacas) | papel, pontos, raio, z0, z1 | `estacas` | corpos |
| `niveis` | níveis nomeados | `<nome>` = z | — | `Modelo.Niveis` |
| `bitola_efetiva` | bitola de uma função já com a sobrescrita, sem consumi-la | funcao, posicao, variavel | — | Var `bitola_<funcao>` (ou `variavel`) |
| `faixa_sobre_apoios` | faixas sobre as linhas de apoios (clamp na face, passo reduzido se não couber, pernas, VAR) | funcao, chave, linhas (grade/lados/auto), cobrimento, recuo_lateral, z_face, superior, empilhar_y, perna, regra_perna, limite_perna_z | `contorno`, `estacas`, `arranjo` | Var `bitola_principal`, `z_topo_inferior` (máx.) |
| `malha_face` | uma direção de malha numa face, recortada pelo contorno e interrompida na abertura | funcao, chave, angulo, cobrimento, recuo_lateral, z_face, superior, camadas, perna, abertura, folga_abertura, trecho_minimo, cobrimento_normal | `contorno`, `abertura` | Var `z_topo_inferior` (inferior) |
| `contorno_lateral` | estribos horizontais em volta do contorno − cobrimento | funcao, chave, cobrimento, z0, z1 | `contorno` | posição |
| `estribos_verticais` | estribos no plano yz distribuídos em x, com trecho excluído | cobrimento, comprimento, largura, altura, x0/y0/z0, excluir_x0/x1 | — | posição |
| `calice_forma` | abertura, paredes, colarinho (limitado ao contorno); corpos colarinho/furo/regularização | centro_x/y, pilar_bx/by, folga, parede, colarinho, z_base_colarinho, z_topo, embutimento, regularizacao | `contorno` | Dados `calice_furo`, `calice_colarinho`, `abertura`; Var `z_fundo_pilar`, `z_fundo_calice` |
| `calice_armaduras` | horizontais do topo e restantes + grampos, verticais, suspensão, fundo | colarinho, cobrimento, cobrimento_calice, parede, centro_x/y, z_topo, embutimento, regularizacao, z_topo_inferior, grampos, desconto_perna_externa, comprimento_fundo | `calice_furo`, `calice_colarinho` | posições calice_* , suspensao, fundo_calice |
| `arranque` | esperas do pilar (pé, trecho, traspasse) + estribos do pilar; pontos da seção por `SecaoBarras.Posicoes` | centro_x/y, pilar_bx/by, nx, ny, cobrimento_pilar, cobrimento, z_topo, z_topo_inferior, fck, espera, pe, pe_dentro | — | arranque, arranque_estribo (+ avisos lb/espera) |
| `barras_secao_longitudinal` | longitudinais de seção retangular vertical: cantos (1 ou 3 em "L"), faces, emendas pelo comercial (alternadas ou na mesma seção; traspasse NBR 6118, mín. 45φ) — cálculo `SecaoBarras`, o mesmo do pilar pré-moldado | funcao, funcao_vizinha, funcao_face, funcao_estribo \| bitola_estribo, chave, centro_x/y, bx, by, cobrimento (+ _xp/_xm/_yp/_ym), z_inicio, z_fim, por_canto, espacamento_canto, n_face_x/y, emendas, mesma_secao, comercial, fck, feixe, feixe_vizinha, feixe_face | — | `{chave}_canto/_vizinha/_face_x/_face_y` (+ `_2…`, `_b1…` das peças); sobrescritas bitola e traspasse na chave do grupo |
| `grampos_secao` | grampos suplementares de seção retangular vertical: retos (medida externa do estribo) com ganchos de 135° para dentro, um por par de intermediárias das faces (ou no meio), a cada espaçamento | funcao, chave, funcao_estribo \| bitola_estribo, funcao_longitudinal, centro_x/y, bx, by, cobrimento (+ _xp/_xm/_yp/_ym), por_canto, espacamento_canto, n_face_x/y, direcoes, z_inicio, z_fim, espacamento | — | `{chave}_x`, `{chave}_y` |
| `borda_u` | U de borda em volta do contorno (perna na face lateral, abas para dentro); circular: radiais, perímetro/espaçamento; polígono: por lado | funcao, chave, contorno, cobrimento, z_inferior, z_superior, aba | `contorno`, Var `circular`/`raio` | `{chave}` |
| `barras_borda` | barras corridas por lado dentro dos U (base retangular); circular: anéis fechados (2 arcos de 180°) por dentro dos U | funcao, chave, contorno, cobrimento, bitola_u, z_inferior, z_superior, por_face, emenda | `contorno`, Var `circular`, `raio` | `{chave}_x`, `{chave}_y` (ou `_1…`); `{chave}_anel` |
| `verificacoes_geometria` | eixo do apoio × raio; pilar fora do contorno | dist_face, raio_apoio, centro_x/y, pilar_bx/by, texto_pilar_fora | `contorno` | avisos |
| `verificacoes_calice` | NBR 9062: Lemb ≥ 40 e ≥ 1,2/1,5·h, parede ≥ 15, fundo ≥ 20, colarinho > contorno | pilar_bx/by, embutimento, interface, colarinho, parede, z_topo, regularizacao | `calice_furo`, `contorno` | avisos |
| `aviso` | aviso fixo (com `ativo` = condição) | texto | — | aviso |

## Ordem no bloco sobre estacas (`MotorBlocoEstacas.ReceitaPadrao`)
Variáveis da receita: cobrimento, cobrimento_calice, dist_face, diametro_estaca, embutimento_estaca, altura_base, lastro,
pilar_bx/by, calice, colarinho, altura_colarinho, altura_total, embutimento, regularizacao, folga, parede, z_inferior.

1. `apoios_estacas` → 2. `corpo_prismatico` bloco → 3. `calice_forma` (ativo = calice) → 4. `corpo_prismatico` pilar →
5. `corpos_circulares` estacas → 6. `corpo_prismatico` lastro (ativo = lastro > 0) → 7. `niveis` →
8. `bitola_efetiva` lateral → 9. `faixa_sobre_apoios` principal → 10–13. `malha_face` secundária x, superior x, secundária y,
superior y → 14. `contorno_lateral` → 15. `estribos_verticais` (ativo = !triangular) → 16. `calice_armaduras` (ativo = calice) →
17. `arranque` (ativo = !calice) → 18. `verificacoes_geometria` → 19. `verificacoes_calice` (ativo = calice) → 20. `aviso` triangular.

A ordem das posições no modelo é a mesma do motor antigo (a numeração da prancha depende dela).
Paridade (2026-11-07): 14 casos gravados em JSON antes/depois são idênticos byte a byte, exceto o aviso de sobrescrita de
posição inexistente, que agora vem do `ContextoMotor.Concluir` ("neste bloco estacas" em vez de "neste bloco").

## Receita do bloco (2026-11-07)
O bloco agora é montado pela receita JSON de fábrica `src/DEB.Core/Motor/Receitas/Fabrica/bloco_estacas.json` (recurso
embutido `DEB.Receitas.bloco_estacas.json`, registrado por `ReceitasFabrica.Garantir()`); o `MotorBlocoEstacas` só roda
o `MotorReceita` com ela. A sequência em C# (`ReceitaPadrao`) foi removida depois de conferida idêntica em 8 casos
(B28, B15, B31, arranque, embutido, 1 estaca, excêntrico, sem lastro) × 2 perfis × sobrescrita.
- Variáveis da receita: `c`, `cc`, `emb_est`, `lastro_ef`, `tri`, `calice`, `colar`, `h_colar`, `h_total`, `emb_pilar`,
  `reg`, `z_inferior`, `perna_por_valor`, `pe_dentro` (mais as do elemento em snake_case e as publicadas pelos componentes).
- O usuário pode substituir a receita inteira em `%AppData%/PCEB/Receitas/bloco_estacas.json` (exportar a de fábrica com
  `RepositorioReceitas.ExportarFabrica`); arquivo inválido → fábrica + aviso. O `ValidadorReceita` confere sem executar.
- `calice_forma` publica também `calice_colarinho_x0/x1/y0/y1` (trecho sem estribos verticais).
- Fórmulas: além do pedido da frente R, `var('nome', padrão)` (variável de um componente anterior) e `tem('funcao')`.

## Barras curvas (2026-11-07)

`PosicaoArmadura.Bulges` (um por segmento, como a polilinha do CAD: tan(ângulo/4), positivo = anti-horário visto de
`NormalArcos`, padrão +z) + `NormalArcos`. As mesmas para todas as barras de `Barras`. A prancha desenha os arcos de verdade
quando a vista mostra o plano deles (planta para arcos horizontais, a forma dobrada sempre) e em trechos nas outras
(`Arcos.Amostrar`). Gravados no JSON do modelo ("bulges", "normalArcos").
