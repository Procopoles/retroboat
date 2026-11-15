using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Retroboat.Cloud;
using Retroboat.Library;
using Retroboat.Media;
using Retroboat.Media.Sources;
using Xunit;

namespace Retroboat.Tests
{
    /// <summary>
    /// A nuvem como fonte da verdade da mídia: o que as fontes públicas trazem é buscado uma vez só, por qualquer PC, e o que
    /// a pessoa busca no PROCURAR MÍDIAS de um PC chega aos outros.
    /// </summary>
    public class MediaCloudTruthTests
    {
        const string Game = "God Hand (USA).iso";
        const string Stem = "God Hand (USA)";
        const string Image = "~/.emulationstation/media/ps2/images/God Hand (USA)-image.png";

        /// <summary>A importação automática deste PC (PERSONALIZADO) com o registro da nuvem.</summary>
        static AutoImportResult Import(RetroBatInstall install, TempDir cloud, string pc, FakeMediaSource source) =>
            new AutoImport(install, install.DefaultRomsPath, MediaTest.Search(source), MediaImportLog.InCloud(cloud.Path, pc), new FakeMediaHttp()).Apply();

        static RetroBatInstall Pc(TempDir pcs, string name, TempDir cloud)
        {
            var install = new RetroBatInstall(pcs.Combine(name));
            new CloudLibrarySync(install, install.DefaultRomsPath, new CloudLibrary(cloud.Combine("jogos"))).Apply();
            return install;
        }

        static string Gamelist(RetroBatInstall install) => Path.Combine(install.GamelistsPath, "ps2", "gamelist.xml");

        static XElement Find(RetroBatInstall install) => CloudLibrarySync.FindGame(XDocument.Load(Gamelist(install)), Game);

        static void Edit(RetroBatInstall install, Action<XElement> change)
        {
            var gamelist = XDocument.Load(Gamelist(install));
            change(CloudLibrarySync.FindGame(gamelist, Game));
            gamelist.Save(Gamelist(install));
        }

        static string LocalImage(RetroBatInstall install) =>
            Path.Combine(install.UserEmulationStationPath, "media", "ps2", "images", Stem + "-image.png");

        /// <summary>Grava a imagem do jogo neste PC como o PROCURAR MÍDIAS faria, com a data de quando foi buscada.</summary>
        static void WriteImage(RetroBatInstall install, byte[] content, DateTime when)
        {
            var file = LocalImage(install);
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            File.WriteAllBytes(file, content);
            File.SetLastWriteTimeUtc(file, when);
        }

        static CloudMediaSyncResult Sync(RetroBatInstall install, TempDir cloud, string pc) =>
            new CloudMediaSync(install, install.DefaultRomsPath, cloud.Path, pc).Apply();

