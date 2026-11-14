using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Retroboat.Library;

namespace Retroboat.Integrations
{
    /// <summary>
    /// As chaves que o nosso ES usa (RB-037): &lt;RetroBat&gt;\retroboat\credenciais.ini. O ES lê a seção [Credenciais] e, para
    /// a chave que não está aqui, usa a que veio compilada nele. O Retroboat preenche e troca sozinho
    /// (<see cref="CredentialRefresh"/>); a seção [Origem] diz de onde veio cada uma e quando.
    ///
    /// Fica só neste PC: cada PC acha as suas chaves sozinho, e nada de chave vai para o Drive.
    /// </summary>
    public sealed class CredentialStore
    {
        const string CredentialsSection = "Credenciais";
        const string OriginSection = "Origem";
        const string ReplacedSection = "Trocada";

        readonly string _path;
        readonly Dictionary<string, string> _values = new Dictionary<string, string>(StringComparer.Ordinal);
        readonly Dictionary<string, string> _origins = new Dictionary<string, string>(StringComparer.Ordinal);
        readonly Dictionary<string, DateTime> _replaced = new Dictionary<string, DateTime>(StringComparer.Ordinal);

        CredentialStore(string path)
        {
            _path = path;
        }

        public static string GetPath(RetroBatInstall install) => Path.Combine(install.RetroboatDataPath, "credenciais.ini");

        public static CredentialStore Load(string path)
        {
            var store = new CredentialStore(path);
            if (!File.Exists(path))
                return store;

            string section = null;
            foreach (var raw in File.ReadAllLines(path, Encoding.UTF8))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith(";", StringComparison.Ordinal) || line.StartsWith("#", StringComparison.Ordinal))
                    continue;

                if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal))
                {
                    section = line.Substring(1, line.Length - 2).Trim();
                    continue;
                }

                // O valor pode ter "=" (devid=...&devpassword=...): só o primeiro separa.
                var equals = line.IndexOf('=');
                if (equals <= 0)
                    continue;

                var key = line.Substring(0, equals).Trim();
                var value = line.Substring(equals + 1).Trim();
                if (section == CredentialsSection && value.Length > 0)
                    store._values[key] = value;
                else if (section == OriginSection)
                    store._origins[key] = value;
                else if (section == ReplacedSection && DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var when))
                    store._replaced[key] = when;
            }

            return store;
        }

        public string Get(string name) => _values.TryGetValue(name, out var value) ? value : null;

        /// <summary>De onde veio a chave e quando, ex.: "repositório do RetroBat (ES 42.2026.1112.1244) · 2026-11-14T19:00:00Z".</summary>
        public string OriginOf(string name) => _origins.TryGetValue(name, out var origin) ? origin : null;

        public bool Has(string name) => _values.ContainsKey(name);

        /// <summary>Quando a troca automática trocou esta chave por último (não conta a primeira busca); nulo se nunca.</summary>
        public DateTime? ReplacedAt(string name) => _replaced.TryGetValue(name, out var when) ? when : (DateTime?)null;

        /// <param name="replacement">true quando a chave anterior parou de funcionar (aparece na tela INTEGRAÇÕES por uns dias).</param>
        public void Set(string name, string value, string origin, DateTime when, bool replacement)
        {
            _values[name] = value;
            _origins[name] = origin + " · " + Iso(when);
            if (replacement)
                _replaced[name] = when.ToUniversalTime();
        }

        static string Iso(DateTime when) => when.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

        /// <summary>Grava inteiro, por um arquivo temporário: o ES nunca lê um arquivo pela metade.</summary>
        public void Save()
        {
            var text = new StringBuilder()
                .AppendLine("; Chaves das integrações do Retroboat (RB-037). O nosso ES lê a seção [Credenciais]; a chave que faltar aqui")
                .AppendLine("; é a que veio compilada nele. O Retroboat preenche e troca sozinho quando uma para de funcionar,")
                .AppendLine("; procurando no repositório do RetroBat. Veja a tela INTEGRAÇÕES no menu NUVEM do ES.")
                .AppendLine("[" + CredentialsSection + "]");
            foreach (var name in Ordered(_values.Keys))
                text.AppendLine(name + "=" + _values[name]);

            text.AppendLine().AppendLine("[" + OriginSection + "]");
            foreach (var name in Ordered(_origins.Keys))
                text.AppendLine(name + "=" + _origins[name]);

            if (_replaced.Count > 0)
            {
                text.AppendLine().AppendLine("[" + ReplacedSection + "]");
                foreach (var name in Ordered(_replaced.Keys))
                    text.AppendLine(name + "=" + Iso(_replaced[name]));
            }

            Directory.CreateDirectory(Path.GetDirectoryName(_path));
            var temp = _path + ".retroboat-part";
            File.WriteAllText(temp, text.ToString(), new UTF8Encoding(false));
            if (File.Exists(_path))
                File.Delete(_path);
            File.Move(temp, _path);
        }

        static IEnumerable<string> Ordered(IEnumerable<string> names)
        {
            var known = IntegrationCatalog.AllCredentials.ToList();
            return names.OrderBy(n => known.IndexOf(n) < 0 ? int.MaxValue : known.IndexOf(n)).ThenBy(n => n, StringComparer.Ordinal);
        }
    }
}
