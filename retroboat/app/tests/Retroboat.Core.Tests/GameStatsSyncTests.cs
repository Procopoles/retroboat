using System.IO;
using System.Linq;
using System.Xml.Linq;
using Retroboat.Library;
using Retroboat.Profile;
using Xunit;

namespace Retroboat.Tests
{
    public class GameStatsSyncTests
    {
        const string Game = "Jogo (USA).iso";
        const string Key = "ps2/" + Game;

        static RetroBatInstall Install(TempDir dir, string name) => new RetroBatInstall(dir.Combine(name));

        static string Gamelist(RetroBatInstall install) => Path.Combine(install.GamelistsPath, "ps2", "gamelist.xml");

        static GameStatsSyncResult Sync(RetroBatInstall install, TempDir cloud, string pc, string roms = null) =>
            new GameStatsSync(install, cloud.Path, roms, pc).Sync();

        /// <summary>O ES gravou o jogo no gamelist ao fechar.</summary>
        static void EsWrote(RetroBatInstall install, int playcount = 0, int gametime = 0, string lastplayed = null, bool favorite = false, string name = "Jogo")
        {
            var path = Gamelist(install);
            var doc = File.Exists(path) ? XDocument.Load(path) : new XDocument(new XElement("gameList"));
            doc.Root.Elements("game").Where(g => (string)g.Element("path") == "./" + Game).Remove();
            var game = new XElement("game", new XElement("path", "./" + Game), new XElement("name", name));
            if (favorite) game.Add(new XElement("favorite", "true"));
            if (playcount > 0) game.Add(new XElement("playcount", playcount));
            if (lastplayed != null) game.Add(new XElement("lastplayed", lastplayed));
            if (gametime > 0) game.Add(new XElement("gametime", gametime));
            doc.Root.Add(game);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            doc.Save(path);
        }

        static XElement GameIn(RetroBatInstall install) =>
            XDocument.Load(Gamelist(install)).Root.Elements("game").Single(g => (string)g.Element("path") == "./" + Game);

        static int PlayCount(RetroBatInstall install) => (int?)GameIn(install).Element("playcount") ?? 0;
        static int GameTime(RetroBatInstall install) => (int?)GameIn(install).Element("gametime") ?? 0;
        static string LastPlayed(RetroBatInstall install) => (string)GameIn(install).Element("lastplayed");
        static bool Favorite(RetroBatInstall install) => (string)GameIn(install).Element("favorite") == "true";

        [Fact]
        public void Play_time_and_favorite_from_one_pc_appear_on_another()
        {
            using (var cloud = new TempDir())
            using (var pcs = new TempDir())
            {
                var a = Install(pcs, "A");
                EsWrote(a, playcount: 3, gametime: 600, lastplayed: "20261108T161400", favorite: true);
                var sent = Sync(a, cloud, "PC-A");
                Assert.Equal(new[] { Key }, sent.Sent);
                Assert.Empty(sent.Received);

                var b = Install(pcs, "B");
                EsWrote(b, name: "Jogo no B");
                var received = Sync(b, cloud, "PC-B");

                Assert.Equal(new[] { Key }, received.Received);
                Assert.Empty(received.Sent);
                Assert.Equal(3, PlayCount(b));
                Assert.Equal(600, GameTime(b));
                Assert.Equal("20261108T161400", LastPlayed(b));
                Assert.True(Favorite(b));
                Assert.Equal("Jogo no B", (string)GameIn(b).Element("name")); // O resto do jogo não muda.
                Assert.Empty(received.Errors);
            }
        }

