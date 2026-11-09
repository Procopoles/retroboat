using System;
using System.Collections.Generic;
using System.IO;
using Retroboat.Settings;

namespace Retroboat.Cloud
{
    /// <summary>
    /// Pasta do Retroboat dentro da nuvem sincronizada, ex.: G:\Meu Drive\retroboat.
    /// </summary>
    public sealed class CloudLocation
    {
        public CloudLocation(string retroboatPath, string source, string libraryId = null, bool libraryMissing = false)
        {
            RetroboatPath = retroboatPath;
            Source = source;
            LibraryId = libraryId;
            LibraryMissing = libraryMissing;
        }

        public string RetroboatPath { get; }

        /// <summary>De onde veio o caminho: "configurado" ou "Google Drive (G:\)".</summary>
        public string Source { get; }

        /// <summary>RB-021: id da biblioteca encontrada nesta pasta (ou o que se procurava, com <see cref="LibraryMissing"/>).</summary>
        public string LibraryId { get; }

        /// <summary>
        /// O Google Drive está aqui, mas nenhuma conta tem a biblioteca deste PC (conta desconectada, Drive carregando ou
        /// biblioteca apagada). O caminho é só para mostrar: a pasta não é a biblioteca e não pode ser usada.
        /// </summary>
        public bool LibraryMissing { get; }

        public string GamesPath => Path.Combine(RetroboatPath, CloudLocator.GamesFolderName);

        public bool Exists => !LibraryMissing && Directory.Exists(RetroboatPath);
    }

    /// <summary>Uma conta do Google Drive montada neste PC e a pasta do Retroboat nela.</summary>
    public sealed class CloudDrive
    {
        public CloudDrive(string driveRoot, string myDrive)
        {
            DriveRoot = driveRoot;
            MyDrive = myDrive;
        }

        /// <summary>Ex.: G:\</summary>
        public string DriveRoot { get; }

        /// <summary>Ex.: G:\Meu Drive</summary>
        public string MyDrive { get; }

        public string RetroboatPath => Path.Combine(MyDrive, CloudLocator.RetroboatFolderName);
        public bool HasRetroboatFolder => Directory.Exists(RetroboatPath);
        public string Source => "Google Drive (" + DriveRoot + ")";

        /// <summary>Ex.: Meu Drive (G:)</summary>
        public string DisplayName => Path.GetFileName(MyDrive) + " (" + DriveRoot.TrimEnd('\\', '/') + ")";
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

        /// <summary>A pasta do Retroboat que este PC usa, pelo caminho configurado ou pela biblioteca escolhida (RB-021).</summary>
        public static CloudLocation Locate(RetroboatSettings settings)
        {
            return settings == null ? Locate() : Locate(settings.CloudPath, settings.CloudLibrary);
        }

        /// <summary>
        /// Usa o caminho configurado quando houver. Com o id de uma biblioteca, só vale a pasta "retroboat" que tem esse id,
        /// em qualquer conta; se nenhuma tem, a pasta volta marcada com <see cref="CloudLocation.LibraryMissing"/> e nunca
        /// cai para outra conta. Sem id, prefere a unidade que já tem a pasta "retroboat". Retorna null se não houver
        /// Google Drive.
        /// </summary>
        public static CloudLocation Locate(string configuredPath = null, string libraryId = null)
        {
            return Locate(configuredPath, libraryId, FindDrives());
        }

        internal static CloudLocation Locate(string configuredPath, string libraryId, IEnumerable<CloudDrive> drives)
        {
            if (!string.IsNullOrWhiteSpace(configuredPath))
                return new CloudLocation(Path.GetFullPath(configuredPath), "configurado");

            CloudDrive first = null;
            CloudDrive firstWithFolder = null;
            foreach (var drive in drives)
            {
                if (first == null)
                    first = drive;

                if (!drive.HasRetroboatFolder)
                    continue;

                if (string.IsNullOrWhiteSpace(libraryId))
                {
                    if (firstWithFolder == null)
                        firstWithFolder = drive;
                }
                else if (LibraryIdentity.Has(drive.RetroboatPath, libraryId))
                {
                    return new CloudLocation(drive.RetroboatPath, drive.Source, libraryId);
                }
            }

            if (first == null)
                return null;

            if (!string.IsNullOrWhiteSpace(libraryId))
                return new CloudLocation(first.RetroboatPath, first.Source, libraryId, libraryMissing: true);

            var chosen = firstWithFolder ?? first;
            return new CloudLocation(chosen.RetroboatPath, chosen.Source);
        }

        /// <summary>A pasta do Retroboat na nuvem está acessível agora.</summary>
        public static bool IsAvailable(string configuredPath = null, string libraryId = null)
        {
            var location = Locate(configuredPath, libraryId);
            return location != null && location.Exists;
        }

        public static bool IsAvailable(RetroboatSettings settings)
        {
            var location = Locate(settings);
            return location != null && location.Exists;
        }

        /// <summary>As contas do Google Drive montadas agora que têm a pasta "Meu Drive", na ordem das letras.</summary>
        public static List<CloudDrive> FindDrives()
        {
            var drives = new List<CloudDrive>();
            foreach (var driveRoot in GetGoogleDriveRoots())
            {
                var myDrive = FindMyDrive(driveRoot);
                if (myDrive != null)
                    drives.Add(new CloudDrive(driveRoot, myDrive));
            }

            return drives;
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
