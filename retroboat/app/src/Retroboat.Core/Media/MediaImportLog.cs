using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Retroboat.Library;

namespace Retroboat.Media
{
    /// <summary>O que a importação automática busca de um jogo nas fontes públicas.</summary>
    public enum MediaImportKind
    {
        /// <summary>Capa, telas, logo, fan art e vídeo (<see cref="AutoImport"/>).</summary>
        Images,

        /// <summary>Nome, descrição, gênero, datas e estúdios (<see cref="AutoImport"/>).</summary>
        Info
    }

    /// <summary>
    /// O que a importação automática já buscou nas fontes públicas, jogo a jogo, achado ou não, de todos os PCs. O resultado
    /// vai para a nuvem com o <see cref="CloudMediaSync"/>; este registro garante que nenhum PC volte às fontes para o mesmo
    /// jogo sozinho. Buscar de novo é só pelo PROCURAR MÍDIAS do ES, quando a pessoa pede.
    ///
    /// Na nuvem: midia\importacao\&lt;PC&gt;.txt, uma linha por busca ("informacoes", tab, "ps2/Jogo (USA).iso", tab, data UTC,
    /// tab, 1 achado ou 0 não). Cada PC só grava o seu arquivo (o Drive nunca vê dois PCs no mesmo arquivo); a leitura junta
    /// todos.
    /// </summary>
    public sealed class MediaImportLog
    {
        sealed class Entry
        {
            public DateTime When;
            public bool Found;
        }

        readonly string _folder;
        readonly string _ownFile;
        readonly Dictionary<string, Entry> _all = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, Entry> _own = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        bool _changed;

        /// <summary>A lista antiga do AutoInfo, apagada no próximo <see cref="Save"/> (<see cref="ImportLegacy"/>).</summary>
        string _legacy;

        MediaImportLog(string folder, string pcName)
        {
            _folder = folder;
            _ownFile = Path.Combine(folder, SafeFileName(string.IsNullOrEmpty(pcName) ? Environment.MachineName : pcName) + ".txt");
            if (!Directory.Exists(folder))
                return;

            foreach (var file in Directory.GetFiles(folder, "*.txt"))
            {
                bool own = string.Equals(Path.GetFullPath(file), Path.GetFullPath(_ownFile), StringComparison.OrdinalIgnoreCase);
                try
                {
                    foreach (var line in File.ReadAllLines(file, Encoding.UTF8))
                        Read(line, own);
                }
                catch (IOException) when (!own)
                {
                    // Arquivo de outro PC no meio do envio pelo Drive: vale na próxima leitura.
                }
            }
        }

        /// <summary>O registro na nuvem, de todos os PCs: o que um PC buscou, nenhum outro busca de novo.</summary>
        public static MediaImportLog InCloud(string cloudRetroboatPath, string pcName = null) =>
            new MediaImportLog(Path.Combine(cloudRetroboatPath, "midia", "importacao"), pcName);

        /// <summary>Só deste PC (sem a nuvem: testes e a linha de comando).</summary>
        public static MediaImportLog Local(RetroBatInstall install, string pcName = null) =>
            new MediaImportLog(Path.Combine(install.RetroboatDataPath, "cache", "importacao"), pcName);

        /// <summary>Lista de consultas que o AutoInfo mantinha só neste PC até a importação ir para a nuvem.</summary>
        public static string LegacyInfoFile(RetroBatInstall install) => Path.Combine(install.RetroboatDataPath, "cache", "informacoes", "consultados.txt");

        /// <param name="system">O sistema do ES (ex.: "ps2").</param>
        /// <param name="file">O arquivo do jogo na pasta do sistema.</param>
        public bool WasSearched(MediaImportKind kind, string system, string file) => _all.ContainsKey(Key(kind, system, file));

        /// <summary>Anota a busca (achado ou não). Só uma busca que as fontes responderam: sem internet, tenta de novo depois.</summary>
        public void Record(MediaImportKind kind, string system, string file, bool found)
        {
            var key = Key(kind, system, file);
            var entry = new Entry { When = DateTime.UtcNow, Found = found };
            _all[key] = entry;
            _own[key] = entry;
            _changed = true;
        }

        /// <summary>
        /// Traz para o registro as consultas que o AutoInfo anotava só neste PC ("ps2/Jogo.iso", tab, data) e apaga a lista
        /// antiga depois de gravar: daqui em diante, o registro é um só.
        /// </summary>
        public void ImportLegacy(string legacyInfoFile)
        {
            if (string.IsNullOrEmpty(legacyInfoFile) || !File.Exists(legacyInfoFile))
                return;

            foreach (var line in File.ReadAllLines(legacyInfoFile, Encoding.UTF8))
            {
                var parts = line.Split('\t');
                if (parts.Length < 2 || !TryDate(parts[1], out var when))
                    continue;

                var key = KindName(MediaImportKind.Info) + "\t" + parts[0];
                if (_all.ContainsKey(key))
                    continue;

                // A lista antiga não dizia se achou: conta como achado, que é o caso comum.
                var entry = new Entry { When = when, Found = true };
                _all[key] = entry;
                _own[key] = entry;
                _changed = true;
            }

            _legacy = legacyInfoFile;
        }

        public void Save()
        {
            if (_changed)
            {
                Directory.CreateDirectory(_folder);
                var temp = _ownFile + ".retroboat-part";
                File.WriteAllLines(temp,
                    _own.OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase)
                        .Select(e => e.Key + "\t" + e.Value.When.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture) + "\t" + (e.Value.Found ? "1" : "0")),
                    new UTF8Encoding(false));
                if (File.Exists(_ownFile))
                    File.Delete(_ownFile);
                File.Move(temp, _ownFile);
                _changed = false;
            }

            if (_legacy != null && File.Exists(_legacy))
                File.Delete(_legacy);
            _legacy = null;
        }

        void Read(string line, bool own)
        {
            var parts = line.Split('\t');
            if (parts.Length < 3 || !TryDate(parts[2], out var when))
                return;

            var key = parts[0] + "\t" + parts[1];
            var entry = new Entry { When = when, Found = parts.Length < 4 || parts[3] != "0" };
            if (!_all.TryGetValue(key, out var existing) || existing.When < when)
                _all[key] = entry;

            if (own)
                _own[key] = entry;
        }

        static string Key(MediaImportKind kind, string system, string file) => KindName(kind) + "\t" + system + "/" + file;

        static string KindName(MediaImportKind kind) => kind == MediaImportKind.Images ? "imagens" : "informacoes";

        static bool TryDate(string text, out DateTime when) =>
            DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out when);

        static string SafeFileName(string name)
        {
            foreach (var invalid in Path.GetInvalidFileNameChars())
                name = name.Replace(invalid, '_');

            return name;
        }
    }
}
