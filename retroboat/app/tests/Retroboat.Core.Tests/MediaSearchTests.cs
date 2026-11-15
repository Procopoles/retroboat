using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Retroboat.Media;
using Retroboat.Media.Sources;
using Xunit;

namespace Retroboat.Tests
{
    /// <summary>A busca PERSONALIZADO (RB-038): cada campo da primeira fonte da sua lista que o tenha.</summary>
    public class MediaSearchTests
    {
        const string Game = "God of War (USA)";
        const string LongPortuguese = "God of War é um jogo eletrônico de ação-aventura desenvolvido pela Santa Monica Studio e publicado pela " +
                                      "Sony Computer Entertainment para o PlayStation 2 em março de 2005.";

        static MediaQuery Query(string file = Game + ".iso") => new MediaQuery("ps2", file);

        [Fact]
        public void Each_field_comes_from_the_first_source_of_its_list_that_has_it()
        {
            var hfs = new FakeMediaSource(MediaSourceIds.HfsDb).Media(Game, MediaFields.Cover).Media(Game, MediaFields.Video, ".mp4");
            var ss = new FakeMediaSource(MediaSourceIds.ScreenScraper)
                .Media(Game, MediaFields.Cover).Media(Game, MediaFields.Screenshot).Media(Game, MediaFields.Video, ".mp4")
                .Text(Game, MediaFields.Genre, "Briga de rua").Text(Game, MediaFields.Publisher, "SCEA");
            var wiki = new FakeMediaSource(MediaSourceIds.Wikipedia).Text(Game, MediaFields.Genre, "Ação-aventura");
            var libretro = new FakeMediaSource(MediaSourceIds.Libretro).Text(Game, MediaFields.Publisher, "Sony Computer Entertainment");

            var result = MediaTest.Search(hfs, ss, wiki, libretro).Find(Query());

            Assert.Equal(hfs.UrlOf(MediaFields.Cover, Game), result.Media[MediaFields.Cover].Url);
            Assert.Equal(hfs.UrlOf(MediaFields.Video, Game, ".mp4"), result.Media[MediaFields.Video].Url);
            Assert.Equal(ss.UrlOf(MediaFields.Screenshot, Game), result.Media[MediaFields.Screenshot].Url);
            Assert.Equal("Ação-aventura", result.Texts[MediaFields.Genre]);
            Assert.Equal("Sony Computer Entertainment", result.Texts[MediaFields.Publisher]);
            Assert.Equal(MediaSourceIds.HfsDb, result.Origins[MediaFields.Cover]);
            Assert.Equal(MediaSourceIds.Wikipedia, result.Origins[MediaFields.Genre]);
            Assert.Empty(result.Errors);
        }

        [Fact]
        public void The_person_choice_goes_first_and_the_rest_follows_the_preset()
        {
            var hfs = new FakeMediaSource(MediaSourceIds.HfsDb).Media(Game, MediaFields.Cover);
            var ss = new FakeMediaSource(MediaSourceIds.ScreenScraper).Media(Game, MediaFields.Cover);
            var profile = MediaProfile.Parse("thumbnail=screenscraper");

            var result = MediaTest.Search(profile, hfs, ss).Find(Query(), new[] { MediaFields.Cover });

            Assert.Equal(MediaSourceIds.ScreenScraper, result.Origins[MediaFields.Cover]);
            Assert.Equal(0, hfs.Calls); // o ScreenScraper tinha a capa: o HfsDB nem é consultado

            ss = new FakeMediaSource(MediaSourceIds.ScreenScraper);
            result = MediaTest.Search(profile, hfs, ss).Find(Query(), new[] { MediaFields.Cover });
            Assert.Equal(MediaSourceIds.HfsDb, result.Origins[MediaFields.Cover]);
        }

        [Fact]
        public void A_field_turned_off_is_not_searched()
        {
            var hfs = new FakeMediaSource(MediaSourceIds.HfsDb).Media(Game, MediaFields.Video, ".mp4").Media(Game, MediaFields.Cover);

            var result = MediaTest.Search(MediaProfile.Parse("video=none"), hfs).Find(Query());

            Assert.False(result.Media.ContainsKey(MediaFields.Video));
            Assert.True(result.Media.ContainsKey(MediaFields.Cover));
            Assert.DoesNotContain(MediaFields.Video, hfs.Wanted.Single());
        }

        [Fact]
        public void A_long_portuguese_description_wins_even_from_a_later_source()
        {
            var ss = new FakeMediaSource(MediaSourceIds.ScreenScraper).Text(Game, MediaFields.Description, "God of War is an action game.");
            var wiki = new FakeMediaSource(MediaSourceIds.Wikipedia).Add(Game, c =>
            {
                c.SetText(MediaFields.Description, LongPortuguese);
                c.DescriptionInPortuguese = true;
            });

            var result = MediaTest.Search(ss, wiki).Find(Query(), new[] { MediaFields.Description });

            Assert.Equal(LongPortuguese, result.Texts[MediaFields.Description]);
            Assert.True(result.DescriptionInPortuguese);
            Assert.Equal(MediaSourceIds.Wikipedia, result.Origins[MediaFields.Description]);
        }

