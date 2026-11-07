using System;
using System.Collections.Generic;
using DEB.Core.Templates;

namespace DEB.Core.Motor;

// =====================================================================================================================
// CONTRATOS DO MOTOR DE DETALHAMENTO (D19; proposta em docs/04_arquitetura/motor_contratos.md, 2026-11-07).
// Fluxo: ElementoEstrutural (dados do cálculo) + PerfilDetalhamento (padrão do escritório, JSON) + Sobrescritas (edições
// manuais) → IMotorDetalhamento → ModeloArmadura (neutro, 3D) → IGeradorPrancha → ResultadoTemplate (o CAD já desenha).
// Mudança nestes tipos = combinar antes (outras frentes e o PC1 dependem deles). Acrescentar campo opcional é permitido.
// Unidades: cm; sistema local do elemento: x, y em planta, z para cima (bloco: z = 0 no fundo da base).
// =====================================================================================================================

/// <summary>Ponto/vetor 3D em cm no sistema local do elemento.</summary>
public readonly struct P3 : IEquatable<P3>
{
    public double X { get; }
    public double Y { get; }
    public double Z { get; }
    public P3(double x, double y, double z) { X = x; Y = y; Z = z; }
    public static P3 operator +(P3 a, P3 b) => new P3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static P3 operator -(P3 a, P3 b) => new P3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static P3 operator *(P3 a, double k) => new P3(a.X * k, a.Y * k, a.Z * k);
    public double Comprimento => Math.Sqrt(X * X + Y * Y + Z * Z);
    public bool Equals(P3 o) => Math.Abs(X - o.X) < 1e-9 && Math.Abs(Y - o.Y) < 1e-9 && Math.Abs(Z - o.Z) < 1e-9;
    public override bool Equals(object obj) => obj is P3 o && Equals(o);
    public override int GetHashCode() => (Math.Round(X, 6), Math.Round(Y, 6), Math.Round(Z, 6)).GetHashCode();
    public override string ToString() => FormattableString.Invariant($"({X}; {Y}; {Z})");
}

// ---------------------------------------------------------------------------------------------------------------------
// 1. DADOS DO ELEMENTO (o que vem do cálculo ou do desenho; nada de padrão do escritório aqui)
// ---------------------------------------------------------------------------------------------------------------------

/// <summary>Armadura decidida pelo cálculo/engenheiro para uma função (bitola e quantidade/espaçamento).</summary>
public sealed class ArmaduraCalculada
{
    /// <summary>Função no vocabulário do motor (ex.: "principal", "secundaria", "calice_topo"; ver <see cref="Funcoes"/>).</summary>
    public string Funcao { get; set; } = "";
    /// <summary>mm; 0 = sem esta armadura.</summary>
    public double Bitola { get; set; }
    /// <summary>0 = pelo espaçamento.</summary>
    public int Quantidade { get; set; }
    /// <summary>cm; 0 = pela quantidade.</summary>
    public double Espacamento { get; set; }
}

/// <summary>Base dos elementos. Cada motor tem a sua subclasse (ElementoBloco, depois ElementoPilar…).</summary>
public abstract class ElementoEstrutural
{
    /// <summary>Tipo do elemento = chave do motor e do perfil (ex.: "bloco_estacas").</summary>
    public abstract string Tipo { get; }
    public string Nome { get; set; } = "";
    public List<string> Pilares { get; set; } = new List<string>();
    /// <summary>Peças iguais; 0 = número de pilares.</summary>
    public int Repeticoes { get; set; }
    /// <summary>Nível de referência por pilar (m). Bloco: topo do bloco.</summary>
    public List<NivelPilar> Niveis { get; set; } = new List<NivelPilar>();
    public double Fck { get; set; } = 30;
    public string Aco { get; set; } = "CA-50";
    public List<ArmaduraCalculada> Armaduras { get; set; } = new List<ArmaduraCalculada>();

    public ArmaduraCalculada Armadura(string funcao) => Armaduras.Find(a => a.Funcao == funcao);
}

public sealed class NivelPilar
{
    public string Pilar { get; set; } = "";
    public double CotaM { get; set; }
}

public enum ArranjoEstacas { Grade, Triangulo }
public enum LigacaoPilar { CaliceColarinho, CaliceEmbutido, Arranque }
public enum InterfaceCalice { Lisa, Rugosa, Chaves }

/// <summary>Bloco sobre estacas (B3, B4, B15, B28, B31, B3.1 — src/DEB.Cad/docs/BLOCOS_REQUISITOS.md).</summary>
public sealed class ElementoBloco : ElementoEstrutural
{
    public const string TipoId = "bloco_estacas";
    public override string Tipo => TipoId;

