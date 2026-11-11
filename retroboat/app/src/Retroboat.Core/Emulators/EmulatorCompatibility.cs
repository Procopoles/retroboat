using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Retroboat.Cloud;
using Retroboat.Config;
using Retroboat.Library;
using Retroboat.Saves;

namespace Retroboat.Emulators
{
    public enum CompatImpact
    {
        /// <summary>O jogo abre, mas algo do Retroboat deixa de funcionar (ex.: o save não vai para a nuvem).</summary>
        Warning,

        /// <summary>O jogo não vai abrir como deveria (ex.: o executável que o RetroBat procura sumiu).</summary>
        Blocking
    }

    /// <summary>Um problema achado: o código (para buscar no log), a frase para o usuário e o detalhe técnico.</summary>
    public sealed class CompatIssue
    {
        public string Code { get; set; }
        public CompatImpact Impact { get; set; }
        public string PackageKey { get; set; }
        public string PackageName { get; set; }
        public string Version { get; set; }

        /// <summary>Para o usuário, em português, sem jargão.</summary>
        public string Message { get; set; }

        /// <summary>O que o Retroboat esperava (caminho, nome, valor).</summary>
        public string Expected { get; set; }

        /// <summary>O que achou.</summary>
        public string Found { get; set; }

        /// <summary>Um aviso aparece uma vez por problema e versão.</summary>
        public string Key => Code + "|" + PackageKey + "|" + Version + "|" + Expected;
    }

    /// <summary>
    /// Verificação de compatibilidade dos emuladores (RB-033). Os emuladores ficam sempre na última versão; quando uma versão
    /// nova muda algo de que o Retroboat depende, esta classe só precisa PERCEBER, registrar no log de um jeito fácil de achar
    /// (logs\emuladores.log, código entre colchetes) e dizer ao usuário qual é o problema. A correção vem depois, no Retroboat.
    ///
    /// Para incluir uma verificação nova: um método Check... que devolve CompatIssue, chamado de CheckInstalled (o que precisa
    /// existir antes do jogo) ou de CheckAfterGame (o que o emulador fez durante o jogo), e uma linha na tabela do
    /// design/rb033-emuladores.md.
    /// </summary>
    public static class EmulatorCompatibility
    {
        public const string RetroArchMissing = "RETROARCH_AUSENTE";
        public const string ExecutableChanged = "EXECUTAVEL_MUDOU";
        public const string ExecutableInvalid = "EXECUTAVEL_INVALIDO";
        public const string RepositoryChanged = "REPOSITORIO_MUDOU";
        public const string SaveOutsideCloud = "SAVE_FORA_DA_NUVEM";
        public const string SaveFolderChanged = "PASTA_DE_SAVES_MUDOU";
        public const string ConfigMissing = "CONFIGURACAO_SUMIU";

        public const string ShownFileName = "emuladores-avisos.xml";

        // ------------------------------------------------------------------------------------------------------------
        // 1. Depois de instalar ou atualizar, e antes de cada jogo: o que o emulatorLauncher precisa achar.
        // ------------------------------------------------------------------------------------------------------------

        public static List<CompatIssue> CheckInstalled(RetroBatInstall install, EmulatorPackage package, string version)
        {
            var issues = new List<CompatIssue>();
            if (package.Kind == PackageKind.Core)
            {
                AddIfAny(issues, CheckRetroArch(install, package, version));
                AddIfAny(issues, CheckProgram(install, package, version, Path.Combine(package.LocalFolder(install), package.CoreFileName)));
                return issues;
            }

            var main = package.MainFile(install);
            AddIfAny(issues, CheckExpectedExecutable(install, package, version));
            if (main != null)
                AddIfAny(issues, CheckProgram(install, package, version, main));

            return issues;
        }

        static CompatIssue CheckRetroArch(RetroBatInstall install, EmulatorPackage package, string version)
        {
            var exe = EmulatorPackage.RetroArchExe(install);
            if (File.Exists(exe))
                return null;

            return Issue(RetroArchMissing, CompatImpact.Blocking, package, version,
                "O RetroArch, que roda este core, não está no PC.",
                exe, "não existe");
        }

