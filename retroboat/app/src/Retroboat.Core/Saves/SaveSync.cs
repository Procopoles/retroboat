using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Xml.Linq;

namespace Retroboat.Saves
{
    public enum SaveAction
    {
        None,
        /// <summary>Nada a copiar, só anotar o estado da nuvem (ex.: apagado lá e aqui também não existe).</summary>
        Record,
        Upload,
        Download,
        /// <summary>O arquivo sumiu deste PC fora do jogo (pasta apagada, RetroBat reinstalado): volta da nuvem.</summary>
        Restore,
        /// <summary>O arquivo foi apagado no emulador de outro PC.</summary>
        DeleteLocal,
        /// <summary>O emulador apagou o arquivo durante o jogo: a nuvem registra a exclusão.</summary>
        UploadDeletion,
        /// <summary>Mudou neste PC e na nuvem desde a última sincronização.</summary>
        Conflict,
        /// <summary>Outro PC já avisou que enviou uma versão mais nova, que ainda não chegou a este PC.</summary>
        WaitForCloud,
        /// <summary>
        /// A cópia deste PC está danificada (gravação cortada por um pen drive arrancado, queda de energia):
        /// não vai para a nuvem e é trocada pela versão boa mais nova da nuvem, com cópia no histórico.
        /// </summary>
        Repair
    }

    public enum ConflictChoice
    {
        KeepLocal,
        KeepCloud
    }

    /// <summary>
    /// Uma versão imutável na nuvem: &lt;arquivo&gt;\&lt;data UTC&gt;-&lt;PC&gt;.gz (conteúdo comprimido) ou
    /// .apagado (o emulador apagou o arquivo). A data no começo do nome ordena as versões.
    /// </summary>
    public sealed class SaveVersion
    {
        public const string StampFormat = "yyyyMMdd'T'HHmmssfff'Z'";
        public const string DataExtension = ".gz";
        public const string DeletedExtension = ".apagado";
        const int StampLength = 19;

        public string Id { get; private set; }
        public string Path { get; private set; }
        public bool Deleted { get; private set; }
        public DateTime TimeUtc { get; private set; }
        public string Pc { get; private set; }

        public static string NewId(DateTime utc, string pc) => utc.ToString(StampFormat, CultureInfo.InvariantCulture) + "-" + pc;

        public static SaveVersion TryParse(string path)
        {
            var extension = System.IO.Path.GetExtension(path);
            bool deleted = string.Equals(extension, DeletedExtension, StringComparison.OrdinalIgnoreCase);
            if (!deleted && !string.Equals(extension, DataExtension, StringComparison.OrdinalIgnoreCase))
                return null;

            var id = System.IO.Path.GetFileNameWithoutExtension(path);
            if (id.Length < StampLength + 2 || id[StampLength] != '-'
                || !DateTime.TryParseExact(id.Substring(0, StampLength), StampFormat, CultureInfo.InvariantCulture,
                                           DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var time))
                return null;

            return new SaveVersion { Id = id, Path = path, Deleted = deleted, TimeUtc = time, Pc = id.Substring(StampLength + 1) };
        }
    }

    public sealed class SaveItemPlan
    {
        public string Name { get; set; }
        public SaveAction Action { get; set; }
        public string LocalPath { get; set; }

        /// <summary>SHA-1 da cópia deste PC; nulo se ela não existe.</summary>
        public string LocalSha1 { get; set; }
        public DateTime LocalModifiedUtc { get; set; }

        /// <summary>Versão mais nova na nuvem; nula se o arquivo nunca foi para a nuvem.</summary>
        public SaveVersion Cloud { get; set; }

        /// <summary>Versão que outro PC enviou e que ainda não chegou (para WaitForCloud).</summary>
        public string WaitingFor { get; set; }

        /// <summary>Por que a cópia deste PC não serve (danificada); nulo se está boa ou não foi conferida.</summary>
        public string LocalProblem { get; set; }

        public bool LocalExists => LocalSha1 != null;

        public override string ToString() => Name + ": " + Action;
    }

    public enum SaveCopyKind
    {
        Cloud,
        Conflict,
        LocalHistory
    }

    /// <summary>Uma cópia guardada de um arquivo, que pode ser restaurada.</summary>
    public sealed class SaveCopy
    {
        public SaveCopy(string id, SaveCopyKind kind, DateTime timeUtc, string pc, string path, bool deleted)
        {
            Id = id;
            Kind = kind;
            TimeUtc = timeUtc;
            Pc = pc;
            Path = path;
            Deleted = deleted;
        }

        /// <summary>Identificador para restaurar: o nome da versão, conflito-&lt;versão&gt; ou local-&lt;data&gt;.</summary>
        public string Id { get; }
        public SaveCopyKind Kind { get; }
        public DateTime TimeUtc { get; }
        public string Pc { get; }
        public string Path { get; }
        public bool Deleted { get; }

        public string KindName => Kind == SaveCopyKind.Cloud ? "nuvem" : Kind == SaveCopyKind.Conflict ? "conflito" : "histórico deste PC";
    }

    /// <summary>Trava de outro PC: ele está jogando com estes arquivos.</summary>
    public sealed class SaveLock
    {
        public string Pc { get; set; }
        public DateTime SinceUtc { get; set; }
        public DateTime HeartbeatUtc { get; set; }
        public string Game { get; set; }
    }

    public sealed class SaveSyncResult
    {
        public List<string> Uploaded { get; } = new List<string>();
        public List<string> Downloaded { get; } = new List<string>();
        public List<string> DeletedLocal { get; } = new List<string>();
        public List<string> DeletedInCloud { get; } = new List<string>();

