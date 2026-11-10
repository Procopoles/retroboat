using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Retroboat.Cloud;
using Retroboat.Downloads;
using Retroboat.Library;
using Retroboat.Transfers;
using Xunit;

namespace Retroboat.Tests
{
    public class DownloadPauseTests
    {
        static DownloadItem Item(string name) => new DownloadItem { System = "ps2", CloudName = name + ".zip", LocalName = name + ".iso", Title = name };

        [Fact]
        public void Pausing_keeps_the_game_in_the_queue_and_resuming_continues_it()
        {
            using (var dir = new TempDir())
            {
                var install = new RetroBatInstall(dir.Path);
                var queue = new DownloadQueue(install);
                queue.Add(Item("A"));
                queue.Add(Item("B"));

                var calls = new List<string>();
                var service = new DownloadService(queue, (item, progress, token) =>
                {
                    calls.Add(item.CloudName);
                    if (calls.Count == 1)
                    {
                        progress.Report(new TransferProgress { BytesDone = 100, BytesTotal = 400 });
                        // O menu do jogo no ES pausa a fila (outro processo).
                        new DownloadQueue(install).SetPaused(true);
                        token.WaitHandle.WaitOne(TimeSpan.FromSeconds(10));
                        return new OnDemandResult(token.IsCancellationRequested ? OnDemandOutcome.Cancelled : OnDemandOutcome.Installed);
                    }

                    return new OnDemandResult(OnDemandOutcome.Installed);
                }, () => true, TimeSpan.FromMilliseconds(20));

                DownloadStatus paused = null;
                service.Changed += status =>
                {
                    if (status.Phase != DownloadPhase.Paused)
                        return;

                    paused = status;
                    ThreadPool.QueueUserWorkItem(_ =>
                    {
                        Thread.Sleep(100);
                        new DownloadQueue(install).SetPaused(false); // RETOMAR
                    });
                };

                bool cancelled = false;
                var completed = new List<string>();
                service.Cancelled += _ => cancelled = true;
                service.Completed += status => completed.Add(status.Item.CloudName + " " + status.Phase);

                service.Run(CancellationToken.None);

                Assert.NotNull(paused);
                Assert.Equal("A.zip", paused.Item.CloudName);
                Assert.Equal(100, paused.BytesDone);   // parado onde estava
                Assert.Equal(400, paused.BytesTotal);
                Assert.Equal(1, paused.Waiting);
                Assert.Equal(new[] { "A.zip", "A.zip", "B.zip" }, calls);
                Assert.False(cancelled);
                Assert.Equal(new[] { "A.zip Finished", "B.zip Finished" }, completed);
                Assert.True(queue.IsEmpty);
                Assert.False(queue.IsPaused);
            }
        }

        [Fact]
        public void A_paused_queue_waits_across_sessions_without_downloading()
        {
            using (var dir = new TempDir())
            using (var stop = new CancellationTokenSource())
            {
                var install = new RetroBatInstall(dir.Path);
                var queue = new DownloadQueue(install);
                queue.Add(Item("A"));
                Assert.True(queue.SetPaused(true));
                Assert.False(queue.SetPaused(true));

                // Outra abertura do ES: a fila continua pausada.
                Assert.True(new DownloadQueue(install).IsPaused);

                bool installed = false;
                var phases = new List<DownloadPhase>();
                var service = new DownloadService(queue, (item, progress, token) =>
                {
                    installed = true;
                    return new OnDemandResult(OnDemandOutcome.Installed);
                }, () => true, TimeSpan.FromMilliseconds(20));
                service.Changed += status => phases.Add(status.Phase);

                var worker = new Thread(() => service.Run(stop.Token));
                worker.Start();
                Thread.Sleep(300);
                stop.Cancel();
                Assert.True(worker.Join(TimeSpan.FromSeconds(5)));

                Assert.False(installed);
                Assert.Equal(new[] { DownloadPhase.Paused }, phases);
                Assert.Equal("A.zip", queue.Items().Single().CloudName);
                Assert.True(queue.IsPaused);
            }
        }

        [Fact]
        public void Cancelling_a_paused_game_moves_on_to_the_next_one_still_paused()
        {
            using (var dir = new TempDir())
            using (var stop = new CancellationTokenSource())
            {
                var install = new RetroBatInstall(dir.Path);
                var queue = new DownloadQueue(install);
                queue.Add(Item("A"));
                queue.Add(Item("B"));
                queue.SetPaused(true);

                var shown = new List<string>();
                var service = new DownloadService(queue, (item, progress, token) => new OnDemandResult(OnDemandOutcome.Installed),
                    () => true, TimeSpan.FromMilliseconds(20));
                service.Changed += status =>
                {
                    shown.Add(status.Item.CloudName + " " + status.Phase);
                    if (status.Item.CloudName == "A.zip")
                        new DownloadQueue(install).Remove(status.Item.Key); // CANCELAR DOWNLOAD
                    else
                        stop.Cancel();
                };

                DownloadItem cancelled = null;
                service.Cancelled += item => cancelled = item;

                service.Run(stop.Token);

                Assert.Equal(new[] { "A.zip Paused", "B.zip Paused" }, shown);
                Assert.Equal("A.zip", cancelled.CloudName);
                Assert.Equal("B.zip", queue.Items().Single().CloudName);
                Assert.True(queue.IsPaused);
            }
        }

        [Fact]
        public void An_empty_queue_is_never_paused()
        {
            using (var dir = new TempDir())
            {
                var queue = new DownloadQueue(new RetroBatInstall(dir.Path));
                Assert.False(queue.SetPaused(true));

                queue.Add(Item("A"));
                queue.SetPaused(true);
                queue.Add(Item("B"));            // entrar na fila pausada não retoma
                Assert.True(queue.IsPaused);

                queue.Remove(Item("A").Key);
                queue.Remove(Item("B").Key);
                queue.Add(Item("C"));            // a fila esvaziou: a próxima começa baixando

                Assert.False(queue.IsPaused);
                Assert.Equal("paused", DownloadStatusJson.PhaseName(DownloadPhase.Paused));
            }
        }
    }
}
