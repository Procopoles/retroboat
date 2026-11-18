using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;

namespace Retroboat.Media.Sources
{
    /// <summary>A resposta inteira de uma API, mesmo de erro.</summary>
    public sealed class HttpAnswer
    {
        public HttpAnswer(int status, string body)
        {
            Status = status;
            Body = body ?? "";
        }

        public int Status { get; }

        public string Body { get; }

        public bool Ok => Status >= 200 && Status < 300;
    }

    /// <summary>HTTP das fontes do PERSONALIZADO. Separado para os testes não irem à internet.</summary>
    public interface IMediaHttp
    {
        /// <summary>Lança WebException só sem resposta (sem internet, DNS, tempo esgotado).</summary>
        HttpAnswer Get(string url, IDictionary<string, string> headers = null);

        /// <summary>POST de formulário.</summary>
        HttpAnswer Post(string url, string form, IDictionary<string, string> headers = null);

        /// <summary>O arquivo (imagem, vídeo); nulo se não existe (404). Lança WebException sem resposta.</summary>
        byte[] Download(string url);

        /// <summary>
        /// RB-041: o arquivo direto no disco, sem passar inteiro pela memória (um vídeo tem até 50 MB). Se
        /// <paramref name="file"/> já tem um pedaço de uma tentativa anterior, continua de onde parou quando o servidor
        /// aceita. False se não existe (404). Lança WebException sem resposta ou com outro erro.
        /// </summary>
        bool DownloadTo(string url, string file);
    }

    public sealed class MediaHttp : IMediaHttp
    {
        public const string UserAgent = "Retroboat/0.1 (https://github.com/Procopoles/retroboat)";

        public HttpAnswer Get(string url, IDictionary<string, string> headers = null) => Send(url, null, headers);

        public HttpAnswer Post(string url, string form, IDictionary<string, string> headers = null) => Send(url, form ?? "", headers);

        public byte[] Download(string url)
        {
            var request = Create(url, 120000);
            try
            {
                using (var response = request.GetResponse())
                using (var stream = response.GetResponseStream())
                using (var memory = new MemoryStream())
                {
                    stream.CopyTo(memory);
                    return memory.ToArray();
                }
            }
            catch (WebException ex) when ((ex.Response as HttpWebResponse)?.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
        }

        public bool DownloadTo(string url, string file)
        {
            long existing = File.Exists(file) ? new FileInfo(file).Length : 0;
            var request = Create(url, 120000);
            request.AutomaticDecompression = DecompressionMethods.None; // Pedaços de um arquivo compactado não se emendam.
            if (existing > 0)
                request.AddRange(existing);

            try
            {
                using (var response = (HttpWebResponse)request.GetResponse())
                using (var stream = response.GetResponseStream())
                {
                    // 206: o servidor manda só o que falta. 200: ele não sabe continuar, então começa do zero.
                    bool resume = existing > 0 && response.StatusCode == HttpStatusCode.PartialContent;
                    Directory.CreateDirectory(Path.GetDirectoryName(file));
                    using (var output = new FileStream(file, resume ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 81920))
                        stream.CopyTo(output, 81920);
                }

                return true;
            }
            catch (WebException ex) when ((ex.Response as HttpWebResponse)?.StatusCode == HttpStatusCode.NotFound)
            {
                return false;
            }
            catch (WebException ex) when (existing > 0 && (ex.Response as HttpWebResponse)?.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
            {
                // O pedaço não serve mais (o arquivo mudou na fonte): do zero.
                File.Delete(file);
                return DownloadTo(url, file);
            }
        }

        static HttpAnswer Send(string url, string form, IDictionary<string, string> headers)
        {
            var request = Create(url, 30000);
            if (headers != null)
            {
                foreach (var header in headers)
                    request.Headers[header.Key] = header.Value;
            }

            if (form != null)
            {
                request.Method = "POST";
                request.ContentType = "application/x-www-form-urlencoded";
                var body = Encoding.UTF8.GetBytes(form);
                using (var stream = request.GetRequestStream())
                    stream.Write(body, 0, body.Length);
            }

            try
            {
                using (var response = (HttpWebResponse)request.GetResponse())
                    return new HttpAnswer((int)response.StatusCode, Read(response));
            }
            catch (WebException ex) when (ex.Response is HttpWebResponse error)
            {
                using (error)
                    return new HttpAnswer((int)error.StatusCode, Read(error));
            }
        }

        static HttpWebRequest Create(string url, int timeout)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.UserAgent = UserAgent;
            request.Timeout = timeout;
            request.ReadWriteTimeout = timeout;
            request.AllowAutoRedirect = true;
            request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            return request;
        }

        static string Read(WebResponse response)
        {
            using (var stream = response.GetResponseStream())
            using (var reader = new StreamReader(stream, Encoding.UTF8))
                return reader.ReadToEnd();
        }
    }
}
