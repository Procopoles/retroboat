using System;

namespace Retroboat.Cloud
{
    /// <summary>
    /// Linha de status que o Retroboat coloca no topo da descrição de um jogo que não está instalado
    /// ("Na nuvem · 3,0 GB · ..."). O resto da descrição (do scraper, da nuvem ou do usuário) fica embaixo,
    /// intacto, e a linha sai quando o jogo é instalado. Até a interface ganhar um status próprio (RB-010,
    /// RB-014), é por aqui que o ES mostra que o jogo está na nuvem.
    /// </summary>
    public static class GameDescription
    {
        public const string CloudPrefix = "Na nuvem ·";
        public const string CorruptPrefix = "Corrompido na nuvem:";

        public static bool HasStatus(string description)
        {
            return description != null && (description.StartsWith(CloudPrefix, StringComparison.Ordinal) || description.StartsWith(CorruptPrefix, StringComparison.Ordinal));
        }

        /// <summary>A descrição sem a linha de status (texto vazio se só havia o status).</summary>
        public static string WithoutStatus(string description)
        {
            if (!HasStatus(description))
                return description ?? "";

            int lineEnd = description.IndexOf('\n');
            return lineEnd < 0 ? "" : description.Substring(lineEnd + 1).TrimStart('\r', '\n');
        }

        public static string WithStatus(string status, string body)
        {
            return string.IsNullOrEmpty(body) ? status : status + "\n\n" + body;
        }
    }
}
