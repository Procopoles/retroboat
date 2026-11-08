using System.IO;
using Retroboat.Bios;
using Retroboat.Library;
using Retroboat.Settings;
using Xunit;

namespace Retroboat.Tests
{
    public class EsBiosCheckTests
    {
        const string EsSettingsFile = @"emulationstation\.emulationstation\es_settings.cfg";
        const string Ini = "retroboat.ini";

        static string Setup(TempDir dir) =>
            dir.WriteText(EsSettingsFile, "<?xml version=\"1.0\"?>\n<config>\n\t<bool name=\"ScrapeVideos\" value=\"true\" />\n\t<string name=\"Language\" value=\"pt_BR\" />\n</config>\n");

        [Fact]
        public void Turns_the_check_off_and_keeps_the_other_settings()
        {
            using (var dir = new TempDir())
            {
                var path = Setup(dir);
                var settings = new RetroboatSettings();

                Assert.True(EsBiosCheck.TurnOffOnce(new RetroBatInstall(dir.Path), settings, dir.Combine(Ini)));

                var text = File.ReadAllText(path);
                Assert.Contains("<bool name=\"CheckBiosesAtLaunch\" value=\"false\"", text);
                Assert.Contains("<bool name=\"ScrapeVideos\" value=\"true\"", text);
                Assert.Contains("<string name=\"Language\" value=\"pt_BR\"", text);
                Assert.True(RetroboatSettings.Load(dir.Combine(Ini)).EsBiosCheckHandled);
            }
        }

        [Fact]
        public void Does_not_undo_a_user_who_turned_the_check_back_on()
        {
            using (var dir = new TempDir())
            {
                var path = Setup(dir);
                var install = new RetroBatInstall(dir.Path);
                Assert.True(EsBiosCheck.TurnOffOnce(install, new RetroboatSettings(), dir.Combine(Ini)));

                // O ES não grava valores iguais ao padrão: religar no menu tira a chave do arquivo.
                Setup(dir);
                Assert.False(EsBiosCheck.TurnOffOnce(install, RetroboatSettings.Load(dir.Combine(Ini)), dir.Combine(Ini)));

                Assert.DoesNotContain("CheckBiosesAtLaunch", File.ReadAllText(path));
            }
        }

        [Fact]
        public void Waits_for_the_es_settings_file_to_exist()
        {
            using (var dir = new TempDir())
            {
                var settings = new RetroboatSettings();

                Assert.False(EsBiosCheck.TurnOffOnce(new RetroBatInstall(dir.Path), settings, dir.Combine(Ini)));

                Assert.False(File.Exists(dir.Combine(EsSettingsFile)));
                Assert.False(settings.EsBiosCheckHandled);
            }
        }
    }
}
