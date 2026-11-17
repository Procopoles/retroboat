using System;

namespace Retroboat.SteamSwitch
{
    /// <summary>O que está na frente quando o botão Xbox é apertado.</summary>
    public enum SwitchScreen
    {
        /// <summary>Qualquer outra janela: um jogo (do Retroboat ou da Steam), a área de trabalho. O botão não faz nada.</summary>
        Other,
        /// <summary>O ES, nos menus (com um jogo aberto, o emulador está na frente).</summary>
        Es,
        /// <summary>A Steam no Big Picture.</summary>
        BigPicture
    }

    /// <summary>O que quem acompanha o controle deve fazer depois de cada volta.</summary>
    public enum SwitchStep
    {
        None,
        /// <summary>Mostrar "Ir para a Steam? Aperte o botão Xbox de novo".</summary>
        AskSteam,
        /// <summary>Mostrar "Voltar para o Retroboat? Aperte o botão Xbox de novo".</summary>
        AskEs,
        /// <summary>Esconder a pergunta: o tempo acabou ou a janela da frente mudou.</summary>
        HideAsk,
        /// <summary>Outro programa usa o botão (<see cref="SwitchFlow.Conflict"/>): mostrar como desligar.</summary>
        WarnConflict,
        /// <summary>Confirmado: abrir o Big Picture.</summary>
        OpenSteam,
        /// <summary>O Big Picture está demorando (Steam fechada): mostrar "Abrindo a Steam".</summary>
        ShowOpening,
        /// <summary>O Big Picture chegou na frente: minimizar o ES, que dorme.</summary>
        SteamOpened,
        /// <summary>O Big Picture não apareceu a tempo.</summary>
        SteamFailed,
        /// <summary>Confirmado: trazer o ES de volta e fechar o Big Picture.</summary>
        ReturnToEs,
        /// <summary>O ES não está na frente (a janela normal da Steam abriu por cima): minimizar a da Steam e pedir o foco de novo.</summary>
        FocusEsAgain,
        /// <summary>O ES voltou para a frente.</summary>
        EsReturned,
        /// <summary>O ES não voltou para a frente a tempo.</summary>
        EsReturnFailed
    }

    /// <summary>
    /// RB-040: a troca entre o Retroboat e o Big Picture pelo botão Xbox, sem janelas nem Windows: só a sequência. Um
    /// aperto pergunta; outro, em até <see cref="ConfirmWindow"/>, confirma. Assim um aperto sem querer não tira o
    /// usuário de onde está. Confirmar com o botão Xbox, e não com o A: o A também chegaria ao ES ou ao Big Picture,
    /// que estão na frente, e clicaria em alguma coisa lá; nenhum dos dois usa o botão Xbox.
    /// </summary>
    public sealed class SwitchFlow
    {
        public static readonly TimeSpan ConfirmWindow = TimeSpan.FromSeconds(3);

        /// <summary>Com a Steam fechada, ela abre antes do Big Picture; numa máquina lenta, isso leva bem mais que o normal.</summary>
        public static readonly TimeSpan SteamTimeout = TimeSpan.FromSeconds(60);

        /// <summary>O Big Picture com a Steam aberta chega em menos de 1 s; passou disso, o usuário vê que está abrindo.</summary>
        public static readonly TimeSpan OpeningNoticeAfter = TimeSpan.FromSeconds(1);

        /// <summary>
        /// Saindo do Big Picture, a Steam abre a janela normal dela uns 0,4 s depois e toma o foco do ES (visto em
        /// 17/11/2026). Por isso o ES só conta como de volta depois de ficar na frente esse tempo seguido.
        /// </summary>
        public static readonly TimeSpan EsSettle = TimeSpan.FromSeconds(1.5);

        public static readonly TimeSpan EsTimeout = TimeSpan.FromSeconds(6);
        public static readonly TimeSpan EsFocusRetry = TimeSpan.FromMilliseconds(300);

        enum Stage { Idle, AskingSteam, AskingEs, OpeningSteam, ReturningToEs }

        Stage _stage = Stage.Idle;
        DateTime _since;
        DateTime _lastFocus;
        DateTime? _esInFrontSince;
        bool _openingShown;

        /// <summary>O conflito do último <see cref="SwitchStep.WarnConflict"/>.</summary>
        public GuideButtonConflict Conflict { get; private set; }

