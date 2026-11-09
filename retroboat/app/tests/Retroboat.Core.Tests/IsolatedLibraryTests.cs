using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Retroboat.Cloud;
using Retroboat.Library;
using Xunit;

namespace Retroboat.Tests
{
    public class IsolatedLibraryTests
    {
        const string Systems = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<systemList>
  <system>
    <name>snes</name>
    <path>~\..\roms\snes</path>
    <extension>.sfc .zip</extension>
    <command>""%HOME%\emulatorLauncher.exe"" -rom %ROM%</command>
  </system>
  <system>
    <name>gw</name>
    <path>~\..\roms\gameandwatch</path>
  </system>
  <system>
    <name>library</name>
    <path>~\..\library</path>
  </system>
  <system>
    <name>mess</name>
    <path></path>
  </system>
</systemList>";

        const string LauncherConfig = "# EmulationStation config files path\r\nhome=.\\.emulationstation\r\n\r\nbios=.\\..\\bios\r\n";

        static RetroBatInstall CreateInstall(TempDir dir)
        {
            dir.WriteText(@"RetroBat\emulationstation\.emulationstation\es_systems.cfg", Systems);
            dir.WriteText(@"RetroBat\emulationstation\emulatorLauncher.cfg", LauncherConfig);
            dir.WriteFile(@"RetroBat\retrobat.exe", new byte[1]);
            return new RetroBatInstall(dir.Combine("RetroBat"));
        }

        [Fact]
        public void Reads_only_systems_under_roms_and_keeps_folder_names()
        {
            using (var dir = new TempDir())
            {
                var install = CreateInstall(dir);

                var systems = IsolatedLibrary.ReadSystems(install.SystemsConfigPath);

                Assert.Equal(new[] { "snes:snes", "gw:gameandwatch" }, systems.Select(s => s.Name + ":" + s.Folder));
            }
        }

        [Fact]
        public void Custom_folder_writes_overlay_config_and_local_gamelists_but_no_console_folders()
        {
            using (var dir = new TempDir())
            {
                var install = CreateInstall(dir);
                var roms = dir.Combine("Jogos");

                var result = new IsolatedLibrary(install).Apply(roms);

                Assert.True(result.OverlayWritten);
                var overlay = XDocument.Load(Path.Combine(install.UserEmulationStationPath, IsolatedLibrary.OverlayFileName));
                var paths = overlay.Root.Elements("system").ToDictionary(s => (string)s.Element("name"), s => (string)s.Element("path"));
                Assert.Equal(Path.Combine(roms, "snes"), paths["snes"]);
                Assert.Equal(Path.Combine(roms, "gameandwatch"), paths["gw"]);
                Assert.Equal(2, paths.Count);
                Assert.All(overlay.Root.Elements("system"), s => Assert.Equal(new[] { "name", "path" }, s.Elements().Select(e => e.Name.LocalName)));

                var config = File.ReadAllText(install.EmulatorLauncherConfigPath);
                Assert.Equal(roms, KeyValueConfig.GetValue(config, "roms"));
                Assert.StartsWith(LauncherConfig, config);

                // As pastas de console ficam só no Drive: aqui, a pasta só surge com o primeiro jogo (download ou marcador).
                Assert.False(Directory.Exists(roms));
                Assert.Empty(result.FoldersRemoved);

                Assert.True(File.Exists(Path.Combine(install.GamelistsPath, "snes", "gamelist.xml")));
                Assert.True(File.Exists(Path.Combine(install.GamelistsPath, "gw", "gamelist.xml")));
                Assert.Equal("gameList", XDocument.Load(Path.Combine(install.GamelistsPath, "gw", "gamelist.xml")).Root.Name.LocalName);
                Assert.Empty(result.Warnings);
            }
        }

        [Fact]
        public void Second_apply_changes_nothing()
        {
            using (var dir = new TempDir())
            {
                var install = CreateInstall(dir);
                var library = new IsolatedLibrary(install);
                library.Apply(dir.Combine("Jogos"));
                var overlayTime = File.GetLastWriteTimeUtc(library.OverlayPath);

                var second = library.Apply(dir.Combine("Jogos"));

                Assert.False(second.HasChanges);
                Assert.Equal(overlayTime, File.GetLastWriteTimeUtc(library.OverlayPath));
            }
        }

