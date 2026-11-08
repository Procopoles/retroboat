using System;
using System.Diagnostics;
using System.Threading;
using Retroboat.Cloud;
using Retroboat.Library;
using Retroboat.Logging;
using Retroboat.Settings;

namespace Retroboat.Launch
{
    /// <summary>Para que o Retroboat precisa do Google Drive agora: muda só a explicação da pergunta.</summary>
    enum CloudNeed
    {
        Startup,
        Download,
        Saves
    }

    enum CloudAppAnswer
    {
        /// <summary>O Drive está aberto.</summary>
        Available,

        /// <summary>O usuário respondeu "Agora não".</summary>
        Declined,

        /// <summary>Não está instalado, ou abriu e não conectou.</summary>
        Unavailable
    }

    /// <summary>
    /// Google Drive fechado: pergunta, em tela cheia e com controle, se o Retroboat deve abrir. "Sempre abrir"
    /// não pergunta mais. Se o usuário abrir o Drive por conta própria, a pergunta some sozinha. Usada pelo
    /// Retroboat.exe ao iniciar e pelo retroboat-launch ao abrir um jogo que precisa da nuvem.
    /// </summary>
    static class CloudAppPrompt
    {
        const string Title = "Google Drive";
        const int OpenOption = 0;
        const int AlwaysOption = 1;
        const int PollMilliseconds = 500;

        // O Drive costuma levar de 5 a 20 s para montar a unidade depois de aberto.
        static readonly TimeSpan OpenTimeout = TimeSpan.FromSeconds(60);

        // Já estava rodando: ou termina de subir logo, ou está sem login e esperar mais não adianta.
        static readonly TimeSpan ConnectingTimeout = TimeSpan.FromSeconds(20);

        // Quanto o Drive leva para puxar o foco depois de aberto (RB-019).
        static readonly TimeSpan DriveTakesFocus = TimeSpan.FromSeconds(15);

        public static CloudAppAnswer Ensure(RetroBatInstall install, RetroboatSettings settings, CloudNeed need)
        {
            switch (GoogleDriveApp.GetState(settings.CloudPath))
            {
                case CloudAppState.Open:
                    return CloudAppAnswer.Available;
                case CloudAppState.NotApplicable:
                    return CloudAppAnswer.Unavailable;
                case CloudAppState.Connecting:
                    Log.Info("Google Drive subindo: aguardando a unidade.");
                    return Finish(install, settings, WaitForDrive(settings, "Conectando ao Google Drive...", ConnectingTimeout));
            }

            if (!settings.CloudAppAutoOpen)
            {
                // Memory card não vale uma segunda pergunta; BAIXAR, sim: o jogo não abre sem o Drive.
                if (need == CloudNeed.Saves && CloudStatusFile.WasDeclined(install))
                    return CloudAppAnswer.Declined;

                var answer = Ask(install, settings, need);
                if (answer.HasValue)
                    return answer.Value;
            }

            if (!GoogleDriveApp.Start())
            {
                Log.Warn("Não foi possível abrir o Google Drive.");
                return ExplainFailure(install, settings, "O Windows não conseguiu abrir o Google Drive. O Retroboat continua com o que está neste PC.");
            }

            Log.Info("Google Drive aberto pelo Retroboat.");
            Theme.HoldFocus(DriveTakesFocus);
            if (WaitForDrive(settings, "Abrindo o Google Drive...", OpenTimeout))
                return Finish(install, settings, true);

            Log.Warn("O Google Drive foi aberto e não conectou em " + OpenTimeout.TotalSeconds + " s.");
            return ExplainFailure(install, settings, "Confira se você entrou na sua conta e se há internet. O Retroboat continua com o que está neste PC.");
        }

