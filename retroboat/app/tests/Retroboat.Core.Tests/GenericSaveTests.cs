using System;
using System.IO;
using System.Linq;
using Retroboat.Saves;
using Xunit;

namespace Retroboat.Tests
{
    /// <summary>A regra genérica de saves: a pasta saves\&lt;sistema&gt; inteira, de qualquer emulador.</summary>
    public class GenericSaveTests
    {
        sealed class TwoPcs : IDisposable
        {
            public readonly TempDir Cloud = new TempDir();
            public readonly TempDir A = new TempDir();
            public readonly TempDir B = new TempDir();
            public DateTime Now = new DateTime(2026, 10, 25, 18, 0, 0, DateTimeKind.Utc);
            public readonly SaveRule Rule;

            public TwoPcs(string system) => Rule = SaveRules.Generic(system);

            public SaveSync PcA() => new SaveSync(A.Combine("saves"), A.Combine("retroboat"), Cloud.Path, Rule, "PC-A", () => Now);
            public SaveSync PcB() => new SaveSync(B.Combine("saves"), B.Combine("retroboat"), Cloud.Path, Rule, "PC-B", () => Now);

            public string LocalA(string name) => A.Combine("saves", Rule.Folder, name);
            public string LocalB(string name) => B.Combine("saves", Rule.Folder, name);

            public void Write(string path, byte[] data)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, data);
                Now = Now.AddMinutes(1);
            }

