using System.IO;
using System.Linq;
using System.Xml.Linq;
using Retroboat.Cloud;
using Retroboat.Library;
using Retroboat.Media;
using Xunit;

namespace Retroboat.Tests
{
    public class MediaTests
    {
        const string Game = "Jogo (USA).iso";

        static RetroBatInstall Install(TempDir dir, string name) => new RetroBatInstall(dir.Combine(name));

        static string Gamelist(RetroBatInstall install) => Path.Combine(install.GamelistsPath, "ps2", "gamelist.xml");

        static void SyncLibrary(RetroBatInstall install, TempDir cloud) =>
            new CloudLibrarySync(install, install.DefaultRomsPath, new CloudLibrary(cloud.Combine("jogos"))).Apply();

        static CloudMediaSyncResult SyncMedia(RetroBatInstall install, TempDir cloud, string pc, CloudMediaOptions options = null) =>
            new CloudMediaSync(install, install.DefaultRomsPath, cloud.Path, pc, options).Apply();

        [Fact]
        public void Scraped_media_leaves_the_games_folder()
        {
            using (var dir = new TempDir())
            {
                var install = Install(dir, "RetroBat");
                dir.WriteText(@"RetroBat\emulationstation\.emulationstation\es_systems.cfg",
                    "<systemList><system><name>ps2</name><path>~\\..\\roms\\ps2</path></system></systemList>");
                dir.WriteFile(@"RetroBat\roms\ps2\images\Jogo (USA)-image.png", new byte[] { 1, 2, 3 });
                dir.WriteText(@"RetroBat\emulationstation\.emulationstation\gamelists\ps2\gamelist.xml",
                    "<gameList><game><path>./Jogo (USA).iso</path><image>./images/Jogo (USA)-image.png</image></game></gameList>");

                var result = new MediaRelocator(install).Apply(install.DefaultRomsPath);

                Assert.Equal(1, result.FilesMoved);
                Assert.False(Directory.Exists(dir.Combine(@"RetroBat\roms\ps2\images")));
                Assert.True(File.Exists(dir.Combine(@"RetroBat\emulationstation\.emulationstation\media\ps2\images\Jogo (USA)-image.png")));
                var image = (string)XDocument.Load(Gamelist(install)).Root.Element("game").Element("image");
                Assert.Equal("~/.emulationstation/media/ps2/images/Jogo (USA)-image.png", image);
            }
        }

