using System;
using System.IO;
using System.Linq;
using Retroboat.Cloud;
using Retroboat.Library;
using Retroboat.Settings;
using Xunit;

namespace Retroboat.Tests
{
    public class GoogleDriveAppTests
    {
        const string Exe = @"C:\Program Files\Google\Drive File Stream\1.0.0.0\GoogleDriveFS.exe";

        /// <summary>Uma letra sem unidade neste PC: é o que uma pasta do Drive tem com o Drive fechado.</summary>
        static string MissingDrivePath()
        {
            var letter = "ZYXWVUTSRQPONMLKJIH".First(l => !Directory.Exists(l + @":\"));
            return letter + @":\Meu Drive\retroboat";
        }

        [Fact]
        public void Finds_the_newest_version_that_has_the_program()
        {
            using (var programFiles = new TempDir())
            {
                programFiles.WriteFile(@"Google\Drive File Stream\9.0.0.0\outro.txt", new byte[1]);
                programFiles.WriteFile(@"Google\Drive File Stream\133.0.2.0\GoogleDriveFS.exe", new byte[1]);
                programFiles.WriteFile(@"Google\Drive File Stream\99.0.0.0\GoogleDriveFS.exe", new byte[1]);
                programFiles.WriteFile(@"Google\Drive File Stream\Drivers\GoogleDriveFS.exe", new byte[1]);

                Assert.Equal(programFiles.Combine(@"Google\Drive File Stream\133.0.2.0\GoogleDriveFS.exe"), GoogleDriveApp.FindExecutableIn(programFiles.Path));
            }
        }

        [Fact]
        public void Not_installed_has_no_program()
        {
            using (var programFiles = new TempDir())
                Assert.Null(GoogleDriveApp.FindExecutableIn(programFiles.Path));
        }

        [Fact]
        public void Nothing_to_ask_when_the_cloud_folder_is_there()
        {
            using (var cloud = new TempDir())
                Assert.Equal(CloudAppState.Open, GoogleDriveApp.GetState(cloud.Path, Exe, running: false));
        }

        [Fact]
        public void Installed_and_closed_can_be_opened()
        {
            Assert.Equal(CloudAppState.Closed, GoogleDriveApp.GetState(MissingDrivePath(), Exe, running: false));
        }

        [Fact]
        public void Running_without_the_drive_is_still_connecting()
        {
            Assert.Equal(CloudAppState.Connecting, GoogleDriveApp.GetState(MissingDrivePath(), Exe, running: true));
        }

        [Fact]
        public void Without_the_app_there_is_nothing_to_open()
        {
            Assert.Equal(CloudAppState.NotApplicable, GoogleDriveApp.GetState(MissingDrivePath(), executable: null, running: false));
        }

        [Fact]
        public void A_missing_folder_on_a_disk_that_is_there_is_not_the_apps_fault()
        {
            using (var dir = new TempDir())
                Assert.Equal(CloudAppState.NotApplicable, GoogleDriveApp.GetState(dir.Combine("nao-existe"), Exe, running: false));
        }

        [Fact]
        public void Not_now_lasts_until_the_cloud_comes_back()
        {
            using (var retrobat = new TempDir())
            {
                var install = new RetroBatInstall(retrobat.Path);
                Assert.False(CloudStatusFile.WasDeclined(install));

                CloudStatusFile.RememberDeclined(install);
                CloudStatusFile.Write(install, connected: false);
                Assert.True(CloudStatusFile.WasDeclined(install));

                CloudStatusFile.Write(install, connected: true);
                Assert.False(CloudStatusFile.WasDeclined(install));
            }
        }

        [Fact]
        public void The_theme_state_can_be_read_back()
        {
            using (var retrobat = new TempDir())
            {
                var install = new RetroBatInstall(retrobat.Path);
                Assert.False(CloudStatusFile.IsConnected(install));

                CloudStatusFile.Write(install, connected: true);
                Assert.True(CloudStatusFile.IsConnected(install));

                CloudStatusFile.Write(install, connected: false);
                Assert.False(CloudStatusFile.IsConnected(install));
            }
        }

        [Fact]
        public void Always_open_is_remembered_and_off_by_default()
        {
            using (var dir = new TempDir())
            {
                var path = dir.Combine("retroboat.ini");
                Assert.False(RetroboatSettings.Load(path).CloudAppAutoOpen);

                new RetroboatSettings { CloudAppAutoOpen = true }.Save(path);
                Assert.True(RetroboatSettings.Load(path).CloudAppAutoOpen);
            }
        }

        [Fact]
        public void Both_themes_show_a_cloud_with_an_x_when_off()
        {
            foreach (var overlay in Retroboat.Themes.ThemeOverlays.All)
            {
                var xml = System.Text.Encoding.UTF8.GetString(overlay.ReadOverlay());
                Assert.Contains("rbCloudOfflineX", xml);
                Assert.Contains("&#xF00D;", xml);
            }
        }
    }
}
