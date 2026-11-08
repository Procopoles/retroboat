using System;
using System.Globalization;
using System.IO;
using Retroboat.Library;

namespace Retroboat.Cloud
{
    /// <summary>
    /// Estado da nuvem para o tema: o tema não lê o retroboat.ini nem a API do lançador, mas sabe
    /// perguntar se um arquivo existe. Com a nuvem encontrada, existe
    /// &lt;RetroBat&gt;\retroboat\estado\nuvem-conectada; sem ela, o arquivo é apagado. O tema mostra o
    /// ícone de nuvem do cabeçalho aceso, ou apagado com um X.
    /// </summary>
    public static class CloudStatusFile
    {
        public const string Folder = "estado";
        public const string Connected = "nuvem-conectada";
        public const string Declined = "drive-agora-nao";

        /// <summary>O ES do Retroboat (RB-010) cria ao abrir e apaga ao sair: o tema mostra o andamento ao vivo só com ele.</summary>
        public const string NativeEs = "es-nativo";

        /// <summary>Antes de abrir o RetroBat: um ES que caiu não apagou o arquivo, e o tema acharia que ele está aberto.</summary>
        public static void ClearNativeEs(RetroBatInstall install)
        {
            var path = Path.Combine(install.RetroboatDataPath, Folder, NativeEs);
            if (File.Exists(path))
                File.Delete(path);
        }

        public static string ConnectedPath(RetroBatInstall install) => Path.Combine(install.RetroboatDataPath, Folder, Connected);

        static string DeclinedPath(RetroBatInstall install) => Path.Combine(install.RetroboatDataPath, Folder, Declined);

        public static bool IsConnected(RetroBatInstall install) => File.Exists(ConnectedPath(install));

        public static void Write(RetroBatInstall install, bool connected)
        {
            var path = ConnectedPath(install);
            if (connected)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture) + Environment.NewLine);
                ClearDeclined(install);
            }
            else if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        /// <summary>
        /// O usuário respondeu "Agora não" para abrir o Drive. Vale até o Retroboat abrir de novo ou a nuvem
        /// voltar: abrir um jogo com memory card não pergunta outra vez.
        /// </summary>
        public static void RememberDeclined(RetroBatInstall install)
        {
            var path = DeclinedPath(install);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture) + Environment.NewLine);
        }

        public static bool WasDeclined(RetroBatInstall install) => File.Exists(DeclinedPath(install));

        public static void ClearDeclined(RetroBatInstall install)
        {
            var path = DeclinedPath(install);
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
