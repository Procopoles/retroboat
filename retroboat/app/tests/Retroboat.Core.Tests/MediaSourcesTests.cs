using System.Collections.Generic;
using System.Linq;
using Retroboat.Es;
using Retroboat.Library;
using Retroboat.Media;
using Retroboat.Media.Sources;
using Xunit;

namespace Retroboat.Tests
{
    /// <summary>As fontes do PERSONALIZADO (RB-038), o perfil de prioridades e o andamento do "Atualizar listas de jogos".</summary>
    public class MediaSourcesTests
    {
        const string FakeLogin = "devid=teste&devpassword=senha-falsa";

        // ---- Perfil ----

        [Fact]
        public void The_preset_is_the_best_source_of_each_field()
        {
            var profile = MediaProfile.Default;

            Assert.Equal(MediaSourceIds.HfsDb, profile.First(MediaFields.Cover));
            Assert.Equal(MediaSourceIds.HfsDb, profile.First(MediaFields.Video));
            Assert.Equal(MediaSourceIds.ScreenScraper, profile.First(MediaFields.Description));
            Assert.Equal(MediaSourceIds.Wikipedia, profile.First(MediaFields.Genre));
            Assert.Equal(MediaSourceIds.Libretro, profile.First(MediaFields.Publisher));
            Assert.Equal("", profile.ToString());
            Assert.All(MediaFields.All, f => Assert.NotEmpty(MediaProfile.Preset[f.Id]));
        }

        [Fact]
        public void A_choice_goes_first_and_the_rest_keeps_the_preset_order()
        {
            var profile = MediaProfile.Parse("thumbnail=screenscraper;video=none;desc=wikipedia,hfsdb;genre=fonte-que-nao-existe;campo=hfsdb");

            Assert.Equal(new[] { "screenscraper", "hfsdb", "libretro", "thegamesdb" }, profile.Order(MediaFields.Cover));
            Assert.True(profile.IsOff(MediaFields.Video));
            Assert.Equal(MediaProfile.Off, profile.First(MediaFields.Video));
            Assert.Equal(new[] { "wikipedia", "hfsdb", "screenscraper", "thegamesdb", "libretro" }, profile.Order(MediaFields.Description));
            Assert.Equal(MediaProfile.Preset[MediaFields.Genre], profile.Order(MediaFields.Genre));
            Assert.Equal("thumbnail=screenscraper;video=none;desc=wikipedia,hfsdb", profile.ToString());
        }

        [Fact]
        public void A_source_without_the_field_cannot_be_chosen_and_choosing_the_preset_clears_the_change()
        {
            var profile = MediaProfile.Parse("video=libretro;thumbnail=screenscraper");
            Assert.Equal(MediaProfile.Preset[MediaFields.Video], profile.Order(MediaFields.Video));

            profile.Set(MediaFields.Cover, MediaSourceIds.HfsDb);
            Assert.Equal("", profile.ToString());
        }

        [Fact]
        public void The_profile_comes_from_the_es_settings()
        {
            using (var dir = new TempDir())
            {
                var install = new RetroBatInstall(dir.Combine("RetroBat"));
                Assert.Equal("", MediaProfile.ForInstall(install).ToString());

                dir.WriteText(@"RetroBat\emulationstation\.emulationstation\es_settings.cfg",
                    "<config><string name=\"RetroboatMediaProfile\" value=\"video=screenscraper\" /></config>");
                Assert.Equal(MediaSourceIds.ScreenScraper, MediaProfile.ForInstall(install).First(MediaFields.Video));
            }
        }

        // ---- Nomes ----

        [Theory]
        [InlineData("Warriors, The (USA)", "The Warriors")]
        [InlineData("Pokémon - Edição Azul", "Pokemon: Edicao Azul")]
        [InlineData("Grand Theft Auto - San Andreas (USA) (v1.03)", "Grand Theft Auto: San Andreas")]
        public void Names_from_the_file_and_from_the_source_match(string file, string source)
        {
            Assert.Equal(MediaNames.Key(file), MediaNames.Key(source));
        }

        [Fact]
        public void Regions_dates_and_players_come_in_the_gamelist_format()
        {
            Assert.Equal(new[] { "us", "eu" }, MediaNames.FileRegions("Sonic The Hedgehog (USA, Europe) (Rev 1).md"));
            Assert.Empty(MediaNames.FileRegions("Jogo Caseiro.iso"));
            Assert.Equal("20051018T000000", MediaNames.Date("2005-10-18T00:00:00"));
            Assert.Equal("19910101T000000", MediaNames.Date("1991"));
            Assert.Equal("1-4", MediaNames.Players("+ de 4 joueurs"));
            Assert.Equal("1", MediaNames.Players("1 joueur"));
            Assert.Equal("1-2", MediaNames.Players("1-2"));
        }