            /// <summary>Um arquivo que veio com o RetroBat: data de antes do Retroboat começar a sincronizar neste PC.</summary>
            public void Shipped(string path, byte[] data)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, data);
                File.SetLastWriteTimeUtc(path, new DateTime(2024, 2, 24, 22, 0, 0, DateTimeKind.Utc));
            }

            public void Dispose()
            {
                Cloud.Dispose();
                A.Dispose();
                B.Dispose();
            }
        }

        static byte[] Data(int seed, int length = 4096) => TempDir.RandomBytes(length, seed);

        [Fact]
        public void PlayStation_saves_memory_cards_and_save_states_of_any_emulator_reach_the_other_pc()
        {
            using (var pcs = new TwoPcs("psx"))
            {
                var names = new[] { @"mednafen\Final Fantasy VII (USA).srm", @"mednafen\sstates\Final Fantasy VII (USA).state1", @"duckstation\memcards\Final Fantasy VII (USA)_1.mcd" };
                for (int i = 0; i < names.Length; i++)
                    pcs.Write(pcs.LocalA(names[i]), Data(i + 1));

                var sent = pcs.PcA().Sync();
                Assert.Equal(names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase), sent.Uploaded.OrderBy(n => n, StringComparer.OrdinalIgnoreCase));
                Assert.True(Directory.Exists(pcs.Cloud.Combine(@"saves\psx\mednafen\Final Fantasy VII (USA).srm")));

                var received = pcs.PcB().Sync();
                Assert.Equal(3, received.Downloaded.Count);
                for (int i = 0; i < names.Length; i++)
                    Assert.Equal(Data(i + 1), File.ReadAllBytes(pcs.LocalB(names[i])));

                Assert.False(pcs.PcA().Sync().HasTransfers);
                Assert.False(pcs.PcB().Sync().HasTransfers);
            }
        }

        [Fact]
        public void GameCube_saves_deep_in_the_dolphin_folder_and_dreamcast_vmus_travel()
        {
            using (var gc = new TwoPcs("gamecube"))
            using (var dc = new TwoPcs("dreamcast"))
            {
                var gci = @"dolphin-emu\User\GC\USA\Card A\01-GALE-SuperSmashBros0110290334.gci";
                gc.Write(gc.LocalA(gci), Data(1));
                gc.PcA().Sync();
                gc.PcB().Sync();
                Assert.Equal(Data(1), File.ReadAllBytes(gc.LocalB(gci)));

                var vmu = @"flycast\vmu\vmu_save_A1.bin";
                dc.Write(dc.LocalA(vmu), Data(2, 128 * 1024));
                dc.PcA().Sync();
                dc.PcB().Sync();
                Assert.Equal(Data(2, 128 * 1024), File.ReadAllBytes(dc.LocalB(vmu)));

                // Mudou no B, vai para o A.
                dc.Write(dc.LocalB(vmu), Data(3, 128 * 1024));
                dc.PcB().Sync();
                dc.PcA().Sync();
                Assert.Equal(Data(3, 128 * 1024), File.ReadAllBytes(dc.LocalA(vmu)));
            }
        }

        [Fact]
        public void Files_that_came_with_retrobat_never_go_up_and_a_new_pc_takes_the_cloud_copy_without_asking()
        {
            using (var pcs = new TwoPcs("naomi"))
            {
                var eeprom = @"reicast\18wheelr.zip.eeprom";
                var other = @"reicast\alienfnt.zip.eeprom";
                pcs.Shipped(pcs.LocalA(eeprom), Data(1, 128));
                pcs.Shipped(pcs.LocalA(other), Data(2, 128));
                pcs.Shipped(pcs.LocalB(eeprom), Data(1, 128));

                Assert.Empty(pcs.PcA().Sync().Uploaded);
                Assert.Empty(pcs.PcB().Sync().Uploaded);
                Assert.False(Directory.Exists(pcs.Cloud.Combine(@"saves\naomi\reicast")));

                // Jogou no A: a EEPROM mudou e vai. O B troca a de fábrica sem conflito, e a dele fica no histórico.
                pcs.Write(pcs.LocalA(eeprom), Data(3, 128));
                Assert.Equal(new[] { eeprom }, pcs.PcA().Sync().Uploaded);
                var received = pcs.PcB().Sync();
                Assert.Equal(new[] { eeprom }, received.Downloaded);
                Assert.Empty(received.Conflicts);
                Assert.Equal(Data(3, 128), File.ReadAllBytes(pcs.LocalB(eeprom)));
                Assert.Single(Directory.GetFiles(pcs.B.Combine(@"retroboat\historico\saves\naomi\reicast\18wheelr.zip.eeprom")));
                Assert.False(File.Exists(pcs.LocalB(other)));
            }
        }

        [Fact]
        public void A_save_changed_on_both_pcs_is_a_conflict_like_the_memory_cards()
        {
            using (var pcs = new TwoPcs("psx"))
            {
                var srm = @"mednafen\Game.srm";
                pcs.Write(pcs.LocalA(srm), Data(1));
                pcs.PcA().Sync();
                pcs.PcB().Sync();

                pcs.Write(pcs.LocalA(srm), Data(2));
                pcs.Write(pcs.LocalB(srm), Data(3));
                pcs.PcA().Sync();

                var plan = Assert.Single(pcs.PcB().Plan());
                Assert.Equal(SaveAction.Conflict, plan.Action);
                Assert.Equal(srm, plan.Name);
            }
        }

        [Fact]
        public void Folders_of_other_rules_firmware_caches_and_huge_files_stay_out()
        {
            using (var pcs = new TwoPcs("ps2"))
            {
                pcs.Write(pcs.LocalA(@"pcsx2\memcards\Mcd001.ps2"), Data(1));   // regra própria dos memory cards
                pcs.Write(pcs.LocalA(@"pcsx2\covers\SLUS-20312.jpg"), Data(2));
                pcs.Write(pcs.LocalA(@"pcsx2\cache\x.bin"), Data(3));
                pcs.Write(pcs.LocalA(@"pcsx2\sstates\SLUS-20312 (8B52C23E).01.p2s"), Data(4));

                var sent = pcs.PcA().Sync();

                Assert.Equal(new[] { @"pcsx2\sstates\SLUS-20312 (8B52C23E).01.p2s" }, sent.Uploaded);
                Assert.False(Directory.Exists(pcs.Cloud.Combine(@"saves\ps2\pcsx2\memcards")));
            }

            using (var pcs = new TwoPcs("psx"))
            {
                pcs.Rule.MaxFileBytes = 1000;
                pcs.Write(pcs.LocalA(@"mednafen\small.srm"), Data(1, 512));
                pcs.Write(pcs.LocalA(@"mednafen\big.state"), Data(2, 2000));

                Assert.Equal(new[] { @"mednafen\small.srm" }, pcs.PcA().Sync().Uploaded);
            }
        }

        [Fact]
        public void The_cloud_keeps_the_last_ten_versions_and_each_pc_prunes_only_its_own()
        {
            using (var pcs = new TwoPcs("psx"))
            {
                var state = @"mednafen\sstates\Game.state";
                pcs.Write(pcs.LocalB(state), Data(100));
                pcs.PcB().Sync();
                pcs.PcA().Sync();

                for (int i = 1; i <= 14; i++)
                {
                    pcs.Write(pcs.LocalA(state), Data(i));
                    pcs.PcA().Sync();
                }

                var versions = Directory.GetFiles(pcs.Cloud.Combine(@"saves\psx\mednafen\sstates\Game.state")).Select(SaveVersion.TryParse).Where(v => v != null).ToList();
                Assert.Equal(11, versions.Count); // as 10 mais novas do A e a do B, que só o B apaga
                Assert.Single(versions, v => v.Pc == "PC-B");
                Assert.Equal(Data(14), File.ReadAllBytes(pcs.LocalA(state)));

                pcs.PcB().Sync();
                Assert.Equal(Data(14), File.ReadAllBytes(pcs.LocalB(state)));
            }
        }

        [Fact]
        public void A_save_created_during_the_game_goes_up_with_its_folder()
        {
            using (var pcs = new TwoPcs("dreamcast"))
            {
                pcs.PcA().Sync();
                var session = new SaveSession(pcs.PcA(), utcNow: () => pcs.Now);
                session.Start("Crazy Taxi");
                Assert.NotNull(pcs.PcB().FindActiveLock());

                var vmu = @"flycast\vmu\Crazy Taxi.A1.bin";
                pcs.Write(pcs.LocalA(vmu), Data(1, 1024));
                session.Poll();
                pcs.Now = pcs.Now.AddSeconds(5);
                Assert.Equal(new[] { vmu }, session.Poll().Uploaded);

                session.Finish();
                Assert.Null(pcs.PcB().FindActiveLock());
                pcs.PcB().Sync();
                Assert.Equal(Data(1, 1024), File.ReadAllBytes(pcs.LocalB(vmu)));
            }
        }

        [Fact]
        public void A_save_deleted_outside_the_game_comes_back_and_cloud_folders_outside_the_rule_are_ignored()
        {
            using (var pcs = new TwoPcs("psx"))
            {
                var srm = @"mednafen\Game.srm";
                pcs.Write(pcs.LocalA(srm), Data(1));
                pcs.PcA().Sync();

                File.Delete(pcs.LocalA(srm));
                pcs.PcA().Sync();
                Assert.Equal(Data(1), File.ReadAllBytes(pcs.LocalA(srm)));

                // Algo na nuvem dentro de uma pasta que nunca é save (ou da trava) não vira arquivo local.
                var version = SaveVersion.NewId(pcs.Now, "PC-X") + SaveVersion.DataExtension;
                Directory.CreateDirectory(pcs.Cloud.Combine(@"saves\psx\bios\scph1001.bin"));
                File.Copy(Directory.GetFiles(pcs.Cloud.Combine(@"saves\psx\mednafen\Game.srm")).First(), pcs.Cloud.Combine(@"saves\psx\bios\scph1001.bin", version));

                pcs.PcB().Sync();
                Assert.True(File.Exists(pcs.LocalB(srm)));
                Assert.False(File.Exists(pcs.LocalB(@"bios\scph1001.bin")));
            }
        }

        [Fact]
        public void Every_system_folder_gets_the_generic_rule_except_the_ones_with_installed_games_or_keys()
        {
            using (var local = new TempDir())
            using (var cloud = new TempDir())
            {
                foreach (var system in new[] { "psx", "gamecube", "switch", "ps3" })
                    Directory.CreateDirectory(local.Combine(system));
                Directory.CreateDirectory(cloud.Combine("dreamcast"));

                var rules = SaveRules.AllFor(local.Path, cloud.Path);

                Assert.Equal(new[] { @"ps2\pcsx2\memcards", "dreamcast", "gamecube", "psx" }, rules.Select(r => r.Folder));
                Assert.Equal(new[] { @"pcsx2\memcards" }, SaveRules.Generic("ps2").ExcludedFolders);
                Assert.Equal("PlayStation", SaveRules.Generic("psx").DisplayName);
            }
        }
    }
}
