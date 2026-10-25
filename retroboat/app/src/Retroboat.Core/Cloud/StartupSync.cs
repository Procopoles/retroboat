using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Retroboat.Bios;
using Retroboat.Library;

namespace Retroboat.Cloud
{
    public sealed class StartupSyncReport
    {
        public CloudLocation Cloud { get; set; }
        public BiosSyncResult Bios { get; set; }
        public CloudLibrarySyncResult Library { get; set; }
        public IReadOnlyList<BiosReport> BiosStatus { get; set; } = new List<BiosReport>();

        /// <summary>Frases curtas para a interface (notificação do ES, janela do lançador).</summary>
        public List<string> Messages { get; } = new List<string>();

        /// <summary>Há algo que o usuário precisa resolver.</summary>
        public bool HasWarnings { get; set; }
    }

    /// <summary>
    /// O que o Retroboat sincroniza com a nuvem antes de abrir o RetroBat: BIOS e a biblioteca de jogos
    /// (marcadores dos jogos que ainda não foram baixados).
    /// </summary>
    public static class StartupSync
    {
        public static StartupSyncReport Run(RetroBatInstall install, string configuredCloudPath, string romsPath)
        {
            var report = new StartupSyncReport { Cloud = CloudLocator.Locate(configuredCloudPath) };
            var localBios = Path.Combine(install.Root, "bios");

            if (report.Cloud == null || !report.Cloud.Exists)
            {
                report.Messages.Add("Nuvem: Google Drive para computador não encontrado. Usando só o que está neste PC.");
                report.HasWarnings = true;
            }
            else
            {
                report.Bios = new BiosSync(report.Cloud.RetroboatPath, localBios).Sync();
                report.Messages.Add(DescribeSync(report.Bios));

                if (report.Bios.Errors.Count > 0)
                {
                    report.Messages.Add("Atenção: " + report.Bios.Errors.Count + " problema(s) ao sincronizar BIOS. Detalhes no log do Retroboat.");
                    report.HasWarnings = true;
                }

                if (!string.IsNullOrEmpty(romsPath) && Directory.Exists(report.Cloud.GamesPath))
                {
                    report.Library = new CloudLibrarySync(install, romsPath, new CloudLibrary(report.Cloud.GamesPath)).Apply();
                    var library = report.Library;
                    if (library.InCloud + library.Installed + library.Corrupt > 0)
                    {
                        report.Messages.Add("Biblioteca na nuvem: " + library.Installed + " instalado(s), " + library.InCloud + " para baixar ao jogar" +
                                            (library.Corrupt > 0 ? ", " + library.Corrupt + " corrompido(s)" : ""));
                        if (library.Corrupt > 0)
                            report.HasWarnings = true;
                    }
                }
            }

            // Só avisa sobre BIOS de sistemas que têm jogos, na nuvem ou neste PC.
            var systems = SystemsWithGames(report.Cloud, romsPath);
            report.BiosStatus = BiosStatus.Check(localBios, BiosCatalog.All.Where(s => systems.Contains(s.System)));
            var corrupt = report.Bios?.SkippedCorrupt.ToList() ?? new List<CorruptFile>();
            foreach (var bios in report.BiosStatus)
            {
                if (bios.State == BiosState.Ok)
                {
                    report.Messages.Add(bios.Summary);
                    continue;
                }

                // "Sem BIOS" porque a do Drive está zerada: o aviso precisa dizer isso, senão parece que nada foi enviado.
                var zeroedInCloud = corrupt.FirstOrDefault(c => c.InCloud && bios.Set.Accepts(Path.GetFileName(c.RelativePath)));
                if (bios.State == BiosState.Missing && zeroedInCloud != null)
                {
                    report.Messages.Add("Atenção: " + bios.Set.DisplayName + ": a BIOS do Drive (" + Path.GetFileName(zeroedInCloud.RelativePath) +
                                        ") está zerada, sem conteúdo. Coloque uma cópia válida em retroboat\\bios\\" + bios.Set.Folder + ".");
                    corrupt.RemoveAll(c => c.InCloud && bios.Set.Owns(Path.GetFileName(c.RelativePath)));
                }
                else
                {
                    report.Messages.Add("Atenção: " + bios.Summary);
                }

                report.HasWarnings = true;
            }

            if (corrupt.Count > 0)
            {
                report.Messages.Add("Atenção: arquivo(s) de BIOS zerado(s), não copiados: " + string.Join(", ", corrupt.Select(c => Path.GetFileName(c.RelativePath))));
                report.HasWarnings = true;
            }

            return report;
        }

        static string DescribeSync(BiosSyncResult result)
        {
            if (!result.HasTransfers)
                return "Nuvem: BIOS em dia";

            var parts = new List<string>();
            if (result.Downloaded.Count > 0) parts.Add(result.Downloaded.Count + " baixada(s)");
            if (result.Uploaded.Count > 0) parts.Add(result.Uploaded.Count + " enviada(s)");
            if (result.Organized.Count > 0) parts.Add(result.Organized.Count + " organizada(s) no Drive");
            return "Nuvem: BIOS sincronizadas (" + string.Join(", ", parts) + ")";
        }

        static HashSet<string> SystemsWithGames(CloudLocation cloud, string romsPath)
        {
            var systems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (cloud != null && Directory.Exists(cloud.GamesPath))
            {
                foreach (var game in new CloudLibrary(cloud.GamesPath).GetGames())
                    systems.Add(game.System);
            }

            if (!string.IsNullOrEmpty(romsPath) && Directory.Exists(romsPath))
            {
                foreach (var dir in Directory.GetDirectories(romsPath))
                {
                    if (Directory.EnumerateFiles(dir).Any(f => !Path.GetFileName(f).Equals("_info.txt", StringComparison.OrdinalIgnoreCase)
                                                             && !Path.GetFileName(f).Equals("gamelist.xml", StringComparison.OrdinalIgnoreCase)))
                        systems.Add(Path.GetFileName(dir));
                }
            }

            return systems;
        }
    }
}
