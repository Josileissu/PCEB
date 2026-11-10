"""Extrai os detalhes padrão do DWG de referência (P56, já em DXF) para DXFs novos e simples, um por detalhe.

Uso: python -I extrair.py <DD.dxf> <pasta de saída>
Cada DXF sai em cm (desenho na escala do modelo), origem no canto inferior esquerdo, com layers padronizados:
  PCEB-ARMADURA (barras, vermelho), PCEB-CONTORNO (forma), PCEB-FINA, PCEB-TRACEJADA, PCEB-TEXTO, PCEB-COTA, PCEB-PREENCHIMENTO.
A escala da legenda vai no nome do bloco de informações (comentário 999) e no arquivo detalhes.json.
"""
import io
import json
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import dxf  # noqa: E402

sys.stdout.reconfigure(encoding='utf-8')


def vp(cx, cy, vh, w, h):
    vw = vh * w / h
    return (cx - vw / 2, cy - vh / 2, cx + vw / 2, cy + vh / 2)


DETALHES = [
    # nome da legenda, arquivo, espaço (0 modelo / 1 folha), janela (x0, y0, x1, y1), escala
    ('REFORÇO NOS FUROS DOS PILARES', 'reforco_furos', 0, vp(-15632, 7573, 712.5, 125, 142), 50),
    ('CHAVES DE CISALHAMENTO', 'chaves_cisalhamento', 0, vp(-16804, 7866, 69.1, 76, 69), 10),
    ('ESPAÇAMENTO DAS BARRAS', 'espacamento_barras', 0, vp(-16795, 7666, 40.4, 64, 81), 5),
    ('ALÇA DE IÇAMENTO', 'alca_icamento', 0, vp(-16791, 7455, 100.0, 36, 50), 20),
    # na folha (mm de papel): em 1:10, 1 mm de papel = 1 cm do modelo
    ('RAIO DE CURVATURA PARA GANCHOS', 'raio_curvatura', 1, (-470.0, 55.0, -384.0, 110.0), 10),
    ('COMPRIMENTO DAS BARRAS', 'comprimento_barras', 1, (-470.0, 12.0, -384.0, 55.0), 10),
    ('EMENDAS POR TRASPASSE', 'emendas_traspasse', 1, (-383.0, 12.0, -302.0, 110.0), 10),
    ('EMENDAS POR SOLDA POR TRASPASSE', 'emendas_solda', 1, (-302.0, 12.0, -186.0, 110.0), 10),
    # bloco inteiro (fora da área da folha no DWG de referência)
    ('ATERRAMENTO ESTRUTURAL PILARES', 'aterramento', 'DET_ATERRAMENTO', None, 10),
]

# layers da moldura/carimbo da folha: nunca entram nos detalhes
FOLHA = ('MARGEM', 'MARGEM2', 'G-ANNO-TTLB', 'G-ANNO-TTLB-THIN', 'G-ANNO-TTLB-WIDE', 'A-DETL-THIN')

COTA_LAYERS = ('DIM', 'COTAS', 'DT-COTAS', 'TEXTO DA COTA')


def classe(p):
    lay = p['layer'].upper()
    tipo = p['tipo'].upper()
    if p['kind'] == 'text':
        return 'PCEB-TEXTO'
    if p['kind'] in ('fill', 'solid'):
        return 'PCEB-PREENCHIMENTO'
    if lay in COTA_LAYERS:
        return 'PCEB-COTA'
    if any(k in tipo for k in ('DASH', 'HIDDEN', 'TRACEJ', 'CENTER', 'DOT', 'PHANTOM', 'ACAD_ISO')):
        return 'PCEB-TRACEJADA'
    if p['cor'] == 1 or 'ARMADURA' in lay or 'BARRAS' in lay:
        return 'PCEB-ARMADURA'
    if p['peso'] >= 35 or lay in ('DT-FORMA', 'PILARES', 'CONTORNO', 'A-DETL-1', 'FORMA'):
        return 'PCEB-CONTORNO'
    return 'PCEB-FINA'


def inside(pt, w, tol=0.0):
    return w[0] - tol <= pt[0] <= w[2] + tol and w[1] - tol <= pt[1] <= w[3] + tol


def clip(p0, p1, w):
    """Liang-Barsky: devolve o trecho dentro da janela ou None."""
    x0, y0 = p0
    dx, dy = p1[0] - x0, p1[1] - y0
    t0, t1 = 0.0, 1.0
    for pp, q in ((-dx, x0 - w[0]), (dx, w[2] - x0), (-dy, y0 - w[1]), (dy, w[3] - y0)):
        if abs(pp) < 1e-12:
            if q < 0:
                return None
            continue
        t = q / pp
        if pp < 0:
            t0 = max(t0, t)
        else:
            t1 = min(t1, t)
        if t0 > t1:
            return None
    return (x0 + t0 * dx, y0 + t0 * dy), (x0 + t1 * dx, y0 + t1 * dy)


