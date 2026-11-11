using System.IO;
using System.Linq;
using Retroboat.Cloud;
using Retroboat.Library;
using Retroboat.Settings;
using Xunit;

namespace Retroboat.Tests
{
    public class SettingsSummaryTests
    {
        const string Id = "3f2504e0-4f89-11d3-9a0c-0305e82c3301";

        static string Value(RetroboatSettings settings, CloudLocation location, string key) =>
            SettingsSummary.Describe(settings, () => location).Single(l => l.Key == key).Value;

        [Fact]
        public void Library_state_tells_the_menu_what_is_going_on()
        {
            using (var dir = new TempDir())
            {
                var retroboat = dir.Combine(@"G\Meu Drive\retroboat");
                dir.WriteFile(@"G\Meu Drive\retroboat\jogos\ps2\A.zip", new byte[10]);
                dir.WriteFile(@"G\Meu Drive\retroboat\jogos\psx\B.chd", new byte[10]);
                var chosen = new RetroboatSettings { CloudLibrary = Id, RomsPath = @"D:\Jogos", CloudVideos = true };

                var ok = new CloudLocation(retroboat, "Google Drive (G:\\)", Id);
                Assert.Equal("ok", Value(chosen, ok, "LibraryState"));
                Assert.Equal("2", Value(chosen, ok, "Games"));
                Assert.EndsWith(@"Meu Drive (" + Path.GetPathRoot(retroboat).TrimEnd('\\') + ")", Value(chosen, ok, "LibraryName"));
                Assert.Equal("1", Value(chosen, ok, "Videos"));
                Assert.Equal("1", Value(chosen, ok, "Automatic"));
                Assert.Equal(@"D:\Jogos", Value(chosen, ok, "RomsPath"));

                var missing = new CloudLocation(retroboat, "Google Drive (G:\\)", Id, libraryMissing: true);
                Assert.Equal("missing", Value(chosen, missing, "LibraryState"));
                Assert.Equal("0", Value(chosen, missing, "Games"));

                Assert.Equal("offline", Value(chosen, null, "LibraryState"));
                Assert.Equal("none", Value(new RetroboatSettings(), new CloudLocation(retroboat, "Google Drive (G:\\)"), "LibraryState"));
                Assert.Equal("configured", Value(new RetroboatSettings { CloudPath = retroboat }, new CloudLocation(retroboat, "configurado"), "LibraryState"));
            }
        }

        [Fact]
        public void Menu_can_only_change_the_main_options()
        {
            var settings = new RetroboatSettings { CloudLibrary = Id, RomsPath = @"D:\Jogos" };

            Assert.True(SettingsSummary.TrySet(settings, "Videos", "1", out _));
            Assert.True(SettingsSummary.TrySet(settings, "manuals", "1", out _));
            Assert.True(SettingsSummary.TrySet(settings, "Automatic", "0", out _));
            Assert.True(SettingsSummary.TrySet(settings, "AutoOpen", "1", out _));
            Assert.True(settings.CloudVideos && settings.CloudManuals && !settings.AutoMedia && settings.CloudAppAutoOpen);

            Assert.False(SettingsSummary.TrySet(settings, "Library", "1", out var error));
            Assert.Contains("setup", error);
            Assert.False(SettingsSummary.TrySet(settings, "RomsPath", "1", out _));
            Assert.False(SettingsSummary.TrySet(settings, "Videos", "sim", out _));
            Assert.Equal(Id, settings.CloudLibrary);
            Assert.Equal(@"D:\Jogos", settings.RomsPath);
        }

        [Fact]
        public void Changes_from_the_menu_are_taken_live_and_turning_media_on_asks_for_a_fetch()
        {
            var running = new RetroboatSettings { CloudLibrary = Id, RomsPath = @"D:\Jogos", AutoMedia = true };

            Assert.True(running.TakeMainOptionsFrom(new RetroboatSettings { CloudVideos = true, AutoMedia = true, CloudLibrary = "outra", RomsPath = @"E:\" }));
            Assert.True(running.CloudVideos);
            Assert.Equal(Id, running.CloudLibrary);
            Assert.Equal(@"D:\Jogos", running.RomsPath);

            // Desligar, ou mudar só o "abrir o Drive", não tem o que buscar.
            Assert.False(running.TakeMainOptionsFrom(new RetroboatSettings { CloudVideos = false, AutoMedia = false, CloudAppAutoOpen = true }));
            Assert.False(running.CloudVideos);
            Assert.True(running.CloudAppAutoOpen);
        }

        [Fact]
        public void Audio_output_is_remembered_and_reaches_the_running_launcher()
        {
            using (var dir = new TempDir())
            {
                const string Speakers = "{0.0.0.00000000}.{6b1a1c2e-0f4d-4c6a-9a59-2d1c3b4a5e6f}";
                var path = dir.Combine("retroboat.ini");
                new RetroboatSettings { RomsPath = @"D:\Jogos", AudioOutput = Speakers }.Save(path);
                Assert.Equal(Speakers, RetroboatSettings.Load(path).AudioOutput);

                // O Retroboat.exe aberto pega a escolha do menu e não a apaga ao gravar outra coisa.
                var running = new RetroboatSettings { RomsPath = @"D:\Jogos" };
                running.TakeMainOptionsFrom(RetroboatSettings.Load(path));
                running.Save(path);
                Assert.Equal(Speakers, RetroboatSettings.Load(path).AudioOutput);

                new RetroboatSettings { RomsPath = @"D:\Jogos" }.Save(path);
                Assert.Null(RetroboatSettings.Load(path).AudioOutput);
            }
        }

        [Fact]
        public void Audio_outputs_are_listed_without_changing_anything()
        {
            // Só leitura: o teste nunca troca a saída padrão do PC.
            var outputs = AudioOutputs.List();
            Assert.All(outputs, o => Assert.False(string.IsNullOrEmpty(o.Id)));
            Assert.All(outputs, o => Assert.False(string.IsNullOrEmpty(o.Name)));
            Assert.True(outputs.Count(o => o.IsDefault) <= 1);
            Assert.Null(AudioOutputs.ApplyChosen(new RetroboatSettings()));
        }

        [Fact]
        public void Setup_request_is_taken_once()
        {
            using (var dir = new TempDir())
            {
                var install = new RetroBatInstall(dir.Path);

                Assert.False(SetupRequest.Take(install));
                SetupRequest.Create(install);
                Assert.True(SetupRequest.Exists(install));
                Assert.True(SetupRequest.Take(install));
                Assert.False(SetupRequest.Exists(install));
            }
        }
    }
}
