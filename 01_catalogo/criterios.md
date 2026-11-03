# Critérios do catálogo

## Ficha de cada programa (mesmos títulos em todas)
1. Identificação: fabricante, licença, plataforma, instalado aqui?
2. Modelagem
3. Análise
4. Dimensionamento (normas)
5. Detalhamento
6. Fabricação / CAM
7. Documentação e desenho
8. Importa / exporta
9. API / automação
10. Pré-moldados
11. O que aproveitar para o nosso programa
12. A confirmar

**Confiança da informação**: ✓ = conferido neste computador; sem marca = conhecimento geral, ainda
não conferido (pode estar desatualizado). Ao lapidar, cada item vira ✓ ou é corrigido.

## Avaliação dos open source (nota 1 a 5, provisória)
| Critério | Pergunta |
|---|---|
| Maturidade | É usado em projetos reais / pesquisa há anos? |
| Manutenção | Recebe atualizações? Tem comunidade ativa? |
| Documentação | Dá para aprender sem ler o código? |
| Integração | Como conversa conosco: arquivo de texto, Python, .NET, executável? |
| Aderência | Serve para pré-moldado (peças, consolos, fases, NBR)? |
| Licença | Permite uso no escritório? E embutir num programa nosso que talvez seja distribuído? |

### Sobre licenças (resumo para decidir depois)
- **MIT / BSD / Apache**: pode embutir e distribuir, mantendo o aviso de copyright.
- **LGPL**: pode usar como biblioteca separada (DLL) sem abrir o nosso código.
- **GPL**: usar como **programa externo** (chamar o executável, trocar arquivos) não obriga a abrir o nosso código;
  **embutir/ligar** como biblioteca obriga a distribuir o conjunto como GPL.
- **Licença acadêmica / não comercial** (caso do OpenSees): uso por empresa precisa ser verificado com o detentor.