        /// <summary>O executável que o emulatorLauncher abre (o do catálogo) está na pasta. Uma versão nova pode trocar o nome.</summary>
        static CompatIssue CheckExpectedExecutable(RetroBatInstall install, EmulatorPackage package, string version)
        {
            var expected = package.ExpectedFiles(install);
            if (expected.Count == 0 || expected.Any(File.Exists))
                return null;

            var folder = package.LocalFolder(install);
            var found = EmulatorPackage.FindExecutables(folder).Select(f => Relative(folder, f)).ToList();
            return Issue(ExecutableChanged, CompatImpact.Blocking, package, version,
                "A versão nova não tem mais o " + package.Executables[0] + ", que o RetroBat procura para abrir o jogo.",
                string.Join(" ou ", expected),
                found.Count == 0 ? "nenhum .exe na pasta" : "executáveis na pasta: " + string.Join(", ", found));
        }

        static CompatIssue CheckProgram(RetroBatInstall install, EmulatorPackage package, string version, string path)
        {
            if (!File.Exists(path))
                return null;

            var problem = PackageCheck.Program(path);
            return problem == null ? null : Issue(ExecutableInvalid, CompatImpact.Blocking, package, version,
                "O programa do emulador está danificado: " + problem + ".", "programa válido do Windows em " + path, problem);
        }

        /// <summary>O versions.xml sumiu ou mudou de formato: o Retroboat deixa de ver as versões novas.</summary>
        public static CompatIssue CheckRepository(RepositoryCache server, EmulatorPackage package, string version)
        {
            if (server == null || !server.FormatChanged)
                return null;

            return Issue(RepositoryChanged, CompatImpact.Warning, package, version,
                "O repositório do RetroBat mudou, e o Retroboat não consegue mais ver as versões novas dos emuladores.",
                "versions.xml com <systems><system name version/>", server.Problem);
        }

        // ------------------------------------------------------------------------------------------------------------
        // 2. Durante o jogo: o que o emulador gravou e onde.
        // ------------------------------------------------------------------------------------------------------------

        /// <summary>Retrato dos arquivos de save fora de saves\ (na pasta do emulador e em bios\), antes do jogo abrir.</summary>
        public static GameWatch Watch(RetroBatInstall install, EmulatorPackage package, string version, string savesPath)
        {
            var roots = new List<string> { Path.Combine(install.Root, "bios") };
            roots.Add(package.Kind == PackageKind.Core ? EmulatorPackage.RetroArchFolder(install) : package.LocalFolder(install));

            var watch = new GameWatch(install, package, version, savesPath, roots);
            foreach (var file in SaveLikeFiles(watch))
                watch.Before[file.FullName] = Stamp(file);

            return watch;
        }

        public static List<CompatIssue> CheckAfterGame(GameWatch watch)
        {
            var issues = new List<CompatIssue>();
            issues.AddRange(CheckSavesOutsideCloud(watch));
            AddIfAny(issues, CheckPcsx2MemoryCards(watch));
            issues.AddRange(CheckConfigFiles(watch));
            return issues;
        }

        /// <summary>
        /// Um save novo ou alterado durante o jogo fora de saves\ não vai para a nuvem: o emulador (ou a versão nova dele) grava
        /// em outro lugar. Ex.: a VMU do Flycast do RetroArch em bios\dc.
        /// </summary>
        static IEnumerable<CompatIssue> CheckSavesOutsideCloud(GameWatch watch)
        {
            var changed = SaveLikeFiles(watch)
                .Where(f => !watch.Before.TryGetValue(f.FullName, out var before) || before != Stamp(f))
                .Select(f => f.FullName)
                .ToList();

            return changed.Take(5).Select(path => Issue(SaveOutsideCloud, CompatImpact.Warning, watch.Package, watch.Version,
                "O jogo salvou em " + Relative(watch.Install.Root, path) + ", fora da pasta saves: esse save não vai para a nuvem.",
                "saves dentro de " + watch.SavesPath,
                path + (watch.Before.ContainsKey(path) ? " (alterado)" : " (novo)")));
        }