        /// <summary>Conflitos que ficaram sem escolha (a cópia deste PC foi guardada em conflitos\ na nuvem).</summary>
        public List<string> Conflicts { get; } = new List<string>();
        public List<string> Waiting { get; } = new List<string>();

        /// <summary>Arquivos deixados de lado, com o motivo (vazio, zerado, danificado, cartão em pasta).</summary>
        public List<string> Skipped { get; } = new List<string>();

        /// <summary>Cópia danificada deste PC trocada pela versão boa da nuvem (ou restaurada à mão).</summary>
        public List<string> Repaired { get; } = new List<string>();

        /// <summary>Cópia deste PC danificada que não deu para consertar (a nuvem não tem versão boa).</summary>
        public List<string> DamagedHere { get; } = new List<string>();

        /// <summary>Versões danificadas na nuvem, puladas.</summary>
        public List<string> DamagedInCloud { get; } = new List<string>();
        public List<string> Errors { get; } = new List<string>();

        public bool HasTransfers => Uploaded.Count + Downloaded.Count + DeletedLocal.Count + DeletedInCloud.Count + Repaired.Count > 0;

        public void Add(SaveSyncResult other)
        {
            Uploaded.AddRange(other.Uploaded);
            Downloaded.AddRange(other.Downloaded);
            DeletedLocal.AddRange(other.DeletedLocal);
            DeletedInCloud.AddRange(other.DeletedInCloud);
            Conflicts.AddRange(other.Conflicts);
            Waiting.AddRange(other.Waiting);
            Skipped.AddRange(other.Skipped);
            Repaired.AddRange(other.Repaired);
            DamagedHere.AddRange(other.DamagedHere);
            DamagedInCloud.AddRange(other.DamagedInCloud);
            Errors.AddRange(other.Errors);
        }
    }

    /// <summary>
    /// Sincroniza uma pasta de saves (ex.: os memory cards do PCSX2) com retroboat\saves\&lt;mesma pasta&gt; na
    /// nuvem. O emulador decide quais arquivos existem e o que vai neles; o Retroboat leva cada mudança para a
    /// nuvem e traz as dos outros PCs.
    ///
    /// Numa pasta sincronizada (Google Drive para computador) não dá para saber quando um envio terminou e
    /// dois PCs gravando o mesmo arquivo geram cópias de conflito. Por isso:
    /// <list type="bullet">
    /// <item>cada gravação vira uma versão imutável nova (&lt;arquivo&gt;\&lt;data&gt;-&lt;PC&gt;.gz), e a mais nova vale;</item>
    /// <item>cada PC só grava os próprios arquivos: as versões que criou e pcs\&lt;PC&gt;.xml (trava e
    /// última versão enviada de cada arquivo, para os outros saberem que algo está chegando).</item>
    /// </list>
    /// Para saber de que lado veio uma mudança, este PC guarda a versão e o SHA-1 de cada arquivo na última
    /// sincronização (&lt;RetroBat&gt;\retroboat\saves-sync.xml). Antes de substituir ou apagar um arquivo
    /// local, guarda uma cópia em &lt;RetroBat&gt;\retroboat\historico.
    /// </summary>
    public sealed class SaveSync
    {
        public const string StateFileName = "saves-sync.xml";
        public const string PcsFolderName = "pcs";
        public const string ConflictsFolderName = "conflitos";
        public const int SnapshotsKept = 20;
        public static readonly TimeSpan LockStaleAfter = TimeSpan.FromMinutes(10);

        const string PartSuffix = ".retroboat-part";

        readonly string _statePath;
        readonly string _historyRoot;
        readonly Func<DateTime> _utcNow;
        readonly HashSet<string> _keptConflicts = new HashSet<string>(StringComparer.Ordinal);

        public SaveSync(string savesPath, string retroboatDataPath, string cloudRetroboatPath, SaveRule rule, string pcName = null, Func<DateTime> utcNow = null)
        {
            Rule = rule;
            LocalFolder = Path.Combine(savesPath, rule.Folder);
            CloudFolder = Path.Combine(cloudRetroboatPath, "saves", rule.Folder);
            _statePath = Path.Combine(retroboatDataPath, StateFileName);
            _historyRoot = Path.Combine(retroboatDataPath, "historico", "saves", rule.Folder);
            PcName = SafeFileName(string.IsNullOrEmpty(pcName) ? Environment.MachineName : pcName);
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
        }

        public SaveRule Rule { get; }
        public string LocalFolder { get; }
        public string CloudFolder { get; }
        public string PcName { get; }

        string PcsFolder => Path.Combine(CloudFolder, PcsFolderName);
        string OwnPcPath => Path.Combine(PcsFolder, PcName + ".xml");

        /// <summary>Planeja e aplica. Conflitos sem escolha ficam para depois; nada é perdido.</summary>
        public SaveSyncResult Sync(Func<SaveItemPlan, ConflictChoice?> resolve = null) => Apply(Plan(), resolve);

