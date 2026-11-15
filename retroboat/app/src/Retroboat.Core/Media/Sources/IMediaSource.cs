using System;
using System.Collections.Generic;
using System.IO;

namespace Retroboat.Media.Sources
{
    /// <summary>O jogo que se procura.</summary>
    public sealed class MediaQuery
    {
        public MediaQuery(string system, string fileName, string typedName = null)
        {
            System = system;
            FileName = fileName;
            TypedName = string.IsNullOrWhiteSpace(typedName) ? null : typedName.Trim();
        }

        /// <summary>Sistema do ES (ex.: "ps2").</summary>
        public string System { get; }

        /// <summary>Nome do arquivo, com extensão quando se sabe ("Jogo (USA).iso"); é o que o ScreenScraper reconhece.</summary>
        public string FileName { get; }

        /// <summary>O nome do arquivo sem extensão (o padrão No-Intro/Redump que libretro e Wikidata usam).</summary>
        public string Stem => HasKnownExtension(FileName) ? Path.GetFileNameWithoutExtension(FileName) : FileName;

        /// <summary>O nome que a pessoa digitou no PROCURAR MÍDIAS do ES; nulo na busca pelo arquivo.</summary>
        public string TypedName { get; }

        /// <summary>O que vale como nome do jogo: o digitado, ou o do arquivo.</summary>
        public string SearchName => TypedName ?? Stem;

        /// <summary>
        /// O ES manda o nome sem extensão quando não tem o arquivo; "Jogo (USA) (v1.1)" não tem extensão, e o ".1)" não
        /// pode virar uma.
        /// </summary>
        static bool HasKnownExtension(string name)
        {
            var extension = Path.GetExtension(name ?? "");
            return extension.Length >= 2 && extension.Length <= 5 && !extension.Contains(")") && !extension.Contains(" ");
        }
    }

    /// <summary>Um arquivo de mídia que a fonte tem: o endereço e a extensão com que ele deve ser gravado.</summary>
    public sealed class MediaUrl
    {
        public MediaUrl(string url, string extension = null)
        {
            Url = url;
            Extension = NormalizeExtension(extension) ?? ExtensionOf(url);
        }

        public string Url { get; }

        /// <summary>".png", ".jpg", ".mp4"; nulo se nem a fonte nem o endereço dizem.</summary>
        public string Extension { get; }

        static string NormalizeExtension(string extension)
        {
            if (string.IsNullOrWhiteSpace(extension))
                return null;

            extension = extension.Trim().ToLowerInvariant();
            return extension.StartsWith(".", StringComparison.Ordinal) ? extension : "." + extension;
        }

        static string ExtensionOf(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                return null;

            var extension = Path.GetExtension(uri.AbsolutePath).ToLowerInvariant();
            return extension.Length >= 3 && extension.Length <= 5 ? extension : null;
        }
    }

    /// <summary>O que uma fonte achou de um jogo, já nos campos do <see cref="MediaFields"/>.</summary>
    public sealed class MediaCandidate
    {
        public MediaCandidate(string source)
        {
            Source = source;
        }

        public string Source { get; }

        /// <summary>Campo de texto → valor, no formato do gamelist (data yyyyMMddT000000, jogadores "1-4").</summary>
        public Dictionary<string, string> Texts { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>A descrição está em português.</summary>
        public bool DescriptionInPortuguese { get; set; }

        /// <summary>Campo de mídia → arquivo.</summary>
        public Dictionary<string, MediaUrl> Media { get; } = new Dictionary<string, MediaUrl>(StringComparer.OrdinalIgnoreCase);

        public void SetText(string field, string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                Texts[field] = value.Trim();
        }

        public void SetMedia(string field, string url, string extension = null)
        {
            if (!string.IsNullOrWhiteSpace(url))
                Media[field] = new MediaUrl(url.Trim(), extension);
        }

        public bool Has(string field) => Texts.ContainsKey(field) || Media.ContainsKey(field);

        public bool IsEmpty => Texts.Count == 0 && Media.Count == 0;
    }

    /// <summary>
    /// Uma fonte do PERSONALIZADO (RB-038). Separada para os testes não dependerem da internet.
    /// </summary>
    public interface IMediaSource
    {
        /// <summary>Um de <see cref="MediaSourceIds"/>.</summary>
        string Id { get; }

        /// <summary>A fonte conhece o sistema e pode ser usada (tem a chave, quando precisa de uma).</summary>
        bool Supports(string system);

        /// <summary>
        /// O jogo, ou nulo se a fonte não o conhece. Lança <see cref="MediaSourceException"/>, WebException ou IOException
        /// quando a fonte não respondeu (sem internet, fora do ar, limite do dia).
        /// </summary>
        /// <param name="wanted">Os campos que se espera desta fonte: uma fonte com consulta extra por tipo de mídia (TheGamesDB)
        /// só a faz quando precisa. As outras podem devolver tudo o que têm.</param>
        MediaCandidate Find(MediaQuery query, IReadOnlyCollection<string> wanted);
    }

    /// <summary>Fonte que consulta muitos jogos de uma vez mais rápido (Wikidata, listas do libretro).</summary>
    public interface IBatchMediaSource : IMediaSource
    {
        /// <summary>Busca de uma vez os jogos que o <see cref="IMediaSource.Find"/> vai pedir em seguida, pelo nome sem extensão.</summary>
        void Prepare(string system, IReadOnlyCollection<string> stems);
    }

    /// <summary>A fonte respondeu, mas recusou (limite de uso, chave recusada, API fechada): tentar de novo depois.</summary>
    public sealed class MediaSourceException : Exception
    {
        public MediaSourceException(string message) : base(message)
        {
        }
    }
}