        [Fact]
        public void Content_scraped_on_one_pc_appears_on_another_before_download()
        {
            using (var cloud = new TempDir())
            using (var pcs = new TempDir())
            {
                cloud.WriteFile(@"jogos\ps2\" + Game, TempDir.RandomBytes(1000));

                // PC A: o jogo foi "scrapeado" localmente.
                var a = Install(pcs, "A");
                SyncLibrary(a, cloud);
                pcs.WriteFile(@"A\emulationstation\.emulationstation\media\ps2\images\Jogo (USA)-image.png", new byte[] { 9, 9, 9 });
                var gamelistA = XDocument.Load(Gamelist(a));
                var gameA = CloudLibrarySync.FindGame(gamelistA, Game);
                gameA.SetElementValue("name", "Jogo Incrível");
                gameA.SetElementValue("desc", GameDescription.WithStatus((string)gameA.Element("desc"), "Uma aventura."));
                gameA.SetElementValue("image", "~/.emulationstation/media/ps2/images/Jogo (USA)-image.png");
                gameA.SetElementValue("playcount", "7");
                gameA.SetAttributeValue("id", "12345");
                gamelistA.Save(Gamelist(a));

                var sent = SyncMedia(a, cloud, "PC-A");

                Assert.Equal(1, sent.GamesShared);
                Assert.Equal(1, sent.FilesUploaded);
                Assert.True(File.Exists(cloud.Combine(@"midia\ps2\imagens\Jogo (USA)-image.png")));
                var shared = File.ReadAllText(cloud.Combine(@"midia\ps2\metadados\PC-A.xml"));
                Assert.Contains("Uma aventura.", shared);
                Assert.DoesNotContain("Na nuvem", shared);
                Assert.DoesNotContain("playcount", shared);

                // PC B: nunca viu o jogo; recebe capa, nome e descrição antes de baixar.
                var b = Install(pcs, "B");
                SyncLibrary(b, cloud);
                var received = SyncMedia(b, cloud, "PC-B");

                Assert.Equal(1, received.GamesFilled);
                Assert.Equal(1, received.FilesDownloaded);
                Assert.False(File.Exists(cloud.Combine(@"midia\ps2\metadados\PC-B.xml")));
                var gameB = CloudLibrarySync.FindGame(XDocument.Load(Gamelist(b)), Game);
                Assert.Equal("Jogo Incrível", (string)gameB.Element("name"));
                Assert.Equal("12345", (string)gameB.Attribute("id"));
                Assert.Equal("~/.emulationstation/media/ps2/images/Jogo (USA)-image.png", (string)gameB.Element("image"));
                Assert.True(File.Exists(pcs.Combine(@"B\emulationstation\.emulationstation\media\ps2\images\Jogo (USA)-image.png")));

                var desc = (string)gameB.Element("desc");
                Assert.StartsWith(GameDescription.CloudPrefix, desc);
                Assert.EndsWith("Uma aventura.", desc);
            }
        }

        [Fact]
        public void Videos_only_come_when_enabled()
        {
            using (var cloud = new TempDir())
            using (var pcs = new TempDir())
            {
                cloud.WriteFile(@"jogos\ps2\" + Game, TempDir.RandomBytes(1000));
                cloud.WriteFile(@"midia\ps2\videos\Jogo (USA)-video.mp4", new byte[] { 1 });
                cloud.WriteText(@"midia\ps2\metadados\PC-A.xml",
                    "<retroboatMedia><game key=\"" + Game + "\" updated=\"2026-10-25T10:00:00Z\"><video>videos/Jogo (USA)-video.mp4</video></game></retroboatMedia>");

                var b = Install(pcs, "B");
                SyncLibrary(b, cloud);
                SyncMedia(b, cloud, "PC-B");
                Assert.Null(CloudLibrarySync.FindGame(XDocument.Load(Gamelist(b)), Game).Element("video"));

                SyncMedia(b, cloud, "PC-B", new CloudMediaOptions { Videos = true });
                Assert.NotNull(CloudLibrarySync.FindGame(XDocument.Load(Gamelist(b)), Game).Element("video"));
            }
        }

        [Fact]
        public void Cloud_never_overwrites_what_this_pc_already_has()
        {
            using (var cloud = new TempDir())
            using (var pcs = new TempDir())
            {
                cloud.WriteFile(@"jogos\ps2\" + Game, TempDir.RandomBytes(1000));
                // Entrada antiga de outro PC: a deste PC (só o nome) fica mais nova e não pode esconder o gênero.
                cloud.WriteText(@"midia\ps2\metadados\PC-A.xml",
                    "<retroboatMedia><game key=\"" + Game + "\" updated=\"2020-01-01T00:00:00Z\"><name>Nome da Nuvem</name><genre>Ação</genre></game></retroboatMedia>");

                var b = Install(pcs, "B");
                SyncLibrary(b, cloud);
                var gamelist = XDocument.Load(Gamelist(b));
                CloudLibrarySync.FindGame(gamelist, Game).SetElementValue("name", "Meu Nome");
                gamelist.Save(Gamelist(b));

                SyncMedia(b, cloud, "PC-B");

                var game = CloudLibrarySync.FindGame(XDocument.Load(Gamelist(b)), Game);
                Assert.Equal("Meu Nome", (string)game.Element("name"));
                Assert.Equal("Ação", (string)game.Element("genre"));
            }
        }

        [Fact]
        public void Newest_entry_wins_between_pcs()
        {
            using (var cloud = new TempDir())
            using (var pcs = new TempDir())
            {
                cloud.WriteFile(@"jogos\ps2\" + Game, TempDir.RandomBytes(1000));
                cloud.WriteText(@"midia\ps2\metadados\PC-A.xml",
                    "<retroboatMedia><game key=\"" + Game + "\" updated=\"2026-10-20T10:00:00Z\"><genre>Antigo</genre></game></retroboatMedia>");
                cloud.WriteText(@"midia\ps2\metadados\PC-C.xml",
                    "<retroboatMedia><game key=\"" + Game + "\" updated=\"2026-10-25T10:00:00Z\"><genre>Novo</genre></game></retroboatMedia>");

                var b = Install(pcs, "B");
                SyncLibrary(b, cloud);
                SyncMedia(b, cloud, "PC-B");

                Assert.Equal("Novo", (string)CloudLibrarySync.FindGame(XDocument.Load(Gamelist(b)), Game).Element("genre"));
            }
        }

        [Fact]
        public void Status_line_comes_off_but_the_description_stays_after_install()
        {
            using (var retrobat = new TempDir())
            {
                var path = retrobat.WriteText("gamelist.xml", "<gameList><game><path>./A.iso</path><desc>Uma aventura.</desc></game></gameList>");
                var entry = new CloudIndexEntry { LocalName = "A.iso", State = CloudGameState.Cloud, CloudSize = 1000 };

                CloudLibrarySync.UpdateGamelist(path, new[] { entry });
                var inCloud = (string)XDocument.Load(path).Root.Element("game").Element("desc");
                Assert.StartsWith(GameDescription.CloudPrefix, inCloud);
                Assert.EndsWith("Uma aventura.", inCloud);

                entry.State = CloudGameState.Installed;
                CloudLibrarySync.UpdateGamelist(path, new[] { entry });
                Assert.Equal("Uma aventura.", (string)XDocument.Load(path).Root.Element("game").Element("desc"));
            }
        }
    }
}
