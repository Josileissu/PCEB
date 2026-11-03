using System.Collections.Generic;
using DEB.Core.Calculo;
using DEB.Core.Configuracao;
using DEB.Core.Normas;
using DEB.Core.Tabelas;

namespace DEB.Revit.Core
{
    /// <summary>
    /// Regras de detalhamento vindas do núcleo comum (DEB.Core) — as mesmas do plugin de CAD (decisão D7).
    /// Por enquanto usa os valores padrão do núcleo; a configuração do usuário compartilhada vem depois.
    /// </summary>
    public static class SharedRules
    {
        private static readonly ConfiguracaoArmadura Config = new ConfiguracaoArmadura();
        private static readonly RegrasDobramento Dobramento = Config.Dobramento;
        private static readonly TabelaBarras Bitolas = TabelaBarras.Padrao();

        /// <summary>"CA-60" abaixo de 6,3 mm, senão "CA-50" (mesma convenção dos nomes de tipo de barra).</summary>
        public static string SteelFor(double diameterMm) => diameterMm < 6.3 ? "CA-60" : "CA-50";

        /// <summary>Diâmetro do pino de dobramento em mm (NBR 6118, tabelas 9.1 e 9.2).</summary>
        public static double BendDiameterMm(double diameterMm, string steel, bool stirrup) =>
            Dobramento.DiametroPino(diameterMm, steel, stirrup) * 10.0;

        /// <summary>Massa linear em kg/m, da tabela de bitolas do núcleo.</summary>
        public static double KgPerMeter(double diameterMm, string steel) => Bitolas.MassaLinear(diameterMm, steel);

        /// <summary>
        /// Comprimento real (cm, arredondado) de uma barra dada pelo eixo em cm — mesmas regras do CAD:
        /// desconto das dobras, ganchos e passagens pela norma (ver ComprimentoPorEixo no núcleo).
        /// </summary>
        public static double RealLengthCm(IReadOnlyList<(double X, double Y, double Z)> axisCm, bool closed, double diameterMm,
            bool stirrup, bool hooks135) =>
            ComprimentoPorEixo.Calcular(axisCm, closed, diameterMm, SteelFor(diameterMm), stirrup, hooks135, Config);
    }
}
