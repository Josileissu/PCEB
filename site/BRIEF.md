# PCEB — Briefing refinado do site

**Status:** documento de trabalho · 03/11/2026
**Fonte:** `armadura/README.md`, `armadura/docs/*`, `armadura/CLAUDE.md`, `armadura/info_auxiliar/prompt_port_editor_armaduras_gstarcad_autocad.txt`
**Projeto do site:** `pceb-site/` (separado do plugin; ver "Como unir no futuro")

> **Nota de 04/11/2026 — documento histórico.** O repositório foi atualizado e o módulo
> **Armadura BR** passou a se chamar **DEB — Detalhador Estrutural Brasileiro**, com versões
> para CAD e Revit. O site foi concluído com o nome atual e a página `deb.html`; este briefing
> permanece como registro da decisão original. Consulte `README.md` para o estado final.

---

## 1. O que o site precisa resolver

O PCEB é o **programa brasileiro de cálculo estrutural** em construção. O que já existe e pode ser
usado é o **Armadura BR**, o módulo de detalhamento, numeração e quantificação de armaduras dentro
do CAD. O site tem três trabalhos, nesta ordem:

1. **Explicar o PCEB** — qual é a ambição, o que está pronto e o que vem depois, sem prometer o que
   ainda não existe.
2. **Apresentar o Armadura BR** — a dor real que ele resolve (detalhar, renumerar e fechar a relação
   do aço sem retrabalho) e a diferença técnica (entidades nativas, RA viva, norma brasileira).
3. **Levar à ação** — documentação, download/instalação e contato.

### Público

- Engenheiros calculistas e projetistas de estruturas de concreto armado no Brasil.
- Escritórios de projeto que detalham em GstarCAD, AutoCAD ou ZWCAD.
- Desenhistas/projetistas que hoje mantêm a relação do aço manualmente.
- Avaliadores técnicos (privacidade, licença, compatibilidade, confiabilidade).

### Proposta de valor (uma frase)

> **O desenho de armadura deixa de ser linha e texto soltos: vira um objeto vivo, com norma
> brasileira, numeração e relação do aço que se atualizam sozinhas dentro do CAD que você já usa.**

Para o PCEB como programa:

> **Cálculo e detalhamento na mesma direção — começando pelo detalhamento que já está em uso.**

---

## 2. Fatos verificados (usar apenas isto)

| Fato | Fonte |
|---|---|
| Nome do projeto/repositório: **PCEB** | `README.md`, `docs/DOMINIO.md` |
| Módulo disponível: **Armadura BR 0.1.25** | `README.md` |
| Plugin .NET de detalhamento, numeração e quantificação de armaduras | `README.md` |
| **Não dimensiona estruturas**; decisões de cálculo são do engenheiro | `README.md`, especificação |
| GstarCAD 2021–2027, AutoCAD 2021–2027, ZWCAD 2024–2026 | `README.md` |
| Entidades nativas (`LWPOLYLINE`, `MTEXT`, `TEXT`, `TABLE`) + XData `ARMADURA_BR` | `README.md`, `docs/ARQUITETURA.md` |
| O DWG abre legível sem o plugin (perde só as atualizações automáticas) | `README.md` |
| Barras, estribos, treliças, telas, cordoalhas CP-190 | `README.md` |
| Faixas, cortes, chamadas e indicações | `README.md` |
| Relação do aço com resumo, peso, detalhe de dobra, numeração global/individual/sequencial, bloqueio e RA parcial | `README.md` |
| Lista de corte e dobra em CSV + HTML | `README.md` |
| Detalhamentos como blocos do CAD | `README.md` |
| Importação de `.cad` do QiArmaduras (AltoQi Builder) | `README.md`, `docs/FORMATO_QICAD.md` |
| Templates de elementos; base de reservatório e pilar pré-moldado implementados | `docs/TEMPLATES.md`, `docs/PROGRESSO.md` |
| Norma de referência do detalhamento: NBR 6118 (pinos/dobramento) e NBR 9062 (pré-moldados) | `docs/*`, `CLAUDE.md` |
| 178 testes de núcleo; autoteste no CAD com 200+ verificações | `README.md`, `docs/PROGRESSO.md` |
| Licença GPL-3.0 + exceção da seção 7 para APIs de CAD | `README.md`, `LICENSE*` |
| Autor: Marquezin · GitHub: `Josileissu/PCEB` | `README.md` |
| Domínio planejado: `pceb.com.br` (+ `.app`/`.dev` redirecionando) | `docs/DOMINIO.md` |

