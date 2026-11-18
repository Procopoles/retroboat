using System;
using System.IO;
using System.Net;
using Retroboat.Media.Sources;

namespace Retroboat.Media.Background
{
    public enum BackgroundFetchOutcome
    {
        /// <summary>O arquivo está no PC, no lugar final.</summary>
        Ready,

        /// <summary>A fonte não tem mais o arquivo (404, ou sumiu da nuvem com o Drive aberto): a mídia sai da fila.</summary>
        Gone,

        /// <summary>Não deu agora (sem internet, fonte fora do ar, Drive fechado): tenta de novo mais tarde.</summary>
        Retry
    }

    /// <summary>
    /// Traz uma mídia da fila de segundo plano para o PC (RB-041): da internet, direto para o disco e continuando de onde
    /// parou; da nuvem, uma cópia do Drive. O arquivo entra pelo nome final só inteiro (".retroboat-part" até lá), então o
    /// ES nunca vê um vídeo pela metade.
    /// </summary>
    public sealed class BackgroundMediaFetcher
    {
        public const string PartSuffix = ".retroboat-part";

        readonly IMediaHttp _http;
        readonly Func<bool> _cloudAvailable;

        /// <param name="http">Quem baixa (os testes trocam).</param>
        /// <param name="cloudAvailable">O Google Drive está aberto: sem ele, um arquivo da nuvem que não aparece não sumiu.</param>
        public BackgroundMediaFetcher(IMediaHttp http = null, Func<bool> cloudAvailable = null)
        {
            _http = http ?? new MediaHttp();
            _cloudAvailable = cloudAvailable ?? (() => true);
        }

        public BackgroundFetchOutcome Fetch(BackgroundMediaItem item, out string error)
        {
            error = null;
            var part = item.Target + PartSuffix;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(item.Target));
                if (item.Url != null)
                {
                    // Baixado antes de o processo ser fechado, mas ainda fora do gamelist: só falta apontar.
                    if (File.Exists(item.Target) && new FileInfo(item.Target).Length > 0 && !File.Exists(part))
                        return BackgroundFetchOutcome.Ready;

                    if (!_http.DownloadTo(item.Url, part))
                    {
                        DeleteQuietly(part);
                        return BackgroundFetchOutcome.Gone;
                    }
                }
                else
                {
                    if (!File.Exists(item.CloudFile))
                    {
                        if (_cloudAvailable())
                            return BackgroundFetchOutcome.Gone;

                        error = "Google Drive fechado";
                        return BackgroundFetchOutcome.Retry;
                    }

                    // Outro caminho já trouxe este arquivo (a mesma versão): nada a copiar.
                    if (File.Exists(item.Target) && !IsDifferentAndNewer(item.CloudFile, item.Target))
                        return BackgroundFetchOutcome.Ready;

                    File.Copy(item.CloudFile, part, true);
                }

                if (new FileInfo(part).Length == 0)
                {
                    DeleteQuietly(part);
                    return BackgroundFetchOutcome.Gone;
                }

                if (File.Exists(item.Target))
                    File.Delete(item.Target);
                File.Move(part, item.Target);
                return BackgroundFetchOutcome.Ready;
            }
            catch (Exception ex) when (ex is WebException || ex is IOException || ex is UnauthorizedAccessException)
            {
                // O pedaço baixado fica: a próxima tentativa continua dele.
                error = ex.Message;
                return BackgroundFetchOutcome.Retry;
            }
        }

        /// <summary>Como no <see cref="CloudMediaSync"/>: o tamanho diz se é outro arquivo, a data diz qual vale (sem ler o conteúdo).</summary>
        static bool IsDifferentAndNewer(string file, string than)
        {
            var a = new FileInfo(file);
            var b = new FileInfo(than);
            return a.Length != b.Length && a.LastWriteTimeUtc > b.LastWriteTimeUtc;
        }

        static void DeleteQuietly(string path)
        {
            try { File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
