"""Raio-X de uma pasta de instalação: tecnologias identificadas pelos nomes dos arquivos e cabeçalhos PE.
Uso: python raiox.py <nome> <pasta> [<pasta>...]  — só leitura."""
import os, re, sys, struct, collections

TECH = [
    # (grupo, rótulo, regex no nome do arquivo, minúsculo)
    ("Interface", "Qt", r"^qt[56]?(core|gui|widgets|quick)|^qt\d"),
    ("Interface", "WPF", r"^(presentationframework|presentationcore|windowsbase)\.dll$"),
    ("Interface", "WinForms", r"^system\.windows\.forms\.dll$"),
    ("Interface", "MFC", r"^mfc\d+u?\.dll$"),
    ("Interface", "Codejock (Xtreme Toolkit)", r"toolkitpro|codejock|xtp"),
    ("Interface", "BCGSoft", r"^bcg"),
    ("Interface", "DevExpress", r"^devexpress"),
    ("Interface", "Telerik", r"^telerik"),
    ("Interface", "Infragistics", r"^infragistics"),
    ("Interface", "Syncfusion", r"^syncfusion"),
    ("Interface", "Xceed", r"^xceed"),
    ("Interface", "Chromium (CEF)", r"^libcef\.dll$|cefsharp"),
    ("Interface", "WebView2", r"webview2"),
    ("Interface", "Electron", r"^electron|app\.asar$"),
    ("Interface", "wxWidgets", r"^wx(base|msw)"),
    ("Gráfico", "DirectX 11/12", r"^d3d1[12]|^d3dcompiler|^dxgi"),
    ("Gráfico", "DirectX 9", r"^d3dx?9"),
    ("Gráfico", "OpenGL", r"opengl|^glew|^glut|^freeglut|^osmesa"),
    ("Gráfico", "Vulkan", r"vulkan"),
    ("Gráfico", "HOOPS (Tech Soft 3D)", r"hoops|^hps|^hexchange|^a3dlib"),
    ("Gráfico", "VTK", r"^vtk"),
    ("Gráfico", "OpenSceneGraph", r"^osg"),
    ("Gráfico", "Coin3D / Open Inventor", r"^coin\d|inventor"),
    ("Gráfico", "SharpDX / SlimDX", r"sharpdx|slimdx"),
    ("Gráfico", "Helix Toolkit", r"helixtoolkit"),
    ("Gráfico", "Autodesk OGS/Graphics", r"^ogs|adskgraphics|^ags"),
    ("Gráfico", "NVIDIA / CUDA", r"cuda|nvrtc|optix"),
    ("Geometria", "ACIS (Spatial)", r"spaacis|^acis|spa_"),
    ("Geometria", "Parasolid", r"pskernel|parasolid"),
    ("Geometria", "Open CASCADE", r"^tk(brep|g2d|g3d|math|kernel|occ)"),
    ("Geometria", "ODA / Teigha (DWG)", r"^td_|^oda|teigha|^tg_|^tdrawing"),
    ("Geometria", "RealDWG / ObjectARX", r"^acdb\d|^acge\d|realdwg"),
    ("Geometria", "IFC (IfcOpenShell/ODA IFC/xBIM)", r"ifc"),
    ("Geometria", "CGAL", r"cgal"),
    ("Cálculo", "Intel MKL", r"^mkl_|libiomp5"),
    ("Cálculo", "Intel TBB", r"^tbb"),
    ("Cálculo", "OpenMP", r"vcomp\d+|libgomp|libiomp"),
    ("Cálculo", "MUMPS/PARDISO/SuiteSparse", r"mumps|pardiso|umfpack|cholmod|suitesparse"),
    ("Cálculo", "BLAS/LAPACK/OpenBLAS", r"openblas|lapack|blas"),
    ("Cálculo", "MPI", r"^msmpi|^impi|mpich"),
    ("Cálculo", "Fortran (Intel)", r"libifcore|libmmd|svml_dispmd"),
    ("Script", "Python embutido", r"^python\d+\.dll$|^python3\d*\.dll$"),
    ("Script", "IronPython", r"ironpython"),
    ("Script", "Lua", r"^lua"),
    ("Script", "JavaScript (V8/ClearScript)", r"^v8|clearscript"),
    ("Script", "VBA", r"^vbe\d|vba"),
    ("Dados", "SQLite", r"sqlite"),
    ("Dados", "SQL Server CE/LocalDB", r"sqlce|localdb"),
    ("Dados", "HDF5", r"hdf5"),
    ("Relatórios", "Crystal Reports", r"crystal"),
    ("Relatórios", "Stimulsoft/FastReport/List&Label", r"stimulsoft|fastreport|cmll|listlabel"),
    ("Licença", "FlexNet/FlexLM", r"flexnet|lmgr|flexlm|^fnp"),
    ("Licença", "Sentinel/HASP/CodeMeter", r"hasp|sentinel|codemeter|wibu"),
]

def pe_info(path):
    """(é .NET, é 64 bits) lendo o cabeçalho PE; None se não for PE."""
    try:
        with open(path, "rb") as f:
            h = f.read(4096)
        if h[:2] != b"MZ": return None
        pe = struct.unpack_from("<I", h, 0x3C)[0]
        if pe + 24 > len(h) or h[pe:pe+4] != b"PE\0\0": return None
        machine = struct.unpack_from("<H", h, pe + 4)[0]
        opt = pe + 24
        magic = struct.unpack_from("<H", h, opt)[0]
        dd = opt + (96 if magic == 0x10B else 112)
        cli_rva = struct.unpack_from("<I", h, dd + 14 * 8)[0] if dd + 14 * 8 + 4 <= len(h) else 0
        return (cli_rva != 0, machine == 0x8664)
    except Exception:
        return None

def main():
    nome, pastas = sys.argv[1], sys.argv[2:]
    achados = collections.defaultdict(collections.Counter)
    exemplos = collections.defaultdict(set)
    n_dll = n_net = n_nat = 0
    tam = 0
    exts = collections.Counter()
    for raiz in pastas:
        for d, _, fs in os.walk(raiz):
            for fn in fs:
                p = os.path.join(d, fn)
                low = fn.lower()
                ext = os.path.splitext(low)[1]
                exts[ext] += 1
                try: tam += os.path.getsize(p)
                except OSError: pass
                if ext in (".dll", ".exe"):
                    n_dll += 1
                    info = pe_info(p)
                    if info:
                        if info[0]: n_net += 1
                        else: n_nat += 1
                for g, rot, rx in TECH:
                    if re.search(rx, low):
                        achados[g][rot] += 1
                        if len(exemplos[rot]) < 3: exemplos[rot].add(fn)
    print(f"## {nome}")
    print(f"pastas: {'; '.join(pastas)}")
    print(f"tamanho: {tam/1e9:.2f} GB | executáveis/DLLs: {n_dll} (.NET {n_net}, nativos {n_nat})")
    print("extensões mais comuns: " + ", ".join(f"{e or '(sem)'} {c}" for e, c in exts.most_common(12)))
    for g in ["Interface", "Gráfico", "Geometria", "Cálculo", "Script", "Dados", "Relatórios", "Licença"]:
        if achados[g]:
            itens = [f"{r} ({c}: {', '.join(sorted(exemplos[r]))})" for r, c in achados[g].most_common()]
            print(f"- {g}: " + "; ".join(itens))
    print()

if __name__ == "__main__":
    main()