        [Fact]
        public void Gamelist_inside_the_games_folder_is_moved_out()
        {
            using (var dir = new TempDir())
            {
                var install = CreateInstall(dir);
                var gamelist = "<gameList><game><path>./Mario.sfc</path><image>./images/Mario.png</image></game></gameList>";
                dir.WriteText(@"Jogos\snes\gamelist.xml", gamelist);

                var result = new IsolatedLibrary(install).Apply(dir.Combine("Jogos"));

                Assert.Equal(new[] { "snes" }, result.GamelistsMoved);
                Assert.False(File.Exists(dir.Combine("Jogos", "snes", "gamelist.xml")));
                Assert.Equal(gamelist, File.ReadAllText(Path.Combine(install.GamelistsPath, "snes", "gamelist.xml")));
            }
        }

        [Fact]
        public void Default_folder_also_moves_gamelists_out_of_the_games_folders()
        {
            using (var dir = new TempDir())
            {
                var install = CreateInstall(dir);
                dir.WriteText(@"RetroBat\roms\snes\gamelist.xml", "<gameList />");

                var result = new IsolatedLibrary(install).Apply(install.DefaultRomsPath);

                Assert.Equal(new[] { "snes" }, result.GamelistsMoved);
                Assert.False(File.Exists(Path.Combine(install.UserEmulationStationPath, IsolatedLibrary.OverlayFileName)));
                Assert.Null(KeyValueConfig.GetValue(File.ReadAllText(install.EmulatorLauncherConfigPath), "roms"));
            }
        }

        [Fact]
        public void Going_back_to_the_default_folder_removes_overlay_and_managed_roms_key()
        {
            using (var dir = new TempDir())
            {
                var install = CreateInstall(dir);
                var library = new IsolatedLibrary(install);
                library.Apply(dir.Combine("Jogos"));

                var result = library.Apply(install.DefaultRomsPath);

                Assert.True(result.OverlayRemoved);
                Assert.True(result.LauncherConfigUpdated);
                Assert.False(File.Exists(library.OverlayPath));
                Assert.Equal(LauncherConfig, File.ReadAllText(install.EmulatorLauncherConfigPath));
            }
        }

        [Fact]
        public void Dry_run_writes_nothing()
        {
            using (var dir = new TempDir())
            {
                var install = CreateInstall(dir);
                dir.WriteText(@"Jogos\snes\gamelist.xml", "<gameList />");

                var result = new IsolatedLibrary(install).Apply(dir.Combine("Jogos"), dryRun: true);

                Assert.True(result.OverlayWritten);
                Assert.True(result.LauncherConfigUpdated);
                Assert.Equal(new[] { "snes" }, result.GamelistsMoved);
                Assert.False(File.Exists(Path.Combine(install.UserEmulationStationPath, IsolatedLibrary.OverlayFileName)));
                Assert.Equal(LauncherConfig, File.ReadAllText(install.EmulatorLauncherConfigPath));
                Assert.True(File.Exists(dir.Combine("Jogos", "snes", "gamelist.xml")));
                Assert.False(Directory.Exists(install.GamelistsPath));
            }
        }

        [Fact]
        public void Warns_about_an_invalid_overlay_that_would_disable_ours()
        {
            using (var dir = new TempDir())
            {
                var install = CreateInstall(dir);
                dir.WriteText(@"RetroBat\emulationstation\.emulationstation\es_systems_meu.cfg", "<systemList><system>");

                var result = new IsolatedLibrary(install).Apply(dir.Combine("Jogos"));

                Assert.Contains(result.Warnings, w => w.Contains("es_systems_meu.cfg"));
            }
        }

