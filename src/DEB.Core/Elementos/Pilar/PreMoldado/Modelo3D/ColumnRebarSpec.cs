using System;
using System.Collections.Generic;
using System.Linq;
using DEB.Core.Normas.Verificacao;
using APL = DEB.Core.Elementos.Pilar.PreMoldado.ArmaduraPorLinha;
using VP = DEB.Core.Normas.Verificacao.VerificacoesPilar;

namespace DEB.Core.Elementos.Pilar.PreMoldado.Modelo3D
{
    /// <summary>
    /// Armadura escolhida para um grupo de pilares. Todas as medidas em mm.
    /// Serializável: os modelos salvos ficam em %AppData%\PCEB\DEB.Revit\ModelosArmaduraPilar.json.
    /// </summary>
    public sealed class ColumnRebarSpec
    {
        public string Name { get; set; } = "";

        // ---- Geral ----
        public double CoverMm { get; set; } = 25;
        /// <summary>Classe de agressividade ambiental ("I" a "IV"): cobrimento exigido, com as reduções da NBR 9062 (verificações G1/G2).</summary>
        public string ExposureClass { get; set; } = "II";
        /// <summary>fck (MPa), para o cobrimento reduzido (NBR 9062, 9.2.1.1.2) e o aviso dos concretos C55+.</summary>
        public double FckMPa { get; set; } = 40;
        /// <summary>Relação a/c ≤ 0,45 declarada (libera a 2ª redução do cobrimento com fck ≥ 40 MPa).</summary>
        public bool WaterCement045 { get; set; }
        /// <summary>Dimensão máxima do agregado graúdo (mm): espaçamento livre (P5) e cobrimento (G4).</summary>
        public double AggregateMm { get; set; } = 19;
        /// <summary>Aço das longitudinais (espaçamento dos estribos: 12φ no CA-50, 24φ no CA-25).</summary>
        public string LongSteel { get; set; } = "CA-50";
        /// <summary>Aço dos estribos (fyk na alternativa φt &lt; φ/4).</summary>
        public string StirrupSteel { get; set; } = "CA-50";

        // ---- Longitudinal ----
        // Desde a D32 (17/11/2026) as longitudinais saem da armadura por linha (ArmaduraPorLinha, abaixo), a mesma regra do
        // template do pilar do CAD. Os três campos simples ficam para ler os modelos salvos antes dela: sem a lista, as linhas
        // nascem deles (Linhas(), ArmaduraPorLinha.DaArmaduraSimples) e as barras saem iguais às de antes.
        public double LongDiameterMm { get; set; } = 12.5;
        /// <summary>Barras em cada face paralela a X (inclui os cantos). Só para modelos sem armadura por linha.</summary>
        public int LongBarsAlongX { get; set; } = 2;
        /// <summary>Barras em cada face paralela a Y (inclui os cantos). Só para modelos sem armadura por linha.</summary>
        public int LongBarsAlongY { get; set; } = 2;
        /// <summary>Cobrimento nas pontas (topo e base) das barras longitudinais.</summary>
        public double LongEndCoverMm { get; set; } = 25;
        /// <summary>
        /// Armadura por linha (D32): as linhas da tabela "Longitudinais" do CAD — arranjo, barras por face, bitola (mm), feixe e
        /// início e fim (cm a partir da base da peça, a exceção às medidas em mm, como no CAD). Null = modelo salvo antes da D32
        /// (as linhas nascem dos campos simples acima).
        /// </summary>
        public List<LinhaArmaduraPorLinha> ArmaduraPorLinha { get; set; }
        /// <summary>Canto em "L" (arranjo "Ao lado dos cantos"): distância entre eixos, como o "CantoEsp" do CAD (padrão 5 cm).</summary>
        public double LongCornerSpacingMm { get; set; } = 50;

        // ---- Estribos ----
        public double StirrupDiameterMm { get; set; } = 5;
        public double StirrupSpacingMm { get; set; } = 150;
        /// <summary>Sem uso desde a D30 (os estribos começam no cobrimento das pontas, LongEndCoverMm, como no CAD); fica para ler
        /// os modelos salvos.</summary>
        public double StirrupEndOffsetMm { get; set; } = 50;
        public bool CrossTies { get; set; } = true;

