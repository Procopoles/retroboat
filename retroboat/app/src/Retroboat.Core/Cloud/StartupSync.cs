using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Retroboat.Accounts;
using Retroboat.Bios;
using Retroboat.Config;
using Retroboat.Library;
using Retroboat.Media;
using Retroboat.Saves;

namespace Retroboat.Cloud
{
    public sealed class StartupSyncReport
    {
        public CloudLocation Cloud { get; set; }
        public CloudFoldersResult Folders { get; set; }
        public BiosSyncResult Bios { get; set; }
        public CloudLibrarySyncResult Library { get; set; }
        public MediaRelocationResult Relocation { get; set; }
        public CloudMediaSyncResult Media { get; set; }
        public AutoMediaResult AutoMedia { get; set; }
        public AutoInfoResult AutoInfo { get; set; }
        public AccountSyncResult Accounts { get; set; }
        public List<ConfigSyncResult> Configs { get; set; } = new List<ConfigSyncResult>();
        public SaveSyncResult Saves { get; set; }
        public IReadOnlyList<BiosReport> BiosStatus { get; set; } = new List<BiosReport>();

        /// <summary>Frases curtas para a interface (notificação do ES, janela do lançador).</summary>
        public List<string> Messages { get; } = new List<string>();

        /// <summary>Há algo que o usuário precisa resolver.</summary>
        public bool HasWarnings { get; set; }
    }

    /// <summary>
    /// O que o Retroboat sincroniza com a nuvem antes de abrir o RetroBat: BIOS, contas, configurações (opções do ES,
    /// controles, emuladores), memory cards (o que ficou de uma sessão offline) e a biblioteca de jogos (marcadores
    /// dos jogos que ainda não foram baixados).
    /// </summary>
    public static class StartupSync
    {
        public static StartupSyncReport Run(RetroBatInstall install, string configuredCloudPath, string romsPath, CloudMediaOptions mediaOptions = null,
                                            IThumbnailSource thumbnails = null, IReadOnlyList<IGameInfoSource> infoSources = null)
        {
            var report = new StartupSyncReport { Cloud = CloudLocator.Locate(configuredCloudPath) };
            var localBios = Path.Combine(install.Root, "bios");

            // Pastas do Drive no formato do RetroBat (jogos\psx, jogos\n64...): a pessoa só solta o jogo na pasta do
            // console. Só dentro de um retroboat\jogos que já existe.
            report.Folders = CloudFolders.Ensure(install, report.Cloud);
            if (report.Folders.SystemsCreated.Count > 0)
                report.Messages.Add("Nuvem: " + report.Folders.SystemsCreated.Count + " pasta(s) de console criada(s) em retroboat\\jogos");
            if (report.Folders.Errors.Count > 0)
            {
                report.Messages.Add("Atenção: não foi possível criar " + report.Folders.Errors.Count + " pasta(s) no Drive. Detalhes no log do Retroboat.");
                report.HasWarnings = true;
            }

            // Ícone de nuvem do cabeçalho do tema.
            CloudStatusFile.Write(install, report.Cloud != null && report.Cloud.Exists);

            // Mídia que o scraper do ES gravou dentro da pasta de jogos vai para .emulationstation\media (item 1c).
            if (!string.IsNullOrEmpty(romsPath))
                report.Relocation = new MediaRelocator(install).Apply(romsPath);

            if (report.Cloud == null || !report.Cloud.Exists)
            {
                report.Messages.Add(GoogleDriveApp.GetState(configuredCloudPath) == CloudAppState.Closed
                    ? "Nuvem: Google Drive fechado. Usando só o que está neste PC."
                    : "Nuvem: Google Drive para computador não encontrado. Usando só o que está neste PC.");
                report.HasWarnings = true;

                // Os jogos da nuvem continuam no ES; os gamelists acompanham o índice deste PC.
                try
                {
                    CloudLibrarySync.RefreshGamelists(install);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Xml.XmlException)
                {
                    report.Messages.Add("Atenção: não foi possível atualizar a lista de jogos (" + ex.Message + ").");
                }
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

                // Logins do ES (ScreenScraper, IGDB, RetroAchievements): quem não tem login recebe o da nuvem.
                // Com o ES aberto, não: ele regrava o es_settings.cfg ao sair e desfaria a troca.
                if (System.Diagnostics.Process.GetProcessesByName("emulationstation").Length == 0)
                {
                    try
                    {
                        report.Accounts = new AccountSync(install, report.Cloud.RetroboatPath).Apply();
                        if (report.Accounts.Received.Count > 0)
                            report.Messages.Add("Contas da nuvem: " + string.Join(", ", report.Accounts.Received) + " neste PC");
                        if (report.Accounts.Sent.Count > 0)
                            report.Messages.Add("Contas enviadas para a nuvem: " + string.Join(", ", report.Accounts.Sent));
                    }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Xml.XmlException || ex is FormatException)
                    {
                        report.Messages.Add("Atenção: não foi possível sincronizar as contas (" + ex.Message + ").");
                        report.HasWarnings = true;
                    }
                }

                // Opções do ES (globais, por sistema e por jogo), controles e configurações dos emuladores. Com o ES ou o
                // emulador aberto, aquela parte só envia: o que veio da nuvem espera ele fechar.
                report.Configs = CloudConfigs.SyncAll(install, report.Cloud.RetroboatPath);
                report.Messages.AddRange(CloudConfigs.Describe(report.Configs));
                if (CloudConfigs.HasErrors(report.Configs))
                    report.HasWarnings = true;

                // Memory cards: envia o que ficou de uma partida offline e baixa o que chegou. Conflitos ficam para
                // a abertura do jogo, onde dá para perguntar. Com um jogo aberto neste PC, não mexe nos cartões.
                if (System.Diagnostics.Process.GetProcessesByName("retroboat-launch").Length == 0)
                    report.Saves = SyncSaves(install, report.Cloud.RetroboatPath, report);

                if (!string.IsNullOrEmpty(romsPath) && Directory.Exists(report.Cloud.GamesPath))
                {
                    report.Library = new CloudLibrarySync(install, romsPath, new CloudLibrary(report.Cloud.GamesPath)).Apply();
                    var library = report.Library;
                    if (library.InCloud + library.Installed + library.Corrupt > 0)
                    {
                        report.Messages.Add("Biblioteca na nuvem: " + library.Installed + " instalado(s), " + library.InCloud + " para baixar" +
                                            (library.Corrupt > 0 ? ", " + library.Corrupt + " corrompido(s)" : ""));
                        if (library.Corrupt > 0)
                            report.HasWarnings = true;
                    }

                    report.Media = new CloudMediaSync(install, romsPath, report.Cloud.RetroboatPath, options: mediaOptions).Apply();
                    var media = report.Media;
                    if (media.GamesFilled > 0 || media.GamesShared > 0)
                    {
                        var parts = new List<string>();
                        if (media.GamesFilled > 0) parts.Add(media.GamesFilled + " jogo(s) recebidos");
                        if (media.GamesShared > 0) parts.Add(media.GamesShared + " enviado(s)");
                        report.Messages.Add("Capas e descrições da nuvem: " + string.Join(", ", parts));
                    }

                    if (media.Errors.Count > 0)
                    {
                        report.Messages.Add("Atenção: problema ao sincronizar capas e descrições. Detalhes no log do Retroboat.");
                        report.HasWarnings = true;
                    }
                }
            }

