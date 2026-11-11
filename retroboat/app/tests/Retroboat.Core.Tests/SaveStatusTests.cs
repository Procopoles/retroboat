using System;
using System.IO;
using System.Linq;
using Retroboat.Library;
using Retroboat.Saves;
using Xunit;

namespace Retroboat.Tests
{
    /// <summary>ESTADO DA NUVEM na página do jogo: o estado de cada save, por jogo, para o ES.</summary>
    public class SaveStatusTests
    {
        const string Game = "Legacy of Kain - Soul Reaver (USA)";

        sealed class TwoPcs : IDisposable
        {
            public readonly TempDir Cloud = new TempDir();
            public readonly TempDir A = new TempDir();
            public readonly TempDir B = new TempDir();
            public DateTime Now = new DateTime(2026, 11, 10, 23, 0, 0, DateTimeKind.Utc);
            public readonly SaveRule Rule;

            public TwoPcs(SaveRule rule) => Rule = rule;

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

            public void Dispose()
            {
                Cloud.Dispose();
                A.Dispose();
                B.Dispose();
            }
        }

        static byte[] Data(int seed, int length = 4096) => TempDir.RandomBytes(length, seed);

        [Fact]
        public void A_save_sent_to_the_cloud_is_synced_with_the_time_and_pc_of_the_version()
        {
            using (var pcs = new TwoPcs(SaveRules.Generic("psx")))
            {
                pcs.PcA().Plan(); // a regra começa a valer neste PC
                pcs.Write(pcs.LocalA(Game + ".srm"), Data(1));
                var sentAt = pcs.Now;
                pcs.PcA().Sync();

                var item = Assert.Single(pcs.PcA().ItemStates());
                Assert.Equal(SaveCloudState.Synced, item.State);
                Assert.Equal("PC-A", item.Pc);
                Assert.Equal(sentAt, item.TimeUtc);
            }
        }

        [Fact]
        public void A_save_made_without_the_drive_is_pending_until_it_reaches_the_cloud()
        {
            using (var pcs = new TwoPcs(SaveRules.Generic("psx")))
            {
                pcs.PcA().Plan();
                pcs.Write(pcs.LocalA(Game + ".srm"), Data(1));
                pcs.PcA().Sync();

                // Jogou com o Drive fechado: o arquivo mudou e nada foi para a nuvem.
                pcs.Write(pcs.LocalA(Game + ".srm"), Data(2));
                Assert.Equal(SaveCloudState.Pending, Assert.Single(pcs.PcA().ItemStates()).State);

                pcs.PcA().Sync();
                Assert.Equal(SaveCloudState.Synced, Assert.Single(pcs.PcA().ItemStates()).State);
            }
        }

        [Fact]
        public void A_file_touched_without_changing_is_still_synced()
        {
            using (var pcs = new TwoPcs(SaveRules.Generic("psx")))
            {
                pcs.PcA().Plan();
                pcs.Write(pcs.LocalA(Game + ".srm"), Data(1));
                pcs.PcA().Sync();

                File.SetLastWriteTimeUtc(pcs.LocalA(Game + ".srm"), DateTime.UtcNow.AddMinutes(5));
                Assert.Equal(SaveCloudState.Synced, Assert.Single(pcs.PcA().ItemStates()).State);
            }
        }

        [Fact]
        public void A_save_received_from_another_pc_shows_that_pc()
        {
            using (var pcs = new TwoPcs(SaveRules.Generic("psx")))
            {
                pcs.PcA().Plan();
                pcs.Write(pcs.LocalA(Game + ".srm"), Data(1));
                pcs.PcA().Sync();

                pcs.PcB().Sync();
                var item = Assert.Single(pcs.PcB().ItemStates());
                Assert.Equal(SaveCloudState.Synced, item.State);
                Assert.Equal("PC-A", item.Pc);
            }
        }

