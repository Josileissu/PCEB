# Critérios do catálogo

## Tabela de programas
Cada programa estudado é uma linha de `programas.md`: tipo e licença, onde está e, na observação, só o que foi
**conferido** num dos computadores (API, normas atendidas, o que serve ao PCEB). Conhecimento geral não conferido não entra.

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