        [Fact]
        public void Warns_when_both_gamelists_exist()
        {
            using (var dir = new TempDir())
            {
                var install = CreateInstall(dir);
                dir.WriteText(@"RetroBat\emulationstation\.emulationstation\gamelists\snes\gamelist.xml", "<gameList />");
                dir.WriteText(@"Jogos\snes\gamelist.xml", "<gameList />");

                var result = new IsolatedLibrary(install).Apply(dir.Combine("Jogos"));

                Assert.Contains(result.Warnings, w => w.Contains("prioridade"));
                Assert.Empty(result.GamelistsMoved);
            }
        }

        [Fact]
        public void Unreachable_games_folder_is_a_warning_not_an_error()
        {
            using (var dir = new TempDir())
            {
                var install = CreateInstall(dir);
                var missingDrive = FindMissingDriveLetter();
                if (missingDrive == null)
                    return;

                var result = new IsolatedLibrary(install).Apply(missingDrive + @"Jogos");

                Assert.True(result.OverlayWritten);
                Assert.Contains(result.Warnings, w => w.Contains("não está acessível"));
                Assert.Empty(result.FoldersRemoved);
                Assert.Equal(2, result.GamelistsCreated);
            }
        }

        [Fact]
        public void Empty_console_folders_are_removed_and_everything_else_stays()
        {
            using (var dir = new TempDir())
            {
                var install = CreateInstall(dir);
                var roms = dir.Combine("Jogos");
                dir.WriteFile(@"Jogos\snes\Zelda.sfc", new byte[0]); // marcador de um jogo da nuvem
                Directory.CreateDirectory(dir.Combine("Jogos", "gameandwatch"));
                Directory.CreateDirectory(dir.Combine("Jogos", "minhas coisas")); // não é pasta de console
                Directory.CreateDirectory(dir.Combine("Jogos", "library"));       // sistema fora de ~\..\roms

                var result = new IsolatedLibrary(install).Apply(roms);

                Assert.Equal(new[] { "gameandwatch" }, result.FoldersRemoved);
                Assert.False(Directory.Exists(dir.Combine("Jogos", "gameandwatch")));
                Assert.True(File.Exists(dir.Combine("Jogos", "snes", "Zelda.sfc")));
                Assert.True(Directory.Exists(dir.Combine("Jogos", "minhas coisas")));
                Assert.True(Directory.Exists(dir.Combine("Jogos", "library")));
                Assert.False(new IsolatedLibrary(install).Apply(roms).HasChanges);
            }
        }

        [Fact]
        public void Console_folder_appears_with_its_first_cloud_game_and_leaves_with_its_last()
        {
            using (var dir = new TempDir())
            {
                var install = CreateInstall(dir);
                var roms = dir.Combine("Jogos");
                var cloudGame = dir.WriteFile(@"nuvem\jogos\snes\Mario.sfc", TempDir.RandomBytes(1024));
                Directory.CreateDirectory(dir.Combine("nuvem", "jogos", "gameandwatch")); // no Drive, todas as pastas de console
                var cloud = new CloudLibrary(dir.Combine("nuvem", "jogos"));
                var library = new IsolatedLibrary(install);

                library.Apply(roms);
                new CloudLibrarySync(install, roms, cloud).Apply();

                Assert.Equal(new[] { "snes" }, Directory.GetDirectories(roms).Select(Path.GetFileName));
                Assert.Equal(0, new FileInfo(Path.Combine(roms, "snes", "Mario.sfc")).Length);

                File.Delete(cloudGame);
                new CloudLibrarySync(install, roms, cloud).Apply();
                var result = library.Apply(roms);

                Assert.Equal(new[] { "snes" }, result.FoldersRemoved);
                Assert.Empty(Directory.GetDirectories(roms));
                Assert.True(Directory.Exists(dir.Combine("nuvem", "jogos", "snes")));
                Assert.True(Directory.Exists(dir.Combine("nuvem", "jogos", "gameandwatch")));
            }
        }

