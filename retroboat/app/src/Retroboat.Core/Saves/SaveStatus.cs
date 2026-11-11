using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Xml.Linq;
using Retroboat.Cloud;
using Retroboat.Library;

namespace Retroboat.Saves
{
    /// <summary>
    /// Como um save está em relação à nuvem. A ordem importa: o estado de um jogo é o pior dos arquivos dele.
    /// </summary>
    public enum SaveCloudState
    {
        /// <summary>A cópia deste PC é a que está na nuvem.</summary>
        Synced,

        /// <summary>Outro PC avisou que enviou uma versão mais nova, que ainda não chegou ao Drive deste PC.</summary>
        Incoming,

        /// <summary>Mudou neste PC e ainda não foi para a nuvem (jogo com o Drive fechado, ou o envio falhou).</summary>
        Pending,

        /// <summary>Mudou aqui e na nuvem: a escolha fica para a abertura do jogo.</summary>
        Conflict
    }

    public sealed class SaveItemStatus
    {
        public string Name { get; set; }
        public SaveCloudState State { get; set; }

        /// <summary>Em dia: quando a versão foi para a nuvem. Pendente: a última gravação neste PC.</summary>
        public DateTime TimeUtc { get; set; }

        /// <summary>PC que enviou a versão da nuvem (em dia); nulo nos outros estados.</summary>
        public string Pc { get; set; }
    }

    /// <summary>Os saves de um jogo (ou os que os jogos de um console dividem), juntos: o pior estado e o mais novo.</summary>
    public sealed class SaveGroupStatus
    {
        public SaveCloudState State { get; private set; }
        public DateTime TimeUtc { get; private set; }
        public string Pc { get; private set; }
        public int Files { get; private set; }

        internal SaveGroupStatus() { }

        internal SaveGroupStatus(SaveCloudState state, DateTime timeUtc, string pc, int files)
        {
            State = state;
            TimeUtc = timeUtc;
            Pc = pc;
            Files = files;
        }

        public void Add(SaveItemStatus item)
        {
            bool first = Files++ == 0;
            if (first || item.State > State || (item.State == State && item.TimeUtc > TimeUtc))
            {
                State = item.State;
                TimeUtc = item.TimeUtc;
                Pc = item.Pc;
            }
        }
    }

    /// <summary>Os saves de um sistema, por jogo (o nome do arquivo do jogo sem a extensão).</summary>
    public sealed class SystemSaveStatus
    {
        public SystemSaveStatus(string system) => System = system.ToLowerInvariant();

        public string System { get; }

        public Dictionary<string, SaveGroupStatus> Games { get; } = new Dictionary<string, SaveGroupStatus>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Arquivos que não são de um jogo só: o memory card compartilhado do PS2 (Mcd001.ps2), a VMU do Dreamcast, saves
        /// com o nome interno do jogo. Contam para todo jogo do sistema que já foi jogado. Nulo se não há nenhum.
        /// </summary>
        public SaveGroupStatus Shared { get; set; }
    }

    /// <summary>
    /// ESTADO DA NUVEM na página do jogo, como na Steam: o Retroboat grava, a cada sincronização de saves, o estado de cada
    /// jogo em &lt;RetroBat&gt;\retroboat\estado\saves-jogos.xml, e o ES do Retroboat mostra ao lado do JOGAR. Só olha
    /// este PC e o estado da última sincronização (<see cref="SaveSync.ItemStates"/>), por isso também funciona depois de
    /// um jogo com o Drive fechado.
    /// </summary>
    public static class SaveStatus
    {
        public const string FileName = "saves-jogos.xml";
        const string MutexName = @"Local\RetroboatSavesStatus";

        public static string GetPath(RetroBatInstall install) => Path.Combine(install.RetroboatDataPath, CloudStatusFile.Folder, FileName);

        /// <summary>
        /// O que a última sincronização sabe e o arquivo local não: conflito que ficou para depois, versão de outro PC que
        /// ainda está chegando, envio que falhou.
        /// </summary>
        public static void Overlay(List<SaveItemStatus> items, SaveSyncResult result)
        {
            if (items == null || result == null)
                return;

            void Mark(string name, SaveCloudState state, bool onlyKnown)
            {
                var item = items.FirstOrDefault(i => string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase));
                if (item == null)
                {
                    if (onlyKnown)
                        return;

                    items.Add(item = new SaveItemStatus { Name = name, State = state, TimeUtc = DateTime.MinValue });
                }

                if (state > item.State)
                {
                    item.State = state;
                    item.Pc = null;
                }
            }

