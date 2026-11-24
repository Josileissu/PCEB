# Escada (D42)

O botão **Escada** fica no painel "Concreto armado" e trata a escada em laje de lance reto. Ela pode ser de três tipos, escolhidos na janela:
- **armada longitudinalmente**: entre os apoios das pontas, com patamares opcionais;
- **armada transversalmente**: entre as vigas ou paredes laterais;
- **em balanço**: engastada numa viga ou parede de um lado. O apoio fica à esquerda de quem sobe.

A escada armada longitudinalmente pode ser **pré-moldada** (D43). O cartão "Pré-moldada" define:
- a forma de cada ponta: apoio direto ou dente;
- o material do apoio, que define Hd = k·Fd pela NBR 9062, 7.3.9;
- os pinos: quantidade, bitola, furo e embutimento;
- o içamento.

A aba "Ligações e içamento" mostra Fd e Hd, o tirante, a suspensão e a biela do dente, a resistência dos pinos, o peso da peça e a força por alça. Todas as barras ficam dentro da peça. A família tem o recorte do dente e os furos dos pinos.

Em "Peça" escolhe-se **Lance** ou **Patamar** (laje plana, para o patamar intermediário como peça à parte; usa o vão do patamar e a
espessura do patamar). O campo "Cargas de outras peças" recebe as pontas que se apoiam nesta peça: `x comprimento: G + Q; …` (cm; kN por
metro de largura; x = centro da carga a partir do apoio de baixo). O autoteste monta uma escada em U (dois lances, patamar e viga do
patamar), mostra as transferências no `resultado.txt` e cria o patamar.

**Escada L/U** (botão próprio): clique o eixo do apoio de baixo do 1º lance e o sentido de subida numa planta. A janela monta o arranjo
(em U com o patamar como peça e a viga do patamar, em L ou em linha com o patamar de um lance sobre o do outro), deixa editar cada
peça ("Editar…" abre a janela da escada) e o x de cada apoio, e mostra a planta, as cargas entre peças, as peças, a viga do patamar e as
verificações. "Criar no Revit" cria todas as peças com as barras e a viga do patamar (sem barras). O autoteste cria uma em U e uma em L.

Na transversal e no balanço o cálculo é feito numa faixa de 1 m ao longo do lance. A prévia mostra também o corte transversal no meio do lance, com as barras na largura e as dobras.

## Como usar
1. Selecione as **duas vigas** (ou lajes) de apoio, a de baixo e a de cima, e clique em **Escada**. O programa lê da seleção:
   - o sentido de subida, perpendicular à viga de baixo e apontando para a de cima;
   - o desnível, entre os topos das duas vigas;
   - o vão, entre os eixos;
   - a largura do trecho comum das vigas, até 120 cm;
   - os patamares: a sobra do vão é dividida entre as duas pontas.

   Sem seleção, clique numa planta o eixo do apoio inferior e depois um ponto no sentido de subida. O desnível e o vão então são digitados.
2. A janela (`Escadas/EscadaWindow.cs`) calcula ao abrir e a cada clique em **Calcular**. Abas:
   - **Elevação e armação**: contorno com os degraus, eixo do cálculo, barras N1 a N5, quina reentrante e cotas.
   - **Esforços (ELU)**: Md com o MRd, Vd com o VRd1 e Nd, por metro de largura.
   - **Reações e deformada**: reações por metro e na escada inteira, e as flechas elástica, imediata (Branson) e total.
   - **MEF × cálculo**: o botão "Conferir por MEF" roda o OpenSees e o CalculiX em segundo plano e sobrepõe os resultados ao cálculo.
   - **Otimizador**: opções que atendem a todas as verificações, com os apoios fixos e ordenadas por custo. O botão "Usar" aplica a opção.
   - **Verificações**: tabela do padrão D41. Os códigos E são de geometria, os K de cálculo e os M da conferência por MEF.
   - **Memória** do cálculo.
3. **Criar no Revit** faz quatro coisas:
   - gera a família `PCEB_Escada_E<n>` a partir do `Metric Generic Model.rft`, com o contorno extrudado na largura, "aceita armadura" ligado e material de concreto;
   - carrega a família e coloca a instância na posição;
   - cria as barras do núcleo: as longitudinais repetidas na largura, e as de distribuição em uma barra por trecho reto, repetida ao longo dele;
   - preenche os dados das barras: partição = marca da escada e `PCEB_Comprimento (núcleo)`.

## Núcleo
- `DEB.Core/Elementos/Escada`: `EscadaLaje` (entrada), `GeometriaEscada`, `CalculoEscada`, `MefEscada`, `OtimizadorEscada`.
- `DEB.Core/Analise/Mef`: `Portico2D` (OpenSees), `Chapa2D` (CalculiX) e `FerramentasMef`. Os caminhos dos programas vêm de `%AppData%\PCEB\mef.txt`, com as linhas `python=` e `ccx=`, ou das variáveis `PCEB_PYTHON_OPENSEES` e `PCEB_CCX`.
  - O openseespy do Windows só carrega no **Python 3.12**.
  - Sem os programas, a conferência por MEF fica indisponível e o resto funciona.
- Levantamento, cargas e esquemas em `docs/04_arquitetura/escadas.md`; tarefa P1-13.

## Autoteste
`SD_AUTORUN=escadas`, com o `Teste_PM.rvt` e o `SD_SAVEAS` apontando para uma cópia. O autoteste:
- calcula a escada de exemplo (ou a de `SD_ESCADA="Desnivel=144;Espelhos=8;Piso=28;..."`) e roda o MEF e o otimizador;
- grava a janela em PNG, uma imagem por aba;
- cria duas vigas de apoio em níveis diferentes, giradas 30°, e posiciona a escada por elas;
- cria a família e as barras e exporta um corte e uma vista 3D.

O resultado vai para `logs\escadas_*\resultado.txt`.
