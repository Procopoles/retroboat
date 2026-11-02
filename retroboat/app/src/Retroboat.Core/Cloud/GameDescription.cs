using System;

namespace Retroboat.Cloud
{
    /// <summary>
    /// Linha de status que o Retroboat coloca no topo da descrição de um jogo corrompido na nuvem
    /// ("Corrompido na nuvem: ..."). O resto da descrição (do scraper, da nuvem, das informações automáticas
    /// ou do usuário) fica embaixo, intacto. Jogo na nuvem ou instalado não tem linha: o tema mostra o status
    /// (RB-014, RB-017) e a descrição fica só com o texto do jogo, como na Steam. A linha "Na nuvem · ..." das
    /// versões anteriores é reconhecida para sair dos gamelists antigos.
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

        /// <summary>Troca o texto da descrição, mantendo o aviso de corrompido se houver (a linha "Na nuvem" antiga sai).</summary>
        public static string ReplaceBody(string description, string body)
        {
            return description != null && description.StartsWith(CorruptPrefix, StringComparison.Ordinal)
                ? WithStatus(description.Split('\n')[0].TrimEnd('\r'), body)
                : body;
        }
    }
}
