# Domínio do PCEB — recomendação e configuração

Verificado em **03/11/2026**. Preços e disponibilidade mudam; confirme no registrador antes de pagar.

## 1. Recomendação

| Domínio | Papel | Registrador | Custo aproximado |
|---|---|---|---|
| **pceb.com.br** | endereço principal, site e e-mail | [Registro.br](https://registro.br) | **R$ 40/ano** (registro e renovação) |
| **pceb.app** | redireciona para o principal | Cloudflare Registrar (ou Porkbun) | **US$ 14,20/ano** (Cloudflare, a preço de custo) |
| **pceb.dev** | documentação / área técnica | Cloudflare Registrar (ou Porkbun) | **US$ 12,20/ano** (Cloudflare, a preço de custo) |

Alternativas: no Porkbun, `.app` e `.dev` saem por US$ 8,75 no primeiro ano e US$ 14,93 / US$ 12,87
na renovação. O `.com` (`pceb.com`) está registrado desde 2000 e não está à venda; não conte com ele.

Custo total do conjunto: **cerca de R$ 180 a R$ 200 por ano** (R$ 40 + ~US$ 26,40).

## 2. Ordem de compra

1. Registre `pceb.com.br` no Registro.br (CPF/CNPJ, até 10 anos de uma vez).
2. Crie uma conta no Cloudflare e adicione a zona `pceb.com.br`.
3. Troque os servidores DNS no Registro.br pelos dois nameservers que o Cloudflare indicar
   (formato `ada.ns.cloudflare.com` / `bob.ns.cloudflare.com`; os nomes são sorteados por conta).
4. Registre `pceb.app` e `pceb.dev` no Cloudflare Registrar. Como já estarão na mesma conta,
   o DNS é criado automaticamente.
5. Ative **DNSSEC** nas três zonas no Cloudflare. No `.com.br`, copie o DS gerado para o
   Registro.br (painel do domínio → DNSSEC).

## 3. Endereço canônico

Adote **`https://pceb.com.br`** (sem `www`) como endereço oficial. Todo o resto redireciona
para ele com **301**, preservando caminho e query string:

| Origem | Destino |
|---|---|
| `https://www.pceb.com.br/*` | `https://pceb.com.br/*` |
| `https://pceb.app/*` | `https://pceb.com.br/*` |
| `https://pceb.dev/*` | `https://pceb.com.br/*` |

### Regras no Cloudflare (Rules → Redirect Rules)

Crie uma regra para cada origem, com "Preserve query string" ligado:

- **Nome:** `www → apex`
  - Expressão: `http.host eq "www.pceb.com.br"`
  - Destino dinâmico: `concat("https://pceb.com.br", http.request.uri.path)`
  - Status: `301`
- **Nome:** `app → principal`
  - Expressão: `http.host eq "pceb.app"`
  - Destino dinâmico: `concat("https://pceb.com.br", http.request.uri.path)`
  - Status: `301`
- **Nome:** `dev → principal`
  - Expressão: `http.host eq "pceb.dev"`
  - Destino dinâmico: `concat("https://pceb.com.br", http.request.uri.path)`
  - Status: `301`

`.app` e `.dev` estão na lista HSTS preload: só respondem por HTTPS. O certificado é emitido
pelo próprio Cloudflare (Universal SSL), sem custo.

Se preferir manter a documentação em `pceb.dev`, troque a última regra por um proxy para
`docs.pceb.com.br` ou sirva o site de docs direto nesse host.

## 4. Apontamento do site

Recomendado: **Cloudflare Pages** (gratuito, deploy pelo GitHub, HTTPS automático).
No projeto Pages, adicione o domínio personalizado `pceb.com.br` e `www.pceb.com.br`; o
Cloudflare cria os registros:

| Tipo | Nome | Conteúdo | Proxy |
|---|---|---|---|
| CNAME | `@` | `<projeto>.pages.dev` | Proxied |
| CNAME | `www` | `<projeto>.pages.dev` | Proxied |

Alternativa com GitHub Pages (o repositório é `Josileissu/PCEB`):

| Tipo | Nome | Conteúdo |
|---|---|---|
| A | `@` | `185.199.108.153` |
| A | `@` | `185.199.109.153` |
| A | `@` | `185.199.110.153` |
| A | `@` | `185.199.111.153` |
| CNAME | `www` | `josileissu.github.io` |

Nesse caso, crie o arquivo `CNAME` com `pceb.com.br` na raiz do site publicado e mantenha o
proxy do Cloudflare **desligado** (nuvem cinza) até o certificado do GitHub Pages ser emitido;
depois pode religar.

## 5. E-mail

Opção gratuita e simples: **Cloudflare Email Routing** — encaminha `contato@pceb.com.br`,
`suporte@pceb.com.br` e `dev@pceb.com.br` para um Gmail. O Cloudflare cria os MX e o SPF
automaticamente.

Para caixa de saída com o domínio próprio, use **Zoho Mail** (plano gratuito) ou Google Workspace:

| Tipo | Nome | Conteúdo | Prioridade |
|---|---|---|---|
| MX | `@` | `mx.zoho.com` | 10 |
| MX | `@` | `mx2.zoho.com` | 20 |
| MX | `@` | `mx3.zoho.com` | 50 |
| TXT | `@` | `v=spf1 include:zoho.com ~all` | — |
| TXT | `_dmarc` | `v=DMARC1; p=none; rua=mailto:dmarc@pceb.com.br` | — |

Adicione também o DKIM fornecido pelo provedor (TXT em `zoho._domainkey` ou equivalente).

## 6. Segurança e acabamento

- **DNSSEC** ativo nas três zonas.
- **HSTS** no `.com.br` depois que o HTTPS estiver estável (mínimo 6 meses, `includeSubDomains`).
- **CAA** (opcional): `0 issue "letsencrypt.org"` e `0 issue "pki.goog"` para limitar quem
  pode emitir certificado.
- **Redirects** sempre 301 e sem cadeia (`http → https` é feito pelo Cloudflare).
- **Registro.br**: ative renovação automática e mantenha o e-mail de contato atualizado.

## 7. Checklist

- [ ] Registrar `pceb.com.br` no Registro.br
- [ ] Criar conta no Cloudflare e adicionar as três zonas
- [ ] Trocar os nameservers no Registro.br
- [ ] Registrar `pceb.app` e `pceb.dev`
- [ ] Apontar o site (Cloudflare Pages ou GitHub Pages)
- [ ] Criar as três regras de redirecionamento 301
- [ ] Configurar e-mail (Email Routing ou Zoho)
- [ ] Ativar DNSSEC (e copiar o DS para o Registro.br)
- [ ] Ativar HSTS e CAA
- [ ] Testar `http://`, `https://`, `www`, `.app` e `.dev`

> A aplicação dessas regras exige acesso às contas do Registro.br e do Cloudflare. O script
> `tools/autoteste*.ps1` não tem relação com isto; nenhuma credencial deve ser gravada no
> repositório.
