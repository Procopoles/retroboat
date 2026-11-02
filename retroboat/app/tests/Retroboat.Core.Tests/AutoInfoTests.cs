using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Xml.Linq;
using Retroboat.Cloud;
using Retroboat.Library;
using Retroboat.Media;
using Xunit;

namespace Retroboat.Tests
{
    public class AutoInfoTests
    {
        /// <summary>Fonte de informações em memória: nenhum teste vai à internet.</summary>
        sealed class FakeInfo : IGameInfoSource
        {
            readonly Dictionary<string, GameInfo> _games = new Dictionary<string, GameInfo>();
            public int Calls;
            public Exception Fail;

            public FakeInfo Add(string stem, GameInfo info)
            {
                _games[stem] = info;
                return this;
            }

            public IDictionary<string, GameInfo> Find(string system, IReadOnlyCollection<string> stems)
            {
                Calls++;
                if (Fail != null)
                    throw Fail;

                return stems.Where(s => system == "ps2" && _games.ContainsKey(s)).ToDictionary(s => s, s => _games[s]);
            }
        }

        const string GodOfWar = "God of War (USA)";

        static readonly GameInfo Libretro = new GameInfo
        {
            Title = "God of War",
            Description = "God of War is an action-oriented adventure game with a strong focus on fast-paced combat.",
            Genre = "Ação",
            ReleaseDate = "20050322T000000",
            Developer = "Santa Monica Studio",
            Publisher = "Sony Computer Entertainment",
            Players = "1",
            Origin = GameInfoOrigin.Libretro
        };

        static readonly GameInfo Wikipedia = new GameInfo
        {
            Title = "God of War",
            Description = "God of War é um jogo eletrônico de ação-aventura desenvolvido pela Santa Monica Studio e publicado pela " +
                          "Sony Computer Entertainment. Foi lançado para PlayStation 2 em março de 2005.",
            DescriptionInPortuguese = true,
            Genre = "Ação-aventura",
            Origin = GameInfoOrigin.Wikidata,
            Developer = "Santa Monica Studio"
        };

        static RetroBatInstall Install(TempDir dir) => new RetroBatInstall(dir.Combine("RetroBat"));

        static string Gamelist(RetroBatInstall install) => Path.Combine(install.GamelistsPath, "ps2", "gamelist.xml");

        static XElement Game(RetroBatInstall install, string file = GodOfWar + ".iso") =>
            CloudLibrarySync.FindGame(XDocument.Load(Gamelist(install)), file);