        [Fact]
        public void Every_source_knows_the_main_systems()
        {
            foreach (var source in new[] { MediaSourceIds.ScreenScraper, MediaSourceIds.HfsDb, MediaSourceIds.TheGamesDb })
                Assert.NotEmpty(MediaSystems.Ids(source, "ps2"));

            Assert.Equal(new[] { "18", "36" }, MediaSystems.Ids(MediaSourceIds.TheGamesDb, "megadrive"));
            Assert.Empty(MediaSystems.Ids(MediaSourceIds.HfsDb, "fds"));
        }

        // ---- ScreenScraper ----

        const string SsGame = @"<Data><jeu id=""21887"">
  <noms><nom region=""ss"">Shadow of the Colossus SS</nom><nom region=""eu"">Shadow of the Colossus EU</nom><nom region=""us"">Shadow of the Colossus</nom></noms>
  <synopsis><synopsis langue=""en"">Bosses are common.</synopsis><synopsis langue=""pt"">Chefes são comuns.</synopsis></synopsis>
  <genres>
    <genre id=""1"" principale=""1"" langue=""en"">Action</genre><genre id=""1"" principale=""1"" langue=""pt"">Ação</genre>
    <genre id=""2"" principale=""0"" langue=""pt"">Ação / Aventura</genre>
  </genres>
  <dates><date region=""jp"">2005-12-08</date><date region=""us"">2005-10-18</date></dates>
  <developpeur>Team&amp;nbsp;Ico</developpeur><editeur>Sony</editeur><joueurs>1</joueurs>
  <medias>
    <media type=""box-2D"" region=""eu"" format=""png"">https://ss.test/box-eu?x=1</media>
    <media type=""box-2D"" region=""us"" format=""png"">https://ss.test/box-us?x=1</media>
    <media type=""ss"" region=""wor"" format=""jpg"">#screenscraperserveur#ss?x=1</media>
    <media type=""wheel"" region=""wor"" format=""png"">https://ss.test/wheel?x=1</media>
    <media type=""video-normalized"" format=""mp4"">https://ss.test/video?x=1</media>
  </medias>
</jeu></Data>";

        [Fact]
        public void ScreenScraper_finds_the_game_by_file_with_the_edition_of_the_file_and_portuguese_text()
        {
            var http = new FakeMediaHttp().Answer("https://api.screenscraper.fr/api2/jeuInfos.php", SsGame);
            var source = new ScreenScraperSource(http, FakeLogin, "Retroboat");

            var game = source.Find(new MediaQuery("ps2", "Shadow of the Colossus (USA).iso"), new string[0]);

            Assert.Contains("romnom=Shadow%20of%20the%20Colossus%20%28USA%29.iso", http.Requests.Single());
            Assert.Contains("systemeid=58", http.Requests.Single());
            Assert.Equal("Shadow of the Colossus", game.Texts[MediaFields.Name]);
            Assert.Equal("Chefes são comuns.", game.Texts[MediaFields.Description]);
            Assert.True(game.DescriptionInPortuguese);
            Assert.Equal("Ação / Aventura", game.Texts[MediaFields.Genre]);
            Assert.Equal("20051018T000000", game.Texts[MediaFields.ReleaseDate]);
            Assert.Equal("Team Ico", game.Texts[MediaFields.Developer]);
            Assert.Equal("https://ss.test/box-us?x=1&maxwidth=1000&maxheight=1000", game.Media[MediaFields.Cover].Url);
            Assert.Equal(".png", game.Media[MediaFields.Cover].Extension);
            Assert.Equal("https://www.screenscraper.fr/ss?x=1&maxwidth=1000&maxheight=1000", game.Media[MediaFields.Screenshot].Url);
            Assert.Equal("https://ss.test/video?x=1", game.Media[MediaFields.Video].Url);
            Assert.Equal(".mp4", game.Media[MediaFields.Video].Extension);
        }

