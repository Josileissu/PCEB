# Escada (D42)

O botão **Escada** fica no painel "Concreto armado" e trata a escada em laje armada longitudinalmente: um lance reto entre dois apoios, com patamares opcionais.

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
