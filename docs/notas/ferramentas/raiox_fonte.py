"""Raio-X de código-fonte baixado (.zip, .tar.*, .tgz), lido direto do arquivo, sem extrair nem executar.
Uso: python -I raiox_fonte.py <nome> <arquivo> [<arquivo>...]  — só leitura; relatório em texto na saída padrão."""
import collections, io, os, re, sys, tarfile, zipfile

LING = {
    "C/C++": (".c", ".h", ".cc", ".cpp", ".cxx", ".hh", ".hpp", ".hxx", ".inl", ".tcc"),
    "Python": (".py", ".pyx", ".pxd", ".pyi"),
    "Fortran": (".f", ".f90", ".f95", ".f03", ".for", ".F", ".F90"),
    "C#": (".cs",), "Java": (".java",), "Tcl": (".tcl",), "JavaScript/TS": (".js", ".ts", ".tsx", ".jsx"),
    "GLSL/shaders": (".glsl", ".vert", ".frag", ".comp", ".msl", ".hlsl", ".metal"),
    "Objective-C": (".m", ".mm"), "Rust": (".rs",), "Lua": (".lua",), "SWIG": (".i",),
    "CMake": (".cmake",), "Matlab/Octave": (".m",),
}
EXT2LING = {}
for ling, exts in LING.items():
    for e in exts:
        EXT2LING.setdefault(e, ling)
EXT2LING[".m"] = "Objective-C/Matlab"

# tecnologia procurada no texto dos arquivos de build (CMake, setup, pyproject, csproj, Makefile, meson)
TECH = [
    ("Interface", "Qt", r"\bQt[56]?\b|find_package\(\s*Qt"), ("Interface", "GTK", r"\bgtk[+-]?\d?\b"),
    ("Interface", "wxWidgets", r"wxWidgets"), ("Interface", "FLTK", r"\bfltk\b"),
    ("Interface", "WinForms/WPF", r"System\.Windows\.Forms|UseWindowsForms|UseWPF|PresentationFramework"),
    ("Interface", "Dear ImGui", r"imgui"), ("Interface", "Tk", r"\bTk\b|tkinter"),
    ("Gráfico", "OpenGL", r"OpenGL|\bGLEW\b|\bepoxy\b|freeglut"), ("Gráfico", "Vulkan", r"Vulkan"),
    ("Gráfico", "Metal", r"\bMetal\b"), ("Gráfico", "DirectX", r"\bd3d1[12]\b|DirectX"),
    ("Gráfico", "VTK", r"\bVTK\b"), ("Gráfico", "Coin3D/Pivy", r"\bCoin\b|pivy"),
    ("Gráfico", "OpenTK/SharpGL", r"OpenTK|SharpGL"), ("Gráfico", "Embree/OSPRay/Cycles", r"Embree|OSPRay|Cycles"),
    ("Gráfico", "OpenSubdiv/USD/MaterialX", r"OpenSubdiv|\bUSD\b|pxr|MaterialX"),
    ("Geometria", "Open CASCADE", r"OpenCASCADE|OCCT|\bOCC\b|TKBRep"), ("Geometria", "CGAL", r"\bCGAL\b"),
    ("Geometria", "Eigen", r"\bEigen3?\b"), ("Geometria", "Boost", r"\bBoost\b"),
    ("Geometria", "Manifold/Clipper", r"manifold|clipper"), ("Geometria", "shapely", r"shapely"),
    ("Cálculo", "BLAS/LAPACK/OpenBLAS", r"\bBLAS\b|LAPACK|OpenBLAS"), ("Cálculo", "MKL", r"\bMKL\b"),
    ("Cálculo", "MUMPS", r"MUMPS"), ("Cálculo", "PETSc", r"PETSc"), ("Cálculo", "SuiteSparse/UMFPACK", r"SuiteSparse|UMFPACK|CHOLMOD"),
    ("Cálculo", "SPOOLES/ARPACK", r"SPOOLES|ARPACK"), ("Cálculo", "MPI", r"\bMPI\b"), ("Cálculo", "OpenMP", r"OpenMP"),
    ("Cálculo", "TBB", r"\bTBB\b"), ("Cálculo", "CUDA/HIP/OneAPI", r"\bCUDA\b|\bHIP\b|oneAPI|SYCL"),
    ("Cálculo", "numpy/scipy", r"numpy|scipy"), ("Cálculo", "numba/cython", r"numba|[Cc]ython"),
    ("Dados", "HDF5/MED", r"HDF5|\bMED\b|medfile"), ("Dados", "SQLite", r"SQLite"), ("Dados", "zlib/zstd", r"zlib|zstd"),
    ("Script", "Python embutido", r"Python3?_?(LIBRARY|INCLUDE|Development)|PYTHON_(LIBRARY|INCLUDE)|pybind11|nanobind"),
    ("Script", "SWIG", r"\bSWIG\b"), ("Script", "Tcl", r"\bTCL\b|\bTcl\b"),
    ("Formatos", "IFC", r"\bIFC\b|ifcopenshell"), ("Formatos", "STEP/IGES", r"\bSTEP\b|\bIGES\b"),
    ("Formatos", "DXF/DWG", r"\bDXF\b|\bDWG\b|libredwg|ezdxf"), ("Formatos", "glTF/OBJ/STL/FBX", r"glTF|\bOBJ\b|\bSTL\b|\bFBX\b"),
    ("Formatos", "Alembic/OpenVDB", r"Alembic|OpenVDB"), ("Formatos", "OpenImageIO/OpenEXR", r"OpenImageIO|OpenEXR"),
    ("Formatos", "Gmsh/MED/VTK", r"\bgmsh\b"),
]
BUILD = re.compile(r"(^|/)(CMakeLists\.txt|[^/]+\.cmake|setup\.py|setup\.cfg|pyproject\.toml|requirements[^/]*\.txt|"
                   r"[^/]+\.csproj|Makefile[^/]*|meson\.build|configure\.ac|wscript|environment[^/]*\.ya?ml|package\.json)$", re.I)