def recortar(prims, w):
    out = []
    for p in prims:
        k = p['kind']
        if p['layer'].upper() in FOLHA:
            continue
        if k == 'line':
            c = clip(p['p'][0], p['p'][1], w)
            if c and math.dist(c[0], c[1]) > 1e-6:
                q = dict(p)
                q['p'] = list(c)
                out.append(q)
        elif k in ('circle', 'arc'):
            if inside(p['c'], w) and p['r'] < (w[2] - w[0]):
                out.append(p)
        elif k == 'text':
            if inside(p['p'], w) and p['s'].strip():
                out.append(p)
        elif k in ('solid', 'fill', 'arrow'):
            if all(inside(pt, w, 0.01 * (w[2] - w[0])) for pt in p['p']):
                out.append(p)
    return out


def bbox(prims):
    xs, ys = [], []
    for p in prims:
        if 'p' in p and p['kind'] != 'text':
            for pt in p['p']:
                xs.append(pt[0]); ys.append(pt[1])
        elif 'c' in p:
            xs += [p['c'][0] - p['r'], p['c'][0] + p['r']]; ys += [p['c'][1] - p['r'], p['c'][1] + p['r']]
        else:
            xs.append(p['p'][0]); ys.append(p['p'][1])
    return min(xs), min(ys), max(xs), max(ys)


def mover(prims, dx, dy):
    for p in prims:
        if 'p' in p:
            if p['kind'] == 'text':
                p['p'] = (p['p'][0] + dx, p['p'][1] + dy)
            else:
                p['p'] = [(a + dx, b + dy) for a, b in p['p']]
        if 'c' in p:
            p['c'] = (p['c'][0] + dx, p['c'][1] + dy)


# ------------------------------------------------------------------ escrita do DXF (R12, ASCII)

LAYERS = [('PCEB-ARMADURA', 1, 'CONTINUOUS'), ('PCEB-CONTORNO', 7, 'CONTINUOUS'), ('PCEB-FINA', 8, 'CONTINUOUS'),
          ('PCEB-TRACEJADA', 8, 'DASHED'), ('PCEB-TEXTO', 7, 'CONTINUOUS'), ('PCEB-COTA', 3, 'CONTINUOUS'),
          ('PCEB-PREENCHIMENTO', 7, 'CONTINUOUS')]


def quebrar(par, w, h):
    """Quebra um parágrafo do MTEXT na largura w (largura média do caractere Arial ~ 0,6 h)."""
    if w <= 0 or len(par) * 0.6 * h <= w:
        return [par]
    out, cur = [], ''
    for word in par.split(' '):
        t = (cur + ' ' + word) if cur else word
        if len(t) * 0.6 * h > w and cur:
            out.append(cur)
            cur = word
        else:
            cur = t
    out.append(cur)
    return out


def dxf_text(s):
    """Caracteres fora do cp1252 (sinais de maior/menor ou igual...) vão como U+XXXX com barra, que o AutoCAD e o plugin entendem."""
    return ''.join(c if ord(c) < 256 else '\\U+%04X' % ord(c) for c in s)


def fmt(v):
    return ('%.6f' % v).rstrip('0').rstrip('.') if abs(v) > 1e-9 else '0'