        [Fact]
        public void ScreenScraper_falls_back_to_the_text_search_and_reports_the_daily_limit()
        {
            var http = new FakeMediaHttp()
                .Answer("https://api.screenscraper.fr/api2/jeuInfos.php", "Erreur : Rom/Iso/Dossier non trouvée !", 404)
                .Answer("https://api.screenscraper.fr/api2/jeuRecherche.php", "<Data><jeux>" + SsGame.Replace("<Data>", "").Replace("</Data>", "") + "</jeux></Data>");
            var source = new ScreenScraperSource(http, FakeLogin, "Retroboat");

            var game = source.Find(new MediaQuery("ps2", "SotC.iso", "Shadow of the Colossus"), new string[0]);
            Assert.Contains("recherche=Shadow%20of%20the%20Colossus", http.Requests.Single());
            Assert.NotNull(game);

            var limited = new ScreenScraperSource(new FakeMediaHttp().Answer("https://api.screenscraper.fr", "quota", 430), FakeLogin, "Retroboat");
            Assert.Throws<MediaSourceException>(() => limited.Find(new MediaQuery("ps2", "Jogo (USA).iso"), new string[0]));
            Assert.False(new ScreenScraperSource(http, null, null).Supports("ps2"));
        }

        // ---- HfsDB ----

