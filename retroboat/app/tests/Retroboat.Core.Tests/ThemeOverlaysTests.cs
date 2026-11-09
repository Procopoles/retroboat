using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Retroboat.Es;
using Retroboat.Library;
using Retroboat.Themes;
using Xunit;

namespace Retroboat.Tests
{
    public class ThemeOverlaysTests
    {
        const string PsxThemeXml =
            "<!-- Theme by pajarorrojo -->\r\n<theme defaultView=\"ps4Style\">\r\n\t<formatVersion>7</formatVersion>\r\n" +
            "\t<include>./_theme_views/detailed.xml</include>\r\n\r\n\t<variables>\r\n\t\t<psx-theme-red>DF0024</psx-theme-red>\r\n" +
            "\t</variables>\r\n\r\n</theme>\r\n";

        static string ThemesPath(TempDir retrobat) => new RetroBatInstall(retrobat.Path).ThemesPath;

        static string WritePsxTheme(TempDir retrobat, string folder = "es-theme-PlayStation-X", string version = "43")
        {
            var dir = Path.Combine(ThemesPath(retrobat), folder);
            Directory.CreateDirectory(Path.Combine(dir, "_theme_views"));
            File.WriteAllText(Path.Combine(dir, "theme.xml"), PsxThemeXml);
            File.WriteAllText(Path.Combine(dir, "_theme_views", "ps4-style.xml"), "<theme />");
            File.WriteAllText(Path.Combine(dir, "_theme_views", "top-info.xml"), "<theme><text name=\"version\"><text>v." + version + "</text></text></theme>");
            return dir;
        }

        static ThemeOverlay Psx => ThemeOverlays.All.Single(o => o.Folder == "es-theme-PlayStation-X");
        static ThemeOverlay Carbon => ThemeOverlays.All.Single(o => o.Folder == "es-theme-carbon");

        [Fact]
        public void A_theme_reinstalled_by_es_gets_the_status_back()
        {
            using (var retrobat = new TempDir())
            {
                var dir = WritePsxTheme(retrobat);

                var result = ThemeOverlays.Apply(new RetroBatInstall(retrobat.Path));

                Assert.Equal(new[] { "PlayStation-X: status.xml copiado, include no theme.xml" }, result.Applied);
                Assert.Empty(result.Warnings);
                Assert.Equal(Psx.ReadOverlay(), File.ReadAllBytes(Path.Combine(dir, "retroboat", "status.xml")));

                var themeXml = File.ReadAllText(Path.Combine(dir, "theme.xml"));
                Assert.EndsWith(
                    "\t</variables>\r\n\r\n" +
                    "\t<!-- Retroboat: status dos jogos e da nuvem (o lançador põe de volta se o tema for reinstalado) -->\r\n" +
                    "\t<include>./retroboat/status.xml</include>\r\n" +
                    "</theme>\r\n", themeXml);
                Assert.DoesNotContain("\n", themeXml.Replace("\r\n", ""));
                XDocument.Parse(themeXml);
            }
        }

        [Fact]
        public void A_theme_already_in_place_is_not_touched()
        {
            using (var retrobat = new TempDir())
            {
                var dir = WritePsxTheme(retrobat);
                var install = new RetroBatInstall(retrobat.Path);
                ThemeOverlays.Apply(install);
                var themeXml = File.ReadAllBytes(Path.Combine(dir, "theme.xml"));
                var written = File.GetLastWriteTimeUtc(Path.Combine(dir, "theme.xml"));

                var result = ThemeOverlays.Apply(install);

                Assert.Empty(result.Applied);
                Assert.Equal(new[] { "PlayStation-X" }, result.UpToDate);
                Assert.Equal(themeXml, File.ReadAllBytes(Path.Combine(dir, "theme.xml")));
                Assert.Equal(written, File.GetLastWriteTimeUtc(Path.Combine(dir, "theme.xml")));
            }
        }

