using System;
using System.IO;
using System.Linq;
using System.Text;
using Retroboat.Settings;
using Xunit;

namespace Retroboat.Tests
{
    public class RetroBatIniTests
    {
        const string Original =
            "; RETROBAT GLOBAL CONFIG FILE\r\n" +
            "\r\n" +
            "[SplashScreen]\r\n" +
            "; Monitor do vídeo de abertura? Não: este MonitorIndex é de outra seção.\r\n" +
            "MonitorIndex=7\r\n" +
            "\r\n" +
            "[EmulationStation]\r\n" +
            "Fullscreen=1\r\n" +
            "; Set to which monitor index the frontend will be displayed.\r\n" +
            "MonitorIndex=0\r\n" +
            "\r\n" +
            "; Disable to disable VSync in RetroBat interface.\r\n" +
            "VSync=1\r\n" +
            "AppLauncher=\"C:\\Jogos\\Ferramentas\\app.exe\"\r\n";

        [Fact]
        public void Only_the_EmulationStation_monitor_line_changes()
        {
            using (var dir = new TempDir())
            {
                var path = dir.Combine("retrobat.ini");
                File.WriteAllBytes(path, new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes(Original)).ToArray());

                Assert.True(RetroBatIni.SetMonitorIndex(path, 2));

                var bytes = File.ReadAllBytes(path);
                Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3));
                Assert.Equal(Original.Replace("MonitorIndex=0", "MonitorIndex=2"), Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3));

                // A mesma escolha de novo não regrava o arquivo.
                var written = File.GetLastWriteTimeUtc(path);
                File.SetLastWriteTimeUtc(path, written.AddMinutes(-5));
                Assert.False(RetroBatIni.SetMonitorIndex(path, 2));
                Assert.Equal(written.AddMinutes(-5), File.GetLastWriteTimeUtc(path));

                Assert.True(RetroBatIni.SetMonitorIndex(path, 0));
                Assert.Equal(Original, Encoding.UTF8.GetString(File.ReadAllBytes(path), 3, bytes.Length - 3));
            }
        }

        [Fact]
        public void Missing_key_or_section_is_added_without_touching_the_rest()
        {
            using (var dir = new TempDir())
            {
                var path = dir.WriteText("retrobat.ini", "[EmulationStation]\nFullscreen=1\n\n[Outra]\nChave=1\n");
                RetroBatIni.SetMonitorIndex(path, 1);
                Assert.Equal("[EmulationStation]\nFullscreen=1\nMonitorIndex=1\n\n[Outra]\nChave=1\n", File.ReadAllText(path));

                path = dir.WriteText("sem-secao.ini", "[RetroBat]\r\nAutostart=0");
                RetroBatIni.SetMonitorIndex(path, 3);
                Assert.Equal("[RetroBat]\r\nAutostart=0\r\n\r\n[EmulationStation]\r\nMonitorIndex=3\r\n", File.ReadAllText(path));
            }
        }

        [Fact]
        public void Never_creates_the_file_nor_writes_an_impossible_monitor()
        {
            using (var dir = new TempDir())
            {
                var missing = dir.Combine("retrobat.ini");
                Assert.Throws<FileNotFoundException>(() => RetroBatIni.SetMonitorIndex(missing, 1));
                Assert.False(File.Exists(missing));

                var path = dir.WriteText("retrobat.ini", Original);
                Assert.Throws<ArgumentOutOfRangeException>(() => RetroBatIni.SetMonitorIndex(path, -1));
                Assert.Throws<ArgumentOutOfRangeException>(() => RetroBatIni.SetMonitorIndex(path, RetroBatIni.MaxMonitorIndex + 1));
                Assert.Equal(Original, File.ReadAllText(path));
            }
        }

        [Fact]
        public void Monitor_is_read_from_the_EmulationStation_section_only()
        {
            using (var dir = new TempDir())
            {
                var path = dir.WriteText("retrobat.ini", Original.Replace("MonitorIndex=0", "MonitorIndex=2"));
                Assert.Equal(2, RetroBatIni.GetMonitorIndex(path)); // o 7 do [SplashScreen] não conta

                Assert.Equal(0, RetroBatIni.GetMonitorIndex(dir.WriteText("ruim.ini", "[EmulationStation]\nMonitorIndex=abc\n")));
                Assert.Equal(0, RetroBatIni.GetMonitorIndex(dir.Combine("nao-existe.ini")));
            }
        }

        [Fact]
        public void Es_monitor_number_follows_the_sdl_order()
        {
            // O Windows lista DISPLAY2, DISPLAY1, DISPLAY3, e o principal é o DISPLAY3: o ES conta o principal primeiro e
            // depois os outros na ordem dos adaptadores.
            var names = new[] { @"\\.\DISPLAY2", @"\\.\DISPLAY1", @"\\.\DISPLAY3" };
            Assert.Equal(2, RetroBatIni.ScreenFor(0, names, primary: 2));
            Assert.Equal(1, RetroBatIni.ScreenFor(1, names, primary: 2));
            Assert.Equal(0, RetroBatIni.ScreenFor(2, names, primary: 2));
            Assert.Equal(-1, RetroBatIni.ScreenFor(3, names, primary: 2)); // monitor desligado: fica o principal

            // O PC do usuário: notebook (DISPLAY1, principal) e o 4K (DISPLAY2).
            Assert.Equal(1, RetroBatIni.ScreenFor(1, new[] { @"\\.\DISPLAY1", @"\\.\DISPLAY2" }, primary: 0));
        }
    }
}
