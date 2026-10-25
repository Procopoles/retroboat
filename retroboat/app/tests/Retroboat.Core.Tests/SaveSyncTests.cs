using System;
using System.IO;
using System.Linq;
using Retroboat.Library;
using Retroboat.Saves;
using Xunit;

namespace Retroboat.Tests
{
    public class SaveSyncTests
    {
        const string Card = "Mcd001.ps2";
        static readonly SaveRule Rule = SaveRules.Pcsx2MemoryCards;

        /// <summary>Dois PCs com a mesma pasta de nuvem e um relógio que o teste controla.</summary>
        sealed class TwoPcs : IDisposable
        {
            public readonly TempDir Cloud = new TempDir();
            public readonly TempDir A = new TempDir();
            public readonly TempDir B = new TempDir();
            public DateTime Now = new DateTime(2026, 10, 25, 18, 0, 0, DateTimeKind.Utc);

            public SaveSync PcA(Func<DateTime> clock = null) => new SaveSync(A.Combine("saves"), A.Combine("retroboat"), Cloud.Path, Rule, "PC-A", clock ?? (() => Now));
            public SaveSync PcB(Func<DateTime> clock = null) => new SaveSync(B.Combine("saves"), B.Combine("retroboat"), Cloud.Path, Rule, "PC-B", clock ?? (() => Now));

            public string CardA => A.Combine("saves", Rule.Folder, Card);
            public string CardB => B.Combine("saves", Rule.Folder, Card);
            public string CloudCard => Cloud.Combine("saves", Rule.Folder, Card);

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

        static byte[] Content(int seed) => TempDir.RandomBytes(64 * 1024, seed);

        [Fact]
        public void New_card_goes_to_the_cloud_and_reaches_the_other_pc()
        {
            using (var pcs = new TwoPcs())
            {
                pcs.Write(pcs.CardA, Content(1));

                var sent = pcs.PcA().Sync();
                var received = pcs.PcB().Sync();

                Assert.Equal(new[] { Card }, sent.Uploaded);
                Assert.Equal(new[] { Card }, received.Downloaded);
                Assert.Equal(Content(1), File.ReadAllBytes(pcs.CardB));
                Assert.Single(Directory.GetFiles(pcs.CloudCard, "*.gz"));
                Assert.False(pcs.PcA().Sync().HasTransfers);
                Assert.False(pcs.PcB().Sync().HasTransfers);
            }
        }

        [Fact]
        public void A_save_on_one_pc_replaces_the_other_and_keeps_a_local_copy()
        {
            using (var pcs = new TwoPcs())
            {
                pcs.Write(pcs.CardA, Content(1));
                pcs.PcA().Sync();
                pcs.PcB().Sync();

                pcs.Write(pcs.CardA, Content(2));
                Assert.Equal(new[] { Card }, pcs.PcA().Sync().Uploaded);
                Assert.Equal(new[] { Card }, pcs.PcB().Sync().Downloaded);

                Assert.Equal(Content(2), File.ReadAllBytes(pcs.CardB));
                var snapshot = Assert.Single(Directory.GetFiles(pcs.B.Combine("retroboat", "historico", "saves", Rule.Folder, Card)));
                Assert.Equal(Content(1), File.ReadAllBytes(snapshot));
                Assert.Equal(2, pcs.PcA().History(Card).Count);
            }
        }

        [Fact]
        public void Changes_on_both_sides_are_a_conflict_and_nothing_is_lost()
        {
            using (var pcs = new TwoPcs())
            {
                pcs.Write(pcs.CardA, Content(1));
                pcs.PcA().Sync();
                pcs.PcB().Sync();

                pcs.Write(pcs.CardA, Content(2));
                pcs.PcA().Sync();
                pcs.Write(pcs.CardB, Content(3)); // Jogou offline no PC B.

                var undecided = pcs.PcB().Sync();
                Assert.Equal(new[] { Card }, undecided.Conflicts);
                Assert.Equal(Content(3), File.ReadAllBytes(pcs.CardB));
                pcs.PcB().Sync(); // Outra abertura sem decidir: a mesma cópia não vai de novo.
                Assert.Single(Directory.GetFiles(Path.Combine(pcs.CloudCard, SaveSync.ConflictsFolderName)));

                var plan = pcs.PcB().Plan().Single();
                Assert.Equal(SaveAction.Conflict, plan.Action);
                Assert.Equal("PC-A", plan.Cloud.Pc);

                var kept = pcs.PcB().Sync(p => ConflictChoice.KeepLocal);
                Assert.Equal(new[] { Card }, kept.Uploaded);
                Assert.Equal(new[] { Card }, pcs.PcA().Sync().Downloaded);
                Assert.Equal(Content(3), File.ReadAllBytes(pcs.CardA));
            }
        }

