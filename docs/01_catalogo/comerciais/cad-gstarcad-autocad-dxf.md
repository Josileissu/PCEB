# CAD: GstarCAD, AutoCAD e o formato DXF

| Programa | Licença | Instalado aqui |
|---|---|---|
| **GstarCAD 2021** (21.0.0.1) | comercial (Gstarsoft, China); chave por **FlexNet** ou dongle ✓ | ✓ PC1, `C:\Program Files\Gstarsoft\GstarCAD2021` (0,5 GB), com o **DEB.Cad** (ex-Armadura BR) |
| AutoCAD | comercial (Autodesk) | PC2 (informado pelo usuário); o DEB.Cad compila para AutoCAD 2021–2027 |
| ZWCAD | comercial | previsto no DEB.Cad |

Levantamento de 05/11/2026 pelos arquivos instalados, ajudas (`Help\gcad_new.chm` — novidades da 2021,
`gcad_gcr.chm` — 601 tópicos de comandos) e a experiência do DEB.Cad (`src/DEB.Cad/CLAUDE.md`).

## Arquitetura e tecnologia ✓
- Programa nativo (MFC, interface `DuiLib`/skins); **núcleo DWG da ODA** (Teigha, compilação própria `3.05src`),
  **ACIS** (sólidos 3D), DGN, DWF, PDF (importar, exportar, **PDF para DXF**), STL, nuvem de pontos, **IFC 2x3**
  (objetos AEC), código de barras e QR code; **Git** (`git2.dll`) e **LevelDB** embutidos (colaboração e nuvem `gdn`,
  Dropbox, Baidu).
- Licença FlexNet (`glm*`) ou chave USB.

## API / automação ✓
| Via | O que é | Uso no PCEB |
|---|---|---|
| **.NET** | compatível em grande parte com a do AutoCAD: 2021–2023 `gmap/gmdb` (`GrxCAD.*`), 2024–2025 `GcMgd` (`Gssoft.Gscad.*`), 2026–2027 em .NET 8 | **DEB.Cad** (net48 x64), com Overrules e XData |
| **GRX** (C++) | equivalente ao ObjectARX (`gcrx.dll`, módulos `.grx`) | não usado (decisão: .NET + Overrules, DWG legível sem plugin) |
| **COM/ActiveX** | `gcax.tlb` (`GStarCAD.Application`) | leitura do desenho aberto do usuário |
| **LISP / DCL / OpenDCL** | `APPLOAD`, `base.dcl`, `Opendcl.grx` | scripts `.scr` do autoteste |

## Funções que interessam ✓
- Desenho 2D completo, blocos dinâmicos, atributos, layers, estilos, tabelas (**tabela ↔ Excel**, `AutoXlsTable`),
  plotagem, folhas e conjuntos de folhas, multileaders.
- Ferramentas da 2021: **comparação de desenhos**, travamento de desenho, purga em lote, cópia incremental de texto,
  soma de áreas e tabela de áreas, contorno de objetos, desenho simétrico, camada automática.

## Limitações conhecidas (experiência do DEB.Cad)
- Transação abortada em eventos desliga notificações do comando seguinte (sempre `Commit`).
- `QueueForGraphicsFlush` não implementado; `PaletteSet.AutoRollUp/RolledUp` lançam `NotImplementedException`.
- Mesclar células de tabela depois de escrever zera alturas de texto; `Geometry.WorldLine` não aparece nas viewports
  do layout (usar `Geometry.Polyline`).
- A DLL carregada não é solta (o plugin do usuário roda de uma cópia); só a 2021 está instalada para teste.

## DXF sem CAD
- Bibliotecas abertas para ler/gravar DXF sem programa de CAD: **ezdxf** (Python, MIT) e **netDxf** (C#, MIT).
- DWG: só com ODA (paga) ou RealDWG (Autodesk); o **ODA File Converter** gratuito converte DWG ↔ DXF em lote.

## O que aproveitar para o nosso programa
1. **DEB.Cad** já resolve o detalhamento 2D (barras, estribos, faixas, telas, treliças, cordoalhas, RA) e é a
   referência das regras (D7); o motor D19 gera o modelo neutro e o DEB.Cad desenha.
2. **DXF como saída universal** do PCEB web/Windows (ezdxf/netDxf), DWG só quando o cliente exigir.
3. **Comparação de desenhos** do GstarCAD — útil para conferir prancha gerada × prancha de referência.

## A confirmar
- Versões do AutoCAD no PC2 e teste do DEB.Cad nelas (2022–2027 compilam, mas não foram testadas).
- Versão DXF/DWG exigida por clientes e fábricas.
