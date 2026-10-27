"""Mostra as armaduras, faixas, cortes, RA e detalhamento do .cad com as referências resolvidas."""
import contextlib
import importlib.util
import io
import sys

ARGS = list(sys.argv)
spec = importlib.util.spec_from_file_location("obj", ARGS[3])
sys.argv = [ARGS[3], ARGS[1], ARGS[2], "classe", "XXX"]
with contextlib.redirect_stdout(io.StringIO()):
    m = importlib.util.module_from_spec(spec); spec.loader.exec_module(m)
O = m.objetos


def obj(r):
    return O.get(r[1]) if isinstance(r, tuple) and r[0] == "ref" else None


def ponto(r):
    o = obj(r)
    return (o[1].get("x"), o[1].get("y")) if o else None


def poligono(v):
    pts = []
    for r in v.get("points", []):
        o = obj(r)
        if not o: continue
        arco = obj(o[1].get("polygonArc"))
        pts.append((round(o[1]["x"], 3), round(o[1]["y"], 3)) + (("ARCO",) if arco else ()))
    return pts


diams = [o[1] for o in O.values() if o[0] == "TDiameterConfig"]
print("Diâmetros configurados:", [(d.get("diameter"), d.get("steelType")) for d in diams][:30])
for num, (nome, v, *_) in O.items():
    if nome in ("TCadRebar", "TReflectedRebar", "TCadPolygon"):
        print(f"\n#{num} {nome} fechada={v.get('closed')} pontos={poligono(v)}")
        if nome == "TCadRebar":
            rb = obj(v["rebar"])[1]
            print("   barra:", {k: rb[k] for k in ("diameter", "id", "length", "quantity", "spacing", "repetitions", "continuous", "stirrup")})
            for s in v.get("segments", []):
                so = obj(s)[1]
                print("   segmento:", {k: so[k] for k in ("segmentNumber", "segmentType", "variable", "fixedIndication")})
            print("   escala:", obj(v["scale"])[1] if obj(v["scale"]) else None, " pai:", v.get("parent"))
        if nome == "TReflectedRebar":
            print("   rebatida de:", v.get("cadRebar"))
    elif nome in ("TRebarBand", "TRebarIndication"):
        print(f"\n#{num} {nome}", {k: v.get(k) for k in ("cutCallBand", "fixedQuantity", "quantity", "spacing", "percInitialDistance", "text", "rebar")},
              "p1", ponto(v.get("pos1")), "p2", ponto(v.get("pos2")), "pos", ponto(v.get("finalPos")), "ang", v.get("dimensionAngle"))
    elif nome in ("TStirrupCut", "TCadRebarList", "TCadDetail", "TRebarText", "TCadText"):
        curto = {k: x for k, x in v.items() if not isinstance(x, (list, tuple)) and k not in ("guid",)}
        print(f"\n#{num} {nome}", curto)
