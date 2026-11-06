"""Leitor mínimo de DXF ASCII (sem bibliotecas): camadas, blocos e entidades; achata blocos/cotas em primitivas 2D."""
import io
import math
import re


def pairs(path):
    with io.open(path, 'rb') as f:
        raw = f.read().splitlines()
    lines = []
    for r in raw:
        try:
            lines.append(r.decode('utf-8'))
        except UnicodeDecodeError:
            lines.append(r.decode('cp1252'))
    for i in range(0, len(lines) - 1, 2):
        yield int(lines[i].strip()), lines[i + 1].strip()


class Ent:
    def __init__(self, kind):
        self.kind = kind
        self.items = []          # (código, valor) na ordem

    def get(self, code, default=None):
        for c, v in self.items:
            if c == code:
                return v
        return default

    def all(self, code):
        return [v for c, v in self.items if c == code]

    def f(self, code, default=0.0):
        v = self.get(code)
        return float(v) if v is not None else default

    def i(self, code, default=0):
        v = self.get(code)
        return int(v) if v is not None else default

    @property
    def layer(self):
        return self.get(8, '0')

    @property
    def space(self):        # 1 = paper space
        return int(self.get(67, '0'))


class Drawing:
    def __init__(self):
        self.layers = {}       # nome -> dict(cor, tipo, peso)
        self.blocks = {}       # nome -> (base(x,y), [Ent])
        self.entities = []


def read(path):
    d = Drawing()
    section = None
    table = None
    cur = None
    hdr, bents = None, None
    for c, v in pairs(path):
        if c == 0 and v == 'SECTION':
            section = 'start'
            cur = None
            continue
        if section == 'start' and c == 2:
            section = v
            continue
        if c == 0 and v == 'ENDSEC':
            section = None
            cur = None
            continue
        if section == 'TABLES':
            if c == 0:
                if cur is not None and cur.kind == 'LAYER':
                    nm = cur.get(2)
                    if nm is not None:
                        d.layers[nm] = dict(cor=abs(cur.i(62, 7)), tipo=cur.get(6, 'Continuous'), peso=cur.i(370, -3),
                                            desligada=cur.i(62, 7) < 0 or (cur.i(70) & 1) == 1)
                cur = Ent(v)
                continue
            if cur is not None:
                cur.items.append((c, v))
        elif section == 'BLOCKS':
            if c == 0:
                if v == 'BLOCK':
                    hdr = Ent('BLOCK')
                    cur = hdr
                    bents = []
                    continue
                if v == 'ENDBLK':
                    if hdr is not None:
                        d.blocks[hdr.get(2)] = ((hdr.f(10), hdr.f(20)), bents)
                    hdr, bents, cur = None, None, None
                    continue
                cur = Ent(v)
                if bents is not None:
                    bents.append(cur)
                continue
            if cur is not None:
                cur.items.append((c, v))
        elif section == 'ENTITIES':
            if c == 0:
                cur = Ent(v)
                d.entities.append(cur)
                continue
            if cur is not None:
                cur.items.append((c, v))
    return d


# ---------------------------------------------------------------- achatamento

class Tf:
    """Transformação 2D: escala (sx, sy), rotação r (rad) e translação, aplicada a um ponto do bloco já sem a base."""
    def __init__(self, a=1.0, b=0.0, c=0.0, d=1.0, tx=0.0, ty=0.0):
        self.a, self.b, self.c, self.d, self.tx, self.ty = a, b, c, d, tx, ty

    def __call__(self, p):
        x, y = p
        return (self.a * x + self.c * y + self.tx, self.b * x + self.d * y + self.ty)

    def then(self, o):           # primeiro self, depois o
        return Tf(o.a * self.a + o.c * self.b, o.b * self.a + o.d * self.b,
                  o.a * self.c + o.c * self.d, o.b * self.c + o.d * self.d,
                  o.a * self.tx + o.c * self.ty + o.tx, o.b * self.tx + o.d * self.ty + o.ty)

    @property
    def scale(self):
        return math.sqrt(abs(self.a * self.d - self.b * self.c))

    @property
    def mirrored(self):
        return self.a * self.d - self.b * self.c < 0

    def angle(self, ang):        # direção (rad) transformada
        x, y = math.cos(ang), math.sin(ang)
        return math.atan2(self.b * x + self.d * y, self.a * x + self.c * y)


def insert_tf(e, base):
    sx, sy = e.f(41, 1.0), e.f(42, 1.0)
    r = math.radians(e.f(50))
    cs, sn = math.cos(r), math.sin(r)
    t = Tf(1, 0, 0, 1, -base[0], -base[1])
    t = t.then(Tf(sx, 0, 0, sy))
    t = t.then(Tf(cs, sn, -sn, cs))
    return t.then(Tf(1, 0, 0, 1, e.f(10), e.f(20)))


