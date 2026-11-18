using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Retroboat.Cloud;
using Retroboat.Library;

namespace Retroboat.Media.Background
{
    /// <summary>Uma mídia que acabou de entrar no gamelist, para o ES mostrar sem recarregar (<see cref="EsLiveMedia"/>).</summary>
    public sealed class AppliedMedia
    {
        public string System { get; set; }

        /// <summary>O &lt;path&gt; do jogo no gamelist ("./Jogo.iso").</summary>
        public string GamePath { get; set; }

        /// <summary>A pasta do sistema na pasta de jogos, para comparar com o caminho que o ES usa.</summary>
        public string RomFolder { get; set; }

        /// <summary>Campo do gamelist → valor novo ("~/.emulationstation/media/ps2/videos/Jogo-video.mp4").</summary>
        public Dictionary<string, string> Fields { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>O valor de cada campo antes; o ES só recebe o novo se ainda estiver com este (ou vazio).</summary>
        public Dictionary<string, string> Previous { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>As fontes do PROCURAR MÍDIAS do jogo (&lt;scrap&gt;): o ES as esquece quando recebe o jogo pela API.</summary>
        public List<XElement> Scraps { get; } = new List<XElement>();

        /// <summary>As mídias da fila que entraram neste jogo, para pôr de novo se o ES fechar sem tê-las na memória.</summary>
        public List<BackgroundMediaItem> Items { get; } = new List<BackgroundMediaItem>();
    }

    /// <summary>
    /// Põe no gamelist as mídias de segundo plano que chegaram (RB-041). Um campo só é trocado se ainda está como estava
    /// quando a mídia entrou na fila, vazio ou apontando para um arquivo que não existe; senão o arquivo que chegou é
    /// apagado, porque ninguém o usa. Quem chama segura a vez da sincronização: a abertura e o "Atualizar listas de jogos"
    /// também mexem nos gamelists.
    /// </summary>
    public static class BackgroundMediaGamelists
    {
        public static List<AppliedMedia> Apply(RetroBatInstall install, IEnumerable<BackgroundMediaItem> ready)
        {
            var applied = new List<AppliedMedia>();
            foreach (var system in ready.GroupBy(i => i.System, StringComparer.OrdinalIgnoreCase))
            {
                var path = Path.Combine(install.GamelistsPath, system.Key, "gamelist.xml");
                var gamelist = File.Exists(path) ? XDocument.Load(path) : null;
                bool changed = false;

                foreach (var game in system.GroupBy(i => i.Game, StringComparer.OrdinalIgnoreCase))
                {
                    var element = gamelist == null ? null : CloudLibrarySync.FindGame(gamelist, game.Key);
                    var result = new AppliedMedia { System = system.Key, GamePath = (string)element?.Element("path"), RomFolder = game.First().RomFolder };

                    foreach (var item in game)
                    {
                        if (element == null || !Put(install, element, item, result))
                            DeleteUnused(install, element, item);
                        else
                            result.Items.Add(item);
                    }

                    if (result.Fields.Count > 0)
                    {
                        changed = true;
                        result.Scraps.AddRange(element.Elements("scrap").Select(s => new XElement(s)));
                        applied.Add(result);
                    }
                }

                if (changed)
                    Save(gamelist, path);
            }

            return applied;
        }

        /// <returns>false se nenhum campo usa o arquivo (a pessoa escolheu outro nesse meio-tempo).</returns>
        static bool Put(RetroBatInstall install, XElement game, BackgroundMediaItem item, AppliedMedia result)
        {
            var value = GameMedia.ToHomeRelative(item.Target, install);
            bool used = false;
            foreach (var field in item.Fields)
            {
                var current = (string)game.Element(field);
                if (current == value || SameFile(install, current, item))
                {
                    used = true; // Já apontava para ele: o conteúdo foi trocado, o caminho é o mesmo.
                    continue;
                }

                bool free = string.IsNullOrWhiteSpace(current) || current == item.Replaces || !Exists(install, current, item.RomFolder);
                if (!free)
                    continue;

                game.SetElementValue(field, value);
                result.Fields[field] = value;
                result.Previous[field] = current;
                used = true;
            }

            return used;
        }

        static void DeleteUnused(RetroBatInstall install, XElement game, BackgroundMediaItem item)
        {
            if (game != null && game.Elements().Any(e => GameMedia.MediaFields.ContainsKey(e.Name.LocalName) && SameFile(install, e.Value, item)))
                return;

            try { File.Delete(item.Target); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        static bool SameFile(RetroBatInstall install, string value, BackgroundMediaItem item)
        {
            var resolved = TryResolve(install, value, item.RomFolder);
            return resolved != null && string.Equals(resolved, Path.GetFullPath(item.Target), StringComparison.OrdinalIgnoreCase);
        }

        static bool Exists(RetroBatInstall install, string value, string romFolder)
        {
            var resolved = TryResolve(install, value, romFolder);
            return resolved != null && File.Exists(resolved);
        }

        static string TryResolve(RetroBatInstall install, string value, string romFolder)
        {
            try
            {
                return GameMedia.Resolve(value, romFolder ?? install.DefaultRomsPath, install);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            {
                return null; // Caminho inválido no gamelist: conta como sem arquivo.
            }
        }

        /// <summary>O ES pode ler o gamelist a qualquer momento: o arquivo é trocado de uma vez, nunca fica pela metade.</summary>
        static void Save(XDocument gamelist, string path)
        {
            var temp = path + ".retroboat-part";
            gamelist.Save(temp);
            if (File.Exists(path))
                File.Replace(temp, path, null);
            else
                File.Move(temp, path);
        }
    }
}
