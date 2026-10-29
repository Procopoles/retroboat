using System;
using Retroboat.Text;
using Retroboat.Transfers;

namespace Retroboat.Cli
{
    /// <summary>Barra de progresso numa linha só, reescrita no lugar.</summary>
    sealed class ConsoleProgress : IProgress<TransferProgress>
    {
        int _lastLength;

        public void Report(TransferProgress p)
        {
            var line = string.Format(Format.Culture, "  {0,6}  {1} de {2}{3}{4}",
                Format.Percent(p.BytesDone, p.BytesTotal),
                Format.Bytes(p.BytesDone),
                Format.Bytes(p.BytesTotal),
                p.BytesPerSecond > 0 ? "  " + Format.Speed(p.BytesPerSecond) : "",
                p.Remaining.HasValue ? "  restam " + Format.Duration(p.Remaining.Value) : "");

            Console.Write("\r" + line.PadRight(_lastLength));
            _lastLength = line.Length;
        }

        public void End()
        {
            if (_lastLength > 0)
                Console.WriteLine();

            _lastLength = 0;
        }
    }
}
