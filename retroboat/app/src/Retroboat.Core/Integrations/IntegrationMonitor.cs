using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using Retroboat.Accounts;
using Retroboat.Library;

namespace Retroboat.Integrations
{
    public sealed class IntegrationReport
    {
        public List<IntegrationStatus> Items { get; } = new List<IntegrationStatus>();

        /// <summary>Não verificou agora: a última verificação ainda é recente, ou outra estava em andamento.</summary>
        public bool Skipped { get; set; }

        /// <summary>Só o que mudou desde a última verificação, para avisar no ES: chave trocada sozinha, integração que parou.</summary>
        public List<string> Messages { get; } = new List<string>();

        /// <summary>Detalhes para o log do Retroboat (o que cada API respondeu, onde a busca procurou).</summary>
        public List<string> Log { get; } = new List<string>();
    }

    /// <summary>
    /// Verifica as integrações (RB-037) e troca sozinha a chave que parou de funcionar: cada chave do build é testada na própria
    /// API; recusada (ou no limite), a <see cref="CredentialRefresh"/> procura outra no repositório do RetroBat e, achando,
    /// grava no credenciais.ini, que o nosso ES lê na hora. O resultado vai para a tela INTEGRAÇÕES do menu NUVEM
    /// (<see cref="IntegrationStatusFile"/>).
    ///
    /// Roda em segundo plano na abertura do Retroboat, de tempos em tempos com o ES aberto e no VERIFICAR AGORA da tela.
    /// </summary>
    public sealed class IntegrationMonitor
    {
        /// <summary>Sem pedido da pessoa, não verifica de novo antes disso: cada verificação são umas dez consultas.</summary>
        public static readonly TimeSpan MinInterval = TimeSpan.FromHours(1);

        /// <summary>Por quanto tempo a tela mostra que uma chave foi trocada sozinha.</summary>
        static readonly TimeSpan ShowReplacementFor = TimeSpan.FromDays(7);

        const string MutexName = @"Local\RetroboatIntegrations";

        readonly RetroBatInstall _install;
        readonly IntegrationChecks _checks;
        readonly ICredentialRepository _repository;
        readonly Func<DateTime> _now;

        public IntegrationMonitor(RetroBatInstall install, IntegrationChecks checks = null, ICredentialRepository repository = null, Func<DateTime> now = null)
        {
            _install = install;
            _checks = checks ?? new IntegrationChecks();
            _repository = repository ?? new RetroBatCredentialRepository(install);
            _now = now ?? (() => DateTime.UtcNow);
        }

        /// <param name="force">Verificar mesmo que a última seja recente (VERIFICAR AGORA, linha de comando).</param>
        public IntegrationReport Run(bool force = false)
        {
            var previous = IntegrationStatusFile.Read(_install);
            if (!force && previous != null && _now() - previous.Value.CheckedAt < MinInterval)
                return Skip(previous.Value.Items, "Integrações verificadas há pouco (" + previous.Value.CheckedAt.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture) + ").");

            using (var mutex = new Mutex(false, MutexName))
            {
                bool acquired;
                try
                {
                    acquired = mutex.WaitOne(TimeSpan.FromSeconds(1));
                }
                catch (AbandonedMutexException)
                {
                    acquired = true;
                }

                if (!acquired)
                    return Skip(previous?.Items ?? new List<IntegrationStatus>(), "Outra verificação das integrações em andamento.");

                try
                {
                    return Verify(previous?.Items);
                }
                finally
                {
                    mutex.ReleaseMutex();
                }
            }
        }

        static IntegrationReport Skip(List<IntegrationStatus> items, string why)
        {
            var report = new IntegrationReport { Skipped = true };
            report.Items.AddRange(items);
            report.Log.Add(why);
            return report;
        }

        IntegrationReport Verify(List<IntegrationStatus> previous)
        {
            var report = new IntegrationReport();
            var storePath = CredentialStore.GetPath(_install);
            var store = CredentialStore.Load(storePath);
            var refresh = new CredentialRefresh(_checks, _repository);
            bool storeChanged = false;

            foreach (var integration in IntegrationCatalog.All)
            {
                var status = new IntegrationStatus { Id = integration.Id, Name = integration.Name, Purpose = integration.Purpose };
                switch (integration.Kind)
                {
                    case IntegrationKind.BuiltInKey:
                        storeChanged |= VerifyKey(integration, store, refresh, status, report);
                        break;
                    case IntegrationKind.Account:
                        Describe(status, _checks.Check(integration, null, IgdbAccount()), report);
                        break;
                    default:
                        Describe(status, _checks.Check(integration, null), report);
                        break;
                }

                report.Items.Add(status);
            }

            foreach (var problem in refresh.Problems)
                report.Log.Add("Busca de chaves: " + problem);

            if (storeChanged)
                store.Save();

            IntegrationStatusFile.Write(_install, report.Items, _now());
            AddTransitions(previous, report);
            return report;
        }

