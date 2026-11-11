using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Xml.Linq;
using Retroboat.Cloud;
using Retroboat.Emulators;
using Retroboat.Library;
using Retroboat.Transfers;
using Xunit;

namespace Retroboat.Tests
{
    /// <summary>RB-033: emuladores sempre na última versão, conferência do arquivo, junto com o jogo e compatibilidade.</summary>
    public class EmulatorTests
    {
        // ---- Catálogo ----

        [Fact]
        public void Catalog_maps_standalone_emulators_aliases_and_cores()
        {
            var flycast = EmulatorCatalog.For("flycast", "flycast", _ => false);
            Assert.Equal(PackageKind.Emulator, flycast.Kind);
            Assert.Equal("flycast.exe", flycast.Executables[0]);
            Assert.Equal("flycast.7z", flycast.ArchiveName);

            Assert.Equal("dolphin-emu", EmulatorCatalog.For("dolphin", "", _ => false).Name);
            Assert.Equal("mame", EmulatorCatalog.For("mame64", "", _ => false).Name);

            var core = EmulatorCatalog.For("libretro", "swanstation", _ => false);
            Assert.Equal(PackageKind.Core, core.Kind);
            Assert.Equal("core/swanstation", core.Key);
            Assert.Equal("swanstation_libretro.dll.zip", core.ArchiveName);
            Assert.Equal("SwanStation (RetroArch)", core.DisplayName);
        }

        [Fact]
        public void Catalog_ignores_external_programs_and_libretro_without_core()
        {
            Assert.Null(EmulatorCatalog.For("libretro", "", _ => true));
            Assert.Null(EmulatorCatalog.For("windows", "", _ => false));
            Assert.Null(EmulatorCatalog.For("", "", _ => true));

            // Fora do catálogo, mas com pasta do RetroBat: é dele, com qualquer .exe.
            var other = EmulatorCatalog.For("novoemu", "", name => name == "novoemu");
            Assert.Equal("novoemu", other.Name);
            Assert.Empty(other.Executables);
        }

        // ---- Qual emulador o jogo usa ----

        static IReadOnlyList<EmulatorDefinition> PsxEmulators() => EmulatorChoice.Definitions(XElement.Parse(
            "<emulators>" +
            "<emulator name=\"libretro\"><cores><core>mednafen_psx_hw</core><core default=\"true\">swanstation</core><core>pcsx_rearmed</core></cores></emulator>" +
            "<emulator name=\"duckstation\"><cores><core>duckstation</core></cores></emulator>" +
            "</emulators>"));

        [Fact]
        public void Choice_follows_the_es_order_game_then_system_then_default()
        {
            var emulators = PsxEmulators();
            Assert.Equal("libretro/swanstation", EmulatorChoice.Resolve(emulators, null, null, null, null).ToString());
            Assert.Equal("libretro/pcsx_rearmed", EmulatorChoice.Resolve(emulators, null, null, "", "pcsx_rearmed").ToString());
            Assert.Equal("duckstation/duckstation", EmulatorChoice.Resolve(emulators, null, null, "duckstation", "").ToString());
            Assert.Equal("libretro/mednafen_psx_hw", EmulatorChoice.Resolve(emulators, "libretro", "mednafen_psx_hw", "duckstation", "").ToString());

            // O core do sistema não vale para o emulador escolhido no jogo; o que não existe na lista é ignorado.
            Assert.Equal("libretro/swanstation", EmulatorChoice.Resolve(emulators, "libretro", "", "libretro", "pcsx_rearmed").ToString());
            Assert.Equal("libretro/swanstation", EmulatorChoice.Resolve(emulators, "naoexiste", "x", "auto", "auto").ToString());
        }