    public ArranjoEstacas Arranjo { get; set; } = ArranjoEstacas.Grade;
    public int EstacasX { get; set; } = 2;
    public int EstacasY { get; set; } = 1;
    /// <summary>Entre eixos em x (triangular: lado do triângulo).</summary>
    public double EspX { get; set; } = 120;
    public double EspY { get; set; } = 120;
    public double DiamEstaca { get; set; } = 40;
    /// <summary>Eixo da estaca → face do bloco (triangular: as 6 faces).</summary>
    public double DistFace { get; set; } = 35;
    public double EmbutimentoEstaca { get; set; } = 5;
    public double AlturaBase { get; set; } = 55;
    public double Lastro { get; set; } = 5;

    public double PilarBx { get; set; } = 30;
    public double PilarBy { get; set; } = 20;
    /// <summary>Deslocamento do pilar em relação ao centro das estacas.</summary>
    public double PilarEx { get; set; }
    public double PilarEy { get; set; }

    public LigacaoPilar Ligacao { get; set; } = LigacaoPilar.CaliceColarinho;
    public double AlturaColarinho { get; set; } = 40;
    public double ParedeCalice { get; set; } = 21;
    public double FolgaCalice { get; set; } = 4;
    /// <summary>Topo do bloco → base do pilar.</summary>
    public double Lemb { get; set; } = 50;
    public double Regularizacao { get; set; } = 5;
    public InterfaceCalice Interface { get; set; } = InterfaceCalice.Chaves;
    /// <summary>Arranque: barras por face (com os cantos) e cobrimento do pilar.</summary>
    public int ArranqueNx { get; set; } = 2;
    public int ArranqueNy { get; set; } = 2;
    public double ArranqueCobrimento { get; set; } = 3;
}

/// <summary>Vocabulário de funções (chaves de <see cref="ArmaduraCalculada.Funcao"/>, do perfil e das posições).</summary>
public static class Funcoes
{
    public const string Principal = "principal", Secundaria = "secundaria", Superior = "superior", Lateral = "lateral",
        EstriboVertical = "estribo_vertical", CaliceTopo = "calice_topo", Calice = "calice", CaliceVertical = "calice_vertical",
        Suspensao = "suspensao", FundoCalice = "fundo_calice", Arranque = "arranque", ArranqueEstribo = "arranque_estribo",
        Longitudinal = "longitudinal", Grampo = "grampo";
}

// ---------------------------------------------------------------------------------------------------------------------
// 2. MODELO DE ARMADURA NEUTRO (três camadas: intenção, geometria calculada, sobrescrita)
// ---------------------------------------------------------------------------------------------------------------------

public enum FormaBarra { Reta, U, Estribo, Grampo, Generica }

/// <summary>Edição manual (camada 3): o engenheiro forçou um parâmetro de uma posição. Sobrevive à regeneração.</summary>
public sealed class Sobrescrita
{
    /// <summary><see cref="PosicaoArmadura.Chave"/>.</summary>
    public string Posicao { get; set; } = "";
    /// <summary>Nome do parâmetro da intenção (ex.: "perna", "quantidade", "espacamento", "bitola").</summary>
    public string Parametro { get; set; } = "";
    public double Valor { get; set; }
}

/// <summary>Linha de distribuição das barras iguais de uma posição (3D).</summary>
public sealed class Distribuicao
{
    /// <summary>Centro da 1ª e da última barra (a barra representativa está no Inicio).</summary>
    public P3 Inicio { get; set; }
    public P3 Fim { get; set; }
    public int Quantidade { get; set; } = 1;
    public double Espacamento { get; set; }
    /// <summary>Distribuições iguais em outro lugar (ex.: grampos nas 2 paredes opostas = 2).</summary>
    public int Repeticoes { get; set; } = 1;
}

/// <summary>Uma posição de armadura (todas as barras iguais de uma função num lugar).</summary>
public sealed class PosicaoArmadura
{
    /// <summary>Chave estável (não muda ao regenerar; as sobrescritas apontam para ela). Ex.: "principal_x", "calice_topo".</summary>
    public string Chave { get; set; } = "";
    public string Funcao { get; set; } = "";
    /// <summary>Nome legível (vai para avisos e para a prancha).</summary>
    public string Nome { get; set; } = "";
    public FormaBarra Forma { get; set; }
    public double Bitola { get; set; }
    public string Aco { get; set; } = "CA-50";

    // Camada 1 — intenção: os parâmetros que geraram a geometria e a regra de cada um.
    /// <summary>Parâmetros nomeados (cm ou contagem): "perna", "perna2", "quantidade", "espacamento", …</summary>
    public Dictionary<string, double> Parametros { get; set; } = new Dictionary<string, double>();
    /// <summary>Regra legível de cada parâmetro calculado (ex.: "perna" → "topo da base − cobrimento").</summary>
    public Dictionary<string, string> Regras { get; set; } = new Dictionary<string, string>();
    /// <summary>Parâmetros que vieram de uma <see cref="Sobrescrita"/>.</summary>
    public HashSet<string> Manuais { get; set; } = new HashSet<string>();