        [Fact]
        public void Choosing_the_cloud_in_a_conflict_brings_it_and_keeps_this_pc_copy()
        {
            using (var pcs = new TwoPcs())
            {
                pcs.Write(pcs.CardA, Content(1));
                pcs.PcA().Sync();
                pcs.PcB().Sync();
                pcs.Write(pcs.CardA, Content(2));
                pcs.PcA().Sync();
                pcs.Write(pcs.CardB, Content(3));

                var result = pcs.PcB().Sync(p => ConflictChoice.KeepCloud);

                Assert.Equal(new[] { Card }, result.Downloaded);
                Assert.Equal(Content(2), File.ReadAllBytes(pcs.CardB));
                Assert.Single(Directory.GetFiles(Path.Combine(pcs.CloudCard, SaveSync.ConflictsFolderName)));
                Assert.Contains(Directory.GetFiles(pcs.B.Combine("retroboat", "historico", "saves", Rule.Folder, Card)), f => File.ReadAllBytes(f).SequenceEqual(Content(3)));
            }
        }

        [Fact]
        public void Same_card_on_both_pcs_on_first_use_is_not_a_conflict()
        {
            using (var pcs = new TwoPcs())
            {
                pcs.Write(pcs.CardA, Content(1));
                pcs.Write(pcs.CardB, Content(1));
                pcs.PcA().Sync();

                var result = pcs.PcB().Sync();

                Assert.False(result.HasTransfers);
                Assert.Empty(result.Conflicts);
                Assert.Equal(SaveAction.None, pcs.PcB().Plan().Single().Action);
            }
        }

        [Fact]
        public void Card_missing_outside_a_game_comes_back_from_the_cloud()
        {
            using (var pcs = new TwoPcs())
            {
                pcs.Write(pcs.CardA, Content(1));
                pcs.PcA().Sync();
                File.Delete(pcs.CardA); // Pasta saves apagada, RetroBat reinstalado...

                var result = pcs.PcA().Sync();

                Assert.Equal(new[] { Card }, result.Downloaded);
                Assert.Equal(Content(1), File.ReadAllBytes(pcs.CardA));
                Assert.Empty(Directory.GetFiles(pcs.CloudCard, "*" + SaveVersion.DeletedExtension));
            }
        }

        [Fact]
        public void Card_deleted_by_the_emulator_is_deleted_on_the_other_pc_and_kept_in_history()
        {
            using (var pcs = new TwoPcs())
            {
                pcs.Write(pcs.CardA, Content(1));
                pcs.PcA().Sync();
                pcs.PcB().Sync();

                File.Delete(pcs.CardA);
                var a = pcs.PcA();
                var deleted = a.Apply(a.Plan(sessionItems: new[] { Card }));
                var b = pcs.PcB().Sync();

                Assert.Equal(new[] { Card }, deleted.DeletedInCloud);
                Assert.Equal(new[] { Card }, b.DeletedLocal);
                Assert.False(File.Exists(pcs.CardB));
                Assert.Single(Directory.GetFiles(pcs.B.Combine("retroboat", "historico", "saves", Rule.Folder, Card)));
                Assert.Single(Directory.GetFiles(pcs.CloudCard, "*.gz"));
                Assert.False(pcs.PcA().Sync().HasTransfers);
            }
        }

