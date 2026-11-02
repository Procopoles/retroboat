using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Retroboat.Media
{
    /// <summary>
    /// Informações dos jogos na base do libretro (github.com/libretro/libretro-database, pasta metadat), pelo
    /// nome No-Intro/Redump, sem conta nem chave. Para o PS2, o arquivo de desenvolvedoras traz o registro
    /// completo (descrição em inglês, editora, data, jogadores, gênero); nos outros sistemas, cada pasta traz
    /// um campo. Os arquivos ficam em cache no PC por uma semana.
    /// </summary>
    public sealed class LibretroMetadata : IGameInfoSource
    {
        public const string BaseUrl = "https://raw.githubusercontent.com/libretro/libretro-database/master/metadat/";
        static readonly string[] Folders = { "developer", "publisher", "genre", "releaseyear", "releasemonth", "maxusers" };
        static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);

        readonly string _cacheDir;

        /// <param name="cacheDir">Pasta do cache (ex.: &lt;RetroBat&gt;\retroboat\cache\libretro).</param>
        public LibretroMetadata(string cacheDir)
        {
            _cacheDir = cacheDir;
        }

        public IDictionary<string, GameInfo> Find(string system, IReadOnlyCollection<string> stems)
        {
            var found = new Dictionary<string, GameInfo>(StringComparer.OrdinalIgnoreCase);
            if (!AutoMedia.Playlists.TryGetValue(system, out var playlist))
                return found;

            var records = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
            foreach (var folder in Folders)
                Merge(records, Parse(Load(folder, playlist)));

            if (records.Count == 0)
                return found;

            var index = new ThumbnailIndex(records.Keys);
            foreach (var stem in stems)
            {
                var name = index.Find(stem);
                if (name != null)
                    found[stem] = ToInfo(name, records[name]);
            }

            return found;
        }

        internal static GameInfo ToInfo(string name, Dictionary<string, string> fields)
        {
            string Field(string key) => fields.TryGetValue(key, out var value) && value.Trim().Length > 0 ? value.Trim() : null;

            return new GameInfo
            {
                Title = GameInfoText.CleanTitle(name),
                Description = Field("description"),
                Genre = GameInfoText.TranslateGenre(Field("genre")),
                ReleaseDate = GameInfoText.ReleaseDate(Field("releaseyear"), Field("releasemonth"), Field("releaseday")),
                Developer = Field("developer"),
                Publisher = Field("publisher"),
                Players = GameInfoText.Players(Field("users"))
            };
        }

        /// <summary>Texto do arquivo, do cache ou da internet. Vazio se o libretro não tem esse campo para o sistema.</summary>
        string Load(string folder, string playlist)
        {
            var cacheFile = Path.Combine(_cacheDir, "metadat", folder, LibretroThumbnails.Safe(playlist) + ".dat");
            if (File.Exists(cacheFile) && DateTime.UtcNow - File.GetLastWriteTimeUtc(cacheFile) < Lifetime)
                return File.ReadAllText(cacheFile, Encoding.UTF8);

            string text;
            try
            {
                text = Encoding.UTF8.GetString(WebFetch.Get(BaseUrl + folder + "/" + Uri.EscapeDataString(playlist) + ".dat"));
            }
            catch (WebException ex) when (WebFetch.IsNotFound(ex))
            {
                text = ""; // O libretro não tem esse campo para o sistema; o arquivo vazio evita perguntar de novo por uma semana.
            }
            catch (WebException) when (File.Exists(cacheFile))
            {
                return File.ReadAllText(cacheFile, Encoding.UTF8); // Sem conexão: o arquivo antigo ainda serve.
            }

            Directory.CreateDirectory(Path.GetDirectoryName(cacheFile));
            File.WriteAllText(cacheFile, text, new UTF8Encoding(false));
            return text;
        }

        static readonly Regex Field = new Regex(@"^\s*([a-z_]+)\s+""(.*)""\s*$");

        /// <summary>Jogos de um arquivo DAT do clrmamepro: game ( name "..." developer "..." rom ( ... ) ).</summary>
        internal static Dictionary<string, Dictionary<string, string>> Parse(string dat)
        {
            var games = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
            Dictionary<string, string> current = null;
            int depth = 0;

            foreach (var raw in dat.Split('\n'))
            {
                var line = raw.Trim();
                if (line.EndsWith("(", StringComparison.Ordinal))
                {
                    if (depth == 0 && line.StartsWith("game", StringComparison.Ordinal))
                        current = new Dictionary<string, string>(StringComparer.Ordinal);
                    depth++;
                    continue;
                }

                if (line == ")")
                {
                    depth = Math.Max(0, depth - 1);
                    if (depth == 0 && current != null)
                    {
                        if (current.TryGetValue("name", out var name) && !games.ContainsKey(name))
                            games[name] = current;
                        current = null;
                    }

                    continue;
                }

                // Só os campos do jogo; os da linha "rom ( ... )" (nome do arquivo, serial) ficam de fora.
                var match = Field.Match(line);
                if (current != null && depth == 1 && match.Success && !current.ContainsKey(match.Groups[1].Value))
                    current[match.Groups[1].Value] = match.Groups[2].Value.Replace("\\\"", "\"");
            }

            return games;
        }

        static void Merge(Dictionary<string, Dictionary<string, string>> into, Dictionary<string, Dictionary<string, string>> from)
        {
            foreach (var game in from)
            {
                if (!into.TryGetValue(game.Key, out var fields))
                    into[game.Key] = fields = new Dictionary<string, string>(StringComparer.Ordinal);

                foreach (var field in game.Value.Where(f => !fields.ContainsKey(f.Key)))
                    fields[field.Key] = field.Value;
            }
        }
    }
}
