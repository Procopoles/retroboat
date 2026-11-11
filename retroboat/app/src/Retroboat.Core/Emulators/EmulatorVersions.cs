using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Retroboat.Emulators
{
    /// <summary>
    /// Versões como o repositório do RetroBat as escreve no versions.xml: "2.7.0.1", "2609.1" ou, para quem não tem versão
    /// no executável, a data do programa ("0.26.08.16" = 16/08/2026).
    /// </summary>
    public static class EmulatorVersions
    {
        /// <summary>Nulo se não é uma versão (ex.: vazio, "nightly").</summary>
        public static Version Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            var parts = text.Trim().Split('.');
            if (parts.Length < 2 || parts.Length > 4)
                return parts.Length == 1 && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var single) ? new Version(single, 0) : null;

            var numbers = new int[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i]))
                    return null;
            }

            switch (numbers.Length)
            {
                case 2: return new Version(numbers[0], numbers[1]);
                case 3: return new Version(numbers[0], numbers[1], numbers[2]);
                default: return new Version(numbers[0], numbers[1], numbers[2], numbers[3]);
            }
        }

        /// <summary>
        /// A versão do programa instalado, como o emulatorLauncher a compara com o versions.xml: a FileVersion do executável;
        /// sem ela, a data dele no formato do repositório (0.aa.MM.dd). Nulo se o arquivo não existe.
        /// </summary>
        public static Version OfFile(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return null;

            try
            {
                var info = FileVersionInfo.GetVersionInfo(path);
                if (info.FileMajorPart != 0 || info.FileMinorPart != 0 || info.FileBuildPart != 0 || info.FilePrivatePart != 0)
                    return new Version(info.FileMajorPart, info.FileMinorPart, info.FileBuildPart, info.FilePrivatePart);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
            }

            return FromDate(File.GetLastWriteTime(path));
        }

        public static Version FromDate(DateTime date) => new Version(0, date.Year % 100, date.Month, date.Day);

        /// <summary>Texto para mostrar: "2.7.0.1", sem zeros finais além do segundo número ("2609.1", "2.8.2").</summary>
        public static string Display(Version version)
        {
            if (version == null)
                return "";

            var parts = new[] { version.Major, version.Minor, version.Build, version.Revision }.Where(p => p >= 0).ToList();
            while (parts.Count > 2 && parts[parts.Count - 1] == 0)
                parts.RemoveAt(parts.Count - 1);

            return string.Join(".", parts);
        }

        /// <summary>O repositório tem uma versão mais nova que a instalada. Sem uma das duas, não.</summary>
        public static bool IsNewer(Version server, Version installed) => server != null && installed != null && Normalize(server) > Normalize(installed);

        public static bool Same(Version a, Version b) => a != null && b != null && Normalize(a) == Normalize(b);

        /// <summary>"2.8" e "2.8.0.0" são a mesma versão.</summary>
        static Version Normalize(Version v) => new Version(v.Major, v.Minor, Math.Max(0, v.Build), Math.Max(0, v.Revision));
    }
}