        /// <returns>true se gravou chave nova no credenciais.ini.</returns>
        bool VerifyKey(Integration integration, CredentialStore store, CredentialRefresh refresh, IntegrationStatus status, IntegrationReport report)
        {
            var primary = integration.Credentials[0];
            var current = integration.Credentials.ToDictionary(n => n, store.Get, StringComparer.Ordinal);
            bool firstSearch = current[primary] == null;

            // Sem chave no arquivo (primeira vez): procura logo, começando pela que veio compilada no nosso ES.
            var result = firstSearch ? null : _checks.Check(integration, current);
            if (result != null)
                report.Log.Add(integration.Name + ": " + IntegrationStatusFile.StateName(result.State) + (result.Detail != null ? " (" + result.Detail + ")" : ""));

            bool needsKey = result == null || result.State == IntegrationState.KeyRejected || result.State == IntegrationState.LimitReached;
            if (needsKey)
            {
                var found = refresh.Find(integration, current);
                if (found != null)
                {
                    foreach (var value in found.Values)
                        store.Set(value.Key, value.Value, found.Origin, _now(), replacement: !firstSearch);

                    report.Log.Add(integration.Name + (firstSearch ? ": chave achada em " : ": chave trocada sozinha, a nova veio de ") + found.Origin);
                    status.ReplacedNow = !firstSearch;
                    result = new CheckResult(IntegrationState.Ok);
                    Describe(status, result, report, store.ReplacedAt(primary), found.Origin);
                    return true;
                }

                if (firstSearch)
                {
                    // Nada achado nem testado: o ES usa a chave compilada nele, que ninguém conseguiu conferir.
                    status.State = IntegrationState.Unknown;
                    status.Text = "Não verificada";
                    status.Detail = "Usa a chave que veio no ES.";
                    return false;
                }
            }

            Describe(status, result, report, store.ReplacedAt(primary), OriginName(store.OriginOf(primary)));
            return false;
        }

        void Describe(IntegrationStatus status, CheckResult result, IntegrationReport report, DateTime? replacedAt = null, string origin = null)
        {
            status.State = result.State;
            var integration = IntegrationCatalog.Find(status.Id);
            bool builtIn = integration?.Kind == IntegrationKind.BuiltInKey;
            bool account = integration?.Kind == IntegrationKind.Account;
            if (!builtIn)
                report.Log.Add(status.Name + ": " + IntegrationStatusFile.StateName(result.State) + (result.Detail != null ? " (" + result.Detail + ")" : ""));

            switch (result.State)
            {
                case IntegrationState.Ok:
                    status.Text = "Funcionando";
                    if (builtIn && replacedAt.HasValue && _now() - replacedAt.Value < ShowReplacementFor)
                        status.Detail = "Chave trocada sozinha em " + replacedAt.Value.ToLocalTime().ToString("dd/MM 'às' HH:mm", CultureInfo.InvariantCulture) +
                                        (origin != null ? ", do " + origin : "") + ".";
                    break;
                case IntegrationState.KeyRejected:
                    status.Text = account ? "Conta recusada" : "Chave recusada";
                    status.Detail = account
                        ? "Confira o Client ID e o segredo da Twitch nas opções do scraper do ES."
                        : "Ainda não há chave nova no repositório do RetroBat. O Retroboat tenta de novo sozinho.";
                    break;
                case IntegrationState.LimitReached:
                    status.Text = "Limite atingido";
                    status.Detail = builtIn ? "A chave vale, mas o limite de uso acabou. Volta sozinho." : "Muitas consultas agora. Volta sozinho.";
                    break;
                case IntegrationState.Unreachable:
                    status.Text = "Sem resposta";
                    status.Detail = builtIn ? "Fora do ar ou sem internet. Não é a chave." : "Fora do ar ou sem internet.";
                    break;
                case IntegrationState.NoAccount:
                    status.Text = "Sem conta";
                    status.Detail = "Opcional: a sua conta Twitch nas opções do scraper do ES.";
                    break;
                default:
                    status.Text = "Não verificada";
                    break;
            }
        }

        /// <summary>"repositório do RetroBat (ES 42...) · 2026-11-14T19:00:00Z" → "repositório do RetroBat (ES 42...)".</summary>
        static string OriginName(string origin)
        {
            if (string.IsNullOrEmpty(origin))
                return null;

            var dot = origin.LastIndexOf(" · ", StringComparison.Ordinal);
            return dot > 0 ? origin.Substring(0, dot) : origin;
        }

        /// <summary>A conta do IGDB que a pessoa informou no ES (Client ID e segredo da Twitch).</summary>
        (string Id, string Secret)? IgdbAccount()
        {
            try
            {
                var settings = EsSettings.Load(Path.Combine(_install.UserEmulationStationPath, "es_settings.cfg"));
                var id = settings.Get("IGDBClientID");
                var secret = settings.Get("IGDBSecret");
                return string.IsNullOrEmpty(id) || string.IsNullOrEmpty(secret) ? ((string, string)?)null : (id, secret);
            }
            catch (Exception ex) when (ex is IOException || ex is System.Xml.XmlException || ex is UnauthorizedAccessException)
            {
                return null;
            }
        }

        /// <summary>Aviso no ES só do que mudou: uma chave trocada sozinha, ou uma integração que parou e não teve troca.</summary>
        static void AddTransitions(List<IntegrationStatus> previous, IntegrationReport report)
        {
            foreach (var item in report.Items)
            {
                if (item.ReplacedNow)
                {
                    report.Messages.Add(item.Name + ": a chave parou de funcionar e foi trocada sozinha.");
                    continue;
                }

                var before = previous?.FirstOrDefault(p => p.Id == item.Id)?.State ?? IntegrationState.Unknown;
                if (item.State == IntegrationState.KeyRejected && before != IntegrationState.KeyRejected)
                    report.Messages.Add(item.Name + " parou de funcionar: " + item.Text.ToLowerInvariant() + ". " + item.Detail);
                else if (item.State == IntegrationState.Ok && before == IntegrationState.KeyRejected)
                    report.Messages.Add(item.Name + " voltou a funcionar.");
            }
        }
    }
}