        /// <summary>
        /// O que fazer com cada arquivo. <paramref name="sessionItems"/> são os arquivos que existiam durante o
        /// jogo: só eles, se sumirem, contam como apagados no emulador. Fora do jogo, um arquivo que sumiu
        /// volta da nuvem. Com <paramref name="trustDates"/>, um arquivo com o mesmo tamanho e data da última
        /// sincronização não é lido de novo.
        /// </summary>
        public List<SaveItemPlan> Plan(IEnumerable<string> only = null, ICollection<string> sessionItems = null, bool trustDates = true)
        {
            // Pen drive ou disco desconectado: nada a planejar. Sem isso, os arquivos pareceriam apagados.
            if (!LocalDeviceAvailable)
                return new List<SaveItemPlan>();

            var state = SaveSyncState.Load(_statePath);
            var announced = ReadAnnouncedVersions();
            var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

            if (only != null)
            {
                names.UnionWith(only);
            }
            else
            {
                names.UnionWith(LocalItems().Select(Path.GetFileName));
                if (Directory.Exists(CloudFolder))
                {
                    names.UnionWith(Directory.GetDirectories(CloudFolder).Select(Path.GetFileName)
                        .Where(n => !n.Equals(PcsFolderName, StringComparison.OrdinalIgnoreCase)));
                }

                names.UnionWith(state.Names(Rule.Folder));
            }

            return names.Select(name => PlanItem(name, state.Get(Rule.Folder, name), announced, sessionItems, trustDates)).ToList();
        }

        SaveItemPlan PlanItem(string name, SyncedItem synced, Dictionary<string, string> announced, ICollection<string> sessionItems, bool trustDates)
        {
            var plan = new SaveItemPlan { Name = name, LocalPath = Path.Combine(LocalFolder, name), Cloud = LatestVersion(name) };

            var local = new FileInfo(plan.LocalPath);
            if (local.Exists)
            {
                plan.LocalModifiedUtc = local.LastWriteTimeUtc;
                bool sameAsSynced = trustDates && synced != null && !synced.Deleted && synced.Length == local.Length && synced.ModifiedTicks == local.LastWriteTimeUtc.Ticks;
                if (sameAsSynced)
                {
                    plan.LocalSha1 = synced.Sha1;
                }
                else
                {
                    // Só o que mudou desde a última sincronização é conferido; o resto já estava bom quando foi sincronizado.
                    var data = ReadShared(local.FullName);
                    plan.LocalSha1 = Sha1(data);
                    if (synced == null || synced.Deleted || plan.LocalSha1 != synced.Sha1)
                        plan.LocalProblem = Problem(data);
                }
            }

            if (plan.LocalProblem != null)
            {
                // Danificado aqui: nunca vai para a nuvem. Se a nuvem tem uma versão, ela volta para o lugar.
                plan.Action = plan.Cloud != null && !plan.Cloud.Deleted ? SaveAction.Repair : SaveAction.None;
                return plan;
            }

            if (announced.TryGetValue(name, out var newer) && string.CompareOrdinal(newer, plan.Cloud?.Id) > 0)
            {
                plan.WaitingFor = newer;
                plan.Action = SaveAction.WaitForCloud;
                return plan;
            }

            bool basePresent = synced != null && !synced.Deleted;
            bool cloudChanged = !string.Equals(plan.Cloud?.Id, synced?.Version, StringComparison.Ordinal);
            bool cloudHasData = plan.Cloud != null && !plan.Cloud.Deleted;

            if (!plan.LocalExists && basePresent)
            {
                // Sumiu deste PC. Só é exclusão se foi o emulador, durante o jogo, e ninguém mudou o arquivo na nuvem.
                bool deletedByEmulator = sessionItems != null && sessionItems.Contains(name);
                if (deletedByEmulator && !cloudChanged)
                    plan.Action = SaveAction.UploadDeletion;
                else if (cloudHasData)
                    plan.Action = deletedByEmulator ? SaveAction.Download : SaveAction.Restore;
                else
                    plan.Action = SaveAction.Record;

                return plan;
            }

            bool localChanged = plan.LocalExists && (!basePresent || plan.LocalSha1 != synced.Sha1);

            if (!localChanged && !cloudChanged)
                plan.Action = SaveAction.None;
            else if (!localChanged)
                plan.Action = plan.Cloud == null ? (plan.LocalExists ? SaveAction.Upload : SaveAction.None) // A nuvem perdeu o histórico: reenvia.
                            : plan.Cloud.Deleted ? (plan.LocalExists ? SaveAction.DeleteLocal : SaveAction.Record)
                            : SaveAction.Download;
            else if (!cloudChanged || !cloudHasData)
                plan.Action = SaveAction.Upload; // Apagado em outro PC mas mudou aqui: o que mudou fica.
            else
            {
                // A versão da nuvem danificada não disputa com a boa deste PC.
                var cloudData = TryReadGood(plan.Cloud, out _);
                plan.Action = cloudData == null ? SaveAction.Upload
                            : Sha1(cloudData) == plan.LocalSha1 ? SaveAction.Record
                            : SaveAction.Conflict;
            }

            return plan;
        }