        [Fact]
        public void The_carbon_fork_with_its_own_include_and_other_line_endings_stays_as_it_is()
        {
            using (var retrobat = new TempDir())
            {
                var dir = Path.Combine(ThemesPath(retrobat), "es-theme-carbon");
                var themeXml = "<theme>\n  <include>./views/detailed.xml</include>\n" +
                               "  <!-- Retroboat (RB-014): status Baixar/Jogar do jogo selecionado -->\n" +
                               "  <include>./retroboat/status.xml</include>\n</theme>\n";
                retrobat.WriteText(Path.Combine(dir, "theme.xml"), themeXml);
                retrobat.WriteText(Path.Combine(dir, "views", "detailed.xml"), "<theme />");
                var crlf = Encoding.UTF8.GetString(Carbon.ReadOverlay()).Replace("\r\n", "\n").Replace("\n", "\r\n");
                retrobat.WriteFile(Path.Combine(dir, "retroboat", "status.xml"), Encoding.UTF8.GetBytes(crlf));

                var result = ThemeOverlays.Apply(new RetroBatInstall(retrobat.Path));

                Assert.Equal(new[] { "Carbon" }, result.UpToDate);
                Assert.Empty(result.Applied);
                Assert.Equal(themeXml, File.ReadAllText(Path.Combine(dir, "theme.xml")));
                Assert.Equal(crlf, File.ReadAllText(Path.Combine(dir, "retroboat", "status.xml")));
            }
        }

        [Fact]
        public void A_folder_with_the_name_but_not_the_files_of_the_theme_is_left_alone()
        {
            using (var retrobat = new TempDir())
            {
                var dir = Path.Combine(ThemesPath(retrobat), "es-theme-PlayStation-X");
                retrobat.WriteText(Path.Combine(dir, "theme.xml"), PsxThemeXml);

                var result = ThemeOverlays.Apply(new RetroBatInstall(retrobat.Path));

                Assert.Empty(result.Applied);
                Assert.Contains(result.Warnings, w => w.Contains("não parece ser esse tema"));
                Assert.Equal(PsxThemeXml, File.ReadAllText(Path.Combine(dir, "theme.xml")));
                Assert.False(Directory.Exists(Path.Combine(dir, "retroboat")));
            }
        }

        [Fact]
        public void A_newer_theme_version_gets_the_status_and_a_warning_to_check_the_layout()
        {
            using (var retrobat = new TempDir())
            {
                WritePsxTheme(retrobat, version: "44");

                var result = ThemeOverlays.Apply(new RetroBatInstall(retrobat.Path));

                Assert.Single(result.Applied);
                Assert.Contains(result.Warnings, w => w.Contains("feito para a versão 43") && w.Contains("instalada é a 44"));
            }
        }

        [Fact]
        public void A_theme_unzipped_from_github_with_the_branch_suffix_is_recognized()
        {
            using (var retrobat = new TempDir())
            {
                WritePsxTheme(retrobat, folder: "es-theme-PlayStation-X-master");
                Directory.CreateDirectory(Path.Combine(ThemesPath(retrobat), "es-theme-outro"));

                var result = ThemeOverlays.Apply(new RetroBatInstall(retrobat.Path));

                Assert.Equal(new[] { "PlayStation-X (es-theme-PlayStation-X-master): status.xml copiado, include no theme.xml" }, result.Applied);
            }
        }

        [Fact]
        public void Status_only_reports_what_is_missing()
        {
            using (var retrobat = new TempDir())
            {
                var dir = WritePsxTheme(retrobat);

                var result = ThemeOverlays.Apply(new RetroBatInstall(retrobat.Path), dryRun: true);

                Assert.Single(result.Applied);
                Assert.Equal(PsxThemeXml, File.ReadAllText(Path.Combine(dir, "theme.xml")));
                Assert.False(Directory.Exists(Path.Combine(dir, "retroboat")));
            }
        }

        [Fact]
        public void The_include_keeps_the_bom_and_goes_on_its_own_line()
        {
            var bom = new byte[] { 0xEF, 0xBB, 0xBF };
            var original = bom.Concat(Encoding.UTF8.GetBytes("<theme><view name=\"system\" /></theme>")).ToArray();

            var patched = ThemeOverlays.AddInclude(original);

            Assert.Equal(bom, patched.Take(3).ToArray());
            var text = Encoding.UTF8.GetString(patched, 3, patched.Length - 3);
            Assert.Contains("\n  <include>./retroboat/status.xml</include>\n</theme>", text);
            Assert.Equal("system", XDocument.Parse(text).Root.Element("view").Attribute("name").Value);
            Assert.Null(ThemeOverlays.AddInclude(Encoding.UTF8.GetBytes("<theme>")));
        }