        [Fact]
        public void Choice_for_a_game_reads_gamelist_settings_and_the_retroboat_overlay()
        {
            using (var temp = new TempDir())
            {
                var install = new RetroBatInstall(temp.Path);
                temp.WriteText(@"emulationstation\.emulationstation\es_systems.cfg",
                    "<systemList><system><name>dreamcast</name><path>~\\..\\roms\\dreamcast</path><command>x</command>" +
                    "<emulators><emulator name=\"libretro\"><cores><core>flycast</core></cores></emulator><emulator name=\"flycast\"><cores><core>flycast</core></cores></emulator></emulators>" +
                    "</system></systemList>");

                Assert.Equal("libretro/flycast", EmulatorChoice.ForGame(install, "dreamcast", @"C:\roms\dreamcast\Jogo.chd").ToString());

                temp.WriteText(@"emulationstation\.emulationstation\es_settings.cfg",
                    "<?xml version=\"1.0\"?><config><string name=\"dreamcast.emulator\" value=\"flycast\" /></config>");
                Assert.Equal("flycast/flycast", EmulatorChoice.ForGame(install, "dreamcast", @"C:\roms\dreamcast\Jogo.chd").ToString());

                temp.WriteText(@"emulationstation\.emulationstation\gamelists\dreamcast\gamelist.xml",
                    "<gameList><game><path>./Jogo.chd</path><name>Jogo</name><emulator>libretro</emulator><core>flycast</core></game></gameList>");
                Assert.Equal("libretro/flycast", EmulatorChoice.ForGame(install, "dreamcast", @"C:\roms\dreamcast\Jogo.chd").ToString());
                Assert.Null(EmulatorChoice.ForGame(install, "naoexiste", null));
            }
        }

        // ---- Versões ----

        [Theory]
        [InlineData("2.7.0.1", "2.7.0.1")]
        [InlineData("0.26.08.16", "0.26.8.16")]
        [InlineData("2609.1", "2609.1")]
        [InlineData("2.8", "2.8")]
        public void Versions_parse_the_repository_formats(string text, string expected)
        {
            Assert.Equal(Version.Parse(expected), EmulatorVersions.Parse(text));
        }

        [Fact]
        public void Versions_compare_ignoring_trailing_zeros()
        {
            Assert.Null(EmulatorVersions.Parse("nightly"));
            Assert.True(EmulatorVersions.IsNewer(EmulatorVersions.Parse("2.8.2.0"), EmulatorVersions.Parse("2.8.1")));
            Assert.False(EmulatorVersions.IsNewer(EmulatorVersions.Parse("2.8"), EmulatorVersions.Parse("2.8.0.0")));
            Assert.False(EmulatorVersions.IsNewer(null, EmulatorVersions.Parse("1.0")));
            Assert.Equal("2.8.2", EmulatorVersions.Display(new Version(2, 8, 2, 0)));
            Assert.Equal("2609.1", EmulatorVersions.Display(new Version(2609, 1)));
        }

        [Fact]
        public void Repository_versions_reject_an_unknown_format()
        {
            var versions = RetroBatRepository.ParseVersions("<systems><system name=\"flycast\" version=\"2.7.0.1\" /><system name=\"x\" /></systems>");
            Assert.Equal("2.7.0.1", versions["FLYCAST"]);
            Assert.Single(versions);

            Assert.True(Assert.Throws<RepositoryException>(() => RetroBatRepository.ParseVersions("<emulators><emulator id=\"a\" /></emulators>")).FormatChanged);
            Assert.True(Assert.Throws<RepositoryException>(() => RetroBatRepository.ParseVersions("<html>404")).FormatChanged);
        }

        // ---- Instalado ou não ----

        [Fact]
        public void A_retrobat_template_folder_without_executable_is_not_installed()
        {
            using (var temp = new TempDir())
            {
                var install = new RetroBatInstall(temp.Path);
                var flycast = EmulatorCatalog.For("flycast", "", _ => true);
                temp.WriteText(@"emulators\flycast\mappings\SDL_Xbox.cfg", "[x]");
                Assert.False(flycast.IsInstalled(install));

                // Um .exe de outro nome conta (uma versão nova que renomeou não é baixada de novo a cada jogo).
                temp.WriteFile(@"emulators\flycast\bin\flycast-novo.exe", ProgramBytes());
                Assert.True(flycast.IsInstalled(install));

                var core = EmulatorCatalog.For("libretro", "flycast", _ => true);
                Assert.False(core.IsInstalled(install));
                temp.WriteFile(@"emulators\retroarch\cores\flycast_libretro.dll", ProgramBytes());
                Assert.True(core.IsInstalled(install));
            }
        }

        // ---- Conferência do arquivo ----

