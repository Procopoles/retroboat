using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;

namespace Retroboat.Integrations
{
    /// <summary>Uma chave que funciona, achada pela busca automática.</summary>
    public sealed class CredentialFound
    {
        public CredentialFound(Dictionary<string, string> values, string origin)
        {
            Values = values;
            Origin = origin;
        }

        /// <summary>As credenciais da integração (a principal e as que a acompanham).</summary>
        public Dictionary<string, string> Values { get; }

        /// <summary>De onde veio (<see cref="CredentialSource.Name"/>).</summary>
        public string Origin { get; }
    }

    /// <summary>
    /// A busca automática de uma chave nova (RB-037): passa pelos lugares do <see cref="ICredentialRepository"/>, do mais barato
    /// ao mais caro, pega os candidatos de cada um (<see cref="CredentialExtractor"/>) e testa cada um na própria API. A primeira
    /// que a API aceita vale. Sem resposta da API, para: é a internet, e testar o resto só gastaria consultas.
    /// </summary>
    public sealed class CredentialRefresh
    {
        readonly IntegrationChecks _checks;
        readonly IEnumerator<CredentialSource> _pending;
        readonly List<CredentialSource> _seen = new List<CredentialSource>();

        /// <summary>O que deu errado na busca, para o log (pacote que não baixou, nenhuma candidata aceita...).</summary>
        public List<string> Problems { get; } = new List<string>();

        public CredentialRefresh(IntegrationChecks checks, ICredentialRepository repository)
        {
            _checks = checks;
            _pending = repository.Sources().GetEnumerator();
        }

        /// <param name="current">As credenciais em uso (a principal pode faltar, na primeira busca).</param>
        /// <returns>Nulo se nenhum lugar tem uma chave que funcione.</returns>
        public CredentialFound Find(Integration integration, IReadOnlyDictionary<string, string> current)
        {
            var primary = integration.Credentials[0];
            var tried = new HashSet<string>(StringComparer.Ordinal);
            if (current.TryGetValue(primary, out var inUse) && !string.IsNullOrEmpty(inUse))
                tried.Add(inUse);

            foreach (var source in Sources())
            {
                Dictionary<string, List<string>> candidates;
                try
                {
                    candidates = source.Candidates;
                }
                catch (Exception ex) when (ex is WebException || ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException)
                {
                    Problems.Add(source.Name + ": " + ex.Message);
                    continue;
                }

                if (!candidates.TryGetValue(primary, out var values))
                    continue;

                foreach (var value in values.Where(tried.Add))
                {
                    var trial = integration.Credentials.ToDictionary(n => n, n => current.TryGetValue(n, out var v) ? v : null, StringComparer.Ordinal);
                    trial[primary] = value;
                    foreach (var other in integration.Credentials.Skip(1))
                        if (candidates.TryGetValue(other, out var companions) && companions.Count > 0)
                            trial[other] = companions[0];

                    var result = _checks.Check(integration, trial);
                    if (result.State == IntegrationState.Ok)
                        return new CredentialFound(trial.Where(t => t.Value != null).ToDictionary(t => t.Key, t => t.Value, StringComparer.Ordinal), source.Name);

                    if (result.State == IntegrationState.Unreachable)
                    {
                        Problems.Add(integration.Name + ": a API não respondeu ao testar uma chave candidata (" + result.Detail + ")");
                        return null;
                    }
                }
            }

            Problems.Add(integration.Name + ": nenhuma chave candidata funcionou (" + (tried.Count - (inUse != null ? 1 : 0)) + " testada(s))");
            return null;
        }

        /// <summary>Os lugares já abertos nesta verificação primeiro: o pacote do repositório é baixado no máximo uma vez.</summary>
        IEnumerable<CredentialSource> Sources()
        {
            foreach (var source in _seen.ToList())
                yield return source;

            while (_pending.MoveNext())
            {
                _seen.Add(_pending.Current);
                yield return _pending.Current;
            }
        }
    }
}
