using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Retroboat.Cloud;
using Retroboat.Library;
using Retroboat.Media;
using Xunit;

namespace Retroboat.Tests
{
    public class PartialDownloadsTests
    {
        [Fact]
        public void A_browser_download_in_progress_on_the_drive_is_not_a_game_until_it_finishes()
        {
            using (var cloud = new TempDir())
            {
                cloud.WriteFile(@"jogos\ps2\Pronto (USA).zip", new byte[100]);
                cloud.WriteFile(@"jogos\ps2\Opera (USA).zip.opdownload", new byte[50]);
                cloud.WriteFile(@"jogos\ps2\Chrome (USA).iso.crdownload", new byte[50]);
                cloud.WriteFile(@"jogos\ps2\Unconfirmed 123456.crdownload", new byte[50]);
                cloud.WriteFile(@"jogos\ps2\Firefox (USA).zip", new byte[0]);           // o Firefox cria o nome final vazio...
                cloud.WriteFile(@"jogos\ps2\Firefox (USA).zip.part", new byte[50]);     // ...e baixa ao lado
                cloud.WriteFile(@"jogos\ps2\Aria (USA).zip", new byte[100]);            // o aria2 grava direto no nome final...
                cloud.WriteFile(@"jogos\ps2\Aria (USA).zip.aria2", new byte[10]);       // ...com o controle ao lado
                cloud.WriteFile(@"jogos\psx\Torrent (USA).chd.!ut", new byte[50]);
                var library = new CloudLibrary(cloud.Combine("jogos"));

                Assert.Equal(new[] { "ps2/Pronto (USA).zip" }, library.GetGames().Select(g => g.Id));

                // O Opera terminou: renomeia para o nome final, e o jogo entra.
                File.Move(cloud.Combine(@"jogos\ps2\Opera (USA).zip.opdownload"), cloud.Combine(@"jogos\ps2\Opera (USA).zip"));
                File.Delete(cloud.Combine(@"jogos\ps2\Firefox (USA).zip.part"));
                File.WriteAllBytes(cloud.Combine(@"jogos\ps2\Firefox (USA).zip"), new byte[100]);

                Assert.Equal(new[] { "ps2/Firefox (USA).zip", "ps2/Opera (USA).zip", "ps2/Pronto (USA).zip" }, library.GetGames().Select(g => g.Id));
            }
        }

        [Fact]
        public void A_console_with_only_a_download_in_progress_is_not_active()
        {
            using (var dir = new TempDir())
            {
                dir.WriteFile(@"Jogos\ps2\Jogo (USA).zip.opdownload", new byte[] { 1 });
                dir.WriteFile(@"nuvem\jogos\psx\Jogo (USA).zip.crdownload", new byte[] { 1 });
                var cloud = new CloudLocation(dir.Combine("nuvem"), "configurado");

                Assert.Empty(ActiveSystems.Folders(dir.Combine("Jogos"), cloud));
            }
        }

