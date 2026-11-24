# Gabarito de SolverBarrasTests.PropriedadesDaSecao (sectionproperties 3.10, MIT). Rodar: py -3.12 secoes_sectionproperties.py
from sectionproperties.pre.geometry import Polygon, Geometry
from sectionproperties.analysis.section import Section
from sectionproperties.pre.library import rectangular_section
def calc(nome, geom, ms):
    geom.create_mesh(mesh_sizes=[ms]); s=Section(geom); s.calculate_geometric_properties(); s.calculate_warping_properties()
    ixx,iyy,ixy=s.get_ic(); j=s.get_j(); phi=s.get_phi()
    print(f"{nome}: A={s.get_area():.4f} Ixx(z²)={ixx:.2f} Iyy(y²)={iyy:.2f} Ixy={ixy:.2f} J={j:.1f} phi={phi:.4f}")
calc("ret20x50", rectangular_section(d=50,b=20), 2)
calc("q40", rectangular_section(d=40,b=40), 2)
calc("L", Geometry(Polygon([(0,0),(40,0),(40,15),(15,15),(15,60),(0,60)])), 2)
calc("T", Geometry(Polygon([(15,0),(35,0),(35,40),(50,40),(50,52),(0,52),(0,40),(15,40)])), 2)
calc("TInv", Geometry(Polygon([(0,0),(60,0),(60,20),(40,20),(40,70),(20,70),(20,20),(0,20)])), 2)
calc("trap", Geometry(Polygon([(0,0),(30,0),(25,40),(5,40)])), 2)
