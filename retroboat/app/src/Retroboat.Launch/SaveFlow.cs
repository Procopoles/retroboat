using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Retroboat.Cloud;
using Retroboat.Library;
using Retroboat.Logging;
using Retroboat.Saves;
using Retroboat.Settings;
using Retroboat.Text;

namespace Retroboat.Launch
{
    /// <summary>
    /// Memory cards (e outros saves) na nuvem em volta de uma partida. O emulador cria, escolhe e grava os
    /// cartões; o Retroboat:
    /// <list type="bullet">
    /// <item>antes de abrir: avisa se outro PC está jogando com eles e puxa a versão mais nova;</item>
    /// <item>durante: envia cada gravação poucos segundos depois que o emulador para de gravar;</item>
    /// <item>depois: envia o que faltar, libera a trava e pergunta sobre conflitos que surgiram.</item>
    /// </list>
    /// Sem o Drive, ou se algo falhar aqui, o jogo abre com o que está neste PC: o Retroboat nunca impede de jogar.
    /// </summary>
    sealed class SaveFlow
    {
        const string Title = "Memory cards na nuvem";
        const string Busy = "Sincronizando memory cards...";
        static readonly TimeSpan PollEvery = TimeSpan.FromSeconds(2);
        static readonly TimeSpan MaxWait = TimeSpan.FromMinutes(2);

        readonly List<SaveSession> _sessions = new List<SaveSession>();

        SaveFlow() { }

        /// <summary>O usuário preferiu voltar ao ES sem jogar.</summary>
        public bool Cancelled { get; private set; }

