using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Retroboat.Library;

namespace Retroboat.Accounts
{
    /// <summary>Uma conta que o ES guarda no es_settings.cfg (usuário, senha, chave...).</summary>
    public sealed class AccountService
    {
        public AccountService(string name, params string[] settings)
        {
            Name = name;
            Settings = settings;
        }

        /// <summary>Nome para mensagens e para a nuvem ("ScreenScraper").</summary>
        public string Name { get; }

        /// <summary>Chaves do es_settings.cfg que formam a conta, sincronizadas sempre juntas.</summary>
        public IReadOnlyList<string> Settings { get; }
    }

    public sealed class AccountSyncResult
    {
        /// <summary>Contas que vieram da nuvem para este PC.</summary>
        public List<string> Received { get; } = new List<string>();

        /// <summary>Contas deste PC enviadas para a nuvem.</summary>
        public List<string> Sent { get; } = new List<string>();

        public List<string> Errors { get; } = new List<string>();
    }

    /// <summary>
    /// Logins dos serviços que o ES usa (ScreenScraper, IGDB, RetroAchievements) na nuvem: quem entra num
    /// PC sem login já recebe o da nuvem, e quem troca o login no ES manda a troca para os outros PCs.
    ///
    /// Na nuvem: contas\&lt;PC&gt;.xml, um arquivo por PC (como os metadados); vale a entrada mais recente de
    /// cada conta. Os valores vão em Base64, só para não aparecerem na pré-visualização e na busca do Drive:
    /// não é criptografia, e quem abre a pasta do Drive consegue ler, como já acontece com o es_settings.cfg.
    ///
    /// Para saber de que lado veio uma diferença, cada PC guarda o hash (não o valor) do que sincronizou
    /// por último em &lt;RetroBat&gt;\retroboat\contas-sync.xml: se o login local não mudou desde então, a
    /// mudança foi em outro PC e vem da nuvem; se mudou, vai para a nuvem.
    /// </summary>
    public sealed class AccountSync
    {
        public static readonly IReadOnlyList<AccountService> Services = new[]
        {
            new AccountService("ScreenScraper", "ScreenScraperUser", "ScreenScraperPass"),
            new AccountService("IGDB", "IGDBClientID", "IGDBSecret"),
            new AccountService("RetroAchievements", "global.retroachievements.username", "global.retroachievements.password")
        };

        public const string StateFileName = "contas-sync.xml";

        readonly string _settingsPath;
        readonly string _statePath;
        readonly string _cloudDir;
        readonly string _pcName;

        public AccountSync(RetroBatInstall install, string cloudRetroboatPath, string pcName = null)
        {
            _settingsPath = Path.Combine(install.UserEmulationStationPath, "es_settings.cfg");
            _statePath = Path.Combine(install.RetroboatDataPath, StateFileName);
            _cloudDir = Path.Combine(cloudRetroboatPath, "contas");
            _pcName = string.IsNullOrEmpty(pcName) ? Environment.MachineName : pcName;
        }

        public AccountSyncResult Apply()
        {
            var result = new AccountSyncResult();
            var settings = EsSettings.Load(_settingsPath);
            var state = LoadState(_statePath);
            var ownPath = Path.Combine(_cloudDir, SafeFileName(_pcName) + ".xml");
            var own = ReadAccounts(ownPath);
            var ownBefore = Serialize(own).ToString();
            var cloud = ReadNewest(_cloudDir);
            bool settingsChanged = false;

            foreach (var service in Services)
            {
                var local = service.Settings.ToDictionary(s => s, s => settings.Get(s) ?? "", StringComparer.Ordinal);
                bool localEmpty = local.Values.All(string.IsNullOrEmpty);
                cloud.TryGetValue(service.Name, out var remote);
                var localKey = Fingerprint(local);
                state.TryGetValue(service.Name, out var lastSynced);

                if (remote != null && (localEmpty || (localKey != Fingerprint(remote.Values) && localKey == lastSynced)))
                {
                    // Sem login aqui, ou o login daqui é o que já veio da nuvem e outro PC trocou: recebe.
                    foreach (var setting in service.Settings)
                        settings.Set(setting, remote.Values.TryGetValue(setting, out var value) ? value : "");

                    settingsChanged = true;
                    state[service.Name] = Fingerprint(remote.Values);
                    result.Received.Add(service.Name);
                }
                else if (!localEmpty && (remote == null || localKey != Fingerprint(remote.Values)))
                {
                    // Login novo ou trocado neste PC: envia.
                    own[service.Name] = new AccountEntry { Service = service.Name, Updated = DateTime.UtcNow, Values = local };
                    state[service.Name] = localKey;
                    result.Sent.Add(service.Name);
                }
                else if (!localEmpty)
                {
                    state[service.Name] = localKey; // Igual nos dois lados.
                }
            }

            if (Serialize(own).ToString() != ownBefore)
            {
                Directory.CreateDirectory(_cloudDir);
                SaveAtomically(ownPath, new XDocument(Serialize(own)));
            }

            if (settingsChanged)
                settings.Save();

            SaveState(_statePath, state);
            return result;
        }