        [Fact]
        public void Plays_on_both_pcs_add_up_and_the_latest_date_wins()
        {
            using (var cloud = new TempDir())
            using (var pcs = new TempDir())
            {
                var a = Install(pcs, "A");
                var b = Install(pcs, "B");
                EsWrote(a, playcount: 3, gametime: 600, lastplayed: "20261108T161400");
                Sync(a, cloud, "PC-A");
                EsWrote(b);
                Sync(b, cloud, "PC-B");

                // B joga 2 vezes (300 s); A joga 1 vez (100 s), sem ver o B.
                EsWrote(b, playcount: 5, gametime: 900, lastplayed: "20261109T200000");
                EsWrote(a, playcount: 4, gametime: 700, lastplayed: "20261109T100000");
                Sync(b, cloud, "PC-B");
                Sync(a, cloud, "PC-A");
                Sync(b, cloud, "PC-B");

                foreach (var pc in new[] { a, b })
                {
                    Assert.Equal(6, PlayCount(pc));
                    Assert.Equal(1000, GameTime(pc));
                    Assert.Equal("20261109T200000", LastPlayed(pc));
                }

                // Sem jogar, nada vai nem vem, e nada conta duas vezes.
                var again = Sync(a, cloud, "PC-A");
                Assert.Empty(again.Sent);
                Assert.Empty(again.Received);
                Assert.Equal(6, PlayCount(a));
            }
        }

        [Fact]
        public void Pre_existing_plays_on_each_pc_are_all_counted_on_the_first_sync()
        {
            using (var cloud = new TempDir())
            using (var pcs = new TempDir())
            {
                var a = Install(pcs, "A");
                var b = Install(pcs, "B");
                EsWrote(a, playcount: 5, gametime: 5000);
                EsWrote(b, playcount: 2, gametime: 1200);

                Sync(a, cloud, "PC-A");
                Sync(b, cloud, "PC-B");
                Sync(a, cloud, "PC-A");

                Assert.Equal(7, PlayCount(a));
                Assert.Equal(7, PlayCount(b));
                Assert.Equal(6200, GameTime(a));
                Assert.Equal(6200, GameTime(b));
            }
        }

        [Fact]
        public void Unfavoriting_on_one_pc_unfavorites_on_the_others()
        {
            using (var cloud = new TempDir())
            using (var pcs = new TempDir())
            {
                var a = Install(pcs, "A");
                var b = Install(pcs, "B");
                EsWrote(a, favorite: true);
                File.SetLastWriteTimeUtc(Gamelist(a), System.DateTime.UtcNow.AddMinutes(-10));
                Sync(a, cloud, "PC-A");
                EsWrote(b);
                Sync(b, cloud, "PC-B");
                Assert.True(Favorite(b));

                // O ES do B gravou o gamelist depois: a mudança é mais nova que a do A.
                EsWrote(b, favorite: false);
                Sync(b, cloud, "PC-B");
                Sync(a, cloud, "PC-A");

                Assert.False(Favorite(a));
                Assert.False(Favorite(b));
                Assert.Null(GameIn(a).Element("favorite")); // Como o ES grava: falso não aparece.
            }
        }

        [Fact]
        public void A_copied_retrobat_folder_on_another_pc_never_counts_twice()
        {
            using (var cloud = new TempDir())
            using (var pcs = new TempDir())
            {
                var a = Install(pcs, "A");
                EsWrote(a, playcount: 4, gametime: 400);
                Sync(a, cloud, "PC-A");

                // A pasta inteira (gamelist e estado) vai para outro PC, como um RetroBat num pen drive.
                CopyDirectory(pcs.Combine("A"), pcs.Combine("C"));
                var c = Install(pcs, "C");
                Sync(c, cloud, "PC-C");
                Assert.Equal(4, PlayCount(c));

                EsWrote(c, playcount: 5, gametime: 500);
                Sync(c, cloud, "PC-C");
                Sync(a, cloud, "PC-A");

                Assert.Equal(5, PlayCount(a));
                Assert.Equal(500, GameTime(a));
                Assert.Equal(2, Directory.GetFiles(cloud.Combine(@"perfil\jogos"), "*.xml").Length);
            }
        }