        /// <summary>
        /// Aplica o plano. <paramref name="resolve"/> escolhe o lado de cada conflito; sem escolha, a cópia
        /// deste PC vai para conflitos\ na nuvem e o arquivo local fica como está. Com
        /// <paramref name="uploadOnly"/> (durante o jogo), nada local é trocado: só envios.
        /// </summary>
        public SaveSyncResult Apply(IEnumerable<SaveItemPlan> plans, Func<SaveItemPlan, ConflictChoice?> resolve = null, bool uploadOnly = false)
        {
            var result = new SaveSyncResult();
            var state = SaveSyncState.Load(_statePath);
            var sent = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var plan in plans)
            {
                try
                {
                    switch (plan.Action)
                    {
                        case SaveAction.None:
                            if (plan.LocalProblem != null)
                                result.DamagedHere.Add(plan.Name + ": danificado neste PC (" + plan.LocalProblem + "); não enviado, e a nuvem não tem uma versão para pôr no lugar");
                            break;

                        case SaveAction.Repair:
                            if (uploadOnly)
                                result.Skipped.Add(plan.Name + " (danificado neste PC: " + plan.LocalProblem + "; não enviado)");
                            else
                                Repair(plan, state, result, sent);
                            break;

                        case SaveAction.Record:
                            state.Set(Rule.Folder, plan.Name, RecordOf(plan));
                            break;

                        case SaveAction.Upload:
                            Upload(plan, state, result, sent);
                            break;

                        case SaveAction.UploadDeletion:
                            UploadDeletion(plan, state, result, sent);
                            break;

                        case SaveAction.Download:
                        case SaveAction.Restore:
                            if (!uploadOnly)
                                Download(plan, state, result, sent);
                            break;

                        case SaveAction.DeleteLocal:
                            if (!uploadOnly)
                                DeleteLocal(plan, state, result);
                            break;

                        case SaveAction.WaitForCloud:
                            // Durante o jogo, o que mudou aqui fica a salvo até a versão do outro PC chegar.
                            if (uploadOnly)
                                KeepInConflicts(plan);

                            result.Waiting.Add(plan.Name);
                            break;

                        case SaveAction.Conflict:
                            var choice = uploadOnly ? null : resolve?.Invoke(plan);
                            if (choice == ConflictChoice.KeepLocal)
                            {
                                Upload(plan, state, result, sent);
                            }
                            else
                            {
                                KeepInConflicts(plan);
                                if (choice == ConflictChoice.KeepCloud)
                                    Download(plan, state, result, sent);
                                else
                                    result.Conflicts.Add(plan.Name);
                            }

                            break;
                    }
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidDataException)
                {
                    result.Errors.Add(plan.Name + ": " + ex.Message);
                }
            }

            state.Save();
            if (sent.Count > 0)
                UpdateOwnPcFile(pc => { foreach (var item in sent) SetItem(pc, item.Key, item.Value); });

            return result;
        }

        /// <summary>Itens que o emulador usa e que ainda não são sincronizados (cartões em pasta), para avisar.</summary>
        public IEnumerable<string> UnsupportedItems() =>
            LocalFolderItems().Select(dir => Path.GetFileName(dir) + " (" + Rule.ItemName + " em pasta, ainda não sincronizado)");

        void Upload(SaveItemPlan plan, SaveSyncState state, SaveSyncResult result, Dictionary<string, string> sent)
        {
            var before = new FileInfo(plan.LocalPath).LastWriteTimeUtc;
            var data = ReadShared(plan.LocalPath);
            var after = new FileInfo(plan.LocalPath).LastWriteTimeUtc;

            var problem = Problem(data);
            if (problem != null)
            {
                result.Skipped.Add(plan.Name + " (" + problem + "; não enviado)");
                return;
            }

            var id = NewVersionId(plan, state);
            WriteAtomically(Path.Combine(CloudFolder, plan.Name, id + SaveVersion.DataExtension), Compress(data));

            // Se o emulador gravou enquanto o arquivo era lido, a data não serve de atalho na próxima vez.
            state.Set(Rule.Folder, plan.Name, new SyncedItem { Version = id, Sha1 = Sha1(data), Length = data.Length, ModifiedTicks = before == after ? after.Ticks : 0 });
            sent[plan.Name] = id;
            result.Uploaded.Add(plan.Name);
        }

        void UploadDeletion(SaveItemPlan plan, SaveSyncState state, SaveSyncResult result, Dictionary<string, string> sent)
        {
            var id = NewVersionId(plan, state);
            WriteAtomically(Path.Combine(CloudFolder, plan.Name, id + SaveVersion.DeletedExtension), new byte[0]);
            state.Set(Rule.Folder, plan.Name, new SyncedItem { Version = id, Deleted = true });
            sent[plan.Name] = id;
            result.DeletedInCloud.Add(plan.Name);
        }

        void Download(SaveItemPlan plan, SaveSyncState state, SaveSyncResult result, Dictionary<string, string> sent)
        {
            var best = NewestGoodVersion(plan.Name, result, out var data);
            if (best == null)
            {
                result.Skipped.Add(plan.Name + " (nenhuma versão boa na nuvem; a deste PC foi mantida)");
                return;
            }

            PlaceLocal(plan, best, data, state, sent);
            result.Downloaded.Add(plan.Name);
        }

        void Repair(SaveItemPlan plan, SaveSyncState state, SaveSyncResult result, Dictionary<string, string> sent)
        {
            var best = NewestGoodVersion(plan.Name, result, out var data);
            if (best == null)
            {
                result.DamagedHere.Add(plan.Name + ": danificado neste PC (" + plan.LocalProblem + "); nenhuma versão boa na nuvem para pôr no lugar");
                return;
            }

            PlaceLocal(plan, best, data, state, sent);
            result.Repaired.Add(plan.Name + ": a cópia deste PC estava danificada (" + plan.LocalProblem + "); voltou a versão de " +
                                Describe(best) + ". A danificada ficou no histórico deste PC.");
        }

