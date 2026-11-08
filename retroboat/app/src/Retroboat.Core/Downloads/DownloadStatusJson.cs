using System;
using System.Collections.Generic;
using System.Web.Script.Serialization;

namespace Retroboat.Downloads
{
    /// <summary>
    /// O andamento no formato do POST /retroboat/download do ES do Retroboat (RB-010; seção 3.1 de
    /// retroboat/design/rb010-es-nativo.md). O jogo vai pela pasta do sistema e pelo nome do arquivo local, como o
    /// ES o vê.
    /// </summary>
    public static class DownloadStatusJson
    {
        /// <summary>Nada baixando agora: o ES tira a barra e relê a fila.</summary>
        public const string Idle = "idle";

        public static string PhaseName(DownloadPhase phase)
        {
            switch (phase)
            {
                case DownloadPhase.Preparing: return "preparing";
                case DownloadPhase.Downloading: return "downloading";
                case DownloadPhase.WaitingForCloud: return "waiting";
                case DownloadPhase.Finished: return "finished";
                default: return "failed";
            }
        }

        /// <param name="status">Nulo: <see cref="Idle"/>.</param>
        public static string Of(DownloadStatus status)
        {
            var item = status?.Item;
            return new JavaScriptSerializer().Serialize(new Dictionary<string, object>
            {
                ["phase"] = status != null ? PhaseName(status.Phase) : Idle,
                ["system"] = item?.System ?? "",
                ["local"] = item?.LocalName ?? "",
                ["title"] = item?.Title ?? "",
                ["done"] = status?.BytesDone ?? 0,
                ["total"] = status?.BytesTotal ?? 0,
                ["speed"] = Math.Round(status?.BytesPerSecond ?? 0, 1),
                ["remaining"] = status?.Remaining != null ? (long)Math.Max(0, Math.Round(status.Remaining.Value.TotalSeconds)) : -1,
                ["message"] = status?.Message ?? ""
            });
        }
    }
}
