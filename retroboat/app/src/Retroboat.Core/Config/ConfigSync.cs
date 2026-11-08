using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Xml;
using System.Xml.Linq;
using Retroboat.Saves;

namespace Retroboat.Config
{
    public enum ConfigSyncMode
    {
        /// <summary>Envia o que mudou aqui e grava aqui o que mudou na nuvem.</summary>
        Full,

        /// <summary>
        /// Só envia: o programa dono do arquivo está aberto (o ES regrava o es_settings.cfg ao sair e desfaria o que
        /// viesse da nuvem). O que veio da nuvem espera a próxima sincronização completa.
        /// </summary>
        SendOnly,

        /// <summary>Só diz o que faria, sem mudar nada aqui nem na nuvem.</summary>
        DryRun
    }

    public sealed class ConfigSyncResult
    {
        public ConfigSyncResult(string source)
        {
            Source = source;
        }

        /// <summary>A fonte, para mensagens ("opções do ES").</summary>
        public string Source { get; }

        /// <summary>A fonte não existe neste PC (ex.: emulador ainda não instalado).</summary>
        public bool Skipped { get; set; }

        /// <summary>Entradas que vieram da nuvem para este PC (num DryRun, as que viriam).</summary>
        public List<string> Received { get; } = new List<string>();

        /// <summary>Entradas deste PC enviadas para a nuvem (num DryRun, as que iriam).</summary>
        public List<string> Sent { get; } = new List<string>();

        /// <summary>Mudanças da nuvem que esperam o programa dono do arquivo fechar (SendOnly).</summary>
        public List<string> Pending { get; } = new List<string>();

        /// <summary>Entradas que mudaram aqui e em outro PC desde a última sincronização; vale a mais nova.</summary>
        public List<string> Conflicts { get; } = new List<string>();

        public List<string> Errors { get; } = new List<string>();

        public bool HasChanges => Received.Count + Sent.Count > 0;
    }

    /// <summary>
    /// Configurações na nuvem, entrada por entrada (uma opção do ES, uma chave de .ini, um arquivo), no modelo das
    /// contas: a nuvem é a fonte da verdade, e cada PC só grava o próprio arquivo.
    ///
    /// Na nuvem: configuracoes\&lt;fonte&gt;\&lt;PC&gt;.xml, com a última versão de cada entrada que o PC mudou (valor ou
    /// "removido") e quando. Vale, para cada entrada, a versão mais nova entre todos os PCs. Nenhum arquivo é
    /// editado por dois PCs, então o Drive nunca cria cópias de conflito.
    ///
    /// Neste PC: retroboat\configuracoes-sync\&lt;fonte&gt;.xml guarda o hash de cada entrada na última sincronização,
    /// para separar "mudou aqui" de "mudou na nuvem". Se mudou nos dois lados, vale a mudança mais nova. No primeiro
    /// uso em um PC, e quando um arquivo já sincronizado some (apagado, emulador reinstalado), vale a nuvem: um PC
    /// novo ou zerado recebe as configurações, em vez de mandar as dele, de fábrica, para os outros.
    ///
    /// Antes de gravar qualquer arquivo daqui, uma cópia vai para retroboat\historico\configuracoes\&lt;fonte&gt; (as
    /// 10 últimas).
    /// </summary>
    public sealed class ConfigSync
    {
        public const string CloudFolderName = "configuracoes";
        public const string StateFolderName = "configuracoes-sync";
        const int HistoryKept = 10;
        static readonly TimeSpan LockWait = TimeSpan.FromSeconds(15);

        readonly IConfigSource _source;
        readonly string _statePath;
        readonly string _historyDir;
        readonly string _mutexName;

        public ConfigSync(IConfigSource source, string retroboatDataPath, string cloudRetroboatPath, string pcName = null)
        {
            _source = source;
            PcName = string.IsNullOrEmpty(pcName) ? Environment.MachineName : pcName;
            CloudFolder = Path.Combine(cloudRetroboatPath, CloudFolderName, source.Name);
            _statePath = Path.Combine(retroboatDataPath, StateFolderName, source.Name + ".xml");
            _historyDir = Path.Combine(retroboatDataPath, "historico", CloudFolderName, source.Name);
            _mutexName = @"Local\RetroboatConfigSync-" + source.Name + "-" + Hash(Path.GetFullPath(retroboatDataPath).ToLowerInvariant()).Substring(0, 12);
        }

        public IConfigSource Source => _source;
        public string PcName { get; }
        public string CloudFolder { get; }
        string OwnPath => Path.Combine(CloudFolder, SafeFileName(PcName) + ".xml");

