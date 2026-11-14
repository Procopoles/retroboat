using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using Retroboat.Integrations;
using Retroboat.Library;
using Xunit;

namespace Retroboat.Tests
{
    /// <summary>RB-037: estado das integrações e troca automática de chave, sem ir à internet.</summary>
    public class IntegrationTests
    {
        // Valores inventados, só com o formato das chaves de verdade (o repositório é público).
        const string GoodTgdb = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        const string OldTgdb = "1111111111111111111111111111111111111111111111111111111111111111";
        const string Md5LookAlike = "fedcba9876543210fedcba9876543210";
        const string SsLogin = "devid=retrobat&devpassword=Abc123xyz";
        const string Cheevos = "z=retrobat&y=Q7wErTy9UiOp2AsDfGh4JkLz6XcVbN1m";
        const string Hfs = "Retrobat:Tq7Lm2Xv9Pk4Rz";

        /// <summary>As APIs em memória: só aceitam as chaves de <see cref="Valid"/>.</summary>
        sealed class FakeApis : IHttpProbe
        {
            public readonly HashSet<string> Valid = new HashSet<string> { SsLogin, GoodTgdb, Cheevos, Hfs };
            public bool Offline;
            public readonly List<string> Calls = new List<string>();

            public (int Status, string Body) Send(string url, string postBody = null, string basicAuth = null)
            {
                Calls.Add(url);
                if (Offline)
                    throw new WebException("O nome remoto não pôde ser resolvido");

                if (url.Contains("screenscraper"))
                    return Valid.Any(url.Contains) ? (200, "{\"header\":{\"success\": \"true\"}}") : (403, "Erreur de login : Vérifier vos identifiants développeur !");
                if (url.Contains("thegamesdb"))
                    return Valid.Any(url.Contains) ? (200, "{\"code\":200,\"status\":\"Success\"}") : (403, "{\"code\":403,\"status\":\"Invalid API key was provided.\"}");
                if (url.Contains("retroachievements"))
                    return Valid.Any(url.Contains) ? (200, "[{\"ID\":1}]") : (401, "{\"message\":\"Unauthenticated.\"}");
                if (url.Contains("hfsplay"))
                    return Valid.Contains(basicAuth) ? (200, "{\"token\":\"abc\"}") : (403, "{\"detail\":\"Invalid username/password.\"}");

                return (200, "ok");
            }
        }

        /// <summary>Um "executável do ES" com as strings que o build compila, no meio de bytes quaisquer.</summary>
        static byte[] FakeExe(params string[] strings)
        {
            var bytes = new List<byte>();
            foreach (var text in strings.Concat(new[] { "gridtile.image:selected", "00000000000000000000000000000000", Md5LookAlike }))
            {
                bytes.AddRange(new byte[] { 0, 1, 2 });
                bytes.AddRange(Encoding.ASCII.GetBytes(text));
            }

            bytes.AddRange(new byte[] { 0, 0 });
            return bytes.ToArray();
        }

        sealed class FakeRepository : ICredentialRepository
        {
            public readonly List<CredentialSource> List = new List<CredentialSource>();
            public int Opened;

            public FakeRepository Add(string name, params string[] strings)
            {
                List.Add(new CredentialSource(name, () => { Opened++; return CredentialExtractor.FromBinary(FakeExe(strings)); }));
                return this;
            }

            public IEnumerable<CredentialSource> Sources() => List;
        }

        static RetroBatInstall Install(TempDir dir) => new RetroBatInstall(dir.Combine("RetroBat"));

        static IntegrationStatus Item(IntegrationReport report, string id) => report.Items.Single(i => i.Id == id);

