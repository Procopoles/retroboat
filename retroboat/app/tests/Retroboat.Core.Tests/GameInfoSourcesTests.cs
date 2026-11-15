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
    /// <summary>As fontes de informação da importação (libretro, Wikidata, Wikipédia), usadas pelo PERSONALIZADO.</summary>
    public class GameInfoSourcesTests
    {
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
