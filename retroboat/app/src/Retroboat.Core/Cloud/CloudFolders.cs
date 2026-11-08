using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Retroboat.Library;

namespace Retroboat.Cloud
{
    public sealed class CloudFoldersResult
    {
        /// <summary>A pasta "retroboat" não existia no Drive e foi criada.</summary>
        public bool RootCreated { get; set; }

        /// <summary>Pastas criadas em jogos (ex.: "psx").</summary>
        public List<string> SystemsCreated { get; } = new List<string>();

        public List<string> Errors { get; } = new List<string>();
    }

    /// <summary>
    /// A pasta de jogos do Drive no mesmo formato do RetroBat: jogos\&lt;sistema&gt; com os nomes das pastas de roms
    /// (psx, n64, gc, gameandwatch...), para a pessoa só soltar o jogo na pasta do console, sem criar pasta nenhuma.
    /// A lista vem do es_systems.cfg a cada início, como a pasta de jogos local, então um sistema novo do upstream
    /// ganha a pasta sozinho. Nada é apagado: uma pasta que o RetroBat deixou de ter pode ter jogos.
    /// </summary>
    public static class CloudFolders
    {
        /// <summary>
        /// Cria a pasta "retroboat" no "Meu Drive" quando o Drive está aberto e ela ainda não existe. Um caminho
        /// configurado à mão no retroboat.ini não é criado: nele, a pasta faltando quer dizer disco desconectado.
        /// </summary>
        /// <returns>true se a pasta existe (já existia ou foi criada).</returns>
        public static bool EnsureRoot(CloudLocation location)
        {
            if (location == null)
                return false;
            if (location.Exists)
                return true;
            if (location.IsConfigured || !Directory.Exists(Path.GetDirectoryName(location.RetroboatPath)))
                return false;

            Directory.CreateDirectory(location.RetroboatPath);
            return true;
        }

        /// <summary>As pastas de roms do RetroBat, uma por sistema, como as da pasta de jogos local (IsolatedLibrary).</summary>
        public static List<string> SystemFolders(RetroBatInstall install)
        {
            var config = install.SystemsConfigPath;
            if (config == null)
                return new List<string>();

            return IsolatedLibrary.FirstPerName(IsolatedLibrary.ReadSystems(config))
                .Select(s => s.Folder)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>A pasta do Retroboat (se o Drive está aberto) e as pastas de sistema que faltam em jogos.</summary>
        public static CloudFoldersResult Ensure(RetroBatInstall install, CloudLocation location)
        {
            var result = new CloudFoldersResult();
            if (location == null)
                return result;

            try
            {
                bool existed = location.Exists;
                if (!EnsureRoot(location))
                    return result;

                result.RootCreated = !existed;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                result.Errors.Add(location.RetroboatPath + ": " + ex.Message);
                return result;
            }

            var folders = SystemFolders(install);
            if (folders.Count == 0)
                return result;

            // Uma listagem só: no Drive, perguntar pasta por pasta custa mais.
            var existing = Directory.Exists(location.GamesPath)
                ? new HashSet<string>(Directory.GetDirectories(location.GamesPath).Select(Path.GetFileName), StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var folder in folders)
            {
                var path = Path.Combine(location.GamesPath, folder);
                if (existing.Contains(folder) || Directory.Exists(path))
                    continue;

                try
                {
                    Directory.CreateDirectory(path);
                    result.SystemsCreated.Add(folder);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    result.Errors.Add(folder + ": " + ex.Message);
                }
            }

            return result;
        }
    }
}
