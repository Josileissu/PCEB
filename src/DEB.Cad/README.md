# Detalhador Estrutural Brasileiro

Plugin .NET para **detalhamento, numeração e quantificação de armaduras** dentro do CAD.
Desenha barras, estribos, treliças, telas, faixas, chamadas e cortes, mantém a **relação do aço**
atualizada e organiza os detalhamentos em blocos — usando apenas entidades nativas
(`LWPOLYLINE`, `MTEXT`, `TEXT` e `TABLE`) com XData `ARMADURA_BR`.

> **O plugin não dimensiona estruturas.** Ele só desenha, numera e quantifica; as decisões de
> cálculo são do engenheiro responsável.

Versão atual: **0.1.25**

## Programas suportados

| Programa | Versões | Framework | Situação |
|---|---|---|---|
| GstarCAD | 2021–2027 | .NET Framework 4.8 (2021–2025) / .NET 8 (2026–2027) | 2021 testada; 2022–2027 compilam, sem teste no CAD |
| AutoCAD | 2021–2027 | .NET Framework 4.8 (2021–2024) / .NET 8 (2025–2026) / .NET 10 (2027) | compila usando o pacote oficial `AutoCAD.NET` do NuGet |
| ZWCAD | 2024–2026 | .NET Framework 4.8 | compila; sem teste no CAD |

O desenho continua legível em computadores **sem** o plugin: sem ele, apenas deixam de acontecer
as atualizações automáticas de cotas, descrições e relação do aço.

## Principais recursos

- **Barras** — desenho por pontos ou por cobrimento, conversão de polilinhas, edição de segmentos
  (gancho 45°/90°/180°, VAR, cota), recoteamento, rebatimento vinculado e divisão com traspasse.
- **Estribos** — simples, aberto, múltiplo, pente e circular (interno/externo), com seção,
  rebatido e faixas; ganchos no canto superior esquerdo.
- **Treliças, telas e cordoalhas** — modelos de catálogo (CP-190) e desenho por dois pontos.
- **Faixas e chamadas** — ortogonal/alinhada, corte + cota, corte + chamada (reta/arco),
  chamada com quantidade e indicação simples; a quantidade da barra é a soma das faixas.
- **Relação do aço** — tabela com apresentação, resumo, peso total e detalhe de dobra;
  numeração global/individual/sequencial, relações parciais por detalhamento, bloqueio de revisão
  e lista de corte e dobra em CSV + HTML.
- **Detalhamentos em blocos** — criação a partir da seleção, propriedades por detalhamento,
  grade de gerenciamento e relação parcial por bloco.
- **Paleta de propriedades** — edição de barras, faixas, tabelas e detalhamentos; paleta rápida
  de propriedades e paleta de comandos.
- **Integração** — importação de arquivos `.cad` do QiArmaduras, templates de elementos,
  configuração por desenho/usuário, camadas padrão e autoteste integrado (`ARMTESTE`).

## Estrutura do repositório

```
src/
  DEB.Core/        Núcleo sem CAD: modelo, normas, comprimentos, descrição,
                        numeração, relação do aço, codec XData (netstandard2.0)
  DEB.Cad/         Fontes compartilhados da camada CAD (sem .csproj):
                        sincronizador de eventos, serviços, overrules e comandos
  DEB.GstarCAD/    Projeto GstarCAD (GrxCAD.*)
  DEB.AutoCAD/     Projeto AutoCAD (Autodesk.AutoCAD.*)
  DEB.ZWCAD/       Projeto ZWCAD (ZwSoft.ZwCAD.*)
  DEB.Carregador/  Carregador do GstarCAD (carrega uma cópia da DLL)
tests/
  DEB.Core.Tests/  Testes do núcleo com xUnit (net8.0)
instalador/             Script Inno Setup, bundle do AutoCAD e scripts de geração
sdk/                    Bibliotecas de referência dos SDKs (GstarCAD/ZWCAD)
docs/                   Arquitetura, formato QiCAD, templates e progresso
tools/                  Scripts de autoteste no CAD
```

