using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Xml;
using System.Xml.Linq;
using Retroboat.Library;
using Retroboat.Transfers;

namespace Retroboat.Emulators
{
    public sealed class EmulatorInstallResult
    {
        public bool Installed { get; set; }
        public bool Cancelled { get; set; }

        /// <summary>Por que não instalou, em português (sem internet, pacote corrompido...).</summary>
        public string Error { get; set; }

        /// <summary>A versão que ficou instalada.</summary>
        public string Version { get; set; }

        /// <summary>O que a verificação de compatibilidade achou depois de instalar.</summary>
        public IReadOnlyList<CompatIssue> Issues { get; set; } = new CompatIssue[0];

        public static EmulatorInstallResult Failed(string error) => new EmulatorInstallResult { Error = error };
    }

    /// <summary>
    /// Instala ou atualiza um pacote do repositório do RetroBat (RB-033): baixa (continuando um download pela metade), confere o
    /// arquivo (<see cref="PackageCheck"/>), extrai numa pasta temporária, põe no lugar sem trocar as configurações que o
    /// RetroBat nunca sobrescreve, registra e roda a verificação de compatibilidade. Qualquer falha antes de pôr no lugar deixa
    /// a pasta do emulador como estava. Um pacote por vez (outro processo que pedir o mesmo espera e encontra pronto).
    /// </summary>
    public sealed class EmulatorInstaller
    {
        public const string TempFolderName = ".retroboat-temp";
        const string PartSuffix = ".retroboat-part";

        readonly RetroBatInstall _install;
        readonly RepositoryCache _server;
        readonly SevenZip _sevenZip;

        public EmulatorInstaller(RetroBatInstall install, RepositoryCache server, SevenZip sevenZip = null)
        {
            _install = install;
            _server = server;
            _sevenZip = sevenZip ?? SevenZip.For(install);
        }

        public EmulatorInstallResult Install(PackageStatus status, IProgress<TransferProgress> progress, CancellationToken token)
        {
            var package = status.Package;
            using (var mutex = new Mutex(false, @"Local\RetroboatEmulator-" + package.Key.Replace('/', '-')))
            {
                try
                {
                    while (!mutex.WaitOne(200))
                        token.ThrowIfCancellationRequested();
                }
                catch (AbandonedMutexException)
                {
                    // Outro processo foi encerrado no meio: a vez é deste, e a pasta temporária dele é descartada abaixo.
                }
                catch (OperationCanceledException)
                {
                    return new EmulatorInstallResult { Cancelled = true };
                }

                try
                {
                    // Enquanto esperava, outro processo pode ter instalado.
                    var now = PackageStatus.Of(_install, package, _server);
                    if (!now.NeedsDownload && now.State != PackageState.NotInstalled)
                        return new EmulatorInstallResult { Installed = true, Version = now.InstalledVersion, Issues = CompatAfterInstall(package, now.InstalledVersion) };

                    return Run(now.Remote != null ? now : status, progress, token);
                }
                catch (OperationCanceledException)
                {
                    EmulatorLog.Info(_install, package.DisplayName + ": download cancelado; o pedaço baixado fica para continuar depois.");
                    return new EmulatorInstallResult { Cancelled = true };
                }
                catch (RepositoryException ex)
                {
                    EmulatorLog.Warn(_install, package.DisplayName + ": " + ex.Message);
                    return EmulatorInstallResult.Failed(ex.Message);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    EmulatorLog.Error(_install, package.DisplayName + ": falha ao instalar", ex);
                    return EmulatorInstallResult.Failed(ex.Message);
                }
                finally
                {
                    mutex.ReleaseMutex();
                }
            }
        }