        [Fact]
        public void The_es_hook_reapplies_the_status_when_the_theme_is_switched()
        {
            using (var retrobat = new TempDir())
            {
                var install = new RetroBatInstall(retrobat.Path);
                var hook = Path.Combine(install.UserEmulationStationPath, "scripts", ThemeOverlays.HookEvent, ThemeOverlays.HookFile);

                Assert.False(ThemeOverlays.Apply(install, install.RetroboatDataPath).HookWritten);
                Assert.False(File.Exists(hook));

                retrobat.WriteFile(@"retroboat\retroboat-cli.exe", new byte[] { 1 });
                Assert.True(ThemeOverlays.Apply(install, install.RetroboatDataPath).HookWritten);
                Assert.Contains(@"""%~dp0..\..\..\..\retroboat\retroboat-cli.exe"" themes apply --retrobat ""%~dp0..\..\..\..""", File.ReadAllText(hook));
                Assert.False(ThemeOverlays.Apply(install, install.RetroboatDataPath).HookWritten);
            }
        }

        [Fact]
        public void Updating_the_gamelists_in_the_es_syncs_with_the_cloud_first()
        {
            using (var retrobat = new TempDir())
            {
                var install = new RetroBatInstall(retrobat.Path);
                var script = Path.Combine(install.UserEmulationStationPath, "scripts", EsScripts.UpdateGamelistsEvent, EsScripts.UpdateGamelistsFile);

                Assert.False(EsScripts.WriteUpdateGamelists(install, install.RetroboatDataPath));
                Assert.False(File.Exists(script));

                retrobat.WriteFile(@"retroboat\Retroboat.exe", new byte[] { 1 });
                Assert.True(EsScripts.WriteUpdateGamelists(install, install.RetroboatDataPath));
                Assert.Contains(@"""%~dp0..\..\..\..\retroboat\Retroboat.exe"" --refresh --retrobat ""%~dp0..\..\..\..""", File.ReadAllText(script));
                Assert.False(EsScripts.WriteUpdateGamelists(install, install.RetroboatDataPath));

                // Ao lado do retrobat.exe também vale; o caminho continua relativo ao script.
                retrobat.WriteFile("Retroboat.exe", new byte[] { 1 });
                Assert.True(EsScripts.WriteUpdateGamelists(install, install.Root));
                Assert.Contains(@"""%~dp0..\..\..\..\Retroboat.exe"" --refresh", File.ReadAllText(script));
            }
        }

        [Fact]
        public void Bundled_overlays_are_valid_xml()
        {
            foreach (var overlay in ThemeOverlays.All)
                XDocument.Parse(Encoding.UTF8.GetString(overlay.ReadOverlay()).TrimStart('﻿'));
        }

        /// <summary>
        /// Os status.xml do app são cópias dos clones dos temas (..\es-theme-*), onde eles são editados e
        /// testados. Na máquina de quem desenvolve, este teste avisa se uma cópia ficou para trás.
        /// </summary>
        [Fact]
        public void Bundled_overlays_match_the_theme_clones()
        {
            var app = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (app != null && !File.Exists(Path.Combine(app.FullName, "Retroboat.sln")))
                app = app.Parent;
            if (app == null)
                return;

            var clones = Path.Combine(app.FullName, "..", "..", "..");
            foreach (var overlay in ThemeOverlays.All)
            {
                var clone = Path.Combine(clones, overlay.Folder, "retroboat", "status.xml");
                if (!File.Exists(clone))
                    continue;

                Assert.True(
                    File.ReadAllText(clone).Replace("\r", "") == Encoding.UTF8.GetString(overlay.ReadOverlay()).Replace("\r", ""),
                    overlay.Folder + ": o status.xml do app é diferente do clone (" + Path.GetFullPath(clone) + "). Copie o do clone para src\\Retroboat.Core\\Themes\\Overlays.");
            }
        }
    }
}