        [Fact]
        public void A_game_left_in_the_es_recovery_counts_and_gets_the_total()
        {
            using (var cloud = new TempDir())
            using (var pcs = new TempDir())
            {
                var a = Install(pcs, "A");
                var b = Install(pcs, "B");
                EsWrote(a, playcount: 2, gametime: 200);
                Sync(a, cloud, "PC-A");
                EsWrote(b, playcount: 1, gametime: 100);
                Sync(b, cloud, "PC-B");
                Sync(a, cloud, "PC-A");
                Assert.Equal(3, PlayCount(a));

                // O ES do A foi fechado à força depois de uma partida: ela só está na recovery.
                var recovery = Path.Combine(a.UserEmulationStationPath, "recovery", "ps2", "Jogo (USA).xml");
                Directory.CreateDirectory(Path.GetDirectoryName(recovery));
                new XDocument(new XElement("gameList", new XAttribute("parentHash", "123"),
                    new XElement("game", new XElement("path", "./" + Game), new XElement("playcount", 4), new XElement("gametime", 360)))).Save(recovery);

                // O B jogou de novo enquanto isso.
                EsWrote(b, playcount: 4, gametime: 350);
                Sync(b, cloud, "PC-B");
                Sync(a, cloud, "PC-A");

                // 2 + 1 do A, 1 + 1 do B. A recovery vale por cima do gamelist no ES, então ganha o total também.
                var inRecovery = XDocument.Load(recovery).Root.Element("game");
                Assert.Equal(5, (int)inRecovery.Element("playcount"));
                Assert.Equal(410, (int)inRecovery.Element("gametime"));
                Assert.Equal("123", (string)XDocument.Load(recovery).Root.Attribute("parentHash"));
                Assert.Equal(5, PlayCount(a));
            }
        }

        [Fact]
        public void Interrupted_before_writing_the_gamelist_nothing_counts_twice()
        {
            using (var cloud = new TempDir())
            using (var pcs = new TempDir())
            {
                var a = Install(pcs, "A");
                var b = Install(pcs, "B");
                EsWrote(b, playcount: 3, gametime: 300);
                Sync(b, cloud, "PC-B");
                EsWrote(a, playcount: 1, gametime: 100);

                // A sincronização do A gravou o estado e foi interrompida antes do gamelist.
                var before = File.ReadAllText(Gamelist(a));
                Sync(a, cloud, "PC-A");
                File.WriteAllText(Gamelist(a), before);

                Sync(a, cloud, "PC-A");
                Sync(b, cloud, "PC-B");

                Assert.Equal(4, PlayCount(a));
                Assert.Equal(4, PlayCount(b));
                Assert.Equal(400, GameTime(b));
            }
        }

        [Fact]
        public void A_game_with_plays_in_the_cloud_gets_a_gamelist_line_when_its_file_is_here()
        {
            using (var cloud = new TempDir())
            using (var pcs = new TempDir())
            {
                var a = Install(pcs, "A");
                EsWrote(a, playcount: 2, favorite: true);
                Sync(a, cloud, "PC-A");

                var b = Install(pcs, "B");
                pcs.WriteText(@"B\emulationstation\.emulationstation\es_systems.cfg",
                    "<systemList><system><name>ps2</name><path>~\\..\\roms\\ps2</path></system></systemList>");
                pcs.WriteFile(@"B\roms\ps2\" + Game, new byte[0]); // Marcador de jogo da nuvem.
                var result = Sync(b, cloud, "PC-B", b.DefaultRomsPath);

                Assert.Equal(new[] { Key }, result.Received);
                Assert.Equal(2, PlayCount(b));
                Assert.True(Favorite(b));

                // Sem o arquivo, nenhuma linha é criada.
                var c = Install(pcs, "C");
                pcs.WriteText(@"C\emulationstation\.emulationstation\es_systems.cfg",
                    "<systemList><system><name>ps2</name><path>~\\..\\roms\\ps2</path></system></systemList>");
                Assert.Empty(Sync(c, cloud, "PC-C", c.DefaultRomsPath).Received);
                Assert.False(File.Exists(Gamelist(c)));
            }
        }

        [Fact]
        public void A_file_from_another_pc_halfway_through_the_drive_upload_is_skipped()
        {
            using (var cloud = new TempDir())
            using (var pcs = new TempDir())
            {
                cloud.WriteText(@"perfil\jogos\PC-X-12345678.xml", "<retroboatGameStats pc=\"PC-X\"><game key=\"ps2/Jog");
                var a = Install(pcs, "A");
                EsWrote(a, playcount: 1);

                var result = Sync(a, cloud, "PC-A");

                Assert.Empty(result.Errors);
                Assert.Equal(1, PlayCount(a));
            }
        }

        static void CopyDirectory(string source, string target)
        {
            foreach (var dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(dir.Replace(source, target));

            Directory.CreateDirectory(target);
            foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
                File.Copy(file, file.Replace(source, target), true);
        }
    }
}
