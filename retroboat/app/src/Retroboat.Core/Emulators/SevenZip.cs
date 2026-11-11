using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using Retroboat.Library;

namespace Retroboat.Emulators
{
    /// <summary>
    /// O 7-Zip que vem com o RetroBat (system\tools\7za.exe): confere e extrai os pacotes .7z e .zip do repositório, como o
    /// próprio RetroBat faz.
    /// </summary>
    public sealed class SevenZip
    {
        public SevenZip(string exePath)
        {
            ExePath = exePath;
        }

        public string ExePath { get; }

        public bool Exists => File.Exists(ExePath);

        public static SevenZip For(RetroBatInstall install) => new SevenZip(Path.Combine(install.Root, "system", "tools", "7za.exe"));

        /// <summary>Confere o CRC de cada arquivo do pacote. Nulo se está íntegro; senão, o que o 7-Zip disse.</summary>
        public string Test(string archive, CancellationToken token) => Run("t \"" + archive + "\" -y", token);

        /// <summary>Extrai com as pastas. Nulo se deu certo; senão, o que o 7-Zip disse.</summary>
        public string Extract(string archive, string destination, CancellationToken token)
        {
            Directory.CreateDirectory(destination);
            return Run("x \"" + archive + "\" -o\"" + destination + "\" -y", token);
        }

        string Run(string arguments, CancellationToken token)
        {
            if (!Exists)
                return "o 7-Zip do RetroBat não foi encontrado em " + ExePath;

            var output = new StringBuilder();
            var info = new ProcessStartInfo(ExePath, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using (var process = new Process { StartInfo = info })
            {
                process.OutputDataReceived += (s, e) => { if (e.Data != null) lock (output) output.AppendLine(e.Data); };
                process.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (output) output.AppendLine(e.Data); };
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                while (!process.WaitForExit(200))
                {
                    if (token.IsCancellationRequested)
                    {
                        try { process.Kill(); } catch (InvalidOperationException) { }
                        process.WaitForExit(5000);
                        token.ThrowIfCancellationRequested();
                    }
                }

                process.WaitForExit();
                if (process.ExitCode == 0)
                    return null;

                lock (output)
                    return "7-Zip saiu com o código " + process.ExitCode + ": " + Summary(output.ToString());
            }
        }

        /// <summary>As linhas que dizem o problema (ERROR, CRC Failed...), não a lista inteira de arquivos.</summary>
        static string Summary(string output)
        {
            var lines = output.Replace("\r", "").Split('\n');
            var useful = new StringBuilder();
            foreach (var line in lines)
            {
                var text = line.Trim();
                if (text.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("crc", StringComparison.OrdinalIgnoreCase) >= 0
                    || text.IndexOf("can not", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("unexpected", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    if (useful.Length > 0)
                        useful.Append(" | ");
                    useful.Append(text);
                }
            }

            var result = useful.Length > 0 ? useful.ToString() : output.Trim();
            return result.Length > 400 ? result.Substring(0, 400) + "..." : result;
        }
    }
}