        [Fact]
        public void A_save_deleted_here_but_in_the_cloud_still_counts_it_comes_back_before_the_game()
        {
            using (var pcs = new TwoPcs(SaveRules.Generic("psx")))
            {
                pcs.PcA().Plan();
                pcs.Write(pcs.LocalA(Game + ".srm"), Data(1));
                pcs.PcA().Sync();

                File.Delete(pcs.LocalA(Game + ".srm"));
                Assert.Equal(SaveCloudState.Synced, Assert.Single(pcs.PcA().ItemStates()).State);
            }
        }

        [Fact]
        public void Files_shipped_with_RetroBat_and_empty_files_are_not_saves()
        {
            using (var pcs = new TwoPcs(SaveRules.Generic("mame")))
            {
                var shipped = pcs.LocalA(@"nvram\sf2\eeprom");
                Directory.CreateDirectory(Path.GetDirectoryName(shipped));
                File.WriteAllBytes(shipped, Data(1));
                File.SetLastWriteTimeUtc(shipped, new DateTime(2024, 2, 24, 22, 0, 0, DateTimeKind.Utc));

                // Antes da regra rodar aqui, nada conta.
                Assert.Empty(pcs.PcA().ItemStates());

                pcs.PcA().Plan();
                pcs.Write(pcs.LocalA(@"nvram\kof98\zeros"), new byte[512]);
                Assert.Empty(pcs.PcA().ItemStates());
            }
        }

        [Fact]
        public void An_unformatted_memory_card_is_not_waiting_for_upload()
        {
            using (var pcs = new TwoPcs(SaveRules.Pcsx2MemoryCards))
            {
                pcs.Write(pcs.LocalA("Mcd002.ps2"), Enumerable.Repeat((byte)0xFF, 0x4000).ToArray());
                Assert.Empty(pcs.PcA().ItemStates());
            }
        }

        [Fact]
        public void Conflict_left_for_later_shows_until_the_game_opens()
        {
            using (var pcs = new TwoPcs(SaveRules.Generic("psx")))
            {
                pcs.PcA().Plan();
                pcs.PcB().Plan();
                pcs.Write(pcs.LocalA(Game + ".srm"), Data(1));
                pcs.PcA().Sync();
                pcs.PcB().Sync();

                pcs.Write(pcs.LocalA(Game + ".srm"), Data(2));
                pcs.PcA().Sync();
                pcs.Write(pcs.LocalB(Game + ".srm"), Data(3));
                var result = pcs.PcB().Sync();
                Assert.Single(result.Conflicts);

                var items = pcs.PcB().ItemStates();
                SaveStatus.Overlay(items, result);
                Assert.Equal(SaveCloudState.Conflict, Assert.Single(items).State);
            }
        }

        [Fact]
        public void Lock_errors_do_not_become_a_save()
        {
            var items = new System.Collections.Generic.List<SaveItemStatus>();
            var result = new SaveSyncResult();
            result.Errors.Add("trava: acesso negado");
            SaveStatus.Overlay(items, result);
            Assert.Empty(items);
        }

