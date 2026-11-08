using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Retroboat.Accounts;
using Retroboat.Config;
using Retroboat.Library;
using Xunit;

namespace Retroboat.Tests
{
    public class ConfigSyncTests
    {
        const string Game = "ps2[\"Shadow of the Colossus (USA).iso\"].internal_resolution";

        static RetroBatInstall Pc(TempDir pcs, string name)
        {
            var install = new RetroBatInstall(pcs.Combine(name));
            Directory.CreateDirectory(install.UserEmulationStationPath);
            return install;
        }

        static string SettingsPath(RetroBatInstall install) => Path.Combine(install.UserEmulationStationPath, "es_settings.cfg");

        static void WriteSettings(RetroBatInstall install, params (string Type, string Name, string Value)[] entries)
        {
            var root = new XElement("config", entries.Select(e => new XElement(e.Type, new XAttribute("name", e.Name), new XAttribute("value", e.Value))));
            File.WriteAllText(SettingsPath(install), "<?xml version=\"1.0\"?>\n" + root + "\n");
        }

        static string Get(RetroBatInstall install, string name) => EsSettings.Load(SettingsPath(install)).Get(name);

        static void Set(RetroBatInstall install, string name, string value)
        {
            var settings = EsSettings.Load(SettingsPath(install));
            settings.Set(name, value);
            settings.Save();
        }

        static ConfigSyncResult SyncEs(RetroBatInstall install, TempDir cloud, string pc, ConfigSyncMode mode = ConfigSyncMode.Full) =>
            new ConfigSync(new EsSettingsConfig(install), install.RetroboatDataPath, cloud.Path, pc).Sync(mode);

