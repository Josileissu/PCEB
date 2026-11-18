using System.Collections.Generic;
using System.Linq;
using DEB.Core.Normas.Verificacao;

namespace DEB.Core.Elementos.Pilar.PreMoldado.Modelo3D
{
    /// <summary>
    /// Verificações de um grupo de pilares para o painel da janela do Revit (etapa 3 das verificações normativas), sem gravar
    /// arquivo: selo geral, contagem e a lista de pendências. A janela só mostra (D26).
    /// </summary>
    public sealed class PainelVerificacoes
    {
        /// <summary>Resultados normativos (inclusive os OK), sem repetir a mesma linha (a mesma regra conferida em várias zonas de
        /// estribos com o mesmo texto aparece uma vez).</summary>
        public ListaVerificacoes Resultados { get; } = new ListaVerificacoes();

        /// <summary>Avisos sem código (leitura da armadura por linha, alça, tirante do consolo): contam como "a conferir".</summary>
        public List<string> OutrosAvisos { get; } = new List<string>();

        public int Ok => Resultados.Count(r => r.Situacao == SituacaoVerificacao.Ok);
        public int Conferir => Resultados.Count(r => r.Situacao == SituacaoVerificacao.Conferir) + OutrosAvisos.Count;
        public int NaoAtende => Resultados.Count(r => r.Situacao == SituacaoVerificacao.NaoAtende);

        /// <summary>Selo: o pior resultado; aviso sem código leva no mínimo a "Conferir".</summary>
        public SituacaoVerificacao Geral =>
            OutrosAvisos.Count > 0 && Resultados.Geral == SituacaoVerificacao.Ok ? SituacaoVerificacao.Conferir : Resultados.Geral;

        /// <summary>Pendências normativas, as que não atendem primeiro.</summary>
        public IEnumerable<ResultadoVerificacao> Pendencias =>
            Resultados.Where(r => r.Situacao != SituacaoVerificacao.Ok).OrderByDescending(r => r.Situacao);

        /// <summary>Texto do selo ("OK", "Conferir", "Não atende").</summary>
        public static string Texto(SituacaoVerificacao s) =>
            s == SituacaoVerificacao.NaoAtende ? "Não atende" : s == SituacaoVerificacao.Conferir ? "Conferir" : "OK";

        /// <summary>Contador do painel: "n OK · n a conferir · n não atende".</summary>
        public string Contagem => $"{Ok} OK · {Conferir} a conferir · {NaoAtende} não atende";

        /// <summary>As mesmas regras do <see cref="ColumnRebarSpec.Check"/> e do relatório do grupo (D37).</summary>
        public static PainelVerificacoes De(ColumnRebarSpec spec, ColumnBox frame, IList<Corbel> corbels)
        {
            var p = new PainelVerificacoes();
            List<string> avisos = spec.Check(frame, corbels);
            foreach (var r in spec.UltimasVerificacoes)
                if (!p.Resultados.Any(x => x.Mensagem == r.Mensagem)) p.Resultados.Add(r);
            var normativos = new HashSet<string>(p.Resultados.Avisos());
            p.OutrosAvisos.AddRange(avisos.Where(a => !normativos.Contains(a)).Distinct());
            return p;
        }
    }
}
