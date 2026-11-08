using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Retroboat.Accounts;
using Retroboat.Library;

namespace Retroboat.Config
{
    /// <summary>
    /// O que fica no PC no es_settings.cfg. O resto viaja: opções globais (global.*), por sistema (ps2.*) e por jogo
    /// (ps2["Jogo.iso"].*), que o emulatorLauncher aplica no emulador a cada jogo, e as preferências do próprio ES
    /// (idioma, coleções, scraper, proteção de tela...).
    ///
    /// Risco semântico (RB-005): uma chave nova do upstream que dependa da máquina precisa entrar aqui, senão viaja.
    /// </summary>
    public static class EsSettingsRules
    {
        /// <summary>Chaves que dependem deste PC: tela, áudio, caminhos, tema instalado, estado da sessão.</summary>
        static readonly HashSet<string> PcKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // Tela e janela.
            "MonitorID", "FullscreenBorderless", "Windowed", "WindowWidth", "WindowHeight", "ScreenWidth", "ScreenHeight",
            "ScreenOffsetX", "ScreenOffsetY", "ScreenRotate", "VSync", "Overscan", "MaxVRAM", "HideWindow", "HideWindowFullReinit",
            // Áudio e volume do PC.
            "AudioDevice", "AudioCard", "OMXAudioDev", "audio.device", "audio.volume", "SystemVolume",
            // Sessão: último sistema aberto.
            "LastSystem",
            // O tema escolhido precisa estar instalado neste PC; o ES troca por outro se não estiver e mandaria a troca.
            "ThemeSet",
            // As coleções personalizadas são listas de caminhos de jogos deste PC (.emulationstation\collections).
            "CollectionSystemsCustom",
            // O Retroboat desliga o aviso de BIOS do ES em cada PC, uma vez (EsBiosCheck).
            "CheckBiosesAtLaunch",
            // Caminhos e programas deste PC.
            "global.bootgame.path", "global.bootgame.cmd", "AlternateSplashScreen", "SlideshowScreenSaverImageDir",
            "SlideshowScreenSaverVideoDir", "MusicDirectory",
            // Rede e identidade da máquina (Batocera).
            "system.hostname", "Overclock",
            // Sessão do RetroAchievements: o ES gera ao entrar com a conta (que viaja pelas contas da nuvem).
            "global.retroachievements.token"
        };

        /// <summary>Prefixos de chaves deste PC: controle de cada jogador (o dispositivo ligado aqui), saídas de vídeo, rede.</summary>
        static readonly string[] PcPrefixes = { "INPUT P", "es.resolution", "global.videooutput", "display.rotate", "wifi.", "network." };

        /// <summary>
        /// Opções do emulatorLauncher que dependem do hardware, em qualquer nível (global, sistema ou jogo): modo de
        /// vídeo da tela, monitor, placa de vídeo, ID de dispositivo.
        /// </summary>
        static readonly HashSet<string> PcOptions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "videomode", "MonitorIndex", "GPUIndex", "gearstick_deviceid"
        };

        static readonly Regex Secret = new Regex(@"(password|passwd|pass$|token|secret|apikey|api_key)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>As contas (ScreenScraper, IGDB, RetroAchievements) já têm sincronização própria, com os valores protegidos.</summary>
        static readonly HashSet<string> AccountKeys = new HashSet<string>(AccountSync.Services.SelectMany(s => s.Settings), StringComparer.OrdinalIgnoreCase);

        public static bool IsPortableKey(string name)
        {
            if (string.IsNullOrEmpty(name) || PcKeys.Contains(name) || AccountKeys.Contains(name) || Secret.IsMatch(name))
                return false;

            foreach (var prefix in PcPrefixes)
            {
                if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            return !PcOptions.Contains(OptionName(name));
        }

        /// <summary>"ps2[\"Jogo (v1.1).iso\"].videomode" → "videomode": o nome da opção, depois do jogo entre colchetes.</summary>
        static string OptionName(string name)
        {
            int bracket = name.LastIndexOf(']');
            int dot = name.LastIndexOf('.');
            return dot > bracket ? name.Substring(dot + 1) : name;
        }
    }

    /// <summary>
    /// As opções do ES (es_settings.cfg) na nuvem. É o que o RetroBat chama de configurações do emulador e do jogo: o
    /// emulatorLauncher regera a configuração do emulador a partir delas a cada jogo. Cada chave é uma entrada, com o
    /// tipo junto ("bool:true", "string:2"), para ser criada igual no outro PC.
    ///
    /// Só grava com o ES fechado: ele regrava o arquivo inteiro e desfaria o que viesse da nuvem.
    /// </summary>
    public sealed class EsSettingsConfig : IConfigSource
    {
        public const string FileName = "es_settings.cfg";

        readonly string _path;

        public EsSettingsConfig(RetroBatInstall install)
        {
            Root = install.UserEmulationStationPath;
            _path = Path.Combine(Root, FileName);
        }

        public string Name => "es";
        public string DisplayName => "opções do ES";
        public string Root { get; }
        public bool IsAvailable => Directory.Exists(Root);

        public ConfigSnapshot Read()
        {
            var snapshot = new ConfigSnapshot();
            if (!File.Exists(_path))
                return snapshot;

            snapshot.ExistingGroups.Add(FileName);
            foreach (var (type, name, value) in EsSettings.Load(_path).Entries())
            {
                if (!EsSettingsRules.IsPortableKey(name))
                    continue;

                if (ConfigValues.HasAbsolutePath(value))
                    snapshot.Held.Add(name);
                else
                    snapshot.Values[name] = type + ":" + value;
            }

            return snapshot;
        }

        public string GroupOf(string id) => FileName;

        public bool IsPortable(string id, string value)
        {
            if (!EsSettingsRules.IsPortableKey(id))
                return false;

            return value == null || (TrySplit(value, out _, out var text) && !ConfigValues.HasAbsolutePath(text));
        }

        public DateTime ChangedUtc(string id) => File.Exists(_path) ? File.GetLastWriteTimeUtc(_path) : DateTime.MinValue;

        public IEnumerable<string> FilesFor(IEnumerable<string> ids) => new[] { _path };

        public void Write(IReadOnlyDictionary<string, string> changes)
        {
            var settings = EsSettings.Load(_path);
            foreach (var change in changes)
            {
                if (change.Value == null)
                    settings.Remove(change.Key);
                else if (TrySplit(change.Value, out var type, out var text))
                    settings.SetTyped(type, change.Key, text);
            }

            settings.Save();
        }

        static bool TrySplit(string value, out string type, out string text)
        {
            int colon = value.IndexOf(':');
            type = colon > 0 ? value.Substring(0, colon) : null;
            text = colon > 0 ? value.Substring(colon + 1) : null;
            return type != null && EsSettings.Types.Contains(type);
        }
    }
}
