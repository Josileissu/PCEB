# CAD: GstarCAD, AutoCAD e o formato DXF

| Programa | Licença | Instalado aqui |
|---|---|---|
| GstarCAD 2021 | comercial | ✓ `C:\Program Files\Gstarsoft\GstarCAD2021`, com nosso plugin **Armadura BR 0.1.3** ✓ |
| AutoCAD | comercial | não — entra **via DXF** (requisito) |

## Funções que interessam
- Desenho 2D de formas e armaduras, blocos dinâmicos, atributos, layers, estilos de cota/texto, plotagem.
- Overrules / entidades customizadas (usado no Armadura BR).

## Importa / exporta
- DWG, **DXF** (texto ou binário, versões R12 a 2018), PDF, imagens.

## API / automação
- GstarCAD: .NET (compatível em grande parte com a do AutoCAD), LISP, GRX (C++).
- AutoCAD: .NET, ObjectARX, LISP, JavaScript.
- **Sem CAD instalado**, DXF pode ser lido/escrito por bibliotecas abertas:
  `ezdxf` (Python, MIT) e `netDxf` (C#, MIT) — a confirmar versões.

## Pré-moldados
- Hoje o escritório produz pranchas/fichas em CAD (prancha-modelo PP127 em PDF).

## O que aproveitar
- Plugin Armadura BR (ver memória do projeto) já resolve faixa com escala, comprimento real, estribos.
- DXF como **formato de saída universal** das pranchas geradas pelo programa web/Windows.

## A confirmar
- Padrões de layer/blocos do escritório (pedir template DWG); versão DXF exigida por clientes/fábricas.
