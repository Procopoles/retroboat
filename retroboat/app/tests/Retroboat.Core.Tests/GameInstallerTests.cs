using System;
using System.IO;
using System.Linq;
using System.Threading;
using Retroboat.Cloud;
using Retroboat.Integrity;
using Retroboat.Transfers;
using Xunit;

namespace Retroboat.Tests
{
    public class GameInstallerTests
    {
        const int GameSize = 3 << 20;

        [Fact]
        public void Installs_a_copy_with_the_same_content_and_date()
        {
            using (var cloud = new TempDir())
            using (var roms = new TempDir())
            {
                var content = TempDir.RandomBytes(GameSize);
                var source = cloud.WriteFile("jogos/snes/Jogo (USA).sfc", content);
                var sourceDate = new DateTime(2024, 5, 25, 19, 19, 39, DateTimeKind.Utc);
                File.SetLastWriteTimeUtc(source, sourceDate);
                var game = new CloudLibrary(cloud.Combine("jogos")).Find("snes/Jogo (USA).sfc");

                var result = new GameInstaller().Install(game, roms.Path);

                Assert.Equal(InstallOutcome.Installed, result.Outcome);
                Assert.Equal(roms.Combine("snes", "Jogo (USA).sfc"), result.Destination);
                Assert.Equal(content, File.ReadAllBytes(result.Destination));
                Assert.Equal(sourceDate, File.GetLastWriteTimeUtc(result.Destination));
                Assert.False(File.Exists(result.Destination + GameInstaller.PartSuffix));
                Assert.Equal(GameSize, result.BytesCopied);
            }
        }

        [Fact]
        public void Second_install_is_skipped()
        {
            using (var cloud = new TempDir())
            using (var roms = new TempDir())
            {
                cloud.WriteFile("jogos/snes/Jogo.sfc", TempDir.RandomBytes(1000));
                var game = new CloudLibrary(cloud.Combine("jogos")).Find("snes/Jogo.sfc");
                var installer = new GameInstaller();

                installer.Install(game, roms.Path);
                var second = installer.Install(game, roms.Path);

                Assert.Equal(InstallOutcome.AlreadyInstalled, second.Outcome);
            }
        }

        [Fact]
        public void Resumes_from_a_partial_copy()
        {
            using (var cloud = new TempDir())
            using (var roms = new TempDir())
            {
                var content = TempDir.RandomBytes(GameSize);
                cloud.WriteFile("jogos/psx/Jogo.chd", content);
                roms.WriteFile("psx/Jogo.chd" + GameInstaller.PartSuffix, content.Take(1 << 20).ToArray());
                var game = new CloudLibrary(cloud.Combine("jogos")).Find("psx/Jogo.chd");

                var result = new GameInstaller { BufferSize = 64 * 1024 }.Install(game, roms.Path);

                Assert.Equal(InstallOutcome.Installed, result.Outcome);
                Assert.Equal(1 << 20, result.BytesResumed);
                Assert.Equal(GameSize - (1 << 20), result.BytesCopied);
                Assert.Equal(content, File.ReadAllBytes(result.Destination));
            }
        }

        [Fact]
        public void Cancelled_install_keeps_the_partial_copy_and_continues_later()
        {
            using (var cloud = new TempDir())
            using (var roms = new TempDir())
            {
                var content = TempDir.RandomBytes(GameSize);
                cloud.WriteFile("jogos/psx/Jogo.chd", content);
                var game = new CloudLibrary(cloud.Combine("jogos")).Find("psx/Jogo.chd");
                var installer = new GameInstaller { BufferSize = 64 * 1024 };

                using (var cancel = new CancellationTokenSource())
                {
                    var first = installer.Install(game, roms.Path, new CancelOnFirstReport(cancel), cancel.Token);

                    Assert.Equal(InstallOutcome.Cancelled, first.Outcome);
                    Assert.False(File.Exists(roms.Combine("psx", "Jogo.chd")));
                    var partLength = new FileInfo(roms.Combine("psx", "Jogo.chd" + GameInstaller.PartSuffix)).Length;
                    Assert.InRange(partLength, 1, GameSize - 1);
                }

                var second = installer.Install(game, roms.Path);

                Assert.Equal(InstallOutcome.Installed, second.Outcome);
                Assert.True(second.BytesResumed > 0);
                Assert.Equal(content, File.ReadAllBytes(second.Destination));
            }
        }

        [Fact]
        public void Read_aborted_by_the_system_is_an_interruption_and_resumes_later()
        {
            using (var cloud = new TempDir())
            using (var roms = new TempDir())
            {
                var content = TempDir.RandomBytes(GameSize);
                cloud.WriteFile("jogos/psx/Jogo.chd", content);
                var game = new CloudLibrary(cloud.Combine("jogos")).Find("psx/Jogo.chd");
                var installer = new GameInstaller { BufferSize = 64 * 1024 };

                // O Drive reiniciou: o Windows aborta a leitura e o .NET lança OperationCanceledException sem token.
                var first = installer.Install(game, roms.Path, new AbortOnFirstReport(), CancellationToken.None);

                Assert.Equal(InstallOutcome.Interrupted, first.Outcome);
                Assert.Contains("interrompida", first.Error);
                Assert.InRange(new FileInfo(roms.Combine("psx", "Jogo.chd" + GameInstaller.PartSuffix)).Length, 1, GameSize - 1);

                var second = installer.Install(game, roms.Path);

                Assert.Equal(InstallOutcome.Installed, second.Outcome);
                Assert.True(second.BytesResumed > 0);
                Assert.Equal(content, File.ReadAllBytes(second.Destination));
            }
        }