        [Fact]
        public void Zeroed_card_is_never_sent_and_never_replaces_a_good_one()
        {
            using (var pcs = new TwoPcs())
            {
                pcs.Write(pcs.CardA, new byte[64 * 1024]);
                var result = pcs.PcA().Sync();

                Assert.Empty(result.Uploaded);
                Assert.Single(result.Skipped);
                Assert.False(Directory.Exists(pcs.CloudCard));
            }
        }

        [Fact]
        public void Other_pc_playing_holds_the_lock_until_it_closes_or_goes_silent()
        {
            using (var pcs = new TwoPcs())
            {
                var a = pcs.PcA();
                a.Lock("Black (USA)");

                var lockInfo = pcs.PcB().FindActiveLock();
                Assert.Equal("PC-A", lockInfo.Pc);
                Assert.Equal("Black (USA)", lockInfo.Game);
                Assert.Null(a.FindActiveLock()); // A própria trava não conta.

                pcs.Now = pcs.Now.AddMinutes(11);
                Assert.Null(pcs.PcB().FindActiveLock());

                a.RenewLock();
                Assert.NotNull(pcs.PcB().FindActiveLock());
                a.Unlock();
                Assert.Null(pcs.PcB().FindActiveLock());
            }
        }

        [Fact]
        public void Version_announced_by_another_pc_but_not_arrived_is_waited_for()
        {
            using (var pcs = new TwoPcs())
            {
                pcs.Write(pcs.CardA, Content(1));
                pcs.PcA().Sync();
                pcs.PcB().Sync();
                pcs.Write(pcs.CardA, Content(2));
                pcs.PcA().Sync();

                // O pcs\PC-A.xml chegou ao PC B antes da versão nova.
                var newest = pcs.PcA().LatestVersion(Card);
                var inTransit = newest.Path + ".em-transito";
                File.Move(newest.Path, inTransit);

                var plan = pcs.PcB().Plan().Single();
                Assert.Equal(SaveAction.WaitForCloud, plan.Action);
                Assert.Equal(newest.Id, plan.WaitingFor);
                Assert.Equal(new[] { Card }, pcs.PcB().Sync().Waiting);

                File.Move(inTransit, newest.Path);
                Assert.Equal(new[] { Card }, pcs.PcB().Sync().Downloaded);
            }
        }

        [Fact]
        public void New_version_sorts_after_what_this_pc_has_seen_even_with_a_late_clock()
        {
            using (var pcs = new TwoPcs())
            {
                pcs.Write(pcs.CardA, Content(1));
                pcs.PcA().Sync();
                pcs.PcB().Sync();

                var late = pcs.Now.AddHours(-3); // Relógio do PC B atrasado.
                pcs.Write(pcs.CardB, Content(2));
                pcs.PcB(() => late).Sync();

                Assert.Equal("PC-B", pcs.PcA().LatestVersion(Card).Pc);
                Assert.Equal(new[] { Card }, pcs.PcA().Sync().Downloaded);
                Assert.Equal(Content(2), File.ReadAllBytes(pcs.CardA));
            }
        }

        [Fact]
        public void Session_sends_each_save_after_the_emulator_stops_writing()
        {
            using (var pcs = new TwoPcs())
            {
                pcs.Write(pcs.CardA, Content(1));
                pcs.PcA().Sync();

                var session = new SaveSession(pcs.PcA(), utcNow: () => pcs.Now);
                session.Start("Black (USA)");
                Assert.NotNull(pcs.PcB().FindActiveLock());

                File.WriteAllBytes(pcs.CardA, Content(2));
                Assert.Empty(session.Poll().Uploaded); // Acabou de gravar: espera parar.
                pcs.Now = pcs.Now.AddSeconds(1);
                Assert.Empty(session.Poll().Uploaded);
                pcs.Now = pcs.Now.AddSeconds(4);
                Assert.Equal(new[] { Card }, session.Poll().Uploaded);
                Assert.Empty(session.Poll().Uploaded);

                // Cartão criado no emulador durante o jogo.
                var second = pcs.A.Combine("saves", Rule.Folder, "Mcd002.ps2");
                File.WriteAllBytes(second, Content(5));
                session.Poll();
                pcs.Now = pcs.Now.AddSeconds(5);
                Assert.Equal(new[] { "Mcd002.ps2" }, session.Poll().Uploaded);

                // Apagado no emulador durante o jogo; e uma gravação que ainda não esperou: Finish envia.
                File.Delete(second);
                File.WriteAllBytes(pcs.CardA, Content(3));
                var last = session.Finish();
                Assert.Equal(new[] { Card }, last.Uploaded);
                Assert.Equal(new[] { "Mcd002.ps2" }, last.DeletedInCloud);
                Assert.Null(pcs.PcB().FindActiveLock());

                var b = pcs.PcB().Sync();
                Assert.Equal(new[] { Card }, b.Downloaded);
                Assert.False(File.Exists(pcs.B.Combine("saves", Rule.Folder, "Mcd002.ps2")));
                Assert.Equal(Content(3), File.ReadAllBytes(pcs.CardB));
            }
        }

