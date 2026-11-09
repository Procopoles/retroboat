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
        public void Zeroed_bios_in_the_cloud_never_replaces_a_good_one_and_is_repaired_from_it()
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
                Assert.Equal(new[] { @"pcsx2\bios\" + Ps2Name }, result.Uploaded);
                Assert.Empty(result.SkippedCorrupt);
                Assert.Equal(good, File.ReadAllBytes(localFile));
                Assert.Equal(good, File.ReadAllBytes(cloudFile));
            }
        }

        [Fact]
        public void Zeroed_copy_with_the_same_size_and_date_is_repaired_on_either_side()
        {
            // Como saiu da recuperação de disco: mesmo nome, tamanho e data, mas só zeros.
            var date = new DateTime(2024, 7, 21, 9, 42, 22, DateTimeKind.Utc);
            var good = FakePs2Bios();

            using (var cloud = new TempDir())
            using (var local = new TempDir())
            {
                var cloudFile = cloud.WriteFile(@"bios\pcsx2\bios\" + Ps2Name, new byte[good.Length]);
                var localFile = local.WriteFile(@"pcsx2\bios\" + Ps2Name, good);
                File.SetLastWriteTimeUtc(cloudFile, date);
                File.SetLastWriteTimeUtc(localFile, date);

                var result = new BiosSync(cloud.Path, local.Path).Sync();

                Assert.Single(result.Uploaded);
                Assert.Empty(result.SkippedCorrupt);
                Assert.Equal(good, File.ReadAllBytes(cloudFile));
            }

            using (var cloud = new TempDir())
            using (var local = new TempDir())
            {
                var cloudFile = cloud.WriteFile(@"bios\pcsx2\bios\" + Ps2Name, good);
                var localFile = local.WriteFile(@"pcsx2\bios\" + Ps2Name, new byte[good.Length]);
                File.SetLastWriteTimeUtc(cloudFile, date);
                File.SetLastWriteTimeUtc(localFile, date);

                var result = new BiosSync(cloud.Path, local.Path).Sync();

                Assert.Single(result.Downloaded);
                Assert.Empty(result.Uploaded);
                Assert.Equal(good, File.ReadAllBytes(localFile));
            }
        }

        [Fact]
        public void Zeroed_on_both_sides_is_reported_once_and_not_copied()
        {
            using (var cloud = new TempDir())
            using (var local = new TempDir())
            {
                cloud.WriteFile(@"bios\pcsx2\bios\" + Ps2Name, new byte[4 << 20]);
                local.WriteFile(@"pcsx2\bios\" + Ps2Name, new byte[4 << 20]);

                var result = new BiosSync(cloud.Path, local.Path).Sync();

                Assert.False(result.HasTransfers);
                Assert.Single(result.SkippedCorrupt);
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

        /// <summary>Recorte do system\modules\rb_gui\bios_local.json do RetroBat.</summary>
        const string RetroBatList = @"{
  ""dreamcast"": { ""name"": ""Dreamcast"", ""biosFiles"": [
    { ""md5"": ""e10c53c2f8b90bab96ead2d368858623"", ""file"": ""bios/dc/dc_boot.bin"" },
    { ""md5"": """", ""file"": ""bios/dc/dc_flash.bin"" } ] },
  ""saturn"": { ""name"": ""Saturn"", ""biosFiles"": [
    { ""md5"": """", ""file"": ""bios/saturn_bios.bin"" },
    { ""md5"": """", ""file"": ""bios/kronos/saturn_bios.bin"" } ] },
  ""adam"": { ""name"": ""Coleco Adam"", ""biosFiles"": [
    { ""md5"": """", ""file"": ""bios/adam.zip"" },
    { ""md5"": """", ""file"": ""bios/mame/hash/adam_cart.xml"" } ] },
  ""ps2"": { ""name"": ""PlayStation 2"", ""biosFiles"": [ { ""md5"": """", ""file"": ""bios/pcsx2/bios/ps2-0230a-20080220.bin"" } ] },
  ""ecwolf"": { ""name"": ""ECWolf"", ""biosFiles"": [ { ""md5"": ""dbaa5c4e20a11000d1c4db3a2a28f374"", ""file"": ""bios/ecwolf.pk3"" } ] },
  ""triforce"": { ""name"": ""Triforce"", ""biosFiles"": [ { ""md5"": """", ""file"": ""emulators/dolphin-emu/User/Triforce/segaboot.gcm"" } ] }
}";

        static string Describe(BiosSet set) => set.System + ":" + set.Folder + ":" + string.Join(",", set.AcceptedNames);

        [Fact]
        public void RetroBat_list_gives_one_set_per_folder_without_software_lists_ps2_or_files_outside_bios()
        {
            var sets = BiosCatalog.ReadRetroBatList(RetroBatList);

            Assert.Equal(new[] { "dreamcast:dc:dc_boot.bin,dc_flash.bin", "saturn::saturn_bios.bin", @"saturn:kronos:saturn_bios.bin", "adam::adam.zip" },
                sets.Select(Describe));
            Assert.Equal("Dreamcast", sets[0].DisplayName);
        }

        [Fact]
        public void Without_a_readable_retrobat_list_only_ps2_and_ps1_are_known()
        {
            using (var retrobat = new TempDir())
            {
                Assert.Equal(new[] { BiosCatalog.Ps2, BiosCatalog.Ps1 }, BiosCatalog.ForSync(retrobat.Path));

                retrobat.WriteText(BiosCatalog.RetroBatListPath, "{ quebrado");
                Assert.Equal(new[] { BiosCatalog.Ps2, BiosCatalog.Ps1 }, BiosCatalog.ForSync(retrobat.Path));

                retrobat.WriteText(BiosCatalog.RetroBatListPath, RetroBatList);
                Assert.Equal(6, BiosCatalog.ForSync(retrobat.Path).Count);
            }
        }

        [Fact]
        public void Sync_uploads_bios_of_any_console_in_the_retrobat_list_and_nothing_else()
        {
            using (var cloud = new TempDir())
            using (var retrobat = new TempDir())
            {
                retrobat.WriteText(BiosCatalog.RetroBatListPath, RetroBatList);
                retrobat.WriteFile(@"bios\dc\dc_boot.bin", TempDir.RandomBytes(2048, seed: 1));
                retrobat.WriteFile(@"bios\saturn_bios.bin", TempDir.RandomBytes(2048, seed: 2));
                retrobat.WriteFile(@"bios\scph5501.bin", TempDir.RandomBytes(2048, seed: 3));
                retrobat.WriteText(@"bios\mame\hash\adam_cart.xml", "<softwarelist />"); // vem com o RetroBat
                retrobat.WriteText(@"bios\Databases\segadb.xml", "<db />");                // vem com o RetroBat
                retrobat.WriteFile(@"bios\ecwolf.pk3", TempDir.RandomBytes(2048, seed: 5)); // na lista, mas vem livre com o RetroBat
                retrobat.WriteFile(@"bios\outro.bin", TempDir.RandomBytes(2048, seed: 4));  // não está na lista
                var sync = new BiosSync(cloud.Path, retrobat.Combine("bios"), BiosCatalog.ForSync(retrobat.Path));

                var first = sync.Sync();
                var second = sync.Sync();

                Assert.Equal(new[] { @"dc\dc_boot.bin", "saturn_bios.bin", "scph5501.bin" }, first.Uploaded.OrderBy(f => f, StringComparer.Ordinal));
                Assert.Equal(File.ReadAllBytes(retrobat.Combine(@"bios\dc\dc_boot.bin")), File.ReadAllBytes(cloud.Combine(@"bios\dc\dc_boot.bin")));
                Assert.False(Directory.Exists(cloud.Combine(@"bios\mame")));
                Assert.False(Directory.Exists(cloud.Combine(@"bios\Databases")));
                Assert.False(File.Exists(cloud.Combine(@"bios\outro.bin")));
                Assert.False(File.Exists(cloud.Combine(@"bios\ecwolf.pk3")));
                Assert.False(second.HasTransfers);
            }
        }

        [Fact]
        public void Organize_puts_loose_bios_of_any_console_in_its_folder_and_leaves_the_ones_already_in_place()
        {
            using (var cloud = new TempDir())
            {
                cloud.WriteFile("dc_boot.bin", TempDir.RandomBytes(100, seed: 1));             // raiz do Retroboat
                cloud.WriteFile(@"bios\dc_flash.bin", TempDir.RandomBytes(100, seed: 2));      // raiz de bios, pasta errada
                cloud.WriteFile("scph5502.bin", TempDir.RandomBytes(100, seed: 3));            // PS1: vai para a raiz de bios
                cloud.WriteFile(@"bios\scph5501.bin", TempDir.RandomBytes(100, seed: 4));      // já no lugar
                cloud.WriteFile(@"bios\saturn_bios.bin", TempDir.RandomBytes(100, seed: 5));   // raiz também vale
                cloud.WriteFile("biblioteca.xml", new byte[] { 1 });

                var result = new BiosSync(cloud.Path, "", BiosCatalog.ReadRetroBatList(RetroBatList).Concat(new[] { BiosCatalog.Ps1 }).ToList()).Organize();

                Assert.Equal(new[] { @"dc\dc_boot.bin", @"dc\dc_flash.bin", "scph5502.bin" }, result.Organized.OrderBy(f => f, StringComparer.Ordinal));
                Assert.Empty(result.Errors);
                Assert.True(File.Exists(cloud.Combine(@"bios\dc\dc_boot.bin")));
                Assert.True(File.Exists(cloud.Combine(@"bios\dc\dc_flash.bin")));
                Assert.True(File.Exists(cloud.Combine(@"bios\scph5502.bin")));
                Assert.True(File.Exists(cloud.Combine(@"bios\scph5501.bin")));
                Assert.True(File.Exists(cloud.Combine(@"bios\saturn_bios.bin")));
                Assert.True(File.Exists(cloud.Combine("biblioteca.xml")));
            }
        }
    }
}
