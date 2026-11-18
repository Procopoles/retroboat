using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Retroboat.Es;
using Retroboat.Library;

namespace Retroboat.Media.Background
{
    /// <summary>
    /// RB-041: com o ES aberto, a mídia que chegou em segundo plano aparece na hora, sem aviso e sem recarregar as listas.
    ///
    /// O ES guarda o gamelist na memória e o regrava ao sair: mudar só o arquivo não aparece, e um jogo que ele mudou (uma
    /// partida) volta sem a mídia. Então o jogo vai ao ES pelo POST /addgames só com os campos novos e as fontes do PROCURAR
    /// MÍDIAS (que o ES esquece ao receber o jogo); o resto do que ele tem na memória (partidas, favorito) fica. Antes, o ES
    /// diz o que tem: um campo que a pessoa mudou nesta sessão (PROCURAR MÍDIAS) vale mais que a mídia que chegou.
    /// </summary>
    public static class EsLiveMedia
    {
        /// <returns>As que não chegaram ao ES (ele fechou ou não respondeu): tentar de novo depois.</returns>
        public static List<AppliedMedia> Push(RetroBatInstall install, IReadOnlyList<AppliedMedia> applied, IEsGames es)
        {
            var pending = new List<AppliedMedia>();
            foreach (var system in applied.GroupBy(a => a.System, StringComparer.OrdinalIgnoreCase))
            {
                var games = es.Games(system.Key);
                if (games == null)
                {
                    pending.AddRange(system);
                    continue;
                }

                var ids = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var game in games.Where(g => g.Id != null && Normalize(g.Path) != null))
                    ids[Normalize(game.Path)] = game.Id;

                // O mesmo jogo pode vir escrito de outro jeito (nome curto 8.3 do Windows, outra letra de unidade): pelo nome
                // do arquivo, quando só um jogo do sistema tem esse nome.
                var byName = games.Where(g => g.Id != null && g.Path != null)
                    .GroupBy(g => FileName(g.Path), StringComparer.OrdinalIgnoreCase)
                    .Where(g => g.Count() == 1)
                    .ToDictionary(g => g.Key, g => g.Single().Id, StringComparer.OrdinalIgnoreCase);

                var nodes = new List<XElement>();
                var sent = new List<AppliedMedia>();
                foreach (var media in system)
                {
                    // Um jogo que o ES não tem lê o gamelist na próxima vez que carregar as listas.
                    var gameFile = Normalize(TryResolve(install, media.GamePath, media.RomFolder));
                    if (gameFile == null)
                        continue;
                    if (!ids.TryGetValue(gameFile, out var id) && !byName.TryGetValue(FileName(gameFile), out id))
                        continue;

                    var memory = es.Game(system.Key, id);
                    if (memory == null)
                    {
                        pending.Add(media);
                        continue;
                    }

                    var node = new XElement("game", new XElement("path", media.GamePath));
                    foreach (var field in media.Fields)
                    {
                        memory.TryGetValue(field.Key, out var inMemory);
                        media.Previous.TryGetValue(field.Key, out var previous);
                        if (IsFree(install, media.RomFolder, inMemory, previous, field.Value))
                            node.Add(new XElement(field.Key, field.Value));
                    }

                    if (!node.Elements().Skip(1).Any())
                        continue;

                    node.Add(media.Scraps.Select(s => new XElement(s)));
                    nodes.Add(node);
                    sent.Add(media);
                }

                if (nodes.Count > 0 && !es.AddGames(system.Key, new XElement("gameList", nodes).ToString(SaveOptions.DisableFormatting)))
                    pending.AddRange(sent);
            }

            return pending;
        }

        /// <summary>
        /// O ES ainda está com o campo como estava no gamelist antes da mídia chegar (ou vazio, ou com um arquivo que não
        /// existe, ou já com o novo): pode receber o novo.
        /// </summary>
        static bool IsFree(RetroBatInstall install, string romFolder, string inMemory, string previous, string value)
        {
            if (string.IsNullOrWhiteSpace(inMemory))
                return true;

            var memoryPath = Normalize(inMemory);
            if (SamePath(memoryPath, Normalize(TryResolve(install, value, romFolder))))
                return true;

            if (!string.IsNullOrWhiteSpace(previous) && SamePath(memoryPath, Normalize(TryResolve(install, previous, romFolder))))
                return true;

            return !File.Exists(memoryPath);
        }

        /// <summary>O mesmo arquivo, mesmo escrito de outro jeito (nome curto 8.3): mesmo nome, tamanho e data.</summary>
        static bool SamePath(string a, string b)
        {
            if (a == null || b == null)
                return false;
            if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
                return true;

            var fa = new FileInfo(a);
            var fb = new FileInfo(b);
            return string.Equals(fa.Name, fb.Name, StringComparison.OrdinalIgnoreCase) && fa.Exists && fb.Exists &&
                   fa.Length == fb.Length && fa.LastWriteTimeUtc == fb.LastWriteTimeUtc;
        }

        static string FileName(string path) => path.Replace('\\', '/').Split('/').Last();

        static string TryResolve(RetroBatInstall install, string value, string romFolder)
        {
            try
            {
                return GameMedia.Resolve(value, romFolder ?? install.DefaultRomsPath, install);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            {
                return null; // Caminho inválido no gamelist.
            }
        }

        /// <summary>O ES usa barras normais; o Windows, invertidas.</summary>
        static string Normalize(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;

            try
            {
                return Path.GetFullPath(path.Replace('/', '\\')).TrimEnd('\\');
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            {
                return path;
            }
        }
    }
}
