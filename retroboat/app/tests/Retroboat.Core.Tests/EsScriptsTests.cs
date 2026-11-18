using System.IO;
using Retroboat.Es;
using Retroboat.Library;
using Xunit;

namespace Retroboat.Tests
{
    public class EsScriptsTests
    {
        [Fact]
        public void Start_script_opens_the_xbox_button_watcher_without_making_the_es_wait()
        {
            using (var dir = new TempDir())
            {
                dir.WriteText("retrobat.exe", "");
                dir.WriteText(Path.Combine("retroboat", "Retroboat.exe"), "");
                var install = new RetroBatInstall(dir.Path);

                Assert.True(EsScripts.WriteSteamSwitch(install, dir.Combine("retroboat")));
                Assert.False(EsScripts.WriteSteamSwitch(install, dir.Combine("retroboat"))); // já em dia

                var path = Path.Combine(install.UserEmulationStationPath, "scripts", "start", "retroboat-botao-xbox-nowait.bat");
                var script = File.ReadAllText(path);

                // "-nowait" no nome: o ES roda o script numa thread; "start" devolve o controle na hora.
                Assert.EndsWith("-nowait.bat", path);
                Assert.Contains("start \"\" \"%~dp0..\\..\\..\\..\\retroboat\\Retroboat.exe\" --steam-switch --retrobat \"%~dp0..\\..\\..\\..\"", script);
            }
        }

        [Fact]
        public void Start_script_resumes_background_media_only_when_the_queue_exists()
        {
            using (var dir = new TempDir())
            {
                dir.WriteText("retrobat.exe", "");
                dir.WriteText(Path.Combine("retroboat", "Retroboat.exe"), "");
                var install = new RetroBatInstall(dir.Path);

                Assert.True(EsScripts.WriteBackgroundMedia(install, dir.Combine("retroboat")));
                Assert.False(EsScripts.WriteBackgroundMedia(install, dir.Combine("retroboat")));

                var path = Path.Combine(install.UserEmulationStationPath, "scripts", "start", "retroboat-midias-nowait.bat");
                var script = File.ReadAllText(path);

                // A fila vazia apaga o arquivo: o script não abre nada sem mídias pendentes.
                Assert.Contains("if exist \"%~dp0..\\..\\..\\..\\retroboat\\estado\\midias-em-segundo-plano.xml\"", script);
                Assert.Contains("start \"\" \"%~dp0..\\..\\..\\..\\retroboat\\Retroboat.exe\" --media --retrobat \"%~dp0..\\..\\..\\..\"", script);
            }
        }

        [Fact]
        public void Start_script_is_not_written_without_the_launcher()
        {
            using (var dir = new TempDir())
            {
                dir.WriteText("retrobat.exe", "");
                var install = new RetroBatInstall(dir.Path);

                Assert.False(EsScripts.WriteSteamSwitch(install, dir.Combine("retroboat")));
            }
        }
    }
}
