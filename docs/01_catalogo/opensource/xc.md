# XC (xcfem)

| Origem | Licença | Linguagem | Instalado aqui |
|---|---|---|---|
| Luis C. Pérez Tato e colaboradores (github.com/xcfem/xc) | **GPL-3** ✓ — mas o núcleo **deriva do OpenSees** ✓ e o aviso da UC Berkeley vai junto (`doc/copyright_opensees.txt`) ✓ | C++ (núcleo) ✓, uso por **Python** ✓ (módulos em `python_modules/`) | não (código-fonte em `programas/xcfem/download`) |

## Funções
- **Modelagem**: por script Python; geração de geometria e malha própria (pontos, linhas, superfícies) ✓; sem GUI (pós em VTK/ParaView).
- **Análise**: linear e não linear, estática e dinâmica, modal, **ativação/desativação de elementos (fases construtivas)**,
  confiabilidade (FORM/SORM, herdado do OpenSees) ✓.
- **Elementos**: 0D, 1D (barras, vigas, inclusive fibras), 2D (cascas, placas), 3D ✓.
- **Ações** (`python_modules/actions`) ✓: cargas básicas, empuxo de terra, protensão, sismo, vento, neve, térmica,
  tráfego rodoviário/ferroviário/pedestres, combinações.
- **Materiais e normas** (`python_modules/materials`) ✓: EC2, EC3, EC5, ACI, AISC/ASTM, AISI, AWC NDS, EHE, EAE, SIA 262/263,
  Model Code 2010, CEB, **bielas e tirantes** (`strut_and_tie`), protensão, seções. **Sem NBR** ✓.
- **Dimensionamento**: sim, verificações por norma estrangeira (concreto armado, aço, madeira) — o mais próximo de um
  "programa de cálculo completo" entre os abertos.
- **Outros** ✓: geotecnia (fundações, contenções, túneis), ligações metálicas (CBFEM em andamento), exporta SCIA XML;
  IFC em andamento (IFC++).
- **Testes**: ~2 500 arquivos em `verif/` ✓.

## Avaliação (provisória)
| Critério | Nota | Comentário |
|---|---|---|
| Maturidade | 3 | anos de uso em projetos de pontes/estruturas na Espanha; comunidade pequena |
| Manutenção | 4 | ativo no GitHub |
| Documentação | 2 | pouca; aprende-se pelos exemplos de `verif/` |
| Integração | 3 | Python direto; instalação em Linux (Docker recomendado) ✓; Windows a confirmar |
| Aderência | 4 | combinações, verificações por norma, fases, bielas-tirantes — falta só a NBR |
| Licença | 2 | **mesma restrição do OpenSees** no código derivado (uso comercial fora do "uso interno" precisa da UC) |

## Uso possível para nós
- **Modelo de organização**: como estruturar ações, combinações e verificações por norma em módulos — referência para o núcleo PCEB.
- Comparar resultados de verificação EC2 × nossa NBR 6118 (as normas são parentes).

## A confirmar
- Interpretação jurídica da mistura GPL-3 + aviso OpenSees (uso interno no escritório parece permitido; distribuir junto, não).
- Funciona no Windows sem Docker?

## Raio-X do código-fonte (05/11/2026) ✓
Lido direto do pacote em `programas/*/download` com `docs/notas/ferramentas/raiox_fonte.py` (sem compilar nem rodar).
- 7.390 arquivos: C++ ~690 mil linhas (núcleo derivado do OpenSees), **Python ~350 mil** (`python_modules`).
- Dependências: CGAL, Boost, Eigen, VTK/OpenGL (pós), SQLite, MUMPS, UMFPACK, ARPACK, MPI, OpenMP; Gmsh opcional.
- **Normas em `python_modules/materials`**: ACI, AISC, AISI, API, AWC NDS, CEB, MC10, **EC2**, EC3, EC5, **EHE**, EAE,
  SIA 262/263, EN 1337 (aparelhos de apoio), EOTA, protensão, **bielas e tirantes** (`strut_and_tie`), `tie_bars`.
  **Nenhuma NBR** ✓.
- Armadura em seções de fibras (`ReinfLayer`, `ReinfBar`, `column_main_reinforcement.py`) e um esquema de armadura
  de parede em **TikZ/LaTeX** (`draw_wall_reinforcement_scheme.py`) — para relatório, **não é detalhamento de prancha**.
- Ligações metálicas em `python_modules/connections/steel_connections`. 86 MB de testes de verificação (`verif/tests`).
- Estrutura de normas em módulos Python separados é um bom modelo para a nossa NBR 6118/9062 no núcleo.
