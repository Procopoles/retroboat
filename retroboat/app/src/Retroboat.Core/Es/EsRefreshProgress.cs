using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Web.Script.Serialization;

namespace Retroboat.Es
{
    /// <summary>
    /// RB-038: o andamento do "Atualizar listas de jogos" para o ES, que mostra uma barra com a porcentagem e a etapa
    /// enquanto espera o Retroboat.exe --refresh. No máximo um envio a cada 150 ms (fora a troca de etapa e o fim); o ES
    /// anima a barra entre um e outro. Num ES sem a rota (o oficial), para de tentar depois da primeira falha.
    /// </summary>
    public sealed class EsRefreshProgress
    {
        static readonly TimeSpan MinInterval = TimeSpan.FromMilliseconds(150);

        readonly Func<string, bool> _post;
        readonly Stopwatch _clock = Stopwatch.StartNew();
        TimeSpan _lastSent = TimeSpan.MinValue;
        string _lastStep;
        int _lastPercent = -1;
        bool _unavailable;

        /// <param name="post">Quem envia o JSON (os testes trocam); nulo = <see cref="EsApi.PostRefreshProgress"/>.</param>
        public EsRefreshProgress(Func<string, bool> post = null)
        {
            _post = post ?? EsApi.PostRefreshProgress;
        }

        /// <summary>A porcentagem nunca volta: uma etapa que reporta menos que a anterior fica na anterior.</summary>
        public void Report(int percent, string step)
        {
            percent = Math.Max(_lastPercent, Math.Min(99, Math.Max(0, percent)));
            bool stepChanged = step != _lastStep;
            if (!stepChanged && percent == _lastPercent)
                return;

            if (!stepChanged && _clock.Elapsed - _lastSent < MinInterval)
                return;

            Send(percent, step, false);
        }

        /// <summary>Sincronização terminada: 100%, e o ES recarrega as listas.</summary>
        public void Finish(string step) => Send(100, step, true);

        void Send(int percent, string step, bool done)
        {
            _lastPercent = percent;
            _lastStep = step;
            _lastSent = _clock.Elapsed;
            if (_unavailable)
                return;

            var json = new JavaScriptSerializer().Serialize(new Dictionary<string, object>
            {
                ["percent"] = percent,
                ["step"] = step ?? "",
                ["done"] = done
            });

            if (!_post(json))
                _unavailable = true;
        }
    }
}