### O que NÃO afirmar

- ❌ Que o PCEB já calcula/dimensiona estruturas. **Ainda não** — o módulo de cálculo está em
  desenvolvimento e o escopo não foi fechado.
- ❌ Que o Armadura substitui o software de cálculo. Ele **detalha, numera e quantifica**.
- ❌ Que há integração pronta com Eberick/TQS/Robot/Revit. A importação existente é de `.cad` do
  QiArmaduras.
- ❌ Datas de lançamento, preços, número de clientes, depoimentos ou logos de clientes.
- ❌ Que o download público já está publicado. O instalador existe no repositório, mas o canal de
  distribuição ainda será definido (ver pendências).
- ❌ "Homologado" por norma. As tabelas seguem a NBR 6118, mas a responsabilidade é do engenheiro.

### Tom de voz

Técnico, direto, brasileiro, sem hype. Frases curtas. Nada de "revolucione", "disruptivo",
"a melhor ferramenta do mercado". A credibilidade vem de detalhes concretos (cm, NBR, entidades
nativas, XData, testes, licença).

---

## 3. Arquitetura da informação

```
/                     Home — visão PCEB + módulo Armadura + prova técnica + CTA
/armadura.html        Módulo Armadura BR — recursos, fluxo, comandos, compatibilidade
/documentacao.html    Documentação — instalação, comandos, formatos, normas, FAQ
/baixar.html          Download — versão, requisitos, passos, demo, licença
/projeto.html         Projeto — origem, princípios, roteiro, código aberto, contato
/404.html             Página não encontrada
```

Navegação principal: **Armadura · Documentação · Projeto · Baixar** (CTA em destaque).
Na home, âncoras para módulos, recursos, compatibilidade e roteiro.

### Conteúdo por página

**Home**
1. Hero: "Cálculo estrutural com padrão brasileiro." + subtítulo PCEB/Armadura + CTAs.
2. Faixa de compatibilidade (GstarCAD, AutoCAD, ZWCAD) + "DWG legível sem o plugin".
3. Manifesto: um programa, duas frentes — detalhamento disponível / cálculo em desenvolvimento.
4. Módulo em destaque: Armadura BR, com 4 passos do fluxo.
5. Vitrine da relação do aço (painel escuro com tabela técnica).
6. Grade de recursos (barras, estribos, faixas, RA, blocos, templates/importação).
7. Prova técnica (entidades nativas, XData, cm, NBR 6118, testes) + aviso "não dimensiona".
8. Roteiro (3 etapas, sem datas).
9. FAQ curto (5 perguntas).
10. CTA final para download/documentação.

**Armadura**
- Hero do módulo, o problema, o que muda, fluxo detalhado, recursos por grupo, compatibilidade,
  trecho de comandos essenciais, aviso de responsabilidade, CTA.

**Documentação**
- Cartões para os documentos do repositório, instalação em 3 passos, tabela de comandos por grupo,
  formatos (XData, .cad QiArmaduras, CSV/HTML), normas e FAQ.

**Baixar**
- Versão atual, requisitos, compatibilidade, passos de instalação, demo de 1 hora, licença,
  aviso de distribuição (enquanto o canal público não estiver definido).

**Projeto**
- O nome, a origem, princípios (brasileiro, aberto, nativo no CAD, norma no centro), roteiro,
  código aberto/licença, autor/contato.

---

## 4. Direção visual

**Conceito:** prancha de detalhamento + precisão editorial. Não é um site de startup de IA; é
ferramenta de engenharia. A referência visual é a própria prancha: margens, carimbo, cotas,
numeração, tabela de aço — traduzidas para a web.