    // Camada 2 — geometria calculada (nunca digitada): barra representativa em 3D, eixo da barra.
    public List<P3> Vertices { get; set; } = new List<P3>();
    public bool Fechada { get; set; }
    /// <summary>Formato de cada segmento (gancho, sobreposto…), como em BarraTemplate. Vazio = todos retos.</summary>
    public List<DEB.Core.Modelo.FormatoSegmento> Formatos { get; set; } = new List<DEB.Core.Modelo.FormatoSegmento>();
    public Distribuicao Distribuicao { get; set; } = new Distribuicao();
    /// <summary>Grupo VAR: comprimento externo de cada barra (a representativa é a de <see cref="Vertices"/>).</summary>
    public List<double> ComprimentosVar { get; set; } = new List<double>();
    public HashSet<int> SegmentosVar { get; set; } = new HashSet<int>();
    /// <summary>Geometria de cada barra do grupo VAR (opcional; a prancha usa para desenhar a malha completa).</summary>
    public List<List<P3>> VerticesVar { get; set; } = new List<List<P3>>();
    /// <summary>Segmentos desenhados sobre outro (passagem dos ganchos do estribo pelo canto, como em BarraTemplate):
    /// contam como aço, não são cotados. (Acrescentado pela frente B, 2026-11-07.)</summary>
    public HashSet<int> Sobrepostos { get; set; } = new HashSet<int>();
    /// <summary>Geometria de TODAS as barras da posição (todas as faixas/repetições), opcional; quando preenchida,
    /// Barras.Count = Quantidade × Repeticoes e a prancha desenha estas barras em vez de repetir a representativa ao longo
    /// da distribuição (malha interrompida no cálice, faixas da principal, grampos em paredes opostas…). (Frente B.)</summary>
    public List<List<P3>> Barras { get; set; } = new List<List<P3>>();
}

/// <summary>Corpo de concreto (ou vazio) para a forma e para os cortes: polígono em planta extrudado em z.</summary>
public sealed class CorpoConcreto
{
    /// <summary>"bloco", "colarinho", "furo_calice", "pilar", "estaca", "lastro", "regularizacao".</summary>
    public string Papel { get; set; } = "";
    /// <summary>Contorno em planta (anti-horário). Estaca/cilindro: vazio e use Centro + Raio.</summary>
    public List<(double X, double Y)> Planta { get; set; } = new List<(double, double)>();
    public (double X, double Y) Centro { get; set; }
    public double Raio { get; set; }
    public double Z0 { get; set; }
    public double Z1 { get; set; }
    /// <summary>Vazio (furo do cálice): subtrai do concreto.</summary>
    public bool Vazio { get; set; }
}

public sealed class ModeloArmadura
{
    public string TipoElemento { get; set; } = "";
    public string Nome { get; set; } = "";
    public List<CorpoConcreto> Corpos { get; set; } = new List<CorpoConcreto>();
    public List<PosicaoArmadura> Posicoes { get; set; } = new List<PosicaoArmadura>();
    /// <summary>Níveis nomeados (z em cm): "fundo", "topo_base", "topo", "fundo_pilar", "arrasamento".</summary>
    public Dictionary<string, double> Niveis { get; set; } = new Dictionary<string, double>();
    public List<string> Avisos { get; set; } = new List<string>();

    public PosicaoArmadura Posicao(string chave) => Posicoes.Find(p => p.Chave == chave);
}

// ---------------------------------------------------------------------------------------------------------------------
// 3. PERFIL DO ESCRITÓRIO (JSON; fábrica embutida + arquivo do usuário em %AppData%/PCEB/Perfis/<id>.json)
// ---------------------------------------------------------------------------------------------------------------------

public enum TipoVista
{
    /// <summary>Planta da forma, cotada.</summary>
    FormaPlanta,
    /// <summary>Corte da forma com níveis.</summary>
    FormaCorte,
    /// <summary>Vista superior das posições listadas.</summary>
    Planta,
    /// <summary>Corte vertical com as posições listadas.</summary>
    Corte,
    /// <summary>Barras dobradas (formato e cotas) das posições listadas.</summary>
    Barras,
}