        /// <summary>
        /// Põe uma versão boa no lugar do arquivo local (com cópia do anterior no histórico). Se ela não é a mais
        /// nova da nuvem, porque a mais nova está danificada, vira de novo a mais nova, para os outros PCs também.
        /// </summary>
        void PlaceLocal(SaveItemPlan plan, SaveVersion version, byte[] data, SaveSyncState state, Dictionary<string, string> sent)
        {
            if (File.Exists(plan.LocalPath))
                Snapshot(plan.LocalPath);

            WriteAtomically(plan.LocalPath, data);
            var id = version.Id;
            var latest = LatestVersion(plan.Name);
            if (latest != null && latest.Id != version.Id)
            {
                id = NewVersionId(plan, state);
                WriteAtomically(Path.Combine(CloudFolder, plan.Name, id + SaveVersion.DataExtension), Compress(data));
                sent[plan.Name] = id;
            }

            var written = new FileInfo(plan.LocalPath);
            state.Set(Rule.Folder, plan.Name, new SyncedItem { Version = id, Sha1 = Sha1(data), Length = data.Length, ModifiedTicks = written.LastWriteTimeUtc.Ticks });
        }

        /// <summary>
        /// A versão boa mais nova do arquivo na nuvem. Versões que não abrem ou não passam na conferência são
        /// puladas (e contadas em Repaired). Para numa exclusão: o que veio antes dela é de outra vida do arquivo.
        /// </summary>
        SaveVersion NewestGoodVersion(string name, SaveSyncResult result, out byte[] data)
        {
            foreach (var version in History(name))
            {
                data = null;
                if (version.Deleted)
                    return null;

                data = TryReadGood(version, out var problem);
                if (data != null)
                    return version;

                result.DamagedInCloud.Add(name + ": a versão da nuvem de " + Describe(version) + " está danificada (" + problem + ") e foi pulada");
            }

            data = null;
            return null;
        }

        /// <summary>Conteúdo da versão se ela abre e passa na conferência; nulo (com o motivo) se não.</summary>
        byte[] TryReadGood(SaveVersion version, out string problem)
        {
            try
            {
                var data = ReadVersion(version);
                problem = Problem(data);
                return problem == null ? data : null;
            }
            catch (Exception ex) when (ex is InvalidDataException || ex is IOException)
            {
                problem = "não abre: " + ex.Message;
                return null;
            }
        }

        /// <summary>Nulo se o conteúdo serve; senão, o motivo (vazio, zerado, ou o que o validador da regra achou).</summary>
        public string Problem(byte[] data) => IsBlank(data) ? "vazio ou zerado" : Rule.Validate?.Invoke(data);

        /// <summary>O disco da pasta local está conectado (um pen drive pode ser arrancado no meio do jogo).</summary>
        public bool LocalDeviceAvailable
        {
            get
            {
                try { return Directory.Exists(Path.GetPathRoot(Path.GetFullPath(LocalFolder))); }
                catch (Exception ex) when (ex is IOException || ex is ArgumentException || ex is UnauthorizedAccessException) { return false; }
            }
        }