- **Cores:** tinta quase-preta azulada (`#0E171C`), papel quente (`#F6F3EC`), aço/azul profundo
  (`#1F4A5C`), acento óxido/laranja (`#C6522C`), areia (`#C9B48A`).
- **Tipografia:** Archivo (títulos, grotesk firme), Inter (texto), IBM Plex Mono (rótulos,
  números, comandos). Fontes self-hosted em woff2.
- **Motivos:** grade de prancha, linhas de cota com ticks, carimbo, etiquetas `N1`, `01/04`,
  círculos de armadura, tabela de aço.
- **Ilustração principal:** SVG autoral de uma prancha de viga (elevação, seção A-A e relação do
  aço), com traço animado discreto. Nada de banco de imagens.
- **Movimento:** revelação no scroll, traço do SVG e hover de cartões. Respeitar
  `prefers-reduced-motion`. Sem parallax pesado, sem cursor customizado.
- **Responsivo:** 360 px → 1440 px+. Menu móvel acessível.
- **Acessibilidade:** contraste AA, foco visível, landmarks, skip link, `details/summary` no FAQ,
  textos alternativos/`aria-hidden` nos SVGs decorativos.

---

## 5. Requisitos técnicos

- **HTML + CSS + JavaScript puro**, sem dependências e sem etapa de build. Saída estática pronta
  para Cloudflare Pages/GitHub Pages (recomendação em `docs/DOMINIO.md`).
- CSS único (`assets/css/style.css`) com design tokens; JS único e progressivo.
- Links relativos entre páginas para funcionar em subdiretório do GitHub Pages.
- SEO por página: `title`, `description`, canonical `https://pceb.com.br/...`, Open Graph,
  Twitter, JSON-LD `SoftwareApplication` na home.
- `sitemap.xml`, `robots.txt`, favicon SVG e imagem social.
- Sem cookies, analytics ou formulário com backend. Contato por e-mail/link para o repositório.
- Conteúdo em português do Brasil.

---

## 6. Estrutura de arquivos

```
pceb-site/
  BRIEF.md              este documento
  README.md             como visualizar, publicar e unir ao repositório principal
  index.html
  armadura.html
  documentacao.html
  baixar.html
  projeto.html
  404.html
  sitemap.xml
  robots.txt
  assets/
    css/style.css
    js/main.js
    fonts/*.woff2
    img/favicon.svg
    img/og.png
```

---

## 7. Como unir no futuro

O site é independente por decisão: não altera o plugin, não tem build acoplado e não presume
publicação. Para unir:

1. Copiar `pceb-site/` para `armadura/site/` (ou para a raiz do repositório PCEB).
2. Ajustar os links de documentação de `https://github.com/Josileissu/PCEB/blob/main/docs/...`
   para caminhos relativos, se o site passar a ser publicado do mesmo repositório.
3. Publicar a pasta como site estático (Cloudflare Pages ou GitHub Pages) com o domínio
   `pceb.com.br`, conforme `docs/DOMINIO.md`.
4. Se o site virar o site oficial do repositório, mover `BRIEF.md` para `docs/SITE.md`.

Nada no site depende do nome da pasta, então a cópia pode ser feita a qualquer momento.

---

## 8. Pendências para o responsável

| # | Pendência | Impacto |
|---|---|---|
| 1 | Definir o escopo público do módulo de cálculo (o que será dimensionado e quando) | texto da home/projeto |
| 2 | Definir canal de distribuição do instalador (GitHub Releases? site? e-mail?) | CTA de download |
| 3 | Criar `contato@pceb.com.br` (Email Routing) ou indicar e-mail público | contato |
| 4 | Confirmar se o repositório `Josileissu/PCEB` será público | links de docs/código |
| 5 | Registrar `pceb.com.br` e configurar Pages/DNS | publicação |
| 6 | Autorizar uso do nome do autor e de capturas reais do plugin | credibilidade |
| 7 | Validar os textos com o engenheiro responsável (principalmente NBR 6118/9062) | conformidade |

Enquanto 1–4 não forem resolvidos, o site usa textos de "em desenvolvimento" e "distribuição em
preparação", sem prometer cálculo nem download que ainda não existem.
