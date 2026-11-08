using System.IO;
using System.Linq;
using Retroboat.Cloud;
using Retroboat.Library;
using Xunit;

namespace Retroboat.Tests
{
    public class CloudFoldersTests
    {
        const string Systems = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<systemList>
  <system><name>snes</name><path>~\..\roms\snes</path></system>
  <system><name>gw</name><path>~\..\roms\gameandwatch</path></system>
  <system><name>psx</name><path>~\..\roms\psx</path></system>
  <system><name>ps2</name><path>~\..\roms\ps2</path></system>
  <system><name>starship</name><path>~\..\roms\starship</path></system>
  <system><name>starship</name><path>~\..\roms\ghostship</path></system>
  <system><name>library</name><path>~\..\library</path></system>
  <system><name>kodi</name><path>~\..\kodi</path></system>
</systemList>";

        static RetroBatInstall CreateInstall(TempDir dir)
        {
            dir.WriteText(@"RetroBat\emulationstation\.emulationstation\es_systems.cfg", Systems);
            return new RetroBatInstall(dir.Combine("RetroBat"));
        }

        [Fact]
        public void Games_folder_gets_the_same_console_folders_as_retrobat_roms()
        {
            using (var dir = new TempDir())
            {
                var install = CreateInstall(dir);
                dir.WriteFile(@"nuvem\jogos\ps2\God of War (USA).zip", TempDir.RandomBytes(100));
                var cloud = CloudLocator.Locate(dir.Combine("nuvem"));

                var result = CloudFolders.Ensure(install, cloud);

                Assert.Empty(result.Errors);
                Assert.Equal(new[] { "snes", "gameandwatch", "psx", "starship" }, result.SystemsCreated);
                Assert.Equal(new[] { "gameandwatch", "ps2", "psx", "snes", "starship" },
                    Directory.GetDirectories(cloud.GamesPath).Select(Path.GetFileName).OrderBy(n => n));

                // O jogo que já estava lá continua, e as pastas vazias não viram jogos.
                Assert.Equal(new[] { "ps2/God of War (USA).zip" }, new CloudLibrary(cloud.GamesPath).GetGames().Select(g => g.Id));
            }
        }

        [Fact]
        public void Nothing_is_created_twice_and_folders_retrobat_dropped_are_kept()
        {
            using (var dir = new TempDir())
            {
                var install = CreateInstall(dir);
                Directory.CreateDirectory(dir.Combine(@"nuvem\jogos\sistema-antigo"));
                Directory.CreateDirectory(dir.Combine(@"nuvem\jogos\SNES"));
                var cloud = CloudLocator.Locate(dir.Combine("nuvem"));

                CloudFolders.Ensure(install, cloud);
                var again = CloudFolders.Ensure(install, cloud);

                Assert.Empty(again.SystemsCreated);
                Assert.True(Directory.Exists(dir.Combine(@"nuvem\jogos\sistema-antigo")));
                Assert.Equal(6, Directory.GetDirectories(cloud.GamesPath).Length);
            }
        }

        [Fact]
        public void Missing_retroboat_or_games_folder_is_never_created()
        {
            using (var dir = new TempDir())
            {
                var install = CreateInstall(dir);
                Directory.CreateDirectory(dir.Combine("Meu Drive"));
                Directory.CreateDirectory(dir.Combine(@"Outra conta\retroboat"));

                // Drive aberto sem a pasta do Retroboat (ou ainda carregando): nada de segunda pasta vazia no Drive.
                var detected = new CloudLocation(dir.Combine(@"Meu Drive\retroboat"), "Google Drive (G:\\)");
                Assert.Empty(CloudFolders.Ensure(install, detected).SystemsCreated);
                Assert.Empty(Directory.GetFileSystemEntries(dir.Combine("Meu Drive")));

                // Pasta do Retroboat sem jogos: também não.
                var withoutGames = new CloudLocation(dir.Combine(@"Outra conta\retroboat"), "Google Drive (H:\\)");
                Assert.Empty(CloudFolders.Ensure(install, withoutGames).SystemsCreated);
                Assert.Empty(Directory.GetFileSystemEntries(dir.Combine(@"Outra conta\retroboat")));

                Assert.Empty(CloudFolders.Ensure(install, CloudLocator.Locate(dir.Combine("nao-existe"))).SystemsCreated);
                Assert.Empty(CloudFolders.Ensure(install, null).SystemsCreated);
                Assert.False(Directory.Exists(dir.Combine("nao-existe")));
            }
        }

        [Fact]
        public void A_file_with_a_console_name_is_left_alone()
        {
            using (var dir = new TempDir())
            {
                var install = CreateInstall(dir);
                var file = dir.WriteFile(@"nuvem\jogos\psx", TempDir.RandomBytes(1234));

                var result = CloudFolders.Ensure(install, CloudLocator.Locate(dir.Combine("nuvem")));

                Assert.Equal(TempDir.RandomBytes(1234), File.ReadAllBytes(file));
                Assert.Single(result.Errors, e => e.StartsWith("psx: "));
                Assert.Equal(4, result.SystemsCreated.Count);
            }
        }

        [Fact]
        public void Startup_creates_the_folders_and_says_so_once()
        {
            using (var dir = new TempDir())
            {
                var install = CreateInstall(dir);
                dir.WriteFile(@"nuvem\jogos\snes\Jogo.sfc", TempDir.RandomBytes(1000));

                var first = StartupSync.Run(install, dir.Combine("nuvem"), dir.Combine("roms"));
                var second = StartupSync.Run(install, dir.Combine("nuvem"), dir.Combine("roms"));

                Assert.Contains("Nuvem: 4 pasta(s) de console criada(s) em retroboat\\jogos", first.Messages);
                Assert.Contains("Biblioteca na nuvem: 0 instalado(s), 1 para baixar", first.Messages);
                Assert.False(first.HasWarnings);
                Assert.DoesNotContain(second.Messages, m => m.Contains("pasta(s) de console"));
                Assert.True(Directory.Exists(dir.Combine(@"nuvem\jogos\gameandwatch")));
            }
        }
    }
}
