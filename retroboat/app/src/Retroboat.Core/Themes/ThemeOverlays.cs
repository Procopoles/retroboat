using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Retroboat.Library;
using Retroboat.Saves;

namespace Retroboat.Themes
{
    /// <summary>
    /// O status do Retroboat (JOGAR/BAIXAR, nuvem no cabeçalho) para um tema de terceiros: um
    /// retroboat\status.xml dentro da pasta do tema e uma linha de include no fim do theme.xml dele.
    /// </summary>
    public sealed class ThemeOverlay
    {
        public ThemeOverlay(string folder, string displayName, string signatureFile, string patch,
                            string versionFile = null, string versionPattern = null, string madeForVersion = null)
        {
            Folder = folder;
            DisplayName = displayName;
            SignatureFile = signatureFile;
            Patch = patch;
            VersionFile = versionFile;
            VersionPattern = versionPattern;
            MadeForVersion = madeForVersion;
        }

        /// <summary>Pasta que o instalador de temas do ES cria: o nome do repositório do tema.</summary>
        public string Folder { get; }

        public string DisplayName { get; }

        /// <summary>Arquivo que só esse tema tem: confirma que a pasta é mesmo ele antes de mexer.</summary>
        public string SignatureFile { get; }

        public string Patch { get; }

        /// <summary>Onde o tema mostra a versão dele; o ajuste copia posições do layout dessa versão.</summary>
        public string VersionFile { get; }
        public string VersionPattern { get; }
        public string MadeForVersion { get; }

        internal string ResourceName => "Retroboat.Themes." + Folder + ".status.xml";

        /// <summary>O status.xml que vai junto com o app (recurso do Retroboat.Core.dll).</summary>
        public byte[] ReadOverlay()
        {
            using (var stream = typeof(ThemeOverlay).Assembly.GetManifestResourceStream(ResourceName))
            {
                if (stream == null)
                    throw new InvalidOperationException("Recurso " + ResourceName + " não encontrado no Retroboat.Core.dll.");

                using (var memory = new MemoryStream())
                {
                    stream.CopyTo(memory);
                    return memory.ToArray();
                }
            }
        }

        /// <summary>A pasta é deste tema: o nome do repositório, com ou sem o "-master"/"-main" de um ZIP do GitHub.</summary>
        internal bool MatchesFolder(string folderName)
        {
            return string.Equals(folderName, Folder, StringComparison.OrdinalIgnoreCase)
                || string.Equals(folderName, Folder + "-master", StringComparison.OrdinalIgnoreCase)
                || string.Equals(folderName, Folder + "-main", StringComparison.OrdinalIgnoreCase);
        }
    }

    public sealed class ThemeOverlayResult
    {
        /// <summary>Temas que receberam o status agora (instalação nova, reinstalação ou atualização pelo ES).</summary>
        public List<string> Applied { get; } = new List<string>();

        /// <summary>Temas que já estavam com o status em dia.</summary>
        public List<string> UpToDate { get; } = new List<string>();

        public List<string> Warnings { get; } = new List<string>();

        /// <summary>O script do evento theme-changed do ES foi gravado ou atualizado.</summary>
        public bool HookWritten { get; set; }
    }

    /// <summary>
    /// Mantém o status do Retroboat nos temas suportados. O instalador de temas do ES apaga a pasta do tema
    /// e baixa o oficial ao instalar, reinstalar ou atualizar, e com isso o status some. O lançador chama
    /// isto a cada abertura, antes do ES; o script do evento theme-changed chama ao trocar de tema no ES.
    /// Só lê dois arquivos pequenos por tema e só grava o que estiver faltando ou diferente.
    /// </summary>
    public static class ThemeOverlays
    {
        public const string OverlayFolder = "retroboat";
        public const string OverlayFile = "status.xml";
        public const string HookEvent = "theme-changed";
        public const string HookFile = "retroboat-temas.bat";