        static RetroBatInstall CloudGame(TempDir cloud, TempDir pc, string file = GodOfWar + ".iso")
        {
            cloud.WriteFile(@"jogos\ps2\" + file, TempDir.RandomBytes(1000));
            var install = Install(pc);
            new CloudLibrarySync(install, install.DefaultRomsPath, new CloudLibrary(cloud.Combine("jogos"))).Apply();
            return install;
        }

        [Fact]
        public void A_game_that_was_not_downloaded_gets_its_page_like_on_steam()
        {
            using (var cloud = new TempDir())
            using (var pc = new TempDir())
            {
                var install = CloudGame(cloud, pc);

                var result = new AutoInfo(install, new[] { new FakeInfo().Add(GodOfWar, Libretro), new FakeInfo().Add(GodOfWar, Wikipedia) }).Apply();

                Assert.Equal(1, result.GamesFilled);
                var game = Game(install);
                Assert.Equal("God of War", (string)game.Element("name"));
                Assert.Equal(Wikipedia.Description, (string)game.Element("desc"));
                Assert.Equal("Ação-aventura", (string)game.Element("genre")); // o gênero específico vale mais que a categoria larga
                Assert.Equal("20050322T000000", (string)game.Element("releasedate"));
                Assert.Equal("Santa Monica Studio", (string)game.Element("developer"));
                Assert.Equal("Sony Computer Entertainment", (string)game.Element("publisher"));
                Assert.Equal("1", (string)game.Element("players"));
                Assert.Equal(0, new FileInfo(Path.Combine(install.DefaultRomsPath, "ps2", GodOfWar + ".iso")).Length);
            }
        }

        [Fact]
        public void A_portuguese_stub_loses_to_the_full_english_description()
        {
            using (var cloud = new TempDir())
            using (var pc = new TempDir())
            {
                var install = CloudGame(cloud, pc);
                var stub = new GameInfo { Description = "God of War é um jogo de 2005.", DescriptionInPortuguese = true, Origin = GameInfoOrigin.Wikidata };

                new AutoInfo(install, new[] { new FakeInfo().Add(GodOfWar, Libretro), new FakeInfo().Add(GodOfWar, stub) }).Apply();

                Assert.Equal(Libretro.Description, (string)Game(install).Element("desc"));
            }
        }

        [Fact]
        public void The_scraper_comes_last_and_stays_only_where_no_source_knows()
        {
            using (var cloud = new TempDir())
            using (var pc = new TempDir())
            {
                var install = CloudGame(cloud, pc);
                var gamelist = XDocument.Load(Gamelist(install));
                var game = CloudLibrarySync.FindGame(gamelist, GodOfWar + ".iso");
                game.SetElementValue("name", "God of War : Edição");
                game.SetElementValue("desc", "Descrição do scraper.");
                game.SetElementValue("genre", "Briga de rua");
                game.SetElementValue("players", "1-2");
                gamelist.Save(Gamelist(install));

                new AutoInfo(install, new[] { new FakeInfo().Add(GodOfWar, new GameInfo { Title = "God of War", Genre = "Ação-aventura", Origin = GameInfoOrigin.Wikidata }) }).Apply();

                game = Game(install);
                Assert.Equal("God of War", (string)game.Element("name"));
                Assert.Equal("Ação-aventura", (string)game.Element("genre"));
                Assert.Equal("Descrição do scraper.", (string)game.Element("desc"));
                Assert.Equal("1-2", (string)game.Element("players"));
            }
        }

        [Fact]
        public void Each_field_comes_from_the_source_that_gets_it_right_most()
        {
            using (var cloud = new TempDir())
            using (var pc = new TempDir())
            {
                var install = CloudGame(cloud, pc, "Shadow of the Colossus (USA).iso");
                var libretro = new GameInfo { Genre = "Ação", Developer = "Sony Computer Entertainment", Publisher = "Sony Computer Entertainment", ReleaseDate = "20051018T000000", Origin = GameInfoOrigin.Libretro };
                var wikidata = new GameInfo { Genre = "Ação-aventura", Developer = "Team Ico", Publisher = "Sony Interactive Entertainment", ReleaseDate = "20051018T000000", Origin = GameInfoOrigin.Wikidata };

                new AutoInfo(install, new[] { new FakeInfo().Add("Shadow of the Colossus (USA)", libretro), new FakeInfo().Add("Shadow of the Colossus (USA)", wikidata) }).Apply();

                var game = Game(install, "Shadow of the Colossus (USA).iso");
                Assert.Equal("Ação-aventura", (string)game.Element("genre"));
                Assert.Equal("Team Ico", (string)game.Element("developer"));
                Assert.Equal("Sony Computer Entertainment", (string)game.Element("publisher"));
            }
        }

        [Fact]
        public void The_corrupt_warning_stays_above_the_description()
        {
            using (var cloud = new TempDir())
            using (var pc = new TempDir())
            {
                var install = CloudGame(cloud, pc);
                var gamelist = XDocument.Load(Gamelist(install));
                CloudLibrarySync.FindGame(gamelist, GodOfWar + ".iso").SetElementValue("desc", "Corrompido na nuvem: arquivo zerado.");
                gamelist.Save(Gamelist(install));

                new AutoInfo(install, new[] { new FakeInfo().Add(GodOfWar, Libretro) }).Apply();

                Assert.Equal("Corrompido na nuvem: arquivo zerado.\n\n" + Libretro.Description, (string)Game(install).Element("desc"));
            }
        }

        [Fact]
        public void The_old_generic_line_goes_away_even_with_the_drive_closed()
        {
            using (var cloud = new TempDir())
            using (var pc = new TempDir())
            {
                var install = CloudGame(cloud, pc);
                CloudGame(cloud, pc, "Outro (USA).iso");
                var gamelist = XDocument.Load(Gamelist(install));
                CloudLibrarySync.FindGame(gamelist, GodOfWar + ".iso").SetElementValue("desc", "Na nuvem · 7,0 GB · baixa na primeira vez que você jogar.");
                CloudLibrarySync.FindGame(gamelist, "Outro (USA).iso").SetElementValue("desc", "Na nuvem · 1,0 KB · baixa na primeira vez que você jogar.");
                gamelist.Save(Gamelist(install));

                new AutoInfo(install, new[] { new FakeInfo().Add(GodOfWar, Libretro) }).Apply();
                Assert.Equal(Libretro.Description, (string)Game(install).Element("desc"));

                StartupSync.Run(install, pc.Combine("drive-fechado"), install.DefaultRomsPath);
                Assert.Null(Game(install, "Outro (USA).iso").Element("desc"));
            }
        }

        [Fact]
        public void An_unknown_game_is_not_asked_again_for_a_month()
        {
            using (var cloud = new TempDir())
            using (var pc = new TempDir())
            {
                var install = CloudGame(cloud, pc, "Jogo Caseiro.iso");
                var source = new FakeInfo();

                var first = new AutoInfo(install, new[] { source }).Apply();
                var second = new AutoInfo(install, new[] { source }).Apply();

                Assert.Equal(new[] { "ps2/Jogo Caseiro.iso" }, first.NotFound);
                Assert.Empty(second.NotFound);
                Assert.Equal(1, source.Calls);
            }
        }

        [Fact]
        public void A_source_that_is_down_does_not_block_the_other_and_is_tried_again()
        {
            using (var cloud = new TempDir())
            using (var pc = new TempDir())
            {
                var install = CloudGame(cloud, pc);
                var down = new FakeInfo { Fail = new WebException("sem conexão") };
                var up = new FakeInfo().Add(GodOfWar, new GameInfo { Genre = "Ação" });

                var result = new AutoInfo(install, new IGameInfoSource[] { down, up }).Apply();
                Assert.Single(result.Errors);
                Assert.Equal("Ação", (string)Game(install).Element("genre"));

                down.Fail = null;
                down.Add(GodOfWar, Libretro);
                new AutoInfo(install, new IGameInfoSource[] { down, up }).Apply();
                Assert.Equal(2, down.Calls);
                Assert.Equal("Santa Monica Studio", (string)Game(install).Element("developer"));
            }
        }

        [Fact]
        public void Startup_fills_the_info_and_sends_it_to_the_cloud()
        {
            using (var cloud = new TempDir())
            using (var pc = new TempDir())
            {
                cloud.WriteFile(@"jogos\ps2\" + GodOfWar + ".iso", TempDir.RandomBytes(1000));
                var install = Install(pc);
                var options = new CloudMediaOptions { Automatic = true };

                var report = StartupSync.Run(install, cloud.Path, install.DefaultRomsPath, options, new EmptyThumbnails(),
                                             new[] { new FakeInfo().Add(GodOfWar, Libretro) });

                Assert.Equal(1, report.AutoInfo.GamesFilled);
                Assert.Contains("Informações automáticas: 1 jogo(s)", report.Messages);
                Assert.Contains("Santa Monica Studio", File.ReadAllText(Directory.GetFiles(cloud.Combine(@"midia\ps2\metadados")).Single()));
            }
        }

        sealed class EmptyThumbnails : IThumbnailSource
        {
            public IReadOnlyCollection<string> List(string playlist, ThumbnailKind kind) => new string[0];
            public byte[] Download(string playlist, ThumbnailKind kind, string name) => null;
        }

        [Fact]
        public void Libretro_records_become_gamelist_fields()
        {
            const string dat = "clrmamepro (\n\tname \"Sony - PlayStation 2\"\n)\n\n" +
                               "game (\n\tname \"Haunting Ground (USA)\"\n\tserial \"SLUS-21075\"\n\tdescription \"Fiona wakes up in a \\\"castle\\\".\"\n" +
                               "\tdeveloper \"Capcom Production Studio 1\"\n\tpublisher \"Capcom\"\n\treleaseyear \"2005\"\n\treleasemonth \"5\"\n" +
                               "\treleaseday \"10\"\n\tusers \"2\"\n\tgenre \"Board / Mini Games\"\n\trom (\n\t\tserial \"SLUS-21075\"\n" +
                               "\t\tname \"Haunting Ground (USA).cue\"\n\t)\n)\n";

            var games = LibretroMetadata.Parse(dat);
            var info = LibretroMetadata.ToInfo("Haunting Ground (USA)", games["Haunting Ground (USA)"]);

            Assert.Single(games);
            Assert.Equal("Haunting Ground", info.Title);
            Assert.Equal("Fiona wakes up in a \"castle\".", info.Description);
            Assert.False(info.DescriptionInPortuguese);
            Assert.Equal("Tabuleiro / Minigames", info.Genre);
            Assert.Equal("20050510T000000", info.ReleaseDate);
            Assert.Equal("Capcom Production Studio 1", info.Developer);
            Assert.Equal("1-2", info.Players);
        }

        [Theory]
        [InlineData("God of War (USA)", "God of War")]
        [InlineData("Warriors, The (USA) (En,Fr,De,Es,It)", "The Warriors")]
        [InlineData("Grand Theft Auto - San Andreas (USA) (v3.00)", "Grand Theft Auto: San Andreas")]
        [InlineData("Shadow of the Colossus", "Shadow of the Colossus")]
        public void File_names_become_clean_titles(string stem, string title)
        {
            Assert.Equal(title, GameInfoText.CleanTitle(stem));
            Assert.Equal(title, GameInfoText.TitleCandidates(stem)[0]);
        }

        [Fact]
        public void Wikidata_results_keep_the_oldest_game_with_each_name()
        {
            const string json = @"{""head"":{},""results"":{""bindings"":[
                {""title"":{""type"":""literal"",""xml:lang"":""en"",""value"":""Black""},""item"":{""type"":""uri"",""value"":""http://www.wikidata.org/entity/Q2""},
                 ""released"":{""type"":""literal"",""value"":""2010-01-01T00:00:00Z""}},
                {""title"":{""type"":""literal"",""xml:lang"":""en"",""value"":""Black""},""item"":{""type"":""uri"",""value"":""http://www.wikidata.org/entity/Q1""},
                 ""name"":{""type"":""literal"",""value"":""Black""},""released"":{""type"":""literal"",""value"":""2006-02-28T00:00:00Z""},
                 ""developer"":{""type"":""literal"",""value"":""Criterion Games""},""genres"":{""type"":""literal"",""value"":""jogo eletrônico de tiro em primeira pessoa|jogo eletrônico de ação""},
                 ""modes"":{""type"":""literal"",""value"":""http://www.wikidata.org/entity/Q208850""},
                 ""ptwiki"":{""type"":""uri"",""value"":""https://pt.wikipedia.org/wiki/Black_(jogo_eletr%C3%B4nico)""}}]}}";

            var items = WikidataInfo.ParseResults(json);

            var black = Assert.Single(items).Value;
            Assert.Equal("2006-02-28T00:00:00Z", black.Released);
            Assert.Equal("Criterion Games", black.Developer);
            Assert.Equal("https://pt.wikipedia.org/wiki/Black_(jogo_eletr%C3%B4nico)", black.PtWiki);
            Assert.Equal("Ação / Tiro em primeira pessoa", GameInfoText.WikidataGenres(black.Genres));
            Assert.Equal("Ação-aventura", GameInfoText.WikidataGenres(new[] { "jogo eletrônico de ação-aventura", "videojogo com personagem LGBT", "ficção científica" }));
            var query = WikidataInfo.BuildQuery(new[] { "Black" }, "Q10680");
            Assert.Contains("wdt:P400 wd:Q10680", query);
            Assert.Contains("\"Black\"@mul", query);
            Assert.Contains("GROUP_CONCAT(DISTINCT STR(?devName)", query); // "Rockstar Toronto"@en e @mul são o mesmo nome
        }

        [Fact]
        public void Wikipedia_disambiguation_pages_are_not_descriptions()
        {
            Assert.Equal("Black é um jogo de tiro.", WikidataInfo.ParseSummary(@"{""type"":""standard"",""extract"":""Black é um jogo de tiro.""}"));
            Assert.Null(WikidataInfo.ParseSummary(@"{""type"":""disambiguation"",""extract"":""Black pode referir-se a:""}"));
        }
    }
}
