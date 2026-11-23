# Vigas pré-moldadas — cálculo e armação (D40)

Botão **Pré-moldados → Vigas pré-moldadas** (`Commands/PrecastBeamsCommand.cs`, código em `src/Vigas/`).

## Fluxo
1. **Levantamento**: o mesmo do "Conferir peças" e do "Exportar para o CAD" (`PieceReview` + `ConversorLevantamento`):
   grupos de vigas iguais (giradas 180° entram no grupo), marca = nome do detalhamento. Dente que falta no catálogo entra
   com a armadura padrão (o cálculo confere).
2. **Cargas** (características; o peso próprio e a capa do 2º estágio são calculados): parâmetros de instância da viga,
   grupo "Análise estrutural", criados na primeira vez:
   - `PCEB_Carga permanente (kN/m)` — laje, capa fora da viga, revestimento, alvenaria;
   - `PCEB_Carga variável (kN/m)`;
   - `PCEB_Cargas concentradas` — texto `x: G + Q; x: G + Q` (cm da ponta esquerda; kN), ex. `250: 30 + 12`;
   - `PCEB_Apoio esquerdo (cm)` / `PCEB_Apoio direito (cm)` — eixo do apoio; 0 = furo do pino, meio do dente ou 10 cm.
   Peças do mesmo grupo com cargas diferentes: calcula a envoltória.
3. **Janela** (prévia, D26): grupos com selo (OK / CONFERIR / NÃO ATENDE), elevação com as barras, seção no meio do vão,
   diagramas Md e Vd, a armadura proposta, as verificações e o relatório HTML. Materiais (fck, classe, cobrimento) valem
   para todos os grupos; cargas por grupo (gravadas nas vigas ao armar).
4. **Armar no Revit**: limpa a armadura anterior do plugin, grava a marca e as cargas, cria as barras (núcleo:
   `VigaRebarLayout`, as mesmas regras do template do CAD), os dados das barras (comprimento do núcleo, D30), a montagem
   (viga + barras) e, na 1ª peça do grupo, as vistas (elevação, superior, corte junto ao apoio/dente, corte no meio, 3D)
   com filtros de bitola e cotas, e as tabelas "Relação do aço" e "Resumo do aço". **Não monta folha** (D29).
5. **Relatório** do grupo em `Relatórios PCEB\Verificação - <marca>.html`, ao lado do modelo.

## O que o cálculo faz (núcleo, `DEB.Core/Elementos/Viga/PreMoldada/Calculo`)
Viga biapoiada de concreto armado, com balanços nas pontas até os apoios:
- ELU, combinação normal (γg = γq = 1,4); Md máximo e Vd junto aos apoios (sem a redução de 17.4.1.2.1);
- flexão (17.2.2): x/d ≤ 0,45 (≤ C50), armadura dupla acima disso; As,mín (Tabela 17.3); arranjo da inferior em até 2
  camadas pela menor área que cabe (espaçamento livre de 18.3.2.2); 2º estágio: peça sozinha com todas as permanentes e a
  seção composta com o total;
- içamento: momento negativo nas alças com coeficiente dinâmico 1,3 e o fcj do saque → armadura superior;
- cortante, modelo I (17.4.2.2): VRd2, Vc0, Asw/s; espaçamento pelo Vd (18.3.3.2); zona das pontas até onde o vão não resolve.
  O padrão do escritório é o mínimo (espaçamentos e bitola de partida);
- ancoragem da inferior no apoio (18.3.2.4) nas pontas retas;
- dente Gerber: tirante pelo modelo do consolo (a/d; atrito-cisalhamento com μ = 1,4 para a/d ≤ 0,5; Hd = 0,16·Vd com
  neoprene, 0,5·Vd sem) e suspensão = Vd/fywd, comparados ao catálogo; biela como estimativa ("Conferir");
- pele (17.3.5.2.3) para h > 60 cm;
- ELS: flecha (Branson, ψ2 = 0,3, fluência com carga aos 30 dias, limite ℓ/250; contraflecha até ℓ/350 vai para a nota) e
  abertura de fissuras (combinação frequente, ψ1 = 0,4, Tabela 13.4).

Códigos K no relatório: K1–K3 flexão, K4 içamento, K5–K7 cortante, K8 ancoragem, K9–K11 dente, K12 flecha, K13 fissuras.
Com o cálculo, V6/V7 (estribos) e D7 (suspensão) deixam de ser "Conferir".

Aba e ressalto (23/11): armados com as mesmas funções da seção do CAD — grampo ou estribo fechado da aba e as longitudinais da ponta;
estribo do ressalto até o fundo (no dente, até o fundo do dente), construtivas e a barra em L de ligação com a capa. O cálculo usa a
alma (b × h) e o peso próprio com a aba e o ressalto; a armadura da aba e do ressalto é a do template (padrão), não calculada.
Não entram ainda (avisados): reforço das aberturas e protensão. A aba que passa por cima do dente (rente ao topo) ainda não é
reconhecida pelo levantamento do Revit (a peça fica de fora, com nota).

## Testar (AutoRun)
`SD_AUTORUN=vigas` no modelo de teste: grava `SD_CARGAS` (`g;q;concentradas`, padrão `12;6;`), calcula, grava os
relatórios, arma, exporta PNG das vistas e salva a cópia em `SD_SAVEAS`. `SD_SOCALCULO=1` só calcula (modelo intacto).
Resultado em `%AppData%\PCEB\DEB.Revit\logs\vigas_<data>\resultado.txt`. A janela é renderizada em `janela.png`, mas sai
vazia com a sessão do Windows bloqueada (a renderização WPF fica suspensa).