        [Fact]
        public void Active_card_is_checked_by_content_even_if_the_date_does_not_change()
        {
            using (var pcs = new TwoPcs())
            {
                pcs.Write(pcs.CardA, Content(1));
                pcs.PcA().Sync();
                var date = File.GetLastWriteTimeUtc(pcs.CardA);

                var session = new SaveSession(pcs.PcA(), () => new[] { Card }, () => pcs.Now);
                session.Start("Black (USA)");
                File.WriteAllBytes(pcs.CardA, Content(2));
                File.SetLastWriteTimeUtc(pcs.CardA, date);
                session.Poll();
                pcs.Now = pcs.Now.AddSeconds(5);

                Assert.Equal(new[] { Card }, session.Poll().Uploaded);
            }
        }

        [Fact]
        public void Save_from_another_pc_during_the_game_waits_and_this_pc_copy_is_kept()
        {
            using (var pcs = new TwoPcs())
            {
                pcs.Write(pcs.CardA, Content(1));
                pcs.PcA().Sync();
                pcs.PcB().Sync();

                var session = new SaveSession(pcs.PcA(), utcNow: () => pcs.Now);
                session.Start("Black (USA)");

                pcs.Write(pcs.CardB, Content(2)); // O PC B jogou mesmo assim e salvou.
                pcs.PcB().Sync();

                File.WriteAllBytes(pcs.CardA, Content(3));
                var last = session.Finish();

                Assert.Equal(new[] { Card }, last.Conflicts);
                Assert.Equal(Content(3), File.ReadAllBytes(pcs.CardA));
                Assert.Single(Directory.GetFiles(Path.Combine(pcs.CloudCard, SaveSync.ConflictsFolderName)));
                Assert.Equal(SaveAction.Conflict, pcs.PcA().Plan().Single().Action);
            }
        }

        [Fact]
        public void Saves_path_and_active_cards_come_from_the_retrobat_config()
        {
            using (var dir = new TempDir())
            {
                dir.WriteFile("retrobat.exe", new byte[1]);
                dir.WriteText(@"emulationstation\emulatorLauncher.cfg", "home=.\\.emulationstation\r\nsaves=.\\..\\meus-saves\r\n");
                dir.WriteText(@"emulators\pcsx2\inis\PCSX2.ini",
                    "[Folders]\r\nMemoryCards = x\r\n\r\n[MemoryCards]\r\nSlot1_Enable = true\r\nSlot1_Filename = Black (USA).ps2\r\nSlot2_Enable = false\r\nSlot2_Filename = Mcd002.ps2\r\n");
                var install = new RetroBatInstall(dir.Path);

                Assert.Equal(dir.Combine("meus-saves"), SaveRules.GetSavesPath(install));
                Assert.Equal(new[] { "Black (USA).ps2" }, SaveRules.Pcsx2ActiveCards(install));
                Assert.Single(SaveRules.For("ps2", "pcsx2"));
                Assert.Single(SaveRules.For("ps2", ""));
                Assert.Empty(SaveRules.For("ps2", "libretro"));
            }
        }
    }
}
