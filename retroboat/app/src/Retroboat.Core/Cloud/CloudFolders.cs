using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Retroboat.Library;

namespace Retroboat.Cloud
{
    public sealed class CloudFoldersResult
    {
        /// <summary>Pastas criadas em jogos (ex.: "psx").</summary>
        public List<string> SystemsCreated { get; } = new List<string>();

        public List<string> Errors { get; } = new List<string>();
    }

    /// <summary>
    /// A pasta de jogos do Drive no mesmo formato do RetroBat: jogos\&lt;sistema&gt; com os nomes das pastas de roms
    /// (psx, n64, gc, gameandwatch...), para a pessoa só soltar o jogo na pasta do console, sem criar pasta nenhuma.
    /// A lista vem do es_systems.cfg a cada início, como a pasta de jogos local, então um sistema novo do upstream
    /// ganha a pasta sozinho. Nada é apagado: uma pasta que o RetroBat deixou de ter pode ter jogos.
    /// <para>
    /// Só cria dentro de um retroboat\jogos que já existe. Uma pasta do Retroboat ou de jogos que não aparece pode
    /// ser o Drive ainda carregando ou outra conta montada primeiro; criar ali faria uma segunda pasta vazia no Drive,
    /// e o Retroboat passaria a ler a fonte errada.
    /// </para>
    /// </summary>
    public static class CloudFolders
    {
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

        /// <summary>As pastas de sistema que faltam em retroboat\jogos. Sem essa pasta no Drive, não cria nada.</summary>
        public static CloudFoldersResult Ensure(RetroBatInstall install, CloudLocation location)
        {
            var result = new CloudFoldersResult();
            if (location == null || !Directory.Exists(location.GamesPath))
                return result;

            var folders = SystemFolders(install);
            if (folders.Count == 0)
                return result;

            // Uma listagem só: no Drive, perguntar pasta por pasta custa mais.
            HashSet<string> existing;
            try
            {
                existing = new HashSet<string>(Directory.GetDirectories(location.GamesPath).Select(Path.GetFileName), StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                result.Errors.Add(location.GamesPath + ": " + ex.Message);
                return result;
            }

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
