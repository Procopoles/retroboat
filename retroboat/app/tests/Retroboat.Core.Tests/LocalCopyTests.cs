using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Retroboat.Cloud;
using Xunit;

namespace Retroboat.Tests
{
    public class LocalCopyTests
    {
        const string IsoName = "Jogo (USA).iso";

        static byte[] FakeIso(int size = 3 << 20, int seed = 7)
        {
            var data = TempDir.RandomBytes(size, seed);
            System.Text.Encoding.ASCII.GetBytes("CD001").CopyTo(data, 0x8001);
            return data;
        }

        sealed class Setup : IDisposable
        {
            public readonly TempDir Cloud = new TempDir();
            public readonly TempDir Roms = new TempDir();
            public readonly TempDir Data = new TempDir();

            public string IndexPath => Data.Combine(CloudIndex.FileName);

            public CloudGame Game(string id) => new CloudLibrary(Cloud.Combine("jogos")).Find(id);

            /// <summary>Jogo da nuvem já baixado neste PC, com a entrada no índice como a sincronização deixa.</summary>
            public CloudIndexEntry Installed(string system, string cloudName, string localName)
            {
                var game = Game(system + "/" + cloudName);
                var entry = new CloudIndexEntry
                {
                    System = system,
                    CloudName = cloudName,
                    LocalName = localName,
                    CloudSize = game.Size,
                    CloudModifiedUtc = game.LastWriteTimeUtc,
                    State = CloudGameState.Installed
                };

                var index = CloudIndex.Load(IndexPath);
                index.Set(entry);
                index.Save();
                return entry;
            }

            public void Dispose()
            {
                Cloud.Dispose();
                Roms.Dispose();
                Data.Dispose();
            }
        }