        // ---- Zonas de estribos ----
        public double TopZoneLengthMm { get; set; } = 500;
        public double TopZoneSpacingMm { get; set; } = 100;
        public double BaseZoneLengthMm { get; set; } = 0;
        public double BaseZoneSpacingMm { get; set; } = 100;
        /// <summary>Quanto a zona do consolo se estende acima e abaixo dele.</summary>
        public double CorbelZoneExtensionMm { get; set; } = 200;
        public double CorbelZoneSpacingMm { get; set; } = 75;

        // ---- Consolo (NBR 9062) ----
        public bool CorbelRebar { get; set; } = true;
        public double TieDiameterMm { get; set; } = 12.5;
        public int TieCount { get; set; } = 3;
        /// <summary>Perna vertical do tirante dentro do pilar (ancoragem), medida externa como no CAD (D30/D7, desde 16/11/2026).</summary>
        public double TieInnerLegMm { get; set; } = 400;
        public double SewDiameterMm { get; set; } = 8;
        /// <summary>Estribos horizontais de costura, distribuídos em 2/3 d abaixo do tirante.</summary>
        public int SewCount { get; set; } = 3;
        /// <summary>Barra de ancoragem soldada na ponta do tirante (padrão do escritório; tirante em L). Sem ela, o tirante
        /// desce na ponta (U invertido).</summary>
        public bool TieWeldedBar { get; set; } = true;
        /// <summary>Estribos verticais do consolo (fechados no plano transversal, repetidos ao longo do balanço).</summary>
        public int CorbelStirrupCount { get; set; } = 5;
        public double CorbelStirrupDiameterMm { get; set; } = 6.3;
        /// <summary>Armadura inferior do consolo (U com as pernas para cima, entrando no pilar).</summary>
        public int CorbelBottomCount { get; set; } = 2;
        public double CorbelBottomDiameterMm { get; set; } = 10;
        /// <summary>Pinos (chumbadores em L) no meio da almofada: acima do topo, embutido e pé.</summary>
        public int CorbelPinCount { get; set; } = 1;
        public double CorbelPinDiameterMm { get; set; } = 20;
        public double CorbelPinAboveMm { get; set; } = 420;
        public double CorbelPinEmbedMm { get; set; } = 180;
        public double CorbelPinFootMm { get; set; } = 200;

        // ---- Içamento / insertos ----
        // Alças como as do CAD (D30, item 5, 16/11/2026): simétricas ao CG ao longo do eixo, a ~LiftEndPercent % das pontas
        // (TemplatePilarPreMoldado.PosicaoAlcas), pernas entrando no pilar até por dentro das longitudinais da face oposta
        // (TemplatePilarPreMoldado.EmbutimentoAlca), com o pé dobrado lá.
        /// <summary>Cordoalhas por ponto de içamento (2 = o "AlcaPernas" padrão do CAD; D30, leitura do coordenador).</summary>
        public int LiftLoops { get; set; } = 2;
        /// <summary>
        /// Face das alças, na numeração apresentada do CAD (1 = oeste/−x, 2 = sul/−y, 3 = leste/+x, 4 = norte/+y), escolhida pelo
        /// usuário como o "AlcaFace" do CAD (padrão 2). 0 = a regra automática antiga (face com consolos para cima, LiftFace).
        /// </summary>
        public int LiftFaceNumber { get; set; } = 2;
        /// <summary>Diâmetro da cordoalha CP-190 da alça (o CAD usa 12,7).</summary>
        public double LiftDiameterMm { get; set; } = 12.7;
        /// <summary>Embutimento das pernas, da face da alça até o lado de fora do pé; limitado ao lado de dentro das longitudinais da
        /// face oposta (0 ou maior que o pilar = até lá, como o "0 = até o fundo" do CAD).</summary>
        public double LiftEmbedMm { get; set; } = 800;
        /// <summary>Altura da alça acima da face (eixo do topo da alça).</summary>
        public double LiftProtrusionMm { get; set; } = 150;
        /// <summary>Distância entre as pernas (eixos); 100 = o "AlcaEntre" padrão do CAD.</summary>
        public double LiftWidthMm { get; set; } = 100;
        /// <summary>Pé: dobra no fim de cada perna, junto da face oposta, para fora (o "AlcaPe" do CAD).</summary>
        public double LiftFootMm { get; set; } = 200;
        /// <summary>Distância das alças às pontas, % do comprimento, partindo do CG (o "AlcaPct" do CAD).</summary>
        public double LiftEndPercent { get; set; } = 20;
        /// <summary>Sem uso desde 16/11/2026 (as 2 cordoalhas do ponto ficam coladas); mantido para ler modelos salvos.</summary>
        public double LiftSpacingMm { get; set; } = 200;
        /// <summary>Família de inserto a posicionar no topo (vazio = nenhum). Formato "Família" ou "Família : Tipo".</summary>
        public string InsertFamily { get; set; } = "";

