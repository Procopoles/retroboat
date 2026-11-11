using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Threading;
using System.Xml;
using System.Xml.Linq;
using Retroboat.Transfers;

namespace Retroboat.Emulators
{
    /// <summary>Um arquivo no servidor, pelo cabeçalho (HEAD).</summary>
    public sealed class RemoteFile
    {
        public bool Exists { get; set; }
        public long Length { get; set; }
        public DateTime? ModifiedUtc { get; set; }
    }

    /// <summary>Não deu para falar com o repositório (sem internet, servidor fora), ou a resposta não é a esperada.</summary>
    public sealed class RepositoryException : Exception
    {
        public RepositoryException(string message, bool formatChanged = false, Exception inner = null) : base(message, inner)
        {
            FormatChanged = formatChanged;
        }

        /// <summary>O servidor respondeu, mas num formato que o Retroboat não conhece: o repositório mudou (REPOSITORIO_MUDOU).</summary>
        public bool FormatChanged { get; }
    }

    /// <summary>De onde vêm os pacotes. O de verdade é o <see cref="RetroBatRepository"/>; os testes usam arquivos locais.</summary>
    public interface IPackageSource
    {
        string Describe(EmulatorPackage package);

        /// <summary>Nome do pacote → versão, do versions.xml.</summary>
        IReadOnlyDictionary<string, string> ReadVersions(int timeoutMs);

        RemoteFile Probe(EmulatorPackage package, int timeoutMs);

        /// <summary>Baixa para <paramref name="partPath"/>, continuando o que já estiver lá.</summary>
        void Download(EmulatorPackage package, string partPath, long expectedLength, IProgress<TransferProgress> progress, CancellationToken token);
    }

    /// <summary>
    /// O repositório do RetroBat, o mesmo que o emulatorLauncher usa no primeiro jogo. Os endereços foram achados consultando o
    /// servidor (eles não aparecem no emulatorLauncher). Se o RetroBat mudar de servidor, só esta classe muda.
    /// </summary>
    public sealed class RetroBatRepository : IPackageSource
    {
        public const string DefaultBaseUrl = "https://www.retrobat.ovh/repo/win64/stable/emulators/";

        // O mesmo que o RetroBuild do RetroBat manda.
        const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64)";
        const int ReadTimeoutMs = 30000;

        public RetroBatRepository(string baseUrl = null)
        {
            BaseUrl = (baseUrl ?? DefaultBaseUrl).TrimEnd('/') + "/";
        }

        public string BaseUrl { get; }

        public string VersionsUrl => BaseUrl + "versions.xml";

        public string UrlOf(EmulatorPackage package) => BaseUrl + (package.Kind == PackageKind.Core ? "cores/" : "") + Uri.EscapeDataString(package.ArchiveName);

        public string Describe(EmulatorPackage package) => UrlOf(package);

        public IReadOnlyDictionary<string, string> ReadVersions(int timeoutMs)
        {
            string xml;
            try
            {
                var request = Create(VersionsUrl, "GET", timeoutMs);
                using (var response = (HttpWebResponse)request.GetResponse())
                using (var reader = new StreamReader(response.GetResponseStream()))
                    xml = reader.ReadToEnd();
            }
            catch (WebException ex) when (ex.Response is HttpWebResponse http && http.StatusCode == HttpStatusCode.NotFound)
            {
                throw new RepositoryException("o versions.xml não existe mais em " + VersionsUrl, formatChanged: true, inner: ex);
            }
            catch (WebException ex)
            {
                throw new RepositoryException("sem conexão com o repositório do RetroBat (" + ex.Message + ")", inner: ex);
            }
            catch (IOException ex)
            {
                throw new RepositoryException("a leitura do repositório do RetroBat foi interrompida (" + ex.Message + ")", inner: ex);
            }

            return ParseVersions(xml);
        }