public sealed class VistaPerfil
{
    public string Id { get; set; } = "";
    /// <summary>Título (aceita "{nome}"); PT/EN separados por "|" ("CORTE A-A|SECTION A-A").</summary>
    public string Titulo { get; set; } = "";
    public TipoVista Tipo { get; set; }
    /// <summary>Corte: plano "x" (paralelo a xz, passando pelo pilar) ou "y" (paralelo a yz).</summary>
    public string Plano { get; set; } = "";
    /// <summary>Funções ou chaves de posição mostradas ("principal", "calice_*"); vazio = todas.</summary>
    public List<string> Posicoes { get; set; } = new List<string>();
    /// <summary>Desenhar as barras dobradas (com cotas) embaixo da vista.</summary>
    public bool BarrasAbaixo { get; set; }
    /// <summary>Quadro da folha (0 = forma, 1 = armadura) e ordem dentro dele (esquerda → direita, linha a linha).</summary>
    public int Quadro { get; set; } = 1;
    public int Linha { get; set; }
    public int Ordem { get; set; }
}

public sealed class FolhaPerfil
{
    /// <summary>"PT", "EN" ou "PT/EN".</summary>
    public string Idioma { get; set; } = "PT";
    public double Escala { get; set; } = 20;
    /// <summary>Ex.: "BLOCO {nome} - {repeticoes}x - {estacas} ø {diametro}".</summary>
    public string Titulo { get; set; } = "";
    public List<VistaPerfil> Vistas { get; set; } = new List<VistaPerfil>();
    /// <summary>Detalhes padrão: "chaves", "raio_curvatura", "emendas", "comprimento_barras", "ancoragem_estacas", "barra_variavel".</summary>
    public List<string> Detalhes { get; set; } = new List<string>();
    public bool RelacaoAco { get; set; } = true;
    public bool InformacoesProducao { get; set; } = true;
    /// <summary>Notas gerais (uma por item).</summary>
    public List<string> Notas { get; set; } = new List<string>();
    /// <summary>Textos da prancha que dependem do elemento/escritório, "PT|EN" (opcional, frente C, 2026-11-07). Chaves:
    /// "nivel.&lt;nível&gt;" (rótulo do nível no corte da forma; só aparecem os níveis com rótulo), "papel.&lt;papel&gt;" (texto
    /// do corpo no corte, aceita {e} = espessura), "peca" ("BLOCO|PILE CAP"), "grupo" (DadosDetalhamento.Grupo),
    /// "nivel_referencia" (nível a que <see cref="ElementoEstrutural.Niveis"/> se refere; padrão "topo"). Faltando, valem os
    /// padrões de <c>DEB.Core.Motor.Prancha.VocabularioPrancha</c>.</summary>
    public Dictionary<string, string> Rotulos { get; set; } = new Dictionary<string, string>();
}

public sealed class PerfilDetalhamento
{
    public string Id { get; set; } = "";
    public string Nome { get; set; } = "";
    public string TipoElemento { get; set; } = "";
    /// <summary>Valores padrão do escritório: "cobrimento.solo" = 4, "secundaria.perna" = 20, …</summary>
    public Dictionary<string, double> Padroes { get; set; } = new Dictionary<string, double>();
    /// <summary>Regras escolhidas: "principal.perna" = "altura_base" | "valor", …</summary>
    public Dictionary<string, string> Regras { get; set; } = new Dictionary<string, string>();
    public FolhaPerfil Folha { get; set; } = new FolhaPerfil();

    public double Padrao(string chave, double seFaltar) => Padroes.TryGetValue(chave, out var v) ? v : seFaltar;
    public string Regra(string chave, string seFaltar) => Regras.TryGetValue(chave, out var v) ? v : seFaltar;
}

// ---------------------------------------------------------------------------------------------------------------------
// 4. INTERFACES
// ---------------------------------------------------------------------------------------------------------------------

/// <summary>Regras de um tipo de elemento: dados + perfil + sobrescritas → modelo de armadura. Sem desenho, sem CAD.</summary>
public interface IMotorDetalhamento<TElemento> where TElemento : ElementoEstrutural
{
    string TipoElemento { get; }
    /// <summary>Aplica as sobrescritas no lugar das regras (marcando em <see cref="PosicaoArmadura.Manuais"/>) e avisa se
    /// algum valor manual sair da norma. Sobrescrita de posição inexistente vira aviso, nunca erro.</summary>
    ModeloArmadura Gerar(TElemento elemento, PerfilDetalhamento perfil, IReadOnlyList<Sobrescrita> sobrescritas);
}

/// <summary>Monta a prancha (vistas, cortes, faixas, chamadas, tabelas, detalhes) a partir do modelo e do perfil.
/// Comum a todos os elementos; o que é específico entra pelo perfil.</summary>
public interface IGeradorPrancha
{
    ResultadoTemplate Gerar(ElementoEstrutural elemento, ModeloArmadura modelo, PerfilDetalhamento perfil);
}
