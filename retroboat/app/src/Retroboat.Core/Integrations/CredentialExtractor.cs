using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Retroboat.Integrations
{
    /// <summary>
    /// Acha chaves de API dentro de um executável do ES. O build oficial compila as chaves como texto (os #define do
    /// CMakeLists), então elas aparecem entre as strings do .exe. Cada credencial tem o seu formato; o que bate com ele é só
    /// candidato: quem decide é a verificação ao vivo (<see cref="IntegrationChecks"/>), que descarta o que a API recusa
    /// (ex.: um hash qualquer com cara de chave do TheGamesDB).
    /// </summary>
    public static class CredentialExtractor
    {
        /// <summary>Até quantos candidatos de uma credencial testar por executável (cada um custa uma consulta à API).</summary>
        const int MaxCandidates = 8;

        static readonly Dictionary<string, Regex> Formats = new Dictionary<string, Regex>(StringComparer.Ordinal)
        {
            [IntegrationCatalog.ScreenScraperLogin] = new Regex(@"^devid=[^&\s=]+&devpassword=[^&\s=]+$"),
            [IntegrationCatalog.ScreenScraperSoftName] = new Regex(@"^[A-Za-z0-9][A-Za-z0-9 ._]*-Emulationstation$", RegexOptions.IgnoreCase),
            // As chaves do TheGamesDB têm 64 hexadecimais; as antigas, 32.
            [IntegrationCatalog.GamesDbKey] = new Regex(@"^(?:[0-9a-f]{64}|[0-9a-f]{32})$"),
            [IntegrationCatalog.CheevosLogin] = new Regex(@"^z=[^&\s=]+&y=[A-Za-z0-9]{16,}$"),
            // usuário:senha, com a senha com cara de senha (maiúscula, minúscula e número): fora "gridtile:selected" dos temas.
            [IntegrationCatalog.HfsLogin] = new Regex(@"^[A-Za-z][A-Za-z0-9_-]{2,}:(?=[A-Za-z0-9]*\d)(?=[A-Za-z0-9]*[A-Z])(?=[A-Za-z0-9]*[a-z])[A-Za-z0-9]{8,}$")
        };

        /// <summary>As credenciais que este extrator sabe achar.</summary>
        public static IEnumerable<string> KnownCredentials => Formats.Keys;

        /// <summary>
        /// Os candidatos de cada credencial num executável (ou num .zip do ES com o executável dentro), do mais provável ao
        /// menos provável.
        /// </summary>
        public static Dictionary<string, List<string>> FromFile(string path)
        {
            var data = File.ReadAllBytes(path);
            return path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? FromZip(data) : FromBinary(data);
        }

        /// <summary>O emulationstation.exe de dentro do pacote do ES (o zip do release).</summary>
        public static Dictionary<string, List<string>> FromZip(byte[] zip)
        {
            using (var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read))
            {
                var exe = archive.Entries.FirstOrDefault(e => string.Equals(e.Name, "emulationstation.exe", StringComparison.OrdinalIgnoreCase));
                if (exe == null)
                    throw new InvalidDataException("o pacote do ES não tem o emulationstation.exe");

                using (var stream = exe.Open())
                using (var memory = new MemoryStream())
                {
                    stream.CopyTo(memory);
                    return FromBinary(memory.ToArray());
                }
            }
        }

        public static Dictionary<string, List<string>> FromBinary(byte[] data) => FromStrings(Strings(data));

        public static Dictionary<string, List<string>> FromStrings(IEnumerable<string> strings)
        {
            var all = strings.ToList();
            var result = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var format in Formats)
            {
                var found = all.Where(s => format.Value.IsMatch(s) && !IsFiller(s))
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(s => Rank(format.Key, s))
                    .Take(MaxCandidates)
                    .ToList();
                if (found.Count > 0)
                    result[format.Key] = found;
            }

            return result;
        }

        /// <summary>As strings ASCII de um binário, como o "strings" do Unix (6 caracteres ou mais).</summary>
        public static IEnumerable<string> Strings(byte[] data, int minLength = 6)
        {
            var current = new StringBuilder();
            foreach (var b in data)
            {
                if (b >= 32 && b < 127)
                {
                    current.Append((char)b);
                    continue;
                }

                if (current.Length >= minLength)
                    yield return current.ToString();

                current.Clear();
            }

            if (current.Length >= minLength)
                yield return current.ToString();
        }

        /// <summary>"0000...", "ffff...": preenchimento, não chave.</summary>
        static bool IsFiller(string value) => value.Distinct().Count() <= 2;

        /// <summary>O formato mais provável primeiro: a chave nova do TheGamesDB (64) antes da antiga (32); contas do RetroBat antes.</summary>
        static int Rank(string credential, string value)
        {
            if (credential == IntegrationCatalog.GamesDbKey)
                return value.Length == 64 ? 0 : 1;

            return value.IndexOf("retrobat", StringComparison.OrdinalIgnoreCase) >= 0 ? 0 : 1;
        }
    }
}
