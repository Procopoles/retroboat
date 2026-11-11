using System;
using System.Collections.Generic;
using Retroboat.Library;

namespace Retroboat.Emulators
{
    public enum PackageState
    {
        /// <summary>O programa (ou o core) não está neste PC.</summary>
        NotInstalled,

        /// <summary>Instalado, e o repositório não tem nada mais novo (ou não deu para saber).</summary>
        Current,

        /// <summary>O repositório tem uma versão mais nova: ela é instalada antes do próximo jogo.</summary>
        UpdateAvailable
    }

    /// <summary>
    /// Onde um pacote está: instalado ou não, e se o repositório tem algo mais novo. A regra é ficar sempre na última versão
    /// (RB-033). Sem internet, um pacote instalado conta como em dia.
    /// </summary>
    public sealed class PackageStatus
    {
        public EmulatorPackage Package { get; set; }
        public PackageState State { get; set; }

        /// <summary>A versão instalada, se dá para saber.</summary>
        public string InstalledVersion { get; set; }

        /// <summary>A versão do repositório (versions.xml); vazia para cores.</summary>
        public string AvailableVersion { get; set; }

        /// <summary>O pacote no servidor; nulo se não deu para consultar.</summary>
        public RemoteFile Remote { get; set; }

        /// <summary>O repositório tem este pacote. Falso também quando não deu para consultar e ele não está instalado.</summary>
        public bool InRepository { get; set; }

        /// <summary>Por que não deu para consultar o repositório, se não deu.</summary>
        public string Problem { get; set; }

        public bool NeedsDownload => InRepository && State != PackageState.Current;

        /// <summary>Tamanho do download, se o servidor informou.</summary>
        public long DownloadBytes => Remote != null && Remote.Exists && Remote.Length > 0 ? Remote.Length : 0;

        /// <summary>Consulta (com o cache de <see cref="RepositoryCache"/>). Não lança exceção por falta de internet.</summary>
        public static PackageStatus Of(RetroBatInstall install, EmulatorPackage package, RepositoryCache server)
        {
            var status = new PackageStatus { Package = package };
            var main = package.MainFile(install);
            var installed = new InstalledEmulators(install);
            var record = installed.Find(package);
            bool isInstalled = main != null;

            if (package.Kind == PackageKind.Emulator)
            {
                var versions = server.Versions();
                string available = null;
                if (versions != null)
                    versions.TryGetValue(package.Name, out available);

                status.AvailableVersion = available ?? "";
                status.InstalledVersion = isInstalled ? VersionInstalled(record, main) : "";

                if (!isInstalled)
                {
                    status.State = PackageState.NotInstalled;
                    if (available != null || versions == null)
                        status.Remote = server.Probe(package);
                }
                else
                {
                    // O registro diz o que o repositório anunciava quando o Retroboat instalou: o número do executável nem
                    // sempre segue o do versions.xml, e sem isso um emulador seria atualizado a cada jogo.
                    var local = EmulatorVersions.Parse(record != null && record.Version.Length > 0 ? record.Version : status.InstalledVersion);
                    bool newer = EmulatorVersions.IsNewer(EmulatorVersions.Parse(available), local);
                    status.State = newer ? PackageState.UpdateAvailable : PackageState.Current;
                    if (newer)
                        status.Remote = server.Probe(package);
                }

                status.InRepository = available != null && (status.Remote == null || status.Remote.Exists);
            }
            else
            {
                status.Remote = server.Probe(package);
                status.InRepository = status.Remote != null && status.Remote.Exists;
                status.InstalledVersion = "";
                status.AvailableVersion = "";

                if (!isInstalled)
                {
                    status.State = PackageState.NotInstalled;
                }
                else if (record == null || !record.ServerModifiedUtc.HasValue)
                {
                    // Um core que o RetroBat instalou antes: fica como está, e as próximas mudanças no servidor contam.
                    status.State = PackageState.Current;
                    if (status.Remote != null && status.Remote.Exists)
                        installed.Save(new InstalledRecord { Key = package.Key, Version = "", Length = status.Remote.Length, ServerModifiedUtc = status.Remote.ModifiedUtc, InstalledUtc = DateTime.UtcNow, Adopted = true });
                }
                else
                {
                    bool newer = status.Remote != null && status.Remote.Exists && status.Remote.ModifiedUtc.HasValue
                        && status.Remote.ModifiedUtc.Value > record.ServerModifiedUtc.Value.AddMinutes(1);
                    status.State = newer ? PackageState.UpdateAvailable : PackageState.Current;
                }
            }

            status.Problem = server.Problem;
            return status;
        }

        static string VersionInstalled(InstalledRecord record, string mainFile)
        {
            var fromFile = EmulatorVersions.Display(EmulatorVersions.OfFile(mainFile));
            return record != null && record.Version.Length > 0 ? record.Version : fromFile;
        }

        public override string ToString()
        {
            var parts = new List<string> { Package.DisplayName, State.ToString() };
            if (!string.IsNullOrEmpty(InstalledVersion)) parts.Add("instalada " + InstalledVersion);
            if (!string.IsNullOrEmpty(AvailableVersion)) parts.Add("repositório " + AvailableVersion);
            if (DownloadBytes > 0) parts.Add(DownloadBytes + " bytes");
            return string.Join(", ", parts);
        }
    }
}
