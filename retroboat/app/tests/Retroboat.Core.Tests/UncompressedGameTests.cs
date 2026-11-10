using System.IO;
using System.Threading;
using Retroboat.Library;
using Retroboat.Settings;
using Xunit;

namespace Retroboat.Tests
{
    public class UncompressedGameTests
    {
        const string Rom = @"C:\Jogos\psx\Crash Bandicoot (USA).zip";

        static RetroBatInstall CreateInstall(TempDir dir, string esSettings = "<config />", string roms = null)
        {
            dir.WriteFile(@"RetroBat\retrobat.exe", new byte[1]);
            dir.WriteText(@"RetroBat\emulationstation\.emulationstation\es_settings.cfg", esSettings);
            dir.WriteText(@"RetroBat\emulationstation\emulatorLauncher.cfg", "home=.\\.emulationstation\r\n" + (roms != null ? "roms=" + roms + "\r\n" : ""));
            var install = new RetroBatInstall(dir.Combine("RetroBat"));
            new RetroboatSettings { RomsPath = dir.Combine("Jogos") }.Save(RetroboatSettings.GetPath(install));
            return install;
        }

        static RetroboatSettings Settings(RetroBatInstall install) => RetroboatSettings.Load(RetroboatSettings.GetPath(install));

        [Fact]
        public void The_answer_is_kept_in_the_pc_settings()
        {
            using (var dir = new TempDir())
            {
                var path = dir.Combine("retroboat.ini");
                new RetroboatSettings().Save(path);
                Assert.Null(RetroboatSettings.Load(path).KeepUncompressed);
                Assert.Contains("KeepUncompressed=\r\n", File.ReadAllText(path));

                new RetroboatSettings { KeepUncompressed = false }.Save(path);
                Assert.False(RetroboatSettings.Load(path).KeepUncompressed);

                new RetroboatSettings { KeepUncompressed = true }.Save(path);
                Assert.True(RetroboatSettings.Load(path).KeepUncompressed);
            }
        }

        [Fact]
        public void The_folder_is_where_the_emulator_launcher_extracts()
        {
            using (var dir = new TempDir())
            {
                var install = CreateInstall(dir, roms: dir.Combine("Jogos"));
                var game = UncompressedGame.For(install, Settings(install), "psx", Rom);
                Assert.Equal(dir.Combine("Jogos", ".uncompressed", "psx", "Crash Bandicoot (USA).zip"), game.Folder);

                // Sem roms= no emulatorLauncher.cfg: a pasta roms do RetroBat.
                install = CreateInstall(dir);
                Assert.Equal(Path.Combine(install.DefaultRomsPath, ".uncompressed", "psx", "Crash Bandicoot (USA).zip"), UncompressedGame.For(install, Settings(install), "psx", Rom).Folder);

                // Com a pasta de descompressão do ES.
                install = CreateInstall(dir, "<config><string name=\"global.decompressedpath\" value=\"D:/Temp\" /></config>");
                Assert.Equal(@"D:\Temp\.uncompressed\psx\Crash Bandicoot (USA).zip", UncompressedGame.For(install, Settings(install), "psx", Rom).Folder);
            }
        }

        [Fact]
        public void A_remembered_answer_goes_to_the_emulator_launcher()
        {
            using (var dir = new TempDir())
            {
                var install = CreateInstall(dir);
                var settings = Settings(install);

                var first = UncompressedGame.For(install, settings, "psx", Rom);
                Assert.Null(first.Argument);
                Assert.True(first.Learns);

                settings.KeepUncompressed = false;
                var game = UncompressedGame.For(install, settings, "psx", Rom);
                Assert.Equal("-decompressedfolders delete", game.Argument);
                Assert.False(game.Learns);

                settings.KeepUncompressed = true;
                Assert.Equal("-decompressedfolders keep", UncompressedGame.For(install, settings, "psx", Rom).Argument);
            }
        }

        [Fact]
        public void The_es_option_wins_at_any_level()
        {
            foreach (var key in new[] { "global.decompressedfolders", "psx.decompressedfolders", "psx[\"Crash Bandicoot (USA).zip\"].decompressedfolders" })
            {
                using (var dir = new TempDir())
                {
                    var install = CreateInstall(dir, "<config><string name='" + key + "' value='ask' /></config>");
                    var settings = Settings(install);
                    settings.KeepUncompressed = false;

                    var game = UncompressedGame.For(install, settings, "psx", Rom);

                    Assert.Null(game.Argument);
                    Assert.False(game.Learns);
                }
            }
        }

        [Fact]
        public void Keeping_or_deleting_the_folder_is_the_answer()
        {
            foreach (var keep in new[] { true, false })
            {
                using (var dir = new TempDir())
                {
                    var install = CreateInstall(dir, roms: dir.Combine("Jogos"));
                    var game = UncompressedGame.For(install, Settings(install), "psx", Rom);

                    int code = game.Around(() =>
                    {
                        // O emulatorLauncher descompacta, o jogo roda e, depois da pergunta, a pasta fica ou some.
                        dir.WriteFile(Path.Combine(game.Folder, "Crash Bandicoot (USA).cue"), new byte[] { 1 });
                        Thread.Sleep(1500);
                        if (!keep)
                            Directory.Delete(game.Folder, true);
                        return 7;
                    }, out var learned);

                    Assert.Equal(7, code);
                    Assert.Equal(keep, learned);
                    Assert.Equal(keep, Settings(install).KeepUncompressed);
                    Assert.Equal(dir.Combine("Jogos"), Settings(install).RomsPath); // o resto do retroboat.ini fica
                }
            }
        }

        [Fact]
        public void A_game_that_was_not_extracted_teaches_nothing()
        {
            using (var dir = new TempDir())
            {
                var install = CreateInstall(dir, roms: dir.Combine("Jogos"));
                var game = UncompressedGame.For(install, Settings(install), "psx", Rom);

                game.Around(() => 0, out var learned);

                Assert.Null(learned);
                Assert.Null(Settings(install).KeepUncompressed);

                // Pasta que já existia: o emulatorLauncher a usa sem perguntar.
                dir.WriteFile(Path.Combine(game.Folder, "Crash Bandicoot (USA).cue"), new byte[] { 1 });
                Assert.False(UncompressedGame.For(install, Settings(install), "psx", Rom).Learns);
            }
        }
    }
}
