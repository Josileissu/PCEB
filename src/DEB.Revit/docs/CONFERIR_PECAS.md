# Conferir peças

Plano: `docs/04_arquitetura/conferencia_e_subdivisao_pecas.md`.

Botão "Conferir peças" (painel Pré-moldados, `Commands/ReviewPiecesCommand`): `Precast/PieceReview` levanta vigas e pilares
(SuperComponent nulo), estações nos vértices ao longo do eixo/altura, corta o sólido no meio de cada intervalo
(`BooleanOperationsUtils.CutWithHalfSpace`; a face com normal −eixo no plano = seção, com furos) e junta os trechos iguais
(`DEB.Core/Pecas`: `SecaoPoligonal`, `Subdivisao`, `ConferenciaPecas` — grupos pela GEOMETRIA, invertidas iguais; nome do tipo só
conferido). Janela `Precast/UI/PieceReviewWindow` (abas Vigas/Pilares, só leitura nesta etapa) + `PieceReviewPreview` (elevação
com trechos coloridos pela função, seções cotadas, área/Iy/CG). AutoRun `conferir` → `logs/conferir_*/resultado.txt` + PNG por grupo.
Testado no PC2: Teste_PM (3 grupos de vigas 20×50 com dentes 25×25, pilares 40×40 e 50×50, tudo ok) e Projeto1 (viga com mísula
de 0 a 325 + trecho sem aba + dente: "seção que o CAD ainda não desenha").
Pilares (mesmo dia, pedido do usuário): consolos pelo mesmo reconhecimento do "Pilares pré-moldados" (`PrecastSurvey`), unidos ao
pilar nos cortes horizontais; perfil exato de cada consolo cortado no meio da largura (`ConsoloLevantado`); aba Pilares com as
QUATRO FACES em pé (como no template: face 1 = +x, 2 = −y, 3 = −x, 4 = +y, vistas de fora; consolo de frente, de perfil ou tracejado
atrás), coluna "CONSOLOS (perfil)" ampliada com cotas e chanfros, seções empilhadas na vertical; grupos com giro 90/180/270° = iguais.
**Chanfro do consolo** (relato do usuário): `Describe` media a altura da ponta nos vértices da ponta — o chanfro 2 × 2 do Teste_PM
virava "ponta 21,5" (trapezoidal, CT no catálogo). Agora `PerfilConsolo.Medir` (Core): envoltórias convexas do perfil, chanfro =
último trecho ≤ 5 cm com outra inclinação, altura pelas retas principais → 23,5 (o JSON do CAD também).
**Viga apoiada em viga** (relato do usuário, modelo real de uma obra, V245): o consolo é aninhado na viga que SE APOIA e encosta na
face lateral da viga que RECEBE. `PieceReview.BeamCorbels` dá cada consolo sem dono (os dos pilares saem antes) à viga cuja face
lateral REAL (planar, normal ±y — inclui a da alma dentro das abas) ele toca e da qual sai; perfil cortado no meio do consolo;
trecho = "Apoio de viga" (situação ✖: o CAD ainda não desenha consolo em viga). Na cópia do modelo real: V112/V212, V237, V312 e 3
sem marca. "encostaEm" do diagnóstico NÃO serve para isso (consolos de pilar encostam nas vigas vizinhas).
Revit do usuário aberto segura a DLL: renomeie `DEB.Revit.dll`/`DEB.Core.dll` para `.antigo` (o Windows deixa) e copie as novas —
o Revit dele segue com a carregada; os abertos depois pegam a nova. Próximo (etapa 3): marca editável gravada no Revit, função dos
trechos, aceitar/ignorar, Extensible Storage, exportar pela janela, "Selecionar no modelo".