## Compilar

Requisitos: .NET SDK 8 (e .NET 10 para o AutoCAD 2027) e Windows x64.

```powershell
# Núcleo + testes (não precisa de CAD instalado)
dotnet test tests\DEB.Core.Tests

# AutoCAD (padrão 2026) -> bin\AutoCAD2026\
dotnet build src\DEB.AutoCAD -c Release
# outras versões: -p:AutoCadVersao=2021 .. 2027

# GstarCAD (padrão 2021) -> bin\GstarCAD2021\
dotnet build src\DEB.GstarCAD -c Release
# outras versões: -p:GstarVersao=2022 .. 2027

# ZWCAD (padrão 2026) -> bin\ZWCAD2026\
dotnet build src\DEB.ZWCAD -c Release
# outras versões: -p:ZwcadVersao=2024 .. 2026

# Versão de demonstração (1 hora de uso) -> bin\demo\<CAD><versão>\
dotnet build src\DEB.AutoCAD -c Release -p:Demo=true
```

Os binários saem em `bin\<CAD><versão>\`, com `AppendTargetFrameworkToOutputPath=false`.
As bibliotecas do AutoCAD vêm do NuGet oficial; as do GstarCAD/ZWCAD ficam em `sdk\` apenas
como referência de compilação (não são copiadas para a saída).

## Instalar

A forma recomendada é o instalador único (`instalador\saida\DEB_Setup_<versão>.exe`),
que detecta GstarCAD, AutoCAD e ZWCAD instalados e registra o autoload de cada um.
Para gerar um novo instalador:

```bat
instalador\gerar.bat
```

Requisitos do script: Inno Setup 6 em `%LocalAppData%\Programs\Inno Setup 6` e .NET 10 SDK em
`%LocalAppData%\Microsoft\dotnet10` (para o AutoCAD 2027).

Instalação manual e desinstalação:

- `INSTALACAO.txt` — GstarCAD e visão geral;
- `INSTALACAO_AUTOCAD.txt` — AutoCAD (autoloader em `%AppData%\Autodesk\ApplicationPlugins`).

No dia a dia, use `ARMPALETA` para abrir a paleta, `ARMCOMANDOS` para a paleta de botões e
`ARMSOBRE` para ver a versão. A lista completa de comandos está no fim dos arquivos de instalação.

## Testes

- Núcleo: `dotnet test tests\DEB.Core.Tests` (178 testes).
- Integração no CAD: `tools\autoteste.ps1` (GstarCAD) e `tools\autoteste_autocad.ps1` (AutoCAD)
  rodam `ARMTESTE` em uma instância nova; o relatório fica em
  `%AppData%\ArmaduraBR\logs\autoteste.txt`.

## Documentação

- [Arquitetura e modelo de dados](docs/ARQUITETURA.md)
- [Templates de elementos](docs/TEMPLATES.md)
- [Formato de importação QiCAD](docs/FORMATO_QICAD.md)
- [Requisitos do pilar pré-moldado](docs/PILAR_PREMOLDADO_REQUISITOS.md)
- [Progresso do projeto](docs/PROGRESSO.md)

## Licença

GNU General Public License v3.0 — veja [LICENSE](LICENSE).

O arquivo [LICENSE-EXCEPTION.md](LICENSE-EXCEPTION.md) concede a permissão adicional da seção 7
da GPL-3 para combinar o programa com APIs proprietárias de CAD (AutoCAD/ObjectARX, GstarCAD/GRX,
ZWCAD/ZRX, Revit, Robot, SAP2000/ETABS e TQS). As bibliotecas dessas APIs não são redistribuídas
pelo projeto e devem ser obtidas sob as licenças dos respectivos fabricantes.

---

Autor: Marquezin · Repositório: <https://github.com/Josileissu/PCEB>
