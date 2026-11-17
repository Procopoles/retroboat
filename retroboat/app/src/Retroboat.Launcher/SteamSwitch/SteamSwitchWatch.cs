using System;
using System.Threading;
using System.Windows.Forms;
using Retroboat.Logging;
using Retroboat.SteamSwitch;

namespace Retroboat.Launcher.SteamSwitch
{
    /// <summary>
    /// RB-040: o botão Xbox troca entre o Retroboat e a Steam (Big Picture). Roda numa thread própria do Retroboat.exe
    /// enquanto o ES está aberto, lendo o controle 20 vezes por segundo (nada mais acontece até o botão ser apertado).
    /// Nasce de dois jeitos, e só um acompanha de cada vez: pelo script do evento "start" do ES
    /// (<c>Retroboat.exe --steam-switch</c>, que vale também com o RetroBat aberto direto, sem o Retroboat.exe) e pelo
    /// lançador, depois que o ES sobe.
    ///
    /// No ES: um aperto pergunta, outro confirma; o Big Picture abre e o ES é minimizado, e minimizado ele dorme (sem
    /// desenhar, sem som, sem texturas). No Big Picture: o mesmo, de volta; o ES volta para a frente e o Big Picture fecha.
    /// Num jogo aberto pela Steam: o mesmo leva de volta ao Big Picture, com o jogo aberto atrás (a Steam faria isso com a
    /// opção "botão Guia foca a Steam", que precisa ficar desligada para a troca funcionar).
    /// Com um jogo do Retroboat, ou qualquer outra janela na frente, o botão não faz nada.
    /// Sem a Steam instalada, também não. Com outro programa usando o botão, mostra como desligar e não troca.
    /// </summary>
    static class SteamSwitchWatch
    {
        const string SingleInstance = @"Local\RetroboatSteamSwitch";
        const int PollInterval = 50;

        /// <summary>O aviso de conflito fica mais tempo: tem um caminho para ler.</summary>
        static readonly TimeSpan ConflictNotice = TimeSpan.FromSeconds(7);

        /// <summary>
        /// O ES recarregado pelo menu fecha e abre de novo, e o evento "start" chama outro Retroboat.exe. Ele espera
        /// isso pelo que já acompanha: se o antigo viu o ES sumir e saiu, o novo assume; se não, o antigo continua.
        /// </summary>
        static readonly TimeSpan TakeOverWithin = TimeSpan.FromSeconds(15);

        const int EsCheckInterval = 2000;
        const int ChecksWithoutEs = 3;
        static readonly TimeSpan EsToAppear = TimeSpan.FromSeconds(60);

        static int _started;

        /// <summary>No lançador, depois que o ES sobe; o processo segue com o resto (nuvem, configurações).</summary>
        public static void StartInBackground() => Start(TimeSpan.Zero);

        /// <summary>
        /// <c>Retroboat.exe --steam-switch</c>, pelo evento "start" do ES: o processo só acompanha o botão e fecha junto
        /// com o ES (três verificações seguidas sem ele, como o acompanhamento do Google Drive).
        /// </summary>
        public static int RunWhileEsIsOpen()
        {
            var thread = Start(TakeOverWithin);
            if (thread == null)
                return 0;

            // Espera o processo do ES terminar, sem gastar nada; listar os processos do Windows a cada 2 s custava
            // 1 a 5% de um núcleo (medido em 17/11/2026). Depois que ele fecha, olha mais algumas vezes: um ES
            // recarregado pelo menu volta em seguida.
            var since = DateTime.UtcNow;
            bool seen = false;
            int without = 0;
            while (thread.IsAlive)
            {
                using (var es = SwitchWindows.FindEsProcess())
                {
                    if (es != null)
                    {
                        seen = true;
                        without = 0;
                        es.WaitForExit();
                        continue;
                    }
                }

                if (seen ? ++without >= ChecksWithoutEs : DateTime.UtcNow - since > EsToAppear)
                    break;

                Thread.Sleep(EsCheckInterval);
            }

            // Thread parada: outro Retroboat.exe continuou acompanhando (ES recarregado), nada a dizer.
            if (thread.IsAlive)
                Log.Info("Botão Xbox: ES fechado, fim do acompanhamento.");
            return 0;
        }

