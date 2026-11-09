using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Retroboat.Cloud;
using Retroboat.Library;
using Retroboat.Settings;
using Xunit;

namespace Retroboat.Tests
{
    public class LibraryIdentityTests
    {
        const string OtherId = "3f2504e0-4f89-11d3-9a0c-0305e82c3301";

        /// <summary>Uma "conta" do Google Drive de mentira: &lt;dir&gt;\&lt;nome&gt;\Meu Drive.</summary>
        static CloudDrive Account(TempDir dir, string name)
        {
            var myDrive = dir.Combine(name, "Meu Drive");
            Directory.CreateDirectory(myDrive);
            return new CloudDrive(dir.Combine(name) + "\\", myDrive);
        }

        static void WriteIdentity(string retroboatPath, string id, string fileName = LibraryIdentity.FileName, string pc = "PC-SALA")
        {
            Directory.CreateDirectory(retroboatPath);
            File.WriteAllText(Path.Combine(retroboatPath, fileName),
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n<biblioteca id=\"" + id + "\" criada=\"2026-11-01T10:00:00Z\" pc=\"" + pc + "\" versao=\"1\" />\r\n");
        }

        /// <summary>Caminho, tamanho, SHA-256 e data de cada arquivo, e cada pasta.</summary>
        static List<string> Snapshot(string root)
        {
            using (var sha = SHA256.Create())
            {
                var files = Directory.GetFiles(root, "*", SearchOption.AllDirectories)
                    .Select(f => f.Substring(root.Length) + " " + new FileInfo(f).Length + " " +
                                 BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(f))) + " " + File.GetLastWriteTimeUtc(f).Ticks);
                var dirs = Directory.GetDirectories(root, "*", SearchOption.AllDirectories).Select(d => d.Substring(root.Length) + "\\");
                return files.Concat(dirs).OrderBy(x => x, StringComparer.Ordinal).ToList();
            }
        }

        [Fact]
        public void Adopt_creates_one_identity_and_never_rewrites_it()
        {
            using (var dir = new TempDir())
            {
                var retroboat = dir.Combine("retroboat");
                Directory.CreateDirectory(retroboat);

                var first = LibraryIdentity.Adopt(retroboat, "PC-QUARTO");
                var before = Snapshot(dir.Path);
                var second = LibraryIdentity.Adopt(retroboat, "OUTRO-PC");

                Assert.True(Guid.TryParse(first.Id, out _));
                Assert.Equal("PC-QUARTO", first.Pc);
                Assert.Equal(first.Id, second.Id);
                Assert.Equal(before, Snapshot(dir.Path));
                Assert.Equal(new[] { LibraryIdentity.FileName }, Directory.GetFiles(retroboat).Select(Path.GetFileName));
            }
        }

        [Fact]
        public void Identity_from_another_pc_is_kept_as_it_is()
        {
            using (var dir = new TempDir())
            {
                var retroboat = dir.Combine("retroboat");
                WriteIdentity(retroboat, OtherId);
                var before = Snapshot(dir.Path);

                var identity = LibraryIdentity.Adopt(retroboat);

                Assert.Equal(OtherId, identity.Id);
                Assert.Equal("PC-SALA", identity.Pc);
                Assert.Equal(before, Snapshot(dir.Path));
            }
        }

        [Fact]
        public void Damaged_identity_is_left_alone_and_nothing_is_adopted()
        {
            using (var dir = new TempDir())
            {
                var retroboat = dir.Combine("retroboat");
                dir.WriteText(@"retroboat\" + LibraryIdentity.FileName, "<biblioteca id=");
                var before = Snapshot(dir.Path);

                Assert.Null(LibraryIdentity.Adopt(retroboat));
                Assert.Equal(before, Snapshot(dir.Path));
            }
        }

        [Fact]
        public void Conflict_copy_made_by_the_drive_still_counts()
        {
            using (var dir = new TempDir())
            {
                var retroboat = dir.Combine("retroboat");
                WriteIdentity(retroboat, OtherId, "biblioteca (1).xml");

                Assert.True(LibraryIdentity.Has(retroboat, OtherId));
                Assert.True(LibraryIdentity.Has(retroboat, OtherId.ToUpperInvariant()));
                Assert.False(LibraryIdentity.Has(retroboat, Guid.NewGuid().ToString()));
            }
        }

        [Fact]
        public void With_an_id_only_that_library_counts_in_any_account_and_never_another()
        {
            using (var dir = new TempDir())
            {
                var a = Account(dir, "G");
                var b = Account(dir, "H");
                dir.WriteFile(@"G\Meu Drive\retroboat\jogos\ps2\Outro.zip", TempDir.RandomBytes(100));
                WriteIdentity(b.RetroboatPath, OtherId);
                var drives = new[] { a, b };

                var found = CloudLocator.Locate(null, OtherId, drives);
                Assert.Equal(b.RetroboatPath, found.RetroboatPath);
                Assert.True(found.Exists);
                Assert.Equal(OtherId, found.LibraryId);

                // A conta da biblioteca saiu (ou ainda está carregando): nunca usar a pasta de G:, mesmo ela existindo.
                var missing = CloudLocator.Locate(null, OtherId, new[] { a });
                Assert.True(missing.LibraryMissing);
                Assert.False(missing.Exists);
                Assert.True(Directory.Exists(missing.RetroboatPath));

                Assert.Null(CloudLocator.Locate(null, OtherId, new CloudDrive[0]));

                // Sem id, como antes: a primeira conta com a pasta.
                Assert.Equal(a.RetroboatPath, CloudLocator.Locate(null, null, drives).RetroboatPath);
            }
        }

        [Fact]
        public void One_library_is_adopted_and_only_the_identity_file_is_added()
        {
            using (var dir = new TempDir())
            {
                var a = Account(dir, "G");
                var b = Account(dir, "H");
                dir.WriteFile(@"H\Meu Drive\retroboat\jogos\ps2\God of War (USA).zip", TempDir.RandomBytes(5000));
                dir.WriteFile(@"H\Meu Drive\retroboat\jogos\psx", TempDir.RandomBytes(10));
                dir.WriteFile(@"H\Meu Drive\retroboat\saves\ps2\pcsx2\memcards\Mcd001.ps2\v1.gz", TempDir.RandomBytes(300));
                dir.WriteFile(@"H\Meu Drive\retroboat\bios\pcsx2\bios\x.bin", TempDir.RandomBytes(400));
                dir.WriteFile(@"G\Meu Drive\Documentos\nota.txt", TempDir.RandomBytes(20));
                var before = Snapshot(dir.Path);
                var settings = new RetroboatSettings();

                var choice = LibraryChoice.ChooseAutomatically(settings, new[] { a, b }, "PC-QUARTO");

                Assert.Equal(LibraryChoiceOutcome.Adopted, choice.Outcome);
                Assert.Equal(b.RetroboatPath, choice.Drive.RetroboatPath);
                Assert.Equal(choice.Identity.Id, settings.CloudLibrary);
                Assert.Equal("Meu Drive (" + b.DriveRoot.TrimEnd('\\') + ")", choice.Drive.DisplayName);

                var added = Snapshot(dir.Path).Except(before).ToList();
                Assert.Single(added);
                Assert.StartsWith(@"\H\Meu Drive\retroboat\" + LibraryIdentity.FileName + " ", added[0]);
                Assert.Empty(before.Except(Snapshot(dir.Path)));
            }
        }

        [Fact]
        public void Two_libraries_or_none_write_nothing_and_wait_for_the_setup()
        {
            using (var dir = new TempDir())
            {
                var a = Account(dir, "G");
                var b = Account(dir, "H");
                Directory.CreateDirectory(Path.Combine(a.RetroboatPath, "jogos"));
                Directory.CreateDirectory(Path.Combine(b.RetroboatPath, "jogos"));
                var before = Snapshot(dir.Path);

                var several = new RetroboatSettings();
                Assert.Equal(LibraryChoiceOutcome.Several, LibraryChoice.ChooseAutomatically(several, new[] { a, b }).Outcome);
                Assert.Null(several.CloudLibrary);

                var empty = Account(dir, "I");
                var before2 = Snapshot(dir.Path);
                var none = new RetroboatSettings();
                Assert.Equal(LibraryChoiceOutcome.NoLibrary, LibraryChoice.ChooseAutomatically(none, new[] { empty }).Outcome);
                Assert.Null(none.CloudLibrary);
                Assert.Empty(Directory.GetFileSystemEntries(empty.MyDrive));

                Assert.Equal(LibraryChoiceOutcome.NoDrive, LibraryChoice.ChooseAutomatically(new RetroboatSettings(), new CloudDrive[0]).Outcome);
                Assert.Equal(before2, Snapshot(dir.Path));
                Assert.Empty(before.Except(Snapshot(dir.Path)));
            }
        }

        [Fact]
        public void Chosen_or_configured_library_is_not_touched()
        {
            using (var dir = new TempDir())
            {
                var a = Account(dir, "G");
                Directory.CreateDirectory(a.RetroboatPath);
                var before = Snapshot(dir.Path);

                var chosen = new RetroboatSettings { CloudLibrary = OtherId };
                Assert.Equal(LibraryChoiceOutcome.AlreadyChosen, LibraryChoice.ChooseAutomatically(chosen, new[] { a }).Outcome);
                Assert.Equal(OtherId, chosen.CloudLibrary);

                var configured = new RetroboatSettings { CloudPath = dir.Combine("nuvem") };
                Assert.Equal(LibraryChoiceOutcome.Configured, LibraryChoice.ChooseAutomatically(configured, new[] { a }).Outcome);
                Assert.Null(configured.CloudLibrary);

                Assert.Equal(before, Snapshot(dir.Path));
            }
        }

        [Fact]
        public void Console_folders_only_go_to_the_library_of_this_pc()
        {
            using (var dir = new TempDir())
            {
                dir.WriteText(@"RetroBat\emulationstation\.emulationstation\es_systems.cfg",
                    "<systemList><system><name>psx</name><path>~\\..\\roms\\psx</path></system><system><name>ps2</name><path>~\\..\\roms\\ps2</path></system></systemList>");
                var install = new RetroBatInstall(dir.Combine("RetroBat"));
                var a = Account(dir, "G");
                var b = Account(dir, "H");
                Directory.CreateDirectory(Path.Combine(a.RetroboatPath, "jogos", "ps2"));
                WriteIdentity(b.RetroboatPath, OtherId);

                // Biblioteca deste PC fora do ar: a pasta de G: existe e tem jogos, mas não é dela. Nada é criado ali.
                var missing = CloudLocator.Locate(null, OtherId, new[] { a });
                Assert.Empty(CloudFolders.Ensure(install, missing).SystemsCreated);
                Assert.False(Directory.Exists(Path.Combine(a.RetroboatPath, "jogos", "psx")));

                // Biblioteca identificada sem a pasta jogos: jogos e as pastas de console são criados.
                var found = CloudLocator.Locate(null, OtherId, new[] { a, b });
                Assert.Equal(new[] { "psx", "ps2" }, CloudFolders.Ensure(install, found).SystemsCreated);
                Assert.True(Directory.Exists(Path.Combine(b.RetroboatPath, "jogos", "psx")));
            }
        }

        [Fact]
        public void Library_id_survives_saving_the_settings()
        {
            using (var dir = new TempDir())
            {
                var path = dir.Combine("retroboat.ini");
                new RetroboatSettings { RomsPath = @"D:\Jogos", CloudLibrary = OtherId }.Save(path);

                var loaded = RetroboatSettings.Load(path);

                Assert.Equal(OtherId, loaded.CloudLibrary);
                Assert.Equal(@"D:\Jogos", loaded.RomsPath);
                Assert.Null(loaded.CloudPath);
            }
        }
    }
}