        sealed class AccountEntry
        {
            public string Service;
            public DateTime Updated;
            public Dictionary<string, string> Values = new Dictionary<string, string>(StringComparer.Ordinal);
        }

        static Dictionary<string, AccountEntry> ReadNewest(string cloudDir)
        {
            var newest = new Dictionary<string, AccountEntry>(StringComparer.OrdinalIgnoreCase);
            if (!Directory.Exists(cloudDir))
                return newest;

            foreach (var file in Directory.GetFiles(cloudDir, "*.xml"))
            {
                Dictionary<string, AccountEntry> entries;
                try { entries = ReadAccounts(file); }
                catch (XmlException) { continue; } // Arquivo de outro PC no meio do envio pelo Drive.

                foreach (var entry in entries.Values)
                {
                    if (!newest.TryGetValue(entry.Service, out var current) || entry.Updated > current.Updated)
                        newest[entry.Service] = entry;
                }
            }

            return newest;
        }

        static Dictionary<string, AccountEntry> ReadAccounts(string path)
        {
            var entries = new Dictionary<string, AccountEntry>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(path))
                return entries;

            foreach (var account in XDocument.Load(path).Root.Elements("account"))
            {
                var entry = new AccountEntry
                {
                    Service = (string)account.Attribute("service"),
                    Updated = DateTime.Parse((string)account.Attribute("updated"), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal)
                };

                foreach (var setting in account.Elements("setting"))
                    entry.Values[(string)setting.Attribute("name")] = Encoding.UTF8.GetString(Convert.FromBase64String(setting.Value));

                if (!string.IsNullOrEmpty(entry.Service))
                    entries[entry.Service] = entry;
            }

            return entries;
        }

        static XElement Serialize(Dictionary<string, AccountEntry> entries)
        {
            return new XElement("retroboatAccounts",
                new XComment(" Logins do ES para os outros PCs. Valores em Base64 (não é criptografia). "),
                entries.Values.OrderBy(e => e.Service, StringComparer.OrdinalIgnoreCase).Select(e => new XElement("account",
                    new XAttribute("service", e.Service),
                    new XAttribute("updated", e.Updated.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)),
                    e.Values.OrderBy(v => v.Key, StringComparer.Ordinal).Select(v => new XElement("setting",
                        new XAttribute("name", v.Key),
                        Convert.ToBase64String(Encoding.UTF8.GetBytes(v.Value)))))));
        }

        /// <summary>Hash dos valores da conta: identifica o login sem guardar a senha neste arquivo.</summary>
        static string Fingerprint(IReadOnlyDictionary<string, string> values)
        {
            var text = string.Join("\u0001", values.OrderBy(v => v.Key, StringComparer.Ordinal).Select(v => v.Key + "=" + v.Value));
            using (var sha = SHA256.Create())
                return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(text)));
        }

        static Dictionary<string, string> LoadState(string path)
        {
            var state = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(path))
                return state;

            try
            {
                foreach (var account in XDocument.Load(path).Root.Elements("account"))
                    state[(string)account.Attribute("service")] = (string)account.Attribute("hash");
            }
            catch (XmlException)
            {
                // Arquivo corrompido: começa de novo; no pior caso, o login local é reenviado.
            }

            return state;
        }

        static void SaveState(string path, Dictionary<string, string> state)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            SaveAtomically(path, new XDocument(new XElement("retroboatAccountSync",
                state.OrderBy(s => s.Key, StringComparer.OrdinalIgnoreCase).Select(s => new XElement("account",
                    new XAttribute("service", s.Key), new XAttribute("hash", s.Value))))));
        }

        static void SaveAtomically(string path, XDocument document)
        {
            var temp = path + ".retroboat-part";
            document.Save(temp);
            if (File.Exists(path))
                File.Delete(path);

            File.Move(temp, path);
        }

        static string SafeFileName(string name)
        {
            foreach (var invalid in Path.GetInvalidFileNameChars())
                name = name.Replace(invalid, '_');

            return name;
        }
    }
}
