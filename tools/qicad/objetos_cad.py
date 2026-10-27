"""Decodifica os objetos do .cad do QiArmaduras usando o esquema gravado no prÃ³prio arquivo.
Estudo do formato (interoperabilidade com arquivos do prÃ³prio usuÃ¡rio)."""
import contextlib
import importlib.util
import io
import struct
import sys

ARGS = list(sys.argv)
caminho = ARGS[1]
dados = open(caminho, "rb").read()
spec = importlib.util.spec_from_file_location("esq", ARGS[2])
sys.argv = [ARGS[2], caminho]
with contextlib.redirect_stdout(io.StringIO()):
    esq = importlib.util.module_from_spec(spec); spec.loader.exec_module(esq)
classes = esq.classes
MAGIA = b"\xCA\xAB\xCA\xAB"


def hierarquia(cid):
    """Classes da raiz atÃ© cid."""
    nome, flag, m1, pais, secoes, off = classes[cid]
    r = []
    for p in pais:
        r += hierarquia(p)
    return r + [cid]


def campos(cid, ordem):
    """Lista de (classe, seÃ§Ã£o, nome, tipo, a) na ordem de gravaÃ§Ã£o suposta."""
    h = hierarquia(cid)
    r = []
    if ordem == "classe":
        for c in h:
            for s, cs in enumerate(classes[c][4]):
                for nm, tipo, a in cs:
                    r.append((classes[c][0], s, nm, tipo, a))
    else:  # por seÃ§Ã£o
        for s in range(5):
            for c in h:
                for nm, tipo, a in classes[c][4][s]:
                    r.append((classes[c][0], s, nm, tipo, a))
    return r


class Leitor:
    def __init__(self, ini, fim):
        self.p = ini
        self.fim = fim

    def i32(self):
        v = struct.unpack_from("<i", dados, self.p)[0]; self.p += 4; return v

    def f64(self):
        v = struct.unpack_from("<d", dados, self.p)[0]; self.p += 8; return v

    def b(self):
        v = dados[self.p]; self.p += 1; return v

    def s(self):
        n = self.i32()
        if n < 0 or n > 100000:
            raise ValueError(f"texto com tamanho {n}")
        v = dados[self.p:self.p + n]; self.p += n
        try:
            return v.decode("utf-8")
        except UnicodeDecodeError:
            return v.decode("latin-1")


def valor(L, tipo):
    if tipo == -1: return L.i32()
    if tipo == -3: return round(L.f64(), 6)
    if tipo == -4: return L.s()
    if tipo == -10: return bool(L.b())
    if tipo in (-6, -8, -15, -18): return ("ref", L.i32())
    if tipo == -17: return L.i32()
    if tipo in (-7, -9):
        n = L.i32()
        if n < 0 or n > 100000: raise ValueError(f"lista {n}")
        return [("ref", L.i32()) for _ in range(n)]
    if tipo == -14:
        n = L.i32(); return [L.s() for _ in range(n)]
    if tipo == -13:
        n = L.i32(); return [round(L.f64(), 6) for _ in range(n)]
    raise ValueError(f"tipo desconhecido {tipo}")


regs = []
o = esq.o
while True:
    i = dados.find(MAGIA, o)
    if i < 0: break
    regs.append(i); o = i + 4
regs.append(len(dados))

objetos = {}
for k in range(len(regs) - 1):
    i, fim = regs[k], regs[k + 1]
    tam, chk, cid = struct.unpack_from("<iIi", dados, i + 4)
    L = Leitor(i + 16, fim)
    num = L.i32(); extra = L.i32()
    ordem = ARGS[3] if len(ARGS) > 3 else "classe"
    vals = {}
    erro = None
    try:
        for cl, s, nm, tipo, a in campos(cid, ordem):
            vals[nm if nm not in vals else f"{cl}.{nm}"] = valor(L, tipo)
    except Exception as e:
        erro = str(e)
    sobra = fim - L.p
    objetos[num] = (classes[cid][0], vals, erro, sobra, extra)

filtro = ARGS[4].split(",") if len(ARGS) > 4 else None
ok = sum(1 for v in objetos.values() if v[2] is None and v[3] == 0)
print(f"{len(objetos)} objetos; {ok} lidos sem sobra/erro")
for num, (nome, vals, erro, sobra, extra) in objetos.items():
    if filtro and nome not in filtro: continue
    print(f"#{num} {nome} extra={extra} sobra={sobra}" + (f" ERRO {erro}" if erro else ""))
    for k, v in vals.items():
        print(f"     {k} = {v}")