        const string HfsGames = @"{""count"": 2, ""results"": [
  {""id"": 1, ""name_en"": ""Shadow Hearts"", ""medias"": []},
  {""id"": 2, ""name_en"": ""Shadow of the Colossus"", ""description_en"": ""Bosses."", ""description_pt"": ""Chefes."",
   ""released_at_US"": ""2005-10-18T00:00:00"",
   ""metadata"": [{""name"": ""players"", ""value"": ""1 joueur""}, {""name"": ""developer"", ""value"": ""Team Ico""}, {""name"": ""editor"", ""value"": ""SCEA""}],
   ""medias"": [
     {""type"": ""cover2d"", ""region"": ""US"", ""file"": ""https://hfs.test/us-back.jpg"", ""metadata"": [{""name"": ""cover2dtype"", ""value"": ""back""}]},
     {""type"": ""cover2d"", ""region"": ""PAL"", ""file"": ""https://hfs.test/pal-front.jpg"", ""metadata"": [{""name"": ""cover2dtype"", ""value"": ""front""}]},
     {""type"": ""cover2d"", ""region"": ""US"", ""file"": ""https://hfs.test/us-front.jpg"", ""metadata"": [{""name"": ""cover2dtype"", ""value"": ""front""}]},
     {""type"": ""video"", ""region"": ""PAL"", ""file"": ""https://hfs.test/pal.mp4"", ""metadata"": []},
     {""type"": ""logo"", ""region"": ""WORLD"", ""file"": ""https://hfs.test/logo.png"", ""metadata"": []}
   ]}
]}";

        [Fact]
        public void HfsDb_picks_the_game_with_the_same_name_and_the_cover_of_the_file_region()
        {
            var http = new FakeMediaHttp()
                .Answer("https://db.hfsplay.fr/api/v1/auth/token", "{\"token\": \"abc\"}")
                .Answer("https://db.hfsplay.fr/api/v1/games", HfsGames);
            var source = new HfsDbSource(http, "usuario:senha");

            var game = source.Find(new MediaQuery("ps2", "Shadow of the Colossus (USA).iso"), new string[0]);

            Assert.Equal("Shadow of the Colossus", game.Texts[MediaFields.Name]);
            Assert.Equal("Chefes.", game.Texts[MediaFields.Description]);
            Assert.True(game.DescriptionInPortuguese);
            Assert.Equal("1", game.Texts[MediaFields.Players]);
            Assert.Equal("SCEA", game.Texts[MediaFields.Publisher]);
            Assert.Equal("20051018T000000", game.Texts[MediaFields.ReleaseDate]);
            Assert.Equal("https://hfs.test/us-front.jpg", game.Media[MediaFields.Cover].Url);
            Assert.Equal(".jpg", game.Media[MediaFields.Cover].Extension);
            Assert.Equal("https://hfs.test/pal.mp4", game.Media[MediaFields.Video].Url);
            Assert.Equal("https://hfs.test/logo.png", game.Media[MediaFields.Logo].Url);

            var european = source.Find(new MediaQuery("ps2", "Shadow of the Colossus (Europe).iso"), new string[0]);
            Assert.Equal("https://hfs.test/pal-front.jpg", european.Media[MediaFields.Cover].Url);
            Assert.Single(http.Requests, r => r.Contains("auth/token")); // o token vale por 30 minutos
        }

        [Fact]
        public void HfsDb_reports_a_rejected_key()
        {
            var http = new FakeMediaHttp().Answer("https://db.hfsplay.fr/api/v1/auth/token", "{\"detail\": \"Invalid username/password.\"}", 403);

            Assert.Throws<MediaSourceException>(() => new HfsDbSource(http, "usuario:senha").Find(new MediaQuery("ps2", "Jogo (USA).iso"), new string[0]));
            Assert.False(new HfsDbSource(http, "sem-separador").Supports("ps2"));
        }

        // ---- TheGamesDB ----

        const string TgdbSearch = @"{""code"": 200, ""data"": {""count"": 1, ""games"": [
  {""id"": 1234, ""game_title"": ""Shadow of the Colossus"", ""release_date"": ""2005-10-18"", ""players"": 1, ""overview"": ""Bosses."", ""genres"": [1, 2]}]},
  ""include"": {""boxart"": {""base_url"": {""original"": ""https://cdn.test/original/"", ""large"": ""https://cdn.test/large/""},
    ""data"": {""1234"": [{""type"": ""boxart"", ""side"": ""back"", ""filename"": ""boxart/back/1234-1.jpg""}, {""type"": ""boxart"", ""side"": ""front"", ""filename"": ""boxart/front/1234-1.jpg""}]}}}}";

        const string TgdbImages = @"{""code"": 200, ""data"": {""base_url"": {""large"": ""https://cdn.test/large/""}, ""images"": {""1234"": [
  {""type"": ""fanart"", ""filename"": ""fanart/1234-1.jpg""}, {""type"": ""clearlogo"", ""filename"": ""clearlogo/1234.png""}]}}}";

        const string TgdbGenres = @"{""code"": 200, ""data"": {""genres"": {""1"": {""id"": 1, ""name"": ""Action""}, ""2"": {""id"": 2, ""name"": ""Adventure""}}}}";

        [Fact]
        public void TheGamesDb_brings_the_cover_with_the_search_and_asks_for_other_images_only_when_wanted()
        {
            var http = new FakeMediaHttp()
                .Answer("https://api.thegamesdb.net/v1/Games/ByGameName", TgdbSearch)
                .Answer("https://api.thegamesdb.net/v1/Games/Images", TgdbImages)
                .Answer("https://api.thegamesdb.net/v1/Genres", TgdbGenres);
            var source = new TheGamesDbSource(http, "chave-falsa");

            var coverOnly = source.Find(new MediaQuery("ps2", "Shadow of the Colossus (USA).iso"), new[] { MediaFields.Cover, MediaFields.Name });
            Assert.Equal("https://cdn.test/large/boxart/front/1234-1.jpg", coverOnly.Media[MediaFields.Cover].Url);
            Assert.Equal("1", coverOnly.Texts[MediaFields.Players]);
            Assert.Single(http.Requests);
            Assert.Contains("filter%5Bplatform%5D=11", http.Requests[0]);

            var all = source.Find(new MediaQuery("ps2", "Shadow of the Colossus (USA).iso"), new[] { MediaFields.FanArt, MediaFields.Logo, MediaFields.Genre });
            Assert.Equal("https://cdn.test/large/fanart/1234-1.jpg", all.Media[MediaFields.FanArt].Url);
            Assert.Equal("https://cdn.test/large/clearlogo/1234.png", all.Media[MediaFields.Logo].Url);
            Assert.Equal("Ação / Aventura", all.Texts[MediaFields.Genre]);
        }

        // ---- Andamento do "Atualizar listas de jogos" ----

        [Fact]
        public void Refresh_progress_never_goes_back_and_stops_on_an_es_without_the_route()
        {
            var sent = new List<string>();
            var progress = new EsRefreshProgress(json => { sent.Add(json); return true; });

            progress.Report(10, "Sincronizando as BIOS");
            progress.Report(5, "Sincronizando as contas");
            progress.Finish("Recarregando a lista de jogos");

            Assert.Equal(3, sent.Count);
            Assert.Contains("\"percent\":10", sent[1]);
            Assert.Contains("\"percent\":100", sent[2]);
            Assert.Contains("\"done\":true", sent[2]);

            var calls = 0;
            var official = new EsRefreshProgress(json => { calls++; return false; });
            official.Report(1, "a");
            official.Report(50, "b");
            official.Finish("c");
            Assert.Equal(1, calls);
        }
    }
}