        /// <summary>Dois PCs com o jogo igual: A buscou a mídia, B recebeu.</summary>
        static (RetroBatInstall A, RetroBatInstall B) TwoPcsInSync(TempDir cloud, TempDir pcs)
        {
            cloud.WriteFile(@"jogos\ps2\" + Game, TempDir.RandomBytes(1000));
            var a = Pc(pcs, "A", cloud);
            var b = Pc(pcs, "B", cloud);

            WriteImage(a, new byte[] { 1, 1, 1 }, DateTime.UtcNow.AddDays(-10));
            Edit(a, g =>
            {
                g.SetElementValue("desc", "Descrição antiga.");
                g.SetElementValue("genre", "Ação");
                g.SetElementValue("image", Image);
            });

            Sync(a, cloud, "PC-A");
            Sync(b, cloud, "PC-B");
            Assert.Equal("Descrição antiga.", (string)Find(b).Element("desc"));
            return (a, b);
        }

        [Fact]
        public void A_search_on_one_pc_reaches_the_other_pcs()
        {
            using (var cloud = new TempDir())
            using (var pcs = new TempDir())
            {
                var (a, b) = TwoPcsInSync(cloud, pcs);

                // A pessoa busca de novo no PC A: texto e imagem novos, com o mesmo nome de arquivo.
                WriteImage(a, new byte[] { 2, 2, 2, 2, 2 }, DateTime.UtcNow.AddMinutes(-1));
                Edit(a, g => g.SetElementValue("desc", "Descrição nova."));
                var sent = Sync(a, cloud, "PC-A");
                Assert.Equal(1, sent.GamesShared);
                Assert.Equal(1, sent.FilesUploaded);

                var received = Sync(b, cloud, "PC-B");

                Assert.Equal(1, received.GamesFilled);
                Assert.Equal("Descrição nova.", (string)Find(b).Element("desc"));
                Assert.Equal(new byte[] { 2, 2, 2, 2, 2 }, File.ReadAllBytes(LocalImage(b)));
                Assert.Equal("Ação", (string)Find(b).Element("genre"));
            }
        }

        [Fact]
        public void An_outdated_pc_does_not_send_its_old_cover_over_the_new_one()
        {
            using (var cloud = new TempDir())
            using (var pcs = new TempDir())
            {
                var (a, b) = TwoPcsInSync(cloud, pcs);
                WriteImage(b, new byte[] { 1, 1, 1 }, DateTime.UtcNow.AddDays(-10)); // a de B, antiga

                WriteImage(a, new byte[] { 3, 3, 3, 3 }, DateTime.UtcNow.AddMinutes(-1));
                Sync(a, cloud, "PC-A");

                var result = Sync(b, cloud, "PC-B");

                Assert.Equal(0, result.FilesUploaded);
                Assert.Equal(new byte[] { 3, 3, 3, 3 }, File.ReadAllBytes(cloud.Combine(@"midia\ps2\imagens\" + Stem + "-image.png")));
                Assert.Equal(new byte[] { 3, 3, 3, 3 }, File.ReadAllBytes(LocalImage(b)));
            }
        }

        [Fact]
        public void A_change_made_here_stays_and_goes_to_the_cloud()
        {
            using (var cloud = new TempDir())
            using (var pcs = new TempDir())
            {
                var (a, b) = TwoPcsInSync(cloud, pcs);

                Edit(b, g => g.SetElementValue("genre", "Briga de rua"));
                Sync(b, cloud, "PC-B");
                Assert.Equal("Briga de rua", (string)Find(b).Element("genre"));

                Sync(a, cloud, "PC-A");
                Assert.Equal("Briga de rua", (string)Find(a).Element("genre"));
                Assert.Equal("Descrição antiga.", (string)Find(a).Element("desc"));
            }
        }

        [Fact]
        public void A_pc_from_before_this_version_still_gets_the_update()
        {
            using (var cloud = new TempDir())
            using (var pcs = new TempDir())
            {
                var (a, b) = TwoPcsInSync(cloud, pcs);
                // B tinha enviado o que sabia, mas não há o registro da última sincronização (versão anterior).
                Edit(b, g => g.SetElementValue("players", "1"));
                Sync(b, cloud, "PC-B");
                File.Delete(CloudMediaSync.LastSyncPath(b, "ps2"));
                var ownB = cloud.Combine(@"midia\ps2\metadados\PC-B.xml");
                var entries = XDocument.Load(ownB);
                entries.Root.Element("game").SetAttributeValue("updated", "2026-11-01T10:00:00Z"); // enviado dias atrás
                entries.Save(ownB);

                Edit(a, g => g.SetElementValue("desc", "Descrição nova."));
                Sync(a, cloud, "PC-A");
                Sync(b, cloud, "PC-B");

                Assert.Equal("Descrição nova.", (string)Find(b).Element("desc"));
            }
        }

        [Fact]
        public void Everything_a_scraper_brings_goes_to_the_cloud_with_its_search_marks()
        {
            using (var cloud = new TempDir())
            using (var pcs = new TempDir())
            {
                cloud.WriteFile(@"jogos\ps2\" + Game, TempDir.RandomBytes(1000));
                var a = Pc(pcs, "A", cloud);
                Edit(a, g =>
                {
                    g.SetElementValue("name", "God Hand");
                    g.SetElementValue("sortname", "God Hand");
                    g.SetElementValue("genres", "10,13");
                    g.SetElementValue("playcount", "3");
                    g.Add(new XElement("scrap", new XAttribute("name", "ScreenScraper"), new XAttribute("date", "20261114T154500")));
                    g.SetAttributeValue("id", "4321");
                });
                Sync(a, cloud, "PC-A");

                var shared = File.ReadAllText(cloud.Combine(@"midia\ps2\metadados\PC-A.xml"));
                Assert.Contains("<scrap name=\"ScreenScraper\">20261114T154500</scrap>", shared);
                Assert.Contains("<genres>10,13</genres>", shared);
                Assert.DoesNotContain("playcount", shared);

                var b = Pc(pcs, "B", cloud);
                Sync(b, cloud, "PC-B");
                var game = Find(b);
                Assert.Equal("God Hand", (string)game.Element("sortname"));
                Assert.Equal("4321", (string)game.Attribute("id"));
                var scrap = game.Elements("scrap").Single();
                Assert.Equal("ScreenScraper", (string)scrap.Attribute("name"));
                Assert.Equal("20261114T154500", (string)scrap.Attribute("date"));
                Assert.True(GameMedia.WasSearchedByHand(game));
            }
        }

        [Fact]
        public void Nothing_changes_on_a_second_sync()
        {
            using (var cloud = new TempDir())
            using (var pcs = new TempDir())
            {
                var (a, b) = TwoPcsInSync(cloud, pcs);
                var ownA = File.ReadAllText(cloud.Combine(@"midia\ps2\metadados\PC-A.xml"));

                var againA = Sync(a, cloud, "PC-A");
                var againB = Sync(b, cloud, "PC-B");

                Assert.Equal(0, againA.GamesShared + againA.FilesUploaded + againA.GamesFilled + againA.FilesDownloaded);
                Assert.Equal(0, againB.GamesShared + againB.FilesUploaded + againB.GamesFilled + againB.FilesDownloaded);
                Assert.Equal(ownA, File.ReadAllText(cloud.Combine(@"midia\ps2\metadados\PC-A.xml")));
                Assert.False(File.Exists(cloud.Combine(@"midia\ps2\metadados\PC-B.xml")));
            }
        }

        [Fact]
        public void A_game_searched_on_one_pc_is_never_searched_again_on_another()
        {
            using (var cloud = new TempDir())
            using (var pcs = new TempDir())
            {
                cloud.WriteFile(@"jogos\ps2\" + Game, TempDir.RandomBytes(1000));
                cloud.WriteFile(@"jogos\ps2\Jogo Caseiro.iso", TempDir.RandomBytes(1000));
                var a = Pc(pcs, "A", cloud);
                var b = Pc(pcs, "B", cloud);
                var source = new FakeMediaSource(MediaSourceIds.Libretro).Text(Stem, MediaFields.Description, "Briga.").Media(Stem, MediaFields.Cover);

                Import(a, cloud, "PC-A", source);
                Assert.Equal(2, source.Calls); // God Hand e o jogo caseiro

                // B não tem nada ainda (nem sincronizou a mídia): mesmo assim, não vai às fontes.
                var resultB = Import(b, cloud, "PC-B", source);

                Assert.Equal(2, source.Calls);
                Assert.Empty(resultB.NotFound);
                Assert.True(File.Exists(cloud.Combine(@"midia\importacao\PC-A.txt")));
            }
        }

        [Fact]
        public void A_game_without_results_is_not_searched_again_on_the_next_start()
        {
            using (var cloud = new TempDir())
            using (var pcs = new TempDir())
            {
                cloud.WriteFile(@"jogos\ps2\Jogo Caseiro.iso", TempDir.RandomBytes(1000));
                var a = Pc(pcs, "A", cloud);
                var source = new FakeMediaSource(MediaSourceIds.Libretro).Media(Stem, MediaFields.Cover);

                var first = Import(a, cloud, "PC-A", source);
                var second = Import(a, cloud, "PC-A", source);

                Assert.Equal(new[] { "ps2/Jogo Caseiro.iso" }, first.NotFound);
                Assert.Empty(second.NotFound);
                Assert.Equal(1, source.Calls);
            }
        }

        [Fact]
        public void What_the_person_chose_in_the_es_is_never_replaced_by_the_automatic_import()
        {
            using (var cloud = new TempDir())
            using (var pcs = new TempDir())
            {
                cloud.WriteFile(@"jogos\ps2\" + Game, TempDir.RandomBytes(1000));
                var a = Pc(pcs, "A", cloud);
                Edit(a, g =>
                {
                    g.SetElementValue("genre", "Escolhido pela pessoa");
                    g.Add(new XElement("scrap", new XAttribute("name", "Retroboat"), new XAttribute("date", "20261114T154500")));
                });
                var source = new FakeMediaSource(MediaSourceIds.Wikipedia).Text(Stem, MediaFields.Genre, "Ação");

                Import(a, cloud, "PC-A", source);

                Assert.Equal(0, source.Calls);
                Assert.Equal("Escolhido pela pessoa", (string)Find(a).Element("genre"));
            }
        }

        [Fact]
        public void The_old_list_of_this_pc_moves_into_the_cloud_log()
        {
            using (var cloud = new TempDir())
            using (var pcs = new TempDir())
            {
                cloud.WriteFile(@"jogos\ps2\" + Game, TempDir.RandomBytes(1000));
                var a = Pc(pcs, "A", cloud);
                var legacy = MediaImportLog.LegacyInfoFile(a);
                Directory.CreateDirectory(Path.GetDirectoryName(legacy));
                File.WriteAllText(legacy, "ps2/" + Game + "\t2026-11-01T10:00:00Z\n");
                var source = new FakeMediaSource(MediaSourceIds.Libretro);

                Import(a, cloud, "PC-A", source);

                // As informações já tinham sido buscadas: só as mídias vão às fontes.
                Assert.All(source.Wanted, wanted => Assert.All(wanted, field => Assert.True(MediaFields.Find(field).IsMedia)));
                Assert.False(File.Exists(legacy));
                Assert.Contains("informacoes\tps2/" + Game, File.ReadAllText(cloud.Combine(@"midia\importacao\PC-A.txt")));
            }
        }

        [Fact]
        public void Without_the_drive_the_automatic_import_waits()
        {
            using (var cloud = new TempDir())
            using (var pcs = new TempDir())
            {
                cloud.WriteFile(@"jogos\ps2\" + Game, TempDir.RandomBytes(1000));
                var a = Pc(pcs, "A", cloud);
                var source = new FakeMediaSource(MediaSourceIds.Libretro).Media(Stem, MediaFields.Cover);

                var report = StartupSync.Run(a, pcs.Combine("drive-fechado"), a.DefaultRomsPath, new CloudMediaOptions { Automatic = true }, MediaTest.Search(source));

                Assert.Null(report.AutoImport);
                Assert.Equal(0, source.Calls);
            }
        }

        [Fact]
        public void A_search_in_the_es_goes_to_the_cloud_when_the_es_closes()
        {
            using (var cloud = new TempDir())
            using (var pcs = new TempDir())
            {
                cloud.WriteFile(@"jogos\ps2\" + Game, TempDir.RandomBytes(1000));
                var a = Pc(pcs, "A", cloud);
                pcs.WriteText(@"A\emulationstation\.emulationstation\es_systems.cfg",
                    "<systemList><system><name>ps2</name><path>~\\..\\roms\\ps2</path></system></systemList>");
                // O scraper do ES grava a imagem na pasta de jogos e o gamelist ao sair.
                pcs.WriteFile(@"A\roms\ps2\images\" + Stem + "-image.png", new byte[] { 5, 5 });
                Edit(a, g =>
                {
                    g.SetElementValue("desc", "Do PROCURAR MÍDIAS.");
                    g.SetElementValue("image", "./images/" + Stem + "-image.png");
                });

                var result = StartupSync.SyncMediaAfterEs(a, cloud.Path, a.DefaultRomsPath, new CloudMediaOptions());

                Assert.Empty(result.Errors);
                Assert.Equal(1, result.FilesUploaded);
                Assert.True(File.Exists(cloud.Combine(@"midia\ps2\imagens\" + Stem + "-image.png")));
                Assert.False(Directory.Exists(pcs.Combine(@"A\roms\ps2\images")));
                Assert.Contains("Do PROCURAR MÍDIAS.", File.ReadAllText(Directory.GetFiles(cloud.Combine(@"midia\ps2\metadados")).Single()));
            }
        }
    }
}