            // Capas e informações automáticas para o que nem este PC nem a nuvem têm. O que vier vai para o Drive na hora.
            if (mediaOptions != null && mediaOptions.Automatic && !string.IsNullOrEmpty(romsPath))
            {
                var source = thumbnails ?? new LibretroThumbnails(Path.Combine(install.RetroboatDataPath, "cache", "libretro"));
                report.AutoMedia = new AutoMedia(install, romsPath, source).Apply();
                if (report.AutoMedia.GamesFilled > 0)
                    report.Messages.Add("Capas automáticas: " + report.AutoMedia.GamesFilled + " jogo(s)");

                report.AutoInfo = new AutoInfo(install, infoSources ?? AutoInfo.DefaultSources(install)).Apply();
                if (report.AutoInfo.GamesFilled > 0)
                    report.Messages.Add("Informações automáticas: " + report.AutoInfo.GamesFilled + " jogo(s)");

                if (report.AutoMedia.GamesFilled + report.AutoInfo.GamesFilled > 0)
                {
                    if (report.Cloud != null && report.Cloud.Exists)
                    {
                        var shared = new CloudMediaSync(install, romsPath, report.Cloud.RetroboatPath, options: mediaOptions).Apply();
                        report.Media = report.Media ?? shared;
                        if (report.Media != shared)
                        {
                            report.Media.GamesShared += shared.GamesShared;
                            report.Media.FilesUploaded += shared.FilesUploaded;
                            report.Media.Errors.AddRange(shared.Errors);
                        }
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

        static SaveSyncResult SyncSaves(RetroBatInstall install, string cloudRetroboatPath, StartupSyncReport report)
        {
            var total = new SaveSyncResult();
            var savesPath = SaveRules.GetSavesPath(install);
            foreach (var rule in SaveRules.AllFor(savesPath, Path.Combine(cloudRetroboatPath, "saves")))
            {
                try
                {
                    var result = new SaveSync(savesPath, install.RetroboatDataPath, cloudRetroboatPath, rule).Sync();
                    total.Add(result);

                    var parts = new List<string>();
                    if (result.Uploaded.Count > 0) parts.Add(result.Uploaded.Count + " enviado(s)");
                    if (result.Downloaded.Count > 0) parts.Add(result.Downloaded.Count + " baixado(s)");
                    if (result.DeletedLocal.Count + result.DeletedInCloud.Count > 0) parts.Add(result.DeletedLocal.Count + result.DeletedInCloud.Count + " apagado(s)");
                    if (parts.Count > 0)
                        report.Messages.Add(rule.DisplayName + ": " + rule.ItemName + "s sincronizados (" + string.Join(", ", parts) + ")");

                    foreach (var repaired in result.Repaired)
                    {
                        report.Messages.Add("Atenção: " + rule.DisplayName + ": " + repaired);
                        report.HasWarnings = true;
                    }

                    if (result.DamagedHere.Count > 0)
                    {
                        report.Messages.Add("Atenção: " + rule.DisplayName + ": " + string.Join("; ", result.DamagedHere) +
                                            ". As cópias anteriores podem voltar com retroboat-cli saves restore.");
                        report.HasWarnings = true;
                    }

                    if (result.Conflicts.Count > 0)
                    {
                        report.Messages.Add("Atenção: " + rule.DisplayName + ": " + string.Join(", ", result.Conflicts) +
                                            " mudou neste PC e na nuvem. Você escolhe qual manter ao abrir um jogo.");
                        report.HasWarnings = true;
                    }

                    if (result.Errors.Count > 0)
                    {
                        report.Messages.Add("Atenção: " + rule.DisplayName + ": problema ao sincronizar " + rule.ItemName + "s. Detalhes no log do Retroboat.");
                        report.HasWarnings = true;
                    }
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Xml.XmlException)
                {
                    total.Errors.Add(rule.DisplayName + ": " + ex.Message);
                    report.Messages.Add("Atenção: não foi possível sincronizar os " + rule.ItemName + "s (" + ex.Message + ").");
                    report.HasWarnings = true;
                }
            }

            return total;
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
