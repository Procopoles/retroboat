using System.IO;
using System.Linq;
using Retroboat.Cloud;
using Xunit;

namespace Retroboat.Tests
{
    public class CloudLibraryTests
    {
        [Fact]
        public void Lists_systems_and_games_ignoring_metadata_and_hidden_entries()
        {
            using (var dir = new TempDir())
            {
                dir.WriteFile("jogos/snes/Mario (USA).sfc", new byte[100]);
                dir.WriteFile("jogos/snes/gamelist.xml", new byte[10]);
                dir.WriteFile("jogos/snes/images/Mario-image.png", new byte[10]);
                dir.WriteFile("jogos/snes/Zelda (USA).sfc" + GameInstaller.PartSuffix, new byte[10]);
                dir.WriteFile("jogos/ps3/Jogo.ps3/PS3_GAME/USRDIR/EBOOT.BIN", new byte[300]);
                dir.WriteFile("jogos/ps3/Jogo.ps3/PS3_DISC.SFB", new byte[20]);
                dir.WriteFile("jogos/.lixeira/x.iso", new byte[10]);

                var library = new CloudLibrary(dir.Combine("jogos"));

                Assert.Equal(new[] { "ps3", "snes" }, library.GetSystems());

                var games = library.GetGames();
                Assert.Equal(new[] { "ps3/Jogo.ps3", "snes/Mario (USA).sfc" }, games.Select(g => g.Id));

                var ps3 = games.Single(g => g.System == "ps3");
                Assert.True(ps3.IsDirectory);
                Assert.Equal(320, ps3.Size);
            }
        }

        [Fact]
        public void Find_is_case_insensitive_and_accepts_backslash()
        {
            using (var dir = new TempDir())
            {
                dir.WriteFile("jogos/snes/Mario (USA).sfc", new byte[100]);
                var library = new CloudLibrary(dir.Combine("jogos"));

                Assert.NotNull(library.Find("SNES/mario (usa).sfc"));
                Assert.NotNull(library.Find(@"snes\Mario (USA).sfc"));
                Assert.Null(library.Find("snes/Outro.sfc"));
                Assert.Null(library.Find("snes"));
            }
        }

        [Fact]
        public void Missing_games_folder_is_an_empty_library()
        {
            using (var dir = new TempDir())
            {
                var library = new CloudLibrary(dir.Combine("nao-existe"));

                Assert.Empty(library.GetSystems());
                Assert.Empty(library.GetGames());
            }
        }

        [Fact]
        public void Locator_prefers_configured_path()
        {
            using (var dir = new TempDir())
            {
                Directory.CreateDirectory(dir.Combine("jogos"));

                var location = CloudLocator.Locate(dir.Path);

                Assert.Equal("configurado", location.Source);
                Assert.True(location.Exists);
                Assert.Equal(dir.Combine("jogos"), location.GamesPath);
            }
        }

        [Fact]
        public void Finds_my_drive_folder_in_portuguese_and_english()
        {
            using (var pt = new TempDir())
            using (var en = new TempDir())
            {
                Directory.CreateDirectory(pt.Combine("Meu Drive"));
                Directory.CreateDirectory(en.Combine("My Drive"));

                Assert.Equal(pt.Combine("Meu Drive"), CloudLocator.FindMyDrive(pt.Path));
                Assert.Equal(en.Combine("My Drive"), CloudLocator.FindMyDrive(en.Path));
            }
        }
    }
}