        static string CloudFile(TempDir cloud, string source, string pc) => File.ReadAllText(cloud.Combine(@"configuracoes\" + source + @"\" + pc + ".xml"));

        [Fact]
        public void Global_system_and_game_options_go_to_the_other_pc_but_hardware_and_paths_stay()
        {
            using (var cloud = new TempDir())
            using (var pcs = new TempDir())
            {
                var a = Pc(pcs, "A");
                var b = Pc(pcs, "B");
                WriteSettings(a,
                    ("string", "global.ratio", "16/9"),
                    ("string", "ps2.internal_resolution", "3"),
                    ("string", Game, "4"),
                    ("bool", "FavoritesFirst", "true"),
                    ("string", "Language", "pt_BR"),
                    ("int", "MonitorID", "1"),
                    ("string", "ThemeSet", "es-theme-PlayStation-X"),
                    ("string", "global.videomode", "1920x1080@60"),
                    ("string", "ps2[\"Jogo.iso\"].MonitorIndex", "2"),
                    ("string", "INPUT P1GUID", "030000007e0500000920000000006803"),
                    ("string", "global.exodosPath", "D:\\Jogos\\eXoDOS"),
                    ("string", "ScreenScraperPass", "senha-teste-123"),
                    ("string", "global.retroachievements.token", "token-teste"));
                WriteSettings(b, ("int", "MonitorID", "0"), ("string", "ThemeSet", "es-theme-carbon"));

                Assert.Equal(5, SyncEs(a, cloud, "PC-A").Sent.Count);
                var received = SyncEs(b, cloud, "PC-B");

                Assert.Equal(5, received.Received.Count);
                Assert.Equal("16/9", Get(b, "global.ratio"));
                Assert.Equal("3", Get(b, "ps2.internal_resolution"));
                Assert.Equal("4", Get(b, Game));
                Assert.Equal("true", Get(b, "FavoritesFirst"));
                Assert.Contains("<bool name=\"FavoritesFirst\"", File.ReadAllText(SettingsPath(b)));

                // O que é deste PC ficou como estava e não foi para a nuvem.
                Assert.Equal("0", Get(b, "MonitorID"));
                Assert.Equal("es-theme-carbon", Get(b, "ThemeSet"));
                Assert.Null(Get(b, "global.videomode"));
                var sent = CloudFile(cloud, "es", "PC-A");
                foreach (var local in new[] { "MonitorID", "ThemeSet", "videomode", "MonitorIndex", "INPUT P1", "eXoDOS", "senha-teste-123", "token-teste" })
                    Assert.DoesNotContain(local, sent);
            }
        }

        [Fact]
        public void A_new_pc_takes_the_cloud_values_instead_of_sending_its_own_defaults()
        {
            using (var cloud = new TempDir())
            using (var pcs = new TempDir())
            {
                var a = Pc(pcs, "A");
                var b = Pc(pcs, "B");
                WriteSettings(a, ("string", "Language", "pt_BR"));
                WriteSettings(b, ("string", "Language", "en_US"), ("string", "ScreenSaverBehavior", "dim"));

                SyncEs(a, cloud, "PC-A");
                var first = SyncEs(b, cloud, "PC-B");

                Assert.Equal(new[] { "Language" }, first.Received);
                Assert.Equal(new[] { "ScreenSaverBehavior" }, first.Sent);
                Assert.Equal("pt_BR", Get(b, "Language"));

                var back = SyncEs(a, cloud, "PC-A");
                Assert.Equal(new[] { "ScreenSaverBehavior" }, back.Received);
                Assert.Equal("pt_BR", Get(a, "Language"));
                Assert.Empty(back.Sent);
            }
        }

        [Fact]
        public void A_change_and_a_removal_on_one_pc_reach_the_other()
        {
            using (var cloud = new TempDir())
            using (var pcs = new TempDir())
            {
                var a = Pc(pcs, "A");
                var b = Pc(pcs, "B");
                WriteSettings(a, ("string", "ps2.internal_resolution", "3"), ("string", "global.ratio", "16/9"));
                SyncEs(a, cloud, "PC-A");
                SyncEs(b, cloud, "PC-B");

                Set(a, "ps2.internal_resolution", "5");
                Set(a, "global.ratio", null); // Voltou ao padrão no ES: a chave sai do arquivo.
                Assert.Equal(new[] { "global.ratio", "ps2.internal_resolution" }, SyncEs(a, cloud, "PC-A").Sent.OrderBy(s => s, StringComparer.Ordinal));

                var result = SyncEs(b, cloud, "PC-B");
                Assert.Equal(2, result.Received.Count);
                Assert.Equal("5", Get(b, "ps2.internal_resolution"));
                Assert.Null(Get(b, "global.ratio"));

                Assert.False(SyncEs(a, cloud, "PC-A").HasChanges);
                Assert.False(SyncEs(b, cloud, "PC-B").HasChanges);
            }
        }

        [Fact]
        public void When_both_pcs_changed_the_same_option_the_newest_change_wins()
        {
            using (var cloud = new TempDir())
            using (var pcs = new TempDir())
            {
                var a = Pc(pcs, "A");
                var b = Pc(pcs, "B");
                WriteSettings(a, ("string", "ps2.internal_resolution", "2"), ("string", "global.smooth", "1"));
                SyncEs(a, cloud, "PC-A");
                SyncEs(b, cloud, "PC-B");

                Set(a, "ps2.internal_resolution", "4");
                Set(a, "global.smooth", "0");
                SyncEs(a, cloud, "PC-A");

                // B mudou a resolução antes de A (vale a de A) e a suavização depois (vale a de B).
                Set(b, "ps2.internal_resolution", "3");
                File.SetLastWriteTimeUtc(SettingsPath(b), DateTime.UtcNow.AddHours(-1));
                var older = SyncEs(b, cloud, "PC-B");
                Assert.Equal("4", Get(b, "ps2.internal_resolution"));
                Assert.Contains(older.Conflicts, c => c.StartsWith("ps2.internal_resolution") && c.Contains("ficou o do PC-A"));

                Set(b, "global.smooth", "2");
                File.SetLastWriteTimeUtc(SettingsPath(b), DateTime.UtcNow.AddHours(1));
                var newer = SyncEs(b, cloud, "PC-B");
                Assert.Equal(new[] { "global.smooth" }, newer.Sent);

                SyncEs(a, cloud, "PC-A");
                Assert.Equal("2", Get(a, "global.smooth"));
                Assert.Equal("4", Get(a, "ps2.internal_resolution"));
            }
        }

        [Fact]
        public void With_the_es_open_changes_are_sent_and_the_cloud_ones_wait()
        {
            using (var cloud = new TempDir())
            using (var pcs = new TempDir())
            {
                var a = Pc(pcs, "A");
                var b = Pc(pcs, "B");
                WriteSettings(a, ("string", "ps2.internal_resolution", "2"), ("string", "global.ratio", "4/3"));
                SyncEs(a, cloud, "PC-A");
                SyncEs(b, cloud, "PC-B");

                Set(a, "ps2.internal_resolution", "6");
                SyncEs(a, cloud, "PC-A");
                Set(b, "global.ratio", "16/9");
                var before = File.ReadAllBytes(SettingsPath(b));

                var open = SyncEs(b, cloud, "PC-B", ConfigSyncMode.SendOnly);
                Assert.Equal(new[] { "global.ratio" }, open.Sent);
                Assert.Equal(new[] { "ps2.internal_resolution" }, open.Pending);
                Assert.Equal(before, File.ReadAllBytes(SettingsPath(b)));

                var closed = SyncEs(b, cloud, "PC-B");
                Assert.Equal(new[] { "ps2.internal_resolution" }, closed.Received);
                Assert.Empty(closed.Sent);
                Assert.Equal("6", Get(b, "ps2.internal_resolution"));
                Assert.Equal("16/9", Get(b, "global.ratio"));
            }
        }

        [Fact]
        public void A_first_sync_with_the_es_open_does_not_send_the_new_pc_defaults_later()
        {
            using (var cloud = new TempDir())
            using (var pcs = new TempDir())
            {
                var a = Pc(pcs, "A");
                var b = Pc(pcs, "B");
                WriteSettings(a, ("string", "Language", "pt_BR"));
                WriteSettings(b, ("string", "Language", "en_US"));
                SyncEs(a, cloud, "PC-A");

                var open = SyncEs(b, cloud, "PC-B", ConfigSyncMode.SendOnly);
                Assert.Equal(new[] { "Language" }, open.Pending);
                Assert.Empty(open.Sent);

                SyncEs(b, cloud, "PC-B");
                Assert.Equal("pt_BR", Get(b, "Language"));
            }
        }

        [Fact]
        public void A_dry_run_changes_nothing()
        {
            using (var cloud = new TempDir())
            using (var pcs = new TempDir())
            {
                var a = Pc(pcs, "A");
                var b = Pc(pcs, "B");
                WriteSettings(a, ("string", "Language", "pt_BR"));
                WriteSettings(b, ("string", "Language", "en_US"));
                SyncEs(a, cloud, "PC-A");

                var plan = SyncEs(b, cloud, "PC-B", ConfigSyncMode.DryRun);
                Assert.Equal(new[] { "Language" }, plan.Received);
                Assert.Equal("en_US", Get(b, "Language"));
                Assert.False(File.Exists(cloud.Combine(@"configuracoes\es\PC-B.xml")));
                Assert.False(Directory.Exists(Path.Combine(b.RetroboatDataPath, ConfigSync.StateFolderName)));
            }
        }

        [Fact]
        public void The_file_is_copied_to_the_history_before_it_changes()
        {
            using (var cloud = new TempDir())
            using (var pcs = new TempDir())
            {
                var a = Pc(pcs, "A");
                var b = Pc(pcs, "B");
                WriteSettings(a, ("string", "Language", "pt_BR"));
                WriteSettings(b, ("string", "Language", "en_US"));
                SyncEs(a, cloud, "PC-A");
                SyncEs(b, cloud, "PC-B");

                var history = Directory.GetFiles(Path.Combine(b.RetroboatDataPath, @"historico\configuracoes\es"), "es_settings.cfg", SearchOption.AllDirectories);
                Assert.Single(history);
                Assert.Contains("en_US", File.ReadAllText(history[0]));
            }
        }

        [Fact]
        public void A_deleted_settings_file_comes_back_from_the_cloud_instead_of_erasing_the_other_pcs()
        {
            using (var cloud = new TempDir())
            using (var pcs = new TempDir())
            {
                var a = Pc(pcs, "A");
                var b = Pc(pcs, "B");
                WriteSettings(a, ("string", "ps2.internal_resolution", "3"), ("string", "Language", "pt_BR"));
                SyncEs(a, cloud, "PC-A");
                SyncEs(b, cloud, "PC-B");

                File.Delete(SettingsPath(b));
                var result = SyncEs(b, cloud, "PC-B");

                Assert.Empty(result.Sent);
                Assert.Equal("3", Get(b, "ps2.internal_resolution"));
                Assert.False(SyncEs(a, cloud, "PC-A").HasChanges);
                Assert.Equal("pt_BR", Get(a, "Language"));
            }
        }

        [Fact]
        public void Values_with_spaces_and_line_breaks_survive_the_cloud_file()
        {
            using (var cloud = new TempDir())
            using (var pcs = new TempDir())
            {
                var a = Pc(pcs, "A");
                var b = Pc(pcs, "B");
                WriteSettings(a, ("string", "global.netplay.nickname", " Jogador 1 "), ("string", "ScreenSaverGameInfo", "start & end"));

                SyncEs(a, cloud, "PC-A");
                SyncEs(b, cloud, "PC-B");

                Assert.Equal(" Jogador 1 ", Get(b, "global.netplay.nickname"));
                Assert.Equal("start & end", Get(b, "ScreenSaverGameInfo"));
                Assert.False(SyncEs(a, cloud, "PC-A").HasChanges);
            }
        }

        [Fact]
        public void A_half_uploaded_file_of_another_pc_is_skipped()
        {
            using (var cloud = new TempDir())
            using (var pcs = new TempDir())
            {
                var a = Pc(pcs, "A");
                WriteSettings(a, ("string", "Language", "pt_BR"));
                cloud.WriteText(@"configuracoes\es\PC-B.xml", "<retroboatConfig source=\"es\" pc=\"PC-B\"><entry id=\"Lang");

                var result = SyncEs(a, cloud, "PC-A");

                Assert.Empty(result.Errors);
                Assert.Equal(new[] { "Language" }, result.Sent);
            }
        }

        [Fact]
        public void Controllers_configured_on_one_pc_reach_the_other()
        {
            using (var cloud = new TempDir())
            using (var pcs = new TempDir())
            {
                var a = Pc(pcs, "A");
                var b = Pc(pcs, "B");
                File.WriteAllText(Path.Combine(a.UserEmulationStationPath, "es_input.cfg"), "<?xml version=\"1.0\"?>\n<inputList>\n" +
                    Controller("keyboard", "-1", "Keyboard", "120") + Controller("joystick", "0300dual", "DualSense", "1") + "</inputList>\n");
                File.WriteAllText(Path.Combine(b.UserEmulationStationPath, "es_input.cfg"), "<?xml version=\"1.0\"?>\n<inputList>\n" +
                    Controller("keyboard", "-1", "Keyboard", "13") + Controller("joystick", "0300pro", "Pro Controller", "0") + "</inputList>\n");

                Sync(a, cloud, "PC-A", new EsInputConfig(a));
                var atB = Sync(b, cloud, "PC-B", new EsInputConfig(b));
                Assert.Equal(new[] { "joystick|0300dual", "keyboard|-1" }, atB.Received.OrderBy(i => i, StringComparer.Ordinal));
                Assert.Equal(new[] { "joystick|0300pro" }, atB.Sent);

                var atA = Sync(a, cloud, "PC-A", new EsInputConfig(a));
                Assert.Equal(new[] { "joystick|0300pro" }, atA.Received);

                foreach (var pc in new[] { a, b })
                {
                    var configs = XDocument.Load(Path.Combine(pc.UserEmulationStationPath, "es_input.cfg")).Root.Elements("inputConfig").ToList();
                    Assert.Equal(3, configs.Count);
                    Assert.Equal("120", (string)configs.Single(c => (string)c.Attribute("type") == "keyboard").Element("input").Attribute("id"));
                }
            }
        }

        static string Controller(string type, string guid, string name, string buttonA) =>
            "\t<inputConfig type=\"" + type + "\" deviceName=\"" + name + "\" deviceGUID=\"" + guid + "\">\n" +
            "\t\t<input name=\"a\" type=\"" + (type == "keyboard" ? "key" : "button") + "\" id=\"" + buttonA + "\" value=\"1\" />\n\t</inputConfig>\n";

        static ConfigSyncResult Sync(RetroBatInstall install, TempDir cloud, string pc, IConfigSource source, ConfigSyncMode mode = ConfigSyncMode.Full) =>
            new ConfigSync(source, install.RetroboatDataPath, cloud.Path, pc).Sync(mode);

        // PCSX2

        static string Pcsx2(RetroBatInstall install, string relative) => Path.Combine(install.Root, "emulators", "pcsx2", relative);

        static void InstallPcsx2(RetroBatInstall install, string ini)
        {
            Directory.CreateDirectory(Pcsx2(install, "inis"));
            File.WriteAllBytes(Pcsx2(install, "pcsx2-qt.exe"), new byte[] { 0x4D, 0x5A });
            if (ini != null)
                File.WriteAllText(Pcsx2(install, @"inis\PCSX2.ini"), ini.Replace("\n", "\r\n"));
        }

        static EmulatorConfigSource Pcsx2Source(RetroBatInstall install) => new EmulatorConfigSource(install, EmulatorConfigRules.Pcsx2);

        static string Ini(string pcsx2Path, string upscale, string geometry) =>
            "[UI]\nSettingsVersion = 1\nStartFullscreen = true\nMainWindowGeometry = " + geometry + "\n\n" +
            "[Folders]\nBios = " + pcsx2Path + "\\bios\\pcsx2\\bios\nSavestates = sstates\n\n" +
            "; ajustes de vídeo\n[EmuCore/GS]\nupscale_multiplier = " + upscale + "\nAdapter = Placa deste PC\n\n" +
            "[MemoryCards]\nSlot1_Filename = Mcd001.ps2\n\n" +
            "[Achievements]\nEnabled = true\nToken = token-teste\n\n" +
            "[GameList]\nRecursivePaths = ..\\..\\roms\\ps2\nRecursivePaths = " + pcsx2Path + "\\jogos\n";

        [Fact]
        public void Pcsx2_settings_travel_without_folders_windows_memory_cards_or_login()
        {
            using (var cloud = new TempDir())
            using (var pcs = new TempDir())
            {
                var a = Pc(pcs, "A");
                var b = Pc(pcs, "B");
                InstallPcsx2(a, Ini(@"C:\RetroBat", "3", "AAAA"));
                InstallPcsx2(b, Ini(@"D:\Outro\RetroBat", "1", "BBBB"));
                var perGame = Encoding.UTF8.GetBytes("[EmuCore/GS]\r\nupscale_multiplier = 2\r\n");
                Directory.CreateDirectory(Pcsx2(a, "gamesettings"));
                File.WriteAllBytes(Pcsx2(a, @"gamesettings\SLUS-20312_8B52C23E.ini"), perGame);

                var sent = Sync(a, cloud, "PC-A", Pcsx2Source(a));
                Assert.Contains("ini|inis/PCSX2.ini@1|EmuCore/GS|upscale_multiplier", sent.Sent);
                Assert.Contains("file|gamesettings/SLUS-20312_8B52C23E.ini", sent.Sent);
                var inCloud = CloudFile(cloud, "pcsx2", "PC-A");
                foreach (var local in new[] { "Folders", "MainWindowGeometry", "SettingsVersion", "Adapter", "MemoryCards", "token-teste", "RecursivePaths" })
                    Assert.DoesNotContain(local, inCloud);

                // B já tinha o PCSX2 configurado, mas é a primeira vez dele: vale a nuvem.
                Sync(b, cloud, "PC-B", Pcsx2Source(b));
                var ini = File.ReadAllText(Pcsx2(b, @"inis\PCSX2.ini"));
                Assert.Contains("upscale_multiplier = 3", ini);
                Assert.Contains(@"Bios = D:\Outro\RetroBat\bios\pcsx2\bios", ini);
                Assert.Contains("MainWindowGeometry = BBBB", ini);
                Assert.Contains("Adapter = Placa deste PC", ini);
                Assert.Contains(@"RecursivePaths = D:\Outro\RetroBat\jogos", ini);
                Assert.Contains("; ajustes de vídeo", ini);
                Assert.Contains("\r\n", ini);
                Assert.Equal(perGame, File.ReadAllBytes(Pcsx2(b, @"gamesettings\SLUS-20312_8B52C23E.ini")));

                Assert.False(Sync(a, cloud, "PC-A", Pcsx2Source(a)).HasChanges);
                Assert.False(Sync(b, cloud, "PC-B", Pcsx2Source(b)).HasChanges);
            }
        }

        [Fact]
        public void A_change_in_the_pcsx2_menus_reaches_the_other_pc_and_a_deleted_per_game_file_too()
        {
            using (var cloud = new TempDir())
            using (var pcs = new TempDir())
            {
                var a = Pc(pcs, "A");
                var b = Pc(pcs, "B");
                InstallPcsx2(a, Ini(@"C:\RetroBat", "3", "AAAA"));
                InstallPcsx2(b, null);
                Directory.CreateDirectory(Pcsx2(a, "gamesettings"));
                File.WriteAllText(Pcsx2(a, @"gamesettings\SLUS-20312_8B52C23E.ini"), "[EmuCore]\r\nEnableCheats = true\r\n");
                Sync(a, cloud, "PC-A", Pcsx2Source(a));
                Sync(b, cloud, "PC-B", Pcsx2Source(b));
                var created = File.ReadAllText(Pcsx2(b, @"inis\PCSX2.ini"));
                Assert.Contains("upscale_multiplier = 3", created);

                // Sem a versão, o PCSX2 zeraria o arquivo criado na primeira abertura.
                Assert.Contains("SettingsVersion = 1", created);

                File.WriteAllText(Pcsx2(a, @"inis\PCSX2.ini"), Ini(@"C:\RetroBat", "6", "CCCC").Replace("StartFullscreen = true\n", ""));
                File.Delete(Pcsx2(a, @"gamesettings\SLUS-20312_8B52C23E.ini"));
                var sent = Sync(a, cloud, "PC-A", Pcsx2Source(a));
                Assert.Equal(3, sent.Sent.Count);

                var received = Sync(b, cloud, "PC-B", Pcsx2Source(b));
                Assert.Equal(3, received.Received.Count);
                var ini = File.ReadAllText(Pcsx2(b, @"inis\PCSX2.ini"));
                Assert.Contains("upscale_multiplier = 6", ini);
                Assert.DoesNotContain("StartFullscreen", ini);
                Assert.False(File.Exists(Pcsx2(b, @"gamesettings\SLUS-20312_8B52C23E.ini")));
                Assert.NotEmpty(Directory.GetFiles(Path.Combine(b.RetroboatDataPath, @"historico\configuracoes\pcsx2"), "SLUS-20312_8B52C23E.ini", SearchOption.AllDirectories));
            }
        }

        [Fact]
        public void Without_pcsx2_installed_nothing_is_written()
        {
            using (var cloud = new TempDir())
            using (var pcs = new TempDir())
            {
                var a = Pc(pcs, "A");
                var b = Pc(pcs, "B");
                InstallPcsx2(a, Ini(@"C:\RetroBat", "3", "AAAA"));
                Sync(a, cloud, "PC-A", Pcsx2Source(a));

                var result = Sync(b, cloud, "PC-B", Pcsx2Source(b));

                Assert.True(result.Skipped);
                Assert.False(Directory.Exists(Path.Combine(b.Root, "emulators")));
            }
        }

        [Fact]
        public void A_reinstalled_emulator_gets_its_settings_back_instead_of_resetting_the_other_pcs()
        {
            using (var cloud = new TempDir())
            using (var pcs = new TempDir())
            {
                var a = Pc(pcs, "A");
                var b = Pc(pcs, "B");
                InstallPcsx2(a, Ini(@"C:\RetroBat", "3", "AAAA"));
                InstallPcsx2(b, null);
                Directory.CreateDirectory(Pcsx2(a, "gamesettings"));
                File.WriteAllText(Pcsx2(a, @"gamesettings\SLUS-20312_8B52C23E.ini"), "[EmuCore]\r\nEnableCheats = true\r\n");
                Sync(a, cloud, "PC-A", Pcsx2Source(a));
                Sync(b, cloud, "PC-B", Pcsx2Source(b));

                Directory.Delete(Path.Combine(b.Root, "emulators", "pcsx2"), true);
                InstallPcsx2(b, null);
                var result = Sync(b, cloud, "PC-B", Pcsx2Source(b));

                Assert.Empty(result.Sent);
                Assert.Contains("upscale_multiplier = 3", File.ReadAllText(Pcsx2(b, @"inis\PCSX2.ini")));
                Assert.True(File.Exists(Pcsx2(b, @"gamesettings\SLUS-20312_8B52C23E.ini")));
                Assert.False(Sync(a, cloud, "PC-A", Pcsx2Source(a)).HasChanges);
            }
        }

        [Fact]
        public void Pcsx2_ini_keys_only_travel_between_the_same_settings_version()
        {
            using (var cloud = new TempDir())
            using (var pcs = new TempDir())
            {
                var a = Pc(pcs, "A");
                var b = Pc(pcs, "B");
                InstallPcsx2(a, Ini(@"C:\RetroBat", "3", "AAAA"));
                InstallPcsx2(b, Ini(@"D:\RetroBat", "1", "BBBB").Replace("SettingsVersion = 1", "SettingsVersion = 2"));
                Directory.CreateDirectory(Pcsx2(a, "gamesettings"));
                File.WriteAllText(Pcsx2(a, @"gamesettings\SLUS-20312_8B52C23E.ini"), "[EmuCore]\r\nEnableCheats = true\r\n");
                Sync(a, cloud, "PC-A", Pcsx2Source(a));

                // Um PCSX2 de outra versão (que zera o arquivo da versão 1) não troca chaves com o A; o resto vai e vem.
                var atB = Sync(b, cloud, "PC-B", Pcsx2Source(b));
                Assert.Equal(new[] { "file|gamesettings/SLUS-20312_8B52C23E.ini" }, atB.Received);
                Assert.Contains("upscale_multiplier = 1", File.ReadAllText(Pcsx2(b, @"inis\PCSX2.ini")));
                Assert.All(atB.Sent, id => Assert.StartsWith("ini|inis/PCSX2.ini@2|", id));

                Assert.False(Sync(a, cloud, "PC-A", Pcsx2Source(a)).HasChanges);
                Assert.Contains("upscale_multiplier = 3", File.ReadAllText(Pcsx2(a, @"inis\PCSX2.ini")));
            }
        }

        [Fact]
        public void Files_from_the_cloud_never_leave_their_folder()
        {
            using (var pcs = new TempDir())
            {
                var source = Pcsx2Source(Pc(pcs, "A"));
                Assert.False(source.IsPortable(@"file|gamesettings/..\..\evil.ini", "AA=="));
                Assert.False(source.IsPortable("file|gamesettings/x.exe", "AA=="));
                Assert.False(source.IsPortable("file|outra/x.ini", "AA=="));
                Assert.True(source.IsPortable("file|gamesettings/SLUS-20312_8B52C23E.ini", "AA=="));
            }
        }

        [Fact]
        public void The_ini_editor_keeps_order_comments_and_repeated_keys()
        {
            var ini = IniDocument.Parse("; topo\r\n[A]\r\nx = 1\r\n\r\n[B]\r\nlist = um\r\nlist = dois\r\ny = 2\r\n");

            ini.Set("A", "x", new[] { "9" });
            ini.Set("A", "nova", new[] { "sim" });
            ini.Set("B", "list", new[] { "tres" });
            ini.Set("B", "y", null);
            ini.Set("C/D", "z", new[] { "3" });

            Assert.Equal("; topo\r\n[A]\r\nx = 9\r\nnova = sim\r\n\r\n[B]\r\nlist = tres\r\n\r\n[C/D]\r\nz = 3\r\n", Encoding.UTF8.GetString(ini.ToBytes()));
            Assert.Equal(new[] { "um", "dois" }, IniDocument.Parse("[B]\nlist = um\nlist = dois\n").Values().Single().Value);
        }
    }
}
