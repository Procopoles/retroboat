using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Retroboat.Cloud;

namespace Retroboat.Library
{
    /// <summary>
    /// Consoles ativos: os que têm pelo menos um jogo disponível, na biblioteca do Drive ou na pasta do console neste PC. Um jogo
    /// da nuvem que ainda não foi baixado conta (ele tem marcador); uma pasta vazia, ou só com gamelist.xml, _info.txt ou mídia do
    /// scraper, não. O que é jogo segue a mesma regra da biblioteca do Drive (<see cref="CloudLibrary.IsGame"/>).
    /// <para>
    /// É a mesma regra do Retroboat::ActiveSystems do ES (desenho: retroboat/design/rb023-menu-emuladores.md). Toda opção que
    /// depende de "consoles com jogos" usa esta classe, em vez de olhar as pastas por conta própria.
    /// </para>
    /// </summary>
    public static class ActiveSystems
    {
        /// <param name="romsPath">A pasta de jogos deste PC; null ou inexistente: só a nuvem.</param>
        /// <param name="cloud">A biblioteca do Drive; null ou fora do ar: só este PC.</param>
        /// <returns>As pastas de console (ex.: "ps2", "psx"), em ordem e sem diferenciar maiúsculas.</returns>
        public static ISet<string> Folders(string romsPath, CloudLocation cloud = null)
        {
            var systems = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

            if (cloud != null && cloud.Exists && Directory.Exists(cloud.GamesPath))
                systems.UnionWith(new CloudLibrary(cloud.GamesPath).GetGames().Select(g => g.System));

            if (!string.IsNullOrEmpty(romsPath) && Directory.Exists(romsPath))
            {
                foreach (var folder in new DirectoryInfo(romsPath).EnumerateDirectories().Where(CloudLibrary.IsVisible))
                {
                    if (folder.EnumerateFileSystemInfos().Any(CloudLibrary.IsGame))
                        systems.Add(folder.Name);
                }
            }

            return systems;
        }
    }
}