        /// <summary>Esperando algo (pergunta na tela, Big Picture abrindo, ES voltando): quem acompanha olha a janela da frente a cada volta.</summary>
        public bool IsBusy => _stage != Stage.Idle;

        /// <param name="pressed">O botão Xbox acabou de ser apertado (não "está apertado").</param>
        /// <param name="screen">A janela da frente; só é chamada quando importa.</param>
        /// <param name="hasSteam">A Steam está instalada? Só é chamada num aperto que perguntaria; sem ela, o botão não faz nada.</param>
        /// <param name="conflict">Outro programa que usa o botão, ou nulo; só é chamada num aperto que perguntaria.</param>
        public SwitchStep Tick(DateTime now, bool pressed, Func<SwitchScreen> screen, Func<bool> hasSteam, Func<GuideButtonConflict> conflict)
        {
            switch (_stage)
            {
                case Stage.Idle:
                    return pressed ? Ask(now, screen(), hasSteam, conflict) : SwitchStep.None;

                case Stage.AskingSteam:
                case Stage.AskingEs:
                    return WhileAsking(now, pressed, screen());

                case Stage.OpeningSteam:
                    if (screen() == SwitchScreen.BigPicture)
                        return Finish(SwitchStep.SteamOpened);
                    if (now - _since > SteamTimeout)
                        return Finish(SwitchStep.SteamFailed);
                    if (!_openingShown && now - _since > OpeningNoticeAfter)
                    {
                        _openingShown = true;
                        return SwitchStep.ShowOpening;
                    }
                    return SwitchStep.None;

                case Stage.ReturningToEs:
                    return WhileReturning(now, screen());
            }

            return SwitchStep.None;
        }

        /// <summary>
        /// O Big Picture fechou pelo menu da própria Steam, com o ES minimizado: quem acompanha já pediu o ES de volta,
        /// e daqui em diante é como a volta pelo botão (a janela normal da Steam também aparece).
        /// </summary>
        public void ReturnAfterSteamClosed(DateTime now)
        {
            StartReturn(now);
        }

        void StartReturn(DateTime now)
        {
            _stage = Stage.ReturningToEs;
            _since = now;
            _lastFocus = now;
            _esInFrontSince = null;
        }

        SwitchStep WhileReturning(DateTime now, SwitchScreen front)
        {
            if (front == SwitchScreen.Es)
            {
                if (_esInFrontSince == null)
                    _esInFrontSince = now;
                return now - _esInFrontSince.Value >= EsSettle ? Finish(SwitchStep.EsReturned) : SwitchStep.None;
            }

            _esInFrontSince = null;
            if (now - _since > EsTimeout)
                return Finish(SwitchStep.EsReturnFailed);

            if (now - _lastFocus < EsFocusRetry)
                return SwitchStep.None;

            _lastFocus = now;
            return SwitchStep.FocusEsAgain;
        }

        SwitchStep Ask(DateTime now, SwitchScreen front, Func<bool> hasSteam, Func<GuideButtonConflict> conflict)
        {
            if (front == SwitchScreen.Other || !hasSteam())
                return SwitchStep.None;

            var found = conflict();
            if (found != null)
            {
                Conflict = found;
                return SwitchStep.WarnConflict;
            }

            _since = now;
            _stage = front == SwitchScreen.Es ? Stage.AskingSteam : Stage.AskingEs;
            return front == SwitchScreen.Es ? SwitchStep.AskSteam : SwitchStep.AskEs;
        }

        SwitchStep WhileAsking(DateTime now, bool pressed, SwitchScreen front)
        {
            var from = _stage == Stage.AskingSteam ? SwitchScreen.Es : SwitchScreen.BigPicture;
            if (front != from || now - _since > ConfirmWindow)
                return Finish(SwitchStep.HideAsk);

            if (!pressed)
                return SwitchStep.None;

            _since = now;
            if (_stage == Stage.AskingSteam)
            {
                _stage = Stage.OpeningSteam;
                _openingShown = false;
                return SwitchStep.OpenSteam;
            }

            StartReturn(now);
            return SwitchStep.ReturnToEs;
        }

        SwitchStep Finish(SwitchStep step)
        {
            _stage = Stage.Idle;
            return step;
        }
    }
}
