using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Retroboat.Bios;
using Retroboat.Cloud;
using Retroboat.Config;
using Retroboat.Integrity;
using Retroboat.Library;
using Retroboat.Saves;
using Retroboat.Text;
using Retroboat.Themes;

namespace Retroboat.Cli
{
    /// <summary>
    /// Ferramenta de linha de comando para testar e operar a biblioteca do Retroboat.
    /// </summary>
    static class Program
    {
        const int ExitOk = 0;
        const int ExitError = 1;
        const int ExitUsage = 2;
        const int ExitCorrupt = 3;
        const int ExitCancelled = 130;

        const string Usage =
@"retroboat-cli: biblioteca de jogos do Retroboat

Nuvem (pasta do Google Drive para computador):
  retroboat-cli cloud where
  retroboat-cli cloud ls [sistema]
  retroboat-cli cloud verify [sistema | sistema/jogo] [--samples N] [--full]
  retroboat-cli cloud install sistema/jogo --to <pasta de jogos> [--force]
  retroboat-cli cloud uninstall sistema/jogo --to <pasta de jogos>

BIOS na nuvem (pasta bios\ dentro da pasta do Retroboat no Drive):
  retroboat-cli bios status --retrobat <pasta do RetroBat>
  retroboat-cli bios sync --retrobat <pasta do RetroBat> [--dry-run]
  retroboat-cli bios organize [--dry-run]

Memory cards na nuvem (retroboat\saves no Drive; o emulador gerencia os cartões):
  retroboat-cli saves status --retrobat <pasta do RetroBat> [--pc NOME]
  retroboat-cli saves sync --retrobat <pasta do RetroBat> [--pc NOME] [--keep local|cloud]
  retroboat-cli saves history <cartão> --retrobat <pasta do RetroBat>
  retroboat-cli saves verify --retrobat <pasta do RetroBat>
  retroboat-cli saves restore <cartão> [<id da cópia>] --retrobat <pasta do RetroBat>   (com o emulador fechado)
  retroboat-cli saves lock|unlock --retrobat <pasta do RetroBat> [--pc NOME] [--game NOME]   (para testes)

Configurações na nuvem (retroboat\configuracoes no Drive: opções do ES, controles, PCSX2):
  retroboat-cli config status --retrobat <pasta do RetroBat> [--pc NOME]
  retroboat-cli config sync --retrobat <pasta do RetroBat> [--pc NOME]   (com o ES e os emuladores fechados)

Biblioteca isolada (RB-003):
  retroboat-cli library apply --retrobat <pasta do RetroBat> [--roms <pasta de jogos>] [--dry-run] [--no-folders]

Status do Retroboat nos temas (Carbon e PlayStation-X; volta depois de reinstalar o tema pelo ES):
  retroboat-cli themes status --retrobat <pasta do RetroBat>
  retroboat-cli themes apply --retrobat <pasta do RetroBat>

Opções gerais:
  --cloud <pasta>   usa esta pasta como a pasta do Retroboat na nuvem, em vez de detectar o Google Drive";

        static readonly CancellationTokenSource Cancellation = new CancellationTokenSource();

        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                Cancellation.Cancel();
            };

