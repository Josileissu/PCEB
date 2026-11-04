# RH Engenharia – "TQS – Bloco de Estacas 3D + Memorial" (página HTML, v2)

| Origem | Licença | Linguagem | Instalado aqui |
|---|---|---|---|
| RH Engenharia (autor/origem a registrar) — não é produto da TQS, apesar do nome | **a registrar** (material de terceiros) | página única HTML + JavaScript; three.js r128 e mp4-muxer por CDN ✓ | arquivo em `programas/blocos-estacas-rh-engenharia/download` |

Material de terceiros: estudar, **não copiar código** nem publicar sem autorização.

## Funções (conferidas no arquivo ✓)
- **Modelagem**: estaqueamento em **malha retangular** (colunas × fileiras, espaçamento, diâmetro, margem, altura do bloco);
  vários pilares com posição, seção e N, Mx, My.
- **Análise**: método clássico de bloco rígido (Alonso; Velloso & Lopes) — estacas verticais de mesma rigidez, ligação
  rotulada, reação linear R = N/n ± My·x/Σx² ± Mx·y/Σy²; peso próprio do bloco (NBR 6120, 2,5 tf/m³); transporte das
  cargas ao CG; esforços globais V e M no bloco; **análise por grelha (elementos finitos)** como comparação;
  **locação real (as-built)** — recalcula com as estacas onde foram executadas.
- **Verificações**: equilíbrio (ΣR = N, momentos), pilares dentro do bloco, sem tração, R ≤ carga admissível.
- **Dimensionamento**: não — bielas e tirantes "deve ser feito separadamente" (dito no memorial). Cita NBR 6118 e 6122.
- **Documentação**: memorial de cálculo passo a passo ("de onde vem" cada fórmula), tabela de reações, diagramas, figuras.
- **Importa / exporta**: **IFC** (exporta e importa), DXF, PDF; vídeo MP4 do modelo 3D.
- **Unidades**: tf, tf·m, m.

## Avaliação
| Critério | Nota | Comentário |
|---|---|---|
| Aderência | 4 | exatamente o tipo de ferramenta de fundações que o PCEB terá (ordem: pré-moldado → fundações) |
| Integração | 3 | roda em qualquer navegador; IFC de ida e volta |
| Licença | ? | desconhecida |

## O que aproveitar para o nosso programa
- **Formato do memorial**: hipótese → fórmula → "de onde vem" → números → conferência por fórmula fechada. Bom padrão para o PCEB.
- Comparação **método simplificado × grelha** e **projeto × as-built** no mesmo relatório.
- Exemplo de ferramenta web leve (uma página) com 3D e IFC — referência para a versão web do PCEB.

## A confirmar
- Autor, origem e licença; estacas inclinadas e malhas não retangulares (parece só retangular).
