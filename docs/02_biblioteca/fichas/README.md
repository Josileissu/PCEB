# Fichas de referência

Uma ficha por assunto: de onde vem o material, o que ele cobre, o que serve ao PCEB **agora** (detalhamento e
verificações normativas, D19) e **depois** (cálculo). As fichas citam itens de norma e páginas; não copiam o texto.
Os arquivos ficam fora do Git, em `docs/02_biblioteca/artigos/<fonte>/` (cada um com os direitos do autor).

## Prof. Paulo Sérgio dos Santos Bastos (UNESP Bauru) — <https://wwwp.feb.unesp.br/pbastos/>
Apostilas de disciplina, atualizadas para a **NBR 6118:2023** (2023–2024). Cada assunto termina com "Disposições
construtivas", cita a norma literalmente e tem exemplos numéricos — servem de gabarito para os testes do núcleo.
Arquivos em `artigos/pbastos/`. Mapa completo do site (manuais, catálogos, teses): [pbastos_site.md](pbastos_site.md).

| Assunto | Ficha | Uso no PCEB |
|---|---|---|
| Fundamentos, materiais, durabilidade, cobrimento | [fundamentos.md](fundamentos.md) | cobrimento por classe (agora) |
| Flexão simples (vigas: dimensionamento da seção) | [flexao_simples.md](flexao_simples.md) | armaduras mín./máx., pele, espaçamentos (agora); cálculo (depois) |
| Força cortante | [cortante.md](cortante.md) | estribos, ganchos, pinos, **suspensão** (agora) |
| Ancoragem e emendas | [ancoragem_emendas.md](ancoragem_emendas.md) | lb, ganchos, traspasse, proporção emendada (agora) |
| Vigas (análise, decalagem, ancoragem nos apoios) | [vigas.md](vigas.md) | decalagem/ancoragem nos apoios (agora); análise (depois) |
| Torção | [torcao.md](torcao.md) | disposições construtivas (agora, viga de borda) |
| Pilares | [pilares.md](pilares.md) | **pilar pré-moldado: verificações** (agora); 2ª ordem (depois) |
| Lajes maciças | [lajes.md](lajes.md) | detalhamento de lajes (futuro) |
| Sapatas | [sapatas.md](sapatas.md) | fundações (fase 3) |
| Blocos sobre estacas | [blocos.md](blocos.md) | motor de blocos (já existe no núcleo) |
| Escadas e marquises | [escadas_marquises.md](escadas_marquises.md) | futuro |
| Concreto protendido | [protendido.md](protendido.md) | vigas protendidas, alveolares (futuro) |
| Pré-moldados (El Debs, ABCIC, fib, Munte) | [pre_moldados.md](pre_moldados.md) | cobrimento reduzido, tolerâncias, consolos, transitórias |
| Alvenaria estrutural | [alvenaria.md](alvenaria.md) | fora do escopo atual |
| Pontes | [pontes.md](pontes.md) | fora do escopo atual |

## Normas usadas pelas fichas
NBR 6118:2023 e NBR 9062:2017, em `S:\Meu Drive\Normas` (fora do Git, D12). A NBR 6118 da pasta tinha o texto
embaralhado; usar a cópia "(texto corrigido)" gerada pela ferramenta "Corrigir texto PDF".

## Livros que o escritório tem só impressos
- *ABNT NBR 9062:2017 — Comentários e exemplos* (IBRACON/ABCIC, CT 304): consolos, dentes, ligações. Quando houver
  versão digital, incorporar às fichas de pré-moldados e às verificações.
