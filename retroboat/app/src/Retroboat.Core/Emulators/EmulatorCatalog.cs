using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Retroboat.Config;
using Retroboat.Library;

namespace Retroboat.Emulators
{
    /// <summary>
    /// Emulador do ES (-emulator e -core) → pacote do repositório do RetroBat. Os dados ficam em EmulatorCatalog.xml (recurso
    /// do Retroboat.Core): para corrigir um nome de executável ou incluir um emulador, mexa só lá.
    /// </summary>
    public static class EmulatorCatalog
    {
        /// <summary>O -emulator dos cores do RetroArch.</summary>
        public const string RetroArch = "libretro";

        const string ResourceName = "Retroboat.Emulators.EmulatorCatalog.xml";

        static readonly Lazy<Data> Loaded = new Lazy<Data>(() => Data.Parse(ReadResource()));

        /// <summary>
        /// O pacote que o ES abriria com este emulador e core. Nulo se não é do repositório do RetroBat: sem nome, core do
        /// RetroArch sem core, ou um programa fora do catálogo e sem pasta em &lt;RetroBat&gt;\emulators (Windows, lojas).
        /// </summary>
        public static EmulatorPackage For(RetroBatInstall install, string emulator, string core)
        {
            return For(emulator, core, name => Directory.Exists(Path.Combine(install.Root, "emulators", name)));
        }

        internal static EmulatorPackage For(string emulator, string core, Func<string, bool> retroBatHasFolder)
        {
            emulator = (emulator ?? "").Trim();
            core = (core ?? "").Trim();
            if (emulator.Length == 0)
                return null;

            var data = Loaded.Value;
            if (string.Equals(emulator, RetroArch, StringComparison.OrdinalIgnoreCase))
            {
                if (core.Length == 0)
                    return null;

                var name = data.CoreNames.TryGetValue(core, out var display) ? display : Title(core);
                return new EmulatorPackage(PackageKind.Core, core.ToLowerInvariant(), name + " (RetroArch)");
            }

            if (data.ByEsName.TryGetValue(emulator, out var known))
                return known;

            // Fora do catálogo: o RetroBat cria uma pasta para cada emulador que ele baixa; sem ela, é um programa externo.
            var folder = emulator.ToLowerInvariant();
            return retroBatHasFolder(folder) ? new EmulatorPackage(PackageKind.Emulator, folder, AutoEmulatorRules.DisplayName(folder)) : null;
        }

        /// <summary>Todos os emuladores do catálogo (sem os cores).</summary>
        public static IReadOnlyList<EmulatorPackage> Emulators => Loaded.Value.ByEsName.Values.Distinct().OrderBy(p => p.Name, StringComparer.Ordinal).ToList();

        static string Title(string name)
        {
            var words = name.Replace('-', ' ').Replace('_', ' ').Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            return string.Join(" ", words.Select(w => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(w)));
        }

        static string ReadResource()
        {
            using (var stream = typeof(EmulatorCatalog).Assembly.GetManifestResourceStream(ResourceName))
            {
                if (stream == null)
                    throw new InvalidOperationException("Catálogo de emuladores não encontrado no Retroboat.Core: " + ResourceName);

                using (var reader = new StreamReader(stream))
                    return reader.ReadToEnd();
            }
        }

        sealed class Data
        {
            public Dictionary<string, EmulatorPackage> ByEsName { get; } = new Dictionary<string, EmulatorPackage>(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, string> CoreNames { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            public static Data Parse(string xml)
            {
                var data = new Data();
                var root = XDocument.Parse(xml).Root;

                foreach (var element in root.Elements("emulator"))
                {
                    var name = ((string)element.Attribute("package") ?? "").Trim().ToLowerInvariant();
                    if (name.Length == 0)
                        continue;

                    var executables = ((string)element.Attribute("exe") ?? "").Split('|').Select(e => e.Trim()).Where(e => e.Length > 0);
                    var package = new EmulatorPackage(PackageKind.Emulator, name, (string)element.Attribute("display"), executables);

                    data.ByEsName[name] = package;
                    foreach (var alias in ((string)element.Attribute("es") ?? "").Split(',').Select(a => a.Trim()).Where(a => a.Length > 0))
                        data.ByEsName[alias] = package;
                }

                foreach (var element in root.Elements("core"))
                {
                    var name = ((string)element.Attribute("name") ?? "").Trim();
                    if (name.Length > 0)
                        data.CoreNames[name] = (string)element.Attribute("display") ?? name;
                }

                return data;
            }
        }
    }
}