        public ColumnRebarSpec Clone()
        {
            var c = (ColumnRebarSpec)MemberwiseClone();
            c.ArmaduraPorLinha = ArmaduraPorLinha?.Select(l => l.Copia()).ToList();   // cópia própria das linhas
            return c;
        }

        /// <summary>As linhas da armadura por linha: as gravadas, ou as que nascem dos campos simples (modelo salvo antes da D32).</summary>
        public List<LinhaArmaduraPorLinha> Linhas() =>
            ArmaduraPorLinha ?? APL.DaArmaduraSimples(LongDiameterMm, LongBarsAlongX, LongBarsAlongY);

        /// <summary>
        /// Maior bitola longitudinal (mm; feixe: o diâmetro do envoltório), a mesma conta do <see cref="TemplatePilarPreMoldado.BitolaMaxLongitudinal"/>
        /// do CAD: define onde o tirante, a costura e as pernas das alças param dentro do pilar. Sem linhas válidas: LongDiameterMm.
        /// </summary>
        public double MaxLongDiameterMm() =>
            APL.Ler(Linhas(), 0, 1e9, new List<string>()).Select(l => 20 * Motor.SecaoBarras.RaioFeixe(l.Bitola, l.Feixe)).DefaultIfEmpty(LongDiameterMm).Max();

        /// <summary>Menor bitola das linhas (mm; uma barra), para o espaçamento máximo dos estribos (12 φ). Sem linhas: LongDiameterMm.</summary>
        public double MinLongDiameterMm() =>
            APL.Ler(Linhas(), 0, 1e9, new List<string>()).Select(l => l.Bitola).DefaultIfEmpty(LongDiameterMm).Min();

        /// <summary>Padrão para o pilar: zona de base = embutimento; espaçamentos dentro dos limites da NBR 6118.</summary>
        public static ColumnRebarSpec Default(ColumnBox frame, double embedmentMm, IList<Corbel> corbels)
        {
            var s = new ColumnRebarSpec { Name = "Padrão" };
            double bMin = Math.Min(U_.ToMm(frame.Bx), U_.ToMm(frame.By));
            s.LongBarsAlongX = Math.Max(2, (int)Math.Ceiling((U_.ToMm(frame.Bx) - 2 * 40) / 300.0) + 1);
            s.LongBarsAlongY = Math.Max(2, (int)Math.Ceiling((U_.ToMm(frame.By) - 2 * 40) / 300.0) + 1);
            s.StirrupSpacingMm = Math.Floor(Math.Min(Math.Min(200, bMin), 12 * s.LongDiameterMm) / 5) * 5;
            s.BaseZoneLengthMm = embedmentMm;
            s.CorbelRebar = corbels.Count > 0;
            return s;
        }

        // ------------------------------------------------------------------
        // Verificações (NBR 6118 / NBR 9062). Retornam mensagens; não bloqueiam a geração.

        private ListaVerificacoes _ultimas = new ListaVerificacoes();

        /// <summary>Resultados do último <see cref="Check"/> (para o <see cref="PainelVerificacoes"/> sem conferir duas vezes).</summary>
        internal ListaVerificacoes UltimasVerificacoes => _ultimas;