        [Fact]
        public void The_extractor_finds_each_key_format_and_ignores_look_alikes()
        {
            var found = CredentialExtractor.FromBinary(FakeExe(SsLogin, "Retrobat-Emulationstation", GoodTgdb, Cheevos, Hfs, "http://x:y"));

            Assert.Equal(new[] { SsLogin }, found[IntegrationCatalog.ScreenScraperLogin]);
            Assert.Equal(new[] { "Retrobat-Emulationstation" }, found[IntegrationCatalog.ScreenScraperSoftName]);
            Assert.Equal(new[] { GoodTgdb, Md5LookAlike }, found[IntegrationCatalog.GamesDbKey]); // a de 64 primeiro; zeros fora
            Assert.Equal(new[] { Cheevos }, found[IntegrationCatalog.CheevosLogin]);
            Assert.Equal(new[] { Hfs }, found[IntegrationCatalog.HfsLogin]); // "gridtile.image:selected" não é senha
        }

        [Fact]
        public void The_first_check_finds_the_keys_and_the_api_picks_the_right_one()
        {
            using (var dir = new TempDir())
            {
                var install = Install(dir);
                var apis = new FakeApis();
                var repository = new FakeRepository().Add("emulationstation.oficial.exe deste PC", SsLogin, GoodTgdb, Cheevos, Hfs);

                var report = new IntegrationMonitor(install, new IntegrationChecks(apis), repository).Run();

                Assert.All(report.Items.Where(i => i.Id != "igdb"), i => Assert.Equal(IntegrationState.Ok, i.State));
                Assert.Equal(IntegrationState.NoAccount, Item(report, "igdb").State);
                Assert.Empty(report.Messages); // a primeira busca não é troca: nada a avisar
                var store = CredentialStore.Load(CredentialStore.GetPath(install));
                Assert.Equal(GoodTgdb, store.Get(IntegrationCatalog.GamesDbKey));
                Assert.Equal(SsLogin, store.Get(IntegrationCatalog.ScreenScraperLogin));
                Assert.Null(store.ReplacedAt(IntegrationCatalog.GamesDbKey));
                Assert.Contains("estado=\"ok\"", File.ReadAllText(IntegrationStatusFile.GetPath(install)));
            }
        }

        [Fact]
        public void A_key_that_stops_working_is_replaced_from_the_retrobat_repository()
        {
            using (var dir = new TempDir())
            {
                var install = Install(dir);
                var store = CredentialStore.Load(CredentialStore.GetPath(install));
                store.Set(IntegrationCatalog.GamesDbKey, OldTgdb, "teste", DateTime.UtcNow.AddDays(-30), replacement: false);
                store.Save();

                var apis = new FakeApis();
                var repository = new FakeRepository()
                    .Add("emulationstation.exe deste PC", OldTgdb)
                    .Add("repositório do RetroBat (ES de 12/11/2026)", SsLogin, GoodTgdb, Cheevos, Hfs);

                var report = new IntegrationMonitor(install, new IntegrationChecks(apis), repository).Run(force: true);

                var tgdb = Item(report, "thegamesdb");
                Assert.Equal(IntegrationState.Ok, tgdb.State);
                Assert.True(tgdb.ReplacedNow);
                Assert.Contains("trocada sozinha", tgdb.Detail);
                Assert.Contains("repositório do RetroBat (ES de 12/11/2026)", tgdb.Detail);
                Assert.Contains("TheGamesDB: a chave parou de funcionar e foi trocada sozinha.", report.Messages);
                var saved = CredentialStore.Load(CredentialStore.GetPath(install));
                Assert.Equal(GoodTgdb, saved.Get(IntegrationCatalog.GamesDbKey));
                Assert.NotNull(saved.ReplacedAt(IntegrationCatalog.GamesDbKey));
                Assert.StartsWith("repositório do RetroBat", saved.OriginOf(IntegrationCatalog.GamesDbKey));
            }
        }

        [Fact]
        public void Without_internet_nothing_is_called_a_bad_key_and_nothing_is_replaced()
        {
            using (var dir = new TempDir())
            {
                var install = Install(dir);
                var store = CredentialStore.Load(CredentialStore.GetPath(install));
                foreach (var key in new[] { (IntegrationCatalog.GamesDbKey, GoodTgdb), (IntegrationCatalog.ScreenScraperLogin, SsLogin), (IntegrationCatalog.CheevosLogin, Cheevos), (IntegrationCatalog.HfsLogin, Hfs) })
                    store.Set(key.Item1, key.Item2, "teste", DateTime.UtcNow, replacement: false);
                store.Save();
                var repository = new FakeRepository().Add("repositório", GoodTgdb);

                var report = new IntegrationMonitor(install, new IntegrationChecks(new FakeApis { Offline = true }), repository).Run(force: true);

                Assert.Equal(IntegrationState.Unreachable, Item(report, "thegamesdb").State);
                Assert.Equal(0, repository.Opened);
                Assert.Empty(report.Messages);
                Assert.Equal("Sem internet", IntegrationStatusFile.Summarize(report.Items).Text);
            }
        }

