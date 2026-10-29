using System.Linq;
using Retroboat.Cloud;
using Retroboat.Library;
using Xunit;

namespace Retroboat.Tests
{
    public class StartupSyncTests
    {
        [Fact]
        public void Zeroed_bios_in_the_cloud_is_explained_to_the_user()
        {
            using (var cloud = new TempDir())
            using (var retrobat = new TempDir())
            {
                cloud.WriteFile(@"jogos\ps2\Jogo.zip", TempDir.RandomBytes(1000));
                cloud.WriteFile("SCPH-39001_BIOS_V7_USA_160.BIN", new byte[4 << 20]);
                cloud.WriteFile("SCPH-39001_BIOS_V7_USA_160.NVM", new byte[1024]);

                var report = StartupSync.Run(new RetroBatInstall(retrobat.Path), cloud.Path, retrobat.Combine("roms"));

                Assert.True(report.HasWarnings);
                Assert.Contains(report.Messages, m => m.StartsWith("Nuvem: BIOS sincronizadas") && m.Contains("2 organizada(s)"));
                Assert.Contains(report.Messages, m => m.Contains("a BIOS do Drive (SCPH-39001_BIOS_V7_USA_160.BIN) está zerada"));
                Assert.DoesNotContain(report.Messages, m => m.Contains("não copiados"));
            }
        }

        [Fact]
        public void Valid_bios_is_downloaded_and_reported_ok()
        {
            using (var cloud = new TempDir())
            using (var retrobat = new TempDir())
            {
                cloud.WriteFile(@"jogos\ps2\Jogo.zip", TempDir.RandomBytes(1000));
                cloud.WriteFile(@"bios\pcsx2\bios\SCPH-39001_BIOS_V7_USA_160.BIN", BiosTests.FakePs2Bios());

                var report = StartupSync.Run(new RetroBatInstall(retrobat.Path), cloud.Path, retrobat.Combine("roms"));

                Assert.False(report.HasWarnings);
                Assert.Equal(new[]
                {
                    "Nuvem: BIOS sincronizadas (1 baixada(s))",
                    "Biblioteca na nuvem: 0 instalado(s), 1 para baixar ao jogar",
                    "PlayStation 2: BIOS ok (v1.60, EUA, 2002-02-07)"
                }, report.Messages);
            }
        }

        [Fact]
        public void Systems_without_games_do_not_produce_bios_warnings()
        {
            using (var cloud = new TempDir())
            using (var retrobat = new TempDir())
            {
                cloud.WriteFile(@"jogos\snes\Jogo.sfc", TempDir.RandomBytes(1000));

                var report = StartupSync.Run(new RetroBatInstall(retrobat.Path), cloud.Path, retrobat.Combine("roms"));

                Assert.False(report.HasWarnings);
                Assert.Equal(new[] { "Nuvem: BIOS em dia", "Biblioteca na nuvem: 0 instalado(s), 1 para baixar ao jogar" }, report.Messages.ToArray());
            }
        }
    }
}
