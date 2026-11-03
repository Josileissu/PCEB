"""Prévia de um template sem o CAD (checagem de 2026-10-30).

Uso:
  set ARM_PREVIA=<pasta>  &&  dotnet test tests\\DEB.Core.Tests --filter GravarPreviaDoPilar
  python -E -P tools/previa_template.py <pasta>\\previa_pilar.json <saida.png>

Textos viram retângulos do tamanho aproximado (largura ~ 0,8·h por caractere), para ver ocupação e vazios.
"""
import json
import math
import sys

import matplotlib

matplotlib.use("Agg")
import matplotlib.pyplot as plt  # noqa: E402
from matplotlib.patches import Circle, Polygon  # noqa: E402

dados = json.load(open(sys.argv[1], encoding="utf-8"))
fig, ax = plt.subplots(figsize=(24, 16), dpi=110)
ax.set_facecolor("black")
fig.patch.set_facecolor("black")


def linha(pts, cor, lw=0.5):
    if len(pts) >= 2:
        ax.plot([p[0] for p in pts], [p[1] for p in pts], color=cor, lw=lw)


cores = {"ARM-PILAR-SECAO": "#ff40ff", "ARM-CHAVE": "#ff4040", "ARM-BARRA-DETALHE": "#ff4040", "ARM-BARRA-2": "#4080ff", "ARM-PINO": "#ff4040"}
for c, l in zip(dados["contornos"], dados.get("layers", [""] * len(dados["contornos"]))):
    linha(c, cores.get(l, "#bbbbbb"))
for b in dados["barras"]:
    linha(b, "#ff4040", 0.8)
for x, y, r in dados["circulos"]:
    ax.add_patch(Circle((x, y), r, fill=False, color="#ffff60", lw=0.4))
for p1, p2, pos in dados["cotas"]:
    linha([p1, p2], "#40c0ff", 0.3)
    linha([p1, pos], "#205070", 0.2)
for p1, p2, pos, refs in dados["faixas"]:
    if refs:
        for rf in refs:
            linha([pos, rf], "#40ff40", 0.3)
    else:
        linha([p1, p2], "#40ff40", 0.5)
for t in dados["textos"]:
    w = 0.8 * t["h"] * max(1, len(t["t"]))
    h = t["h"]
    dx = {0: 0, 1: -w / 2, 2: -w}[t["al"]]
    a = t["a"]
    ca, sa = math.cos(a), math.sin(a)
    cantos = [(dx, 0), (dx + w, 0), (dx + w, h), (dx, h)]
    pts = [(t["x"] + u * ca - v * sa, t["y"] + u * sa + v * ca) for u, v in cantos]
    ax.add_patch(Polygon(pts, closed=True, fill=True, color="white", alpha=0.35, lw=0))
for x, y, w, h in dados["tabelas"]:
    ax.add_patch(Polygon([(x, y), (x + w, y), (x + w, y - h), (x, y - h)], closed=True, fill=False, color="cyan", lw=0.6))
if dados.get("ra"):
    x, y = dados["ra"]
    ax.add_patch(Polygon([(x, y), (x + 250, y), (x + 250, y - 600), (x, y - 600)], closed=True, fill=False, color="magenta", lw=0.6, ls="--"))
ax.set_aspect("equal")
ax.autoscale()
if len(sys.argv) >= 7:  # recorte opcional: x0 y0 x1 y1
    x0, y0, x1, y1 = map(float, sys.argv[3:7])
    ax.set_xlim(x0, x1)
    ax.set_ylim(y0, y1)
ax.tick_params(colors="gray")
plt.savefig(sys.argv[2], bbox_inches="tight", facecolor="black")
xs = [p[0] for c in dados["contornos"] for p in c] + [t["x"] for t in dados["textos"]]
ys = [p[1] for c in dados["contornos"] for p in c] + [t["y"] for t in dados["textos"]]
print(f"extensão: x {min(xs):.0f}..{max(xs):.0f}  y {min(ys):.0f}..{max(ys):.0f}")
