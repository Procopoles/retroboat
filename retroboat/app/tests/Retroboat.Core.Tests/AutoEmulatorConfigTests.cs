using System;
using System.IO;
using System.Linq;
using System.Text;
using Retroboat.Config;
using Retroboat.Library;
using Xunit;

namespace Retroboat.Tests
{
    /// <summary>A regra genérica: qualquer emulador instalado, sem regra própria.</summary>
    public class AutoEmulatorConfigTests
    {
        static RetroBatInstall Pc(TempDir pcs, string name)
        {
            var install = new RetroBatInstall(pcs.Combine(name));
            Directory.CreateDirectory(install.UserEmulationStationPath);
            return install;
        }

        static string Emu(RetroBatInstall install, string folder, string relative = null) =>
            relative == null ? Path.Combine(install.Root, "emulators", folder) : Path.Combine(install.Root, "emulators", folder, relative);

        /// <summary>Um emulador baixado: o .exe na pasta, como o RetroBat deixa.</summary>
        static void Install(RetroBatInstall install, string folder, string exe)
        {
            Directory.CreateDirectory(Emu(install, folder));
            File.WriteAllBytes(Emu(install, folder, exe), new byte[] { 0x4D, 0x5A });
        }

        static void Write(RetroBatInstall install, string folder, string relative, string text)
        {
            var path = Emu(install, folder, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, text.Replace("\n", "\r\n"));
        }

        static string Read(RetroBatInstall install, string folder, string relative) => File.ReadAllText(Emu(install, folder, relative));

        static bool Exists(RetroBatInstall install, string folder, string relative) => File.Exists(Emu(install, folder, relative));

        static ConfigSyncResult Sync(RetroBatInstall install, TempDir cloud, string pc, string folder, ConfigSyncMode mode = ConfigSyncMode.Full) =>
            new ConfigSync(new AutoEmulatorConfigSource(install, folder), install.RetroboatDataPath, cloud.Path, pc).Sync(mode);

