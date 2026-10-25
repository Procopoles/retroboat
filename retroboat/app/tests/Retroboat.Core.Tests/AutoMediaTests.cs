using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Retroboat.Cloud;
using Retroboat.Library;
using Retroboat.Media;
using Xunit;

namespace Retroboat.Tests
{
    public class AutoMediaTests
    {
        const string Ps2 = "Sony - PlayStation 2";

        /// <summary>Fonte de imagens em memória: nenhum teste vai à internet.</summary>
        sealed class FakeThumbnails : IThumbnailSource
        {
            readonly Dictionary<ThumbnailKind, string[]> _names;
            public int ListCalls;
            public List<string> Downloads = new List<string>();
            public string FailOn;

            public FakeThumbnails(string[] boxarts, string[] snaps = null, string[] titles = null)
            {
                _names = new Dictionary<ThumbnailKind, string[]>
                {
                    [ThumbnailKind.Boxart] = boxarts,
                    [ThumbnailKind.Snap] = snaps ?? new string[0],
                    [ThumbnailKind.Title] = titles ?? new string[0]
                };
            }

            public IReadOnlyCollection<string> List(string playlist, ThumbnailKind kind)
            {
                ListCalls++;
                return playlist == Ps2 ? _names[kind] : new string[0];
            }

            public byte[] Download(string playlist, ThumbnailKind kind, string name)
            {
                Downloads.Add(kind + ":" + name);
                if (name == FailOn)
                    throw new System.Net.WebException("conexão interrompida");

                return _names[kind].Contains(name) ? new byte[] { (byte)kind, 1, 2, 3 } : null;
            }
        }

        static RetroBatInstall Install(TempDir dir) => new RetroBatInstall(dir.Combine("RetroBat"));

        static string Gamelist(RetroBatInstall install) => Path.Combine(install.GamelistsPath, "ps2", "gamelist.xml");

        static void SyncLibrary(RetroBatInstall install, TempDir cloud) =>
            new CloudLibrarySync(install, install.DefaultRomsPath, new CloudLibrary(cloud.Combine("jogos"))).Apply();

        [Theory]
        [InlineData("Clock Tower 3 (Europe) (En,Fr,De,Es,It)", "Clock Tower 3 (Europe) (En,Fr,De,Es,It)")]
        [InlineData("Ico (Europe) (En_Fr_De_Es_It)", "Ico (Europe) (En,Fr,De,Es,It)")]
        [InlineData("The Punisher (USA)", "Punisher, The (USA)")]
        [InlineData("Tony Hawks Pro Skater 4 (USA) (v2.01)", "Tony Hawk's Pro Skater 4 (USA) (v2.01)")]
        [InlineData("Grand Theft Auto - Liberty City Stories", "Grand Theft Auto - Liberty City Stories (USA)")]
        [InlineData("Fatal Frame II (U) (Undub v1.01)", "Fatal Frame II - Crimson Butterfly (USA)")]
        [InlineData("Shadow of the Colossus (Europe)", "Shadow of the Colossus (Europe, Australia)")]
        public void Finds_the_libretro_name_for_common_variations(string file, string expected)
        {
            var index = new ThumbnailIndex(new[]
            {
                "Clock Tower 3 (Europe) (En,Fr,De,Es,It)",
                "Ico (Europe) (En,Fr,De,Es,It)",
                "Ico (USA)",
                "Punisher, The (USA)",
                "Tony Hawk's Pro Skater 4 (USA) (v2.01)",
                "Grand Theft Auto - Liberty City Stories (Europe) (En,Fr,De,Es,It)",
                "Grand Theft Auto - Liberty City Stories (USA)",
                "Fatal Frame II - Crimson Butterfly (Japan)",
                "Fatal Frame II - Crimson Butterfly (USA)",
                "Shadow of the Colossus (USA)",
                "Shadow of the Colossus (Europe, Australia)"
            });

            Assert.Equal(expected, index.Find(file));
        }

