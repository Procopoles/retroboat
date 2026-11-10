using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Xml.Linq;
using Retroboat.Cloud;
using Retroboat.Launch;
using Retroboat.Library;
using Retroboat.Settings;
using Xunit;

namespace Retroboat.Tests
{
    public class CloudGamesTests
    {
        const string IsoName = "Jogo (Europe) (En,Fr).iso";

        static byte[] FakeIso(int size = 3 << 20)
        {
            var data = TempDir.RandomBytes(size, seed: 5);
            System.Text.Encoding.ASCII.GetBytes("CD001").CopyTo(data, 0x8001);
            return data;
        }

        static string WriteZip(TempDir dir, string relativePath, string entryName, byte[] content, CompressionLevel level = CompressionLevel.Optimal)
        {
            var path = dir.Combine(relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
            using (var stream = archive.CreateEntry(entryName, level).Open())
                stream.Write(content, 0, content.Length);

            return path;
        }

        [Fact]
        public void Zip_catalog_reads_names_sizes_and_crc()
        {
            using (var dir = new TempDir())
            {
                var content = FakeIso();
                var zip = WriteZip(dir, @"jogos\ps2\Jogo.zip", IsoName, content);

                var entry = ZipCatalog.GetSingleDiscImage(ZipCatalog.Read(zip));

                Assert.Equal(IsoName, entry.FileName);
                Assert.Equal(content.Length, entry.UncompressedSize);
                Assert.Equal((ushort)8, entry.Method);
                var crc = new Retroboat.Integrity.Crc32();
                crc.Append(content, 0, content.Length);
                Assert.Equal(crc.Value, entry.Crc32);
            }
        }

        [Fact]
        public void Zip_with_several_files_or_a_cartridge_is_installed_as_is()
        {
            using (var dir = new TempDir())
            {
                var cartridge = WriteZip(dir, @"jogos\snes\Mario.zip", "Mario.sfc", TempDir.RandomBytes(1000));
                Assert.Null(ZipCatalog.GetSingleDiscImage(ZipCatalog.Read(cartridge)));

                var game = new CloudLibrary(dir.Combine("jogos")).Find("snes/Mario.zip");
                Assert.Equal("Mario.zip", GameInstaller.GetLocalName(game));
            }
        }

        [Fact]
        public void Disc_inside_a_zip_is_extracted_on_install_and_checked_by_crc()
        {
            using (var cloud = new TempDir())
            using (var roms = new TempDir())
            {
                var content = FakeIso();
                WriteZip(cloud, @"jogos\ps2\Jogo.zip", IsoName, content);
                var game = new CloudLibrary(cloud.Combine("jogos")).Find("ps2/Jogo.zip");

                Assert.Equal(IsoName, GameInstaller.GetLocalName(game));

                var result = new GameInstaller { BufferSize = 64 * 1024 }.Install(game, roms.Path);

                Assert.Equal(InstallOutcome.Installed, result.Outcome);
                Assert.Equal(roms.Combine("ps2", IsoName), result.Destination);
                Assert.Equal(content, File.ReadAllBytes(result.Destination));
                Assert.False(File.Exists(roms.Combine("ps2", "Jogo.zip")));
                Assert.Equal(Retroboat.Integrity.IntegrityStatus.Ok, result.Integrity.Status);
            }
        }

        [Fact]
        public void Zip_extraction_aborted_by_the_system_is_an_interruption_not_a_cancel()
        {
            using (var cloud = new TempDir())
            using (var roms = new TempDir())
            {
                WriteZip(cloud, @"jogos\ps2\Jogo.zip", IsoName, FakeIso());
                var game = new CloudLibrary(cloud.Combine("jogos")).Find("ps2/Jogo.zip");

                var result = new GameInstaller { BufferSize = 64 * 1024 }.Install(game, roms.Path, new GameInstallerTests.AbortOnFirstReport(), CancellationToken.None);

                Assert.Equal(InstallOutcome.Interrupted, result.Outcome);
                Assert.False(File.Exists(roms.Combine("ps2", IsoName + GameInstaller.PartSuffix)));
            }
        }

        [Fact]
        public void Damaged_zip_content_fails_and_leaves_nothing_behind()
        {
            using (var cloud = new TempDir())
            using (var roms = new TempDir())
            {
                var zip = WriteZip(cloud, @"jogos\ps2\Jogo.zip", IsoName, FakeIso(), CompressionLevel.NoCompression);
                var bytes = File.ReadAllBytes(zip);
                bytes[200000] ^= 0xFF; // Um byte trocado no meio do ISO armazenado.
                File.WriteAllBytes(zip, bytes);
                var game = new CloudLibrary(cloud.Combine("jogos")).Find("ps2/Jogo.zip");

                var result = new GameInstaller().Install(game, roms.Path);

                Assert.Equal(InstallOutcome.Failed, result.Outcome);
                Assert.Contains("CRC-32", result.Error);
                Assert.Empty(Directory.GetFiles(roms.Combine("ps2")));
            }
        }

        [Fact]
        public void Overlay_wraps_the_command_of_systems_with_cloud_games_only()
        {
            var systems = new[]
            {
                new SystemFolder("ps2", "ps2", "\"%HOME%\\emulatorLauncher.exe\" -system %SYSTEM% -rom %ROM%"),
                new SystemFolder("snes", "snes", "\"%HOME%\\emulatorLauncher.exe\" -system %SYSTEM% -rom %ROM%")
            };
            var wrapper = new CommandWrapper(@"%HOME%\..\retroboat\retroboat-launch.exe", new[] { "ps2" });

            var overlay = XDocument.Parse(IsolatedLibrary.BuildOverlay(systems, @"D:\Jogos", includePaths: false, wrapper));

            var system = Assert.Single(overlay.Root.Elements("system"));
            Assert.Equal("ps2", (string)system.Element("name"));
            Assert.Null(system.Element("path"));
            Assert.Equal("\"%HOME%\\..\\retroboat\\retroboat-launch.exe\" \"%HOME%\\emulatorLauncher.exe\" -system %SYSTEM% -rom %ROM%", (string)system.Element("command"));
            Assert.Null(IsolatedLibrary.BuildOverlay(systems, @"D:\Jogos", includePaths: false, wrapper: null));
        }

        [Fact]
        public void Command_line_keeps_the_target_arguments_exactly()
        {
            var line = "\"C:\\RetroBat\\retroboat\\retroboat-launch.exe\" \"C:\\RetroBat\\emulationstation\\emulatorLauncher.exe\" -system ps2 -rom \"D:\\Jogos\\ps2\\Jogo (USA).iso\"";

            CommandLine.SplitFirst(line, out var rest);
            var target = CommandLine.SplitFirst(rest, out var arguments);

            Assert.Equal(@"C:\RetroBat\emulationstation\emulatorLauncher.exe", target);
            Assert.Equal("-system ps2 -rom \"D:\\Jogos\\ps2\\Jogo (USA).iso\"", arguments);
            Assert.Equal(@"D:\Jogos\ps2\Jogo (USA).iso", CommandLine.GetOption(new[] { "x", "-system", "ps2", "-rom", @"D:\Jogos\ps2\Jogo (USA).iso" }, "-rom"));
        }

        [Fact]
        public void Cloud_games_appear_as_placeholders_without_a_status_in_the_description()
        {
            using (var cloud = new TempDir())
            using (var retrobat = new TempDir())
            {
                WriteZip(cloud, @"jogos\ps2\Jogo.zip", IsoName, FakeIso());
                cloud.WriteFile(@"jogos\ps2\Outro (USA).iso", FakeIso(1 << 20));
                var install = new RetroBatInstall(retrobat.Path);
                var roms = retrobat.Combine("roms");

                var result = new CloudLibrarySync(install, roms, new CloudLibrary(cloud.Combine("jogos"))).Apply();

                Assert.Equal(2, result.InCloud);
                Assert.Equal(0, new FileInfo(Path.Combine(roms, "ps2", IsoName)).Length);
                Assert.Equal(0, new FileInfo(Path.Combine(roms, "ps2", "Outro (USA).iso")).Length);
                Assert.Contains("ps2", result.SystemFolders);

                var gamelist = XDocument.Load(Path.Combine(install.GamelistsPath, "ps2", "gamelist.xml"));
                var game = gamelist.Root.Elements("game").Single(g => (string)g.Element("path") == "./" + IsoName);
                Assert.Null(game.Element("desc")); // O status fica no tema; a descrição é só do jogo, como na Steam.
                Assert.Equal("Jogo (Europe) (En,Fr)", (string)game.Element("name"));
            }
        }

        [Fact]
        public void Installed_game_loses_the_cloud_description_and_removed_game_loses_its_placeholder()
        {
            using (var cloud = new TempDir())
            using (var retrobat = new TempDir())
            {
                cloud.WriteFile(@"jogos\ps2\A.iso", FakeIso(1 << 20));
                var removed = cloud.WriteFile(@"jogos\ps2\B.iso", FakeIso(1 << 20));
                var install = new RetroBatInstall(retrobat.Path);
                var roms = retrobat.Combine("roms");
                var library = new CloudLibrary(cloud.Combine("jogos"));
                new CloudLibrarySync(install, roms, library).Apply();

                File.WriteAllBytes(Path.Combine(roms, "ps2", "A.iso"), FakeIso(1 << 20));
                File.Delete(removed);
                var result = new CloudLibrarySync(install, roms, library).Apply();

                Assert.Equal(1, result.Installed);
                Assert.Equal(new[] { "ps2/B.iso" }, result.PlaceholdersRemoved);
                Assert.False(File.Exists(Path.Combine(roms, "ps2", "B.iso")));
                var gamelist = XDocument.Load(Path.Combine(install.GamelistsPath, "ps2", "gamelist.xml"));
                var a = gamelist.Root.Elements("game").Single(g => (string)g.Element("path") == "./A.iso");
                Assert.Null(a.Element("desc"));
            }
        }

        [Fact]
        public void Game_replaced_by_a_zip_of_the_same_disc_keeps_its_placeholder()
        {
            using (var cloud = new TempDir())
            using (var retrobat = new TempDir())
            {
                var iso = cloud.WriteFile(@"jogos\ps2\" + IsoName, FakeIso(1 << 20));
                var install = new RetroBatInstall(retrobat.Path);
                var roms = retrobat.Combine("roms");
                var library = new CloudLibrary(cloud.Combine("jogos"));
                new CloudLibrarySync(install, roms, library).Apply();

                File.Delete(iso);
                WriteZip(cloud, @"jogos\ps2\Jogo.zip", IsoName, FakeIso());
                var result = new CloudLibrarySync(install, roms, library).Apply();

                Assert.Empty(result.PlaceholdersRemoved);
                Assert.True(File.Exists(Path.Combine(roms, "ps2", IsoName)));
                Assert.Equal(1, result.InCloud);
            }
        }

        [Fact]
        public void A_cloud_game_keeps_its_description_and_loses_the_old_status_line()
        {
            using (var retrobat = new TempDir())
            {
                var path = retrobat.WriteText(@"gamelist.xml", "<gameList><game><path>./A.iso</path><desc>Minha descrição</desc></game>" +
                    "<game><path>./B.iso</path><desc>Na nuvem · 1,0 KB · baixa na primeira vez que você jogar.\n\nOutra descrição</desc></game></gameList>");

                CloudLibrarySync.UpdateGamelist(path, new[]
                {
                    new CloudIndexEntry { LocalName = "A.iso", State = CloudGameState.Cloud, CloudSize = 1000 },
                    new CloudIndexEntry { LocalName = "B.iso", State = CloudGameState.Cloud, CloudSize = 1000 }
                });

                var games = XDocument.Load(path).Root.Elements("game").ToList();
                Assert.Equal("Minha descrição", (string)games[0].Element("desc"));
                Assert.Equal("Outra descrição", (string)games[1].Element("desc"));
            }
        }

        [Fact]
        public void On_demand_install_replaces_the_placeholder_with_the_game()
        {
            using (var cloud = new TempDir())
            using (var retrobat = new TempDir())
            {
                var content = FakeIso();
                WriteZip(cloud, @"jogos\ps2\Jogo.zip", IsoName, content);
                var install = new RetroBatInstall(retrobat.Path);
                var roms = retrobat.Combine("roms");
                new RetroboatSettings { RomsPath = roms, CloudPath = cloud.Path }.Save(RetroboatSettings.GetPath(install));
                new CloudLibrarySync(install, roms, new CloudLibrary(cloud.Combine("jogos"))).Apply();
                var placeholder = Path.Combine(roms, "ps2", IsoName);

                var installer = new OnDemandInstaller(install);
                var entry = installer.FindPending(placeholder);
                var result = installer.Install(entry, null, CancellationToken.None);

                Assert.Equal(OnDemandOutcome.Installed, result.Outcome);
                Assert.Equal(content, File.ReadAllBytes(placeholder));
                Assert.Null(new OnDemandInstaller(install).FindPending(placeholder));
            }
        }

        [Fact]
        public void On_demand_install_of_a_zeroed_game_marks_it_corrupt()
        {
            using (var cloud = new TempDir())
            using (var retrobat = new TempDir())
            {
                cloud.WriteFile(@"jogos\ps2\Zerado.iso", new byte[1 << 20]);
                var install = new RetroBatInstall(retrobat.Path);
                var roms = retrobat.Combine("roms");
                new RetroboatSettings { RomsPath = roms, CloudPath = cloud.Path }.Save(RetroboatSettings.GetPath(install));
                new CloudLibrarySync(install, roms, new CloudLibrary(cloud.Combine("jogos"))).Apply();

                var installer = new OnDemandInstaller(install);
                var result = installer.Install(installer.FindPending(Path.Combine(roms, "ps2", "Zerado.iso")), null, CancellationToken.None);

                Assert.Equal(OnDemandOutcome.Corrupt, result.Outcome);
                Assert.Contains("zerado", result.Message);
                Assert.Equal(CloudGameState.Corrupt, CloudIndex.Load(CloudLibrarySync.IndexPath(install)).Entries.Single().State);
                Assert.Equal(0, new FileInfo(Path.Combine(roms, "ps2", "Zerado.iso")).Length);

                // Na próxima abertura, o ES mostra o jogo como corrompido.
                new CloudLibrarySync(install, roms, new CloudLibrary(cloud.Combine("jogos"))).Apply();
                var gamelist = File.ReadAllText(Path.Combine(install.GamelistsPath, "ps2", "gamelist.xml"));
                Assert.Contains(CloudLibrarySync.CorruptDescriptionPrefix, gamelist);
            }
        }
    }
}
