# Site do PCEB

Site institucional estático do **PCEB — Programa de Cálculo Estrutural Brasileiro** e do
**DEB — Detalhador Estrutural Brasileiro**.

- HTML, CSS e JavaScript puros; sem dependências e sem etapa de build.
- Links relativos entre páginas, para funcionar na raiz ou em subdiretório.
- Conteúdo em português do Brasil, com foco em engenheiros calculistas e escritórios de projeto.
- Fontes self-hosted em `assets/fonts/` (Archivo, Inter e IBM Plex Mono).

## Páginas

| Arquivo | Conteúdo |
|---|---|
| `index.html` | Home: visão do PCEB, módulo DEB, relação do aço, prova técnica, roteiro e FAQ |
| `deb.html` | DEB: problema, recursos, DEB.Cad, DEB.Revit, compatibilidade e comandos |
| `documentacao.html` | Documentos, instalação no CAD e no Revit, comandos, formatos, normas e FAQ |
| `baixar.html` | Versão, requisitos, compilação, instalador, demonstração e licença |
| `projeto.html` | Origem, princípios, roteiro, código aberto e autor |
| `404.html` | Página não encontrada |

Também fazem parte do site: `sitemap.xml`, `robots.txt`, `assets/img/favicon.svg` e
`assets/img/og.png`.

## Visualizar localmente

Não precisa de Node, npm ou build. Abra `index.html` no navegador ou sirva a pasta:

```powershell
# Python 3
python -m http.server 8000

# ou, se tiver Node
npx serve .
```

Depois acesse `http://localhost:8000`.

Para testar o comportamento de 404 no GitHub Pages/Cloudflare Pages, publique a pasta ou use
um servidor local que respeite `404.html`.

## Publicar

### No ar hoje: GitHub Pages pelo workflow

`.github/workflows/site.yml` publica esta pasta em https://www.pceb.com.br/ a cada envio para a `main`
que mexa em `site/**` (ou manualmente em Actions → "Publicar site"). Domínio próprio desde 10/11/2026: `site/CNAME`
e o domínio personalizado do Pages (Settings → Pages) = `www.pceb.com.br`. DNS no registro.br: `www` CNAME
`josileissu.github.io`; raiz `pceb.com.br` com os A 185.199.108.153/109/110/111.153 (o GitHub redireciona para o www).

O botão "Baixar instalador" (`baixar.html`) e os textos `data-release` são preenchidos por `assets/js/main.js`
com a Release mais recente (API do GitHub). Sem a API, o botão abre `releases/latest`.

### Cloudflare Pages (alternativa)

1. Conecte o repositório `Josileissu/PCEB` (ou envie esta pasta).
2. Build command: **vazio**.
3. Output directory: **`site`** (a pasta que contém `index.html`).
4. Adicione o domínio `pceb.com.br` em **Custom domains**.

### GitHub Pages

1. Se o site ficar na raiz do repositório, habilite Pages em **Settings → Pages**.
2. Se ficar em uma pasta (por exemplo `site/`), publique a partir dela.
3. Use o domínio `pceb.com.br` conforme `docs/DOMINIO.md` do projeto principal.

O arquivo `.nojekyll` já está incluído para o GitHub Pages não processar a pasta com Jekyll.

## Estrutura

```
site/
  BRIEF.md              briefing original (histórico de trabalho)
  README.md             este documento
  index.html
  deb.html
  documentacao.html
  baixar.html
  projeto.html
  404.html
  sitemap.xml
  robots.txt
  .nojekyll
  _fonts.ps1            script que baixou as fontes (histórico)
  _validate.py          checagem de HTML, links, IDs, sitemap e assets
  assets/
    css/style.css
    js/main.js
    fonts/*.woff2
    img/favicon.svg
    img/og.png
```

Para revalidar depois de editar:

```powershell
python _validate.py
```

## Regras de conteúdo

O site não deve prometer o que ainda não existe:

- O PCEB **não calcula** estruturas ainda; o módulo de cálculo está em desenvolvimento.
- O DEB **detalha, numera e quantifica**; não dimensiona.
- Não afirmar homologação por norma, integração pronta com Eberick/TQS/Robot ou datas de lançamento.
- O download público do instalador ainda não tem canal definido; a página de download explica
  como compilar do repositório.

Ao atualizar a versão do plugin ou o número de testes, revise:

- `index.html` (hero, prova técnica e FAQ);
- `deb.html` (versão e compatibilidade);
- `baixar.html` (versão, requisitos e nome do instalador);
- `projeto.html` (módulo e status);
- `sitemap.xml` (datas `lastmod`).

## Observação sobre o nome

O briefing (`BRIEF.md`) foi escrito quando o módulo ainda se chamava **Armadura BR**. Em
04/11/2026 o repositório passou a usar **DEB — Detalhador Estrutural Brasileiro** (CAD e Revit),
e o site foi concluído com esse nome, incluindo a página `deb.html`. O briefing permanece como
registro da decisão original.
