using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Retroboat.Bios
{
    public enum BiosState
    {
        Ok,
        Missing,
        /// <summary>O arquivo que o emulador vai usar existe, mas o conteúdo não presta.</summary>
        Corrupt,
        /// <summary>Há uma BIOS válida na pasta, mas com um nome que o emulatorLauncher não procura.</summary>
        Unrecognized
    }

    public sealed class BiosReport
    {
        public BiosSet Set { get; set; }
        public BiosState State { get; set; }
        public string File { get; set; }
        public string Detail { get; set; }

        /// <summary>Frase curta para o usuário, ex.: "PlayStation 2: BIOS corrompida (arquivo zerado)".</summary>
        public string Summary
        {
            get
            {
                switch (State)
                {
                    case BiosState.Ok: return Set.DisplayName + ": BIOS ok (" + Detail + ")";
                    case BiosState.Missing: return Set.DisplayName + ": sem BIOS. Coloque a do seu console em retroboat\\bios\\" + Set.Folder + " no Drive";
                    case BiosState.Corrupt: return Set.DisplayName + ": BIOS corrompida, " + Detail + " (" + File + ")";
                    default: return Set.DisplayName + ": BIOS válida com nome não reconhecido (" + File + "). Renomeie para " + Set.AcceptedNames[0];
                }
            }
        }
    }

    public static class BiosStatus
    {
        /// <summary>Confere, para cada BIOS do catálogo, o arquivo que o emulador vai usar.</summary>
        public static IReadOnlyList<BiosReport> Check(string biosRoot, IEnumerable<BiosSet> sets = null)
        {
            return (sets ?? BiosCatalog.All).Select(set => Check(biosRoot, set)).ToList();
        }

        public static BiosReport Check(string biosRoot, BiosSet set)
        {
            var folder = Path.Combine(biosRoot, set.Folder);
            var report = new BiosReport { Set = set };

            // Mesma escolha do emulatorLauncher: o primeiro nome aceito que existir.
            var chosen = set.AcceptedNames.Select(n => Path.Combine(folder, n)).FirstOrDefault(System.IO.File.Exists);
            if (chosen != null)
            {
                var inspection = set.Inspect(chosen);
                report.File = Path.GetFileName(chosen);
                report.State = inspection.IsValid ? BiosState.Ok : BiosState.Corrupt;
                report.Detail = inspection.IsValid ? inspection.Description : inspection.Problem;
                return report;
            }

            if (Directory.Exists(folder))
            {
                foreach (var file in Directory.GetFiles(folder).Where(f => !set.Owns(Path.GetFileName(f))))
                {
                    var inspection = set.Inspect(file);
                    if (inspection.IsValid)
                    {
                        report.State = BiosState.Unrecognized;
                        report.File = Path.GetFileName(file);
                        report.Detail = inspection.Description;
                        return report;
                    }
                }
            }

            report.State = BiosState.Missing;
            return report;
        }
    }
}
