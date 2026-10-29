using System;
using System.IO;
using System.Linq;
using System.Text;
using Retroboat.Bios;
using Xunit;

namespace Retroboat.Tests
{
    public class BiosTests
    {
        const string Ps2Folder = @"bios\pcsx2\bios";
        const string Ps2Name = "SCPH-39001_BIOS_V7_USA_160.BIN";

        /// <summary>BIOS de PS2 sintética: ROMDIR com RESET, ROMDIR, EXTINFO e ROMVER.</summary>
        internal static byte[] FakePs2Bios(string romver = "0160AC20020207")
        {
            var data = TempDir.RandomBytes(64 * 1024, seed: 9);
            Array.Clear(data, 0x2700, data.Length - 0x2700);

            void Entry(int index, string name, uint size)
            {
                int pos = 0x2700 + index * 16;
                Encoding.ASCII.GetBytes(name).CopyTo(data, pos);
                BitConverter.GetBytes(size).CopyTo(data, pos + 12);
            }

            Entry(0, "RESET", 0x2700);
            Entry(1, "ROMDIR", 0x40);
            Entry(2, "EXTINFO", 0x10);
            Entry(3, "ROMVER", 0x10);
            Encoding.ASCII.GetBytes(romver + "\n").CopyTo(data, 0x2750);
            return data;
        }

        [Fact]
        public void Reads_version_and_region_of_a_ps2_bios()
        {
            var inspection = Ps2Bios.Inspect(FakePs2Bios());

            Assert.True(inspection.IsValid);
            Assert.Equal("v1.60, EUA, 2002-02-07", inspection.Description);
        }

        [Fact]
        public void Zeroed_or_foreign_file_is_not_a_ps2_bios()
        {
            Assert.Contains("zerado", Ps2Bios.Inspect(new byte[4 << 20]).Problem);
            Assert.Contains("ROMDIR", Ps2Bios.Inspect(TempDir.RandomBytes(4096)).Problem);
        }

