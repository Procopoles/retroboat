using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Retroboat.Library
{
    public sealed class SystemFolder
    {
        public SystemFolder(string name, string folder, string command = null)
        {
            Name = name;
            Folder = folder;
            Command = command;
        }

        /// <summary>Nome do sistema no ES (ex.: "gw").</summary>
        public string Name { get; }

        /// <summary>Pasta relativa à pasta de jogos (ex.: "gameandwatch"). Nem sempre é igual ao nome.</summary>
        public string Folder { get; }

        /// <summary>Comando original do es_systems.cfg para abrir um jogo do sistema.</summary>
        public string Command { get; }
    }

    /// <summary>
    /// Coloca o retroboat-launch na frente do comando dos sistemas, para baixar o jogo da nuvem e sincronizar os
    /// saves e as configurações do emulador antes de o emulatorLauncher abri-lo.
    /// </summary>
    public sealed class CommandWrapper
    {
        /// <param name="systemFolders">Só estes sistemas; nulo: todos.</param>
        public CommandWrapper(string launcherPath, IEnumerable<string> systemFolders = null)
        {
            LauncherPath = launcherPath;
            SystemFolders = systemFolders == null ? null : new HashSet<string>(systemFolders, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>Caminho como o ES entende, ex.: %HOME%\..\retroboat\retroboat-launch.exe.</summary>
        public string LauncherPath { get; }

        /// <summary>Nulo: todos os sistemas.</summary>
        public ISet<string> SystemFolders { get; }

        public string Wrap(SystemFolder system)
        {
            if (string.IsNullOrWhiteSpace(system.Command) || (SystemFolders != null && !SystemFolders.Contains(system.Folder)))
                return null;

            var quoted = "\"" + LauncherPath + "\"";
            return system.Command.TrimStart().StartsWith(quoted, StringComparison.OrdinalIgnoreCase) ? system.Command : quoted + " " + system.Command.Trim();
        }
    }

    public sealed class ApplyResult
    {
        public int SystemsMapped { get; set; }
        public bool OverlayWritten { get; set; }
        public bool OverlayRemoved { get; set; }
        public bool LauncherConfigUpdated { get; set; }
        public string PreviousRomsValue { get; set; }
        public int FoldersCreated { get; set; }
        public int GamelistsCreated { get; set; }
        public List<string> GamelistsMoved { get; } = new List<string>();
        public List<string> Warnings { get; } = new List<string>();

        public bool HasChanges => OverlayWritten || OverlayRemoved || LauncherConfigUpdated || FoldersCreated > 0 || GamelistsCreated > 0 || GamelistsMoved.Count > 0;
    }

    /// <summary>
    /// RB-003: deixa os jogos numa pasta escolhida pelo usuário e tira os metadados de dentro dela,
    /// sem mudar nenhum arquivo do RetroBat. Usa só pontos de extensão que já existem:
    /// <list type="bullet">
    /// <item>es_systems_retroboat.cfg: o ES mescla es_systems_*.cfg por cima do es_systems.cfg;
    /// o overlay só troca o &lt;path&gt; de cada sistema e, nos sistemas com jogos na nuvem, o
    /// &lt;command&gt; (com o retroboat-launch na frente).</item>
    /// <item>roms= no emulatorLauncher.cfg: muda a pasta de jogos para todos os geradores.</item>
    /// <item>.emulationstation\gamelists\&lt;sistema&gt;\gamelist.xml: o ES lê e grava ali quando a pasta
    /// de jogos não tem gamelist.xml.</item>
    /// </list>
    /// É regerado a cada início, então sistemas novos do upstream entram sozinhos.
    /// </summary>
    public sealed class IsolatedLibrary
    {
        public const string OverlayFileName = "es_systems_retroboat.cfg";
        public const string LauncherConfigMarker = "# Retroboat: pasta de jogos (RB-003)";

        const string RomsPrefix = @"~\..\roms\";
        const string EmptyGamelist = "<?xml version=\"1.0\"?>\r\n<gameList />\r\n";

        static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

        readonly RetroBatInstall _install;

        public IsolatedLibrary(RetroBatInstall install)
        {
            _install = install;
        }

        public string OverlayPath => Path.Combine(_install.UserEmulationStationPath, OverlayFileName);

        public ApplyResult Apply(string romsRoot, bool dryRun = false, bool createFolders = true, CommandWrapper wrapper = null)
        {
            var systemsConfig = _install.SystemsConfigPath;
            if (systemsConfig == null)
                throw new InvalidOperationException("es_systems.cfg não encontrado em " + _install.UserEmulationStationPath);

            romsRoot = Path.GetFullPath(romsRoot).TrimEnd('\\', '/');
            bool isDefault = RetroBatInstall.SamePath(romsRoot, _install.DefaultRomsPath);

            var result = new ApplyResult();
            var systems = ReadSystems(systemsConfig);
            var overlaySystems = WithoutDuplicateNames(systems, romsRoot, isDefault, result);
            result.SystemsMapped = overlaySystems.Count;

            ApplyOverlay(overlaySystems, romsRoot, isDefault, wrapper, dryRun, result);
            ApplyLauncherConfig(romsRoot, isDefault, dryRun, result);
            ApplyFoldersAndGamelists(overlaySystems, romsRoot, dryRun, createFolders, result);
            CheckOtherOverlays(result);

            return result;
        }

        /// <summary>Sistemas cuja pasta fica em ~\..\roms\ (os demais, como "library", não são tocados).</summary>
        public static List<SystemFolder> ReadSystems(string systemsConfigPath)
        {
            var doc = XDocument.Load(systemsConfigPath);
            var systems = new List<SystemFolder>();

            foreach (var system in doc.Root.Elements("system"))
            {
                var name = ((string)system.Element("name") ?? "").Trim();
                var path = ((string)system.Element("path") ?? "").Trim().Replace('/', '\\');
                if (name.Length == 0 || !path.StartsWith(RomsPrefix, StringComparison.OrdinalIgnoreCase))
                    continue;

                var folder = path.Substring(RomsPrefix.Length).Trim('\\');
                if (folder.Length > 0)
                    systems.Add(new SystemFolder(name, folder, ((string)system.Element("command"))?.Trim()));
            }

            return systems;
        }

        /// <summary>
        /// O ES casa o overlay com o es_systems.cfg pelo nome e só altera o primeiro sistema com aquele
        /// nome. Se o upstream repetir um nome (aconteceu com "starship", RB-012), só o primeiro pode
        /// ser movido; o outro continua na pasta padrão.
        /// </summary>
        static List<SystemFolder> WithoutDuplicateNames(List<SystemFolder> systems, string romsRoot, bool isDefault, ApplyResult result)
        {
            foreach (var group in systems.GroupBy(s => s.Name, StringComparer.Ordinal).Where(g => g.Count() > 1))
            {
                var folders = group.Select(s => s.Folder).ToList();
                result.Warnings.Add(
                    "O es_systems.cfg tem " + folders.Count + " sistemas chamados \"" + group.Key + "\" (pastas: " + string.Join(", ", folders) + "). " +
                    (isDefault
                        ? "O ES mistura os gamelists deles."
                        : "Só a pasta \"" + folders[0] + "\" vai para " + romsRoot + "; as outras continuam na pasta padrão do RetroBat."));
            }

            return FirstPerName(systems);
        }

        /// <summary>Um sistema por nome: o primeiro, o único que o overlay consegue mover (ver WithoutDuplicateNames).</summary>
        public static List<SystemFolder> FirstPerName(IEnumerable<SystemFolder> systems)
        {
            return systems.GroupBy(s => s.Name, StringComparer.Ordinal).Select(g => g.First()).ToList();
        }

        /// <summary>
        /// Overlay só com o que muda em cada sistema: o &lt;path&gt; (se a pasta de jogos não for a padrão) e o
        /// &lt;command&gt; (se o sistema tem jogos na nuvem). Sistemas sem mudança ficam de fora. Retorna null
        /// quando não há nada a mudar.
        /// </summary>
        public static string BuildOverlay(IEnumerable<SystemFolder> systems, string romsRoot, bool includePaths = true, CommandWrapper wrapper = null)
        {
            var elements = new List<XElement>();
            foreach (var system in systems)
            {
                var command = wrapper?.Wrap(system);
                if (!includePaths && command == null)
                    continue;

                elements.Add(new XElement("system",
                    new XElement("name", system.Name),
                    includePaths ? new XElement("path", Path.Combine(romsRoot, system.Folder)) : null,
                    command != null ? new XElement("command", command) : null));
            }

            if (elements.Count == 0)
                return null;

            var root = new XElement("systemList", elements);

            var doc = new XDocument(
                new XDeclaration("1.0", "UTF-8", null),
                new XComment(" Gerado pelo Retroboat (RB-003, RB-004). Não edite: é recriado a cada início. "),
                root);

            var settings = new XmlWriterSettings { Indent = true, IndentChars = "  ", Encoding = Utf8NoBom, NewLineChars = "\r\n" };
            using (var stream = new MemoryStream())
            {
                using (var writer = XmlWriter.Create(stream, settings))
                    doc.Save(writer);

                return Utf8NoBom.GetString(stream.ToArray()) + "\r\n";
            }
        }

        void ApplyOverlay(List<SystemFolder> systems, string romsRoot, bool isDefault, CommandWrapper wrapper, bool dryRun, ApplyResult result)
        {
            var content = BuildOverlay(systems, romsRoot, includePaths: !isDefault, wrapper);
            if (content == null)
            {
                if (File.Exists(OverlayPath))
                {
                    result.OverlayRemoved = true;
                    if (!dryRun)
                        File.Delete(OverlayPath);
                }

                return;
            }
            if (File.Exists(OverlayPath) && File.ReadAllText(OverlayPath, Encoding.UTF8) == content)
                return;

            result.OverlayWritten = true;
            if (!dryRun)
                File.WriteAllText(OverlayPath, content, Utf8NoBom);
        }

        void ApplyLauncherConfig(string romsRoot, bool isDefault, bool dryRun, ApplyResult result)
        {
            var path = _install.EmulatorLauncherConfigPath;
            if (!File.Exists(path))
            {
                result.Warnings.Add("emulatorLauncher.cfg não encontrado em " + _install.EmulationStationPath + ": os emuladores vão procurar os jogos na pasta padrão.");
                return;
            }

            var content = File.ReadAllText(path);
            var updated = KeyValueConfig.SetManagedValue(content, "roms", isDefault ? null : romsRoot, LauncherConfigMarker);
            if (updated == content)
            {
                if (isDefault && KeyValueConfig.GetValue(content, "roms") is string manual)
                    result.Warnings.Add("O emulatorLauncher.cfg tem roms=" + manual + " configurado à mão; o Retroboat não mexeu nele.");

                return;
            }

            result.LauncherConfigUpdated = true;
            result.PreviousRomsValue = KeyValueConfig.GetValue(content, "roms");
            if (!dryRun)
                File.WriteAllText(path, updated, Utf8NoBom);
        }

        void ApplyFoldersAndGamelists(List<SystemFolder> systems, string romsRoot, bool dryRun, bool createFolders, ApplyResult result)
        {
            bool romsReachable = Directory.Exists(romsRoot) || (createFolders && Directory.Exists(Path.GetPathRoot(romsRoot)));
            if (!romsReachable)
                result.Warnings.Add("A pasta de jogos " + romsRoot + " não está acessível (disco desconectado?). Os sistemas dela não vão aparecer no ES.");

            foreach (var system in systems)
            {
                var romFolder = Path.Combine(romsRoot, system.Folder);
                if (romsReachable && createFolders && !Directory.Exists(romFolder))
                {
                    result.FoldersCreated++;
                    if (!dryRun)
                        Directory.CreateDirectory(romFolder);
                }

                var localGamelist = Path.Combine(_install.GamelistsPath, system.Name, "gamelist.xml");
                var romGamelist = Path.Combine(romFolder, "gamelist.xml");
                bool romGamelistExists = romsReachable && File.Exists(romGamelist);

                if (File.Exists(localGamelist))
                {
                    if (romGamelistExists)
                        result.Warnings.Add("O ES vai usar " + romGamelist + " em vez de " + localGamelist + ", porque o da pasta de jogos tem prioridade. Junte os dois e apague o da pasta de jogos.");

                    continue;
                }

                if (!dryRun)
                    Directory.CreateDirectory(Path.GetDirectoryName(localGamelist));

                if (romGamelistExists)
                {
                    // Os caminhos ./ dentro do gamelist são relativos à pasta do sistema, não ao arquivo,
                    // então continuam valendo depois da mudança.
                    result.GamelistsMoved.Add(system.Name);
                    if (!dryRun)
                        File.Move(romGamelist, localGamelist);
                }
                else
                {
                    // Sem um gamelist local, o ES criaria um novo dentro da pasta de jogos na primeira gravação.
                    result.GamelistsCreated++;
                    if (!dryRun)
                        File.WriteAllText(localGamelist, EmptyGamelist, Utf8NoBom);
                }
            }
        }

        /// <summary>
        /// O ES para de ler os es_systems_*.cfg seguintes quando um deles é inválido (RB-002), o que
        /// desligaria o overlay do Retroboat sem aviso.
        /// </summary>
        void CheckOtherOverlays(ApplyResult result)
        {
            foreach (var dir in new[] { _install.UserEmulationStationPath, _install.EmulationStationPath })
            {
                if (!Directory.Exists(dir))
                    continue;

                foreach (var file in Directory.GetFiles(dir, "es_systems_*.cfg"))
                {
                    if (string.Equals(Path.GetFileName(file), OverlayFileName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    try
                    {
                        var doc = XDocument.Load(file);
                        if (doc.Root == null || doc.Root.Name != "systemList")
                            result.Warnings.Add(file + " não tem <systemList>: o ES pode ignorar o overlay do Retroboat.");
                    }
                    catch (XmlException ex)
                    {
                        result.Warnings.Add(file + " é um XML inválido (" + ex.Message + "): o ES pode ignorar o overlay do Retroboat.");
                    }
                }
            }
        }
    }
}