def escrever(path, prims, nome, escala):
    L = []

    def g(c, v):
        L.append('%3d' % c)
        L.append(v if isinstance(v, str) else fmt(v))

    g(999, 'PCEB - detalhe padrao: %s | escala 1:%d | unidade cm' % (nome, escala))
    g(0, 'SECTION'); g(2, 'HEADER')
    g(9, '$ACADVER'); g(1, 'AC1009')
    g(9, '$INSUNITS'); g(70, '5')
    g(9, '$DWGCODEPAGE'); g(3, 'ANSI_1252')
    g(0, 'ENDSEC')
    g(0, 'SECTION'); g(2, 'TABLES')
    g(0, 'TABLE'); g(2, 'LTYPE'); g(70, '2')
    g(0, 'LTYPE'); g(2, 'CONTINUOUS'); g(70, '0'); g(3, 'Solid line'); g(72, '65'); g(73, '0'); g(40, 0.0)
    g(0, 'LTYPE'); g(2, 'DASHED'); g(70, '0'); g(3, '__ __ __'); g(72, '65'); g(73, '2'); g(40, 0.75 * escala / 10)
    g(49, 0.5 * escala / 10); g(49, -0.25 * escala / 10)
    g(0, 'ENDTAB')
    g(0, 'TABLE'); g(2, 'LAYER'); g(70, str(len(LAYERS)))
    for n, c, t in LAYERS:
        g(0, 'LAYER'); g(2, n); g(70, '0'); g(62, str(c)); g(6, t)
    g(0, 'ENDTAB')
    g(0, 'TABLE'); g(2, 'STYLE'); g(70, '1')
    g(0, 'STYLE'); g(2, 'STANDARD'); g(70, '0'); g(40, 0.0); g(41, 1.0); g(50, 0.0); g(71, '0'); g(42, 2.5); g(3, 'arial.ttf'); g(4, '')
    g(0, 'ENDTAB')
    g(0, 'ENDSEC')
    g(0, 'SECTION'); g(2, 'ENTITIES')
    for p in prims:
        lay = p['classe']
        k = p['kind']
        if k == 'line':
            g(0, 'LINE'); g(8, lay)
            g(10, p['p'][0][0]); g(20, p['p'][0][1]); g(30, 0.0)
            g(11, p['p'][1][0]); g(21, p['p'][1][1]); g(31, 0.0)
        elif k == 'circle':
            g(0, 'CIRCLE'); g(8, lay); g(10, p['c'][0]); g(20, p['c'][1]); g(30, 0.0); g(40, p['r'])
        elif k == 'arc':
            g(0, 'ARC'); g(8, lay); g(10, p['c'][0]); g(20, p['c'][1]); g(30, 0.0); g(40, p['r'])
            g(50, math.degrees(p['a0']) % 360); g(51, math.degrees(p['a1']) % 360)
        elif k in ('solid', 'fill', 'arrow'):
            pts = p['p']
            if k == 'arrow':
                (x0, y0), (x1, y1) = pts
                d = math.hypot(x1 - x0, y1 - y0) or 1.0
                ux, uy = (x1 - x0) / d, (y1 - y0) / d
                s = 0.25 * escala / 10 * 1.0          # 2,5 mm de papel
                pts = [(x0, y0), (x0 + ux * s - uy * s / 4, y0 + uy * s + ux * s / 4), (x0 + ux * s + uy * s / 4, y0 + uy * s - ux * s / 4)]
            if len(pts) == 3:
                pts = pts + [pts[2]]
            if len(pts) == 4:
                g(0, 'SOLID'); g(8, lay)
                for j, (a, b) in enumerate((pts[0], pts[1], pts[3], pts[2])):
                    g(10 + j, a); g(20 + j, b); g(30 + j, 0.0)
            else:   # polígono maior: leque de triângulos
                for j in range(1, len(pts) - 1):
                    tri = (pts[0], pts[j], pts[j + 1], pts[j + 1])
                    g(0, 'SOLID'); g(8, lay)
                    for jj, (a, b) in enumerate(tri):
                        g(10 + jj, a); g(20 + jj, b); g(30 + jj, 0.0)
        elif k == 'text':
            linhas = []
            for par in p['s'].split('\n'):
                linhas += quebrar(par, p.get('w', 0.0), p['h'])
            h = p['h']
            rot = p['rot']
            ux, uy = math.cos(rot), math.sin(rot)
            nx, ny = -uy, ux
            esp = h * 1.6667
            n = len(linhas)
            # deslocamento da 1ª linha conforme o alinhamento vertical do bloco (MTEXT); TEXT tem 1 linha
            if p['va'] == 'T':
                off0 = -h
            elif p['va'] == 'M':
                off0 = (n - 1) * esp / 2 - h / 2
            else:
                off0 = (n - 1) * esp if 'w' in p else 0.0
            for j, s in enumerate(linhas):
                if not s.strip():
                    continue
                off = off0 - j * esp
                x, y = p['p'][0] + nx * off, p['p'][1] + ny * off
                g(0, 'TEXT'); g(8, lay); g(10, x); g(20, y); g(30, 0.0); g(40, h); g(1, dxf_text(s))
                if abs(rot) > 1e-6:
                    g(50, math.degrees(rot))
                ha = {'L': 0, 'C': 1, 'R': 2}[p['ha']]
                if ha:
                    g(72, str(ha)); g(11, x); g(21, y); g(31, 0.0)
    g(0, 'ENDSEC')
    g(0, 'EOF')
    io.open(path, 'w', encoding='cp1252', errors='replace', newline='\r\n').write('\n'.join(L) + '\n')


def main():
    src, outdir = sys.argv[1], sys.argv[2]
    os.makedirs(outdir, exist_ok=True)
    d = dxf.read(src)
    flat = {0: dxf.flatten(d, 0), 1: dxf.flatten(d, 1)}
    resumo = []
    for nome, arq, sp, w, esc in DETALHES:
        if isinstance(sp, str):     # um bloco inteiro: achata só as inserções dele
            fl = dxf.Flat(d)
            fl.run([e for e in d.entities if e.kind == 'INSERT' and e.get(2) == sp][:1])
            prims = [p for p in fl.out if p['layer'].upper() not in FOLHA]
        else:
            prims = recortar(flat[sp], w)
        for p in prims:
            p['classe'] = classe(p)
        x0, y0, x1, y1 = bbox(prims)
        mover(prims, -x0, -y0)
        path = os.path.join(outdir, arq + '.dxf')
        escrever(path, prims, nome, esc)
        cont = {}
        for p in prims:
            cont[p['kind']] = cont.get(p['kind'], 0) + 1
        resumo.append(dict(Nome=nome, Arquivo=arq + '.dxf', Escala=esc,
                           LarguraCm=round(x1 - x0, 1), AlturaCm=round(y1 - y0, 1)))
        print(nome, '1:%d' % esc, '%.0fx%.0f cm' % (x1 - x0, y1 - y0), cont)
    io.open(os.path.join(outdir, 'detalhes.json'), 'w', encoding='utf-8').write(json.dumps(resumo, ensure_ascii=False, indent=2))


if __name__ == '__main__':
    main()