        public ConfigSyncResult Sync(ConfigSyncMode mode = ConfigSyncMode.Full)
        {
            var result = new ConfigSyncResult(_source.DisplayName);
            if (!_source.IsAvailable)
            {
                result.Skipped = true;
                return result;
            }

            // O Retroboat.exe e o retroboat-launch podem sincronizar a mesma fonte ao mesmo tempo.
            using (var mutex = new Mutex(false, _mutexName))
            {
                bool owned;
                try { owned = mutex.WaitOne(LockWait); }
                catch (AbandonedMutexException) { owned = true; }

                if (!owned)
                {
                    result.Errors.Add(_source.DisplayName + ": outra sincronização em andamento");
                    return result;
                }

                try
                {
                    SyncLocked(mode, result);
                }
                finally
                {
                    mutex.ReleaseMutex();
                }
            }

            return result;
        }

        void SyncLocked(ConfigSyncMode mode, ConfigSyncResult result)
        {
            var snapshot = _source.Read();
            var state = LoadState(_statePath);
            var own = ReadOwn(result, ref state, mode);
            var cloud = ReadNewest(own);
            var now = DateTime.UtcNow;

            var receive = new Dictionary<string, string>(StringComparer.Ordinal);
            var send = new Dictionary<string, string>(StringComparer.Ordinal);
            var finals = new Dictionary<string, string>(StringComparer.Ordinal);
            var kept = new Dictionary<string, string>(StringComparer.Ordinal);

            var ids = new SortedSet<string>(snapshot.Values.Keys, StringComparer.Ordinal);
            ids.UnionWith(cloud.Keys);
            if (state != null)
                ids.UnionWith(state.Hashes.Keys);

            foreach (var id in ids)
            {
                string syncedHash = null;
                bool inState = state != null && state.Hashes.TryGetValue(id, out syncedHash);

                if (snapshot.Held.Contains(id))
                {
                    if (inState)
                        kept[id] = syncedHash;
                    continue;
                }

                // Entrada que passou a ficar no PC (lista de chaves locais mudou): esquece.
                if (!_source.IsPortable(id, null))
                    continue;

                snapshot.Values.TryGetValue(id, out var local);
                cloud.TryGetValue(id, out var remote);
                var group = _source.GroupOf(id);
                bool first = state == null || !state.Groups.Contains(group) || !snapshot.ExistingGroups.Contains(group);
                var localHash = Hash(local);
                var remoteHash = remote == null ? null : Hash(remote.Value);

                if (first)
                {
                    // PC novo, ou o arquivo sumiu: vale a nuvem.
                    if (remote != null && remoteHash != localHash)
                        receive[id] = remote.Value;
                    else if (remote == null && local != null)
                        send[id] = local;
                    else
                        finals[id] = local;
                    continue;
                }

                if (!inState)
                    syncedHash = Hash(null);

                bool localChanged = localHash != syncedHash;
                bool cloudChanged = remote != null && remoteHash != syncedHash;

                if (!localChanged)
                {
                    if (cloudChanged)
                        receive[id] = remote.Value;
                    else
                        finals[id] = local;
                }
                else if (remote != null && remoteHash == localHash)
                {
                    finals[id] = local; // Mesma mudança nos dois lados.
                }
                else if (cloudChanged && remote.UpdatedUtc > _source.ChangedUtc(id))
                {
                    receive[id] = remote.Value;
                    result.Conflicts.Add(id + ": mudou aqui e no " + remote.Pc + "; ficou o do " + remote.Pc + ", mais recente");
                }
                else
                {
                    send[id] = local;
                    if (cloudChanged)
                        result.Conflicts.Add(id + ": mudou aqui e no " + remote.Pc + "; ficou o deste PC, mais recente");
                }
            }

            bool apply = mode == ConfigSyncMode.Full;
            (apply || mode == ConfigSyncMode.DryRun ? result.Received : result.Pending).AddRange(receive.Keys);
            result.Sent.AddRange(send.Keys);

            if (mode == ConfigSyncMode.DryRun)
                return;

            if (send.Count > 0)
            {
                foreach (var pair in send)
                    own[pair.Key] = new ConfigEntry(pair.Key, pair.Value, now, PcName);

                SaveEntries(OwnPath, _source.Name, PcName, own);
            }

            if (apply && receive.Count > 0)
            {
                Backup(_source.FilesFor(receive.Keys));
                _source.Write(receive);
            }

            // Sem estado ainda e sem poder gravar o que veio da nuvem: a próxima sincronização completa ainda é a primeira.
            if (!apply && state == null)
                return;

            var newState = new ConfigState();
            if (state != null)
                newState.Groups.UnionWith(state.Groups);

            if (apply)
            {
                newState.Groups.UnionWith(snapshot.ExistingGroups);
                newState.Groups.UnionWith(receive.Where(r => r.Value != null).Select(r => _source.GroupOf(r.Key)));
            }

            foreach (var pair in kept)
                newState.Hashes[pair.Key] = pair.Value;

            foreach (var pair in finals.Concat(send))
            {
                if (pair.Value != null)
                    newState.Hashes[pair.Key] = Hash(pair.Value);
            }

            foreach (var pair in receive)
            {
                if (apply)
                {
                    if (pair.Value != null)
                        newState.Hashes[pair.Key] = Hash(pair.Value);
                }
                else if (state.Hashes.TryGetValue(pair.Key, out var previous))
                {
                    newState.Hashes[pair.Key] = previous; // Continua pendente.
                }
            }

            SaveState(_statePath, _source.Name, newState);
        }