        [Fact]
        public void File_check_requires_the_announced_length_and_a_real_program()
        {
            using (var temp = new TempDir())
            {
                var file = temp.WriteFile("pacote.7z", new byte[100]);
                Assert.Null(PackageCheck.Length(file, 100));
                Assert.Contains("anunciou 120", PackageCheck.Length(file, 120));
                Assert.Contains("vazio", PackageCheck.Length(temp.WriteFile("vazio.7z", new byte[0]), 0));

                Assert.Null(PackageCheck.Program(temp.WriteFile("ok.exe", ProgramBytes())));
                Assert.Contains("MZ", PackageCheck.Program(temp.WriteText("texto.exe", "isto não é um programa, só texto com tamanho suficiente para o cabeçalho............")));
                var truncated = ProgramBytes().Take(0x50).ToArray();
                Assert.NotNull(PackageCheck.Program(temp.WriteFile("cortado.exe", truncated)));
            }
        }

        [Fact]
        public void Installs_an_emulator_from_the_repository_checked_and_recorded()
        {
            using (var env = new Env())
            {
                env.Publish("flycast", "2.7.0.1", ("flycast.exe", ProgramBytes()), ("data\\leia.txt", new byte[] { 1 }));
                var status = env.Status("flycast", "");
                Assert.Equal(PackageState.NotInstalled, status.State);
                Assert.True(status.NeedsDownload);

                var result = env.Installer().Install(status, null, CancellationToken.None);
                Assert.True(result.Installed, result.Error);
                Assert.Equal("2.7.0.1", result.Version);
                Assert.Empty(result.Issues);
                Assert.True(File.Exists(env.Temp.Combine(@"emulators\flycast\flycast.exe")));
                Assert.False(Directory.Exists(env.Temp.Combine(@"emulators", EmulatorInstaller.TempFolderName)));

                var after = env.Status("flycast", "");
                Assert.Equal(PackageState.Current, after.State);
                Assert.Equal("2.7.0.1", after.InstalledVersion);
                Assert.Contains("flycast", File.ReadAllText(EmulatorLog.PathFor(env.Install)));
            }
        }

        [Fact]
        public void A_corrupted_or_short_download_is_rejected_and_nothing_changes()
        {
            using (var env = new Env())
            {
                env.Publish("flycast", "2.7.0.1", ("flycast.exe", ProgramBytes()));
                env.Source.Corrupt = true;
                var result = env.Installer().Install(env.Status("flycast", ""), null, CancellationToken.None);
                Assert.False(result.Installed);
                Assert.Contains("corrompido", result.Error);
                Assert.False(Directory.Exists(env.Temp.Combine(@"emulators\flycast")) && Directory.GetFiles(env.Temp.Combine(@"emulators\flycast")).Any());

                env.Source.Corrupt = false;
                env.Source.Truncate = true;
                result = env.Installer().Install(env.Status("flycast", ""), null, CancellationToken.None);
                Assert.False(result.Installed);
                Assert.Contains("bytes", result.Error);
            }
        }

        [Fact]
        public void A_package_without_a_program_is_rejected()
        {
            using (var env = new Env())
            {
                env.Publish("flycast", "2.7.0.1", ("leia.txt", new byte[] { 1, 2, 3 }));
                var result = env.Installer().Install(env.Status("flycast", ""), null, CancellationToken.None);
                Assert.False(result.Installed);
                Assert.Contains("nenhum programa", result.Error);
            }
        }

