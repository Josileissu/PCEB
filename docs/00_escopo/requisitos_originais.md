# Requisitos originais (texto do usuário, sem alterações)

> Guardado como referência. A versão organizada está em `escopo_organizado.md`.

PROGRAMA PARA ESCRITÓRIO DE ENGENHARIA ESTRUTURAL:

REFERÊNCIAS:
1.    Programas de cálculo, detalhamento, desenho, modeladores, comerciais e opensource;
2.    Tekla, TQS, AltoQi, CYPE, SAP2000, ETABS, REVIT, GstarCAD e AutoCAD (via dxf), OpenSeeS, Calculix, IDEA STATICA, Blender, gmsh, prepomax (https://gitlab.com/MatejB/PrePoMax). Sectionproperties.
3.    Arquivos de bibliografias específicas, normas, livros, contidos em pasta biblioteca para a produção do programa/plugin.

Contém:
1.    Interface WEB/Navegador para estudo, análise estrutural e outros.
2.    Plugin para REVIT e CAD(dxf) para automações e documentação de projetos.
3.    Revit como interface para estrutura 3D?
Requisitos:

1.    Interface navegador/web:
1.1.    Inicialmente rodando local;
1.2.    Segunda etapa rodando em host/online/nuvem;

2.    Programa windows com as mesmas funcionalidades do que o navegador/web possui.

3.    Organização do desenvolvimento:
3.1.    Ambiente de desenvolvimento;
3.2.    Ambiente de produção;

4.    Conteúdo e o que a solução resolve:
4.1.    Análise estrutural;
4.2.    Dimensionamento estrutural;
4.3.    Detalhamento estrutural;
4.4.    Projetos de fabricação;
4.5.    Emissão de arquivos e documentação:
4.5.1.    Extensões: ifc, dxf, xls, csv, xml, json, cam, step, etc;
4.6.    Exportador e importador de arquivos auxiliares:
4.6.1.    Integração com outras ferramentas de análise e programas de desenho/edição;
4.6.2.    SAP2000: s$k, s2k;

5.    Tipos de materiais e estruturas que vamos trabalhar:
5.1.    Inicial: pré-moldados;

6.    PLUGIN DE AUTOMAÇÃO REVIT:
6.1.    Ambiente de programação de plugin para revit. Adotar boas práticas de programação e organização dos arquivos para que seja modular.

7.    INFORMAÇÕES PARAMETRIZADAS:
7.1.    Criação de famílias RVT para trabalho
7.2.    Seções de vigas, pilares, ademais;
7.3.    Modelos de vistas padronizados (ver como ser dinâmica a faixa de vista);

8.    ELABORE PLANO DE TRABALHO E ETAPAS PARA CRIAÇÃO DO PROGRAMA.
8.1.    Vamos começar com detalhamento de vigas pré-moldadas, passando por todos os aspectos normativos, com as suas variações e nossas necessidades de escritório. Exportando um detalhamento condizente com nossos padrões e todas as variações necessárias;
8.2.    Detalhamento de pilares, idem vigas pré-moldadas;
8.3.    Detalhamento de blocos de fundação;
8.4.    etc;