        /// <summary>
        /// O arquivo deste PC na nuvem. Se não der para ler (danificado), vai para o lado e esta vira a primeira
        /// sincronização: vale a nuvem, e o que só existe aqui é enviado de novo.
        /// </summary>
        Dictionary<string, ConfigEntry> ReadOwn(ConfigSyncResult result, ref ConfigState state, ConfigSyncMode mode)
        {
            try
            {
                return ReadEntries(OwnPath);
            }
            catch (Exception ex) when (ex is XmlException || ex is FormatException || ex is IOException)
            {
                result.Errors.Add(_source.DisplayName + ": o arquivo deste PC na nuvem estava danificado (" + ex.Message + "); começando de novo");
                if (mode != ConfigSyncMode.DryRun)
                {
                    var aside = OwnPath + ".danificado-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
                    try { File.Move(OwnPath, aside); }
                    catch (IOException) { }
                }

                state = null;
                return new Dictionary<string, ConfigEntry>(StringComparer.Ordinal);
            }
        }

        /// <summary>A versão mais nova de cada entrada entre todos os PCs (o deste PC incluído).</summary>
        Dictionary<string, ConfigEntry> ReadNewest(Dictionary<string, ConfigEntry> own)
        {
            var newest = new Dictionary<string, ConfigEntry>(StringComparer.Ordinal);
            var files = Directory.Exists(CloudFolder) ? Directory.GetFiles(CloudFolder, "*.xml") : new string[0];

            IEnumerable<IEnumerable<ConfigEntry>> sources = new[] { own.Values };
            foreach (var file in files)
            {
                if (string.Equals(Path.GetFullPath(file), Path.GetFullPath(OwnPath), StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    sources = sources.Concat(new[] { ReadEntries(file).Values.ToList() });
                }
                catch (Exception ex) when (ex is XmlException || ex is FormatException || ex is IOException)
                {
                    // Arquivo de outro PC no meio do envio pelo Drive: fica para a próxima.
                }
            }

            foreach (var entry in sources.SelectMany(s => s))
            {
                if (!newest.TryGetValue(entry.Id, out var current) || entry.UpdatedUtc > current.UpdatedUtc
                    || (entry.UpdatedUtc == current.UpdatedUtc && string.CompareOrdinal(entry.Pc, current.Pc) > 0))
                    newest[entry.Id] = entry;
            }

            foreach (var id in newest.Keys.ToList())
            {
                if (!_source.IsPortable(id, newest[id].Value))
                    newest.Remove(id);
            }

            return newest;
        }

        void Backup(IEnumerable<string> files)
        {
            var existing = files.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (existing.Count == 0)
                return;

            var dir = Path.Combine(_historyDir, DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture));
            var root = Path.GetFullPath(_source.Root).TrimEnd('\\', '/') + "\\";
            foreach (var file in existing)
            {
                var full = Path.GetFullPath(file);
                var relative = full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? full.Substring(root.Length) : Path.GetFileName(full);
                var target = Path.Combine(dir, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(full, target, true);
            }

            foreach (var old in Directory.GetDirectories(_historyDir).OrderByDescending(d => d, StringComparer.Ordinal).Skip(HistoryKept))
            {
                try { Directory.Delete(old, true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        internal sealed class ConfigEntry
        {
            public ConfigEntry(string id, string value, DateTime updatedUtc, string pc)
            {
                Id = id;
                Value = value;
                UpdatedUtc = updatedUtc;
                Pc = pc;
            }

            public string Id { get; }

            /// <summary>Nulo: a entrada foi removida (voltou ao padrão, arquivo apagado).</summary>
            public string Value { get; }

            public DateTime UpdatedUtc { get; }
            public string Pc { get; }
        }

        sealed class ConfigState
        {
            public HashSet<string> Groups { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, string> Hashes { get; } = new Dictionary<string, string>(StringComparer.Ordinal);
        }

        internal static Dictionary<string, ConfigEntry> ReadEntries(string path)
        {
            var entries = new Dictionary<string, ConfigEntry>(StringComparer.Ordinal);
            if (!File.Exists(path))
                return entries;

            var root = XDocument.Load(path, LoadOptions.PreserveWhitespace).Root;
            var pc = (string)root.Attribute("pc") ?? Path.GetFileNameWithoutExtension(path);
            foreach (var element in root.Elements("entry"))
            {
                var id = (string)element.Attribute("id");
                if (string.IsNullOrEmpty(id))
                    continue;

                string value = null;
                if ((string)element.Attribute("removed") != "true")
                {
                    value = element.Value;
                    if ((string)element.Attribute("encoding") == "base64")
                        value = Encoding.UTF8.GetString(Convert.FromBase64String(value));
                }

                var updated = DateTime.Parse((string)element.Attribute("updated"), CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
                entries[id] = new ConfigEntry(id, value, updated, pc);
            }

            return entries;
        }

        static void SaveEntries(string path, string source, string pc, Dictionary<string, ConfigEntry> entries)
        {
            var root = new XElement("retroboatConfig", new XAttribute("source", source), new XAttribute("pc", pc),
                new XComment(" Configurações deste PC para os outros. Cada PC grava só o próprio arquivo; vale a entrada mais recente. "));

            foreach (var entry in entries.Values.OrderBy(e => e.Id, StringComparer.Ordinal))
            {
                var element = new XElement("entry", new XAttribute("id", entry.Id), new XAttribute("updated", Iso(entry.UpdatedUtc)));
                if (entry.Value == null)
                    element.SetAttributeValue("removed", "true");
                else if (IsPlainText(entry.Value))
                    element.Value = entry.Value;
                else
                {
                    element.SetAttributeValue("encoding", "base64");
                    element.Value = Convert.ToBase64String(Encoding.UTF8.GetBytes(entry.Value));
                }

                root.Add(element);
            }

            WriteXml(path, new XDocument(root));
        }

        /// <summary>Texto que o XML guarda sem mudar: sem \r (vira \n), sem espaço nas pontas e sem caracteres de controle.</summary>
        static bool IsPlainText(string value)
        {
            if (value.Length == 0)
                return true;

            if (char.IsWhiteSpace(value[0]) || char.IsWhiteSpace(value[value.Length - 1]) || value.IndexOf('\r') >= 0)
                return false;

            foreach (var c in value)
            {
                if (c != '\n' && c != '\t' && (char.IsControl(c) || !XmlConvert.IsXmlChar(c)))
                    return false;
            }

            return true;
        }

        static ConfigState LoadState(string path)
        {
            if (!File.Exists(path))
                return null;

            try
            {
                var root = XDocument.Load(path).Root;
                var state = new ConfigState();
                foreach (var group in root.Elements("group"))
                    state.Groups.Add((string)group.Attribute("name"));

                foreach (var entry in root.Elements("entry"))
                    state.Hashes[(string)entry.Attribute("id")] = (string)entry.Attribute("hash");

                return state;
            }
            catch (XmlException)
            {
                // Estado danificado: vale a nuvem, como num PC novo. O que mudou só aqui fica no histórico local.
                return null;
            }
        }

        static void SaveState(string path, string source, ConfigState state)
        {
            WriteXml(path, new XDocument(new XElement("retroboatConfigSync", new XAttribute("source", source),
                state.Groups.OrderBy(g => g, StringComparer.OrdinalIgnoreCase).Select(g => new XElement("group", new XAttribute("name", g))),
                state.Hashes.OrderBy(h => h.Key, StringComparer.Ordinal).Select(h => new XElement("entry", new XAttribute("id", h.Key), new XAttribute("hash", h.Value))))));
        }

        static void WriteXml(string path, XDocument document)
        {
            var options = new XmlWriterSettings { Indent = true, IndentChars = "\t", Encoding = new UTF8Encoding(false) };
            using (var memory = new MemoryStream())
            {
                using (var writer = XmlWriter.Create(memory, options))
                    document.Save(writer);

                SaveSync.WriteAtomically(path, memory.ToArray());
            }
        }

        internal static string Hash(string value)
        {
            if (value == null)
                return "-";

            using (var sha = SHA1.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", "").ToLowerInvariant();
        }

        static string Iso(DateTime utc) => utc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

        static string SafeFileName(string name)
        {
            foreach (var invalid in Path.GetInvalidFileNameChars())
                name = name.Replace(invalid, '_');

            return name;
        }
    }
}