        const string IncludeComment = "<!-- Retroboat: status dos jogos e da nuvem (o lançador põe de volta se o tema for reinstalado) -->";
        const string IncludeElement = "<include>./retroboat/status.xml</include>";
        static readonly Regex IncludePattern = new Regex(@"<include[^>]*>\s*\./retroboat/status\.xml\s*</include>", RegexOptions.IgnoreCase);

        public static readonly IReadOnlyList<ThemeOverlay> All = new[]
        {
            new ThemeOverlay("es-theme-carbon", "Carbon", @"views\detailed.xml", "RB-014"),
            new ThemeOverlay("es-theme-PlayStation-X", "PlayStation-X", @"_theme_views\ps4-style.xml", "RB-017",
                             @"_theme_views\top-info.xml", @"<text>\s*v\.(\d+(?:\.\d+)*)\s*</text>", "43")
        };

        public static ThemeOverlayResult Apply(RetroBatInstall install, string cliDirectory = null, bool dryRun = false)
        {
            var result = new ThemeOverlayResult();

            if (Directory.Exists(install.ThemesPath))
            {
                foreach (var themeDir in Directory.GetDirectories(install.ThemesPath))
                {
                    var overlay = All.FirstOrDefault(o => o.MatchesFolder(Path.GetFileName(themeDir)));
                    if (overlay == null)
                        continue;

                    try
                    {
                        ApplyTo(themeDir, overlay, dryRun, result);
                    }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                    {
                        result.Warnings.Add(overlay.DisplayName + ": não foi possível pôr o status do Retroboat no tema (" + ex.Message + ").");
                    }
                }
            }

            if (cliDirectory != null)
            {
                try
                {
                    result.HookWritten = WriteHook(install, cliDirectory, dryRun);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    result.Warnings.Add("Não foi possível gravar o script " + HookEvent + " do ES (" + ex.Message + ").");
                }
            }

            return result;
        }

        static void ApplyTo(string themeDir, ThemeOverlay overlay, bool dryRun, ThemeOverlayResult result)
        {
            var name = overlay.DisplayName + (string.Equals(Path.GetFileName(themeDir), overlay.Folder, StringComparison.OrdinalIgnoreCase) ? "" : " (" + Path.GetFileName(themeDir) + ")");

            var themeXml = Path.Combine(themeDir, "theme.xml");
            if (!File.Exists(themeXml) || !File.Exists(Path.Combine(themeDir, overlay.SignatureFile)))
            {
                result.Warnings.Add(name + ": a pasta " + Path.GetFileName(themeDir) + " não parece ser esse tema; ficou como está.");
                return;
            }

            var changes = new List<string>();

            var overlayPath = Path.Combine(themeDir, OverlayFolder, OverlayFile);
            var bundled = overlay.ReadOverlay();
            var existed = File.Exists(overlayPath);
            if (!existed || !SameText(File.ReadAllBytes(overlayPath), bundled))
            {
                if (!dryRun)
                    SaveSync.WriteAtomically(overlayPath, bundled);
                changes.Add(existed ? "status.xml atualizado" : "status.xml copiado");
            }

            var original = File.ReadAllBytes(themeXml);
            var patched = AddInclude(original);
            if (patched == null)
            {
                result.Warnings.Add(name + ": o theme.xml não termina em </theme>; o status do Retroboat não foi incluído.");
                return;
            }

            if (patched != original)
            {
                if (!dryRun)
                    SaveSync.WriteAtomically(themeXml, patched);
                changes.Add("include no theme.xml");
            }

            if (changes.Count > 0)
                result.Applied.Add(name + ": " + string.Join(", ", changes));
            else
                result.UpToDate.Add(name);

            var installed = ReadVersion(themeDir, overlay);
            if (installed != null && installed != overlay.MadeForVersion)
                result.Warnings.Add(name + ": o status do Retroboat foi feito para a versão " + overlay.MadeForVersion +
                                    " do tema e a instalada é a " + installed + ". Confira se o botão JOGAR/BAIXAR continua no lugar.");
        }

