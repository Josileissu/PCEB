# Gabarito de FlexoCompressaoTests (D44 etapa 3): concreteproperties 0.8 (MIT). Rodar: py -3.12 flexo_compressao_concreteproperties.py
# Pilar 40×40 cm, C30 (γc = 1,4), CA-50 (γs = 1,15), 8ø20 com o centro a 4 cm das faces; parábola-retângulo 0,85·fcd (NBR 6118 8.2.10).
import math
from concreteproperties.material import Concrete, SteelBar
from concreteproperties.stress_strain_profile import EurocodeParabolicUltimate, ConcreteLinear, SteelElasticPlastic
from concreteproperties.concrete_section import ConcreteSection
from sectionproperties.pre.library import rectangular_section
from concreteproperties.pre import add_bar
fcd = 30 / 1.4
conc = Concrete(name="C30", density=2.5e-6, stress_strain_profile=ConcreteLinear(elastic_modulus=30e3),
                ultimate_stress_strain_profile=EurocodeParabolicUltimate(compressive_strength=0.85 * fcd, compressive_strain=0.002, ultimate_strain=0.0035, n=2),
                flexural_tensile_strength=0, colour="lightgrey")
aco = SteelBar(name="CA-50", density=7.85e-6, stress_strain_profile=SteelElasticPlastic(yield_strength=500 / 1.15, elastic_modulus=210e3, fracture_strain=0.05), colour="grey")
geom = rectangular_section(d=400, b=400, material=conc).align_center()
for (y, z) in [(-160, -160), (160, -160), (160, 160), (-160, 160), (0, -160), (0, 160), (-160, 0), (160, 0)]:
    geom = add_bar(geometry=geom, area=math.pi * 20 ** 2 / 4, material=aco, x=y, y=z, n=16)   # desconta o concreto da barra
sec = ConcreteSection(geom)
for n_kn in (1000, 2000):
    for theta_deg in (0, 30, 45):
        r = sec.ultimate_bending_capacity(theta=math.radians(theta_deg), n=n_kn * 1e3)
        print(f"N={n_kn} theta={theta_deg} mx={r.m_x/1e6:.2f} my={r.m_y/1e6:.2f} mxy={r.m_xy/1e6:.2f} kN.m")