        [Fact]
        public void Uninstall_puts_the_marker_back_and_marks_the_game_as_cloud()
        {
            using (var s = new Setup())
            {
                var content = FakeIso();
                s.Cloud.WriteFile(@"jogos\ps2\" + IsoName, content);
                s.Roms.WriteFile(@"ps2\" + IsoName, content);
                s.Roms.WriteFile(@"ps2\" + IsoName + GameInstaller.PartSuffix, new byte[10]);
                var entry = s.Installed("ps2", IsoName, IsoName);

                Assert.Equal(content.Length, LocalCopy.SizeOnDisk(entry, s.Roms.Path));
                var result = LocalCopy.Uninstall(entry, s.Roms.Path, s.Game("ps2/" + IsoName), s.IndexPath);

                Assert.Equal(UninstallOutcome.Removed, result.Outcome);
                Assert.Equal(content.Length, result.BytesFreed);
                Assert.Equal(0, new FileInfo(s.Roms.Combine("ps2", IsoName)).Length);
                Assert.False(File.Exists(s.Roms.Combine("ps2", IsoName + GameInstaller.PartSuffix)));
                Assert.Equal(CloudGameState.Cloud, CloudIndex.Load(s.IndexPath).Find("ps2", IsoName).State);
                Assert.False(LocalCopy.IsInstalled(entry, s.Roms.Path));

                // O marcador é o que a sincronização e o download esperam: o jogo volta a BAIXAR.
                Assert.Equal(UninstallOutcome.NotInstalled, LocalCopy.Uninstall(entry, s.Roms.Path, s.Game("ps2/" + IsoName), s.IndexPath).Outcome);
                Assert.Equal(content, File.ReadAllBytes(s.Cloud.Combine("jogos", "ps2", IsoName)));
            }
        }

        [Fact]
        public void Uninstall_keeps_the_only_good_copy()
        {
            using (var s = new Setup())
            {
                var content = FakeIso();
                s.Cloud.WriteFile(@"jogos\ps2\" + IsoName, new byte[content.Length]); // zerado na nuvem
                s.Roms.WriteFile(@"ps2\" + IsoName, content);
                var entry = s.Installed("ps2", IsoName, IsoName);

                var zeroed = LocalCopy.Uninstall(entry, s.Roms.Path, s.Game("ps2/" + IsoName), s.IndexPath);
                var gone = LocalCopy.Uninstall(entry, s.Roms.Path, null, s.IndexPath);

                Assert.Equal(UninstallOutcome.OnlyCopy, zeroed.Outcome);
                Assert.Contains("zerado", zeroed.Detail);
                Assert.Equal(UninstallOutcome.OnlyCopy, gone.Outcome);
                Assert.Contains("não está mais na nuvem", gone.Detail);
                Assert.Equal(content, File.ReadAllBytes(s.Roms.Combine("ps2", IsoName)));
                Assert.Equal(CloudGameState.Installed, CloudIndex.Load(s.IndexPath).Find("ps2", IsoName).State);
            }
        }

        [Fact]
        public void Uninstall_of_a_folder_game_leaves_a_marker_file()
        {
            using (var s = new Setup())
            {
                s.Cloud.WriteFile(@"jogos\ps3\Jogo.ps3\PS3_GAME\USRDIR\EBOOT.BIN", TempDir.RandomBytes(5000));
                s.Roms.WriteFile(@"ps3\Jogo.ps3\PS3_GAME\USRDIR\EBOOT.BIN", TempDir.RandomBytes(5000));
                s.Roms.WriteFile(@"ps3\Jogo.ps3\PS3_GAME\PARAM.SFO", TempDir.RandomBytes(300));
                var entry = s.Installed("ps3", "Jogo.ps3", "Jogo.ps3");

                var result = LocalCopy.Uninstall(entry, s.Roms.Path, s.Game("ps3/Jogo.ps3"), s.IndexPath);

                Assert.Equal(UninstallOutcome.Removed, result.Outcome);
                Assert.Equal(5300, result.BytesFreed);
                Assert.False(Directory.Exists(s.Roms.Combine("ps3", "Jogo.ps3")));
                Assert.Equal(0, new FileInfo(s.Roms.Combine("ps3", "Jogo.ps3")).Length);
            }
        }

        [Fact]
        public void Verify_compares_a_plain_file_with_the_cloud()
        {
            using (var s = new Setup())
            {
                var content = FakeIso(5 << 20);
                s.Cloud.WriteFile(@"jogos\psx\" + IsoName, content);
                var local = s.Roms.WriteFile(@"psx\" + IsoName, content);
                var entry = s.Installed("psx", IsoName, IsoName);
                var reports = new List<double>();

                var intact = LocalCopy.Verify(entry, s.Roms.Path, s.Game("psx/" + IsoName), new SyncProgress(reports.Add));

                Assert.Equal(VerifyOutcome.Intact, intact.Outcome);
                Assert.True(intact.ComparedWithCloud);
                Assert.Equal(1.0, reports.Last(), 3);

                // Um byte trocado no último bloco, que a comparação sempre lê.
                var changed = (byte[])content.Clone();
                changed[changed.Length - 10] ^= 0xFF;
                File.WriteAllBytes(local, changed);
                var damaged = LocalCopy.Verify(entry, s.Roms.Path, s.Game("psx/" + IsoName));
                Assert.Equal(VerifyOutcome.Damaged, damaged.Outcome);
                Assert.Contains("diferente do arquivo na nuvem", damaged.Detail);

                File.WriteAllBytes(local, content.Take(content.Length - 2048).ToArray());
                var shorter = LocalCopy.Verify(entry, s.Roms.Path, s.Game("psx/" + IsoName));
                Assert.Equal(VerifyOutcome.Damaged, shorter.Outcome);
                Assert.Contains("tamanho", shorter.Detail);
            }
        }

        [Fact]
        public void Verify_of_a_disc_from_a_zip_reads_it_all_and_checks_the_crc()
        {
            using (var s = new Setup())
            {
                var content = FakeIso();
                var zip = s.Cloud.Combine(@"jogos\ps2\Jogo.zip");
                Directory.CreateDirectory(Path.GetDirectoryName(zip));
                using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
                using (var stream = archive.CreateEntry(IsoName).Open())
                    stream.Write(content, 0, content.Length);

                var local = s.Roms.WriteFile(@"ps2\" + IsoName, content);
                var entry = s.Installed("ps2", "Jogo.zip", IsoName);

                Assert.Equal(VerifyOutcome.Intact, LocalCopy.Verify(entry, s.Roms.Path, s.Game("ps2/Jogo.zip")).Outcome);

                // Um byte no meio, fora de qualquer amostra: só a leitura completa acha.
                var changed = (byte[])content.Clone();
                changed[1234567] ^= 0x01;
                File.WriteAllBytes(local, changed);
                var damaged = LocalCopy.Verify(entry, s.Roms.Path, s.Game("ps2/Jogo.zip"));

                Assert.Equal(VerifyOutcome.Damaged, damaged.Outcome);
                Assert.Contains("CRC-32", damaged.Detail);
            }
        }

        [Fact]
        public void Verify_without_the_cloud_checks_only_the_local_copy()
        {
            using (var s = new Setup())
            {
                var content = FakeIso();
                s.Cloud.WriteFile(@"jogos\ps2\" + IsoName, content);
                var local = s.Roms.WriteFile(@"ps2\" + IsoName, content);
                var entry = s.Installed("ps2", IsoName, IsoName);

                var good = LocalCopy.Verify(entry, s.Roms.Path, null);
                Assert.Equal(VerifyOutcome.Intact, good.Outcome);
                Assert.False(good.ComparedWithCloud);

                File.WriteAllBytes(local, new byte[content.Length]);
                var zeroed = LocalCopy.Verify(entry, s.Roms.Path, null);
                Assert.Equal(VerifyOutcome.Damaged, zeroed.Outcome);
                Assert.Contains("zerada", zeroed.Detail);

                File.WriteAllBytes(local, new byte[0]);
                Assert.Equal(VerifyOutcome.NotInstalled, LocalCopy.Verify(entry, s.Roms.Path, s.Game("ps2/" + IsoName)).Outcome);
            }
        }

        [Fact]
        public void Verify_of_a_folder_game_checks_the_file_list_and_ignores_extra_files()
        {
            using (var s = new Setup())
            {
                var eboot = TempDir.RandomBytes(400000);
                s.Cloud.WriteFile(@"jogos\ps3\Jogo.ps3\PS3_GAME\USRDIR\EBOOT.BIN", eboot);
                s.Cloud.WriteFile(@"jogos\ps3\Jogo.ps3\PS3_GAME\PARAM.SFO", TempDir.RandomBytes(300, seed: 1));
                s.Roms.WriteFile(@"ps3\Jogo.ps3\PS3_GAME\USRDIR\EBOOT.BIN", eboot);
                var param = s.Roms.WriteFile(@"ps3\Jogo.ps3\PS3_GAME\PARAM.SFO", TempDir.RandomBytes(300, seed: 1));
                s.Roms.WriteFile(@"ps3\Jogo.ps3\cache.bin", TempDir.RandomBytes(50)); // gravado pelo emulador
                var entry = s.Installed("ps3", "Jogo.ps3", "Jogo.ps3");

                Assert.Equal(VerifyOutcome.Intact, LocalCopy.Verify(entry, s.Roms.Path, s.Game("ps3/Jogo.ps3")).Outcome);

                File.Delete(param);
                var missing = LocalCopy.Verify(entry, s.Roms.Path, s.Game("ps3/Jogo.ps3"));
                Assert.Equal(VerifyOutcome.Damaged, missing.Outcome);
                Assert.Contains(@"falta PS3_GAME\PARAM.SFO", missing.Detail);
            }
        }

        [Fact]
        public void Sample_offsets_cover_the_start_and_the_end()
        {
            Assert.Equal(new long[] { 0 }, LocalCopy.SampleOffsets(1000));

            long length = 700L << 20;
            var offsets = LocalCopy.SampleOffsets(length);
            Assert.Equal(0, offsets.First());
            Assert.Equal(length - LocalCopy.CompareBlockSize, offsets.Last());
            Assert.True(offsets.Count <= LocalCopy.CompareSamples);
            Assert.All(offsets.Take(offsets.Count - 1), o => Assert.Equal(0, o % LocalCopy.CompareBlockSize));
        }

        sealed class SyncProgress : IProgress<double>
        {
            readonly Action<double> _report;
            public SyncProgress(Action<double> report) => _report = report;
            public void Report(double value) => _report(value);
        }
    }
}
