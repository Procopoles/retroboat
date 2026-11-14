using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using Retroboat.Library;

namespace Retroboat.Integrations
{
    /// <summary>Um lugar de onde a busca automática tira chaves candidatas.</summary>
    public sealed class CredentialSource
    {
        public CredentialSource(string name, Func<Dictionary<string, List<string>>> load)
        {
            Name = name;
            _load = load;
        }

        readonly Func<Dictionary<string, List<string>>> _load;
        Dictionary<string, List<string>> _candidates;

        /// <summary>Para o log e a origem gravada no credenciais.ini, ex.: "repositório do RetroBat (ES 42.2026.1112.1244)".</summary>
        public string Name { get; }

        /// <summary>Os candidatos de cada credencial; lido uma vez por busca (o do repositório baixa o pacote do ES).</summary>
        public Dictionary<string, List<string>> Candidates => _candidates ?? (_candidates = _load());
    }

    /// <summary>Os lugares onde procurar chaves, do mais barato ao mais caro. Separado para os testes não irem à internet.</summary>
    public interface ICredentialRepository
    {
        IEnumerable<CredentialSource> Sources();
    }

    /// <summary>
    /// Onde o RetroBat publica as chaves: compiladas no ES dele. Primeiro os executáveis do ES que já estão neste PC (o nosso, com
    /// o que compilamos, e o oficial que o build guarda ao lado); depois o ES mais novo do repositório do RetroBat
    /// (RetroBat-Official/emulationstation, release continuous-master, o mesmo que o RetroBuild baixa). O pacote do repositório
    /// só é baixado de novo quando a versão muda: as chaves achadas em cada versão ficam no cache.
    /// </summary>
    public sealed class RetroBatCredentialRepository : ICredentialRepository
    {
        public const string ReleaseUrl = "https://github.com/RetroBat-Official/emulationstation/releases/download/continuous-master/";
        const string PackageName = "EmulationStation-Win64-no-deps.zip";

        readonly RetroBatInstall _install;
        readonly Func<string, byte[]> _download;

        /// <param name="download">GET de um endereço (os testes trocam); por padrão, a internet.</param>
        public RetroBatCredentialRepository(RetroBatInstall install, Func<string, byte[]> download = null)
        {
            _install = install;
            _download = download ?? Download;
        }

        public static string CacheDir(RetroBatInstall install) => Path.Combine(install.RetroboatDataPath, "cache", "credenciais");

        public IEnumerable<CredentialSource> Sources()
        {
            foreach (var exe in LocalExecutables())
            {
                var path = exe;
                yield return new CredentialSource(Path.GetFileName(path) + " deste PC", () => CredentialExtractor.FromFile(path));
            }

            string version = null;
            try
            {
                version = Encoding.ASCII.GetString(_download(ReleaseUrl + "version.info")).Trim();
            }
            catch (Exception ex) when (ex is WebException || ex is IOException)
            {
                // Sem internet: o repositório fica para a próxima vez.
            }

            if (string.IsNullOrEmpty(version) || version.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                yield break;

            yield return new CredentialSource("repositório do RetroBat (" + Describe(version) + ")", () => FromRelease(version));
        }

        /// <summary>"42.2026.1112.1244-202614121314" → "ES de 12/11/2026".</summary>
        public static string Describe(string version)
        {
            var match = System.Text.RegularExpressions.Regex.Match(version ?? "", @"^\d+\.(\d{4})\.(\d{2})(\d{2})\.");
            return match.Success ? "ES de " + match.Groups[3].Value + "/" + match.Groups[2].Value + "/" + match.Groups[1].Value : "ES " + version;
        }

        /// <summary>O nosso ES primeiro (o que compilamos), depois o oficial e qualquer outra cópia do ES na pasta.</summary>
        IEnumerable<string> LocalExecutables()
        {
            if (!Directory.Exists(_install.EmulationStationPath))
                return new string[0];

            return Directory.GetFiles(_install.EmulationStationPath, "emulationstation*.exe")
                .OrderBy(f => string.Equals(Path.GetFileName(f), "emulationstation.exe", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(f => f, StringComparer.OrdinalIgnoreCase);
        }

        Dictionary<string, List<string>> FromRelease(string version)
        {
            var cacheFile = Path.Combine(CacheDir(_install), "retrobat-" + version + ".txt");
            if (File.Exists(cacheFile))
                return ReadCache(cacheFile);

            var candidates = CredentialExtractor.FromZip(_download(ReleaseUrl + PackageName));
            Directory.CreateDirectory(Path.GetDirectoryName(cacheFile));
            File.WriteAllLines(cacheFile, candidates.SelectMany(c => c.Value.Select(v => c.Key + "\t" + v)), new UTF8Encoding(false));
            return candidates;
        }

        static Dictionary<string, List<string>> ReadCache(string file)
        {
            var result = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var line in File.ReadAllLines(file, Encoding.UTF8))
            {
                var tab = line.IndexOf('\t');
                if (tab <= 0)
                    continue;

                var name = line.Substring(0, tab);
                if (!result.TryGetValue(name, out var list))
                    result[name] = list = new List<string>();
                list.Add(line.Substring(tab + 1));
            }

            return result;
        }

        static byte[] Download(string url)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.UserAgent = HttpProbe.UserAgent;
            request.Timeout = 60000;
            request.ReadWriteTimeout = 60000;
            request.AllowAutoRedirect = true;
            using (var response = request.GetResponse())
            using (var stream = response.GetResponseStream())
            using (var memory = new MemoryStream())
            {
                stream.CopyTo(memory);
                return memory.ToArray();
            }
        }
    }
}