            foreach (var name in result.Waiting) Mark(name, SaveCloudState.Incoming, false);
            // "Arquivo: motivo". Outros erros (a trava, por exemplo) não são de um arquivo.
            foreach (var error in result.Errors) Mark(error.Split(':')[0], SaveCloudState.Pending, true);
            foreach (var name in result.Conflicts) Mark(name, SaveCloudState.Conflict, false);
        }

        /// <summary>
        /// Junta os arquivos por jogo. Um arquivo é de um jogo quando o nome começa com o nome do jogo seguido de "." ou "_"
        /// (Jogo.srm, Jogo.state1, Jogo_1.mcd, Jogo.01.p2s) ou quando uma das pastas tem o nome do jogo (nvram\jogo\...); vale
        /// o nome de jogo mais longo. O resto é dividido pelos jogos do sistema.
        /// </summary>
        public static SystemSaveStatus Group(string system, IEnumerable<SaveItemStatus> items, IEnumerable<string> gameStems)
        {
            var stems = gameStems.Where(s => !string.IsNullOrEmpty(s)).Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(s => s.Length).ToList();
            var status = new SystemSaveStatus(system);

            foreach (var item in items)
            {
                var game = GameOf(item.Name, stems);
                if (game == null)
                {
                    if (status.Shared == null)
                        status.Shared = new SaveGroupStatus();

                    status.Shared.Add(item);
                    continue;
                }

                if (!status.Games.TryGetValue(game, out var group))
                    status.Games[game] = group = new SaveGroupStatus();

                group.Add(item);
            }

            return status;
        }

        /// <summary>O jogo de um arquivo de save, ou nulo se ele não é de um jogo só.</summary>
        public static string GameOf(string itemName, IReadOnlyList<string> stemsLongestFirst)
        {
            var parts = itemName.Split('\\', '/');
            var file = parts[parts.Length - 1];
            foreach (var stem in stemsLongestFirst)
            {
                if (file.StartsWith(stem, StringComparison.OrdinalIgnoreCase)
                    && (file.Length == stem.Length || file[stem.Length] == '.' || file[stem.Length] == '_'))
                    return stem;

                for (int i = 0; i < parts.Length - 1; i++)
                {
                    if (string.Equals(parts[i], stem, StringComparison.OrdinalIgnoreCase))
                        return stem;
                }
            }

            return null;
        }

        /// <summary>
        /// Os nomes dos jogos de um sistema neste PC: os arquivos e as pastas de jogo em &lt;pasta de jogos&gt;\&lt;sistema&gt;,
        /// sem a extensão (o mesmo nome que o ES usa). Os jogos que só estão na nuvem também entram, pelo marcador.
        /// </summary>
        public static IReadOnlyList<string> GameStems(string romsPath, string system)
        {
            var folder = string.IsNullOrEmpty(romsPath) ? null : Path.Combine(romsPath, system);
            if (folder == null || !Directory.Exists(folder))
                return new string[0];

            try
            {
                return Directory.EnumerateFileSystemEntries(folder)
                    .Select(Path.GetFileName)
                    .Where(name => !name.StartsWith(".", StringComparison.Ordinal))
                    .Select(Path.GetFileNameWithoutExtension)
                    .ToList();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return new string[0];
            }
        }

        /// <summary>
        /// Recalcula os sistemas dados e grava para o ES. <paramref name="results"/> é a última sincronização de cada regra
        /// (pela pasta dela), quando houve uma. Os outros sistemas ficam como estavam, a não ser com
        /// <paramref name="replaceAll"/> (todos os sistemas com saves foram recalculados). Nunca falha: é só a interface.
        /// </summary>
        public static void Refresh(RetroBatInstall install, string romsPath, IEnumerable<string> systems,
                                   IDictionary<string, SaveSyncResult> results = null, bool replaceAll = false, string pcName = null)
        {
            try
            {
                var savesPath = SaveRules.GetSavesPath(install);
                var computed = new List<SystemSaveStatus>();
                foreach (var system in systems.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    var items = new List<SaveItemStatus>();
                    bool available = true;
                    foreach (var rule in SaveRules.ForSystem(system))
                    {
                        var ruleItems = new SaveSync(savesPath, install.RetroboatDataPath, null, rule, pcName).ItemStates();
                        if (ruleItems == null)
                        {
                            available = false; // Pen drive fora: fica o que estava.
                            break;
                        }

                        if (results != null && results.TryGetValue(rule.Folder, out var result))
                            Overlay(ruleItems, result);

                        items.AddRange(ruleItems);
                    }

                    if (available)
                        computed.Add(Group(system, items, GameStems(romsPath, system)));
                }

                Write(GetPath(install), computed, replaceAll, pcName);
            }
            catch (Exception ex)
            {
                Logging.Log.Warn("Estado dos saves para o ES não foi atualizado: " + ex.Message);
            }
        }