        static string Cloud(TempDir cloud, string folder, string pc) => File.ReadAllText(cloud.Combine(@"configuracoes\" + folder + @"\" + pc + ".xml"));

        [Fact]
        public void DuckStation_settings_and_per_game_settings_travel_but_hardware_paths_and_login_stay()
        {
            using (var cloud = new TempDir())
            using (var pcs = new TempDir())
            {
                var a = Pc(pcs, "A");
                var b = Pc(pcs, "B");
                Install(a, "duckstation", "duckstation-qt-x64-ReleaseLTCG.exe");
                Install(b, "duckstation", "duckstation-qt-x64-ReleaseLTCG.exe");
                Write(a, "duckstation", "settings.ini",
                    "[Main]\nSettingsVersion = 3\nEmulationSpeed = 1.5\n\n" +
                    "[GPU]\nRenderer = Vulkan\nResolutionScale = 4\nAdapter = NVIDIA GeForce RTX 4070\n\n" +
                    "[Audio]\nBackend = Cubeb\nOutputDevice = Alto-falantes (Realtek)\n\n" +
                    "[BIOS]\nSearchDirectory = C:\\RetroBat\\bios\n\n" +
                    "[UI]\nMainWindowGeometry = AdnQywADAAA\n\n" +
                    "[Cheevos]\nUsername = jogador\nToken = abc123\nChallengeMode = true\n");
                Write(a, "duckstation", "gamesettings/SLUS-00594.ini", "[GPU]\nResolutionScale = 8\n");
                Write(b, "duckstation", "settings.ini", "[Main]\nSettingsVersion = 3\n\n[GPU]\nAdapter = AMD Radeon\n\n[BIOS]\nSearchDirectory = D:\\Outro\\bios\n");

                var sent = Sync(a, cloud, "PC-A", "duckstation");
                Assert.Empty(sent.Errors);
                var uploaded = Cloud(cloud, "duckstation", "PC-A");
                Assert.Contains("ini|settings.ini@3|GPU|ResolutionScale", uploaded);
                Assert.Contains("ini|gamesettings/SLUS-00594.ini|GPU|ResolutionScale", uploaded);
                Assert.Contains("ChallengeMode", uploaded);
                foreach (var local in new[] { "NVIDIA", "Realtek", "RetroBat", "AdnQywADAAA", "jogador", "abc123", "SettingsVersion" })
                    Assert.DoesNotContain(local, uploaded);

                Sync(b, cloud, "PC-B", "duckstation");
                var ini = Read(b, "duckstation", "settings.ini");
                Assert.Contains("ResolutionScale = 4", ini);
                Assert.Contains("Renderer = Vulkan", ini);
                Assert.Contains("EmulationSpeed = 1.5", ini);
                Assert.Contains("ChallengeMode = true", ini);
                Assert.Contains("Adapter = AMD Radeon", ini);
                Assert.Contains(@"SearchDirectory = D:\Outro\bios", ini);
                Assert.DoesNotContain("Token", ini);
                Assert.Contains("ResolutionScale = 8", Read(b, "duckstation", @"gamesettings\SLUS-00594.ini"));

                Assert.False(Sync(a, cloud, "PC-A", "duckstation").HasChanges);
                Assert.False(Sync(b, cloud, "PC-B", "duckstation").HasChanges);
            }
        }

        [Fact]
        public void RetroArch_options_per_core_and_game_and_remaps_travel_but_shipped_folders_do_not()
        {
            using (var cloud = new TempDir())
            using (var pcs = new TempDir())
            {
                var a = Pc(pcs, "A");
                var b = Pc(pcs, "B");
                Install(a, "retroarch", "retroarch.exe");
                Install(b, "retroarch", "retroarch.exe");
                Write(a, "retroarch", "retroarch.cfg",
                    "video_shader_enable = \"true\"\nmenu_driver = \"ozone\"\nvideo_monitor_index = \"1\"\n" +
                    "savefile_directory = \"C:\\RetroBat\\saves\"\ncheevos_password = \"segredo\"\ncheevos_token = \"tok\"\n");
                Write(a, "retroarch", "retroarch-core-options.cfg", "beetle_psx_hw_internal_resolution = \"4x\"\n");
                Write(a, "retroarch", "config/Flycast/Crazy Taxi.opt", "reicast_internal_resolution = \"1920x1440\"\n");
                Write(a, "retroarch", "config/remaps/Beetle PSX HW/Game.rmp", "input_player1_btn_a = \"8\"\n");
                Write(a, "retroarch", "autoconfig/xinput/XInput Controller.cfg", "input_device = \"XInput Controller\"\n");
                Write(a, "retroarch", "overlays/borders/gb.cfg", "overlays = 1\n");
                Write(b, "retroarch", "retroarch.cfg", "video_monitor_index = \"0\"\nsavefile_directory = \"D:\\RetroBat\\saves\"\n");

                Sync(a, cloud, "PC-A", "retroarch");
                var uploaded = Cloud(cloud, "retroarch", "PC-A");
                Assert.Contains("config/Flycast/Crazy Taxi.opt", uploaded);
                Assert.DoesNotContain("autoconfig", uploaded);
                Assert.DoesNotContain("overlays", uploaded);
                foreach (var local in new[] { "segredo", "tok", "RetroBat", "video_monitor_index" })
                    Assert.DoesNotContain(local, uploaded);

                Sync(b, cloud, "PC-B", "retroarch");
                var cfg = Read(b, "retroarch", "retroarch.cfg");
                Assert.Contains("video_shader_enable = \"true\"", cfg);
                Assert.DoesNotContain("menu_driver", cfg); // Todo "driver" fica no PC, o do menu também.
                Assert.Contains("video_monitor_index = \"0\"", cfg);
                Assert.Contains(@"D:\RetroBat\saves", cfg);
                Assert.Contains("1920x1440", Read(b, "retroarch", @"config\Flycast\Crazy Taxi.opt"));
                Assert.Contains("input_player1_btn_a", Read(b, "retroarch", @"config\remaps\Beetle PSX HW\Game.rmp"));
                Assert.Contains("4x", Read(b, "retroarch", "retroarch-core-options.cfg"));
                Assert.False(Exists(b, "retroarch", @"autoconfig\xinput\XInput Controller.cfg"));
            }
        }

        [Fact]
        public void Dolphin_per_game_settings_keep_cheat_lines_and_system_folders_stay()
        {
            using (var cloud = new TempDir())
            using (var pcs = new TempDir())
            {
                var a = Pc(pcs, "A");
                var b = Pc(pcs, "B");
                Install(a, "dolphin-emu", "Dolphin.exe");
                Install(b, "dolphin-emu", "Dolphin.exe");
                Write(a, "dolphin-emu", "User/Config/GFX.ini", "[Settings]\nInternalResolution = 3\nMSAA = 4\n");
                Write(a, "dolphin-emu", "User/Config/Qt.ini", "[mainwindow]\ngeometry = @ByteArray(abc)\n[General]\nLanguage = pt\n");
                Write(a, "dolphin-emu", "User/GameSettings/GALE01.ini",
                    "[OnFrame]\n$Infinite Health\n0x80001234:dword:0x60000000\n[OnFrame_Enabled]\n$Infinite Health\n[Video_Settings]\nSafeTextureCacheColorSamples = 512\n");
                Write(a, "dolphin-emu", "Sys/GameSettings/GALE01.ini", "[Core]\nCPUThread = False\n");
                Write(a, "dolphin-emu", "User/Wii/shared2/setting.xml", "<x/>");

                Sync(a, cloud, "PC-A", "dolphin-emu");
                Assert.DoesNotContain("Sys/", Cloud(cloud, "dolphin-emu", "PC-A"));
                Assert.DoesNotContain("Wii", Cloud(cloud, "dolphin-emu", "PC-A"));

                Sync(b, cloud, "PC-B", "dolphin-emu");
                Assert.Contains("InternalResolution = 3", Read(b, "dolphin-emu", @"User\Config\GFX.ini"));
                var qt = Read(b, "dolphin-emu", @"User\Config\Qt.ini");
                Assert.Contains("Language = pt", qt);
                Assert.DoesNotContain("ByteArray", qt);
                var game = Read(b, "dolphin-emu", @"User\GameSettings\GALE01.ini");
                Assert.Contains("[OnFrame]\r\n$Infinite Health\r\n0x80001234:dword:0x60000000", game);
                Assert.Contains("[OnFrame_Enabled]\r\n$Infinite Health", game);
                Assert.Contains("SafeTextureCacheColorSamples = 512", game);
                Assert.False(Exists(b, "dolphin-emu", @"Sys\GameSettings\GALE01.ini"));

                // A mudança nas linhas de cheat vai como uma entrada só.
                Write(a, "dolphin-emu", "User/GameSettings/GALE01.ini",
                    "[OnFrame]\n$Infinite Health\n0x80001234:dword:0x60000000\n$Moon Jump\n0x80005678:dword:0x1\n[OnFrame_Enabled]\n$Infinite Health\n[Video_Settings]\nSafeTextureCacheColorSamples = 512\n");
                Assert.Single(Sync(a, cloud, "PC-A", "dolphin-emu").Sent);
                Sync(b, cloud, "PC-B", "dolphin-emu");
                Assert.Contains("$Moon Jump", Read(b, "dolphin-emu", @"User\GameSettings\GALE01.ini"));
            }
        }

        [Fact]
        public void Whole_files_travel_only_without_paths_or_login()
        {
            using (var cloud = new TempDir())
            using (var pcs = new TempDir())
            {
                var a = Pc(pcs, "A");
                var b = Pc(pcs, "B");
                Install(a, "mesen", "Mesen.exe");
                Install(b, "mesen", "Mesen.exe");
                Write(a, "mesen", "settings.json", "{ \"Video\": { \"Scale\": 3 }, \"Url\": \"https://example.org/x\" }");
                Write(a, "mesen", "paths.json", "{ \"Folder\": \"C:\\\\RetroBat\\\\roms\" }");
                Write(a, "mesen", "net.json", "{ \"Server\": \"\\\\\\\\nas\\\\share\" }");
                Write(a, "mesen", "account.json", "{ \"Password\": \"x\" }");

                Sync(a, cloud, "PC-A", "mesen");
                Sync(b, cloud, "PC-B", "mesen");

                Assert.Contains("\"Scale\": 3", Read(b, "mesen", "settings.json"));
                Assert.False(Exists(b, "mesen", "paths.json"));
                Assert.False(Exists(b, "mesen", "net.json"));
                Assert.False(Exists(b, "mesen", "account.json"));
            }
        }

        [Fact]
        public void A_different_format_version_keeps_the_pcs_apart_and_a_missing_versioned_file_waits_for_the_emulator()
        {
            using (var cloud = new TempDir())
            using (var pcs = new TempDir())
            {
                var a = Pc(pcs, "A");
                var b = Pc(pcs, "B");
                var c = Pc(pcs, "C");
                foreach (var pc in new[] { a, b, c })
                    Install(pc, "duckstation", "duckstation.exe");
                Write(a, "duckstation", "settings.ini", "[Main]\nSettingsVersion = 3\n[GPU]\nResolutionScale = 4\n");
                Write(a, "duckstation", "notes.ini", "[Main]\nFoo = 1\n");
                Write(b, "duckstation", "settings.ini", "[Main]\nSettingsVersion = 4\n[GPU]\nResolutionScale = 1\n");

                Sync(a, cloud, "PC-A", "duckstation");
                Sync(b, cloud, "PC-B", "duckstation");
                Sync(c, cloud, "PC-C", "duckstation");

                Assert.Contains("ResolutionScale = 1", Read(b, "duckstation", "settings.ini"));
                Assert.Contains("SettingsVersion = 4", Read(b, "duckstation", "settings.ini"));
                Assert.Contains("Foo = 1", Read(b, "duckstation", "notes.ini"));

                // C ainda não tem o settings.ini: sem saber a versão do emulador dele, espera o emulador criar.
                Assert.False(Exists(c, "duckstation", "settings.ini"));
                Assert.Contains("Foo = 1", Read(c, "duckstation", "notes.ini"));

                Write(c, "duckstation", "settings.ini", "[Main]\nSettingsVersion = 3\n");
                Sync(c, cloud, "PC-C", "duckstation");
                Assert.Contains("ResolutionScale = 4", Read(c, "duckstation", "settings.ini"));
            }
        }

        [Fact]
        public void An_emulator_not_downloaded_yet_sends_nothing_and_gets_everything_after_it_is_installed()
        {
            using (var cloud = new TempDir())
            using (var pcs = new TempDir())
            {
                var a = Pc(pcs, "A");
                var b = Pc(pcs, "B");
                Install(a, "flycast", "flycast.exe");
                Write(a, "flycast", "emu.cfg", "[config]\nrend.Resolution = 1440\npvr.rend = 4\n");
                Write(b, "flycast", "emu.cfg", "[config]\npvr.rend = 0\n"); // Modelo do RetroBat, sem o emulador.

                Sync(a, cloud, "PC-A", "flycast");
                Assert.True(Sync(b, cloud, "PC-B", "flycast").Skipped);
                Assert.DoesNotContain("PC-B", string.Join(",", Directory.GetFiles(cloud.Combine(@"configuracoes\flycast"))));

                Install(b, "flycast", "flycast.exe");
                Write(b, "flycast", "emu.cfg", "[config]\npvr.rend = 0\n"); // O emulador acabou de gravar o padrão.
                Sync(b, cloud, "PC-B", "flycast");
                var cfg = Read(b, "flycast", "emu.cfg");
                Assert.Contains("rend.Resolution = 1440", cfg);
                Assert.Contains("pvr.rend = 4", cfg);
            }
        }

        [Fact]
        public void Deleting_a_per_game_file_deletes_it_elsewhere_but_a_deleted_main_file_comes_back()
        {
            using (var cloud = new TempDir())
            using (var pcs = new TempDir())
            {
                var a = Pc(pcs, "A");
                var b = Pc(pcs, "B");
                Install(a, "retroarch", "retroarch.exe");
                Install(b, "retroarch", "retroarch.exe");
                Write(a, "retroarch", "retroarch.cfg", "video_shader_enable = \"true\"\n");
                Write(a, "retroarch", "config/Flycast/Crazy Taxi.opt", "reicast_internal_resolution = \"1920x1440\"\n");
                Write(a, "retroarch", "config/Flycast/Other.opt", "reicast_internal_resolution = \"640x480\"\n");
                Sync(a, cloud, "PC-A", "retroarch");
                Sync(b, cloud, "PC-B", "retroarch");

                File.Delete(Emu(a, "retroarch", @"config\Flycast\Crazy Taxi.opt"));
                File.Delete(Emu(a, "retroarch", "retroarch.cfg"));
                Sync(a, cloud, "PC-A", "retroarch");
                Sync(b, cloud, "PC-B", "retroarch");

                Assert.False(Exists(b, "retroarch", @"config\Flycast\Crazy Taxi.opt"));
                Assert.True(Exists(b, "retroarch", @"config\Flycast\Other.opt"));
                Assert.Contains("video_shader_enable", Read(b, "retroarch", "retroarch.cfg"));
                Assert.Contains("video_shader_enable", Read(a, "retroarch", "retroarch.cfg"));
            }
        }

        [Fact]
        public void Nothing_from_the_cloud_leaves_the_emulator_folder_or_lands_in_system_folders()
        {
            using (var pcs = new TempDir())
            {
                var a = Pc(pcs, "A");
                Install(a, "retroarch", "retroarch.exe");
                var source = new AutoEmulatorConfigSource(a, "retroarch");

                Assert.True(source.IsPortable("ini|retroarch.cfg||video_shader_enable", "\"true\""));
                Assert.True(source.IsPortable("file|config/x.json", Convert.ToBase64String(Encoding.UTF8.GetBytes("{}"))));
                Assert.False(source.IsPortable("ini|../retroarch.cfg||a", "1"));
                Assert.False(source.IsPortable("ini|config/../../x.cfg||a", "1"));
                Assert.False(source.IsPortable(@"ini|config\x.cfg||a", "1"));
                Assert.False(source.IsPortable("ini|C:/x.cfg||a", "1"));
                Assert.False(source.IsPortable("ini|bios/x.cfg||a", "1"));
                Assert.False(source.IsPortable("ini|User/Wii/x.ini|a|b", "1"));
                Assert.False(source.IsPortable("file|retroarch.exe", "AA=="));
                Assert.False(source.IsPortable("file|x.dll", "AA=="));
                Assert.False(source.IsPortable("file|x.cfg", "AA=="));          // .cfg vai chave a chave
                Assert.False(source.IsPortable("ini|x.cfg||a=b", "1"));
                Assert.False(source.IsPortable("ini|x.cfg|a]b|c", "1"));
                Assert.False(source.IsPortable("ini|x.cfg||cheevos_password", "1"));
                Assert.False(source.IsPortable("ini|x.cfg||a", @"C:\RetroBat"));
                Assert.False(source.IsPortable("ini|x.cfg@3||a", "1"));      // versão diferente da daqui (sem arquivo)
                Assert.False(source.IsPortable("file|x.json", "não é base64"));
            }
        }

        [Fact]
        public void The_launched_emulator_picks_its_folder()
        {
            using (var pcs = new TempDir())
            {
                var a = Pc(pcs, "A");
                foreach (var folder in new[] { "retroarch", "dolphin-emu", "dolphin-triforce", "duckstation", "pcsx2", "pcsx2-16" })
                    Directory.CreateDirectory(Emu(a, folder));

                string Names(string emulator) => string.Join(",", CloudConfigs.EmulatorSources(a, emulator).Select(s => s.Folder));

                Assert.Equal("retroarch", Names("libretro"));
                Assert.Equal("dolphin-emu,dolphin-triforce", Names("dolphin"));
                Assert.Equal("pcsx2,pcsx2-16", Names("pcsx2"));
                Assert.Equal(6, CloudConfigs.EmulatorSources(a, "desconhecido").Count);
                Assert.Equal(6, CloudConfigs.EmulatorSources(a, null).Count);

                // O PCSX2 tem regra própria; o pcsx2-16 vai pela genérica.
                Assert.IsType<EmulatorConfigSource>(CloudConfigs.EmulatorSources(a).Single(s => s.Folder == "pcsx2"));
                Assert.IsType<AutoEmulatorConfigSource>(CloudConfigs.EmulatorSources(a).Single(s => s.Folder == "pcsx2-16"));
            }
        }

        [Fact]
        public void A_folder_with_too_many_files_is_not_synced()
        {
            using (var cloud = new TempDir())
            using (var pcs = new TempDir())
            {
                var a = Pc(pcs, "A");
                Install(a, "mame", "mame.exe");
                Directory.CreateDirectory(Emu(a, "mame", "listas"));
                for (int i = 0; i <= AutoEmulatorRules.MaxFiles; i++)
                    File.WriteAllText(Emu(a, "mame", @"listas\" + i + ".xml"), "<x/>");

                // CloudConfigs transforma a exceção em aviso; a biblioteca, os saves e o jogo seguem.
                Assert.Throws<InvalidOperationException>(() => Sync(a, cloud, "PC-A", "mame"));
                Assert.False(Directory.Exists(cloud.Combine(@"configuracoesmame")) && Directory.GetFiles(cloud.Combine(@"configuracoesmame")).Length > 0);
            }
        }

        [Fact]
        public void Ini_lines_that_are_not_keys_are_kept_and_replaced_per_section()
        {
            var document = IniDocument.Parse("; topo\n[autoexec]\nmount c .\nc:\n[cpu]\ncycles = max\n");

            var raw = document.RawLines();
            Assert.Equal("autoexec", Assert.Single(raw).Key);
            Assert.Equal(new[] { "mount c .", "c:" }, raw[0].Value);

            document.SetLines("autoexec", new[] { "mount d ." });
            document.SetLines("novo", new[] { "linha" });
            Assert.Equal("; topo\n[autoexec]\nmount d .\n[cpu]\ncycles = max\n\n[novo]\nlinha\n", Encoding.UTF8.GetString(document.ToBytes()));

            document.SetLines("autoexec", null);
            Assert.DoesNotContain("mount", Encoding.UTF8.GetString(document.ToBytes()));
        }

        [Fact]
        public void The_launcher_wraps_every_system_when_no_list_is_given()
        {
            var wrapper = new CommandWrapper(@"%HOME%\..\retroboat\retroboat-launch.exe");
            var command = wrapper.Wrap(new SystemFolder("snes", "snes", "\"%HOME%\\emulatorLauncher.exe\" -system %SYSTEM% -rom %ROM%"));

            Assert.StartsWith("\"%HOME%\\..\\retroboat\\retroboat-launch.exe\" \"%HOME%\\emulatorLauncher.exe\"", command);
            Assert.Null(wrapper.Wrap(new SystemFolder("x", "x", "")));
        }
    }
}
