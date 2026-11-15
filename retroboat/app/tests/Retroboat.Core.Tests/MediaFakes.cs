using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Retroboat.Media;
using Retroboat.Media.Sources;

namespace Retroboat.Tests
{
    /// <summary>Uma fonte do PERSONALIZADO em memória: nenhum teste vai à internet.</summary>
    sealed class FakeMediaSource : IBatchMediaSource
    {
        readonly Dictionary<string, MediaCandidate> _games = new Dictionary<string, MediaCandidate>(StringComparer.OrdinalIgnoreCase);

        public FakeMediaSource(string id)
        {
            Id = id;
        }

        public string Id { get; }

        public HashSet<string> Systems { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ps2" };

        public int Calls;
        public int PrepareCalls;
        public Exception Fail;
        public List<string> Asked = new List<string>();
        public List<IReadOnlyCollection<string>> Wanted = new List<IReadOnlyCollection<string>>();

        /// <summary>O jogo, pelo nome do arquivo sem extensão (ou pelo nome digitado).</summary>
        public FakeMediaSource Add(string name, Action<MediaCandidate> fill)
        {
            var candidate = new MediaCandidate(Id);
            fill(candidate);
            _games[name] = candidate;
            return this;
        }

        /// <summary>Atalho: o jogo com estes campos de texto.</summary>
        public FakeMediaSource Text(string name, string field, string value) => Add(name, c =>
        {
            if (_games.TryGetValue(name, out var old))
            {
                foreach (var text in old.Texts) c.Texts[text.Key] = text.Value;
                foreach (var media in old.Media) c.Media[media.Key] = media.Value;
                c.DescriptionInPortuguese = old.DescriptionInPortuguese;
            }

            c.SetText(field, value);
        });

        /// <summary>Atalho: o jogo com esta mídia (endereço "https://&lt;fonte&gt;.test/&lt;campo&gt;/&lt;jogo&gt;.png").</summary>
        public FakeMediaSource Media(string name, string field, string extension = ".png") => Add(name, c =>
        {
            if (_games.TryGetValue(name, out var old))
            {
                foreach (var text in old.Texts) c.Texts[text.Key] = text.Value;
                foreach (var media in old.Media) c.Media[media.Key] = media.Value;
                c.DescriptionInPortuguese = old.DescriptionInPortuguese;
            }

            c.SetMedia(field, UrlOf(field, name, extension), extension);
        });

        public string UrlOf(string field, string name, string extension = ".png") => "https://" + Id + ".test/" + field + "/" + Uri.EscapeDataString(name) + extension;

        public bool Supports(string system) => Systems.Contains(system);

        public void Prepare(string system, IReadOnlyCollection<string> stems) => PrepareCalls++;

        public MediaCandidate Find(MediaQuery query, IReadOnlyCollection<string> wanted)
        {
            lock (this)
            {
                Calls++;
                Asked.Add(query.SearchName);
                Wanted.Add(wanted.ToList());
            }

            if (Fail != null)
                throw Fail;

            return _games.TryGetValue(query.SearchName, out var candidate) ? candidate : null;
        }
    }

    /// <summary>HTTP em memória: respostas da API por começo de endereço, e arquivos baixados.</summary>
    sealed class FakeMediaHttp : IMediaHttp
    {
        readonly List<(string Prefix, HttpAnswer Answer)> _answers = new List<(string, HttpAnswer)>();

        public List<string> Requests = new List<string>();
        public List<string> Downloads = new List<string>();
        public string FailOn;

        public FakeMediaHttp Answer(string urlPrefix, string body, int status = 200)
        {
            _answers.Add((urlPrefix, new HttpAnswer(status, body)));
            return this;
        }

        public HttpAnswer Get(string url, IDictionary<string, string> headers = null) => Reply(url);

        public HttpAnswer Post(string url, string form, IDictionary<string, string> headers = null) => Reply(url);

        HttpAnswer Reply(string url)
        {
            lock (this)
                Requests.Add(url);

            var found = _answers.FirstOrDefault(a => url.StartsWith(a.Prefix, StringComparison.Ordinal));
            return found.Answer ?? new HttpAnswer(404, "");
        }

        public byte[] Download(string url)
        {
            lock (this)
                Downloads.Add(url);

            if (FailOn != null && url.Contains(FailOn))
                throw new WebException("conexão interrompida");

            return new byte[] { 1, 2, 3, (byte)url.Length };
        }
    }

    static class MediaTest
    {
        public static CustomMediaSearch Search(params IMediaSource[] sources) => new CustomMediaSearch(MediaProfile.Default, sources);

        public static CustomMediaSearch Search(MediaProfile profile, params IMediaSource[] sources) => new CustomMediaSearch(profile, sources);
    }
}