        /// <summary>
        /// O theme.xml com a linha de include antes do último &lt;/theme&gt;, no mesmo encoding, final de linha e
        /// recuo do arquivo. O mesmo array se a linha já está lá; nulo se o arquivo não termina em &lt;/theme&gt;.
        /// </summary>
        internal static byte[] AddInclude(byte[] themeXml)
        {
            var hasBom = themeXml.Length >= 3 && themeXml[0] == 0xEF && themeXml[1] == 0xBB && themeXml[2] == 0xBF;
            var text = new UTF8Encoding(false).GetString(themeXml, hasBom ? 3 : 0, themeXml.Length - (hasBom ? 3 : 0));

            if (IncludePattern.IsMatch(text))
                return themeXml;

            var end = text.LastIndexOf("</theme>", StringComparison.Ordinal);
            if (end < 0)
                return null;

            var newline = text.Contains("\r\n") ? "\r\n" : "\n";
            var indent = Regex.IsMatch(text, @"\n\t<") ? "\t" : "  ";
            var lines = indent + IncludeComment + newline + indent + IncludeElement + newline;

            // Antes da linha do </theme>; se ele divide a linha com outra coisa, numa linha nova.
            var lineStart = end == 0 ? 0 : text.LastIndexOf('\n', end - 1) + 1;
            var ownLine = text.Substring(lineStart, end - lineStart).Trim().Length == 0;

            var patched = ownLine ? text.Insert(lineStart, lines) : text.Insert(end, newline + lines);
            var bytes = new UTF8Encoding(false).GetBytes(patched);
            return hasBom ? new byte[] { 0xEF, 0xBB, 0xBF }.Concat(bytes).ToArray() : bytes;
        }

        /// <summary>
        /// Script do evento theme-changed: o ES roda os scripts desse evento antes de recarregar o tema
        /// (Scripting.cpp, evento síncrono), então escolher no menu um tema recém-baixado já mostra o status.
        /// </summary>
        static bool WriteHook(RetroBatInstall install, string cliDirectory, bool dryRun)
        {
            var cli = Path.Combine(cliDirectory, "retroboat-cli.exe");
            if (!File.Exists(cli))
                return false;

            // Caminhos relativos à pasta do script, para o RetroBat continuar portátil (pen drive, outra letra).
            const string root = @"%~dp0..\..\..\..";
            string cliFromRoot;
            if (RetroBatInstall.SamePath(cliDirectory, install.RetroboatDataPath))
                cliFromRoot = root + @"\retroboat\retroboat-cli.exe";
            else if (RetroBatInstall.SamePath(cliDirectory, install.Root))
                cliFromRoot = root + @"\retroboat-cli.exe";
            else
                cliFromRoot = cli;

            // Só ASCII: o cmd lê o .bat na página de código do console.
            var script =
                "@echo off\r\n" +
                "rem Retroboat (RB-017): poe de volta o status do Retroboat nos temas suportados.\r\n" +
                "rem Gravado pelo Retroboat.exe; editar aqui nao adianta.\r\n" +
                "if exist \"" + cliFromRoot + "\" \"" + cliFromRoot + "\" themes apply --retrobat \"" + root + "\" >nul 2>&1\r\n";

            var path = Path.Combine(install.UserEmulationStationPath, "scripts", HookEvent, HookFile);
            var bytes = Encoding.ASCII.GetBytes(script);
            if (File.Exists(path) && File.ReadAllBytes(path).SequenceEqual(bytes))
                return false;

            if (!dryRun)
                SaveSync.WriteAtomically(path, bytes);
            return true;
        }

        static string ReadVersion(string themeDir, ThemeOverlay overlay)
        {
            if (overlay.VersionFile == null)
                return null;

            var path = Path.Combine(themeDir, overlay.VersionFile);
            if (!File.Exists(path))
                return null;

            var match = Regex.Match(File.ReadAllText(path), overlay.VersionPattern);
            return match.Success ? match.Groups[1].Value : null;
        }

        /// <summary>Mesmo texto, ignorando CR: o git troca LF por CRLF conforme o clone, e isso não muda o tema.</summary>
        static bool SameText(byte[] a, byte[] b)
        {
            return a.Where(x => x != (byte)'\r').SequenceEqual(b.Where(x => x != (byte)'\r'));
        }
    }
}
