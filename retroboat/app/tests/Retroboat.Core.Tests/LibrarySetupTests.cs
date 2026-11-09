using System;
using System.IO;
using System.Linq;
using Retroboat.Cloud;
using Retroboat.Library;
using Retroboat.Settings;
using Xunit;

namespace Retroboat.Tests
{
    public class LibrarySetupTests
    {
        static CloudDrive Account(TempDir dir, string name)
        {
            var myDrive = dir.Combine(name, "Meu Drive");
            Directory.CreateDirectory(myDrive);
            return new CloudDrive(dir.Combine(name) + "\\", myDrive);
        }

        static RetroBatInstall Install(TempDir dir)
        {
            dir.WriteText(@"RetroBat\emulationstation\.emulationstation\es_systems.cfg",
                "<systemList><system><name>psx</name><path>~\\..\\roms\\psx</path></system><system><name>ps2</name><path>~\\..\\roms\\ps2</path></system></systemList>");
            return new RetroBatInstall(dir.Combine("RetroBat"));
        }

        [Fact]
        public void Setup_opens_only_when_the_app_cannot_decide()
        {
            var ready = new RetroboatSettings { RomsPath = @"D:\Jogos", CloudLibrary = Guid.NewGuid().ToString() };
            var firstRun = new RetroboatSettings();

            Assert.Equal(SetupScope.Full, LibrarySetup.Decide(firstRun, false, LibraryChoiceOutcome.Adopted));
            Assert.Equal(SetupScope.Full, LibrarySetup.Decide(ready, true, LibraryChoiceOutcome.AlreadyChosen));
            Assert.Equal(SetupScope.LibraryOnly, LibrarySetup.Decide(ready, false, LibraryChoiceOutcome.Several));
            Assert.Equal(SetupScope.LibraryOnly, LibrarySetup.Decide(ready, false, LibraryChoiceOutcome.NoLibrary));

            // Biblioteca escolhida, adotada agora, Drive fechado ou biblioteca danificada: nada a perguntar.
            foreach (var outcome in new[] { LibraryChoiceOutcome.AlreadyChosen, LibraryChoiceOutcome.Adopted, LibraryChoiceOutcome.NoDrive,
                                            LibraryChoiceOutcome.Unidentified, LibraryChoiceOutcome.Configured })
                Assert.Equal(SetupScope.None, LibrarySetup.Decide(ready, false, outcome));
        }

        [Fact]
        public void Each_account_is_described_by_what_its_library_holds()
        {
            using (var dir = new TempDir())
            {
                var g = Account(dir, "G");
                var h = Account(dir, "H");
                var i = Account(dir, "I");
                dir.WriteFile(@"G\Meu Drive\retroboat\jogos\ps2\A.zip", new byte[10]);
                dir.WriteFile(@"G\Meu Drive\retroboat\jogos\ps2\B.zip", new byte[10]);
                dir.WriteFile(@"G\Meu Drive\retroboat\jogos\psx\C.chd", new byte[10]);
                Directory.CreateDirectory(dir.Combine(@"H\Meu Drive\retroboat"));

                var described = LibrarySetup.Describe(new[] { g, h, i });

                Assert.Equal("3 jogos (ps2, psx)", described[0].Summary);
                Assert.Equal("sem jogos ainda", described[1].Summary);
                Assert.True(described[1].HasLibrary);
                Assert.False(described[2].HasLibrary);
            }
        }

        [Fact]
        public void Creating_makes_the_folder_identity_and_console_folders_and_nothing_else()
        {
            using (var dir = new TempDir())
            {
                var install = Install(dir);
                var g = Account(dir, "G");
                dir.WriteFile(@"G\Meu Drive\Documentos\nota.txt", new byte[5]);
                var settings = new RetroboatSettings();

                var identity = LibrarySetup.Create(install, settings, g, out bool created, "PC-NOVO");

                Assert.True(created);
                Assert.Equal(identity.Id, settings.CloudLibrary);
                Assert.Equal(new[] { LibraryIdentity.FileName }, Directory.GetFiles(g.RetroboatPath).Select(Path.GetFileName));
                Assert.Equal(new[] { "jogos" }, Directory.GetDirectories(g.RetroboatPath).Select(Path.GetFileName));
                Assert.Equal(new[] { "ps2", "psx" }, Directory.GetDirectories(Path.Combine(g.RetroboatPath, "jogos")).Select(Path.GetFileName).OrderBy(n => n));
                Assert.Equal(5, new FileInfo(dir.Combine(@"G\Meu Drive\Documentos\nota.txt")).Length);
            }
        }

        [Fact]
        public void A_library_that_appeared_meanwhile_is_used_instead_of_created()
        {
            using (var dir = new TempDir())
            {
                var install = Install(dir);
                var g = Account(dir, "G");
                var game = dir.WriteFile(@"G\Meu Drive\retroboat\jogos\ps2\Jogo.zip", TempDir.RandomBytes(300));
                var before = File.ReadAllBytes(game);
                var settings = new RetroboatSettings();

                var identity = LibrarySetup.Create(install, settings, g, out bool created);

                Assert.False(created);
                Assert.NotNull(identity);
                Assert.Equal(before, File.ReadAllBytes(game));
                Assert.True(Directory.Exists(Path.Combine(g.RetroboatPath, "jogos", "psx")));
            }
        }

        [Fact]
        public void Using_a_library_with_a_damaged_identity_changes_nothing()
        {
            using (var dir = new TempDir())
            {
                var g = Account(dir, "G");
                var file = dir.WriteText(@"G\Meu Drive\retroboat\" + LibraryIdentity.FileName, "x");
                var settings = new RetroboatSettings();

                Assert.Null(LibrarySetup.Use(settings, g));
                Assert.Null(settings.CloudLibrary);
                Assert.Equal("x", File.ReadAllText(file));
            }
        }

        [Fact]
        public void Folder_suggestions_keep_the_current_one_first_and_prefer_roomy_disks()
        {
            using (var dir = new TempDir())
            {
                var install = new RetroBatInstall(@"C:\RetroBat");
                var disks = new[] { (@"C:\", 100L << 30), (@"D:\", 800L << 30), (@"E:\", 50L << 30), (@"F:\", 300L << 30) };

                var first = LibrarySetup.SuggestRomsFolders(install, null, disks);
                Assert.Equal(new[] { @"C:\RetroBat\roms", @"D:\Jogos", @"F:\Jogos" }, first.Select(s => s.Path));
                Assert.Equal("Pasta padrão do RetroBat", first[0].Note);
                Assert.Equal(800L << 30, first[1].FreeBytes);
                Assert.StartsWith("Disco D: · ", first[1].Label);
                Assert.EndsWith(@" livres · D:\Jogos", first[1].Label);

                var again = LibrarySetup.SuggestRomsFolders(install, @"D:\Jogos\", disks);
                Assert.Equal(new[] { @"D:\Jogos", @"C:\RetroBat\roms", @"F:\Jogos" }, again.Select(s => s.Path));
                Assert.Equal("Pasta atual", again[0].Note);
            }
        }
    }
}
