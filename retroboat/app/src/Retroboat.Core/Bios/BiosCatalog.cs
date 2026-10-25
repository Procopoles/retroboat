using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

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

        public static readonly IReadOnlyList<BiosSet> All = new[] { Ps2 };
    }
}
