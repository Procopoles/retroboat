using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Retroboat.Cloud;
using Retroboat.Downloads;
using Retroboat.Library;
using Retroboat.Transfers;
using Xunit;

namespace Retroboat.Tests
{
    public class DownloadTests
    {
        static DownloadItem Item(string cloud = "Jogo.zip", string local = "Jogo.iso", string system = "ps2") => new DownloadItem
        {
            System = system,
            CloudName = cloud,
            LocalName = local,
            Title = "Jogo"
        };

        static string Marker(RetroBatInstall install, string local) => Path.Combine(install.RetroboatDataPath, "estado", "baixando", local);

        static OnDemandResult Installed() => new OnDemandResult(OnDemandOutcome.Installed);

        [Fact]
        public void Queue_keeps_order_across_instances_and_marks_the_game_for_the_theme()
        {
            using (var dir = new TempDir())
            {
                var install = new RetroBatInstall(dir.Path);
                var queue = new DownloadQueue(install);

                Assert.True(queue.Add(Item("A.zip", "A.iso")));
                Assert.True(queue.Add(Item("B.zip", "B.iso")));
                Assert.False(queue.Add(Item("A.zip", "A.iso")));

                var other = new DownloadQueue(install);
                Assert.Equal(new[] { "A.zip", "B.zip" }, other.Items().Select(i => i.CloudName));
                Assert.Equal("Jogo", other.Items()[0].Title);
                Assert.True(File.Exists(Marker(install, "A.iso")));
                Assert.True(File.Exists(Marker(install, "B.iso")));
            }
        }

        [Fact]
        public void Removing_clears_the_marker_unless_another_system_has_the_same_file_name()
        {
            using (var dir = new TempDir())
            {
                var install = new RetroBatInstall(dir.Path);
                var queue = new DownloadQueue(install);
                var ps2 = Item("Jogo.iso", "Jogo.iso", "ps2");
                var psx = Item("Jogo.iso", "Jogo.iso", "psx");
                queue.Add(ps2);
                queue.Add(psx);

                Assert.True(queue.Remove(ps2.Key));
                Assert.True(File.Exists(Marker(install, "Jogo.iso")));

                Assert.True(queue.Remove(psx.Key));
                Assert.False(File.Exists(Marker(install, "Jogo.iso")));
                Assert.True(queue.IsEmpty);
                Assert.False(queue.Remove(psx.Key));
            }
        }

        [Fact]
        public void Progress_lives_in_the_marker_and_can_be_read_by_another_process()
        {
            using (var dir = new TempDir())
            {
                var install = new RetroBatInstall(dir.Path);
                var queue = new DownloadQueue(install);
                var item = Item();
                queue.Add(item);

                Assert.False(queue.TryReadProgress(item, out _, out _));

                queue.WriteProgress(item, 1024, 4096);

                Assert.True(new DownloadQueue(install).TryReadProgress(item, out long done, out long total));
                Assert.Equal(1024, done);
                Assert.Equal(4096, total);
            }
        }

        [Fact]
        public void Reconcile_drops_finished_games_and_orphan_markers()
        {
            using (var dir = new TempDir())
            {
                var install = new RetroBatInstall(dir.Path);
                var queue = new DownloadQueue(install);
                queue.Add(Item("Pendente.zip", "Pendente.iso"));
                queue.Add(Item("Pronto.zip", "Pronto.iso"));
                dir.WriteText(@"retroboat\estado\baixando\Sobrou.iso", "");

                queue.Reconcile(i => i.CloudName == "Pendente.zip");

                Assert.Equal("Pendente.zip", queue.Items().Single().CloudName);
                Assert.True(File.Exists(Marker(install, "Pendente.iso")));
                Assert.False(File.Exists(Marker(install, "Pronto.iso")));
                Assert.False(File.Exists(Marker(install, "Sobrou.iso")));
            }
        }

        [Fact]
        public void Reconcile_with_the_real_rule_keeps_only_empty_markers_in_the_games_folder()
        {
            using (var dir = new TempDir())
            {
                dir.WriteText(@"retroboat\retroboat.ini", "[Library]\r\nRomsPath=" + dir.Combine("jogos") + "\r\n");
                dir.WriteFile(@"jogos\ps2\Vazio.iso", new byte[0]);
                dir.WriteFile(@"jogos\ps2\Baixado.iso", new byte[10]);

                var install = new RetroBatInstall(dir.Path);
                var queue = new DownloadQueue(install);
                queue.Add(Item("Vazio.zip", "Vazio.iso"));
                queue.Add(Item("Baixado.zip", "Baixado.iso"));
                queue.Add(Item("Sumiu.zip", "Sumiu.iso"));

                DownloadService.Reconcile(install, queue);

                Assert.Equal("Vazio.zip", queue.Items().Single().CloudName);
            }
        }