        /// <summary>Todas as verificações normativas (inclusive as OK), para o relatório do grupo (D37).</summary>
        public ListaVerificacoes Verificacoes(ColumnBox frame, IList<Corbel> corbels)
        {
            Check(frame, corbels);
            return _ultimas;
        }

        /// <summary>Relatório de verificação do grupo de pilares iguais (D37): cabeçalho com seção, comprimento, materiais e cobrimento.</summary>
        public DEB.Core.Documentacao.RelatorioGrupo Relatorio(ColumnBox frame, IList<Corbel> corbels, string marca, int quantidade, string origem)
        {
            var g = new DEB.Core.Documentacao.RelatorioGrupo
            {
                Marca = marca, Elemento = "Pilar pré-moldado", Origem = origem, Verificacoes = Verificacoes(frame, corbels),
            };
            void D(string k, string v) => g.Dados.Add(new KeyValuePair<string, string>(k, v));
            D("Quantidade de peças", Math.Max(1, quantidade).ToString());
            D("Seção", $"{Fmt.N(U_.ToMm(frame.Bx) / 10)} × {Fmt.N(U_.ToMm(frame.By) / 10)} cm");
            D("Comprimento", $"{Fmt.N(U_.ToMm(frame.H) / 10)} cm");
            D("Concreto", $"fck = {Fmt.N(FckMPa)} MPa");
            D("Aço", $"longitudinais {LongSteel}; estribos {StirrupSteel}");
            D("Agregado graúdo", $"dmáx = {Fmt.N(AggregateMm)} mm");
            D("Classe de agressividade", ExposureClass);
            D("Relação a/c ≤ 0,45", WaterCement045 ? "sim (declarada)" : "não declarada");
            D("Cobrimento", $"{Fmt.N(CoverMm)} mm");
            D("Modelo de armadura", Name);
            g.Memoria.AddRange(_memoriaConsolos);
            return g;
        }

