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
                Assert.Equal(2, result.GamelistsCreated);
            }
        }

        [Fact]
        public void Console_folder_appears_with_its_first_cloud_game_and_existing_folders_stay()
        {
            using (var dir = new TempDir())
            {
                var install = CreateInstall(dir);
                var roms = dir.Combine("Jogos");
                Directory.CreateDirectory(Path.Combine(roms, "gameandwatch")); // pasta antiga, vazia: fica
                dir.WriteFile(@"nuvem\jogos\snes\Mario.sfc", TempDir.RandomBytes(1024));

                new IsolatedLibrary(install).Apply(roms);
                Assert.False(Directory.Exists(Path.Combine(roms, "snes")));

                new CloudLibrarySync(install, roms, new CloudLibrary(dir.Combine("nuvem", "jogos"))).Apply();

                Assert.Equal(0, new FileInfo(Path.Combine(roms, "snes", "Mario.sfc")).Length);
                Assert.True(Directory.Exists(Path.Combine(roms, "gameandwatch")));
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

        const string Ps1System = @"<system>
    <name>psx</name>
    <path>~\..\roms\psx</path>
    <emulators>
      <emulator name=""libretro"">
        <cores>
          <core>mednafen_psx_hw</core>
          <core>pcsx_rearmed</core>
          <!--<core>mednafen_psx</core>-->
          <core>swanstation</core>
        </cores>
      </emulator>
      <emulator name=""duckstation""/>
      <emulator name=""mednafen"">
        <cores>
          <core incompatible_extensions="".chd"">psx</core>
        </cores>
      </emulator>
    </emulators>
  </system>";

        [Fact]
        public void Ps1_defaults_to_swanstation_from_the_retrobat_list_of_emulators()
        {
            using (var dir = new TempDir())
            {
                var install = CreateInstall(dir);
                File.WriteAllText(install.SystemsConfigPath, Systems.Replace("</systemList>", Ps1System + "</systemList>"));
                var library = new IsolatedLibrary(install);

                // Pasta de jogos padrão e sem nuvem: o overlay existe só pelo emulador padrão do PS1.
                Assert.True(library.Apply(install.DefaultRomsPath).OverlayWritten);
                var psx = Assert.Single(XDocument.Load(library.OverlayPath).Root.Elements("system"));
                Assert.Equal(new[] { "name", "emulators" }, psx.Elements().Select(e => e.Name.LocalName));

                // A lista é a do RetroBat, na mesma ordem; só o padrão muda.
                var cores = psx.Descendants("core").ToList();
                Assert.Equal(new[] { "mednafen_psx_hw", "pcsx_rearmed", "swanstation", "psx" }, cores.Select(c => c.Value));
                Assert.Equal(new[] { "swanstation" }, cores.Where(c => (string)c.Attribute("default") == "true").Select(c => c.Value));
                Assert.Equal(".chd", (string)cores.Last().Attribute("incompatible_extensions"));
                Assert.Equal(new[] { "libretro", "duckstation", "mednafen" }, psx.Element("emulators").Elements("emulator").Select(e => (string)e.Attribute("name")));

                // Pasta de jogos própria: caminho e emuladores no mesmo sistema.
                library.Apply(dir.Combine("Jogos"));
                psx = XDocument.Load(library.OverlayPath).Root.Elements("system").Single(s => (string)s.Element("name") == "psx");
                Assert.Equal(new[] { "name", "path", "emulators" }, psx.Elements().Select(e => e.Name.LocalName));
            }
        }

        [Fact]
        public void Without_swanstation_in_the_retrobat_list_ps1_keeps_the_retrobat_default()
        {
            using (var dir = new TempDir())
            {
                var install = CreateInstall(dir);
                File.WriteAllText(install.SystemsConfigPath, Systems.Replace("</systemList>", Ps1System.Replace("<core>swanstation</core>", "") + "</systemList>"));

                var result = new IsolatedLibrary(install).Apply(install.DefaultRomsPath);

                Assert.False(result.OverlayWritten);
                Assert.False(File.Exists(Path.Combine(install.UserEmulationStationPath, IsolatedLibrary.OverlayFileName)));
            }
        }

        [Fact]
        public void Real_retrobat_systems_file_offers_swanstation_for_ps1()
        {
            var psx = IsolatedLibrary.ReadSystems(FindRepoFile(@"system\templates\emulationstation\es_systems.cfg")).Single(s => s.Name == "psx");

            var emulators = IsolatedLibrary.WithRetroboatDefault(psx);

            Assert.NotNull(emulators);
            Assert.Equal("swanstation", emulators.Descendants("core").Single(c => (string)c.Attribute("default") == "true").Value);
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
