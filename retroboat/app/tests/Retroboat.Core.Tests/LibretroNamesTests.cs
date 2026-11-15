using System.Collections.Generic;
using System.Linq;
using Retroboat.Media;
using Xunit;

namespace Retroboat.Tests
{
    /// <summary>Como o libretro acha o jogo pelo nome No-Intro/Redump (parte libretro do PERSONALIZADO).</summary>
    public class LibretroNamesTests
    {
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
    }
}