        [Fact]
        public void A_subtitle_is_not_confused_with_another_game_of_the_series()
        {
            var index = new ThumbnailIndex(new[] { "Need for Speed - Carbon (USA)", "Need for Speed - Most Wanted (USA)" });

            Assert.Null(index.Find("Need for Speed - Carbon - Collector's Edition"));
            Assert.Null(index.Find("Need for Speed"));
        }

        [Fact]
        public void Reads_the_names_of_a_listing_page()
        {
            var html = "<a href=\"?C=N;O=D\">Name</a><a href=\"/Sony%20-%20PlayStation%202/\">Parent</a>" +
                       "<a href=\"God%20Hand%20(USA).png\">God Hand (USA).png</a>" +
                       "<a href=\"Punisher%2C%20The%20(USA).png\">x</a>";

            Assert.Equal(new[] { "God Hand (USA)", "Punisher, The (USA)" }, LibretroThumbnails.ParseListing(html));
        }

        [Fact]
        public void A_cloud_game_without_images_gets_cover_screen_and_title()
        {
            using (var cloud = new TempDir())
            using (var pc = new TempDir())
            {
                cloud.WriteFile(@"jogos\ps2\God Hand (USA).iso", TempDir.RandomBytes(1000));
                var install = Install(pc);
                SyncLibrary(install, cloud);
                var source = new FakeThumbnails(new[] { "God Hand (USA)" }, new[] { "God Hand (USA)" }, new[] { "God Hand (USA)" });

                var result = new AutoMedia(install, install.DefaultRomsPath, source).Apply();

                Assert.Equal(1, result.GamesFilled);
                Assert.Equal(3, result.FilesDownloaded);
                var game = CloudLibrarySync.FindGame(XDocument.Load(Gamelist(install)), "God Hand (USA).iso");
                Assert.Equal("~/.emulationstation/media/ps2/images/God Hand (USA)-thumb.png", (string)game.Element("thumbnail"));
                Assert.Equal("~/.emulationstation/media/ps2/images/God Hand (USA)-thumb.png", (string)game.Element("boxart"));
                Assert.Equal("~/.emulationstation/media/ps2/images/God Hand (USA)-image.png", (string)game.Element("image"));
                Assert.Equal("~/.emulationstation/media/ps2/images/God Hand (USA)-titleshot.png", (string)game.Element("titleshot"));
                Assert.True(File.Exists(pc.Combine(@"RetroBat\emulationstation\.emulationstation\media\ps2\images\God Hand (USA)-thumb.png")));
                Assert.StartsWith(GameDescription.CloudPrefix, (string)game.Element("desc"));
            }
        }

        [Fact]
        public void Without_a_screenshot_the_cover_is_also_the_image()
        {
            using (var cloud = new TempDir())
            using (var pc = new TempDir())
            {
                cloud.WriteFile(@"jogos\ps2\Ico (Europe) (En_Fr_De_Es_It).iso", TempDir.RandomBytes(1000));
                var install = Install(pc);
                SyncLibrary(install, cloud);

                new AutoMedia(install, install.DefaultRomsPath, new FakeThumbnails(new[] { "Ico (Europe) (En,Fr,De,Es,It)" })).Apply();

                var game = CloudLibrarySync.FindGame(XDocument.Load(Gamelist(install)), "Ico (Europe) (En_Fr_De_Es_It).iso");
                Assert.Equal((string)game.Element("thumbnail"), (string)game.Element("image"));
                Assert.Null(game.Element("titleshot"));
            }
        }