        [Fact]
        public void A_rejected_key_without_a_replacement_is_reported_once()
        {
            using (var dir = new TempDir())
            {
                var install = Install(dir);
                var store = CredentialStore.Load(CredentialStore.GetPath(install));
                store.Set(IntegrationCatalog.GamesDbKey, OldTgdb, "teste", DateTime.UtcNow, replacement: false);
                store.Save();
                var apis = new FakeApis();
                var repository = new FakeRepository().Add("repositório", OldTgdb);

                var first = new IntegrationMonitor(install, new IntegrationChecks(apis), repository).Run(force: true);
                var second = new IntegrationMonitor(install, new IntegrationChecks(apis), repository).Run(force: true);

                Assert.Equal(IntegrationState.KeyRejected, Item(first, "thegamesdb").State);
                Assert.Equal("Chave recusada", Item(first, "thegamesdb").Text);
                Assert.Single(first.Messages, m => m.StartsWith("TheGamesDB parou de funcionar"));
                Assert.DoesNotContain(second.Messages, m => m.StartsWith("TheGamesDB"));
                Assert.Equal("TheGamesDB com problema", IntegrationStatusFile.Summarize(second.Items).Text);
            }
        }

        [Fact]
        public void A_recent_check_is_not_repeated_unless_asked()
        {
            using (var dir = new TempDir())
            {
                var install = Install(dir);
                var apis = new FakeApis();
                var repository = new FakeRepository().Add("local", SsLogin, GoodTgdb, Cheevos, Hfs);
                new IntegrationMonitor(install, new IntegrationChecks(apis), repository).Run();
                var calls = apis.Calls.Count;

                var again = new IntegrationMonitor(install, new IntegrationChecks(apis), repository).Run();
                Assert.True(again.Skipped);
                Assert.Equal(calls, apis.Calls.Count);
                Assert.Equal(8, again.Items.Count);

                var forced = new IntegrationMonitor(install, new IntegrationChecks(apis), repository).Run(force: true);
                Assert.False(forced.Skipped);
                Assert.True(apis.Calls.Count > calls);
            }
        }

        [Fact]
        public void The_credentials_file_keeps_values_with_equals_signs()
        {
            using (var dir = new TempDir())
            {
                var path = dir.Combine(@"retroboat\credenciais.ini");
                var store = CredentialStore.Load(path);
                store.Set(IntegrationCatalog.ScreenScraperLogin, SsLogin, "teste", new DateTime(2026, 11, 14, 19, 0, 0, DateTimeKind.Utc), replacement: true);
                store.Save();

                var loaded = CredentialStore.Load(path);
                Assert.Equal(SsLogin, loaded.Get(IntegrationCatalog.ScreenScraperLogin));
                Assert.Equal("teste · 2026-11-14T19:00:00Z", loaded.OriginOf(IntegrationCatalog.ScreenScraperLogin));
                Assert.Equal(new DateTime(2026, 11, 14, 19, 0, 0, DateTimeKind.Utc), loaded.ReplacedAt(IntegrationCatalog.ScreenScraperLogin));
                Assert.Contains("SCREENSCRAPER_DEV_LOGIN=" + SsLogin, File.ReadAllText(path));
            }
        }

        [Theory]
        [InlineData("42.2026.1112.1244-202614121314", "ES de 12/11/2026")]
        [InlineData("estranha", "ES estranha")]
        public void The_repository_version_reads_as_a_date(string version, string expected)
        {
            Assert.Equal(expected, RetroBatCredentialRepository.Describe(version));
        }
    }
}