        [Fact]
        public void Update_installs_the_newer_version_and_keeps_the_user_config()
        {
            using (var env = new Env())
            {
                env.Temp.WriteText(@"system\upgrade.xml", "<upgradeinfo><neveroverwrite path=\"emulators\\flycast\\emu.cfg\"/></upgradeinfo>");
                env.Publish("flycast", "2.6.0.0", ("flycast.exe", ProgramBytes()), ("emu.cfg", new byte[] { 0 }));
                Assert.True(env.Installer().Install(env.Status("flycast", ""), null, CancellationToken.None).Installed);
                env.Temp.WriteText(@"emulators\flycast\emu.cfg", "do usuário");

                env.Publish("flycast", "2.7.0.1", ("flycast.exe", ProgramBytes()), ("emu.cfg", new byte[] { 0 }), ("novo.dll", new byte[] { 9 }));
                env.ExpireCache();
                var status = env.Status("flycast", "");
                Assert.Equal(PackageState.UpdateAvailable, status.State);
                Assert.Equal("2.6.0.0", status.InstalledVersion);

                var result = env.Installer().Install(status, null, CancellationToken.None);
                Assert.True(result.Installed, result.Error);
                Assert.Equal("2.7.0.1", result.Version);
                Assert.Equal("do usuário", File.ReadAllText(env.Temp.Combine(@"emulators\flycast\emu.cfg")));
                Assert.True(File.Exists(env.Temp.Combine(@"emulators\flycast\novo.dll")));
                Assert.Equal(PackageState.Current, env.Status("flycast", "").State);
            }
        }

        [Fact]
        public void A_core_installed_by_retrobat_is_adopted_and_updated_when_the_server_changes()
        {
            using (var env = new Env())
            {
                env.Temp.WriteFile(@"emulators\retroarch\retroarch.exe", ProgramBytes());
                env.Temp.WriteFile(@"emulators\retroarch\cores\flycast_libretro.dll", ProgramBytes());
                env.Publish("core/flycast", null, ("flycast_libretro.dll", ProgramBytes()));

                Assert.Equal(PackageState.Current, env.Status("libretro", "flycast").State);

                env.Source.Modified = env.Source.Modified.AddDays(10);
                env.ExpireCache();
                var status = env.Status("libretro", "flycast");
                Assert.Equal(PackageState.UpdateAvailable, status.State);

                var result = env.Installer().Install(status, null, CancellationToken.None);
                Assert.True(result.Installed, result.Error);
                Assert.Empty(result.Issues);
                env.ExpireCache();
                Assert.Equal(PackageState.Current, env.Status("libretro", "flycast").State);
            }
        }

        [Fact]
        public void Without_internet_an_installed_emulator_is_current_and_a_missing_one_says_why()
        {
            using (var env = new Env())
            {
                env.Source.Offline = true;
                env.Temp.WriteFile(@"emulators\pcsx2\pcsx2-qt.exe", ProgramBytes());
                Assert.Equal(PackageState.Current, env.Status("pcsx2", "").State);

                var missing = env.Status("flycast", "");
                Assert.Equal(PackageState.NotInstalled, missing.State);
                Assert.False(missing.NeedsDownload);
                Assert.Contains("sem conexão", missing.Problem);
            }
        }

        [Fact]
        public void After_a_connection_failure_the_cache_waits_before_trying_again()
        {
            using (var env = new Env())
            {
                env.Source.Offline = true;
                Assert.Null(env.Cache().Versions());

                env.Source.Offline = false;
                env.Source.Versions["flycast"] = "2.7.0.1";
                var soon = env.Cache();
                Assert.Null(soon.Versions());
                Assert.Contains("sem conexão", soon.Problem);

                soon.RetryNow();
                Assert.Equal("2.7.0.1", env.Cache().Versions()["flycast"]);
            }
        }

        // ---- Junto com o jogo ----

        [Fact]
        public void The_emulator_comes_with_the_game_in_one_progress()
        {
            using (var env = new Env())
            {
                env.Temp.WriteText(@"emulationstation\.emulationstation\es_systems.cfg",
                    "<systemList><system><name>dreamcast</name><path>~\\..\\roms\\dreamcast</path><command>x</command>" +
                    "<emulators><emulator name=\"flycast\"><cores><core>flycast</core></cores></emulator></emulators></system></systemList>");
                env.Publish("flycast", "2.7.0.1", ("flycast.exe", ProgramBytes()));
                long emulatorBytes = new FileInfo(env.Source.Files["flycast"]).Length;

                var reports = new List<TransferProgress>();
                var progress = new SyncProgress(reports.Add);
                var entry = new CloudIndexEntry { System = "dreamcast", CloudName = "Jogo.chd", LocalName = "Jogo.chd" };

                var result = GameEmulatorDownload.Install(env.Install, entry, env.Temp.Combine("roms"), gameProgress =>
                {
                    gameProgress.Report(new TransferProgress { BytesDone = 500, BytesTotal = 1000 });
                    gameProgress.Report(new TransferProgress { BytesDone = 1000, BytesTotal = 1000 });
                    return new OnDemandResult(OnDemandOutcome.Installed);
                }, progress, CancellationToken.None, env.Source);

                Assert.Equal(OnDemandOutcome.Installed, result.Outcome);
                Assert.True(File.Exists(env.Temp.Combine(@"emulators\flycast\flycast.exe")));
                Assert.All(reports, r => Assert.Equal(1000 + emulatorBytes, r.BytesTotal));
                Assert.Equal(500, reports[0].BytesDone);
                Assert.Equal(1000 + emulatorBytes, reports.Last().BytesDone);
            }
        }

