using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Retroboat.Config
{
    /// <summary>
    /// O que um PC tem de configuração portável num momento: cada entrada é uma chave com valor (uma opção do ES,
    /// uma chave de um .ini, um arquivo inteiro).
    /// </summary>
    public sealed class ConfigSnapshot
    {
        /// <summary>Entradas que viajam entre PCs.</summary>
        public Dictionary<string, string> Values { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>
        /// Entradas que existem aqui mas, com o valor de agora, ficam no PC (ex.: um caminho absoluto). Nem vão nem
        /// vêm, e a última sincronização delas continua valendo.
        /// </summary>
        public HashSet<string> Held { get; } = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// Grupos (arquivo ou pasta) que existem neste PC. Um grupo que já foi sincronizado e sumiu (arquivo apagado,
        /// emulador reinstalado) volta da nuvem, em vez de apagar as configurações nos outros PCs.
        /// </summary>
        public HashSet<string> ExistingGroups { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Uma fonte de configurações deste PC que o Retroboat leva para a nuvem: o es_settings.cfg, o es_input.cfg, os
    /// arquivos de configuração de um emulador. A fonte só lê e grava; quem decide o que vai e o que vem é o ConfigSync.
    /// </summary>
    public interface IConfigSource
    {
        /// <summary>Nome da pasta na nuvem (retroboat\configuracoes\&lt;nome&gt;) e do estado local.</summary>
        string Name { get; }

        /// <summary>Para mensagens: "opções do ES", "PCSX2".</summary>
        string DisplayName { get; }

        /// <summary>Pasta de onde saem os caminhos das cópias no histórico local.</summary>
        string Root { get; }

        /// <summary>false: não existe neste PC (emulador não instalado, pasta inacessível). Nada vai nem vem.</summary>
        bool IsAvailable { get; }

        /// <summary>Lê o que está neste PC. Lança IOException ou XmlException se o arquivo não puder ser lido agora.</summary>
        ConfigSnapshot Read();

        /// <summary>Arquivo ou pasta a que a entrada pertence (ex.: "inis/PCSX2.ini", "gamesettings").</summary>
        string GroupOf(string id);

        /// <summary>
        /// false para o que nunca sai deste PC: depende do hardware (monitor, áudio, placa de vídeo), é caminho deste PC
        /// ou é senha. Vale para o que vem da nuvem também: um PC com uma versão antiga do Retroboat pode ter enviado.
        /// </summary>
        bool IsPortable(string id, string value);

        /// <summary>Quando a entrada mudou neste PC pela última vez (UTC). Desempata quando os dois lados mudaram.</summary>
        DateTime ChangedUtc(string id);

        /// <summary>Arquivos que Write vai mudar, para a cópia no histórico antes.</summary>
        IEnumerable<string> FilesFor(IEnumerable<string> ids);

        /// <summary>Grava as mudanças (valor nulo = remover), sem tocar no que é só deste PC.</summary>
        void Write(IReadOnlyDictionary<string, string> changes);
    }

    static class ConfigValues
    {
        static readonly Regex AbsolutePath = new Regex(@"^(?:[A-Za-z]:[\\/]|\\\\|//)", RegexOptions.CultureInvariant);

        /// <summary>
        /// Caminho absoluto, que só vale neste PC (a pasta do RetroBat, dos jogos ou do Drive muda de um PC para outro).
        /// Caminhos relativos viajam: o emulatorLauncher e os emuladores os resolvem a partir da própria pasta.
        /// </summary>
        public static bool HasAbsolutePath(string value)
        {
            if (string.IsNullOrEmpty(value))
                return false;

            foreach (var line in value.Split('\n'))
            {
                if (AbsolutePath.IsMatch(line.Trim().Trim('"')))
                    return true;
            }

            return false;
        }
    }
}