        /// <summary>O PCSX2 usa a pasta de memory cards que o Retroboat sincroniza (o emulatorLauncher a grava no PCSX2.ini).</summary>
        static CompatIssue CheckPcsx2MemoryCards(GameWatch watch)
        {
            if (watch.Package.Kind != PackageKind.Emulator || watch.Package.Name != SaveRules.Pcsx2MemoryCards.Emulators.First())
                return null;

            var ini = Path.Combine(watch.Package.LocalFolder(watch.Install), "inis", "PCSX2.ini");
            if (!File.Exists(ini))
                return null; // CheckConfigFiles avisa.

            var expected = Path.Combine(watch.SavesPath, SaveRules.Pcsx2MemoryCards.Folder);
            var value = IniValue(ini, "Folders", "MemoryCards");
            var resolved = string.IsNullOrWhiteSpace(value) ? null : FullPath(value, watch.Package.LocalFolder(watch.Install));
            if (resolved != null && RetroBatInstall.SamePath(resolved, expected))
                return null;

            return Issue(SaveFolderChanged, CompatImpact.Warning, watch.Package, watch.Version,
                "O PCSX2 está usando outra pasta de memory cards: os saves de PS2 não vão para a nuvem.",
                "[Folders] MemoryCards = " + expected + " em " + ini,
                value == null ? "sem a chave MemoryCards em [Folders]" : "MemoryCards = " + value);
        }

        /// <summary>Os arquivos de configuração que o Retroboat sincroniza continuam onde ele procura (o emulatorLauncher os grava a cada jogo).</summary>
        static IEnumerable<CompatIssue> CheckConfigFiles(GameWatch watch)
        {
            if (watch.Package.Kind != PackageKind.Emulator)
                yield break;

            foreach (var rule in EmulatorConfigRules.All.Where(r => string.Equals(r.Folder, watch.Package.Name, StringComparison.OrdinalIgnoreCase)))
            {
                foreach (var ini in rule.IniFiles)
                {
                    var path = Path.Combine(watch.Package.LocalFolder(watch.Install), ini.Path.Replace('/', '\\'));
                    if (!File.Exists(path))
                    {
                        yield return Issue(ConfigMissing, CompatImpact.Warning, watch.Package, watch.Version,
                            "As configurações do " + watch.Package.DisplayName + " não estão mais em " + ini.Path + ": elas não vão para a nuvem.",
                            path, "não existe depois do jogo");
                    }
                }
            }
        }

        // ------------------------------------------------------------------------------------------------------------
        // 3. Relatório: log completo sempre; aviso na tela uma vez por problema e versão.
        // ------------------------------------------------------------------------------------------------------------

        /// <summary>Registra no logs\emuladores.log, em bloco, com o código entre colchetes.</summary>
        public static void Log(RetroBatInstall install, IReadOnlyList<CompatIssue> issues, string when)
        {
            if (issues == null || issues.Count == 0)
                return;

            var first = issues[0];
            var summary = "[COMPAT] " + first.PackageName + (string.IsNullOrEmpty(first.Version) ? "" : " " + first.Version) + ": "
                + issues.Count + " problema(s) " + when + ": " + string.Join(", ", issues.Select(i => i.Code).Distinct());

            var body = new StringBuilder();
            foreach (var issue in issues)
            {
                body.AppendLine("    [" + issue.Code + "] " + (issue.Impact == CompatImpact.Blocking ? "(impede o jogo) " : "") + issue.Message);
                body.AppendLine("        esperado: " + issue.Expected);
                body.AppendLine("        achado:   " + issue.Found);
            }

            EmulatorLog.Report(install, summary, body.ToString().TrimEnd());
        }

