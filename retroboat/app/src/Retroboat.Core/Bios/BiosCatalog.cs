using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace Retroboat.Bios
{
    /// <summary>O que a inspeção do conteúdo de um arquivo de BIOS encontrou.</summary>
    public sealed class BiosInspection
    {
        public bool IsValid { get; set; }

        /// <summary>Ex.: "v1.60, EUA, 2002-02-07".</summary>
        public string Description { get; set; }

        /// <summary>Ex.: "arquivo zerado". Nulo quando válido.</summary>
        public string Problem { get; set; }
    }

    /// <summary>
    /// Uma BIOS que um emulador do RetroBat procura: pasta (relativa a &lt;RetroBat&gt;\bios), nomes
    /// aceitos na ordem em que o emulatorLauncher procura, e como validar o conteúdo.
    /// </summary>
    public sealed class BiosSet
    {
        public string System { get; set; }
        public string DisplayName { get; set; }
        public string Folder { get; set; }
        public string[] AcceptedNames { get; set; }

        /// <summary>Arquivos que acompanham a BIOS com o mesmo nome base (ex.: .MEC e .NVM do PS2).</summary>
        public string[] CompanionExtensions { get; set; } = new string[0];

        public Func<string, BiosInspection> Inspect { get; set; }

        public bool Accepts(string fileName) => AcceptedNames.Contains(fileName, StringComparer.OrdinalIgnoreCase);

        /// <summary>A BIOS em si ou um arquivo que a acompanha.</summary>
        public bool Owns(string fileName)
        {
            if (Accepts(fileName))
                return true;

            var stem = Path.GetFileNameWithoutExtension(fileName);
            var extension = Path.GetExtension(fileName);
            return CompanionExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase)
                && AcceptedNames.Any(a => string.Equals(Path.GetFileNameWithoutExtension(a), stem, StringComparison.OrdinalIgnoreCase));
        }
    }

    public static class BiosCatalog
    {
        /// <summary>
        /// PS2 no PCSX2 (Qt): o emulatorLauncher procura em bios\pcsx2\bios, usa ps2-0230a-20080220.bin se
        /// existir e senão o primeiro desta lista que existir (Pcsx2.Generator.cs, SetupConfigurationQT).
        /// </summary>
        public static readonly BiosSet Ps2 = new BiosSet
        {
            System = "ps2",
            DisplayName = "PlayStation 2",
            Folder = @"pcsx2\bios",
            AcceptedNames = new[]
            {
                "ps2-0230a-20080220.bin", "ps2-0230e-20080220.bin", "ps2-0250e-20100415.bin", "ps2-0230j-20080220.bin",
                "ps3_ps2_emu_bios.bin", "SCPH30004R.bin", "scph39001.bin", "SCPH-39004_BIOS_V7_EUR_160.BIN",
                "SCPH-39001_BIOS_V7_USA_160.BIN", "SCPH-70000_BIOS_V12_JAP_200.BIN"
            },
            CompanionExtensions = new[] { ".MEC", ".NVM", ".ROM1", ".ROM2", ".EROM" },
            Inspect = Ps2Bios.Inspect
        };

        /// <summary>
        /// PS1 nos cores do RetroArch: a BIOS fica solta em bios\. A lista do RetroBat só tem a psxonpsp660.bin.
        /// </summary>
        public static readonly BiosSet Ps1 = new BiosSet
        {
            System = "psx",
            DisplayName = "PlayStation",
            Folder = "",
            AcceptedNames = new[] { "scph5500.bin", "scph5501.bin", "scph5502.bin", "scph1001.bin", "scph7001.bin", "scph101.bin", "psxonpsp660.bin" }
        };

        /// <summary>As BIOS com conferência do conteúdo (status no ES).</summary>
        public static readonly IReadOnlyList<BiosSet> All = new[] { Ps2 };

        /// <summary>Lista de BIOS de cada sistema que vem com o RetroBat, relativa à pasta dele.</summary>
        public const string RetroBatListPath = @"system\modules\rb_gui\bios_local.json";

        /// <summary>
        /// Arquivos da lista que vêm livres com o RetroBat (o pacote retrobat-bios), em todo PC: não sobem. As listas de
        /// software do MAME (mame\hash) também vêm, e saem pela pasta.
        /// </summary>
        static readonly HashSet<string> FreeFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ecwolf.pk3", "freej2me-lr.jar", "emutos.img" };

        /// <summary>
        /// As BIOS que a sincronização organiza e envia: PS2 e PS1 daqui mais a lista do RetroBat (uns 100 sistemas). Sem a
        /// lista, só PS2 e PS1. Baixar não depende disto: tudo o que está em bios\ na nuvem vem.
        /// </summary>
        public static IReadOnlyList<BiosSet> ForSync(string retrobatRoot)
        {
            var sets = new List<BiosSet> { Ps2, Ps1 };
            var path = retrobatRoot == null ? null : Path.Combine(retrobatRoot, RetroBatListPath);
            try
            {
                if (path != null && File.Exists(path))
                    sets.AddRange(ReadRetroBatList(File.ReadAllText(path)));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is InvalidOperationException)
            {
                // Lista ilegível: segue com PS2 e PS1.
            }

            return sets;
        }

        /// <summary>
        /// Um BiosSet por pasta de cada sistema do bios_local.json ({"psx": {"name": ..., "biosFiles": [{"file": "bios/..."}]}}).
        /// Ficam de fora o PS2 (o daqui confere o conteúdo), o que não fica em bios\ e o que vem livre com o RetroBat.
        /// </summary>
        internal static List<BiosSet> ReadRetroBatList(string json)
        {
            var sets = new List<BiosSet>();
            var root = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Deserialize<Dictionary<string, object>>(json);

            foreach (var system in root)
            {
                if (system.Key == Ps2.System || !(system.Value is IDictionary<string, object> info) || !info.TryGetValue("biosFiles", out var files) || !(files is IEnumerable list))
                    continue;

                var paths = list.OfType<IDictionary<string, object>>()
                    .Select(f => f.TryGetValue("file", out var file) ? file as string : null)
                    .Where(p => p != null && p.StartsWith("bios/", StringComparison.OrdinalIgnoreCase) && !p.StartsWith("bios/mame/hash/", StringComparison.OrdinalIgnoreCase))
                    .Select(p => p.Substring("bios/".Length).Replace('/', '\\'))
                    .Where(p => !FreeFiles.Contains(Path.GetFileName(p)));

                foreach (var folder in paths.GroupBy(Path.GetDirectoryName, StringComparer.OrdinalIgnoreCase))
                {
                    sets.Add(new BiosSet
                    {
                        System = system.Key,
                        DisplayName = info.TryGetValue("name", out var name) && name is string text ? text : system.Key,
                        Folder = folder.Key,
                        AcceptedNames = folder.Select(Path.GetFileName).ToArray()
                    });
                }
            }

            return sets;
        }
    }
}