        static Thread Start(TimeSpan waitForOther)
        {
            if (Interlocked.Exchange(ref _started, 1) != 0)
                return null;

            var thread = new Thread(() => Run(waitForOther)) { IsBackground = true, Name = "Retroboat botão Xbox" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            return thread;
        }

        static void Run(TimeSpan waitForOther)
        {
            try
            {
                using (var mutex = new Mutex(false, SingleInstance))
                {
                    try
                    {
                        if (!mutex.WaitOne(waitForOther))
                            return; // Outro Retroboat.exe já acompanha o botão.
                    }
                    catch (AbandonedMutexException)
                    {
                        // O anterior foi encerrado à força; a vez é deste.
                    }

                    try
                    {
                        using (var loop = new Loop())
                            Application.Run(loop);
                    }
                    finally
                    {
                        mutex.ReleaseMutex();
                    }
                }
            }
            catch (Exception ex)
            {
                // A troca é um extra: se quebrar, o resto do Retroboat continua.
                Log.Error("Botão Xbox: a troca com a Steam parou", ex);
            }
        }

        sealed class Loop : ApplicationContext
        {
            readonly System.Windows.Forms.Timer _timer = new System.Windows.Forms.Timer { Interval = PollInterval };
            readonly GuideButton _button = new GuideButton();
            readonly SwitchFlow _flow = new SwitchFlow();
            readonly SwitchPrompt _prompt = new SwitchPrompt();
            static readonly TimeSpan BigPictureCheckEvery = TimeSpan.FromSeconds(1);

            IntPtr _es;
            IntPtr _bigPicture;
            DateTime _nextBigPictureCheck;
            bool _noSteamLogged;

            public Loop()
            {
                Log.Info("Botão Xbox: acompanhando para trocar entre o Retroboat e a Steam.");
                _timer.Tick += (s, e) => Tick();
                _timer.Start();
            }

            void Tick()
            {
                try
                {
                    Step(DateTime.UtcNow);
                }
                catch (Exception ex)
                {
                    Log.Warn("Botão Xbox: " + ex.Message);
                    _prompt.Hide();
                }

                if (_button.Unavailable)
                {
                    Log.Warn("Botão Xbox: XInput sem a leitura do botão neste Windows; a troca com a Steam fica desligada.");
                    _timer.Stop();
                    ExitThread();
                }
            }

            void Step(DateTime now)
            {
                bool pressed = _button.Pressed(now);
                if (!pressed && !_flow.IsBusy)
                {
                    WatchBigPictureClosed(now);
                    return;
                }

                IntPtr front = IntPtr.Zero;
                SteamClient steam = null;
                var step = _flow.Tick(now, pressed, () => SwitchWindows.Front(out front),
                    () => (steam = FindSteam()) != null, () => GuideButtonConflicts.Find(steam));

                switch (step)
                {
                    case SwitchStep.AskSteam:
                        _es = front;
                        _prompt.Show(SwitchPromptText.ToSteam());
                        break;

                    case SwitchStep.AskEs:
                        _prompt.Show(SwitchPromptText.ToEs());
                        break;

                    case SwitchStep.AskBigPicture:
                        _prompt.Show(SwitchPromptText.ToBigPicture());
                        break;

                    case SwitchStep.BigPictureOverGame:
                        // O jogo da Steam fica aberto atrás; de lá o usuário retoma ou fecha o jogo, como na Steam.
                        _prompt.Hide();
                        _bigPicture = front;
                        Log.Info("Botão Xbox: Big Picture na frente; o jogo da Steam (app " + SteamClient.RunningAppId + ") continua aberto.");
                        break;

                    case SwitchStep.HideAsk:
                        _prompt.Hide();
                        break;

                    case SwitchStep.WarnConflict:
                        Log.Info("Botão Xbox: " + _flow.Conflict.Owner + " também usa o botão; troca não feita, aviso mostrado.");
                        _prompt.Show(SwitchPromptText.Conflict(_flow.Conflict), ConflictNotice);
                        break;

                    case SwitchStep.OpenSteam:
                        _prompt.Hide();
                        Log.Info("Botão Xbox: abrindo o Big Picture" + (SteamClient.IsRunning ? "." : " (Steam fechada: abre antes)."));
                        SteamClient.OpenBigPicture();
                        break;

                    case SwitchStep.ShowOpening:
                        _prompt.Show(SwitchPromptText.Opening());
                        break;

                    case SwitchStep.SteamOpened:
                        _prompt.Hide();
                        if (_es == IntPtr.Zero)
                            _es = SwitchWindows.FindEs();
                        SwitchWindows.MinimizeQuietly(_es);
                        _bigPicture = front;
                        Log.Info("Botão Xbox: Big Picture na frente; ES minimizado (dorme até voltar).");
                        break;

                    case SwitchStep.SteamFailed:
                        _prompt.Hide();
                        Log.Warn("Botão Xbox: o Big Picture não apareceu em " + SwitchFlow.SteamTimeout.TotalSeconds + " s; o ES continua.");
                        break;

                    case SwitchStep.ReturnToEs:
                        _prompt.Hide();
                        _bigPicture = IntPtr.Zero;
                        _es = SwitchWindows.FindEs();
                        // Primeiro o ES na frente, depois fechar o Big Picture: fechando antes, o Windows daria o foco a
                        // qualquer outra janela.
                        SwitchWindows.BringBack(_es);
                        SteamClient.CloseBigPicture();
                        break;

                    case SwitchStep.FocusEsAgain:
                        // Saindo do Big Picture, a Steam abre a janela normal dela por cima do ES. Primeiro o ES volta
                        // para a frente; depois a janela da Steam vai para a barra de tarefas (minimizada, sem fechar
                        // nada da Steam). Na ordem contrária, minimizar sem ativar deixaria a da Steam ativa.
                        SwitchWindows.BringBack(_es);
                        if (SwitchWindows.IsSteamWindow(front))
                        {
                            SwitchWindows.MinimizeQuietly(front);
                            Log.Info("Botão Xbox: janela da Steam aberta por cima do ES minimizada.");
                        }
                        break;

                    case SwitchStep.EsReturned:
                        Log.Info("Botão Xbox: de volta ao Retroboat (ES na frente há " + SwitchFlow.EsSettle.TotalSeconds + " s).");
                        break;

                    case SwitchStep.EsReturnFailed:
                        Log.Warn("Botão Xbox: o Windows não deixou o ES voltar para a frente.");
                        break;
                }
            }

            /// <summary>
            /// Saiu do Big Picture pelo menu da própria Steam, sem o botão: o ES ficaria minimizado e dormindo atrás de
            /// tudo. A janela do Big Picture é destruída ao fechar; aí o ES volta sozinho. Olha uma vez por segundo.
            /// </summary>
            void WatchBigPictureClosed(DateTime now)
            {
                if (_bigPicture == IntPtr.Zero || now < _nextBigPictureCheck)
                    return;

                _nextBigPictureCheck = now + BigPictureCheckEvery;
                if (!SwitchWindows.IsGone(_bigPicture))
                    return;

                _bigPicture = IntPtr.Zero;
                var es = SwitchWindows.FindEs();
                if (!SwitchWindows.IsMinimized(es))
                    return; // O usuário já trouxe o ES de volta (ou ele fechou).

                _es = es;
                SwitchWindows.BringBack(es);
                _flow.ReturnAfterSteamClosed(now);
                Log.Info("Botão Xbox: Big Picture fechado pela Steam; trazendo o ES de volta.");
            }

            /// <summary>Lida a cada aperto que perguntaria (com os conflitos): instalar a Steam ou desligar a Game Bar vale na hora.</summary>
            SteamClient FindSteam()
            {
                var steam = SteamClient.Find();
                if (steam == null && !_noSteamLogged)
                    Log.Info("Botão Xbox: Steam não instalada neste PC; o botão não troca nada.");

                _noSteamLogged = steam == null;
                return steam;
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    _timer.Dispose();
                    _prompt.Dispose();
                }

                base.Dispose(disposing);
            }
        }
    }
}