        /// <summary>Grava os sistemas dados no arquivo do ES, mantendo os outros (salvo com <paramref name="replaceAll"/>).</summary>
        public static void Write(string path, IEnumerable<SystemSaveStatus> systems, bool replaceAll = false, string pcName = null)
        {
            using (var mutex = new Mutex(false, MutexName))
            {
                bool owned = false;
                try
                {
                    try { owned = mutex.WaitOne(TimeSpan.FromSeconds(10)); }
                    catch (AbandonedMutexException) { owned = true; }

                    var all = replaceAll ? new Dictionary<string, SystemSaveStatus>(StringComparer.OrdinalIgnoreCase) : Read(path);
                    foreach (var system in systems)
                        all[system.System] = system;

                    var root = new XElement("savesStatus",
                        new XAttribute("pc", SafePcName(pcName)),
                        new XAttribute("unsupported", string.Join(",", SaveRules.SystemsNotYetSupported.OrderBy(s => s, StringComparer.Ordinal))),
                        all.Values.Where(s => s.Shared != null || s.Games.Count > 0).OrderBy(s => s.System, StringComparer.Ordinal).Select(s => new XElement("system",
                            new XAttribute("name", s.System),
                            s.Shared != null ? ToElement("shared", null, s.Shared) : null,
                            s.Games.OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase).Select(g => ToElement("game", g.Key, g.Value)))));

                    using (var memory = new MemoryStream())
                    {
                        new XDocument(root).Save(memory);
                        var data = memory.ToArray();

                        // Igual ao que já está lá: não regrava, para o ES não reler à toa.
                        if (File.Exists(path) && File.ReadAllBytes(path).SequenceEqual(data))
                            return;

                        SaveSync.WriteAtomically(path, data);
                    }
                }
                finally
                {
                    if (owned)
                        mutex.ReleaseMutex();
                }
            }
        }

        /// <summary>O que está no arquivo do ES, por sistema. Vazio se ele não existe ou está danificado.</summary>
        public static Dictionary<string, SystemSaveStatus> Read(string path)
        {
            var systems = new Dictionary<string, SystemSaveStatus>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (!File.Exists(path))
                    return systems;

                foreach (var element in XDocument.Load(path).Root.Elements("system"))
                {
                    var system = new SystemSaveStatus((string)element.Attribute("name"));
                    var shared = element.Element("shared");
                    if (shared != null)
                        system.Shared = FromElement(shared);

                    foreach (var game in element.Elements("game"))
                        system.Games[(string)game.Attribute("stem")] = FromElement(game);

                    systems[system.System] = system;
                }
            }
            catch (Exception ex) when (ex is System.Xml.XmlException || ex is IOException || ex is ArgumentException || ex is NullReferenceException || ex is FormatException) { }

            return systems;
        }

        static readonly string[] StateNames = { "synced", "incoming", "pending", "conflict" };

        static XElement ToElement(string name, string stem, SaveGroupStatus group) => new XElement(name,
            stem != null ? new XAttribute("stem", stem) : null,
            new XAttribute("state", StateNames[(int)group.State]),
            group.TimeUtc > DateTime.MinValue ? new XAttribute("time", group.TimeUtc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)) : null,
            group.Pc != null ? new XAttribute("pc", group.Pc) : null,
            new XAttribute("files", group.Files));

        static SaveGroupStatus FromElement(XElement element)
        {
            var state = Array.IndexOf(StateNames, (string)element.Attribute("state"));
            var time = DateTime.MinValue;
            var text = (string)element.Attribute("time");
            if (text != null)
                time = DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);

            return new SaveGroupStatus(state < 0 ? SaveCloudState.Pending : (SaveCloudState)state, time, (string)element.Attribute("pc"), (int?)element.Attribute("files") ?? 0);
        }

        static string SafePcName(string pcName)
        {
            var name = string.IsNullOrEmpty(pcName) ? Environment.MachineName : pcName;
            foreach (var invalid in Path.GetInvalidFileNameChars())
                name = name.Replace(invalid, '_');

            return name;
        }
    }
}