        [Fact]
        public void Console_folder_with_only_a_hidden_file_or_a_subfolder_stays()
        {
            using (var dir = new TempDir())
            {
                var install = CreateInstall(dir);
                var hidden = dir.WriteText(@"Jogos\snes\desktop.ini", "");
                File.SetAttributes(hidden, FileAttributes.Hidden);
                Directory.CreateDirectory(dir.Combine("Jogos", "gameandwatch", "images"));

                var result = new IsolatedLibrary(install).Apply(dir.Combine("Jogos"));

                Assert.Empty(result.FoldersRemoved);
                Assert.True(File.Exists(hidden));
                Assert.True(Directory.Exists(dir.Combine("Jogos", "gameandwatch", "images")));
            }
        }

        [Fact]
        public void Default_folder_keeps_the_retrobat_console_folders()
        {
            using (var dir = new TempDir())
            {
                var install = CreateInstall(dir);
                Directory.CreateDirectory(Path.Combine(install.DefaultRomsPath, "snes"));

                var result = new IsolatedLibrary(install).Apply(install.DefaultRomsPath);

                Assert.Empty(result.FoldersRemoved);
                Assert.True(Directory.Exists(Path.Combine(install.DefaultRomsPath, "snes")));
            }
        }

        [Fact]
        public void Dry_run_and_keep_flag_leave_empty_console_folders()
        {
            using (var dir = new TempDir())
            {
                var install = CreateInstall(dir);
                var roms = dir.Combine("Jogos");
                Directory.CreateDirectory(Path.Combine(roms, "snes"));

                var dry = new IsolatedLibrary(install).Apply(roms, dryRun: true);
                var kept = new IsolatedLibrary(install).Apply(roms, removeEmptyFolders: false);

                Assert.Equal(new[] { "snes" }, dry.FoldersRemoved);
                Assert.Empty(kept.FoldersRemoved);
                Assert.True(Directory.Exists(Path.Combine(roms, "snes")));
            }
        }

        [Fact]
        public void Real_retrobat_systems_file_maps_all_rom_folders()
        {
            var template = FindRepoFile(@"system\templates\emulationstation\es_systems.cfg");

            var systems = IsolatedLibrary.ReadSystems(template);

            Assert.True(systems.Count >= 200, "esperava pelo menos 200 sistemas, achei " + systems.Count);
            Assert.Contains(systems, s => s.Name == "gw" && s.Folder == "gameandwatch");
            Assert.DoesNotContain(systems, s => s.Name == "library" || s.Name == "screenshots" || s.Name == "retrobat");
        }

        [Fact]
        public void Duplicate_system_names_map_only_the_first_and_warn()
        {
            using (var dir = new TempDir())
            {
                var install = CreateInstall(dir);
                var withDuplicate = Systems.Replace("</systemList>",
                    @"<system><name>starship</name><path>~\..\roms\ghostship</path></system>
                      <system><name>starship</name><path>~\..\roms\starship</path></system></systemList>");
                File.WriteAllText(install.SystemsConfigPath, withDuplicate);
                var roms = dir.Combine("Jogos");

                var result = new IsolatedLibrary(install).Apply(roms);

                var overlay = XDocument.Load(Path.Combine(install.UserEmulationStationPath, IsolatedLibrary.OverlayFileName));
                var starships = overlay.Root.Elements("system").Where(s => (string)s.Element("name") == "starship").ToList();
                Assert.Single(starships);
                Assert.Equal(Path.Combine(roms, "ghostship"), (string)starships[0].Element("path"));
                Assert.Contains(result.Warnings, w => w.Contains("\"starship\"") && w.Contains("ghostship, starship"));
            }
        }

        static string FindMissingDriveLetter()
        {
            var used = DriveInfo.GetDrives().Select(d => char.ToUpperInvariant(d.Name[0])).ToList();
            for (char letter = 'Z'; letter >= 'M'; letter--)
            {
                if (!used.Contains(letter))
                    return letter + @":\";
            }

            return null;
        }

        static string FindRepoFile(string relativePath)
        {
            for (var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory); dir != null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "build.ini")))
                    return Path.Combine(dir.FullName, relativePath);
            }

            throw new FileNotFoundException("raiz do repositório (build.ini) não encontrada");
        }
    }
}