        /// <summary>Os que ainda não apareceram na tela; ficam marcados como mostrados.</summary>
        public static IReadOnlyList<CompatIssue> TakeNotShown(RetroBatInstall install, IReadOnlyList<CompatIssue> issues)
        {
            if (issues == null || issues.Count == 0)
                return new CompatIssue[0];

            var path = Path.Combine(install.RetroboatDataPath, CloudStatusFile.Folder, ShownFileName);
            XDocument doc;
            try
            {
                doc = File.Exists(path) ? XDocument.Load(path) : new XDocument(new XElement("shown"));
            }
            catch (Exception ex) when (ex is XmlException || ex is IOException || ex is UnauthorizedAccessException)
            {
                doc = new XDocument(new XElement("shown"));
            }

            var shown = new HashSet<string>(doc.Root.Elements("issue").Select(e => (string)e.Attribute("key")), StringComparer.Ordinal);
            var fresh = issues.Where(i => !shown.Contains(i.Key)).GroupBy(i => i.Key).Select(g => g.First()).ToList();
            if (fresh.Count == 0)
                return fresh;

            foreach (var issue in fresh)
                doc.Root.Add(new XElement("issue", new XAttribute("key", issue.Key), new XAttribute("date", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture))));

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                doc.Save(path);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // Sem gravar, o aviso só aparece de novo no próximo jogo.
            }