        static string Describe(SaveVersion version) =>
            version.TimeUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture) + " (" + version.Pc + ")";

        void DeleteLocal(SaveItemPlan plan, SaveSyncState state, SaveSyncResult result)
        {
            Snapshot(plan.LocalPath);
            File.Delete(plan.LocalPath);
            state.Set(Rule.Folder, plan.Name, new SyncedItem { Version = plan.Cloud.Id, Deleted = true });
            result.DeletedLocal.Add(plan.Name);
        }

        /// <summary>Guarda a cópia deste PC em conflitos\ na nuvem, para não perder nada de nenhum dos lados.</summary>
        void KeepInConflicts(SaveItemPlan plan)
        {
            if (!File.Exists(plan.LocalPath))
                return;

            var data = ReadShared(plan.LocalPath);
            var sha = Sha1(data);
            if (Problem(data) != null || !_keptConflicts.Add(sha))
                return;

            // A mesma cópia já guardada numa abertura anterior não vai de novo.
            var folder = Path.Combine(CloudFolder, plan.Name, ConflictsFolderName);
            if (Directory.Exists(folder) && Directory.GetFiles(folder).Select(SaveVersion.TryParse)
                    .Any(v => v != null && !v.Deleted && v.Pc == PcName && Sha1(ReadVersion(v)) == sha))
                return;

            var id = SaveVersion.NewId(_utcNow(), PcName);
            WriteAtomically(Path.Combine(folder, id + SaveVersion.DataExtension), Compress(data));
        }

        static SyncedItem RecordOf(SaveItemPlan plan)
        {
            if (plan.Cloud == null || plan.Cloud.Deleted)
                return new SyncedItem { Version = plan.Cloud?.Id, Deleted = true };

            return new SyncedItem
            {
                Version = plan.Cloud.Id,
                Sha1 = plan.LocalSha1,
                Length = plan.LocalExists ? new FileInfo(plan.LocalPath).Length : 0,
                ModifiedTicks = plan.LocalExists ? plan.LocalModifiedUtc.Ticks : 0
            };
        }

        /// <summary>A versão nova vem depois de tudo o que este PC já viu, mesmo com o relógio de outro PC adiantado.</summary>
        string NewVersionId(SaveItemPlan plan, SaveSyncState state)
        {
            var time = _utcNow();
            foreach (var seen in new[] { plan.Cloud?.Id, state.Get(Rule.Folder, plan.Name)?.Version })
            {
                var version = seen != null ? SaveVersion.TryParse(seen + SaveVersion.DataExtension) : null;
                if (version != null && version.TimeUtc >= time)
                    time = version.TimeUtc.AddMilliseconds(1);
            }

            return SaveVersion.NewId(time, PcName);
        }

        /// <summary>Versão mais nova do arquivo na nuvem (as de conflitos\ não contam).</summary>
        public SaveVersion LatestVersion(string name)
        {
            var folder = Path.Combine(CloudFolder, name);
            if (!Directory.Exists(folder))
                return null;

            return Directory.GetFiles(folder).Select(SaveVersion.TryParse).Where(v => v != null)
                .OrderByDescending(v => v.Id, StringComparer.Ordinal).FirstOrDefault();
        }

        public IReadOnlyList<SaveVersion> History(string name)
        {
            var folder = Path.Combine(CloudFolder, name);
            if (!Directory.Exists(folder))
                return new List<SaveVersion>();

            return Directory.GetFiles(folder).Select(SaveVersion.TryParse).Where(v => v != null)
                .OrderByDescending(v => v.Id, StringComparer.Ordinal).ToList();
        }

        /// <summary>
        /// Todas as cópias guardadas de um arquivo, da mais nova para a mais antiga: versões na nuvem, as que
        /// perderam conflitos e as cópias que este PC guardou antes de substituir (histórico local).
        /// </summary>
        public IReadOnlyList<SaveCopy> Copies(string name)
        {
            var copies = new List<SaveCopy>();
            copies.AddRange(History(name).Select(v => new SaveCopy(v.Id, SaveCopyKind.Cloud, v.TimeUtc, v.Pc, v.Path, v.Deleted)));

            var conflicts = Path.Combine(CloudFolder, name, ConflictsFolderName);
            if (Directory.Exists(conflicts))
            {
                copies.AddRange(Directory.GetFiles(conflicts).Select(SaveVersion.TryParse).Where(v => v != null && !v.Deleted)
                    .Select(v => new SaveCopy("conflito-" + v.Id, SaveCopyKind.Conflict, v.TimeUtc, v.Pc, v.Path, false)));
            }

            var local = Path.Combine(_historyRoot, name);
            if (Directory.Exists(local))
            {
                foreach (var file in Directory.GetFiles(local))
                {
                    if (DateTime.TryParseExact(Path.GetFileNameWithoutExtension(file), "yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture,
                                               DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var time))
                        copies.Add(new SaveCopy("local-" + Path.GetFileNameWithoutExtension(file), SaveCopyKind.LocalHistory, time, PcName, file, false));
                }
            }

            return copies.OrderByDescending(c => c.TimeUtc).ToList();
        }

        /// <summary>Conteúdo de uma cópia, ou o motivo de ela não servir.</summary>
        public byte[] ReadCopy(SaveCopy copy, out string problem)
        {
            if (copy.Deleted)
            {
                problem = "é o registro de uma exclusão";
                return null;
            }

            try
            {
                var data = copy.Kind == SaveCopyKind.LocalHistory ? ReadShared(copy.Path) : ReadVersion(SaveVersion.TryParse(copy.Path));
                problem = Problem(data);
                return problem == null ? data : null;
            }
            catch (Exception ex) when (ex is InvalidDataException || ex is IOException)
            {
                problem = "não abre: " + ex.Message;
                return null;
            }
        }

        /// <summary>
        /// Volta o arquivo para uma cópia guardada (por padrão, a versão boa mais nova da nuvem). Ela vira a
        /// versão mais nova da nuvem, para os outros PCs também; o arquivo atual vai para o histórico deste PC.
        /// Não use com o emulador aberto.
        /// </summary>
        public SaveSyncResult Restore(string name, string copyId = null)
        {
            var result = new SaveSyncResult();
            var copies = Copies(name);
            SaveCopy chosen = null;
            byte[] data = null;

            if (copyId == null)
            {
                foreach (var copy in copies.Where(c => c.Kind == SaveCopyKind.Cloud && !c.Deleted))
                {
                    data = ReadCopy(copy, out _);
                    if (data != null)
                    {
                        chosen = copy;
                        break;
                    }
                }

                if (chosen == null)
                    throw new InvalidOperationException("nenhuma versão boa de " + name + " na nuvem");
            }
            else
            {
                chosen = copies.FirstOrDefault(c => string.Equals(c.Id, copyId, StringComparison.OrdinalIgnoreCase))
                         ?? throw new InvalidOperationException("cópia " + copyId + " não encontrada para " + name);
                data = ReadCopy(chosen, out var problem);
                if (data == null)
                    throw new InvalidOperationException("a cópia " + copyId + " não serve: " + problem);
            }

            var state = SaveSyncState.Load(_statePath);
            var plan = new SaveItemPlan { Name = name, LocalPath = Path.Combine(LocalFolder, name), Cloud = LatestVersion(name) };
            if (File.Exists(plan.LocalPath))
                Snapshot(plan.LocalPath);

            WriteAtomically(plan.LocalPath, data);
            var id = NewVersionId(plan, state);
            WriteAtomically(Path.Combine(CloudFolder, name, id + SaveVersion.DataExtension), Compress(data));
            var written = new FileInfo(plan.LocalPath);
            state.Set(Rule.Folder, name, new SyncedItem { Version = id, Sha1 = Sha1(data), Length = data.Length, ModifiedTicks = written.LastWriteTimeUtc.Ticks });
            state.Save();
            UpdateOwnPcFile(pc => SetItem(pc, name, id));

            result.Repaired.Add(name + ": restaurada a cópia de " + chosen.TimeUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture) +
                                " (" + chosen.Pc + ", " + chosen.KindName + ")");
            return result;
        }

        /// <summary>Arquivos da regra que existem neste PC (só arquivos; cartões em pasta ficam de fora).</summary>
        public IEnumerable<string> LocalItems()
        {
            if (!Directory.Exists(LocalFolder))
                return Enumerable.Empty<string>();

            return Directory.GetFiles(LocalFolder, Rule.Pattern).Where(MatchesPattern);
        }

        IEnumerable<string> LocalFolderItems()
        {
            if (!Directory.Exists(LocalFolder))
                return Enumerable.Empty<string>();

            return Directory.GetDirectories(LocalFolder, Rule.Pattern).Where(MatchesPattern);
        }

        // Directory.GetFiles("*.ps2") também acha "x.ps2abc" (extensões de 3 letras no padrão).
        bool MatchesPattern(string path) =>
            !Rule.Pattern.StartsWith("*.", StringComparison.Ordinal)
            || string.Equals(Path.GetExtension(path), Rule.Pattern.Substring(1), StringComparison.OrdinalIgnoreCase);

        // ---- Trava e pcs\<PC>.xml ----

        /// <summary>Outro PC jogando com estes arquivos agora (trava renovada há menos de 10 minutos).</summary>
        public SaveLock FindActiveLock()
        {
            var now = _utcNow();
            return ReadOtherPcs().Select(ReadLock).Where(l => l != null && now - l.HeartbeatUtc <= LockStaleAfter)
                .OrderByDescending(l => l.HeartbeatUtc).FirstOrDefault();
        }

        public void Lock(string game)
        {
            var now = _utcNow();
            UpdateOwnPcFile(pc =>
            {
                pc.Elements("lock").Remove();
                pc.Add(new XElement("lock", new XAttribute("since", Iso(now)), new XAttribute("heartbeat", Iso(now)), new XAttribute("game", game ?? "")));
            });
        }

        public void RenewLock()
        {
            var now = _utcNow();
            UpdateOwnPcFile(pc => pc.Element("lock")?.SetAttributeValue("heartbeat", Iso(now)));
        }

        public void Unlock() => UpdateOwnPcFile(pc => pc.Elements("lock").Remove());

        IEnumerable<XElement> ReadOtherPcs()
        {
            if (!Directory.Exists(PcsFolder))
                yield break;

            foreach (var file in Directory.GetFiles(PcsFolder, "*.xml"))
            {
                if (string.Equals(Path.GetFileNameWithoutExtension(file), PcName, StringComparison.OrdinalIgnoreCase))
                    continue;

                XElement root = null;
                try { root = XDocument.Load(file).Root; }
                catch (Exception ex) when (ex is IOException || ex is System.Xml.XmlException || ex is UnauthorizedAccessException) { }

                if (root != null)
                    yield return root;
            }
        }

        static SaveLock ReadLock(XElement pc)
        {
            var element = pc.Element("lock");
            if (element == null || !TryParseIso((string)element.Attribute("heartbeat"), out var heartbeat))
                return null;

            TryParseIso((string)element.Attribute("since"), out var since);
            return new SaveLock { Pc = (string)pc.Attribute("name"), SinceUtc = since, HeartbeatUtc = heartbeat, Game = (string)element.Attribute("game") };
        }

        /// <summary>Para cada arquivo, a versão mais nova que algum outro PC diz ter enviado.</summary>
        Dictionary<string, string> ReadAnnouncedVersions()
        {
            var newest = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pc in ReadOtherPcs())
            {
                foreach (var item in pc.Elements("item"))
                {
                    var name = (string)item.Attribute("name");
                    var version = (string)item.Attribute("version");
                    if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(version))
                        continue;

                    if (!newest.TryGetValue(name, out var current) || string.CompareOrdinal(version, current) > 0)
                        newest[name] = version;
                }
            }

            return newest;
        }

        void UpdateOwnPcFile(Action<XElement> change)
        {
            XElement pc = null;
            if (File.Exists(OwnPcPath))
            {
                try { pc = XDocument.Load(OwnPcPath).Root; }
                catch (System.Xml.XmlException) { }
            }

            pc = pc ?? new XElement("pc");
            pc.SetAttributeValue("name", PcName);
            change(pc);
            pc.SetAttributeValue("updated", Iso(_utcNow()));

            Directory.CreateDirectory(PcsFolder);
            var temp = OwnPcPath + PartSuffix;
            new XDocument(pc).Save(temp);
            if (File.Exists(OwnPcPath))
                File.Delete(OwnPcPath);

            File.Move(temp, OwnPcPath);
        }

        static void SetItem(XElement pc, string name, string version)
        {
            var item = pc.Elements("item").FirstOrDefault(e => string.Equals((string)e.Attribute("name"), name, StringComparison.OrdinalIgnoreCase));
            if (item == null)
                pc.Add(item = new XElement("item", new XAttribute("name", name)));

            item.SetAttributeValue("version", version);
        }

        // ---- Arquivos ----

        /// <summary>Cópia local antes de substituir ou apagar; ficam as 20 mais recentes de cada arquivo.</summary>
        void Snapshot(string localPath)
        {
            var folder = Path.Combine(_historyRoot, Path.GetFileName(localPath));
            Directory.CreateDirectory(folder);
            var target = Path.Combine(folder, _utcNow().ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture) + Path.GetExtension(localPath));
            File.Copy(localPath, target, true);

            foreach (var old in Directory.GetFiles(folder).OrderByDescending(f => f, StringComparer.Ordinal).Skip(SnapshotsKept))
                File.Delete(old);
        }

        static byte[] ReadVersion(SaveVersion version)
        {
            using (var file = new FileStream(version.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var gzip = new GZipStream(file, CompressionMode.Decompress))
            using (var memory = new MemoryStream())
            {
                gzip.CopyTo(memory);
                return memory.ToArray();
            }
        }

        static byte[] Compress(byte[] data)
        {
            using (var memory = new MemoryStream())
            {
                using (var gzip = new GZipStream(memory, CompressionLevel.Optimal, true))
                    gzip.Write(data, 0, data.Length);

                return memory.ToArray();
            }
        }

        /// <summary>Lê mesmo com o emulador segurando o arquivo aberto.</summary>
        static byte[] ReadShared(string path)
        {
            using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var memory = new MemoryStream())
            {
                file.CopyTo(memory);
                return memory.ToArray();
            }
        }

        /// <summary>
        /// Grava num arquivo temporário, força a ida para o disco e só então troca pelo definitivo. Num pen drive
        /// arrancado no meio, sobra o arquivo antigo ou o temporário, nunca um definitivo pela metade.
        /// </summary>
        internal static void WriteAtomically(string path, byte[] data)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var temp = path + PartSuffix;
            using (var file = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                file.Write(data, 0, data.Length);
                file.Flush(true);
            }

            if (File.Exists(path))
                File.Delete(path);

            File.Move(temp, path);
        }

        /// <summary>Vazio ou só zeros: o que sobra de um arquivo corrompido, nunca um save de verdade.</summary>
        static bool IsBlank(byte[] data)
        {
            foreach (var b in data)
            {
                if (b != 0)
                    return false;
            }

            return true;
        }

        public static string Sha1(byte[] data)
        {
            using (var sha = SHA1.Create())
                return BitConverter.ToString(sha.ComputeHash(data)).Replace("-", "").ToLowerInvariant();
        }

        static string Iso(DateTime utc) => utc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

        static bool TryParseIso(string value, out DateTime utc) =>
            DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out utc);

        static string SafeFileName(string name)
        {
            foreach (var invalid in Path.GetInvalidFileNameChars())
                name = name.Replace(invalid, '_');

            return name;
        }
    }

    sealed class SyncedItem
    {
        public string Version;
        public string Sha1;
        public long Length;
        public long ModifiedTicks;
        public bool Deleted;
    }

    /// <summary>&lt;RetroBat&gt;\retroboat\saves-sync.xml: o que cada arquivo era na última sincronização. Fica no PC.</summary>
    sealed class SaveSyncState
    {
        readonly Dictionary<string, SyncedItem> _items = new Dictionary<string, SyncedItem>(StringComparer.OrdinalIgnoreCase);
        string _path;

        /// <summary>
        /// Se o arquivo estiver danificado (gravação cortada), usa a cópia anterior (.bak); sem nenhum dos dois,
        /// começa do zero: o SHA-1 dos dois lados decide e nada é perdido, no máximo um conflito a mais.
        /// </summary>
        public static SaveSyncState Load(string path)
        {
            foreach (var candidate in new[] { path, path + ".bak" })
            {
                try
                {
                    if (File.Exists(candidate))
                        return Parse(path, XDocument.Load(candidate));
                }
                catch (Exception ex) when (ex is System.Xml.XmlException || ex is FormatException || ex is NullReferenceException) { } // Danificado; erro de disco sobe.
            }

            return new SaveSyncState { _path = path };
        }

        static SaveSyncState Parse(string path, XDocument document)
        {
            var state = new SaveSyncState { _path = path };
            foreach (var element in document.Root.Elements("item"))
            {
                state._items[Key((string)element.Attribute("folder"), (string)element.Attribute("name"))] = new SyncedItem
                {
                    Version = (string)element.Attribute("version"),
                    Sha1 = (string)element.Attribute("sha1"),
                    Length = (long?)element.Attribute("length") ?? 0,
                    ModifiedTicks = (long?)element.Attribute("modified") ?? 0,
                    Deleted = (bool?)element.Attribute("deleted") ?? false
                };
            }

            return state;
        }

        public SyncedItem Get(string folder, string name) => _items.TryGetValue(Key(folder, name), out var item) ? item : null;

        public void Set(string folder, string name, SyncedItem item) => _items[Key(folder, name)] = item;

        public IEnumerable<string> Names(string folder) =>
            _items.Keys.Where(k => k.StartsWith(folder + "|", StringComparison.OrdinalIgnoreCase)).Select(k => k.Substring(folder.Length + 1));

        public void Save()
        {
            var root = new XElement("savesSync", _items.OrderBy(i => i.Key, StringComparer.OrdinalIgnoreCase).Select(i =>
            {
                var parts = i.Key.Split('|');
                return new XElement("item",
                    new XAttribute("folder", parts[0]),
                    new XAttribute("name", parts[1]),
                    i.Value.Version != null ? new XAttribute("version", i.Value.Version) : null,
                    i.Value.Sha1 != null ? new XAttribute("sha1", i.Value.Sha1) : null,
                    i.Value.Deleted ? new XAttribute("deleted", true) : null,
                    !i.Value.Deleted ? new XAttribute("length", i.Value.Length) : null,
                    !i.Value.Deleted ? new XAttribute("modified", i.Value.ModifiedTicks) : null);
            }));

            // A versão anterior fica como .bak, para o caso de esta gravação ser cortada.
            if (File.Exists(_path))
            {
                try { File.Copy(_path, _path + ".bak", true); }
                catch (IOException) { }
            }

            using (var memory = new MemoryStream())
            {
                new XDocument(root).Save(memory);
                SaveSync.WriteAtomically(_path, memory.ToArray());
            }
        }

        static string Key(string folder, string name) => folder + "|" + name;
    }
}
