using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Retroboat.Library;
using Retroboat.Settings;
using Retroboat.Text;

namespace Retroboat.Cloud
{
    public enum SetupScope
    {
        /// <summary>Nada a perguntar.</summary>
        None,

        /// <summary>Só a biblioteca da nuvem (passo 1): duas contas com biblioteca, ou o Drive aberto sem nenhuma.</summary>
        LibraryOnly,

        /// <summary>Os três passos: primeiro uso no PC, --setup ou pedido do menu NUVEM.</summary>
        Full
    }

    /// <summary>Uma conta do Google Drive como o passo 1 do setup mostra.</summary>
    public sealed class LibraryCandidate
    {
        public CloudDrive Drive { get; set; }

        /// <summary>Já tem a pasta "retroboat".</summary>
        public bool HasLibrary { get; set; }

        /// <summary>O biblioteca.xml dela, se tem.</summary>
        public LibraryIdentity Identity { get; set; }

        public int Games { get; set; }

        /// <summary>Sistemas com jogos, do que tem mais jogos para o que tem menos.</summary>
        public List<string> Systems { get; } = new List<string>();

        /// <summary>Ex.: "13 jogos (ps2)", "sem jogos ainda".</summary>
        public string Summary => Games == 0 ? "sem jogos ainda" : Games + (Games == 1 ? " jogo" : " jogos") + " (" + string.Join(", ", Systems.Take(4)) + (Systems.Count > 4 ? "..." : "") + ")";
    }

    /// <summary>Uma pasta de jogos que o passo 2 do setup oferece.</summary>
    public sealed class FolderSuggestion
    {
        public string Path { get; set; }

        /// <summary>Ex.: "Pasta padrão do RetroBat", "Pasta atual", "Disco D:".</summary>
        public string Note { get; set; }

        public long FreeBytes { get; set; }

        /// <summary>O caminho por último: se for longo, só ele é cortado na tela.</summary>
        public string Label => Note + " · " + Format.Bytes(FreeBytes) + " livres · " + Path;
    }

    /// <summary>
    /// RB-021: o que o setup decide e faz, sem tela. As telas ficam no Retroboat.exe (SetupFlow); daqui só sai o que vai
    /// para o Drive, e só pelos caminhos da seção 9 do desenho: criar pastas e o biblioteca.xml.
    /// </summary>
    public static class LibrarySetup
    {
        /// <summary>Tempo entre a primeira e a segunda busca antes de oferecer criar uma biblioteca (Drive ainda carregando).</summary>
        public static readonly TimeSpan SecondSearchDelay = TimeSpan.FromSeconds(5);

        public static SetupScope Decide(RetroboatSettings settings, bool forced, LibraryChoiceOutcome choice)
        {
            if (forced || string.IsNullOrEmpty(settings.RomsPath))
                return SetupScope.Full;

            return choice == LibraryChoiceOutcome.Several || choice == LibraryChoiceOutcome.NoLibrary ? SetupScope.LibraryOnly : SetupScope.None;
        }

        public static List<LibraryCandidate> Describe(IEnumerable<CloudDrive> drives)
        {
            var candidates = new List<LibraryCandidate>();
            foreach (var drive in drives)
            {
                var candidate = new LibraryCandidate { Drive = drive, HasLibrary = drive.HasRetroboatFolder };
                if (candidate.HasLibrary)
                {
                    candidate.Identity = LibraryIdentity.ReadAll(drive.RetroboatPath).FirstOrDefault();
                    var games = new CloudLibrary(Path.Combine(drive.RetroboatPath, CloudLocator.GamesFolderName)).GetGames();
                    candidate.Games = games.Count;
                    candidate.Systems.AddRange(games.GroupBy(g => g.System, StringComparer.OrdinalIgnoreCase)
                        .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase).Select(g => g.Key));
                }

                candidates.Add(candidate);
            }

            return candidates;
        }