        [Fact]
        public void Service_downloads_in_order_and_empties_the_queue()
        {
            using (var dir = new TempDir())
            {
                var queue = new DownloadQueue(new RetroBatInstall(dir.Path));
                queue.Add(Item("A.zip", "A.iso"));
                queue.Add(Item("B.zip", "B.iso"));

                var order = new List<string>();
                var completed = new List<DownloadStatus>();
                var service = new DownloadService(queue, (item, progress, token) =>
                {
                    order.Add(item.CloudName);
                    progress.Report(new TransferProgress { BytesDone = 5, BytesTotal = 10 });
                    return Installed();
                }, () => true);
                service.Completed += completed.Add;

                service.Run(CancellationToken.None);

                Assert.Equal(new[] { "A.zip", "B.zip" }, order);
                Assert.True(queue.IsEmpty);
                Assert.All(completed, c => Assert.Equal(DownloadPhase.Finished, c.Phase));
                Assert.Equal(1, completed[0].Waiting);
                Assert.Equal(0, completed[1].Waiting);
                Assert.Equal(10, completed[0].BytesTotal);
            }
        }

        [Fact]
        public void Service_reports_progress_and_how_many_are_waiting()
        {
            using (var dir = new TempDir())
            {
                var queue = new DownloadQueue(new RetroBatInstall(dir.Path));
                queue.Add(Item("A.zip", "A.iso"));
                queue.Add(Item("B.zip", "B.iso"));

                var phases = new List<DownloadStatus>();
                var service = new DownloadService(queue, (item, progress, token) =>
                {
                    progress.Report(new TransferProgress { BytesDone = 25, BytesTotal = 100, BytesPerSecond = 5 });
                    return Installed();
                }, () => true);
                service.Changed += phases.Add;

                service.Run(CancellationToken.None);

                Assert.Equal(DownloadPhase.Preparing, phases[0].Phase);
                Assert.Equal(DownloadPhase.Downloading, phases[1].Phase);
                Assert.Equal(0.25, phases[1].Fraction);
                Assert.Equal(1, phases[1].Waiting);
            }
        }

        [Fact]
        public void Failure_is_reported_and_the_next_game_still_downloads()
        {
            using (var dir = new TempDir())
            {
                var queue = new DownloadQueue(new RetroBatInstall(dir.Path));
                queue.Add(Item("Ruim.zip", "Ruim.iso"));
                queue.Add(Item("Bom.zip", "Bom.iso"));

                var completed = new List<DownloadStatus>();
                var service = new DownloadService(queue, (item, progress, token) =>
                    item.CloudName == "Ruim.zip" ? new OnDemandResult(OnDemandOutcome.Failed, "Espaço insuficiente") : Installed(), () => true);
                service.Completed += completed.Add;

                service.Run(CancellationToken.None);

                Assert.Equal(DownloadPhase.Failed, completed[0].Phase);
                Assert.Equal("Espaço insuficiente", completed[0].Message);
                Assert.Equal(DownloadPhase.Finished, completed[1].Phase);
                Assert.True(queue.IsEmpty);
            }
        }

        [Fact]
        public void Unexpected_exception_counts_as_failure_and_does_not_stop_the_service()
        {
            using (var dir = new TempDir())
            {
                var queue = new DownloadQueue(new RetroBatInstall(dir.Path));
                queue.Add(Item());

                DownloadStatus completed = null;
                var service = new DownloadService(queue, (item, progress, token) => throw new InvalidOperationException("quebrou"), () => true);
                service.Completed += s => completed = s;

                service.Run(CancellationToken.None);

                Assert.Equal(DownloadPhase.Failed, completed.Phase);
                Assert.Contains("quebrou", completed.Message);
                Assert.True(queue.IsEmpty);
            }
        }