        /// <returns>Nulo se este jogo não tem saves sincronizados (ou a nuvem não está disponível).</returns>
        public static SaveFlow Before(RetroBatInstall install, string[] args, string rom)
        {
            var rules = SaveRules.For(CommandLine.GetOption(args, "-system"), CommandLine.GetOption(args, "-emulator"));
            if (rules.Count == 0)
                return null;

            var flow = new SaveFlow();
            try
            {
                var settings = RetroboatSettings.Load(RetroboatSettings.GetPath(install));
                var cloud = CloudLocator.Locate(settings.CloudPath);
                if (cloud == null || !cloud.Exists)
                {
                    Log.Warn("Google Drive indisponível: o jogo abre com os memory cards deste PC, que vão para a nuvem na próxima vez que o Retroboat abrir com o Drive.");
                    return null;
                }

                var savesPath = SaveRules.GetSavesPath(install);
                var game = string.IsNullOrEmpty(rom) ? null : Path.GetFileNameWithoutExtension(rom);

                foreach (var rule in rules)
                {
                    var sync = new SaveSync(savesPath, install.RetroboatDataPath, cloud.RetroboatPath, rule);
                    foreach (var item in sync.UnsupportedItems())
                        Log.Warn(rule.DisplayName + ": " + item);

                    if (!CheckLock(sync) || !SyncWithChoices(sync, beforeGame: true))
                    {
                        flow.Cancelled = true;
                        flow.FinishSessions();
                        return flow;
                    }

                    var session = new SaveSession(sync, rule == SaveRules.Pcsx2MemoryCards ? () => SaveRules.Pcsx2ActiveCards(install) : (Func<IEnumerable<string>>)null);
                    session.Start(game);
                    flow._sessions.Add(session);
                    Log.Info(rule.DisplayName + ": " + rule.ItemName + "s em dia; trava deste PC para " + game);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Falha ao sincronizar os memory cards antes do jogo; o jogo abre com os deste PC", ex);
                flow.FinishSessions();
                return null;
            }

            return flow;
        }

        /// <summary>Roda o emulador enviando cada gravação para a nuvem e, no fim, fecha a sessão.</summary>
        public int During(Func<int> runEmulator)
        {
            using (var stop = new ManualResetEvent(false))
            {
                var poller = new Thread(() =>
                {
                    while (!stop.WaitOne(PollEvery))
                    {
                        foreach (var session in _sessions)
                            Safe("durante o jogo", () => LogResult(session.Sync.Rule, session.Poll(), "durante o jogo"));
                    }
                }) { IsBackground = true };

                poller.Start();
                try
                {
                    return runEmulator();
                }
                finally
                {
                    stop.Set();
                    poller.Join();
                    FinishSessions();
                }
            }
        }

        void FinishSessions()
        {
            foreach (var session in _sessions)
            {
                Safe("ao fechar o jogo", () => LogResult(session.Sync.Rule, session.Finish(), "ao fechar o jogo"));

                // Outro PC pode ter salvo no mesmo cartão durante a partida.
                Safe("depois do jogo", () => SyncWithChoices(session.Sync, beforeGame: false));
            }

            _sessions.Clear();
        }

        /// <returns>false se o usuário preferiu voltar.</returns>
        static bool CheckLock(SaveSync sync)
        {
            var other = sync.FindActiveLock();
            if (other == null)
                return true;

            var text = "Os " + sync.Rule.ItemName + "s do " + sync.Rule.DisplayName + " estão em uso no " + other.Pc + " desde " +
                       other.SinceUtc.ToLocalTime().ToString("HH:mm", Format.Culture) +
                       (string.IsNullOrEmpty(other.Game) ? "" : ", jogando " + other.Game) +
                       ". Se você jogar aqui também, o que for salvo nos dois PCs vira um conflito, e você escolhe qual versão manter. " +
                       "Esta tela fecha sozinha quando o outro PC terminar.";

            int choice = ChoiceForm.Ask(Title, "Em uso no " + other.Pc, text,
                new[] { "Voltar", "Jogar mesmo assim" }, autoClose: () => sync.FindActiveLock() == null);

            Log.Info(sync.Rule.DisplayName + ": em uso no " + other.Pc + " (renovado " + other.HeartbeatUtc.ToLocalTime().ToString("HH:mm") + "): " +
                     (choice == 1 ? "jogar mesmo assim" : choice == ChoiceForm.AutoClosed ? "a trava saiu" : "voltou ao ES"));
            return choice == 1 || choice == ChoiceForm.AutoClosed;
        }

        /// <summary>Planeja, espera o que está chegando, pergunta os conflitos e aplica.</summary>
        /// <returns>false se o usuário preferiu voltar (só antes do jogo).</returns>
        static bool SyncWithChoices(SaveSync sync, bool beforeGame)
        {
            var rule = sync.Rule;
            var started = DateTime.UtcNow;

            while (true)
            {
                var plans = ChoiceForm.Busy(Title, Busy, () => sync.Plan());
                var waiting = plans.Where(p => p.Action == SaveAction.WaitForCloud).Select(p => p.Name).ToList();

                // Depois do jogo, não segura o usuário: o que estiver chegando vem na próxima sincronização.
                if (beforeGame && waiting.Count > 0 && DateTime.UtcNow - started < MaxWait)
                {
                    int wait = ChoiceForm.Ask(Title, "Aguardando a nuvem",
                        "O " + rule.ItemName + " " + string.Join(", ", waiting) + " foi salvo em outro PC e ainda está chegando. O jogo abre assim que ele chegar.",
                        new[] { "Jogar sem esperar", "Voltar" },
                        autoClose: () => DateTime.UtcNow - started >= MaxWait || sync.Plan(only: waiting).All(p => p.Action != SaveAction.WaitForCloud));

                    if (wait == ChoiceForm.AutoClosed)
                        continue;

                    if (wait != 0)
                        return false;

                    Log.Info(rule.DisplayName + ": jogar sem esperar " + string.Join(", ", waiting));
                }

                var choices = new Dictionary<string, ConflictChoice>(StringComparer.OrdinalIgnoreCase);
                foreach (var conflict in plans.Where(p => p.Action == SaveAction.Conflict))
                {
                    var options = new[]
                    {
                        "Este PC   ·   salvo " + When(conflict.LocalModifiedUtc),
                        "Nuvem   ·   salvo " + When(conflict.Cloud.TimeUtc) + " no " + conflict.Cloud.Pc
                    };

                    int choice = ChoiceForm.Ask(Title, "O " + rule.ItemName + " " + conflict.Name + " mudou neste PC e na nuvem",
                        "Escolha qual versão usar. A outra fica guardada na nuvem, em conflitos." + (beforeGame ? "" : " B ou Esc: decidir na próxima vez."),
                        options);

                    if (choice == 0 || choice == 1)
                    {
                        choices[conflict.Name] = choice == 0 ? ConflictChoice.KeepLocal : ConflictChoice.KeepCloud;
                        Log.Info(rule.DisplayName + ": conflito em " + conflict.Name + ": " + (choice == 0 ? "ficou a versão deste PC" : "ficou a versão da nuvem (" + conflict.Cloud.Pc + ")"));
                    }
                    else if (beforeGame)
                    {
                        return false;
                    }
                }

                var result = ChoiceForm.Busy(Title, Busy, () => sync.Apply(plans, p => choices.TryGetValue(p.Name, out var c) ? c : (ConflictChoice?)null));
                LogResult(rule, result, beforeGame ? "antes do jogo" : "depois do jogo");
                return true;
            }
        }

        /// <summary>"hoje 14:32", "ontem 22:10" ou "23/10 09:15".</summary>
        static string When(DateTime utc)
        {
            var local = utc.ToLocalTime();
            var day = local.Date == DateTime.Today ? "hoje" : local.Date == DateTime.Today.AddDays(-1) ? "ontem" : local.ToString("dd/MM", Format.Culture);
            return day + " " + local.ToString("HH:mm", Format.Culture);
        }

        static void LogResult(SaveRule rule, SaveSyncResult result, string when)
        {
            var prefix = rule.DisplayName + ", " + when + ": ";
            foreach (var name in result.Uploaded) Log.Info(prefix + rule.ItemName + " enviado para a nuvem: " + name);
            foreach (var name in result.Downloaded) Log.Info(prefix + rule.ItemName + " baixado da nuvem: " + name);
            foreach (var name in result.DeletedLocal) Log.Info(prefix + "apagado em outro PC, apagado aqui (cópia no histórico): " + name);
            foreach (var name in result.DeletedInCloud) Log.Info(prefix + "apagado no emulador, apagado na nuvem: " + name);
            foreach (var name in result.Conflicts) Log.Warn(prefix + "mudou aqui e na nuvem; a cópia deste PC foi guardada em conflitos: " + name);
            foreach (var name in result.Waiting) Log.Info(prefix + "aguardando a versão de outro PC chegar: " + name);
            foreach (var item in result.Skipped) Log.Warn(prefix + "não sincronizado: " + item);
            foreach (var error in result.Errors) Log.Warn(prefix + "erro: " + error);
        }

        static void Safe(string when, Action action)
        {
            try { action(); }
            catch (Exception ex) { Log.Error("Falha na sincronização dos memory cards " + when, ex); }
        }
    }
}