        [Fact]
        public void The_game_stays_installed_when_the_emulator_fails()
        {
            using (var env = new Env())
            {
                env.Temp.WriteText(@"emulationstation\.emulationstation\es_systems.cfg",
                    "<systemList><system><name>dreamcast</name><path>~\\..\\roms\\dreamcast</path><command>x</command>" +
                    "<emulators><emulator name=\"flycast\"><cores><core>flycast</core></cores></emulator></emulators></system></systemList>");
                env.Publish("flycast", "2.7.0.1", ("flycast.exe", ProgramBytes()));
                env.Source.Corrupt = true;

                var entry = new CloudIndexEntry { System = "dreamcast", CloudName = "Jogo.chd", LocalName = "Jogo.chd" };
                var result = GameEmulatorDownload.Install(env.Install, entry, env.Temp.Combine("roms"),
                    p => new OnDemandResult(OnDemandOutcome.Installed), null, CancellationToken.None, env.Source);

                Assert.Equal(OnDemandOutcome.Installed, result.Outcome);
                Assert.Contains("oferecido ao abrir o jogo", File.ReadAllText(EmulatorLog.PathFor(env.Install)));
            }
        }

        // ---- Compatibilidade ----

        [Fact]
        public void Compat_reports_a_renamed_executable_and_a_missing_retroarch()
        {
            using (var temp = new TempDir())
            {
                var install = new RetroBatInstall(temp.Path);
                var flycast = EmulatorCatalog.For("flycast", "", _ => true);
                temp.WriteFile(@"emulators\flycast\flycast-x64.exe", ProgramBytes());

                var issue = Assert.Single(EmulatorCompatibility.CheckInstalled(install, flycast, "2.8.0.0"));
                Assert.Equal(EmulatorCompatibility.ExecutableChanged, issue.Code);
                Assert.Equal(CompatImpact.Blocking, issue.Impact);
                Assert.Contains("flycast-x64.exe", issue.Found);

                var core = EmulatorCatalog.For("libretro", "flycast", _ => true);
                temp.WriteFile(@"emulators\retroarch\cores\flycast_libretro.dll", ProgramBytes());
                Assert.Equal(EmulatorCompatibility.RetroArchMissing, Assert.Single(EmulatorCompatibility.CheckInstalled(install, core, "")).Code);

                temp.WriteFile(@"emulators\retroarch\retroarch.exe", ProgramBytes());
                temp.WriteText(@"emulators\retroarch\cores\flycast_libretro.dll", "danificado, sem cabeçalho de programa..........................................");
                Assert.Equal(EmulatorCompatibility.ExecutableInvalid, Assert.Single(EmulatorCompatibility.CheckInstalled(install, core, "")).Code);
            }
        }

        [Fact]
        public void Compat_finds_a_save_written_outside_the_cloud_folder_during_the_game()
        {
            using (var temp = new TempDir())
            {
                var install = new RetroBatInstall(temp.Path);
                var core = EmulatorCatalog.For("libretro", "flycast", _ => true);
                temp.WriteFile(@"bios\dc\dc_boot.bin", new byte[] { 1 });
                temp.WriteFile(@"bios\dc\vmu_save_B1.bin", new byte[] { 1 });
                var watch = EmulatorCompatibility.Watch(install, core, "", temp.Combine("saves"));

                // Durante o jogo: a VMU nova em bios\dc (fora da nuvem) e um save em saves\ (na nuvem).
                temp.WriteFile(@"bios\dc\vmu_save_A1.bin", new byte[128]);
                temp.WriteFile(@"saves\dreamcast\Jogo.srm", new byte[64]);
                temp.WriteFile(@"emulators\retroarch\retroarch.cfg", new byte[1]);

                var issue = Assert.Single(EmulatorCompatibility.CheckAfterGame(watch));
                Assert.Equal(EmulatorCompatibility.SaveOutsideCloud, issue.Code);
                Assert.Contains(@"bios\dc\vmu_save_A1.bin", issue.Message);
                Assert.Contains("(novo)", issue.Found);
            }
        }