            return fresh;
        }

        /// <summary>O título do aviso: "O PCSX2 2.9 mudou algo que o Retroboat usa".</summary>
        public static string Heading(IReadOnlyList<CompatIssue> issues)
        {
            var first = issues[0];
            return "O " + first.PackageName + (string.IsNullOrEmpty(first.Version) ? "" : " " + first.Version) + " mudou algo que o Retroboat usa";
        }

        /// <summary>O texto do aviso: cada problema numa linha e onde estão os detalhes.</summary>
        public static string UserText(IReadOnlyList<CompatIssue> issues)
        {
            var text = new StringBuilder();
            foreach (var issue in issues)
                text.AppendLine("•  " + issue.Message);

            text.AppendLine();
            text.Append("Os detalhes estão em retroboat\\logs\\" + EmulatorLog.FileName + " (" + string.Join(", ", issues.Select(i => i.Code).Distinct()) + ").");
            return text.ToString();
        }

        // ------------------------------------------------------------------------------------------------------------
        // Peças.
        // ------------------------------------------------------------------------------------------------------------

        static readonly HashSet<string> SaveExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".srm", ".sav", ".save", ".mcd", ".mcr", ".mc", ".ps2", ".vmu", ".vms", ".vmi", ".eep", ".eeprom", ".fla", ".flash",
            ".sra", ".rtc", ".dsv", ".gci", ".mpk", ".bcr", ".bkr", ".nv", ".nvram", ".nvmem", ".state", ".sstate"
        };

        /// <summary>Saves com extensão genérica: a VMU do Flycast (vmu_save_A1.bin, Jogo.A1.bin) e os memory cards (Mcd001...).</summary>
        static readonly Regex SaveNames = new Regex(@"^(vmu_save_[a-d][1-4]\.bin|.+\.[a-d][1-4]\.bin|mcd\d{3}.*|.+\.state\d*|.+\.ss\d)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>Tudo dentro de uma pasta com um destes nomes é save.</summary>
        static readonly HashSet<string> SaveFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "saves", "save", "savedata", "gamesaves", "memcards", "memcard", "vmu", "vmus", "states", "savestates", "sstates"
        };

        /// <summary>Pastas grandes que nunca têm save: não são percorridas.</summary>
        static readonly HashSet<string> SkippedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "cores", "assets", "shaders", "shadercache", "cache", "textures", "resources", "database", "thumbnails", "logs", "log",
            "screenshots", "snaps", "crashdumps", "translations", "plugins", "platforms", "imageformats", "dump", "dumps", "overlays",
            "filters", "autoconfig", "info", "downloads", "playlists", "cheats", "covers", "themes", "lang", "locale", "locales"
        };

        const int MaxDepth = 8;
        const int MaxFiles = 50000;

        static IEnumerable<FileInfo> SaveLikeFiles(GameWatch watch)
        {
            int count = 0;
            foreach (var root in watch.Roots.Where(Directory.Exists))
            {
                foreach (var file in Walk(new DirectoryInfo(root), 0, inSaveFolder: false))
                {
                    if (++count > MaxFiles)
                        yield break;

                    if (!IsInside(file.FullName, watch.SavesPath))
                        yield return file;
                }
            }
        }

        static IEnumerable<FileInfo> Walk(DirectoryInfo folder, int depth, bool inSaveFolder)
        {
            FileInfo[] files;
            DirectoryInfo[] children;
            try
            {
                files = folder.GetFiles();
                children = depth < MaxDepth ? folder.GetDirectories() : new DirectoryInfo[0];
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                yield break;
            }

            foreach (var file in files)
            {
                if (inSaveFolder || SaveExtensions.Contains(file.Extension) || SaveNames.IsMatch(file.Name))
                    yield return file;
            }

            foreach (var child in children)
            {
                if (SkippedFolders.Contains(child.Name) || (child.Attributes & FileAttributes.ReparsePoint) != 0)
                    continue;

                foreach (var file in Walk(child, depth + 1, inSaveFolder || SaveFolders.Contains(child.Name)))
                    yield return file;
            }
        }

        static string Stamp(FileInfo file) => file.Length.ToString(CultureInfo.InvariantCulture) + "|" + file.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture);

        static bool IsInside(string path, string folder)
        {
            if (string.IsNullOrEmpty(folder))
                return false;

            var root = Path.GetFullPath(folder).TrimEnd('\\') + "\\";
            return Path.GetFullPath(path).StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }

        static string Relative(string root, string path)
        {
            var prefix = Path.GetFullPath(root).TrimEnd('\\') + "\\";
            var full = Path.GetFullPath(path);
            return full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? full.Substring(prefix.Length) : full;
        }

        static string FullPath(string value, string baseFolder)
        {
            try
            {
                var path = value.Trim().Trim('"').Replace('/', '\\');
                return Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(baseFolder, path));
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            {
                return null;
            }
        }

        /// <summary>Uma chave de um .ini ("chave = valor"), sem diferenciar maiúsculas. Nulo se não existe.</summary>
        internal static string IniValue(string path, string section, string key)
        {
            string current = null;
            foreach (var raw in File.ReadAllLines(path))
            {
                var line = raw.Trim();
                if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal))
                {
                    current = line.Substring(1, line.Length - 2).Trim();
                    continue;
                }

                int eq = line.IndexOf('=');
                if (eq > 0 && string.Equals(current, section, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(line.Substring(0, eq).Trim(), key, StringComparison.OrdinalIgnoreCase))
                    return line.Substring(eq + 1).Trim();
            }

            return null;
        }

        static CompatIssue Issue(string code, CompatImpact impact, EmulatorPackage package, string version, string message, string expected, string found) => new CompatIssue
        {
            Code = code,
            Impact = impact,
            PackageKey = package.Key,
            PackageName = package.DisplayName,
            Version = version ?? "",
            Message = message,
            Expected = expected ?? "",
            Found = found ?? ""
        };

        static void AddIfAny(List<CompatIssue> issues, CompatIssue issue)
        {
            if (issue != null)
                issues.Add(issue);
        }
    }

    /// <summary>O retrato de antes do jogo, para <see cref="EmulatorCompatibility.CheckAfterGame"/>.</summary>
    public sealed class GameWatch
    {
        internal GameWatch(RetroBatInstall install, EmulatorPackage package, string version, string savesPath, IReadOnlyList<string> roots)
        {
            Install = install;
            Package = package;
            Version = version;
            SavesPath = savesPath;
            Roots = roots;
        }

        public RetroBatInstall Install { get; }
        public EmulatorPackage Package { get; }
        public string Version { get; }
        public string SavesPath { get; }
        public IReadOnlyList<string> Roots { get; }

        /// <summary>Caminho → tamanho e data dos saves de fora de saves\ antes do jogo.</summary>
        internal Dictionary<string, string> Before { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }
}
