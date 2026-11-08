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

        /// <summary>O ES está aberto e sem jogo rodando, scraper, instalação de conteúdo ou atualização em andamento.</summary>
        public static bool IsIdle()
        {
            try
            {
                using (var client = CreateClient())
                    return client.DownloadString(BaseUrl + "/isIdle").Contains("true");
            }
            catch (WebException)
            {
                return false;
            }
        }

        /// <summary>
        /// Relê sistemas, gamelists e tema. O ES só avalia o if= do tema ao carregá-lo, e guarda o resultado de
        /// exists(): esta é a única forma de o ícone de nuvem mudar com o ES aberto. Fecha um menu que esteja
        /// aberto; só chame com <see cref="IsIdle"/>, senão o ES pergunta se deve parar o scraper.
        /// </summary>
        public static bool ReloadGames()
        {
            try
            {
                using (var client = CreateClient())
                    client.DownloadString(BaseUrl + "/reloadgames");

                return true;
            }
            catch (WebException)
            {
                return false;
            }
        }

        /// <summary>
        /// Faz o ES mostrar o estado novo dos jogos de um sistema sem recarregar nada e sem mexer na posição do
        /// usuário: limpa o cache de arquivos dele, que guarda o exists() do tema, e redesenha a lista. O nó é da
        /// própria pasta raiz do sistema: um nó de jogo apagaria dados que o ES guarda dele (datas do scraper).
        /// </summary>
        public static bool RefreshGameList(string systemName)
        {
            try
            {
                using (var client = CreateClient())
                {
                    client.Headers[HttpRequestHeader.ContentType] = "text/xml; charset=utf-8";
                    client.UploadString(BaseUrl + "/addgames/" + Uri.EscapeDataString(systemName), "POST", "<gameList><folder><path>.</path></folder></gameList>");
                }

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

        /// <summary>
        /// O ES do Retroboat (RB-010) responde 200 em GET /retroboat/download; o oficial, 404. Com o nosso, o
        /// andamento vai para o próprio ES, que desenha a região do cabeçalho e o status do jogo.
        /// </summary>
        public static bool SupportsNativeDownloads()
        {
            try
            {
                using (var client = CreateClient(2000))
                    client.DownloadString(BaseUrl + "/retroboat/download");

                return true;
            }
            catch (WebException)
            {
                return false;
            }
        }

        /// <summary>
        /// Manda o andamento ao ES do Retroboat (POST /retroboat/download). Prazo curto e sem exceção: um ES lento
        /// ou fechado nunca segura o download.
        /// </summary>
        public static bool PostDownloadStatus(string json)
        {
            try
            {
                using (var client = CreateClient(1500))
                {
                    client.Headers[HttpRequestHeader.ContentType] = "application/json; charset=utf-8";
                    client.UploadString(BaseUrl + "/retroboat/download", "POST", json);
                }

                return true;
            }
            catch (WebException)
            {
                return false;
            }
        }

        static WebClient CreateClient(int timeoutMs = 0) => new TimedWebClient(timeoutMs) { Encoding = Encoding.UTF8 };

        /// <summary>O WebClient espera até 100 s; as chamadas feitas pelo serviço de downloads não podem.</summary>
        sealed class TimedWebClient : WebClient
        {
            readonly int _timeoutMs;

            public TimedWebClient(int timeoutMs) => _timeoutMs = timeoutMs;

            protected override WebRequest GetWebRequest(Uri address)
            {
                var request = base.GetWebRequest(address);
                if (_timeoutMs > 0 && request != null)
                    request.Timeout = _timeoutMs;

                return request;
            }
        }
    }
}