        [Theory]
        [InlineData(Game + ".srm", Game)]
        [InlineData(Game + ".state.auto", Game)]
        [InlineData(@"duckstation\memcards\" + Game + "_1.mcd", Game)]
        [InlineData(@"pcsx2\" + Game + ".01.p2s.png", Game)]
        [InlineData(@"nvram\sf2\eeprom", "sf2")]
        [InlineData("Tekken 2.srm", "Tekken 2")]
        [InlineData("Tekken.srm", "Tekken")]
        [InlineData("Tekken 3.srm", null)]
        [InlineData("Mcd001.ps2", null)]
        [InlineData("vmu_save_A1.bin", null)]
        public void A_save_belongs_to_the_game_with_the_longest_matching_name(string item, string expected)
        {
            var stems = new[] { "Tekken", "Tekken 2", Game, "sf2" }.OrderByDescending(s => s.Length).ToList();
            Assert.Equal(expected, SaveStatus.GameOf(item, stems));
        }

        [Fact]
        public void Each_game_gets_the_worst_state_of_its_files_and_the_rest_is_shared_by_the_system()
        {
            var t = new DateTime(2026, 11, 10, 23, 0, 0, DateTimeKind.Utc);
            var status = SaveStatus.Group("PS2", new[]
            {
                new SaveItemStatus { Name = @"pcsx2\God of War (USA).01.p2s", State = SaveCloudState.Synced, TimeUtc = t, Pc = "PC-A" },
                new SaveItemStatus { Name = @"pcsx2\God of War (USA).02.p2s", State = SaveCloudState.Pending, TimeUtc = t.AddMinutes(5) },
                new SaveItemStatus { Name = "Mcd001.ps2", State = SaveCloudState.Synced, TimeUtc = t.AddMinutes(9), Pc = "PC-B" }
            }, new[] { "God of War (USA)", "God of War II (USA)" });

            Assert.Equal("ps2", status.System);
            var game = Assert.Single(status.Games).Value;
            Assert.Equal(SaveCloudState.Pending, game.State);
            Assert.Equal(2, game.Files);
            Assert.Equal(t.AddMinutes(5), game.TimeUtc);
            Assert.Equal(SaveCloudState.Synced, status.Shared.State);
            Assert.Equal("PC-B", status.Shared.Pc);
        }

        [Fact]
        public void The_file_for_the_es_keeps_other_systems_unless_everything_was_recalculated()
        {
            using (var dir = new TempDir())
            {
                var path = dir.Combine("estado", SaveStatus.FileName);
                var t = new DateTime(2026, 11, 11, 2, 55, 25, DateTimeKind.Utc);
                SaveStatus.Write(path, new[]
                {
                    SaveStatus.Group("ps2", new[] { new SaveItemStatus { Name = "Mcd001.ps2", TimeUtc = t, Pc = "PC-A" } }, new string[0]),
                    SaveStatus.Group("psx", new[] { new SaveItemStatus { Name = Game + ".srm", TimeUtc = t, Pc = "PC-A" } }, new[] { Game })
                }, pcName: "PC-A");

                SaveStatus.Write(path, new[] { SaveStatus.Group("psx", new[] { new SaveItemStatus { Name = Game + ".srm", State = SaveCloudState.Pending, TimeUtc = t } }, new[] { Game }) });
                var read = SaveStatus.Read(path);
                Assert.Equal(SaveCloudState.Synced, read["ps2"].Shared.State);
                Assert.Equal(t, read["ps2"].Shared.TimeUtc);
                Assert.Equal(SaveCloudState.Pending, read["psx"].Games[Game].State);

                SaveStatus.Write(path, new[] { SaveStatus.Group("psx", new SaveItemStatus[0], new[] { Game }) }, replaceAll: true);
                Assert.Empty(SaveStatus.Read(path));
                Assert.Contains("unsupported=\"", File.ReadAllText(path));
            }
        }

        [Fact]
        public void Refresh_writes_the_state_of_each_game_for_the_es_from_this_pc_only()
        {
            using (var dir = new TempDir())
            {
                var install = new RetroBatInstall(dir.Combine("RetroBat"));
                var roms = dir.Combine("Jogos");
                Directory.CreateDirectory(Path.Combine(roms, "psx"));
                File.WriteAllBytes(Path.Combine(roms, "psx", Game + ".zip"), new byte[0]); // marcador: só na nuvem

                var rule = SaveRules.Generic("psx");
                var cloud = dir.Combine("nuvem");
                var sync = new SaveSync(Path.Combine(install.Root, "saves"), install.RetroboatDataPath, cloud, rule, "PC-A");
                sync.Plan();
                var save = Path.Combine(install.Root, "saves", "psx", Game + ".srm");
                Directory.CreateDirectory(Path.GetDirectoryName(save));
                File.WriteAllBytes(save, Data(1));
                sync.Sync();

                SaveStatus.Refresh(install, roms, new[] { "psx" }, pcName: "PC-A");
                var game = SaveStatus.Read(SaveStatus.GetPath(install))["psx"].Games[Game];
                Assert.Equal(SaveCloudState.Synced, game.State);
                Assert.Equal("PC-A", game.Pc);
            }
        }
    }
}