        public List<string> Check(ColumnBox frame, IList<Corbel> corbels)
        {
            var w = new List<string>();
            double bx = U_.ToMm(frame.Bx), by = U_.ToMm(frame.By);
            double bMin = Math.Min(bx, by);

            // armadura por linha (D32): avisos da leitura, como no CAD, e as bitolas de todas as linhas
            var avisosLinhas = new List<string>();
            var linhas = APL.Ler(Linhas(), LongEndCoverMm / 10, U_.ToMm(frame.H) / 10 - LongEndCoverMm / 10, avisosLinhas);
            w.AddRange(avisosLinhas);
            double phiMin = MinLongDiameterMm(), phiMax = linhas.Select(l => l.Bitola).DefaultIfEmpty(LongDiameterMm).Max();
            double phiEqMax = linhas.Select(l => l.Bitola * Math.Sqrt(Math.Max(1, l.Feixe))).DefaultIfEmpty(LongDiameterMm).Max();
            if (linhas.Count == 0) w.Add("Sem armadura longitudinal: inclua as barras na armadura por linha.");
            // regras únicas do núcleo (as mesmas do template do CAD; docs/04_arquitetura/verificacoes_normativas.md)
            var lista = new ListaVerificacoes();
            lista.Add(VP.DimensaoMinima(bx, by), false);
            lista.Add(VP.PilarParede(bx, by), false);
            foreach (var phi in linhas.Select(l => l.Bitola).DefaultIfEmpty(LongDiameterMm).Distinct())
                lista.Add(VP.BitolaLongitudinal(phi, bMin), false);
            foreach (var (label, s) in new[] { ("corrente", StirrupSpacingMm), ("topo", TopZoneSpacingMm),
                                               ("base", BaseZoneSpacingMm), ("consolo", CorbelZoneSpacingMm) })
            {
                lista.Add(VP.BitolaEstribo(StirrupDiameterMm, phiEqMax, s, TabelasNbr.Fyk(StirrupSteel)), false);
                lista.Add(VP.EspacamentoEstribos(s, bMin, phiMin, LongSteel, label), false);
            }
            lista.Add(VP.ConcretoAltaResistencia(FckMPa), false);

            // espaçamentos e taxa em cada trecho da altura com a mesma composição (redução com a altura e emendas): o pior trecho
            double gapMaxFound = 0, clearMin = double.MaxValue, asMax = 0, asMin = double.MaxValue;
            foreach (var trecho in ColumnRebarLayout.LongitudinalStretches(frame, this))
            {
                foreach (var (dist, folga, phi) in ColumnRebarLayout.FaceGaps(trecho))
                {
                    gapMaxFound = Math.Max(gapMaxFound, dist);
                    clearMin = Math.Min(clearMin, folga);
                }
                double asTrecho = trecho.Sum(b => b.Feixe * Math.PI * b.Bitola * b.Bitola / 4);
                asMax = Math.Max(asMax, asTrecho); asMin = Math.Min(asMin, asTrecho);
            }
            if (gapMaxFound > 0) lista.Add(VP.EspacamentoEixos(gapMaxFound, bMin), false);
            if (clearMin < double.MaxValue) lista.Add(VP.EspacamentoLivre(clearMin, phiEqMax, AggregateMm), false);
            double ac = bx * by;
            if (asMin == double.MaxValue) asMin = 0;
            lista.Add(VP.TaxaMinima(asMin, ac), false);
            lista.Add(VP.TaxaMaxima(asMax, ac), false);
            var dc = new DadosCobrimento
            {
                Classe = TabelasNbr.Classe(ExposureClass), Elemento = ElementoCobrimento.VigaPilar, PreMoldado = true, FckMPa = FckMPa,
                AguaCimentoAte045 = WaterCement045, CobrimentoMm = CoverMm, MaiorBitolaMm = StirrupDiameterMm, AgregadoMm = AggregateMm,
            };
            foreach (var x in RegrasCobrimento.Verificar(dc)) lista.Add(x, false);
            w.AddRange(lista.Avisos());
            _ultimas = lista;

            // alça: o vão interno (largura − Ø) tem de comportar o diâmetro de dobra do tipo de barra (o tipo da cordoalha é criado
            // com 5Ø, como o CA-50 abaixo de 20 mm); senão o Revit não resolve a forma ("Não é possível solucionar o formato")
            if (LiftLoops > 0)
            {
                double mandrel = (LiftDiameterMm >= 20 ? 8 : 5) * LiftDiameterMm;
                if (LiftWidthMm - LiftDiameterMm < mandrel - 1e-6)
                    w.Add($"Alça Ø{LiftDiameterMm:0.#}: largura {LiftWidthMm:0} mm não comporta o pino de dobramento ({mandrel:0} mm; " +
                          $"largura mínima {mandrel + LiftDiameterMm:0} mm, NBR 6118 tab. 9.1).");
            }

            if (CorbelRebar && corbels.Count > 0)
            {
                Corbel k = corbels[0];
                double width = U_.ToMm(k.Width);
                double tieGap = (width - 2 * CoverMm - TieDiameterMm) / Math.Max(1, TieCount - 1) - TieDiameterMm;
                if (TieCount > 1 && tieGap < 20) w.Add($"Tirante do consolo: espaçamento livre {tieGap:0} mm < 20 mm.");
                // regras únicas do consolo (NBR 9062 7.3, as mesmas do template do CAD), uma vez por geometria de consolo
                var consolos = new ListaVerificacoes();
                foreach (var g in corbels.GroupBy(c => (Math.Round(U_.ToMm(c.Width)), Math.Round(U_.ToMm(c.Projection)),
                                                        Math.Round(U_.ToMm(c.HeightAtFace)), Math.Round(U_.ToMm(c.TipHeight)),
                                                        c.Apoio?.Tipo, c.Apoio?.Comprimento, c.Apoio?.Largura, c.Apoio?.Afastamento))
                                         .Select(x => x.First()))
                {
                    double b = U_.ToMm(g.Width) / 10, a = U_.ToMm(g.Projection) / 10, h = U_.ToMm(g.HeightAtFace) / 10;
                    var dados = new DadosConsolo
                    {
                        Nome = $"{Fmt.Virgula(b, "0.#")}x{Fmt.Virgula(h, "0.#")}x{Fmt.Virgula(a, "0.#")}", B = b, A = a, H = h,
                        H1 = Math.Min(h, U_.ToMm(g.TipHeight) / 10), Cob = CoverMm / 10,
                        TirN = TieCount, TirPhi = TieDiameterMm, TirAlca = !TieWeldedBar, CostN = SewCount, CostPhi = SewDiameterMm,
                        VertN = CorbelStirrupCount, VertPhi = CorbelStirrupDiameterMm, FckMPa = FckMPa, FykMPa = TabelasNbr.Fyk(LongSteel),
                        Apoio = g.Apoio,
                    };
                    foreach (var x in VerificacoesConsolo.Verificar(dados)) consolos.Add(x);
                }
                w.AddRange(consolos.Avisos());
                _ultimas.Juntar(consolos);
            }

            // cálculo com a reação da viga (D44, etapa 1): um por consolo carregado, com a armadura desenhada
            _memoriaConsolos = new List<DEB.Core.Documentacao.PassoMemoria>();
            if (CorbelRebar)
            {
                var calc = new ListaVerificacoes();
                foreach (Corbel k in corbels.Where(c => c.Carga != null))
                {
                    var res = DEB.Core.Elementos.Consolo.CalculoConsolo.Calcular(DadosCalculo(k), k.Carga, _configuracao);
                    foreach (var x in res.Verificacoes) calc.Add(x);
                    _memoriaConsolos.Add(res.Memoria);
                }
                w.AddRange(calc.Avisos());
                _ultimas.Juntar(calc);
            }

            return w;
        }