LICENSE = re.compile(r"(^|/)(LICEN[CS]E|COPYING|COPYRIGHT)[^/]*$", re.I)
TEXTO = set(EXT2LING) | {".txt", ".cmake", ".toml", ".cfg", ".csproj", ".build", ".ac", ".yml", ".yaml", ".json"}

def membros(caminho):
    """Gera (nome, tamanho, leitor) para cada arquivo do pacote; zip interno é aberto também."""
    if zipfile.is_zipfile(caminho):
        z = zipfile.ZipFile(caminho)
        for i in z.infolist():
            if not i.is_dir():
                yield i.filename, i.file_size, (lambda i=i: z.read(i))
    else:
        t = tarfile.open(caminho, "r:*")
        for m in t:
            if m.isfile():
                yield m.name, m.size, (lambda m=m: t.extractfile(m).read())

def main():
    nome, arquivos = sys.argv[1], sys.argv[2:]
    ling_arq, ling_bytes, ling_linhas = collections.Counter(), collections.Counter(), collections.Counter()
    pastas, tech, licencas, build_txt = collections.Counter(), collections.defaultdict(set), [], []
    total = 0
    for arq in arquivos:
        for n, tam, ler in membros(arq):
            total += 1
            partes = n.split("/")
            raiz = partes[0]
            rel = "/".join(partes[1:]) if len(partes) > 1 else n
            pastas["/".join(rel.split("/")[:2]) if rel.count("/") >= 2 else rel.split("/")[0]] += tam
            ext = os.path.splitext(n)[1]
            ling = EXT2LING.get(ext) or EXT2LING.get(ext.lower())
            if ling:
                ling_arq[ling] += 1; ling_bytes[ling] += tam
                if tam < 5_000_000:
                    ling_linhas[ling] += ler().count(b"\n")
            if LICENSE.search(n) and tam < 200_000:
                txt = ler().decode("utf-8", "replace").strip().splitlines()
                licencas.append((rel, " ".join(l.strip() for l in txt[:3])[:140]))
            if BUILD.search(n) and tam < 2_000_000:
                build_txt.append(rel)
                txt = ler().decode("utf-8", "replace")
                for grupo, rot, rx in TECH:
                    if re.search(rx, txt):
                        tech[(grupo, rot)].add(rel)
    print(f"# Raio-X do código-fonte: {nome}\nArquivos: {total:,}\n")
    print("## Linguagens (arquivos · MB · linhas)")
    for ling, b in ling_bytes.most_common():
        print(f"- {ling}: {ling_arq[ling]:,} · {b/1e6:.1f} MB · {ling_linhas[ling]:,}")
    print("\n## Maiores pastas (MB)")
    for p, b in pastas.most_common(25):
        print(f"- {p}: {b/1e6:.1f}")
    print(f"\n## Tecnologias citadas nos arquivos de build ({len(build_txt)} arquivos lidos)")
    grupo_atual = None
    for (grupo, rot), fontes in sorted(tech.items()):
        if grupo != grupo_atual:
            print(f"### {grupo}"); grupo_atual = grupo
        ex = sorted(fontes, key=len)[:3]
        print(f"- {rot} ({len(fontes)}): " + ", ".join(ex))
    print("\n## Licenças")
    for rel, txt in licencas[:30]:
        print(f"- {rel}: {txt}")

if __name__ == "__main__":
    main()
