using Retroboat.Cloud;
using Retroboat.Library;
using Xunit;

namespace Retroboat.Tests
{
    public class ActiveSystemsTests
    {
        [Fact]
        public void A_console_is_active_with_a_game_in_the_cloud_or_on_this_pc()
        {
            using (var dir = new TempDir())
            {
                dir.WriteFile(@"nuvem\retroboat\jogos\ps2\Jogo (USA).iso", new byte[] { 1 });
                dir.WriteText(@"nuvem\retroboat\jogos\n64\_info.txt", "pasta vazia no Drive");
                dir.WriteFile(@"Jogos\psx\Outro (USA).chd", new byte[0]);                 // marcador de jogo da nuvem conta
                dir.WriteFile(@"Jogos\ps3\Jogo (USA).ps3\PS3_GAME\PARAM.SFO", new byte[] { 1 }); // jogo em pasta
                dir.WriteText(@"Jogos\snes\gamelist.xml", "<gameList />");
                dir.WriteText(@"Jogos\snes\_info.txt", "x");
                dir.WriteFile(@"Jogos\snes\images\capa.png", new byte[] { 1 });          // mídia do scraper não é jogo
                dir.WriteFile(@"Jogos\gba\Jogo (USA).gba.retroboat-part", new byte[] { 1 }); // download pela metade
                dir.WriteFile(@"Jogos\.uncompressed\psp\Jogo.iso", new byte[] { 1 });
                var cloud = new CloudLocation(dir.Combine("nuvem", "retroboat"), "configurado");

                var active = ActiveSystems.Folders(dir.Combine("Jogos"), cloud);

                Assert.Equal(new[] { "ps2", "ps3", "psx" }, active);
                Assert.Contains("PSX", active);
            }
        }

        [Fact]
        public void Without_the_cloud_or_the_games_folder_only_the_other_side_counts()
        {
            using (var dir = new TempDir())
            {
                dir.WriteFile(@"nuvem\retroboat\jogos\ps2\Jogo (USA).iso", new byte[] { 1 });
                dir.WriteFile(@"Jogos\psx\Outro (USA).chd", new byte[] { 1 });
                var cloud = new CloudLocation(dir.Combine("nuvem", "retroboat"), "configurado");

                Assert.Equal(new[] { "psx" }, ActiveSystems.Folders(dir.Combine("Jogos")));
                Assert.Equal(new[] { "ps2" }, ActiveSystems.Folders(dir.Combine("nao-existe"), cloud));
                Assert.Empty(ActiveSystems.Folders(null, new CloudLocation(dir.Combine("x"), "configurado", libraryMissing: true)));
            }
        }
    }
}