        [Fact]
        public void Oversized_partial_copy_is_discarded()
        {
            using (var cloud = new TempDir())
            using (var roms = new TempDir())
            {
                var content = TempDir.RandomBytes(1000);
                cloud.WriteFile("jogos/snes/Jogo.sfc", content);
                roms.WriteFile("snes/Jogo.sfc" + GameInstaller.PartSuffix, TempDir.RandomBytes(5000, seed: 7));
                var game = new CloudLibrary(cloud.Combine("jogos")).Find("snes/Jogo.sfc");

                var result = new GameInstaller().Install(game, roms.Path);

                Assert.Equal(InstallOutcome.Installed, result.Outcome);
                Assert.Equal(0, result.BytesResumed);
                Assert.Equal(content, File.ReadAllBytes(result.Destination));
            }
        }

        [Fact]
        public void Installs_folder_games_with_all_files()
        {
            using (var cloud = new TempDir())
            using (var roms = new TempDir())
            {
                cloud.WriteFile("jogos/ps3/Jogo.ps3/PS3_GAME/USRDIR/EBOOT.BIN", TempDir.RandomBytes(5000));
                cloud.WriteFile("jogos/ps3/Jogo.ps3/PS3_DISC.SFB", TempDir.RandomBytes(100, seed: 3));
                var game = new CloudLibrary(cloud.Combine("jogos")).Find("ps3/Jogo.ps3");

                var result = new GameInstaller().Install(game, roms.Path);

                Assert.Equal(InstallOutcome.Installed, result.Outcome);
                Assert.True(File.Exists(roms.Combine("ps3", "Jogo.ps3", "PS3_GAME", "USRDIR", "EBOOT.BIN")));
                Assert.True(File.Exists(roms.Combine("ps3", "Jogo.ps3", "PS3_DISC.SFB")));
                Assert.Equal(5100, result.BytesTotal);
            }
        }

        [Fact]
        public void Zeroed_game_is_installed_but_flagged()
        {
            using (var cloud = new TempDir())
            using (var roms = new TempDir())
            {
                cloud.WriteFile("jogos/ps2/Jogo.iso", new byte[1 << 20]);
                var game = new CloudLibrary(cloud.Combine("jogos")).Find("ps2/Jogo.iso");

                var result = new GameInstaller().Install(game, roms.Path);

                Assert.Equal(InstallOutcome.Installed, result.Outcome);
                Assert.Equal(IntegrityStatus.Zeroed, result.Integrity.Status);
            }
        }

        [Fact]
        public void Progress_reaches_the_total()
        {
            using (var cloud = new TempDir())
            using (var roms = new TempDir())
            {
                cloud.WriteFile("jogos/snes/Jogo.sfc", TempDir.RandomBytes(GameSize));
                var game = new CloudLibrary(cloud.Combine("jogos")).Find("snes/Jogo.sfc");
                var progress = new LastReport();

                new GameInstaller { BufferSize = 64 * 1024 }.Install(game, roms.Path, progress);

                Assert.Equal(GameSize, progress.Last.BytesDone);
                Assert.Equal(GameSize, progress.Last.BytesTotal);
            }
        }

        [Fact]
        public void Uninstall_removes_local_copy_and_partial_file()
        {
            using (var cloud = new TempDir())
            using (var roms = new TempDir())
            {
                cloud.WriteFile("jogos/snes/Jogo.sfc", TempDir.RandomBytes(1000));
                roms.WriteFile("snes/Jogo.sfc", TempDir.RandomBytes(1000));
                roms.WriteFile("snes/Jogo.sfc" + GameInstaller.PartSuffix, TempDir.RandomBytes(10));
                var game = new CloudLibrary(cloud.Combine("jogos")).Find("snes/Jogo.sfc");

                Assert.True(GameInstaller.Uninstall(game, roms.Path));
                Assert.False(File.Exists(roms.Combine("snes", "Jogo.sfc")));
                Assert.False(File.Exists(roms.Combine("snes", "Jogo.sfc" + GameInstaller.PartSuffix)));
                Assert.True(File.Exists(game.FullPath));
            }
        }

        sealed class CancelOnFirstReport : IProgress<TransferProgress>
        {
            readonly CancellationTokenSource _cancel;

            public CancelOnFirstReport(CancellationTokenSource cancel)
            {
                _cancel = cancel;
            }

            public void Report(TransferProgress value) => _cancel.Cancel();
        }

        internal sealed class AbortOnFirstReport : IProgress<TransferProgress>
        {
            public void Report(TransferProgress value) => throw new OperationCanceledException();
        }

        sealed class LastReport : IProgress<TransferProgress>
        {
            public TransferProgress Last { get; private set; }

            public void Report(TransferProgress value) => Last = value;
        }
    }
}
