using System.IO;
using System.Linq;
using Retroboat.Accounts;
using Retroboat.Library;
using Xunit;

namespace Retroboat.Tests
{
    public class AccountSyncTests
    {
        const string Settings = @"emulationstation\.emulationstation\es_settings.cfg";

        static RetroBatInstall Install(TempDir dir, string name) => new RetroBatInstall(dir.Combine(name));

        static string SettingsPath(RetroBatInstall install) => Path.Combine(install.UserEmulationStationPath, "es_settings.cfg");

        static void WriteSettings(TempDir dir, string pc, string user, string pass)
        {
            dir.WriteText(pc + "\\" + Settings,
                "<?xml version=\"1.0\"?>\n<config>\n\t<bool name=\"ScrapeVideos\" value=\"true\" />\n" +
                "\t<string name=\"ScreenScraperUser\" value=\"" + user + "\" />\n" +
                "\t<string name=\"ScreenScraperPass\" value=\"" + pass + "\" />\n</config>\n");
        }

        static string Get(RetroBatInstall install, string name) => EsSettings.Load(SettingsPath(install)).Get(name);

        static AccountSyncResult Sync(RetroBatInstall install, TempDir cloud, string pc) => new AccountSync(install, cloud.Path, pc).Apply();

        [Fact]
        public void A_pc_without_login_receives_the_one_from_the_cloud()
        {
            using (var cloud = new TempDir())
            using (var pcs = new TempDir())
            {
                WriteSettings(pcs, "A", "usuario-teste", "senha-teste-123");
                var a = Install(pcs, "A");
                var b = Install(pcs, "B");

                Assert.Equal(new[] { "ScreenScraper" }, Sync(a, cloud, "PC-A").Sent);
                var received = Sync(b, cloud, "PC-B");

                Assert.Equal(new[] { "ScreenScraper" }, received.Received);
                Assert.Equal("usuario-teste", Get(b, "ScreenScraperUser"));
                Assert.Equal("senha-teste-123", Get(b, "ScreenScraperPass"));
                Assert.False(File.Exists(cloud.Combine(@"contas\PC-B.xml")));
            }
        }

        [Fact]
        public void The_password_is_not_in_plain_text_in_the_cloud_nor_in_the_local_state()
        {
            using (var cloud = new TempDir())
            using (var pcs = new TempDir())
            {
                WriteSettings(pcs, "A", "usuario-teste", "senha-teste-123");
                var a = Install(pcs, "A");

                Sync(a, cloud, "PC-A");

                Assert.DoesNotContain("senha-teste-123", File.ReadAllText(cloud.Combine(@"contas\PC-A.xml")));
                Assert.DoesNotContain("senha-teste-123", File.ReadAllText(pcs.Combine(@"A\retroboat\" + AccountSync.StateFileName)));
            }
        }

        [Fact]
        public void A_login_changed_on_one_pc_reaches_the_others()
        {
            using (var cloud = new TempDir())
            using (var pcs = new TempDir())
            {
                WriteSettings(pcs, "A", "usuario-teste", "senha-antiga");
                var a = Install(pcs, "A");
                var b = Install(pcs, "B");
                Sync(a, cloud, "PC-A");
                Sync(b, cloud, "PC-B");

                System.Threading.Thread.Sleep(1100); // A nuvem guarda a hora com precisão de segundos.
                WriteSettings(pcs, "A", "usuario-teste", "senha-nova");
                Assert.Equal(new[] { "ScreenScraper" }, Sync(a, cloud, "PC-A").Sent);
                Assert.Equal(new[] { "ScreenScraper" }, Sync(b, cloud, "PC-B").Received);

                Assert.Equal("senha-nova", Get(b, "ScreenScraperPass"));
            }
        }

        [Fact]
        public void A_login_typed_on_this_pc_is_kept_and_sent()
        {
            using (var cloud = new TempDir())
            using (var pcs = new TempDir())
            {
                WriteSettings(pcs, "A", "conta-a", "senha-a");
                WriteSettings(pcs, "B", "conta-b", "senha-b");
                var a = Install(pcs, "A");
                var b = Install(pcs, "B");
                Sync(a, cloud, "PC-A");

                var result = Sync(b, cloud, "PC-B");

                Assert.Equal(new[] { "ScreenScraper" }, result.Sent);
                Assert.Equal("conta-b", Get(b, "ScreenScraperUser"));
            }
        }

        [Fact]
        public void Nothing_happens_when_both_sides_agree_and_other_settings_are_kept()
        {
            using (var cloud = new TempDir())
            using (var pcs = new TempDir())
            {
                WriteSettings(pcs, "A", "usuario-teste", "senha-teste");
                var a = Install(pcs, "A");
                var b = Install(pcs, "B");
                pcs.WriteText(@"B\" + Settings, "<?xml version=\"1.0\"?>\n<config>\n\t<bool name=\"ScrapeVideos\" value=\"false\" />\n</config>\n");
                Sync(a, cloud, "PC-A");
                Sync(b, cloud, "PC-B");

                var again = Sync(b, cloud, "PC-B");

                Assert.Empty(again.Sent);
                Assert.Empty(again.Received);
                Assert.Contains("name=\"ScrapeVideos\" value=\"false\"", File.ReadAllText(SettingsPath(b)));
                Assert.Equal(new[] { "PC-A.xml" }, Directory.GetFiles(cloud.Combine("contas")).Select(Path.GetFileName));
            }
        }
    }
}