        EmulatorInstallResult Run(PackageStatus status, IProgress<TransferProgress> progress, CancellationToken token)
        {
            var package = status.Package;
            bool update = status.State == PackageState.UpdateAvailable;
            var source = _server.Source.Describe(package);
            EmulatorLog.Info(_install, package.DisplayName + ": " + (update ? "atualizando " + status.InstalledVersion + " → " + status.AvailableVersion : "instalando " + status.AvailableVersion).TrimEnd()
                + " de " + source + (status.DownloadBytes > 0 ? " (" + status.DownloadBytes + " bytes)" : ""));

            var temp = Path.Combine(_install.Root, "emulators", TempFolderName, package.Key.Replace('/', '_'));
            var archive = Path.Combine(temp, package.ArchiveName);
            var files = Path.Combine(temp, "arquivos");

            // O pedaço de uma tentativa anterior só serve se é do mesmo pacote do servidor (mesmo tamanho).
            var part = archive + "." + status.DownloadBytes + PartSuffix;
            Directory.CreateDirectory(temp);
            DeleteFolder(files);
            foreach (var old in Directory.GetFiles(temp).Where(f => !string.Equals(f, part, StringComparison.OrdinalIgnoreCase)))
                File.Delete(old);

            // 1. Download, continuando o que ficou de uma tentativa anterior.
            _server.Source.Download(package, part, status.DownloadBytes, progress, token);

            var lengthProblem = PackageCheck.Length(part, status.DownloadBytes);
            if (lengthProblem != null)
                return Reject(package, temp, lengthProblem);

            File.Move(part, archive);

            // 2. Conferência do pacote e extração na pasta temporária.
            var archiveProblem = PackageCheck.Archive(_sevenZip, archive, token);
            if (archiveProblem != null)
                return Reject(package, temp, archiveProblem);

            var extractProblem = _sevenZip.Extract(archive, files, token);
            if (extractProblem != null)
                return Reject(package, temp, "não deu para extrair o pacote (" + extractProblem + ")");

            var contentProblem = PackageCheck.Extracted(files, package);
            if (contentProblem != null)
                return Reject(package, temp, contentProblem);

            token.ThrowIfCancellationRequested();

            // 3. No lugar: a partir daqui não cancela, para a pasta não ficar pela metade.
            var destination = package.LocalFolder(_install);
            var kept = PlaceFiles(package, files, destination);

            var version = package.Kind == PackageKind.Emulator && status.AvailableVersion.Length > 0
                ? status.AvailableVersion
                : EmulatorVersions.Display(EmulatorVersions.OfFile(package.MainFile(_install)));
            new InstalledEmulators(_install).Save(new InstalledRecord
            {
                Key = package.Key,
                Version = package.Kind == PackageKind.Emulator ? version : "",
                Length = new FileInfo(archive).Length,
                ServerModifiedUtc = status.Remote?.ModifiedUtc,
                InstalledUtc = DateTime.UtcNow
            });
            _server.Forget(package);
            DeleteFolder(temp);
            DeleteIfEmpty(Path.GetDirectoryName(temp));

            EmulatorLog.Info(_install, package.DisplayName + " " + version + ": " + (update ? "atualizado" : "instalado") + " em " + destination
                + " (pacote conferido" + (kept > 0 ? "; " + kept + " arquivo(s) de configuração do usuário mantidos" : "") + ").");

            return new EmulatorInstallResult { Installed = true, Version = version, Issues = CompatAfterInstall(package, version) };
        }

        IReadOnlyList<CompatIssue> CompatAfterInstall(EmulatorPackage package, string version)
        {
            var issues = EmulatorCompatibility.CheckInstalled(_install, package, version);
            var repository = EmulatorCompatibility.CheckRepository(_server, package, version);
            if (repository != null)
                issues.Add(repository);

            EmulatorCompatibility.Log(_install, issues, "depois de instalar");
            return issues;
        }

        EmulatorInstallResult Reject(EmulatorPackage package, string temp, string problem)
        {
            EmulatorLog.Warn(_install, package.DisplayName + ": pacote recusado na conferência: " + problem + ". Nada foi trocado na pasta do emulador.");
            DeleteFolder(temp);
            DeleteIfEmpty(Path.GetDirectoryName(temp));
            return EmulatorInstallResult.Failed(problem);
        }

        static void DeleteIfEmpty(string folder)
        {
            try
            {
                if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any())
                    Directory.Delete(folder);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
            }
        }

        /// <summary>
        /// Copia por cima, menos os arquivos que o RetroBat nunca sobrescreve (system\upgrade.xml, &lt;neveroverwrite&gt;) e que já
        /// existem: são as configurações do usuário. Um core é só o .dll (e o que vier junto) na pasta cores.
        /// </summary>
        /// <returns>Quantos arquivos do usuário ficaram como estavam.</returns>
        int PlaceFiles(EmulatorPackage package, string files, string destination)
        {
            var protectedFiles = NeverOverwrite(package);
            int kept = 0;
            foreach (var file in Directory.GetFiles(files, "*", SearchOption.AllDirectories))
            {
                var relative = file.Substring(files.Length).TrimStart('\\');
                if (package.Kind == PackageKind.Core)
                    relative = Path.GetFileName(relative);

                var target = Path.Combine(destination, relative);
                if (File.Exists(target) && protectedFiles.Contains(relative))
                {
                    kept++;
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(target));
                try
                {
                    File.Copy(file, target, overwrite: true);
                }
                catch (IOException ex) when (File.Exists(target))
                {
                    throw new IOException("o " + package.DisplayName + " parece estar aberto (" + Path.GetFileName(target) + " em uso); feche-o e tente de novo", ex);
                }
            }

            return kept;
        }

        /// <summary>Caminhos relativos à pasta do pacote que o RetroBat nunca sobrescreve numa atualização.</summary>
        ISet<string> NeverOverwrite(EmulatorPackage package)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var upgrade = Path.Combine(_install.Root, "system", "upgrade.xml");
            if (package.Kind == PackageKind.Core || !File.Exists(upgrade))
                return result;

            try
            {
                var prefix = "emulators\\" + package.Name + "\\";
                foreach (var element in XDocument.Load(upgrade).Root.Elements("neveroverwrite"))
                {
                    var path = ((string)element.Attribute("path") ?? "").Replace('/', '\\');
                    if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        result.Add(path.Substring(prefix.Length));
                }
            }
            catch (Exception ex) when (ex is XmlException || ex is IOException || ex is UnauthorizedAccessException)
            {
                EmulatorLog.Warn(_install, "system\\upgrade.xml ilegível; a atualização não protege as configurações do usuário: " + ex.Message);
            }

            return result;
        }

        static void DeleteFolder(string folder)
        {
            try
            {
                if (Directory.Exists(folder))
                    Directory.Delete(folder, recursive: true);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
            }
        }
    }
}