        [Fact]
        public void Games_that_already_have_images_are_left_alone_and_nothing_is_fetched()
        {
            using (var cloud = new TempDir())
            using (var pc = new TempDir())
            {
                cloud.WriteFile(@"jogos\ps2\God Hand (USA).iso", TempDir.RandomBytes(1000));
                var install = Install(pc);
                SyncLibrary(install, cloud);
                pc.WriteFile(@"RetroBat\emulationstation\.emulationstation\media\ps2\images\scraper.png", new byte[] { 7 });
                var gamelist = XDocument.Load(Gamelist(install));
                CloudLibrarySync.FindGame(gamelist, "God Hand (USA).iso").SetElementValue("image", "~/.emulationstation/media/ps2/images/scraper.png");
                gamelist.Save(Gamelist(install));
                var source = new FakeThumbnails(new[] { "God Hand (USA)" });

                var result = new AutoMedia(install, install.DefaultRomsPath, source).Apply();

                Assert.Equal(0, result.GamesFilled);
                Assert.Equal(0, source.ListCalls);
                Assert.Null(CloudLibrarySync.FindGame(XDocument.Load(Gamelist(install)), "God Hand (USA).iso").Element("thumbnail"));
            }
        }

        [Fact]
        public void One_game_that_fails_does_not_stop_the_others()
        {
            using (var cloud = new TempDir())
            using (var pc = new TempDir())
            {
                cloud.WriteFile(@"jogos\ps2\God Hand (USA).iso", TempDir.RandomBytes(1000));
                cloud.WriteFile(@"jogos\ps2\SSX 3 (USA).iso", TempDir.RandomBytes(1000));
                var install = Install(pc);
                SyncLibrary(install, cloud);
                var source = new FakeThumbnails(new[] { "God Hand (USA)", "SSX 3 (USA)" }) { FailOn = "God Hand (USA)" };

                var result = new AutoMedia(install, install.DefaultRomsPath, source).Apply();

                Assert.Equal(1, result.GamesFilled);
                Assert.Single(result.Errors);
                Assert.Contains("God Hand", result.Errors[0]);
                Assert.NotNull(CloudLibrarySync.FindGame(XDocument.Load(Gamelist(install)), "SSX 3 (USA).iso").Element("thumbnail"));
            }
        }

        [Fact]
        public void Unknown_names_are_reported_not_guessed()
        {
            using (var cloud = new TempDir())
            using (var pc = new TempDir())
            {
                cloud.WriteFile(@"jogos\ps2\meu jogo caseiro.iso", TempDir.RandomBytes(1000));
                var install = Install(pc);
                SyncLibrary(install, cloud);

                var result = new AutoMedia(install, install.DefaultRomsPath, new FakeThumbnails(new[] { "God Hand (USA)" })).Apply();

                Assert.Equal(0, result.GamesFilled);
                Assert.Equal(new[] { "ps2/meu jogo caseiro.iso" }, result.NotFound);
            }
        }

        [Fact]
        public void Startup_sends_the_automatic_covers_to_the_cloud_right_away()
        {
            using (var cloud = new TempDir())
            using (var pc = new TempDir())
            {
                cloud.WriteFile(@"jogos\ps2\God Hand (USA).iso", TempDir.RandomBytes(1000));
                var install = Install(pc);
                var options = new CloudMediaOptions { Automatic = true };

                var report = StartupSync.Run(install, cloud.Path, install.DefaultRomsPath, options, new FakeThumbnails(new[] { "God Hand (USA)" }));

                Assert.Equal(1, report.AutoMedia.GamesFilled);
                Assert.Contains(report.Messages, m => m.StartsWith("Capas automáticas"));
                Assert.True(File.Exists(cloud.Combine(@"midia\ps2\imagens\God Hand (USA)-thumb.png")));
                Assert.Contains("God Hand (USA)-thumb.png", File.ReadAllText(Directory.GetFiles(cloud.Combine(@"midia\ps2\metadados")).Single()));
            }
        }

        [Fact]
        public void Startup_does_not_fetch_covers_unless_asked()
        {
            using (var cloud = new TempDir())
            using (var pc = new TempDir())
            {
                cloud.WriteFile(@"jogos\ps2\God Hand (USA).iso", TempDir.RandomBytes(1000));
                var install = Install(pc);
                var source = new FakeThumbnails(new[] { "God Hand (USA)" });

                var report = StartupSync.Run(install, cloud.Path, install.DefaultRomsPath, new CloudMediaOptions(), source);

                Assert.Null(report.AutoMedia);
                Assert.Equal(0, source.ListCalls);
            }
        }
    }
}
