using System;
using System.IO;
using System.Linq;
using System.Threading;
using Retroboat.Accounts;
using Retroboat.Settings;

namespace Retroboat.Library
{
    /// <summary>
    /// A pergunta do emulatorLauncher ao fechar um jogo compactado que ele descompactou ("Do you want to keep the uncompressed
    /// game for further use?"). Sem a opção decompressedfolders, ele pergunta toda vez e não guarda a resposta: "não" apaga a
    /// pasta, e no jogo seguinte ele descompacta e pergunta de novo. Aqui a primeira resposta fica no retroboat.ini deste PC
    /// (<see cref="RetroboatSettings.KeepUncompressed"/>) e vai nos próximos jogos como -decompressedfolders keep|delete, que o
    /// emulatorLauncher lê por cima de tudo. A opção de descompressão do ES (geral, do sistema ou do jogo), quando definida,
    /// continua valendo: aí nada é passado nem aprendido.
    /// <para>
    /// A resposta é deduzida pela pasta que o emulatorLauncher usa, &lt;pasta de jogos&gt;\.uncompressed\&lt;sistema&gt;\&lt;arquivo&gt;
    /// (ou &lt;decompressedpath&gt;\.uncompressed\...): ela aparece durante o jogo e, depois da pergunta, fica (manter) ou some
    /// (apagar). Pasta que já existia antes não gera pergunta; pasta que nunca apareceu (jogo sem descompactar) não ensina nada.
    /// </para>
    /// </summary>
    public sealed class UncompressedGame
    {
        public const string Option = "decompressedfolders";
        const string PathOption = "decompressedpath";

        static readonly TimeSpan CheckEvery = TimeSpan.FromSeconds(1);

        readonly RetroBatInstall _install;

        UncompressedGame(RetroBatInstall install, string folder, string argument, bool learns)
        {
            _install = install;
            Folder = folder;
            Argument = argument;
            Learns = learns;
        }

        /// <summary>A pasta em que o emulatorLauncher descompacta este jogo.</summary>
        public string Folder { get; }

        /// <summary>O que acrescentar aos argumentos do emulatorLauncher (ex.: "-decompressedfolders delete"); null = nada.</summary>
        public string Argument { get; }

        /// <summary>A resposta desta partida será guardada (ainda não há resposta nem opção do ES, e a pasta não existe).</summary>
        public bool Learns { get; }

        public static UncompressedGame For(RetroBatInstall install, RetroboatSettings settings, string system, string rom)
        {
            if (string.IsNullOrEmpty(system) || string.IsNullOrEmpty(rom))
                return null;

            var es = EsSettings.Load(Path.Combine(install.UserEmulationStationPath, "es_settings.cfg"));
            var romFile = Path.GetFileName(rom);
            var folder = Path.Combine(BasePath(install, EsOption(es, system, romFile, PathOption)), ".uncompressed", system, romFile);

            // A escolha feita no ES vale sempre (o emulatorLauncher daria a nossa por cima dela).
            if (!string.IsNullOrEmpty(EsOption(es, system, romFile, Option)))
                return new UncompressedGame(install, folder, null, false);

            if (settings.KeepUncompressed.HasValue)
                return new UncompressedGame(install, folder, "-" + Option + " " + (settings.KeepUncompressed.Value ? "keep" : "delete"), false);

            return new UncompressedGame(install, folder, null, !HasFiles(folder));
        }

        /// <summary>
        /// Roda o jogo e, se <see cref="Learns"/>, olha a pasta a cada segundo. Pasta que apareceu: o que sobrou depois da
        /// pergunta é a resposta, gravada no retroboat.ini.
        /// </summary>
        /// <returns>O código de saída do jogo; e, em <paramref name="learned"/>, a resposta guardada agora (null se nenhuma).</returns>
        public int Around(Func<int> run, out bool? learned)
        {
            learned = null;
            if (!Learns)
                return run();

            bool seen = false;
            using (var done = new ManualResetEvent(false))
            {
                var watcher = new Thread(() =>
                {
                    do { seen |= HasFiles(Folder); }
                    while (!seen && !done.WaitOne(CheckEvery));
                }) { IsBackground = true, Priority = ThreadPriority.BelowNormal };
                watcher.Start();

                int code;
                try
                {
                    code = run();
                }
                finally
                {
                    done.Set();
                    watcher.Join();
                }

                bool kept = HasFiles(Folder);
                if (seen || kept)
                {
                    try
                    {
                        Remember(kept);
                        learned = kept;
                    }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                    {
                        // Sem gravar, a pergunta só volta no próximo jogo; o jogo em si não é afetado.
                        Logging.Log.Warn("Não foi possível guardar a resposta sobre o jogo descompactado: " + ex.Message);
                    }
                }

                return code;
            }
        }

        /// <summary>Grava só a resposta, sobre o retroboat.ini de agora (o menu NUVEM pode tê-lo mudado durante o jogo).</summary>
        void Remember(bool keep)
        {
            var path = RetroboatSettings.GetPath(_install);
            var current = RetroboatSettings.Load(path);
            current.KeepUncompressed = keep;
            current.Save(path);
        }

        /// <summary>O valor que o emulatorLauncher usaria: o do jogo, senão o do sistema, senão o geral.</summary>
        internal static string EsOption(EsSettings es, string system, string romFile, string option)
        {
            // Como o emulatorLauncher monta a chave do jogo: sem "=" e "#" no nome do arquivo.
            var game = system + "[\"" + romFile.Replace("=", "").Replace("#", "") + "\"]." + option;
            foreach (var key in new[] { game, system + "." + option, "global." + option })
            {
                var value = es.Get(key);
                if (!string.IsNullOrEmpty(value))
                    return value;
            }

            return null;
        }

        /// <summary>A pasta de jogos do emulatorLauncher (roms= do emulatorLauncher.cfg, relativo à pasta do ES), ou a decompressedpath.</summary>
        static string BasePath(RetroBatInstall install, string decompressedPath)
        {
            if (!string.IsNullOrEmpty(decompressedPath))
                return decompressedPath.Replace('/', '\\');

            string roms = null;
            if (File.Exists(install.EmulatorLauncherConfigPath))
                roms = KeyValueConfig.GetValue(File.ReadAllText(install.EmulatorLauncherConfigPath), "roms");

            if (string.IsNullOrEmpty(roms))
                return install.DefaultRomsPath;

            return Path.GetFullPath(Path.Combine(install.EmulationStationPath, roms.Replace('/', '\\')));
        }

        static bool HasFiles(string folder)
        {
            try
            {
                return Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return false;
            }
        }
    }
}
