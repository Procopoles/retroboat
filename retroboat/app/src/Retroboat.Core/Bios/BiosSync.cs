using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Retroboat.Integrity;

namespace Retroboat.Bios
{
    public sealed class CorruptFile
    {
        public CorruptFile(string relativePath, bool inCloud)
        {
            RelativePath = relativePath;
            InCloud = inCloud;
        }

        /// <summary>Caminho relativo à pasta bios, ex.: pcsx2\bios\scph39001.bin.</summary>
        public string RelativePath { get; }
        public bool InCloud { get; }

        public override string ToString() => RelativePath + (InCloud ? " (na nuvem)" : " (neste PC)");
    }

    public sealed class BiosSyncResult
    {
        /// <summary>Arquivos que estavam soltos na nuvem e foram para a pasta certa.</summary>
        public List<string> Organized { get; } = new List<string>();
        public List<string> Downloaded { get; } = new List<string>();
        public List<string> Uploaded { get; } = new List<string>();
        public int Unchanged { get; set; }

        /// <summary>Arquivos que não foram copiados porque estão zerados (corrompidos).</summary>
        public List<CorruptFile> SkippedCorrupt { get; } = new List<CorruptFile>();
        public List<string> Errors { get; } = new List<string>();

        public bool HasTransfers => Organized.Count + Downloaded.Count + Uploaded.Count > 0;
    }

    /// <summary>
    /// BIOS na nuvem: &lt;nuvem&gt;\bios espelha &lt;RetroBat&gt;\bios, com a mesma estrutura de pastas.
    /// <list type="bullet">
    /// <item>Organizar: BIOS conhecidas soltas na raiz da pasta do Retroboat ou de bios\ vão para a pasta
    /// que o emulador usa (ex.: bios\pcsx2\bios).</item>
    /// <item>Baixar: tudo o que está em bios\ na nuvem e falta ou mudou neste PC.</item>
    /// <item>Enviar: BIOS conhecidas que só existem neste PC (pastas do catálogo). O pacote de BIOS livres
    /// do RetroBat não é enviado: ele já vem em toda instalação.</item>
    /// </list>
    /// Em arquivo presente nos dois lados e diferente, vence o mais novo. Um arquivo zerado nunca é
    /// copiado nem substitui um arquivo bom.
    /// </summary>
    public sealed class BiosSync
    {
        static readonly TimeSpan TimestampTolerance = TimeSpan.FromSeconds(2);

        readonly string _cloudRoot;
        readonly string _localBios;
        readonly IReadOnlyList<BiosSet> _sets;

        public BiosSync(string cloudRetroboatPath, string localBiosPath, IReadOnlyList<BiosSet> sets = null)
        {
            _cloudRoot = cloudRetroboatPath;
            _localBios = localBiosPath;
            _sets = sets ?? BiosCatalog.All;
        }

        public string CloudBiosPath => Path.Combine(_cloudRoot, "bios");

        public BiosSyncResult Sync(bool dryRun = false)
        {
            var result = new BiosSyncResult();
            Organize(result, dryRun);
            Pull(result, dryRun);
            Push(result, dryRun);
            return result;
        }

        public BiosSyncResult Organize(bool dryRun = false)
        {
            var result = new BiosSyncResult();
            Organize(result, dryRun);
            return result;
        }

        void Organize(BiosSyncResult result, bool dryRun)
        {
            foreach (var dir in new[] { _cloudRoot, CloudBiosPath })
            {
                if (!Directory.Exists(dir))
                    continue;

                foreach (var file in Directory.GetFiles(dir))
                {
                    var name = Path.GetFileName(file);
                    var set = _sets.FirstOrDefault(s => s.Owns(name));
                    if (set == null)
                        continue;

                    var target = Path.Combine(CloudBiosPath, set.Folder, name);
                    if (File.Exists(target))
                    {
                        result.Errors.Add(name + ": já existe em bios\\" + set.Folder + "; o arquivo solto foi deixado onde está");
                        continue;
                    }

                    result.Organized.Add(Relative(CloudBiosPath, target));
                    if (!dryRun)
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(target));
                        File.Move(file, target);
                    }
                }
            }
        }

        void Pull(BiosSyncResult result, bool dryRun)
        {
            if (!Directory.Exists(CloudBiosPath))
                return;

            foreach (var cloudFile in Directory.EnumerateFiles(CloudBiosPath, "*", SearchOption.AllDirectories))
            {
                var info = new FileInfo(cloudFile);
                if ((info.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0)
                    continue;

                var relative = Relative(CloudBiosPath, cloudFile);
                var localFile = Path.Combine(_localBios, relative);
                try
                {
                    var local = new FileInfo(localFile);
                    if (local.Exists && local.Length == info.Length && Close(local.LastWriteTimeUtc, info.LastWriteTimeUtc))
                    {
                        result.Unchanged++;
                        continue;
                    }

                    if (local.Exists && local.LastWriteTimeUtc > info.LastWriteTimeUtc + TimestampTolerance)
                        continue; // Este PC tem a versão mais nova: o Push envia.

                    if (IsZeroed(cloudFile))
                    {
                        result.SkippedCorrupt.Add(new CorruptFile(relative, inCloud: true));
                        continue;
                    }

                    result.Downloaded.Add(relative);
                    if (!dryRun)
                        CopyPreservingTime(cloudFile, localFile);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    result.Errors.Add(relative + ": " + ex.Message);
                }
            }
        }

        void Push(BiosSyncResult result, bool dryRun)
        {
            foreach (var set in _sets)
            {
                var folder = Path.Combine(_localBios, set.Folder);
                if (!Directory.Exists(folder))
                    continue;

                foreach (var localFile in Directory.GetFiles(folder).Where(f => set.Owns(Path.GetFileName(f))))
                {
                    var relative = Relative(_localBios, localFile);
                    var cloudFile = Path.Combine(CloudBiosPath, relative);
                    try
                    {
                        var local = new FileInfo(localFile);
                        var cloud = new FileInfo(cloudFile);
                        if (cloud.Exists && (local.Length == cloud.Length && Close(local.LastWriteTimeUtc, cloud.LastWriteTimeUtc)
                                             || local.LastWriteTimeUtc <= cloud.LastWriteTimeUtc + TimestampTolerance))
                            continue;

                        if (IsZeroed(localFile))
                        {
                            result.SkippedCorrupt.Add(new CorruptFile(relative, inCloud: false));
                            continue;
                        }

                        result.Uploaded.Add(relative);
                        if (!dryRun)
                            CopyPreservingTime(localFile, cloudFile);
                    }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                    {
                        result.Errors.Add(relative + ": " + ex.Message);
                    }
                }
            }
        }

        static bool IsZeroed(string path)
        {
            var check = IntegrityChecker.QuickCheck(path);
            return check.Status == IntegrityStatus.Zeroed || check.Status == IntegrityStatus.Empty;
        }

        static void CopyPreservingTime(string source, string destination)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            var temp = destination + ".retroboat-part";
            File.Copy(source, temp, true);
            File.SetLastWriteTimeUtc(temp, File.GetLastWriteTimeUtc(source));
            if (File.Exists(destination))
                File.Delete(destination);

            File.Move(temp, destination);
        }

        static bool Close(DateTime a, DateTime b) => (a - b).Duration() <= TimestampTolerance;

        static string Relative(string root, string path) => path.Substring(root.TrimEnd('\\', '/').Length).TrimStart('\\', '/');
    }
}
