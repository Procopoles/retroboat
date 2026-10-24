using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Retroboat.Transfers
{
    public sealed class TransferProgress
    {
        public string Item { get; set; }
        public long BytesDone { get; set; }
        public long BytesTotal { get; set; }
        public double BytesPerSecond { get; set; }
        public TimeSpan? Remaining { get; set; }
    }

    /// <summary>
    /// Calcula velocidade (média móvel dos últimos segundos) e tempo restante, e limita os avisos
    /// de progresso a alguns por segundo.
    /// </summary>
    public sealed class RateMeter
    {
        const double WindowSeconds = 3;
        const long ReportIntervalMs = 250;

        readonly Stopwatch _clock = Stopwatch.StartNew();
        readonly Queue<KeyValuePair<double, long>> _samples = new Queue<KeyValuePair<double, long>>();
        readonly long _total;
        long _lastReportMs = -ReportIntervalMs;

        public RateMeter(long total)
        {
            _total = total;
        }

        public void Report(IProgress<TransferProgress> progress, string item, long done, bool force = false)
        {
            double now = _clock.Elapsed.TotalSeconds;
            _samples.Enqueue(new KeyValuePair<double, long>(now, done));
            while (_samples.Count > 2 && now - _samples.Peek().Key > WindowSeconds)
                _samples.Dequeue();

            if (progress == null)
                return;

            long nowMs = _clock.ElapsedMilliseconds;
            if (!force && nowMs - _lastReportMs < ReportIntervalMs)
                return;

            _lastReportMs = nowMs;

            var oldest = _samples.Peek();
            double elapsed = now - oldest.Key;
            double speed = elapsed > 0 ? (done - oldest.Value) / elapsed : 0;

            progress.Report(new TransferProgress
            {
                Item = item,
                BytesDone = done,
                BytesTotal = _total,
                BytesPerSecond = speed,
                Remaining = speed > 0 ? TimeSpan.FromSeconds((_total - done) / speed) : (TimeSpan?)null
            });
        }
    }
}
