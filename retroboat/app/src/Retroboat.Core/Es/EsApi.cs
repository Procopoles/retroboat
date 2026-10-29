using System;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Threading;

namespace Retroboat.Es
{
    /// <summary>
    /// API HTTP local do EmulationStation (127.0.0.1:1234, sempre ligada). Usada para mostrar avisos no ES
    /// sem mudar o código dele: POST /notify exibe uma notificação na tela.
    /// </summary>
    public static class EsApi
    {
        const string BaseUrl = "http://127.0.0.1:1234";

        public static bool IsRunning()
        {
            try
            {
                using (var client = CreateClient())
                    client.DownloadString(BaseUrl + "/isIdle");

                return true;
            }
            catch (WebException)
            {
                return false;
            }
        }

        /// <summary>Espera o ES subir (a intro do RetroBat pode levar alguns segundos).</summary>
        public static bool WaitUntilRunning(TimeSpan timeout)
        {
            var clock = Stopwatch.StartNew();
            while (clock.Elapsed < timeout)
            {
                if (IsRunning())
                    return true;

                Thread.Sleep(1000);
            }

            return false;
        }

        public static bool Notify(string message)
        {
            try
            {
                using (var client = CreateClient())
                {
                    client.Headers[HttpRequestHeader.ContentType] = "text/plain; charset=utf-8";
                    client.UploadString(BaseUrl + "/notify", "POST", message);
                }

                return true;
            }
            catch (WebException)
            {
                return false;
            }
        }

        static WebClient CreateClient() => new WebClient { Encoding = Encoding.UTF8 };
    }
}