        [Fact]
        public void Compat_checks_the_pcsx2_memory_card_folder_and_its_config_file()
        {
            using (var temp = new TempDir())
            {
                var install = new RetroBatInstall(temp.Path);
                var pcsx2 = EmulatorCatalog.For("pcsx2", "", _ => true);
                var saves = temp.Combine("saves");
                var watch = EmulatorCompatibility.Watch(install, pcsx2, "2.9.0.0", saves);

                Assert.Equal(EmulatorCompatibility.ConfigMissing, Assert.Single(EmulatorCompatibility.CheckAfterGame(watch)).Code);

                temp.WriteText(@"emulators\pcsx2\inis\PCSX2.ini", "[Folders]\r\nMemoryCards = " + Path.Combine(saves, @"ps2\pcsx2\memcards") + "\r\n");
                Assert.Empty(EmulatorCompatibility.CheckAfterGame(watch));

                temp.WriteText(@"emulators\pcsx2\inis\PCSX2.ini", "[Folders]\r\nMemoryCards = memcards\r\n");
                var issue = Assert.Single(EmulatorCompatibility.CheckAfterGame(watch));
                Assert.Equal(EmulatorCompatibility.SaveFolderChanged, issue.Code);
                Assert.Contains("MemoryCards = memcards", issue.Found);
            }
        }

        [Fact]
        public void Compat_warnings_appear_once_per_version_and_always_go_to_the_log()
        {
            using (var temp = new TempDir())
            {
                var install = new RetroBatInstall(temp.Path);
                var issue = new CompatIssue { Code = "X", PackageKey = "flycast", PackageName = "Flycast", Version = "2.8", Message = "m", Expected = "e", Found = "f" };

                Assert.Single(EmulatorCompatibility.TakeNotShown(install, new[] { issue }));
                Assert.Empty(EmulatorCompatibility.TakeNotShown(install, new[] { issue }));

                var nextVersion = new CompatIssue { Code = "X", PackageKey = "flycast", PackageName = "Flycast", Version = "2.9", Message = "m", Expected = "e", Found = "f" };
                Assert.Single(EmulatorCompatibility.TakeNotShown(install, new[] { nextVersion }));

                EmulatorCompatibility.Log(install, new[] { issue }, "depois do jogo");
                var log = File.ReadAllText(EmulatorLog.PathFor(install));
                Assert.Contains("[COMPAT] Flycast 2.8", log);
                Assert.Contains("[X] m", log);
                Assert.Contains("esperado: e", log);
                Assert.Contains("O Flycast 2.8 mudou algo que o Retroboat usa", EmulatorCompatibility.Heading(new[] { issue }));
            }
        }

        // ---- Peças dos testes ----

        /// <summary>Um programa do Windows de verdade (o próprio assembly dos testes), para a conferência do cabeçalho PE.</summary>
        static byte[] ProgramBytes() => File.ReadAllBytes(typeof(EmulatorTests).Assembly.Location);

        sealed class SyncProgress : IProgress<TransferProgress>
        {
            readonly Action<TransferProgress> _report;
            public SyncProgress(Action<TransferProgress> report) => _report = report;
            public void Report(TransferProgress value) => _report(value);
        }