        [Fact]
        public void Status_reports_ok_corrupt_missing_and_unrecognized()
        {
            using (var dir = new TempDir())
            {
                var bios = dir.Combine("bios");
                Assert.Equal(BiosState.Missing, BiosStatus.Check(bios, BiosCatalog.Ps2).State);

                dir.WriteFile(Ps2Folder + @"\minha-bios.bin", FakePs2Bios());
                var unrecognized = BiosStatus.Check(bios, BiosCatalog.Ps2);
                Assert.Equal(BiosState.Unrecognized, unrecognized.State);
                Assert.Contains("Renomeie", unrecognized.Summary);

                dir.WriteFile(Ps2Folder + @"\" + Ps2Name, new byte[4 << 20]);
                var corrupt = BiosStatus.Check(bios, BiosCatalog.Ps2);
                Assert.Equal(BiosState.Corrupt, corrupt.State);
                Assert.Equal(Ps2Name, corrupt.File);

                dir.WriteFile(Ps2Folder + @"\" + Ps2Name, FakePs2Bios());
                var ok = BiosStatus.Check(bios, BiosCatalog.Ps2);
                Assert.Equal(BiosState.Ok, ok.State);
                Assert.Contains("v1.60, EUA", ok.Summary);
            }
        }

        [Fact]
        public void Status_follows_the_emulator_choice_order()
        {
            using (var dir = new TempDir())
            {
                // O emulatorLauncher usa ps2-0230a-20080220.bin antes de qualquer outro nome.
                dir.WriteFile(Ps2Folder + @"\" + Ps2Name, FakePs2Bios());
                dir.WriteFile(Ps2Folder + @"\ps2-0230a-20080220.bin", new byte[4 << 20]);

                var report = BiosStatus.Check(dir.Combine("bios"), BiosCatalog.Ps2);

                Assert.Equal(BiosState.Corrupt, report.State);
                Assert.Equal("ps2-0230a-20080220.bin", report.File);
            }
        }

        [Fact]
        public void Organize_moves_loose_bios_and_companions_into_the_emulator_folder()
        {
            using (var cloud = new TempDir())
            {
                cloud.WriteFile(Ps2Name, FakePs2Bios());
                cloud.WriteFile("SCPH-39001_BIOS_V7_USA_160.MEC", new byte[] { 3, 6, 2, 0 });
                cloud.WriteFile(@"bios\SCPH-39001_BIOS_V7_USA_160.NVM", TempDir.RandomBytes(1024));
                cloud.WriteFile("registro.json", new byte[] { 1 });
                cloud.WriteFile(@"jogos\ps2\Jogo.iso", new byte[] { 1 });

                var result = new BiosSync(cloud.Path, cloud.Combine("local")).Organize();

                Assert.Equal(3, result.Organized.Count);
                Assert.True(File.Exists(cloud.Combine(@"bios\pcsx2\bios", Ps2Name)));
                Assert.True(File.Exists(cloud.Combine(@"bios\pcsx2\bios\SCPH-39001_BIOS_V7_USA_160.MEC")));
                Assert.True(File.Exists(cloud.Combine(@"bios\pcsx2\bios\SCPH-39001_BIOS_V7_USA_160.NVM")));
                Assert.True(File.Exists(cloud.Combine("registro.json")));
                Assert.False(File.Exists(cloud.Combine(Ps2Name)));
            }
        }

        [Fact]
        public void Sync_downloads_new_bios_and_keeps_the_cloud_date()
        {
            using (var cloud = new TempDir())
            using (var local = new TempDir())
            {
                var cloudFile = cloud.WriteFile(@"bios\pcsx2\bios\" + Ps2Name, FakePs2Bios());
                var date = new DateTime(2024, 7, 21, 9, 42, 22, DateTimeKind.Utc);
                File.SetLastWriteTimeUtc(cloudFile, date);

                var first = new BiosSync(cloud.Path, local.Path).Sync();
                var second = new BiosSync(cloud.Path, local.Path).Sync();

                var localFile = local.Combine(@"pcsx2\bios", Ps2Name);
                Assert.Equal(new[] { @"pcsx2\bios\" + Ps2Name }, first.Downloaded);
                Assert.Equal(File.ReadAllBytes(cloudFile), File.ReadAllBytes(localFile));
                Assert.Equal(date, File.GetLastWriteTimeUtc(localFile));
                Assert.False(second.HasTransfers);
                Assert.Equal(1, second.Unchanged);
            }
        }

        [Fact]
        public void Sync_uploads_bios_that_only_exists_on_this_pc()
        {
            using (var cloud = new TempDir())
            using (var local = new TempDir())
            {
                local.WriteFile(@"pcsx2\bios\" + Ps2Name, FakePs2Bios());
                local.WriteFile(@"pcsx2\bios\leia-me.txt", new byte[] { 1 });
                local.WriteFile(@"dc\dc_boot.bin", TempDir.RandomBytes(100));

                var result = new BiosSync(cloud.Path, local.Path).Sync();

                Assert.Equal(new[] { @"pcsx2\bios\" + Ps2Name }, result.Uploaded);
                Assert.True(File.Exists(cloud.Combine(@"bios\pcsx2\bios", Ps2Name)));
                Assert.False(File.Exists(cloud.Combine(@"bios\dc\dc_boot.bin")));
            }
        }

        [Fact]
        public void Zeroed_bios_is_never_copied_and_never_replaces_a_good_one()
        {
            using (var cloud = new TempDir())
            using (var local = new TempDir())
            {
                var cloudFile = cloud.WriteFile(@"bios\pcsx2\bios\" + Ps2Name, new byte[4 << 20]);
                var good = FakePs2Bios();
                var localFile = local.WriteFile(@"pcsx2\bios\" + Ps2Name, good);
                File.SetLastWriteTimeUtc(localFile, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
                File.SetLastWriteTimeUtc(cloudFile, new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));

                var result = new BiosSync(cloud.Path, local.Path).Sync();

                Assert.Empty(result.Downloaded);
                Assert.True(Assert.Single(result.SkippedCorrupt).InCloud);
                Assert.Equal(good, File.ReadAllBytes(localFile));
            }
        }

        [Fact]
        public void Newer_local_bios_replaces_the_cloud_copy()
        {
            using (var cloud = new TempDir())
            using (var local = new TempDir())
            {
                var cloudFile = cloud.WriteFile(@"bios\pcsx2\bios\" + Ps2Name, FakePs2Bios("0160AC20020207"));
                var localFile = local.WriteFile(@"pcsx2\bios\" + Ps2Name, FakePs2Bios("0170AC20030227"));
                File.SetLastWriteTimeUtc(cloudFile, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
                File.SetLastWriteTimeUtc(localFile, new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));

                var result = new BiosSync(cloud.Path, local.Path).Sync();

                Assert.Empty(result.Downloaded);
                Assert.Single(result.Uploaded);
                Assert.Equal(File.ReadAllBytes(localFile), File.ReadAllBytes(cloudFile));
            }
        }
    }
}