            var options = new Options(args);
            try
            {
                switch ((options.Positional(0) + " " + options.Positional(1)).Trim())
                {
                    case "cloud where": return CloudWhere(options);
                    case "cloud ls": return CloudList(options);
                    case "cloud verify": return CloudVerify(options);
                    case "cloud install": return CloudInstall(options);
                    case "cloud uninstall": return CloudUninstall(options);
                    case "library apply": return LibraryApply(options);
                    case "themes status": return ThemesApply(options, dryRun: true);
                    case "themes apply": return ThemesApply(options, dryRun: false);
                    case "bios status": return BiosStatusCommand(options);
                    case "bios sync": return BiosSyncCommand(options, organizeOnly: false);
                    case "bios organize": return BiosSyncCommand(options, organizeOnly: true);
                    case "saves status": return SavesStatus(options);
                    case "saves sync": return SavesSync(options);
                    case "saves history": return SavesHistory(options);
                    case "saves verify": return SavesVerify(options);
                    case "saves restore": return SavesRestore(options);
                    case "config status": return ConfigCommand(options, ConfigSyncMode.DryRun);
                    case "config sync": return ConfigCommand(options, ConfigSyncMode.Full);
                    case "saves lock": return SavesLock(options, locked: true);
                    case "saves unlock": return SavesLock(options, locked: false);
                    default:
                        Console.WriteLine(Usage);
                        return ExitUsage;
                }
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine();
                Console.WriteLine("Cancelado.");
                return ExitCancelled;
            }
            catch (UsageException ex)
            {
                Console.Error.WriteLine(ex.Message);
                return ExitUsage;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException)
            {
                Console.Error.WriteLine("Erro: " + ex.Message);
                return ExitError;
            }
        }

        static int CloudWhere(Options options)
        {
            var roots = CloudLocator.GetGoogleDriveRoots().ToList();
            Console.WriteLine(roots.Count == 0
                ? "Google Drive para computador: nenhuma unidade encontrada."
                : "Google Drive para computador: " + string.Join(", ", roots));

            var location = CloudLocator.Locate(options.Value("--cloud"));
            if (location == null)
            {
                Console.WriteLine("Pasta do Retroboat na nuvem: não encontrada.");
                return ExitError;
            }

            Console.WriteLine("Pasta do Retroboat: " + location.RetroboatPath + "  [" + location.Source + "]" + (location.Exists ? "" : "  (ainda não existe)"));
            Console.WriteLine("Jogos:              " + location.GamesPath + (Directory.Exists(location.GamesPath) ? "" : "  (ainda não existe)"));
            return location.Exists ? ExitOk : ExitError;
        }

        static int CloudList(Options options)
        {
            var library = OpenLibrary(options);
            var system = options.Positional(2);
            var games = library.GetGames(system);

            if (games.Count == 0)
            {
                Console.WriteLine(system == null ? "Nenhum jogo em " + library.GamesPath : "Nenhum jogo em " + system + ".");
                return ExitOk;
            }

            foreach (var group in games.GroupBy(g => g.System))
            {
                Console.WriteLine(string.Format(Format.Culture, "{0}  ({1} jogos, {2})", group.Key, group.Count(), Format.Bytes(group.Sum(g => g.Size))));
                foreach (var game in group)
                    Console.WriteLine("  " + Format.Bytes(game.Size).PadLeft(9) + "  " + game.Name + (game.IsDirectory ? "\\" : ""));
            }

            Console.WriteLine(string.Format(Format.Culture, "Total: {0} jogos, {1}", games.Count, Format.Bytes(games.Sum(g => g.Size))));
            return ExitOk;
        }

        static int CloudVerify(Options options)
        {
            var library = OpenLibrary(options);
            var target = options.Positional(2);
            var games = SelectGames(library, target);
            int samples = options.IntValue("--samples", IntegrityChecker.DefaultSamples);
            bool full = options.Flag("--full");

            int corrupt = 0, suspicious = 0;
            foreach (var game in games)
            {
                Cancellation.Token.ThrowIfCancellationRequested();

                foreach (var file in FilesOf(game))
                {
                    var label = game.Id + (game.IsDirectory ? "\\" + file.Substring(game.FullPath.Length).TrimStart('\\') : "");
                    var quick = IntegrityChecker.QuickCheck(file, samples);
                    Console.WriteLine(Describe(quick).PadRight(14) + label + (quick.Detail != null ? "  (" + quick.Detail + ")" : ""));

                    bool isCorrupt = quick.IsCorrupt;
                    if (full && quick.Status != IntegrityStatus.Empty && quick.Status != IntegrityStatus.Unreadable)
                    {
                        // A leitura completa decide: a amostragem só olha alguns pontos do arquivo.
                        var progress = new ConsoleProgress();
                        var hashes = IntegrityChecker.FullCheck(file, progress, Cancellation.Token);
                        progress.End();
                        Console.WriteLine("              sha1 " + hashes.Sha1 + "  crc32 " + hashes.Crc32 +
                            (hashes.AllZero ? "  TODO ZERADO (leitura completa)" : quick.Status == IntegrityStatus.Zeroed ? "  NÃO está todo zerado (leitura completa)" : ""));
                        isCorrupt = hashes.AllZero;
                    }

                    if (isCorrupt)
                        corrupt++;
                    else if (quick.Status == IntegrityStatus.SignatureMissing || quick.Status == IntegrityStatus.Unreadable)
                        suspicious++;
                }
            }

            Console.WriteLine(string.Format(Format.Culture, "{0} jogos verificados: {1} corrompidos, {2} suspeitos.", games.Count, corrupt, suspicious));
            return corrupt > 0 ? ExitCorrupt : ExitOk;
        }

        static int CloudInstall(Options options)
        {
            var library = OpenLibrary(options);
            var game = FindGame(library, options.Positional(2));
            var romsRoot = options.Required("--to");

            Console.WriteLine("Instalando " + game.Id + " (" + Format.Bytes(game.Size) + ") em " + Path.Combine(romsRoot, game.System));
            WarnAboutDriveCache(romsRoot, game.Size);

            var progress = new ConsoleProgress();
            var result = new GameInstaller().Install(game, romsRoot, progress, Cancellation.Token, options.Flag("--force"));
            progress.End();

            switch (result.Outcome)
            {
                case InstallOutcome.Installed:
                    Console.WriteLine("Instalado: " + result.Destination + (result.BytesResumed > 0 ? "  (retomado de " + Format.Bytes(result.BytesResumed) + ")" : ""));
                    break;
                case InstallOutcome.AlreadyInstalled:
                    Console.WriteLine("Já estava instalado: " + result.Destination);
                    break;
                case InstallOutcome.Cancelled:
                    Console.WriteLine("Cancelado. Rode o mesmo comando para continuar de onde parou.");
                    return ExitCancelled;
                case InstallOutcome.InsufficientSpace:
                    Console.Error.WriteLine("Espaço insuficiente: " + result.Error);
                    return ExitError;
                default:
                    Console.Error.WriteLine("Falhou: " + result.Error + ". Rode de novo para continuar de onde parou.");
                    return ExitError;
            }

            if (result.Integrity != null && result.Integrity.IsCorrupt)
            {
                Console.Error.WriteLine("ATENÇÃO: a cópia está " + Describe(result.Integrity).ToLowerInvariant() + " (" + result.Integrity.Detail + "). O arquivo na nuvem está danificado.");
                return ExitCorrupt;
            }

            return ExitOk;
        }

        static int CloudUninstall(Options options)
        {
            var library = OpenLibrary(options);
            var game = FindGame(library, options.Positional(2));
            var removed = GameInstaller.Uninstall(game, options.Required("--to"));
            Console.WriteLine(removed ? "Cópia local removida; o jogo continua na nuvem." : "Não havia cópia local.");
            return ExitOk;
        }

        static int LibraryApply(Options options)
        {
            var install = new RetroBatInstall(options.Required("--retrobat"));
            var romsRoot = options.Value("--roms") ?? install.DefaultRomsPath;
            bool dryRun = options.Flag("--dry-run");

            var result = new IsolatedLibrary(install).Apply(romsRoot, dryRun, !options.Flag("--no-folders"));

            Console.WriteLine((dryRun ? "[simulação] " : "") + "Pasta de jogos: " + Path.GetFullPath(romsRoot) + "  (" + result.SystemsMapped + " sistemas)");
            if (result.OverlayWritten) Console.WriteLine("  overlay es_systems_retroboat.cfg gravado");
            if (result.OverlayRemoved) Console.WriteLine("  overlay es_systems_retroboat.cfg removido (pasta padrão)");
            if (result.LauncherConfigUpdated) Console.WriteLine("  emulatorLauncher.cfg atualizado (roms antes: " + (result.PreviousRomsValue ?? "padrão") + ")");
            if (result.FoldersCreated > 0) Console.WriteLine("  " + result.FoldersCreated + " pastas de sistema criadas");
            if (result.GamelistsCreated > 0) Console.WriteLine("  " + result.GamelistsCreated + " gamelists vazios criados em .emulationstation\\gamelists");
            if (result.GamelistsMoved.Count > 0) Console.WriteLine("  gamelists movidos para .emulationstation\\gamelists: " + string.Join(", ", result.GamelistsMoved));
            if (!result.HasChanges) Console.WriteLine("  nada a mudar");

            foreach (var warning in result.Warnings)
                Console.WriteLine("  AVISO: " + warning);

            return ExitOk;
        }

        static int ThemesApply(Options options, bool dryRun)
        {
            var install = new RetroBatInstall(options.Required("--retrobat"));
            var result = ThemeOverlays.Apply(install, dryRun: dryRun);

            Console.WriteLine((dryRun ? "[simulação] " : "") + "Temas em " + install.ThemesPath);
            foreach (var item in result.Applied) Console.WriteLine("  " + (dryRun ? "faltando: " : "") + item);
            foreach (var name in result.UpToDate) Console.WriteLine("  " + name + ": em dia");
            if (result.Applied.Count == 0 && result.UpToDate.Count == 0)
                Console.WriteLine("  nenhum tema suportado instalado (" + string.Join(", ", ThemeOverlays.All.Select(o => o.Folder)) + ")");

            foreach (var warning in result.Warnings)
                Console.WriteLine("  AVISO: " + warning);

            return ExitOk;
        }

        static IEnumerable<SaveSync> OpenSaves(Options options)
        {
            var install = new RetroBatInstall(options.Required("--retrobat"));
            var location = CloudLocator.Locate(options.Value("--cloud"));
            if (location == null || !location.Exists)
                throw new InvalidOperationException("Pasta do Retroboat na nuvem não encontrada. Use --cloud <pasta>.");

            var savesPath = SaveRules.GetSavesPath(install);
            return SaveRules.All.Select(rule => new SaveSync(savesPath, install.RetroboatDataPath, location.RetroboatPath, rule, options.Value("--pc"))).ToList();
        }

        /// <summary>
        /// config status: o que iria e viria, sem mudar nada. config sync: sincroniza tudo; com o ES ou o emulador aberto,
        /// aquela parte só envia, como no lançador.
        /// </summary>
        static int ConfigCommand(Options options, ConfigSyncMode mode)
        {
            var install = new RetroBatInstall(options.Required("--retrobat"));
            var location = CloudLocator.Locate(options.Value("--cloud"));
            if (location == null || !location.Exists)
                throw new InvalidOperationException("Pasta do Retroboat na nuvem não encontrada. Use --cloud <pasta>.");

            int problems = 0;
            foreach (var source in CloudConfigs.AllSources(install))
            {
                var sourceMode = mode;
                if (mode == ConfigSyncMode.Full && (source is EmulatorConfigSource emulator ? emulator.IsRunning() : CloudConfigs.IsEsRunning()))
                {
                    sourceMode = ConfigSyncMode.SendOnly;
                    Console.WriteLine(source.DisplayName + ": o programa está aberto; só envia, e o que veio da nuvem espera ele fechar.");
                }

                var sync = new ConfigSync(source, install.RetroboatDataPath, location.RetroboatPath, options.Value("--pc"));
                var result = sync.Sync(sourceMode);
                Console.WriteLine(source.DisplayName + " (" + sync.PcName + "): " + source.Root);
                Console.WriteLine("  nuvem: " + sync.CloudFolder);
                if (result.Skipped)
                {
                    Console.WriteLine("  não está neste PC (emulador ainda não instalado?)");
                    continue;
                }

                string received = mode == ConfigSyncMode.DryRun ? "da nuvem:" : "recebido:", sent = mode == ConfigSyncMode.DryRun ? "para a nuvem:" : "enviado:";
                foreach (var id in result.Received) Console.WriteLine("  " + received.PadRight(14) + id);
                foreach (var id in result.Sent) Console.WriteLine("  " + sent.PadRight(14) + id);
                foreach (var id in result.Pending) Console.WriteLine("  " + "esperando:".PadRight(14) + id);
                foreach (var item in result.Conflicts) Console.WriteLine("  " + "nos dois:".PadRight(14) + item);
                foreach (var error in result.Errors) Console.WriteLine("  ERRO: " + error);
                if (!result.HasChanges && result.Pending.Count == 0 && result.Errors.Count == 0)
                    Console.WriteLine("  em dia");

                problems += result.Errors.Count;
            }

            return problems > 0 ? ExitError : ExitOk;
        }

        static int SavesStatus(Options options)
        {
            foreach (var sync in OpenSaves(options))
            {
                Console.WriteLine(sync.Rule.DisplayName + " (" + sync.PcName + "): " + sync.LocalFolder);
                Console.WriteLine("  nuvem: " + sync.CloudFolder);
                var other = sync.FindActiveLock();
                if (other != null)
                    Console.WriteLine("  EM USO no " + other.Pc + " desde " + other.SinceUtc.ToLocalTime().ToString("dd/MM HH:mm") + (string.IsNullOrEmpty(other.Game) ? "" : " (" + other.Game + ")"));

                if (!sync.LocalDeviceAvailable)
                    Console.WriteLine("  pasta local inacessível (pen drive ou disco desconectado?)");

                foreach (var plan in sync.Plan())
                {
                    Console.WriteLine(string.Format("  {0,-14} {1}{2}{3}{4}", plan.Action, plan.Name,
                        plan.Cloud != null ? "   nuvem: " + plan.Cloud.TimeUtc.ToLocalTime().ToString("dd/MM HH:mm:ss") + " " + plan.Cloud.Pc + (plan.Cloud.Deleted ? " (apagado)" : "") : "   (não está na nuvem)",
                        plan.WaitingFor != null ? "   aguardando " + plan.WaitingFor : "",
                        plan.LocalProblem != null ? "   DANIFICADO neste PC: " + plan.LocalProblem
                        : plan.LocalBlankNew ? "   em branco (sem formatar; vai para a nuvem no primeiro save)" : ""));
                }

                foreach (var item in sync.UnsupportedItems())
                    Console.WriteLine("  não sincronizado: " + item);
            }

            return ExitOk;
        }

        static int SavesSync(Options options)
        {
            var keep = options.Value("--keep");
            ConflictChoice? choice = keep == null ? (ConflictChoice?)null
                : keep == "local" ? ConflictChoice.KeepLocal
                : keep == "cloud" ? ConflictChoice.KeepCloud
                : throw new UsageException("--keep aceita local ou cloud");

            int problems = 0;
            foreach (var sync in OpenSaves(options))
            {
                var result = sync.Sync(p => choice);
                foreach (var item in result.Uploaded) Console.WriteLine("enviado:  " + item);
                foreach (var item in result.Downloaded) Console.WriteLine("baixado:  " + item);
                foreach (var item in result.DeletedLocal) Console.WriteLine("apagado aqui (apagado em outro PC): " + item);
                foreach (var item in result.DeletedInCloud) Console.WriteLine("apagado na nuvem: " + item);
                foreach (var item in result.Conflicts) Console.WriteLine("CONFLITO (use --keep local|cloud): " + item);
                foreach (var item in result.Waiting) Console.WriteLine("aguardando a versão de outro PC: " + item);
                foreach (var item in result.Repaired) Console.WriteLine("consertado: " + item);
                foreach (var item in result.DamagedHere.Concat(result.DamagedInCloud)) Console.WriteLine("DANIFICADO: " + item);
                foreach (var item in result.Skipped) Console.WriteLine("não sincronizado: " + item);
                foreach (var error in result.Errors) Console.WriteLine("ERRO: " + error);
                problems += result.Conflicts.Count + result.Errors.Count;
            }

            return problems > 0 ? ExitError : ExitOk;
        }

        static int SavesHistory(Options options)
        {
            var name = options.Positional(2) ?? throw new UsageException("Informe o cartão, ex.: Mcd001.ps2");
            foreach (var sync in OpenSaves(options))
            {
                foreach (var copy in sync.Copies(name))
                {
                    string state;
                    if (copy.Deleted)
                        state = "apagado no emulador";
                    else
                        state = sync.ReadCopy(copy, out var problem) != null ? "ok" : "DANIFICADA: " + problem;

                    Console.WriteLine(string.Format("{0:dd/MM/yyyy HH:mm:ss}  {1,-18} {2,-12} {3}  [{4}]",
                        copy.TimeUtc.ToLocalTime(), copy.KindName, copy.Pc, state, copy.Id));
                }
            }

            return ExitOk;
        }

        /// <summary>Confere todos os cartões deste PC e todas as cópias guardadas, sem mudar nada.</summary>
        static int SavesVerify(Options options)
        {
            int problems = 0;
            foreach (var sync in OpenSaves(options))
            {
                if (!sync.LocalDeviceAvailable)
                {
                    Console.WriteLine("ATENÇÃO pasta local inacessível: " + sync.LocalFolder);
                    problems++;
                }

                var names = new SortedSet<string>(sync.LocalItems().Select(Path.GetFileName), StringComparer.OrdinalIgnoreCase);
                if (Directory.Exists(sync.CloudFolder))
                {
                    foreach (var dir in Directory.GetDirectories(sync.CloudFolder))
                    {
                        if (!Path.GetFileName(dir).Equals(SaveSync.PcsFolderName, StringComparison.OrdinalIgnoreCase))
                            names.Add(Path.GetFileName(dir));
                    }
                }

                foreach (var name in names)
                {
                    var local = Path.Combine(sync.LocalFolder, name);
                    string localState = "não existe neste PC";
                    if (File.Exists(local))
                    {
                        var problem = sync.Problem(File.ReadAllBytes(local));
                        localState = problem == null ? "ok" : "DANIFICADO: " + problem;
                        if (problem != null)
                            problems++;
                    }

                    var copies = sync.Copies(name).Where(c => !c.Deleted).ToList();
                    var bad = copies.Where(c => sync.ReadCopy(c, out _) == null).ToList();
                    var newest = sync.Copies(name).FirstOrDefault(c => c.Kind == SaveCopyKind.Cloud);
                    bool newestIsGood = newest == null || newest.Deleted || !bad.Any(b => b.Id == newest.Id);
                    if (!newestIsGood)
                        problems++;

                    Console.WriteLine(name + ": neste PC " + localState + "; " + copies.Count + " cópia(s) guardada(s), " +
                                      (bad.Count == 0 ? "todas boas" : bad.Count + " danificada(s)") +
                                      (newestIsGood ? "" : "; a MAIS NOVA da nuvem está danificada (o próximo PC a sincronizar põe a boa anterior no lugar)"));
                }
            }

            return problems > 0 ? ExitCorrupt : ExitOk;
        }

        static int SavesRestore(Options options)
        {
            var name = options.Positional(2) ?? throw new UsageException("Informe o cartão, ex.: Mcd001.ps2");
            if (System.Diagnostics.Process.GetProcessesByName("retroboat-launch").Length > 0)
                throw new InvalidOperationException("há um jogo aberto pelo Retroboat; feche o emulador antes de restaurar um cartão");

            foreach (var sync in OpenSaves(options))
            {
                var result = sync.Restore(name, options.Positional(3));
                foreach (var item in result.Repaired)
                    Console.WriteLine(item);

                Console.WriteLine("A versão anterior deste PC ficou no histórico; a restaurada já é a mais nova na nuvem.");
            }

            return ExitOk;
        }

        static int SavesLock(Options options, bool locked)
        {
            foreach (var sync in OpenSaves(options))
            {
                if (locked)
                    sync.Lock(options.Value("--game") ?? "teste");
                else
                    sync.Unlock();

                Console.WriteLine((locked ? "Trava" : "Sem trava") + " para " + sync.PcName + " em " + sync.CloudFolder);
            }

            return ExitOk;
        }

        static int BiosStatusCommand(Options options)
        {
            var install = new RetroBatInstall(options.Required("--retrobat"));
            int problems = 0;
            foreach (var report in BiosStatus.Check(Path.Combine(install.Root, "bios")))
            {
                Console.WriteLine((report.State == BiosState.Ok ? "OK      " : "ATENÇÃO ") + report.Summary);
                if (report.State != BiosState.Ok)
                    problems++;
            }

            return problems > 0 ? ExitCorrupt : ExitOk;
        }

        static int BiosSyncCommand(Options options, bool organizeOnly)
        {
            var location = CloudLocator.Locate(options.Value("--cloud"));
            if (location == null || !location.Exists)
                throw new InvalidOperationException("Pasta do Retroboat na nuvem não encontrada. Use --cloud <pasta>.");

            bool dryRun = options.Flag("--dry-run");
            var localBios = organizeOnly ? "" : Path.Combine(new RetroBatInstall(options.Required("--retrobat")).Root, "bios");
            var sync = new BiosSync(location.RetroboatPath, localBios);
            var result = organizeOnly ? sync.Organize(dryRun) : sync.Sync(dryRun);

            string prefix = dryRun ? "[simulação] " : "";
            foreach (var file in result.Organized) Console.WriteLine(prefix + "organizado no Drive: bios\\" + file);
            foreach (var file in result.Downloaded) Console.WriteLine(prefix + "baixado:  bios\\" + file);
            foreach (var file in result.Uploaded) Console.WriteLine(prefix + "enviado:  bios\\" + file);
            foreach (var file in result.SkippedCorrupt) Console.WriteLine("ZERADO, não copiado: bios\\" + file);
            foreach (var error in result.Errors) Console.WriteLine("ERRO: " + error);
            if (!organizeOnly)
                Console.WriteLine(result.Unchanged + " arquivo(s) já em dia.");

            return result.Errors.Count > 0 ? ExitError : result.SkippedCorrupt.Count > 0 ? ExitCorrupt : ExitOk;
        }

        static CloudLibrary OpenLibrary(Options options)
        {
            var location = CloudLocator.Locate(options.Value("--cloud"));
            if (location == null)
                throw new InvalidOperationException("Google Drive para computador não encontrado. Instale e entre na sua conta, ou use --cloud <pasta>.");

            if (!Directory.Exists(location.GamesPath))
                throw new InvalidOperationException("Pasta de jogos não encontrada: " + location.GamesPath);

            return new CloudLibrary(location.GamesPath);
        }

        static List<CloudGame> SelectGames(CloudLibrary library, string target)
        {
            if (string.IsNullOrEmpty(target))
                return library.GetGames().ToList();

            if (target.IndexOf('/') >= 0 || target.IndexOf('\\') >= 0)
                return new List<CloudGame> { FindGame(library, target) };

            return library.GetGames(target).ToList();
        }

        static CloudGame FindGame(CloudLibrary library, string id)
        {
            if (string.IsNullOrEmpty(id))
                throw new UsageException("Informe o jogo como sistema/nome, ex.: ps2/\"Jogo (USA).iso\"");

            return library.Find(id) ?? throw new InvalidOperationException("Jogo não encontrado na nuvem: " + id);
        }

        static IEnumerable<string> FilesOf(CloudGame game)
        {
            return game.IsDirectory
                ? Directory.EnumerateFiles(game.FullPath, "*", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                : new[] { game.FullPath };
        }

        static string Describe(IntegrityResult result)
        {
            switch (result.Status)
            {
                case IntegrityStatus.Ok: return "OK " + result.Format;
                case IntegrityStatus.Unverified: return "NÃO VERIFICADO";
                case IntegrityStatus.SignatureMissing: return "SUSPEITO";
                case IntegrityStatus.Zeroed: return "ZERADO";
                case IntegrityStatus.Empty: return "VAZIO";
                default: return "ILEGÍVEL";
            }
        }

        /// <summary>
        /// O Drive para computador guarda o que é lido num cache local (por padrão no C:), então
        /// instalar no mesmo disco ocupa o espaço do jogo duas vezes até o cache ser liberado.
        /// </summary>
        static void WarnAboutDriveCache(string romsRoot, long size)
        {
            try
            {
                var cacheRoot = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
                var targetRoot = Path.GetPathRoot(Path.GetFullPath(romsRoot));
                if (!string.Equals(cacheRoot, targetRoot, StringComparison.OrdinalIgnoreCase))
                    return;

                var free = new DriveInfo(targetRoot).AvailableFreeSpace;
                if (free < size * 2 + GameInstaller.FreeSpaceMargin)
                    Console.WriteLine("Aviso: o cache do Google Drive também fica em " + targetRoot + ". Durante a instalação o jogo pode ocupar o dobro do espaço (" + Format.Bytes(free) + " livres).");
            }
            catch (IOException) { }
            catch (ArgumentException) { }
        }

        sealed class UsageException : Exception
        {
            public UsageException(string message) : base(message) { }
        }

        sealed class Options
        {
            static readonly HashSet<string> ValueOptions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "--cloud", "--to", "--retrobat", "--roms", "--samples", "--pc", "--keep", "--game" };

            readonly List<string> _positional = new List<string>();
            readonly Dictionary<string, string> _values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            readonly HashSet<string> _flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            public Options(string[] args)
            {
                for (int i = 0; i < args.Length; i++)
                {
                    var arg = args[i];
                    if (ValueOptions.Contains(arg))
                    {
                        if (i + 1 >= args.Length)
                            throw new UsageException("Falta o valor de " + arg);

                        _values[arg] = args[++i];
                    }
                    else if (arg.StartsWith("--", StringComparison.Ordinal))
                    {
                        _flags.Add(arg);
                    }
                    else
                    {
                        _positional.Add(arg);
                    }
                }
            }

            public string Positional(int index) => index < _positional.Count ? _positional[index] : null;
            public string Value(string name) => _values.TryGetValue(name, out var value) ? value : null;
            public bool Flag(string name) => _flags.Contains(name);

            public string Required(string name) => Value(name) ?? throw new UsageException("Falta a opção " + name);

            public int IntValue(string name, int fallback)
            {
                var value = Value(name);
                if (value == null)
                    return fallback;

                if (int.TryParse(value, out var number) && number > 0)
                    return number;

                throw new UsageException(name + " precisa ser um número positivo");
            }
        }
    }
}
