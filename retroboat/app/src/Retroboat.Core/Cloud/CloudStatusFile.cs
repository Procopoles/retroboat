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
    /// ícone de nuvem do cabeçalho aceso ou apagado.
    /// </summary>
    public static class CloudStatusFile
    {
        public const string Folder = "estado";
        public const string Connected = "nuvem-conectada";

        public static string ConnectedPath(RetroBatInstall install) => Path.Combine(install.RetroboatDataPath, Folder, Connected);

        public static void Write(RetroBatInstall install, bool connected)
        {
            var path = ConnectedPath(install);
            if (connected)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture) + Environment.NewLine);
            }
            else if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