        /// <summary>
        /// Passa a usar a biblioteca da conta: lê o biblioteca.xml ou cria um (nunca substitui) e grava o id em
        /// <paramref name="settings"/>. Quem chama grava o retroboat.ini.
        /// </summary>
        /// <returns>null se o biblioteca.xml está danificado (ele fica como está).</returns>
        public static LibraryIdentity Use(RetroboatSettings settings, CloudDrive drive, string pcName = null)
        {
            var identity = LibraryIdentity.Adopt(drive.RetroboatPath, pcName);
            if (identity != null)
                settings.CloudLibrary = identity.Id;

            return identity;
        }

        /// <summary>
        /// "Criar uma biblioteca nova", já confirmado. Se a pasta "retroboat" apareceu nesse meio-tempo (o Drive terminou de
        /// carregar), usa a que existe em vez de criar. Senão cria a pasta, o biblioteca.xml, jogos e as pastas de console.
        /// </summary>
        /// <returns>A identidade e se a pasta foi criada agora; null na identidade se o biblioteca.xml está danificado.</returns>
        public static LibraryIdentity Create(RetroBatInstall install, RetroboatSettings settings, CloudDrive drive, out bool created, string pcName = null)
        {
            created = !drive.HasRetroboatFolder;
            if (created)
                Directory.CreateDirectory(drive.RetroboatPath);

            var identity = Use(settings, drive, pcName);
            if (identity != null)
                CloudFolders.Ensure(install, new CloudLocation(drive.RetroboatPath, drive.Source, identity.Id));

            return identity;
        }

        /// <summary>
        /// Pastas de jogos para o passo 2: a atual (se houver), a padrão do RetroBat e &lt;disco&gt;:\Jogos nos outros discos
        /// fixos, do que tem mais espaço livre para o que tem menos (no máximo dois). Unidades do Google Drive ficam de
        /// fora: os jogos baixados têm de ficar no PC.
        /// </summary>
        public static List<FolderSuggestion> SuggestRomsFolders(RetroBatInstall install, string currentRomsPath)
        {
            var disks = new List<(string Root, long Free)>();
            foreach (var drive in DriveInfo.GetDrives())
            {
                try
                {
                    if (drive.DriveType == DriveType.Fixed && drive.IsReady &&
                        !string.Equals(drive.VolumeLabel, CloudLocator.GoogleDriveVolumeLabel, StringComparison.OrdinalIgnoreCase))
                        disks.Add((drive.RootDirectory.FullName, drive.AvailableFreeSpace));
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }

            return SuggestRomsFolders(install, currentRomsPath, disks);
        }

        internal static List<FolderSuggestion> SuggestRomsFolders(RetroBatInstall install, string currentRomsPath, IEnumerable<(string Root, long Free)> disks)
        {
            var list = disks.ToList();
            long FreeOn(string path) => list.Where(d => SameRoot(d.Root, path)).Select(d => d.Free).FirstOrDefault();

            var suggestions = new List<FolderSuggestion>();
            void Add(string path, string note)
            {
                if (!suggestions.Any(s => RetroBatInstall.SamePath(s.Path, path)))
                    suggestions.Add(new FolderSuggestion { Path = path, Note = note, FreeBytes = FreeOn(path) });
            }

            if (!string.IsNullOrWhiteSpace(currentRomsPath))
                Add(Path.GetFullPath(currentRomsPath).TrimEnd('\\'), "Pasta atual");

            Add(install.DefaultRomsPath, "Pasta padrão do RetroBat");

            foreach (var disk in list.Where(d => !SameRoot(d.Root, install.Root)).OrderByDescending(d => d.Free).Take(2))
                Add(Path.Combine(disk.Root, "Jogos"), "Disco " + disk.Root.TrimEnd('\\'));

            return suggestions;
        }

        /// <summary>A pasta escolhida à mão serve: caminho completo num disco que existe neste PC, fora do Google Drive.</summary>
        public static bool IsValidRomsFolder(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path))
                    return false;

                var root = Path.GetPathRoot(Path.GetFullPath(path));
                if (!Directory.Exists(root))
                    return false;

                return !CloudLocator.GetGoogleDriveRoots().Any(r => SameRoot(r, root));
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        static bool SameRoot(string a, string b)
        {
            return string.Equals(Path.GetPathRoot(Path.GetFullPath(a)), Path.GetPathRoot(Path.GetFullPath(b)), StringComparison.OrdinalIgnoreCase);
        }
    }
}