        [Fact]
        public void What_an_earlier_version_made_for_a_browser_download_leaves_this_pc()
        {
            using (var dir = new TempDir())
            {
                var install = new RetroBatInstall(dir.Combine("RetroBat"));
                var roms = dir.Combine("Jogos");
                var partial = "Castlevania (Europe).zip.opdownload";

                // Como a versão anterior deixava: entrada no índice, marcador vazio, linha no gamelist e capas.
                var index = CloudIndex.Load(CloudLibrarySync.IndexPath(install));
                index.Set(new CloudIndexEntry { System = "ps2", CloudName = partial, LocalName = partial, CloudSize = 50, State = CloudGameState.Cloud });
                index.Set(new CloudIndexEntry { System = "ps2", CloudName = "Guardado.zip.part", LocalName = "Guardado.zip.part", CloudSize = 5, State = CloudGameState.Cloud });
                index.Set(new CloudIndexEntry { System = "ps2", CloudName = "Real (USA).iso", LocalName = "Real (USA).iso", CloudSize = 100, State = CloudGameState.Cloud });
                index.Save();
                dir.WriteFile(@"Jogos\ps2\" + partial, new byte[0]);
                dir.WriteFile(@"Jogos\ps2\Guardado.zip.part", new byte[] { 7 });   // com conteúdo: nunca é apagado
                dir.WriteFile(@"Jogos\ps2\Real (USA).iso", new byte[0]);
                var media = @"RetroBat\emulationstation\.emulationstation\media\ps2\images\";
                dir.WriteFile(media + "Castlevania (Europe).zip-thumb.png", new byte[] { 1 });
                dir.WriteFile(media + "compartilhada.png", new byte[] { 1 });
                dir.WriteFile(media + "Real (USA)-thumb.png", new byte[] { 1 });
                dir.WriteText(@"RetroBat\emulationstation\.emulationstation\gamelists\ps2\gamelist.xml",
                    "<gameList>" +
                    "<game><path>./" + partial + "</path><name>Castlevania</name>" +
                    "<thumbnail>~/.emulationstation/media/ps2/images/Castlevania (Europe).zip-thumb.png</thumbnail>" +
                    "<image>~/.emulationstation/media/ps2/images/compartilhada.png</image></game>" +
                    "<game><path>./Real (USA).iso</path><name>Real</name>" +
                    "<thumbnail>~/.emulationstation/media/ps2/images/Real (USA)-thumb.png</thumbnail>" +
                    "<image>~/.emulationstation/media/ps2/images/compartilhada.png</image></game>" +
                    "</gameList>");

                var result = PartialDownloads.Clean(install, roms);

                Assert.Empty(result.Errors);
                Assert.Equal(new[] { "ps2/" + partial }, result.Games);
                Assert.Equal(new[] { "Real (USA).iso" }, CloudIndex.Load(CloudLibrarySync.IndexPath(install)).Entries.Select(e => e.LocalName));
                Assert.False(File.Exists(dir.Combine(@"Jogos\ps2\" + partial)));
                Assert.True(File.Exists(dir.Combine(@"Jogos\ps2\Guardado.zip.part")));
                Assert.True(File.Exists(dir.Combine(@"Jogos\ps2\Real (USA).iso")));

                var gamelist = XDocument.Load(dir.Combine(@"RetroBat\emulationstation\.emulationstation\gamelists\ps2\gamelist.xml"));
                Assert.Equal(new[] { "./Real (USA).iso" }, gamelist.Root.Elements("game").Select(g => (string)g.Element("path")));
                Assert.False(File.Exists(dir.Combine(media + "Castlevania (Europe).zip-thumb.png")));
                Assert.True(File.Exists(dir.Combine(media + "compartilhada.png")));
                Assert.True(File.Exists(dir.Combine(media + "Real (USA)-thumb.png")));
                Assert.Equal(2, result.FilesDeleted);

                // Com o Drive fechado, o gamelist é refeito a partir do índice: a linha não volta.
                CloudLibrarySync.RefreshGamelists(install);
                Assert.DoesNotContain("opdownload", File.ReadAllText(dir.Combine(@"RetroBat\emulationstation\.emulationstation\gamelists\ps2\gamelist.xml")));

                // Nada mais a fazer na abertura seguinte.
                Assert.Empty(PartialDownloads.Clean(install, roms).Games);
            }
        }

        [Fact]
        public void Cloud_media_sent_for_a_browser_download_leaves_only_what_this_pc_sent()
        {
            using (var cloud = new TempDir())
            {
                cloud.WriteText(@"midia\ps2\metadados\PC-A.xml",
                    "<retroboatMedia>" +
                    "<game key=\"Batman (USA).zip.opdownload\" updated=\"2026-11-09T19:00:00Z\"><name>Batman</name>" +
                    "<thumbnail>imagens/Batman (USA).zip-thumb.png</thumbnail><image>imagens/compartilhada.png</image></game>" +
                    "<game key=\"Real (USA).iso\" updated=\"2026-11-09T19:00:00Z\"><thumbnail>imagens/Real (USA)-thumb.png</thumbnail></game>" +
                    "</retroboatMedia>");
                cloud.WriteText(@"midia\ps2\metadados\PC-B.xml",
                    "<retroboatMedia>" +
                    "<game key=\"Outro (USA).zip.opdownload\" updated=\"2026-11-09T19:00:00Z\"><thumbnail>imagens/Outro (USA).zip-thumb.png</thumbnail></game>" +
                    "<game key=\"Mais um (USA).iso\" updated=\"2026-11-09T19:00:00Z\"><image>imagens/compartilhada.png</image></game>" +
                    "</retroboatMedia>");
                foreach (var image in new[] { "Batman (USA).zip-thumb.png", "compartilhada.png", "Real (USA)-thumb.png", "Outro (USA).zip-thumb.png" })
                    cloud.WriteFile(@"midia\ps2\imagens\" + image, new byte[] { 1 });
                var before = File.ReadAllText(cloud.Combine(@"midia\ps2\metadados\PC-B.xml"));

                var result = CloudMediaSync.RemovePartialDownloads(cloud.Path, "PC-A");

                Assert.Empty(result.Errors);
                Assert.Equal(new[] { "ps2/Batman (USA).zip.opdownload" }, result.Games);
                Assert.Equal(1, result.FilesDeleted);
                var own = File.ReadAllText(cloud.Combine(@"midia\ps2\metadados\PC-A.xml"));
                Assert.DoesNotContain("Batman", own);
                Assert.Contains("Real (USA).iso", own);
                Assert.False(File.Exists(cloud.Combine(@"midia\ps2\imagens\Batman (USA).zip-thumb.png")));
                Assert.True(File.Exists(cloud.Combine(@"midia\ps2\imagens\compartilhada.png")));
                Assert.True(File.Exists(cloud.Combine(@"midia\ps2\imagens\Real (USA)-thumb.png")));

                // O arquivo do outro PC é dele: fica como estava, até ele limpar.
                Assert.Equal(before, File.ReadAllText(cloud.Combine(@"midia\ps2\metadados\PC-B.xml")));
                Assert.True(File.Exists(cloud.Combine(@"midia\ps2\imagens\Outro (USA).zip-thumb.png")));
            }
        }

        [Fact]
        public void Opening_retroboat_while_the_browser_downloads_into_the_drive_shows_only_real_games()
        {
            using (var cloud = new TempDir())
            using (var retrobat = new TempDir())
            {
                cloud.WriteFile(@"jogos\ps2\Pronto (USA).zip", TempDir.RandomBytes(1000));
                cloud.WriteFile(@"jogos\ps2\Baixando (USA).zip.opdownload", TempDir.RandomBytes(500));
                var install = new RetroBatInstall(retrobat.Path);
                var roms = retrobat.Combine("roms");

                var report = StartupSync.Run(install, cloud.Path, roms);

                Assert.Equal(new[] { "ps2/Pronto (USA).zip" }, report.Library.PlaceholdersCreated);
                Assert.False(File.Exists(Path.Combine(roms, "ps2", "Baixando (USA).zip.opdownload")));
                Assert.DoesNotContain("Baixando", File.ReadAllText(Path.Combine(install.GamelistsPath, "ps2", "gamelist.xml")));
                Assert.Empty(report.PartialDownloads.Errors);
            }
        }
    }
}