MTEXT_CODES = re.compile(r'\\[ACcFfHhQqTtWwp][^;]*;|\\[LlOoKk]|[{}]')


def mtext_plain(s):
    s = s.replace('\\P', '\n').replace('\\~', ' ').replace('^I', '    ')
    s = re.sub(r'\\S([^;^]*)[\^#/]([^;]*);', r'\1/\2', s)
    s = MTEXT_CODES.sub('', s)
    s = s.replace('%%c', 'ø').replace('%%C', 'ø').replace('%%d', '°').replace('%%p', '±')
    s = re.sub(r'\\U\+([0-9A-Fa-f]{4})', lambda m: chr(int(m.group(1), 16)), s)
    return s.replace('\\\\', '\\')


def text_plain(s):
    s = s.replace('%%c', 'ø').replace('%%C', 'ø').replace('%%d', '°').replace('%%p', '±').replace('%%u', '').replace('%%U', '')
    return re.sub(r'\\U\+([0-9A-Fa-f]{4})', lambda m: chr(int(m.group(1), 16)), s)


class Flat:
    def __init__(self, d):
        self.d = d
        self.out = []

    def props(self, e, parent):
        lay = e.layer
        if lay == '0' and parent is not None:
            lay = parent['layer']
        li = self.d.layers.get(lay, dict(cor=7, tipo='Continuous', peso=-3, desligada=False))
        cor = e.i(62, 256)
        if cor == 256:
            cor = li['cor']
        elif cor == 0:
            cor = parent['cor'] if parent else 7
        tipo = e.get(6, 'BYLAYER')
        if tipo.upper() == 'BYLAYER':
            tipo = li['tipo']
        elif tipo.upper() == 'BYBLOCK':
            tipo = parent['tipo'] if parent else 'Continuous'
        peso = e.i(370, -1)
        if peso == -1:
            peso = li['peso']
        elif peso == -2:
            peso = parent['peso'] if parent else -3
        return dict(layer=lay, cor=cor, tipo=tipo, peso=peso, desligada=li.get('desligada', False))

    def add(self, kind, pr, **kw):
        kw.update(kind=kind, **pr)
        self.out.append(kw)

    def run(self, ents, tf=Tf(), parent=None, depth=0):
        for e in ents:
            pr = self.props(e, parent)
            if pr['desligada'] or pr['layer'].upper() == 'DEFPOINTS':
                continue
            k = e.kind
            if k == 'LINE':
                self.add('line', pr, p=[tf((e.f(10), e.f(20))), tf((e.f(11), e.f(21)))])
            elif k == 'CIRCLE':
                self.add('circle', pr, c=tf((e.f(10), e.f(20))), r=e.f(40) * tf.scale)
            elif k == 'ARC':
                a0, a1 = math.radians(e.f(50)), math.radians(e.f(51))
                if e.f(230, 1.0) < 0:         # normal para baixo (OCS espelhado)
                    cx = -e.f(10)
                    a0, a1 = math.pi - a1, math.pi - a0
                else:
                    cx = e.f(10)
                b0, b1 = tf.angle(a0), tf.angle(a1)
                if tf.mirrored:
                    b0, b1 = b1, b0
                self.add('arc', pr, c=tf((cx, e.f(20))), r=e.f(40) * tf.scale, a0=b0, a1=b1)
            elif k == 'LWPOLYLINE':
                xs, ys = [float(x) for x in e.all(10)], [float(y) for y in e.all(20)]
                # bulges acompanham os vértices: percorre na ordem
                verts, bul = [], []
                for c, v in e.items:
                    if c == 10:
                        verts.append([float(v), 0.0])
                        bul.append(0.0)
                    elif c == 20 and verts:
                        verts[-1][1] = float(v)
                    elif c == 42 and verts:
                        bul[-1] = float(v)
                closed = (e.i(70) & 1) == 1
                n = len(verts)
                for j in range(n if closed else n - 1):
                    p0, p1 = verts[j], verts[(j + 1) % n]
                    self.segment(pr, tf, p0, p1, bul[j])
            elif k == 'POLYLINE':
                pass  # vértices vêm como entidades VERTEX seguintes (raros aqui)
            elif k == 'SPLINE':
                pts = list(zip([float(x) for x in e.all(11)], [float(y) for y in e.all(21)]))
                if len(pts) < 2:
                    pts = list(zip([float(x) for x in e.all(10)], [float(y) for y in e.all(20)]))
                for j in range(len(pts) - 1):
                    self.add('line', pr, p=[tf(pts[j]), tf(pts[j + 1])])
            elif k == 'SOLID':
                pts = [(e.f(10), e.f(20)), (e.f(11), e.f(21)), (e.f(13), e.f(23)), (e.f(12), e.f(22))]
                self.add('solid', pr, p=[tf(p) for p in pts])
            elif k == 'TEXT':
                h = e.f(40) * tf.scale
                ha, va = e.i(72), e.i(73)
                p = (e.f(11), e.f(21)) if (ha or va) and e.get(11) is not None else (e.f(10), e.f(20))
                self.add('text', pr, p=tf(p), h=h, rot=tf.angle(math.radians(e.f(50))), s=text_plain(e.get(1, '')),
                         ha={0: 'L', 1: 'C', 2: 'R', 4: 'C', 5: 'L'}.get(ha, 'L'), va={0: 'B', 1: 'B', 2: 'M', 3: 'T'}.get(va, 'B'))
            elif k == 'MTEXT':
                s = mtext_plain(e.get(1, '') if not e.all(3) else ''.join(e.all(3)) + e.get(1, ''))
                h = e.f(40) * tf.scale
                m = re.search(r'\\H([0-9.]+)x;', e.get(1, ''))
                att = e.i(71, 1)
                rot = math.radians(e.f(50)) if e.get(50) else math.atan2(e.f(21), e.f(11)) if e.get(11) else 0.0
                self.add('text', pr, p=tf((e.f(10), e.f(20))), h=h, rot=tf.angle(rot), s=s,
                         ha='LCR'[(att - 1) % 3], va='TMB'[(att - 1) // 3], w=e.f(41) * tf.scale)
            elif k in ('INSERT', 'DIMENSION'):
                name = e.get(2)
                if name not in self.d.blocks or depth > 8:
                    continue
                base, bents = self.d.blocks[name]
                t2 = insert_tf(e, base) if k == 'INSERT' else Tf()
                self.run(bents, t2.then(tf), pr, depth + 1)
            elif k == 'HATCH':
                if e.i(70) == 1:      # preenchimento sólido: só contornos de polilinha
                    self.hatch(e, pr, tf)
            elif k == 'LEADER':
                pts = list(zip([float(x) for x in e.all(10)], [float(y) for y in e.all(20)]))
                for j in range(len(pts) - 1):
                    self.add('line', pr, p=[tf(pts[j]), tf(pts[j + 1])])
                if len(pts) >= 2 and e.i(71, 1):
                    self.add('arrow', pr, p=[tf(pts[0]), tf(pts[1])])

    def segment(self, pr, tf, p0, p1, bulge):
        if abs(bulge) < 1e-9:
            self.add('line', pr, p=[tf(tuple(p0)), tf(tuple(p1))])
            return
        th = 4 * math.atan(bulge)
        dx, dy = p1[0] - p0[0], p1[1] - p0[1]
        ch = math.hypot(dx, dy)
        r = ch / (2 * math.sin(abs(th) / 2))
        mx, my = (p0[0] + p1[0]) / 2, (p0[1] + p1[1]) / 2
        hgt = math.sqrt(max(r * r - ch * ch / 4, 0))
        nx, ny = -dy / ch, dx / ch
        sgn = 1 if (bulge > 0) == (abs(th) < math.pi) else -1
        cx, cy = mx + sgn * nx * hgt, my + sgn * ny * hgt
        a0 = math.atan2(p0[1] - cy, p0[0] - cx)
        a1 = math.atan2(p1[1] - cy, p1[0] - cx)
        if bulge < 0:
            a0, a1 = a1, a0
        b0, b1 = tf.angle(a0), tf.angle(a1)
        if tf.mirrored:
            b0, b1 = b1, b0
        self.add('arc', pr, c=tf((cx, cy)), r=r * tf.scale, a0=b0, a1=b1)

    def hatch(self, e, pr, tf):
        # contornos de polilinha (código 72 após 92 com bit 2); só vértices
        it = iter(e.items)
        loops = []
        cur = None
        for c, v in it:
            if c == 92:
                cur = []
                loops.append(cur)
            elif c == 10 and cur is not None:
                cur.append([float(v), 0.0])
            elif c == 20 and cur:
                cur[-1][1] = float(v)
            elif c == 97:
                cur = None
        for lp in loops:
            if len(lp) >= 3:
                self.add('fill', pr, p=[tf(tuple(p)) for p in lp])


def flatten(d, space=0):
    fl = Flat(d)
    fl.run([e for e in d.entities if e.space == space])
    return fl.out
