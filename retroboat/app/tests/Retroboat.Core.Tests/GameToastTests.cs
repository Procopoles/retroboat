using System;
using Retroboat.Downloads;
using Retroboat.Library;
using Retroboat.Media;
using Xunit;

namespace Retroboat.Tests
{
    public class GameToastTests
    {
        [Fact]
        public void Card_sits_in_the_bottom_right_corner_of_the_game_screen()
        {
            var place = GameToastLayout.Place(0, 0, 1920, 1080, 1f);

            Assert.Equal(GameToastLayout.Width, place.W);
            Assert.Equal(GameToastLayout.Height, place.H);
            Assert.Equal(1920 - GameToastLayout.Margin, place.X + place.W);
            Assert.Equal(1080 - GameToastLayout.Margin, place.Y + place.H);
        }

        [Fact]
        public void Card_follows_the_screen_of_the_game_and_scales_with_it()
        {
            // Segundo monitor 4K à esquerda do principal.
            float scale = GameToastLayout.ScaleFor(2160);
            var place = GameToastLayout.Place(-3840, 0, 3840, 2160, scale);

            Assert.Equal(2f, scale);
            Assert.Equal(GameToastLayout.Width * 2, place.W);
            Assert.Equal(-GameToastLayout.Margin * 2, place.X + place.W);
            Assert.Equal(2160 - GameToastLayout.Margin * 2, place.Y + place.H);
        }

        [Fact]
        public void Card_never_leaves_a_tiny_screen()
        {
            var place = GameToastLayout.Place(0, 0, 320, 240, GameToastLayout.ScaleFor(240));

            Assert.True(place.X >= 0);
            Assert.True(place.Y >= 0);
            Assert.True(place.X + place.W <= 320);
        }

        [Fact]
        public void Card_rises_from_below_stays_and_goes_back_down()
        {
            var start = GameToastLayout.At(TimeSpan.Zero, 1f);
            Assert.Equal(GameToastLayout.Rise, start.OffsetY);
            Assert.Equal(0, start.Opacity);

            var middleOfEnter = GameToastLayout.At(TimeSpan.FromMilliseconds(100), 1f);
            Assert.InRange(middleOfEnter.OffsetY, 1, GameToastLayout.Rise / 2);
            Assert.InRange(middleOfEnter.Opacity, 128, 254);

            var holding = GameToastLayout.At(GameToastLayout.Enter + TimeSpan.FromSeconds(3), 1f);
            Assert.Equal(0, holding.OffsetY);
            Assert.Equal(255, holding.Opacity);
            Assert.False(holding.Done);

            var leaving = GameToastLayout.At(GameToastLayout.Enter + GameToastLayout.Hold + TimeSpan.FromMilliseconds(100), 1f);
            Assert.InRange(leaving.OffsetY, 1, GameToastLayout.Rise);
            Assert.InRange(leaving.Opacity, 1, 127);

            Assert.True(GameToastLayout.At(GameToastLayout.Total, 1f).Done);
        }

        [Fact]
        public void Card_motion_is_short_and_never_bounces()
        {
            Assert.Equal(TimeSpan.FromMilliseconds(200), GameToastLayout.Enter);
            Assert.Equal(TimeSpan.FromMilliseconds(200), GameToastLayout.Leave);

            double previous = 0;
            for (int i = 0; i <= 100; i++)
            {
                double value = GameToastLayout.EaseOut(i / 100.0);
                Assert.InRange(value, previous, 1);
                previous = value;
            }
        }

        [Fact]
        public void Cover_is_the_box_art_from_the_gamelist_or_nothing()
        {
            using (var dir = new TempDir())
            {
                var install = new RetroBatInstall(dir.Combine("RetroBat"));
                var roms = dir.Combine("Jogos", "ps2");
                dir.WriteText(@"RetroBat\emulationstation\.emulationstation\es_systems.cfg",
                    "<systemList><system><name>ps2</name><path>~\\..\\roms\\ps2</path></system></systemList>");
                var box = dir.WriteFile(@"RetroBat\emulationstation\.emulationstation\media\ps2\images\Jogo (USA)-thumb.png", new byte[] { 1 });
                dir.WriteFile(@"RetroBat\emulationstation\.emulationstation\media\ps2\images\Jogo (USA)-image.png", new byte[] { 2 });
                dir.WriteText(@"RetroBat\emulationstation\.emulationstation\gamelists\ps2\gamelist.xml",
                    "<gameList>" +
                    "<game><path>./Jogo (USA).iso</path><image>~/.emulationstation/media/ps2/images/Jogo (USA)-image.png</image>" +
                    "<boxart>~/.emulationstation/media/ps2/images/Jogo (USA)-thumb.png</boxart></game>" +
                    "<game><path>./Sem Capa.iso</path><boxart>~/.emulationstation/media/ps2/images/apagada.png</boxart></game>" +
                    "</gameList>");

                Assert.Equal(box, GameMedia.CoverOf(install, roms, "Jogo (USA).iso"));
                Assert.Null(GameMedia.CoverOf(install, roms, "Sem Capa.iso"));
                Assert.Null(GameMedia.CoverOf(install, roms, "Fora do gamelist.iso"));
                Assert.Null(GameMedia.CoverOf(install, dir.Combine("Jogos", "snes"), "Jogo (USA).iso"));
            }
        }
    }
}
