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