        /// <returns>Nulo se é para abrir o Drive; senão, a resposta final.</returns>
        static CloudAppAnswer? Ask(RetroBatInstall install, RetroboatSettings settings, CloudNeed need)
        {
            int choice;
            using (var watch = new DriveWatch(settings))
            {
                choice = ChoiceForm.Ask(Title, "O Google Drive está fechado", Explain(need),
                    new[] { "Abrir o Google Drive", "Sempre abrir, sem perguntar", "Agora não" },
                    autoClose: () => watch.IsOpen, autoCloseInterval: PollMilliseconds);
            }

            if (choice == ChoiceForm.AutoClosed)
            {
                Log.Info("Google Drive aberto pelo usuário durante a pergunta.");
                return Finish(install, settings, true);
            }

            if (choice == AlwaysOption)
            {
                settings.CloudAppAutoOpen = true;
                settings.Save(RetroboatSettings.GetPath(install));
                Log.Info("Google Drive: abrir sempre, sem perguntar ([Cloud] AutoOpen=1 no retroboat.ini).");
            }

            if (choice == OpenOption || choice == AlwaysOption)
                return null;

            Log.Info("Google Drive fechado: o usuário preferiu não abrir agora.");
            CloudStatusFile.RememberDeclined(install);
            return CloudAppAnswer.Declined;
        }

        static string Explain(CloudNeed need)
        {
            switch (need)
            {
                case CloudNeed.Download:
                    return "Este jogo está na nuvem. Para baixar, o Google Drive precisa estar aberto.";
                case CloudNeed.Saves:
                    return "Sem ele, o jogo abre com os memory cards deste PC, que vão para a nuvem depois.";
                default:
                    return "Com ele aberto, os jogos, as capas e os memory cards da nuvem ficam em dia.";
            }
        }

        /// <returns>true se o Drive apareceu; false se o tempo acabou ou o usuário preferiu seguir sem ele.</returns>
        static bool WaitForDrive(RetroboatSettings settings, string heading, TimeSpan timeout)
        {
            var clock = Stopwatch.StartNew();
            using (var watch = new DriveWatch(settings))
            {
                ChoiceForm.Ask(Title, heading, "Isso costuma levar alguns segundos.", new string[0],
                    autoClose: () => watch.IsOpen || clock.Elapsed >= timeout,
                    hint: "B ou Esc: continuar sem o Drive", autoCloseInterval: PollMilliseconds);

                return watch.IsOpen;
            }
        }

        static CloudAppAnswer ExplainFailure(RetroBatInstall install, RetroboatSettings settings, string text)
        {
            using (var watch = new DriveWatch(settings))
            {
                int choice = ChoiceForm.Ask(Title, "O Google Drive não conectou", text, new[] { "Continuar" },
                    autoClose: () => watch.IsOpen, autoCloseInterval: PollMilliseconds);

                return Finish(install, settings, choice == ChoiceForm.AutoClosed);
            }
        }

        static CloudAppAnswer Finish(RetroBatInstall install, RetroboatSettings settings, bool open)
        {
            if (!open)
                return CloudAppAnswer.Unavailable;

            // O ícone do tema acompanha, mesmo que a pasta "retroboat" ainda não exista no Drive.
            CloudStatusFile.Write(install, CloudLocator.IsAvailable(settings.CloudPath));
            return CloudAppAnswer.Available;
        }

        /// <summary>
        /// Procura o Drive fora da tela: olhar as unidades pode travar alguns segundos numa unidade de rede
        /// fora do ar, e a tela precisa continuar respondendo ao controle.
        /// </summary>
        sealed class DriveWatch : IDisposable
        {
            volatile bool _isOpen;
            volatile bool _stopped;

            public DriveWatch(RetroboatSettings settings)
            {
                var worker = new Thread(() =>
                {
                    while (!_stopped)
                    {
                        try
                        {
                            if (GoogleDriveApp.GetState(settings.CloudPath) == CloudAppState.Open)
                            {
                                _isOpen = true;
                                return;
                            }
                        }
                        catch (Exception ex) when (ex is System.IO.IOException || ex is UnauthorizedAccessException)
                        {
                            Log.Warn("Falha ao procurar o Google Drive: " + ex.Message);
                        }

                        Thread.Sleep(1000);
                    }
                }) { IsBackground = true };

                worker.Start();
            }

            public bool IsOpen => _isOpen;

            public void Dispose() => _stopped = true;
        }
    }
}