        /// <summary>&lt;systems&gt;&lt;system name="flycast" version="2.7.0.1"/&gt;...&lt;/systems&gt;</summary>
        public static IReadOnlyDictionary<string, string> ParseVersions(string xml)
        {
            XElement root;
            try
            {
                root = XDocument.Parse(xml).Root;
            }
            catch (XmlException ex)
            {
                throw new RepositoryException("o versions.xml do repositório não é mais um XML válido (" + ex.Message + ")", formatChanged: true, inner: ex);
            }

            var versions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var system in root.Elements("system"))
            {
                var name = ((string)system.Attribute("name"))?.Trim();
                var version = ((string)system.Attribute("version"))?.Trim();
                if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(version))
                    versions[name] = version;
            }

            if (root.Name != "systems" || versions.Count == 0)
                throw new RepositoryException("o versions.xml do repositório mudou de formato (raiz <" + root.Name + ">, " + versions.Count + " emuladores com name e version)", formatChanged: true);

            return versions;
        }

        public RemoteFile Probe(EmulatorPackage package, int timeoutMs)
        {
            try
            {
                var request = Create(UrlOf(package), "HEAD", timeoutMs);
                using (var response = (HttpWebResponse)request.GetResponse())
                {
                    return new RemoteFile
                    {
                        Exists = true,
                        Length = response.ContentLength,
                        ModifiedUtc = response.Headers[HttpResponseHeader.LastModified] != null ? response.LastModified.ToUniversalTime() : (DateTime?)null
                    };
                }
            }
            catch (WebException ex) when (ex.Response is HttpWebResponse http && http.StatusCode == HttpStatusCode.NotFound)
            {
                return new RemoteFile { Exists = false };
            }
            catch (WebException ex)
            {
                throw new RepositoryException("sem conexão com o repositório do RetroBat (" + ex.Message + ")", inner: ex);
            }
        }

        public void Download(EmulatorPackage package, string partPath, long expectedLength, IProgress<TransferProgress> progress, CancellationToken token)
        {
            var url = UrlOf(package);
            long have = File.Exists(partPath) ? new FileInfo(partPath).Length : 0;
            if (expectedLength > 0 && have > expectedLength)
            {
                File.Delete(partPath);
                have = 0;
            }

            if (expectedLength > 0 && have == expectedLength)
                return;

            try
            {
                var request = Create(url, "GET", ReadTimeoutMs);
                if (have > 0)
                    request.AddRange(have);

                using (var response = (HttpWebResponse)request.GetResponse())
                {
                    // Sem suporte a continuar (200 em vez de 206): começa do zero.
                    bool resumed = have > 0 && response.StatusCode == HttpStatusCode.PartialContent;
                    if (!resumed)
                        have = 0;

                    long total = expectedLength > 0 ? expectedLength : (response.ContentLength > 0 ? response.ContentLength + have : 0);
                    var meter = new RateMeter(total);
                    using (var input = response.GetResponseStream())
                    using (var output = new FileStream(partPath, resumed ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        input.ReadTimeout = ReadTimeoutMs;
                        var buffer = new byte[81920];
                        long done = have;
                        int read;
                        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            token.ThrowIfCancellationRequested();
                            output.Write(buffer, 0, read);
                            done += read;
                            meter.Report(progress, package.DisplayName, done);
                        }

                        meter.Report(progress, package.DisplayName, done, force: true);
                    }
                }
            }
            catch (WebException ex) when (ex.Response is HttpWebResponse http && http.StatusCode == HttpStatusCode.NotFound)
            {
                throw new RepositoryException(package.ArchiveName + " não existe mais no repositório (" + url + ")", formatChanged: true, inner: ex);
            }
            catch (WebException ex) when (ex.Response is HttpWebResponse http && (int)http.StatusCode == 416)
            {
                // O pedaço guardado não serve mais (o pacote mudou no servidor): recomeça na próxima tentativa.
                File.Delete(partPath);
                throw new RepositoryException("o pacote mudou no servidor durante o download; tente de novo", inner: ex);
            }
            catch (WebException ex)
            {
                if (token.IsCancellationRequested)
                    throw new OperationCanceledException(token);

                throw new RepositoryException("o download parou (" + ex.Message + ")", inner: ex);
            }
            catch (IOException ex) when (!(ex is FileNotFoundException))
            {
                if (token.IsCancellationRequested)
                    throw new OperationCanceledException(token);

                throw new RepositoryException("o download parou (" + ex.Message + ")", inner: ex);
            }
        }

        static HttpWebRequest Create(string url, string method, int timeoutMs)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = method;
            request.UserAgent = UserAgent;
            request.Timeout = timeoutMs;
            request.ReadWriteTimeout = Math.Max(timeoutMs, ReadTimeoutMs);
            request.AutomaticDecompression = DecompressionMethods.None;
            return request;
        }

        internal static string Iso(DateTime utc) => utc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
    }
}
