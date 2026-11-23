using System;
using System.Collections.Generic;
using System.IO;
using DEB.Core.Lajes;
using DEB.Revit.Core;

namespace DEB.Revit.UI
{
    /// <summary>
    /// AutoRun "janelas" (sem projeto aberto): monta as janelas de prévia que não dependem do modelo com dados de exemplo e grava em
    /// PNG em logs\janelas_AAAAMMDD_HHMMSS — para conferir a apresentação (UI/Estilo) sem abrir um modelo com aquele elemento.
    /// Hoje: a prévia da paginação de laje alveolar (um pano de 2,6 × 6 m entre duas vigas T).
    /// </summary>
    internal static class JanelasDemo
    {
        public static string Run()
        {
            string dir = Path.Combine(DetailingReport.LogDir, "janelas_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            Directory.CreateDirectory(dir);
            var cfg = new ConfigPaginacao();
            var sec = new SecaoViga();
            sec.Linhas.Add(new LinhaSecao { Z = cfg.LeituraViga.DzAlma, Trechos = { (-0.10, 0.10) } });
            sec.Linhas.Add(new LinhaSecao { Z = cfg.LeituraViga.DzAba, Trechos = { (-0.25, 0.25) } });
            var piso = new PisoPlanta
            {
                Id = "100", AnguloVao = Math.PI / 2, ZTopo = 3.0,
                Loops = { new List<(double, double)> { (10, 20), (14.3, 20), (14.3, 26), (10, 26) } },
                Vigas =
                {
                    new VigaPlanta { Id = "1", Tipo = "T", P0 = (9, 20), P1 = (15, 20), Secao = sec },
                    new VigaPlanta { Id = "2", Tipo = "T", P0 = (15, 26), P1 = (9, 26), Secao = sec },
                },
            };
            var panos = PaginacaoObra.Paginar(new[] { piso }, cfg);
            var w = LajeAlveolar.PreviaPaginacaoWindow.Criar(panos, cfg, PaginacaoObra.Relatorio(panos, cfg), PaginacaoObra.Avisos(panos));
            string arq = Path.Combine(dir, "laje_previa.png");
            Estilo.Renderizar(w, arq, 1250, 820);
            return arq;
        }
    }
}
