# Gabarito do muro de arrimo em balanço (forma fechada, β = 0, sem água): dados de partida do DadosMuro
from math import *
h, tt, tb, P, T, es, Df = 3.5, 0.20, 0.35, 0.70, 1.65, 0.40, 0.60
g, phi, q, gc = 18, 30, 10, 25
gf, phif, cf = 18, 30, 0
B = P + tb + T; H = es + h
ka = tan(radians(45 - phi / 2)) ** 2
Ea = 0.5 * ka * g * H**2; Eq = ka * q * H
xt = P + tb
W = [  # (peso, x)
    (tt * h * gc, xt - tt / 2),
    ((tb - tt) * h / 2 * gc, P + 2 * (tb - tt) / 3),
    (B * es * gc, B / 2),
    (T * h * g, xt + T / 2),
    (P * (Df - es) * gf, P / 2),
]
Mo = Ea * H / 3 + Eq * H / 2
Mr = sum(w * x for w, x in W)
print("Ka", ka, "Ea", Ea, "Eq", Eq)
print("FS tomb", Mr / Mo)
N = sum(w for w, _ in W)
k1 = 0.67
print("FS desl", N * tan(radians(k1 * phif)) / (Ea + Eq))
# base com a sobrecarga sobre o talão
Wq = q * T; xq = xt + T / 2
N2 = N + Wq; M2 = Mr + Wq * xq - Mo
xr = M2 / N2; e = B / 2 - xr
smax = N2 / B * (1 + 6 * e / B); smin = N2 / B * (1 - 6 * e / B)
print("N", N2, "e", e, "smax", smax, "smin", smin)
# sem a sobrecarga
xr1 = (Mr - Mo) / N; e1 = B / 2 - xr1
print("sem q: smax", N / B * (1 + 6 * e1 / B))
# capacidade
theta = degrees(atan((Ea + Eq) / N2)); Bef = B - 2 * e
Nq = exp(pi * tan(radians(phif))) * tan(radians(45 + phif / 2)) ** 2
Ng = 2 * (Nq + 1) * tan(radians(phif))
iq = (1 - theta / 90) ** 2; ig = (1 - theta / phif) ** 2
qu = gf * Df * Nq * iq + 0.5 * gf * Bef * Ng * ig
print("theta", theta, "qu", qu, "FS cap", qu * Bef / N2)
# fuste: M na base (característico)
Mg = ka * g * h**3 / 6; Mq = ka * q * h**2 / 2
print("Mk fuste", Mg + Mq, "Md", 1.4 * (Mg + Mq), "Vd", 1.4 * (ka * g * h**2 / 2 + ka * q * h))
# Coulomb e Rankine inclinado
def kac(f, b, d):
    f, b, d = radians(f), radians(b), radians(d)
    s = sqrt(sin(f + d) * sin(f - b) / (cos(d) * cos(b)))
    return cos(f)**2 / (cos(d) * (1 + s)**2)
def kar(f, b):
    f, b = radians(f), radians(b)
    r = sqrt(cos(b)**2 - cos(f)**2)
    return cos(b) * (cos(b) - r) / (cos(b) + r)
print("Coulomb 30/0/20", kac(30, 0, 20), "Rankine 30/10", kar(30, 10))