        [Fact]
        public void A_portuguese_stub_loses_to_the_first_full_description()
        {
            var ss = new FakeMediaSource(MediaSourceIds.ScreenScraper).Text(Game, MediaFields.Description, "God of War is an action-oriented adventure game with a strong focus on combat.");
            var wiki = new FakeMediaSource(MediaSourceIds.Wikipedia).Add(Game, c =>
            {
                c.SetText(MediaFields.Description, "God of War é um jogo de 2005.");
                c.DescriptionInPortuguese = true;
            });

            var result = MediaTest.Search(ss, wiki).Find(Query(), new[] { MediaFields.Description });

            Assert.StartsWith("God of War is", result.Texts[MediaFields.Description]);
            Assert.False(result.DescriptionInPortuguese);
        }

        [Fact]
        public void Sources_are_asked_only_when_they_can_still_decide_a_field()
        {
            var hfs = new FakeMediaSource(MediaSourceIds.HfsDb).Media(Game, MediaFields.Cover);
            var ss = new FakeMediaSource(MediaSourceIds.ScreenScraper).Media(Game, MediaFields.Logo);
            var tgdb = new FakeMediaSource(MediaSourceIds.TheGamesDb).Media(Game, MediaFields.Logo).Media(Game, MediaFields.FanArt);

            var result = MediaTest.Search(hfs, ss, tgdb).Find(Query(), new[] { MediaFields.Cover, MediaFields.Logo, MediaFields.FanArt });

            // Capa e logo saíram na primeira rodada; a fan art (ScreenScraper, depois TheGamesDB) foi para a segunda.
            Assert.Equal(MediaSourceIds.ScreenScraper, result.Origins[MediaFields.Logo]);
            Assert.Equal(MediaSourceIds.TheGamesDb, result.Origins[MediaFields.FanArt]);
            Assert.Equal(1, tgdb.Calls);
            Assert.Equal(new[] { MediaFields.FanArt }, tgdb.Wanted.Single());

            var onlyCover = new FakeMediaSource(MediaSourceIds.TheGamesDb);
            MediaTest.Search(hfs, onlyCover).Find(Query(), new[] { MediaFields.Cover });
            Assert.Equal(0, onlyCover.Calls);
        }

        [Fact]
        public void Without_a_game_screen_the_cover_is_also_the_image()
        {
            var hfs = new FakeMediaSource(MediaSourceIds.HfsDb).Media(Game, MediaFields.Cover);

            var result = MediaTest.Search(hfs).Find(Query(), new[] { MediaFields.Cover, MediaFields.Screenshot });

            Assert.Same(result.Media[MediaFields.Cover], result.Media[MediaFields.Screenshot]);
        }

        [Fact]
        public void A_source_that_is_down_does_not_hide_the_others()
        {
            var hfs = new FakeMediaSource(MediaSourceIds.HfsDb) { Fail = new WebException("sem conexão") };
            var ss = new FakeMediaSource(MediaSourceIds.ScreenScraper) { Fail = new MediaSourceException("ScreenScraper: HTTP 430") };
            var libretro = new FakeMediaSource(MediaSourceIds.Libretro).Media(Game, MediaFields.Cover).Text(Game, MediaFields.Publisher, "Sony");

            var result = MediaTest.Search(hfs, ss, libretro).Find(Query());

            Assert.True(result.Found);
            Assert.Equal(MediaSourceIds.Libretro, result.Origins[MediaFields.Cover]);
            Assert.Equal(2, result.Errors.Count);
            Assert.Contains(result.Errors, e => e.StartsWith("HFSDB:"));
        }

        [Fact]
        public void A_typed_name_is_what_the_sources_search_for()
        {
            var libretro = new FakeMediaSource(MediaSourceIds.Libretro).Media("God of War", MediaFields.Cover);

            var result = MediaTest.Search(libretro).Find(new MediaQuery("ps2", "Arquivo Estranho.iso", "God of War"));

            Assert.True(result.Found);
            Assert.Equal("God of War", libretro.Asked.Single());
        }

        [Fact]
        public void An_unknown_game_or_system_is_not_found_and_is_not_an_error()
        {
            var libretro = new FakeMediaSource(MediaSourceIds.Libretro);

            Assert.False(MediaTest.Search(libretro).Find(Query("Jogo Caseiro.iso")).Found);
            var other = MediaTest.Search(libretro).Find(new MediaQuery("amiga", "Jogo.adf"));
            Assert.False(other.Found);
            Assert.Empty(other.Errors);
            Assert.Equal(1, libretro.Calls);
        }

        [Theory]
        [InlineData("God of War (USA).iso", "God of War (USA)")]
        [InlineData("God of War (USA)", "God of War (USA)")]
        [InlineData("Final Fantasy X (USA) (v1.1)", "Final Fantasy X (USA) (v1.1)")]
        [InlineData("Jogo.v1.2.zip", "Jogo.v1.2")]
        public void The_file_extension_is_not_part_of_the_name(string file, string stem)
        {
            Assert.Equal(stem, new MediaQuery("ps2", file).Stem);
        }
    }
}
