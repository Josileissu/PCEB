# Gtool3D (versão de avaliação 0.11.0)

| Fabricante | Licença | Plataforma | Instalado aqui |
|---|---|---|---|
| Eng. Joabe Wariss (autor independente, Brasil) ✓ | comercial; avaliação de 30 dias, até 200 barras, presa à máquina; proíbe copiar, desassemblar e modificar ✓ | aplicativo web (JavaScript): `Gtool3D.exe` (servidor local em 127.0.0.1 + Edge/Chrome) ou `Gtool3D.html`; também Linux ✓ | não — só o `.rar` em `programas/gTool3D/download` (manual, memorial de exemplo, termo, roteiro e 2 modelos `.gfx` lidos; `.exe` não executado) |

Fontes: manual do usuário (122 p.), termo de uso, roteiro de teste com engenheiros e memorial de exemplo, todos no `.rar` ✓.
Não encontrei nada sobre o programa na web (05/11/2026); parece ser um lançamento recente, ainda em teste com engenheiros.

## 1. Modelagem ✓
- Pórtico, treliça, viga e grelha em 3D ou no plano, no estilo do **Ftool** (sem código nem vínculo com o Ftool, segundo o autor).
- Seções paramétricas (16 formas, entre elas T, L, I e perfis de aço), apoios (mola, recalque, eixos girados), rótulas, barra rígida.
- Teclado de coordenadas, snap, grade, cotas, transformações; Ctrl+Z com as últimas 200 alterações.
- Placas (lajes) com até 8 cantos, convexas: 4 direções, 2 direções (treliçada) ou vazio.

## 2. Análise ✓
- Rigidez direta, 6 graus de liberdade por nó, Euler–Bernoulli ou Timoshenko; **linear elástica** (sem fissuração nem fluência).
- 2ª ordem P-Δ e P-δ, λcr, γz (NBR 6118, 15.5.3), deslocabilidade por andar (NBR 8800, 4.10.4), rigidez reduzida automática (pilar 0,8, viga 0,4, laje 0,3·EcIc).
- Lajes: carga às vigas pelas **charneiras da NBR 6118 (14.7.6.1)**; laje em grelha; placa em elementos finitos (MITC4) validada contra Navier.
- Linha de influência e trem-tipo (painel igual ao do Ftool), envoltórias.
- Tamanho: confortável até cerca de 3.000 barras; limite de cerca de 100 mil graus de liberdade (roda no navegador).

## 3. Dimensionamento ✓
- **Nenhum** (removido na v0.3.0). Normas usadas só como fonte de coeficientes: NBR 6118, 8681 (gerador de combinações), 6120 (94 usos e peso de paredes, transcritos de tabelas publicadas), 6123:2023 (vento estático por nível, Ca das figuras 4 e 5 digitalizadas, sem Cpe/Cpi), desaprumo pela NBR 6118 (11.3.3.4.1).

## 4. Detalhamento / 5. Fabricação
- Não tem.

## 6. Documentação e desenho ✓
- **Memorial de cálculo em PDF no estilo CYPECAD**: 1. versão e licença; 2. dados gerais; 3. normas; 4. método ([K]{u}={F}); 5. ações com as fórmulas, lista "Onde:" e o cálculo do nível mais alto com os números substituídos; 6–7. estados limites, γ/ψ e matriz das combinações; 8. reações, esforços, flechas (f∞ = f0·(1+αf) ≤ ℓ/250) e γz, com o pior caso destacado; 9. considerações e assinatura.
- Relatório impresso; vista em PNG, SVG, PDF vetorial com camadas e DXF com camadas.

## 7. Importa / exporta ✓
- Arquivo do modelo `.gfx` = **JSON legível** (`gtool3d-model/2`, descrito no Apêndice B do manual).
- Importa unifilar do AutoCAD em **DXF** (LINE, LWPOLYLINE, POLYLINE, INSERT aninhado): escolha de camadas, tolerância para juntar nós (5 mm), divide vigas sobre pilares, unidade automática pelo `$INSUNITS`.
- Exporta DXF (geometria), CSV de nós e barras (`;`, UTF-8 com BOM para o Excel em português). Não importa de outros programas de análise.

## 8. API / automação
- Não tem API. O `.gfx` em JSON permite gerar ou ler modelos por fora (a confirmar se o termo de uso permite).

## 9. Pré-moldados
- Nada específico: sem fases construtivas, sem ligações semirrígidas (só molas e rótulas), sem consolos.

## Avaliação
| Critério | Nota | Comentário |
|---|---|---|
| Aderência | 2 | análise de pórtico genérica; útil para conferência, não para pré-moldado |
| Integração | 3 | JSON legível + DXF; sem API |
| Licença | 1 | comercial e restritiva; serve só como referência |
| Documentação | 5 | manual completo, com limitações, validação e testes de tamanho declarados |

## O que aproveitar para o nosso programa
- **Formato do memorial**: seções numeradas, fórmula → "Onde:" → números substituídos → pior caso destacado. Junto com o da RH Engenharia (`outros/rh-blocos-estacas.md`), é um bom modelo para o memorial do PCEB.
- **Casos-teste de charneiras** (Apêndice C): laje 4×4 m com 10 kN/m² → 160 kN, 40 kN por viga; laje 6×4 m com 5 kN/m² → 40 kN nas vigas longas e 20 kN nas curtas. Servem para `03_casos-teste`.
- **Importação de unifilar DXF** (camadas, tolerância, divisão nos nós): ideia para levar desenhos do DEB.Cad ao modelo de cálculo.
- **Roteiro de teste com engenheiros** (2 h: conta manual × programa, tolerância de 1 %, questionário): modelo para validar o PCEB com o escritório.
- **Limites e precisões declarados** (envoltória com 16 trechos por barra, grelha × MEF, tamanho máximo medido): boa prática de transparência.
- Para a decisão D19 (detalhamento primeiro), a contribuição é pequena: interessa mais quando chegarmos à análise.

## A confirmar
- **Edição da NBR 6118**: o programa cita "NBR 6118:2026 (quinta edição, 11/03/2026)". Na biblioteca do usuário está a **NBR 6118:2023 com a Emenda 1 de 11/03/2026**, então deve ser a mesma norma com outro nome. Conferir.
- Contradições no manual: C.3 diz que a NBR 6123 "ainda não é aplicada", mas o histórico (v0.8.0 e v0.9.0) e o memorial mostram o gerador de vento funcionando; o termo diz 30 dias de avaliação e o histórico da v0.8.0 diz 15 dias.
- O `.exe` foi compilado no Linux e, pelo próprio manual, "ainda não foi executado em um Windows real".
- Preço da licença comercial (ainda não definido pelo autor).
