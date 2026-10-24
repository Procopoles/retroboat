using System;
using System.Collections.Generic;
using System.IO;

namespace Retroboat.Cloud
{
    /// <summary>
    /// Pasta do Retroboat dentro da nuvem sincronizada, ex.: G:\Meu Drive\retroboat.
    /// </summary>
    public sealed class CloudLocation
    {
        public CloudLocation(string retroboatPath, string source)
        {
            RetroboatPath = retroboatPath;
            Source = source;
        }

        public string RetroboatPath { get; }

        /// <summary>De onde veio o caminho: "configurado" ou "Google Drive (G:\)".</summary>
        public string Source { get; }

        public string GamesPath => Path.Combine(RetroboatPath, CloudLocator.GamesFolderName);

        public bool Exists => Directory.Exists(RetroboatPath);
    }

    /// <summary>
    /// Encontra a pasta do Retroboat no app Google Drive para computador. O app monta uma unidade
    /// (normalmente G:) com rótulo "Google Drive"; com várias contas, cada uma tem a sua unidade.
    /// </summary>
    public static class CloudLocator
    {
        public const string GoogleDriveVolumeLabel = "Google Drive";
        public const string RetroboatFolderName = "retroboat";
        public const string GamesFolderName = "jogos";

        // Nome da pasta "Meu Drive" nos idiomas mais comuns do Drive para computador.
        static readonly string[] MyDriveNames = { "Meu Drive", "My Drive", "Mi unidad", "Mon Drive", "Meine Ablage", "Il mio Drive", "Mijn Drive" };

        /// <summary>
        /// Usa o caminho configurado quando houver. Senão, procura nas unidades do Google Drive e
        /// prefere a que já tem a pasta "retroboat". Retorna null se não houver Google Drive.
        /// </summary>
        public static CloudLocation Locate(string configuredPath = null)
        {
            if (!string.IsNullOrWhiteSpace(configuredPath))
                return new CloudLocation(Path.GetFullPath(configuredPath), "configurado");

            CloudLocation firstWithoutFolder = null;
            foreach (var driveRoot in GetGoogleDriveRoots())
            {
                var myDrive = FindMyDrive(driveRoot);
                if (myDrive == null)
                    continue;

                var location = new CloudLocation(Path.Combine(myDrive, RetroboatFolderName), "Google Drive (" + driveRoot + ")");
                if (location.Exists)
                    return location;

                if (firstWithoutFolder == null)
                    firstWithoutFolder = location;
            }

            return firstWithoutFolder;
        }

        public static IEnumerable<string> GetGoogleDriveRoots()
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (string.Equals(TryGetVolumeLabel(drive), GoogleDriveVolumeLabel, StringComparison.OrdinalIgnoreCase))
                    yield return drive.RootDirectory.FullName;
            }
        }

        public static string FindMyDrive(string driveRoot)
        {
            foreach (var name in MyDriveNames)
            {
                var path = Path.Combine(driveRoot, name);
                if (Directory.Exists(path))
                    return path;
            }

            return null;
        }

        static string TryGetVolumeLabel(DriveInfo drive)
        {
            try
            {
                return drive.IsReady ? drive.VolumeLabel : null;
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }
    }
}
