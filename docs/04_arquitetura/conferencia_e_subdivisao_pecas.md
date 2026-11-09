# Conferência e subdivisão das peças (proposta aprovada em 2026-11-09, PC2)

## Por quê
O nome do tipo no Revit às vezes está errado (ex.: "seção retangular com aba laje 26x170" medida como 51 × 170 com aba), e
as vigas têm seção variável ao longo do comprimento: trechos com aba para laje, apoio de escada, sem apoio, dentes. Antes de
exportar para o CAD, o usuário confere as seções e os nomes. A mesma subdivisão vai servir para:
- o detalhamento no CAD (JSON das peças, versão 2 com trechos);
- **armar direto no Revit** (mesma estrutura de dados);
- as **análises estruturais**: cada trecho tem a seção poligonal e as propriedades (área, CG, inércias), que viram as
  barras da análise com os seus parâmetros mecânicos. (D19: o motor de detalhamento continua antes das análises — aqui só
  fica o dado pronto.)

**Regra central:** a GEOMETRIA define a peça; o nome do tipo é só conferido. Divergência = aviso na janela, o usuário decide.

## 1. Janela "Conferir peças" (Revit, painel Pré-moldados)
Substitui o "Exportar para o CAD" direto (o exportar fica dentro dela). Abas **Vigas** e **Pilares**.
- Lista agrupada por grupo de peças iguais: marca, família/tipo do Revit, seção medida (ex.: "L 26/51 × 170"), comprimento,
  quantidade e situação (✔ ok; ⚠ nome ≠ geometria; ⚠ material não pré-moldado; ✖ seção que o CAD ainda não desenha).
- Visualização: elevação esquemática com os trechos coloridos e cotados; a seção de cada trecho em escala com todas as
  cotas (inclusive polígono qualquer); clicar no trecho mostra a seção.
- Edições: **marca** (gravada de volta no parâmetro Marca do Revit — resposta do usuário: a marca é o nome da peça), função de
  cada trecho, aceitar/ignorar a peça, "Selecionar no modelo". A geometria nunca é alterada.
- Exporta só o que foi conferido.

## 2. Subdivisão em trechos
1. Estações: os x (ao longo do eixo) de todos os vértices — pontas dos dentes, início/fim das abas, recortes.
2. Corte do sólido no meio de cada intervalo (`BooleanOperationsUtils.CutWithHalfSpace`; a face no plano = seção exata, com furos).
3. Trechos vizinhos com a mesma seção se juntam. Ex.: `0–25 dente | 25–300 aba de laje | 300–420 retangular | 420–520 aba de escada | 520–546 dente`.
4. Seção classificada: retangular, L, T, T invertido, I ou poligonal (faixas de largura ao longo da altura).
5. Função do trecho sugerida pela forma e **confirmada** pelo usuário.
Pilares: o mesmo corte ao longo da altura (consolos, cálice, chanfros) — na etapa 2, aba própria.

### Funções de trecho (respostas do usuário)
dente · apoio de laje · apoio de escada · **apoio de viga (viga apoiada em viga)** · sem apoio.
[A CONFIRMAR: "viga apoiando em viga" — a ponta desta viga apoiada em outra (dente/consolo na outra) e/ou um trecho desta
viga que recebe outra viga; o modelo prevê as duas.]

## 3. Modelo de dados (DEB.Core, sem Revit nem CAD)
- `SecaoPoligonal`: contorno + furos (y, z em cm); área, CG, inércias; tipo reconhecido e dimensões principais.
- `TrechoPeca`: início, fim, seção, função.
- `PecaLevantada` (etapa 3): tipo do Revit, medidas da geometria, situação, decisões do usuário, trechos.
Decisões gravadas no próprio elemento (Extensible Storage do plugin); geometria mudou = volta para "a conferir".

## 4. Etapas
1. **Núcleo** (sem tela): seção poligonal (propriedades, faixas, classificação), junção dos trechos, conferência do nome do
   tipo × geometria, sugestão de função. Testes.
2. **Revit**: corte do sólido por trechos; janela só de leitura (abas Vigas e Pilares, situação, elevação e seções cotadas).
   Testes com `Teste_PM.rvt` e `Projeto1.rvt`.
3. **Edições**: marca (gravada no Revit), função dos trechos, aceitar/ignorar, Extensible Storage, exportação pela janela.
4. **CAD**: JSON v2 com trechos; seções não suportadas marcadas até o VP-4.
5. Depois: armar no Revit e as barras da análise sobre a mesma estrutura.

Frente do DEB.Revit é do PC1: proposta registrada em `docs/notas/pc2_pendencias.md` (09/11/2026).
