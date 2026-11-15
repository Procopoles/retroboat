using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Xml.Linq;
using Retroboat.Cloud;
using Retroboat.Library;
using Retroboat.Media;
using Retroboat.Media.Sources;
using Xunit;

namespace Retroboat.Tests
{
    /// <summary>Mídias e informações automáticas dos jogos novos pelo PERSONALIZADO (RB-038).</summary>
    public class AutoImportTests
    {
        const string GodHand = "God Hand (USA)";

        static RetroBatInstall Install(TempDir dir) => new RetroBatInstall(dir.Combine("RetroBat"));

        static string Gamelist(RetroBatInstall install) => Path.Combine(install.GamelistsPath, "ps2", "gamelist.xml");

        static XElement Find(RetroBatInstall install, string file = GodHand + ".iso") =>
            CloudLibrarySync.FindGame(XDocument.Load(Gamelist(install)), file);

        static RetroBatInstall CloudGame(TempDir cloud, TempDir pc, params string[] files)
        {
            foreach (var file in files.DefaultIfEmpty(GodHand + ".iso"))
                cloud.WriteFile(@"jogos\ps2\" + file, TempDir.RandomBytes(1000));

            var install = Install(pc);
            new CloudLibrarySync(install, install.DefaultRomsPath, new CloudLibrary(cloud.Combine("jogos"))).Apply();
            return install;
        }

        static void Edit(RetroBatInstall install, System.Action<XElement> change, string file = GodHand + ".iso")
        {
            var gamelist = XDocument.Load(Gamelist(install));
            change(CloudLibrarySync.FindGame(gamelist, file));
            gamelist.Save(Gamelist(install));
        }

        /// <summary>O HfsDB com capa e vídeo, o ScreenScraper com telas, logo e descrição em português.</summary>
        static (FakeMediaSource Hfs, FakeMediaSource Ss) FullSources(string name = GodHand)
        {
            var hfs = new FakeMediaSource(MediaSourceIds.HfsDb).Media(name, MediaFields.Cover, ".jpg").Media(name, MediaFields.Video, ".mp4");
            var ss = new FakeMediaSource(MediaSourceIds.ScreenScraper)
                .Media(name, MediaFields.Screenshot).Media(name, MediaFields.TitleScreen).Media(name, MediaFields.Logo)
                .Text(name, MediaFields.Name, "God Hand").Text(name, MediaFields.Genre, "Ação / Beat 'em up").Text(name, MediaFields.Players, "1");
            return (hfs, ss);
        }

        [Fact]
        public void A_new_cloud_game_gets_cover_screens_logo_video_and_info_like_on_steam()
        {
            using (var cloud = new TempDir())
            using (var pc = new TempDir())
            {
                var install = CloudGame(cloud, pc);
                var (hfs, ss) = FullSources();
                var http = new FakeMediaHttp();

                var result = new AutoImport(install, install.DefaultRomsPath, MediaTest.Search(hfs, ss), http: http).Apply();

                Assert.Equal(1, result.GamesFilled);
                Assert.Equal(5, result.FilesDownloaded); // capa, tela do jogo, tela de título, logo e vídeo
                var game = Find(install);
                Assert.Equal("~/.emulationstation/media/ps2/images/God Hand (USA)-thumb.jpg", (string)game.Element("thumbnail"));
                Assert.Equal("~/.emulationstation/media/ps2/images/God Hand (USA)-thumb.jpg", (string)game.Element("boxart"));
                Assert.Equal("~/.emulationstation/media/ps2/images/God Hand (USA)-image.png", (string)game.Element("image"));
                Assert.Equal("~/.emulationstation/media/ps2/images/God Hand (USA)-titleshot.png", (string)game.Element("titleshot"));
                Assert.Equal("~/.emulationstation/media/ps2/images/God Hand (USA)-marquee.png", (string)game.Element("marquee"));
                Assert.Equal("~/.emulationstation/media/ps2/videos/God Hand (USA)-video.mp4", (string)game.Element("video"));
                Assert.True(File.Exists(pc.Combine(@"RetroBat\emulationstation\.emulationstation\media\ps2\videos\God Hand (USA)-video.mp4")));
                Assert.Equal("God Hand", (string)game.Element("name"));
                Assert.Equal("Ação / Beat 'em up", (string)game.Element("genre"));
                Assert.Equal(0, new FileInfo(Path.Combine(install.DefaultRomsPath, "ps2", GodHand + ".iso")).Length); // nem precisou baixar o jogo
            }
        }

        [Fact]
        public void Without_a_game_screen_the_cover_is_also_the_image_with_one_download()
        {
            using (var cloud = new TempDir())
            using (var pc = new TempDir())
            {
                var install = CloudGame(cloud, pc);
                var libretro = new FakeMediaSource(MediaSourceIds.Libretro).Media(GodHand, MediaFields.Cover);
                var http = new FakeMediaHttp();

                new AutoImport(install, install.DefaultRomsPath, MediaTest.Search(libretro), http: http).Apply();

                var game = Find(install);
                Assert.Equal((string)game.Element("thumbnail"), (string)game.Element("image"));
                Assert.Single(http.Downloads);
                Assert.Null(game.Element("titleshot"));
            }
        }

        [Fact]
        public void Media_already_on_the_pc_is_kept_and_only_what_is_missing_is_fetched()
        {
            using (var cloud = new TempDir())
            using (var pc = new TempDir())
            {
                var install = CloudGame(cloud, pc);
                pc.WriteFile(@"RetroBat\emulationstation\.emulationstation\media\ps2\images\da-nuvem.png", new byte[] { 7 });
                Edit(install, g => g.SetElementValue("thumbnail", "~/.emulationstation/media/ps2/images/da-nuvem.png"));
                var (hfs, ss) = FullSources();
                var http = new FakeMediaHttp();

                new AutoImport(install, install.DefaultRomsPath, MediaTest.Search(hfs, ss), http: http).Apply();

                var game = Find(install);
                Assert.Equal("~/.emulationstation/media/ps2/images/da-nuvem.png", (string)game.Element("thumbnail"));
                Assert.DoesNotContain(http.Downloads, d => d.Contains("/thumbnail/"));
                Assert.NotNull(game.Element("video"));
                Assert.DoesNotContain(MediaFields.Cover, hfs.Wanted.Single());
            }
        }

        [Fact]
        public void One_game_that_fails_does_not_stop_the_others_and_is_tried_again()
        {
            using (var cloud = new TempDir())
            using (var pc = new TempDir())
            {
                var install = CloudGame(cloud, pc, GodHand + ".iso", "SSX 3 (USA).iso");
                var libretro = new FakeMediaSource(MediaSourceIds.Libretro).Media(GodHand, MediaFields.Cover).Media("SSX 3 (USA)", MediaFields.Cover);
                var http = new FakeMediaHttp { FailOn = "God%20Hand" };

                var result = new AutoImport(install, install.DefaultRomsPath, MediaTest.Search(libretro), http: http).Apply();

                Assert.Equal(1, result.GamesFilled);
                Assert.Single(result.Errors);
                Assert.Contains("God Hand", result.Errors[0]);
                Assert.NotNull(Find(install, "SSX 3 (USA).iso").Element("thumbnail"));

                http.FailOn = null;
                var again = new AutoImport(install, install.DefaultRomsPath, MediaTest.Search(libretro), http: http).Apply();
                Assert.Equal(1, again.GamesFilled);
                Assert.NotNull(Find(install).Element("thumbnail"));
            }
        }

        [Fact]
        public void An_unknown_game_is_reported_and_not_asked_again()
        {
            using (var cloud = new TempDir())
            using (var pc = new TempDir())
            {
                var install = CloudGame(cloud, pc, "meu jogo caseiro.iso");
                var libretro = new FakeMediaSource(MediaSourceIds.Libretro);

                var first = new AutoImport(install, install.DefaultRomsPath, MediaTest.Search(libretro)).Apply();
                var second = new AutoImport(install, install.DefaultRomsPath, MediaTest.Search(libretro)).Apply();

                Assert.Equal(new[] { "ps2/meu jogo caseiro.iso" }, first.NotFound);
                Assert.Empty(second.NotFound);
                Assert.Equal(1, libretro.Calls);
            }
        }

        [Fact]
        public void A_source_that_is_down_does_not_block_the_others_and_the_game_is_tried_again()
        {
            using (var cloud = new TempDir())
            using (var pc = new TempDir())
            {
                var install = CloudGame(cloud, pc);
                var ss = new FakeMediaSource(MediaSourceIds.ScreenScraper) { Fail = new WebException("sem conexão") };
                var wiki = new FakeMediaSource(MediaSourceIds.Wikipedia).Text(GodHand, MediaFields.Genre, "Beat 'em up");

                var result = new AutoImport(install, install.DefaultRomsPath, MediaTest.Search(ss, wiki), http: new FakeMediaHttp()).Apply();
                Assert.Contains(result.Errors, e => e.Contains("SCREENSCRAPER"));
                Assert.Equal("Beat 'em up", (string)Find(install).Element("genre"));

                ss.Fail = null;
                ss.Text(GodHand, MediaFields.Developer, "Clover Studio");
                new AutoImport(install, install.DefaultRomsPath, MediaTest.Search(ss, wiki), http: new FakeMediaHttp()).Apply();
                Assert.Equal(2, ss.Calls);
                Assert.Equal("Clover Studio", (string)Find(install).Element("developer"));
            }
        }

        [Fact]
        public void Text_from_a_source_replaces_the_placeholder_and_the_corrupt_warning_stays_on_top()
        {
            using (var cloud = new TempDir())
            using (var pc = new TempDir())
            {
                var install = CloudGame(cloud, pc);
                Edit(install, g =>
                {
                    g.SetElementValue("desc", "Corrompido na nuvem: arquivo zerado.");
                    g.SetElementValue("players", "1-2");
                });
                var ss = new FakeMediaSource(MediaSourceIds.ScreenScraper).Text(GodHand, MediaFields.Description, "Um beat 'em up da Clover.");

                new AutoImport(install, install.DefaultRomsPath, MediaTest.Search(ss)).Apply();

                var game = Find(install);
                Assert.Equal("Corrompido na nuvem: arquivo zerado.\n\nUm beat 'em up da Clover.", (string)game.Element("desc"));
                Assert.Equal("1-2", (string)game.Element("players")); // nenhuma fonte tem: fica como estava
            }
        }

        [Fact]
        public void Games_searched_by_hand_or_already_imported_are_left_alone()
        {
            using (var cloud = new TempDir())
            using (var pc = new TempDir())
            {
                var install = CloudGame(cloud, pc, GodHand + ".iso", "Ico (USA).iso");
                Edit(install, g => g.Add(new XElement("scrap", new XAttribute("name", "Personalizado"), new XAttribute("date", "20261115T100000"))));
                var log = MediaImportLog.Local(install);
                log.Record(MediaImportKind.Info, "ps2", "Ico (USA).iso", found: true);
                log.Record(MediaImportKind.Images, "ps2", "Ico (USA).iso", found: true);
                log.Save();
                var libretro = new FakeMediaSource(MediaSourceIds.Libretro).Media(GodHand, MediaFields.Cover).Media("Ico (USA)", MediaFields.Cover);

                var result = new AutoImport(install, install.DefaultRomsPath, MediaTest.Search(libretro), http: new FakeMediaHttp()).Apply();

                Assert.Equal(0, result.GamesFilled);
                Assert.Equal(0, libretro.Calls);
            }
        }

        [Fact]
        public void A_game_imported_before_without_info_gets_only_the_info_now()
        {
            using (var cloud = new TempDir())
            using (var pc = new TempDir())
            {
                var install = CloudGame(cloud, pc);
                var log = MediaImportLog.Local(install);
                log.Record(MediaImportKind.Images, "ps2", GodHand + ".iso", found: true);
                log.Save();
                var (hfs, ss) = FullSources();

                new AutoImport(install, install.DefaultRomsPath, MediaTest.Search(hfs, ss), http: new FakeMediaHttp()).Apply();

                var game = Find(install);
                Assert.Equal("God Hand", (string)game.Element("name"));
                Assert.Null(game.Element("thumbnail"));
                Assert.All(hfs.Wanted.Concat(ss.Wanted), wanted => Assert.All(wanted, field => Assert.False(MediaFields.Find(field).IsMedia)));
            }
        }

        [Fact]
        public void Progress_goes_game_by_game()
        {
            using (var cloud = new TempDir())
            using (var pc = new TempDir())
            {
                var install = CloudGame(cloud, pc, GodHand + ".iso", "SSX 3 (USA).iso");
                var libretro = new FakeMediaSource(MediaSourceIds.Libretro).Media(GodHand, MediaFields.Cover);
                var steps = new List<string>();

                new AutoImport(install, install.DefaultRomsPath, MediaTest.Search(libretro), http: new FakeMediaHttp())
                    .Apply((done, total, game) => steps.Add(done + "/" + total + " " + game));

                Assert.Equal(new[] { "1/2 God Hand (USA)", "2/2 SSX 3 (USA)" }, steps);
                Assert.Equal(1, libretro.PrepareCalls); // os dois numa consulta só, nas fontes que aceitam
            }
        }

        [Fact]
        public void Startup_imports_new_games_sends_them_to_the_cloud_and_reports_progress()
        {
            using (var cloud = new TempDir())
            using (var pc = new TempDir())
            {
                cloud.WriteFile(@"jogos\ps2\" + GodHand + ".iso", TempDir.RandomBytes(1000));
                var install = Install(pc);
                var (hfs, ss) = FullSources();
                var progress = new List<int>();

                var report = StartupSync.Run(install, cloud.Path, install.DefaultRomsPath, new CloudMediaOptions { Automatic = true },
                    MediaTest.Search(hfs, ss), new FakeMediaHttp(), progress: (percent, step) => progress.Add(percent));

                Assert.Equal(1, report.AutoImport.GamesFilled);
                Assert.Contains("Mídias automáticas: 1 jogo(s)", report.Messages);
                Assert.True(File.Exists(cloud.Combine(@"midia\ps2\imagens\God Hand (USA)-thumb.jpg")));
                Assert.Contains("God Hand (USA)-thumb.jpg", File.ReadAllText(Directory.GetFiles(cloud.Combine(@"midia\ps2\metadados")).Single()));
                Assert.Equal(progress.OrderBy(p => p), progress);
                Assert.Equal(100, progress.Last());
            }
        }

        [Fact]
        public void Startup_does_not_import_unless_asked_or_without_the_drive()
        {
            using (var cloud = new TempDir())
            using (var pc = new TempDir())
            {
                cloud.WriteFile(@"jogos\ps2\" + GodHand + ".iso", TempDir.RandomBytes(1000));
                var install = Install(pc);
                var libretro = new FakeMediaSource(MediaSourceIds.Libretro).Media(GodHand, MediaFields.Cover);

                var off = StartupSync.Run(install, cloud.Path, install.DefaultRomsPath, new CloudMediaOptions(), MediaTest.Search(libretro));
                var closed = StartupSync.Run(install, pc.Combine("drive-fechado"), install.DefaultRomsPath, new CloudMediaOptions { Automatic = true }, MediaTest.Search(libretro));

                Assert.Null(off.AutoImport);
                Assert.Null(closed.AutoImport);
                Assert.Equal(0, libretro.Calls);
            }
        }
    }
}