        [Fact]
        public void Taking_the_game_off_the_queue_cancels_the_download()
        {
            using (var dir = new TempDir())
            {
                var queue = new DownloadQueue(new RetroBatInstall(dir.Path));
                var item = Item();
                queue.Add(item);

                DownloadItem cancelled = null;
                bool completed = false;
                var service = new DownloadService(queue, (i, progress, token) =>
                {
                    // Outro processo (a tela de cancelar) tira o jogo da fila.
                    new DownloadQueue(new RetroBatInstall(dir.Path)).Remove(i.Key);
                    token.WaitHandle.WaitOne(TimeSpan.FromSeconds(10));
                    return new OnDemandResult(token.IsCancellationRequested ? OnDemandOutcome.Cancelled : OnDemandOutcome.Installed);
                }, () => true, TimeSpan.FromMilliseconds(20));
                service.Cancelled += i => cancelled = i;
                service.Completed += s => completed = true;

                service.Run(CancellationToken.None);

                Assert.Equal(item.Key, cancelled.Key);
                Assert.False(completed);
                Assert.True(queue.IsEmpty);
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void A_cancel_nobody_asked_for_keeps_the_game_waits_and_continues(bool throws)
        {
            using (var dir = new TempDir())
            {
                var queue = new DownloadQueue(new RetroBatInstall(dir.Path));
                queue.Add(Item());

                // O Google Drive reiniciou no meio: a leitura volta "cancelada", mas o jogo continua na fila.
                int attempts = 0;
                var phases = new List<DownloadPhase>();
                var service = new DownloadService(queue, (i, progress, token) =>
                {
                    if (++attempts > 1)
                        return Installed();
                    if (throws)
                        throw new OperationCanceledException();
                    return new OnDemandResult(OnDemandOutcome.Cancelled);
                }, () => true, TimeSpan.FromMilliseconds(5));
                service.Changed += s => phases.Add(s.Phase);

                bool cancelled = false;
                DownloadStatus completed = null;
                service.Cancelled += _ => cancelled = true;
                service.Completed += s => completed = s;

                service.Run(CancellationToken.None);

                Assert.False(cancelled);
                Assert.Equal(2, attempts);
                Assert.Contains(DownloadPhase.WaitingForCloud, phases);
                Assert.Equal(DownloadPhase.Finished, completed.Phase);
            }
        }

        [Fact]
        public void After_the_drive_drops_it_pauses_before_trying_again()
        {
            using (var dir = new TempDir())
            {
                var queue = new DownloadQueue(new RetroBatInstall(dir.Path));
                queue.Add(Item());

                var times = new List<DateTime>();
                var service = new DownloadService(queue, (i, progress, token) =>
                {
                    times.Add(DateTime.UtcNow);
                    return times.Count == 1 ? new OnDemandResult(OnDemandOutcome.CloudUnavailable) : Installed();
                }, () => true, TimeSpan.FromMilliseconds(5)) { RetryPause = TimeSpan.FromMilliseconds(300) };

                service.Run(CancellationToken.None);

                Assert.Equal(2, times.Count);
                Assert.True(times[1] - times[0] >= TimeSpan.FromMilliseconds(250));
            }
        }

        [Fact]
        public void Stopping_because_the_es_closed_keeps_the_game_for_the_next_session()
        {
            using (var dir = new TempDir())
            using (var stop = new CancellationTokenSource())
            {
                var queue = new DownloadQueue(new RetroBatInstall(dir.Path));
                queue.Add(Item());

                bool cancelled = false;
                var service = new DownloadService(queue, (i, progress, token) =>
                {
                    stop.Cancel();
                    return new OnDemandResult(OnDemandOutcome.Cancelled);
                }, () => true);
                service.Cancelled += _ => cancelled = true;

                service.Run(stop.Token);

                Assert.False(cancelled);
                Assert.Equal("Jogo.zip", queue.Items().Single().CloudName);
            }
        }

        [Fact]
        public void Without_the_drive_it_waits_and_continues_when_the_drive_returns()
        {
            using (var dir = new TempDir())
            {
                var queue = new DownloadQueue(new RetroBatInstall(dir.Path));
                queue.Add(Item());

                int attempts = 0;
                int checks = 0;
                var phases = new List<DownloadPhase>();
                var service = new DownloadService(queue, (i, progress, token) =>
                    ++attempts == 1 ? new OnDemandResult(OnDemandOutcome.CloudUnavailable, "sem Drive") : Installed(),
                    () => ++checks >= 3, TimeSpan.FromMilliseconds(5));
                service.Changed += s => phases.Add(s.Phase);

                DownloadStatus completed = null;
                service.Completed += s => completed = s;

                service.Run(CancellationToken.None);

                Assert.Equal(2, attempts);
                Assert.Equal(new[] { DownloadPhase.Preparing, DownloadPhase.WaitingForCloud, DownloadPhase.Preparing }, phases);
                Assert.Equal(DownloadPhase.Finished, completed.Phase);
            }
        }

        [Fact]
        public void Waiting_for_the_drive_can_be_cancelled()
        {
            using (var dir = new TempDir())
            {
                var queue = new DownloadQueue(new RetroBatInstall(dir.Path));
                var item = Item();
                queue.Add(item);

                bool cancelled = false;
                var service = new DownloadService(queue, (i, progress, token) => new OnDemandResult(OnDemandOutcome.CloudUnavailable), () =>
                {
                    new DownloadQueue(new RetroBatInstall(dir.Path)).Remove(item.Key);
                    return false;
                }, TimeSpan.FromMilliseconds(5));
                service.Cancelled += _ => cancelled = true;

                service.Run(CancellationToken.None);

                Assert.True(cancelled);
                Assert.True(queue.IsEmpty);
            }
        }

        [Fact]
        public void Flip_shows_only_the_first_face_at_the_start_and_only_the_second_at_the_end()
        {
            int[] from = Face(0xFF112233), to = Face(0xFF445566), frame = new int[from.Length];

            HeaderFlip.Render(from, to, 8, 8, 0, frame, 0);
            Assert.Equal(from, frame);

            HeaderFlip.Render(from, to, 8, 8, 1, frame, 0);
            Assert.Equal(to, frame);
        }

        [Fact]
        public void Flip_in_the_middle_shows_the_first_face_above_and_the_second_below_darkened()
        {
            const int size = 40;
            var from = new int[size * size];
            var to = new int[size * size];
            for (int i = 0; i < from.Length; i++)
            {
                from[i] = unchecked((int)0xFFFF0000);
                to[i] = unchecked((int)0xFF00FF00);
            }

            var frame = new int[from.Length];
            HeaderFlip.Render(from, to, size, size, 0.5, frame, unchecked((int)0xFF000000));

            int above = frame[(size / 4) * size + size / 2];
            int below = frame[(size * 3 / 4) * size + size / 2];

            Assert.True((above & 0x00FF0000) != 0 && (above & 0x0000FF00) == 0, "a face que sai fica em cima");
            Assert.True((below & 0x0000FF00) != 0 && (below & 0x00FF0000) == 0, "a face que entra vem de baixo");
            Assert.True((above & 0x00FF0000) >> 16 < 0xFF, "a face virada escurece");
            Assert.Equal(unchecked((int)0xFF000000), above & unchecked((int)0xFF000000));
        }

        [Fact]
        public void Flip_never_shows_the_real_header_between_the_faces()
        {
            const int size = 24;
            int[] from = Face(0xFF112233, size), to = Face(0xFF445566, size), frame = new int[size * size];

            foreach (var t in new[] { 0.05, 0.25, 0.5, 0.75, 0.95 })
            {
                HeaderFlip.Render(from, to, size, size, t, frame, unchecked((int)0xFF000000));
                Assert.All(frame, pixel => Assert.Equal(0xFF, (pixel >> 24) & 0xFF));
            }
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(1, 1)]
        [InlineData(-3, 0)]
        [InlineData(7, 1)]
        public void Ease_stays_between_zero_and_one(double t, double expected)
        {
            Assert.Equal(expected, HeaderFlip.Ease(t));
        }

        [Fact]
        public void Ease_starts_and_lands_slowly_without_bouncing()
        {
            Assert.Equal(0.5, HeaderFlip.Ease(0.5), 6);
            Assert.True(HeaderFlip.Ease(0.1) < 0.1, "o giro precisa aparecer no começo");
            Assert.True(HeaderFlip.Ease(0.9) > 0.9, "e pousar devagar");

            double previous = 0;
            for (double t = 0; t <= 1; t += 0.05)
            {
                double value = HeaderFlip.Ease(t);
                Assert.True(value >= previous && value <= 1);
                previous = value;
            }
        }

        [Fact]
        public void Header_region_stays_on_the_screen_and_scales_with_it()
        {
            foreach (var theme in new[] { "es-theme-carbon", "es-theme-PlayStation-X", "outro", null })
            {
                foreach (var size in new[] { new[] { 1920, 1080 }, new[] { 1280, 720 }, new[] { 3840, 2160 }, new[] { 1024, 768 }, new[] { 2560, 1080 } })
                {
                    var region = HeaderRegion.For(theme, size[0], size[1]);

                    Assert.True(region.X >= 0 && region.Y >= 0, theme + " " + size[0]);
                    Assert.True(region.X + region.W <= size[0] && region.Y + region.H <= size[1], theme + " " + size[0]);
                    Assert.True(region.W > region.H * 3, "precisa caber nome, barra e porcentagem");
                }
            }

            Assert.InRange(HeaderRegion.For(null, 3840, 2160).H - HeaderRegion.For(null, 1920, 1080).H * 2, -2, 2);
        }

        [Fact]
        public void PlayStation_X_header_is_lower_than_in_other_themes()
        {
            Assert.True(HeaderRegion.For("es-theme-PlayStation-X", 1920, 1080).Y > HeaderRegion.For("es-theme-carbon", 1920, 1080).Y);
        }

        static int[] Face(uint color, int size = 8)
        {
            var pixels = new int[size * size];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = unchecked((int)color);

            return pixels;
        }
    }
}