        private List<DEB.Core.Documentacao.PassoMemoria> _memoriaConsolos = new List<DEB.Core.Documentacao.PassoMemoria>();

        // configuração do projeto (D45) do cálculo dos consolos; campo (não propriedade) para não entrar no modelo salvo em JSON
        private DEB.Core.Configuracao.Projeto.ConfiguracaoProjeto _configuracao;

        /// <summary>Configuração do projeto (D45) usada no cálculo dos consolos; null = valores de partida.</summary>
        public void UsarConfiguracao(DEB.Core.Configuracao.Projeto.ConfiguracaoProjeto cfg) => _configuracao = cfg;

        /// <summary>Leva a configuração do projeto de outro modelo de armadura (troca de modelo salvo na janela).</summary>
        public void HerdarConfiguracao(ColumnRebarSpec de) => _configuracao = de?._configuracao;

        /// <summary>Consolo com a armadura do modelo, para o cálculo: nome com face e topo para achar no relatório.</summary>
        private DadosConsolo DadosCalculo(Corbel k)
        {
            double b = U_.ToMm(k.Width) / 10, a = U_.ToMm(k.Projection) / 10, h = U_.ToMm(k.HeightAtFace) / 10;
            return new DadosConsolo
            {
                Nome = $"face {ColumnRebarLayout.FaceNumber(k.Face)}, topo a {Fmt.Virgula(U_.ToMm(k.ZTop) / 10, "0.#")} cm ({Fmt.Virgula(b, "0.#")}x{Fmt.Virgula(h, "0.#")}x{Fmt.Virgula(a, "0.#")})",
                B = b, A = a, H = h, H1 = Math.Min(h, U_.ToMm(k.TipHeight) / 10), Cob = CoverMm / 10,
                TirN = TieCount, TirPhi = TieDiameterMm, TirAlca = !TieWeldedBar, CostN = SewCount, CostPhi = SewDiameterMm,
                VertN = CorbelStirrupCount, VertPhi = CorbelStirrupDiameterMm, FckMPa = FckMPa, FykMPa = TabelasNbr.Fyk(LongSteel), Apoio = k.Apoio,
            };
        }

        public int LongBarCount => 2 * LongBarsAlongX + 2 * Math.Max(0, LongBarsAlongY - 2);

        // Modelos salvos (arquivo do usuário): DEB.Revit/src/Precast/ColumnRebarPresets.cs.
    }
}
