using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Retroboat.Library;

namespace Retroboat.Media
{
    public sealed class MediaRelocationResult
    {
        public int FilesMoved { get; set; }
        public List<string> Systems { get; } = new List<string>();
        public List<string> Errors { get; } = new List<string>();
    }

    /// <summary>
    /// Item 1c da biblioteca isolada: o scraper do ES grava a mídia dentro da pasta de jogos
    /// (images\, videos\, manuals\). Antes do ES abrir, o lançador move essa mídia para
    /// .emulationstation\media\&lt;sistema&gt; e aponta o gamelist para lá com "~/...". Assim a pasta de
    /// jogos guarda só jogos, e a mídia pode ir para a nuvem.
    /// </summary>
    public sealed class MediaRelocator
    {
        readonly RetroBatInstall _install;

        public MediaRelocator(RetroBatInstall install)
        {
            _install = install;
        }

        public MediaRelocationResult Apply(string romsRoot)
        {
            var result = new MediaRelocationResult();
            var config = _install.SystemsConfigPath;
            if (config == null)
                return result;

            foreach (var system in IsolatedLibrary.ReadSystems(config).GroupBy(s => s.Name, StringComparer.Ordinal).Select(g => g.First()))
            {
                var gamelistPath = Path.Combine(_install.GamelistsPath, system.Name, "gamelist.xml");
                if (!File.Exists(gamelistPath))
                    continue;

                var romFolder = Path.GetFullPath(Path.Combine(romsRoot, system.Folder)).TrimEnd('\\');
                try
                {
                    int moved = RelocateSystem(gamelistPath, romFolder, GameMedia.LocalMediaRoot(_install, system.Name));
                    if (moved > 0)
                    {
                        result.FilesMoved += moved;
                        result.Systems.Add(system.Name);
                    }
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Xml.XmlException)
                {
                    result.Errors.Add(system.Name + ": " + ex.Message);
                }
            }

            return result;
        }

        int RelocateSystem(string gamelistPath, string romFolder, string mediaRoot)
        {
            var doc = XDocument.Load(gamelistPath);
            var touchedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int moved = 0;

            foreach (var element in doc.Root.Elements("game").Elements().Where(e => GameMedia.MediaFields.ContainsKey(e.Name.LocalName)))
            {
                var full = GameMedia.Resolve(element.Value, romFolder, _install);
                if (full == null || !full.StartsWith(romFolder + "\\", StringComparison.OrdinalIgnoreCase) || !File.Exists(full))
                    continue;

                var relative = full.Substring(romFolder.Length + 1);
                var target = Path.Combine(mediaRoot, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                if (File.Exists(target))
                    File.Delete(target);

                File.Move(full, target);
                touchedFolders.Add(Path.GetDirectoryName(full));
                element.Value = GameMedia.ToHomeRelative(target, _install);
                moved++;
            }

            if (moved == 0)
                return 0;

            doc.Save(gamelistPath);

            // Pastas de mídia que ficaram vazias saem da pasta de jogos.
            foreach (var folder in touchedFolders.OrderByDescending(f => f.Length))
            {
                for (var dir = folder; dir.StartsWith(romFolder + "\\", StringComparison.OrdinalIgnoreCase); dir = Path.GetDirectoryName(dir))
                {
                    if (!Directory.Exists(dir) || Directory.EnumerateFileSystemEntries(dir).Any())
                        break;

                    Directory.Delete(dir);
                }
            }

            return moved;
        }
    }
}
