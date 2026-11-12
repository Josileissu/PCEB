# Métricas do PCEB

Pedido do usuário (11/11/2026): saber quantos acessos o site tem e quantos downloads foram feitos, sem precisar estar no site.

## Downloads dos instaladores
- **Agora:** `powershell -NoProfile -File tools\metricas.ps1` — downloads de cada release (CAD e Revit), totais e as visitas ao
  repositório no GitHub dos últimos 14 dias (visualizações, clones e de onde vieram). Precisa do `gh` logado na conta dona do repositório.
- **Histórico:** o GitHub só informa o total acumulado de cada arquivo. O workflow `.github/workflows/metricas.yml` grava uma vez por
  dia (06:17, horário de Brasília) o total de cada instalador na branch `metricas`, arquivo `downloads.csv`
  (`data;programa;versao;arquivo;downloads`). A diferença entre dois dias é o número de downloads no período.
  Rodar na hora: Actions → "Métricas de download" → Run workflow (ou `gh workflow run metricas.yml`).

## Visitas ao site (www.pceb.com.br)
LIGADO em 12/11/2026: painel em **https://josileissu.goatcounter.com** (conta do usuário). O GitHub Pages não conta visitas. A escolha foi um contador sem cookies (o site promete "sem cadastro, cookies ou formulário"):
**GoatCounter** (gratuito para uso não comercial, código aberto, sem cookies, não precisa de aviso de privacidade). Para ligar:
1. O usuário cria a conta em goatcounter.com com o código do site (por exemplo `pceb` → painel em `https://pceb.goatcounter.com`).
2. Acrescentar em todas as páginas do site, antes de `</body>`:
   `<script data-goatcounter="https://<código>.goatcounter.com/count" async src="//gc.zgo.at/count.js"></script>`
3. Os botões "Baixar para CAD" e "Baixar para Revit" já têm `data-goatcounter-click` (`baixar-cad`, `baixar-revit`): com o script,
   cada clique aparece no painel como um evento.
O painel mostra visitas por página e por dia, de onde vieram, país, navegador e tela.
