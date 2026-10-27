"""Le o esquema (classes/campos) do .cad do QiArmaduras. Estudo do formato para importacao."""
import struct
import sys

dados = open(sys.argv[1], "rb").read()


def u32(o):
    return struct.unpack_from("<i", dados, o)[0]


def texto(o):
    n = u32(o)
    return dados[o + 4:o + 4 + n].decode("latin-1"), o + 4 + n


o = 0x3D
classes = {}
while o < len(dados) - 8:
    cid = u32(o)
    n = u32(o + 4)
    if not (0 < n < 80) or not (0 < cid < 100000):
        break
    nome, p = texto(o + 4)
    if not nome.replace("_", "").isalnum():
        break
    flag = dados[p]; p += 1
    menos1 = u32(p); p += 4
    npais = u32(p); p += 4
    if not (0 <= npais < 10):
        break
    pais = [u32(p + 4 * i) for i in range(npais)]; p += 4 * npais
    secoes = []
    ok = True
    for s in range(5):
        nc = u32(p); p += 4
        if not (0 <= nc < 200):
            ok = False
            break
        campos = []
        for _ in range(nc):
            a = u32(p); tipo = u32(p + 4)
            nm, p = texto(p + 8)
            campos.append((nm, tipo, a))
        secoes.append(campos)
    if not ok:
        break
    classes[cid] = (nome, flag, menos1, pais, secoes, o)
    o = p

print(f"fim do esquema em 0x{o:X}; {len(classes)} classes")
for cid, (nome, flag, m1, pais, secoes, off) in classes.items():
    pn = ",".join(classes.get(x, ("?",))[0] for x in pais)
    print(f"{cid:4d} {nome} flag={flag} pais=[{pn}] @0x{off:X}")
    for i, campos in enumerate(secoes):
        for nm, tipo, a in campos:
            tn = classes[tipo][0] if tipo in classes else str(tipo)
            print(f"      s{i} {nm}: {tn} (a={a})")