        /// <summary>Um RetroBat de teste com o 7-Zip do repositório e um servidor de pacotes local.</summary>
        sealed class Env : IDisposable
        {
            public Env()
            {
                Temp = new TempDir();
                Install = new RetroBatInstall(Temp.Path);
                foreach (var file in new[] { "7za.exe", "7za.dll" })
                {
                    var source = Path.Combine(SevenZipFolder(), file);
                    if (File.Exists(source))
                        File.Copy(source, Temp.Combine(@"system\tools\" + file).CreateParent());
                }

                Source = new FakeSource();
            }

            public TempDir Temp { get; }
            public RetroBatInstall Install { get; }
            public FakeSource Source { get; }

            public RepositoryCache Cache() => new RepositoryCache(Install, Source);

            public PackageStatus Status(string emulator, string core) =>
                PackageStatus.Of(Install, EmulatorCatalog.For(emulator, core, _ => true), Cache());

            public EmulatorInstaller Installer() => new EmulatorInstaller(Install, Cache());

            public void ExpireCache() => File.Delete(Temp.Combine(@"retroboat\estado", RepositoryCache.FileName));

            /// <summary>Põe um pacote no "servidor": .7z para emulador, .zip para core, criado pelo 7-Zip.</summary>
            public void Publish(string key, string version, params (string Path, byte[] Content)[] files)
            {
                var content = Temp.Combine("servidor-conteudo", Guid.NewGuid().ToString("N"));
                foreach (var file in files)
                    File.WriteAllBytes(Path.Combine(content, file.Path).CreateParent(), file.Content);

                var archive = Temp.Combine("servidor", key.Replace('/', '_') + (key.StartsWith("core/") ? ".zip" : ".7z")).CreateParent();
                if (File.Exists(archive))
                    File.Delete(archive);

                var run = Process.Start(new ProcessStartInfo(Temp.Combine(@"system\tools\7za.exe"), "a \"" + archive + "\" \"" + content + "\\*\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true
                });
                run.StandardOutput.ReadToEnd();
                run.WaitForExit();
                Assert.Equal(0, run.ExitCode);

                Source.Files[key] = archive;
                if (version != null)
                    Source.Versions[key] = version;
            }

            static string SevenZipFolder()
            {
                var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
                while (dir != null && !File.Exists(Path.Combine(dir.FullName, "system", "tools", "7za.exe")))
                    dir = dir.Parent;

                Assert.True(dir != null, "7za.exe do RetroBat não encontrado acima da pasta dos testes");
                return Path.Combine(dir.FullName, "system", "tools");
            }

            public void Dispose() => Temp.Dispose();
        }

        sealed class FakeSource : IPackageSource
        {
            public Dictionary<string, string> Versions { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, string> Files { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public DateTime Modified { get; set; } = new DateTime(2026, 4, 4, 8, 31, 26, DateTimeKind.Utc);
            public bool Offline { get; set; }
            public bool Corrupt { get; set; }
            public bool Truncate { get; set; }

            public string Describe(EmulatorPackage package) => "teste://" + package.ArchiveName;

            public IReadOnlyDictionary<string, string> ReadVersions(int timeoutMs)
            {
                if (Offline)
                    throw new RepositoryException("sem conexão com o repositório do RetroBat (teste)");

                return new Dictionary<string, string>(Versions, StringComparer.OrdinalIgnoreCase);
            }

            public RemoteFile Probe(EmulatorPackage package, int timeoutMs)
            {
                if (Offline)
                    throw new RepositoryException("sem conexão com o repositório do RetroBat (teste)");

                return Files.TryGetValue(package.Key, out var file)
                    ? new RemoteFile { Exists = true, Length = new FileInfo(file).Length, ModifiedUtc = Modified }
                    : new RemoteFile { Exists = false };
            }

            public void Download(EmulatorPackage package, string partPath, long expectedLength, IProgress<TransferProgress> progress, CancellationToken token)
            {
                var bytes = File.ReadAllBytes(Files[package.Key]);
                if (Corrupt)
                    for (int i = 40; i < bytes.Length - 8; i += 7)
                        bytes[i] ^= 0x5A;
                if (Truncate)
                    bytes = bytes.Take(bytes.Length - 10).ToArray();

                File.WriteAllBytes(partPath, bytes);
                progress?.Report(new TransferProgress { Item = package.DisplayName, BytesDone = bytes.Length, BytesTotal = expectedLength });
            }
        }
    }

    static class PathExtensions
    {
        /// <summary>Cria a pasta do arquivo e devolve o caminho.</summary>
        public static string CreateParent(this string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            return path;
        }
    }
}
