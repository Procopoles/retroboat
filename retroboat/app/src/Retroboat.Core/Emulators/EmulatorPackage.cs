using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Retroboat.Library;

namespace Retroboat.Emulators
{
    public enum PackageKind
    {
        /// <summary>Um programa, em &lt;RetroBat&gt;\emulators\&lt;nome&gt;.</summary>
        Emulator,

        /// <summary>Um core do RetroArch, em &lt;RetroBat&gt;\emulators\retroarch\cores\&lt;nome&gt;_libretro.dll.</summary>
        Core
    }

    /// <summary>
    /// Um pacote do repositório do RetroBat: um emulador (emulators\&lt;nome&gt;.7z) ou um core do RetroArch
    /// (emulators\cores\&lt;nome&gt;_libretro.dll.zip). Vai para o mesmo lugar onde o RetroBat o poria, porque é dali que o
    /// emulatorLauncher abre o emulador e regrava as configurações dele.
    /// </summary>
    public sealed class EmulatorPackage
    {
        /// <summary>Pastas mais fundas que isso não são procuradas atrás do executável.</summary>
        const int ExecutableSearchDepth = 3;

        public EmulatorPackage(PackageKind kind, string name, string displayName, IEnumerable<string> executables = null)
        {
            Kind = kind;
            Name = name;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? name : displayName;
            Executables = (executables ?? Enumerable.Empty<string>()).Where(e => !string.IsNullOrWhiteSpace(e)).ToList();
        }

        public PackageKind Kind { get; }

        /// <summary>Nome no repositório e na pasta emulators (flycast, pcsx2); de um core, sem o _libretro (swanstation).</summary>
        public string Name { get; }

        /// <summary>Como o usuário vê: "Flycast", "SwanStation (RetroArch)".</summary>
        public string DisplayName { get; }

        /// <summary>
        /// O que o emulatorLauncher procura na pasta do emulador (relativo a ela; o primeiro é o principal). Vazio quando o
        /// catálogo não conhece: aí qualquer .exe na pasta vale. Um core é sempre &lt;nome&gt;_libretro.dll.
        /// </summary>
        public IReadOnlyList<string> Executables { get; }

        /// <summary>Identifica o pacote nos arquivos de estado: "flycast" ou "core/flycast".</summary>
        public string Key => Kind == PackageKind.Core ? "core/" + Name : Name;

        public string ArchiveName => Kind == PackageKind.Core ? CoreFileName + ".zip" : Name + ".7z";

        public string CoreFileName => Name + "_libretro.dll";

        public string LocalFolder(RetroBatInstall install) => Kind == PackageKind.Core
            ? Path.Combine(RetroArchFolder(install), "cores")
            : Path.Combine(install.Root, "emulators", Name);

        public static string RetroArchFolder(RetroBatInstall install) => Path.Combine(install.Root, "emulators", "retroarch");

        public static string RetroArchExe(RetroBatInstall install) => Path.Combine(RetroArchFolder(install), "retroarch.exe");

        /// <summary>Os arquivos que o emulatorLauncher procuraria, com o caminho completo.</summary>
        public IReadOnlyList<string> ExpectedFiles(RetroBatInstall install)
        {
            var folder = LocalFolder(install);
            if (Kind == PackageKind.Core)
                return new[] { Path.Combine(folder, CoreFileName) };

            return Executables.Select(e => Path.Combine(folder, e)).ToList();
        }

        /// <summary>
        /// O arquivo principal que existe: o core, o executável esperado ou, sem ele, o primeiro .exe da pasta. Nulo se não
        /// está instalado.
        /// </summary>
        public string MainFile(RetroBatInstall install)
        {
            var expected = ExpectedFiles(install).FirstOrDefault(File.Exists);
            if (expected != null || Kind == PackageKind.Core)
                return expected;

            return FindExecutables(LocalFolder(install)).FirstOrDefault();
        }

        /// <summary>
        /// O programa está neste PC. O RetroBat cria a pasta de cada emulador com modelos de configuração e sem nenhum .exe,
        /// então a pasta existir não basta. Um executável de outro nome também conta: uma versão nova que trocou o nome não
        /// pode fazer o Retroboat baixar de novo a cada jogo (a verificação de compatibilidade avisa).
        /// </summary>
        public bool IsInstalled(RetroBatInstall install) => MainFile(install) != null;

        /// <summary>Os .exe da pasta, até três níveis, do mais raso para o mais fundo.</summary>
        public static IReadOnlyList<string> FindExecutables(string folder)
        {
            var found = new List<string>();
            Collect(folder, 0, found);
            return found;
        }

        static void Collect(string folder, int depth, List<string> found)
        {
            try
            {
                if (!Directory.Exists(folder))
                    return;

                found.AddRange(Directory.GetFiles(folder, "*.exe").OrderBy(f => f, StringComparer.OrdinalIgnoreCase));
                if (depth >= ExecutableSearchDepth)
                    return;

                foreach (var child in Directory.GetDirectories(folder).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
                    Collect(child, depth + 1, found);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
            }
        }

        public override string ToString() => DisplayName + " (" + Key + ")";
    }
}
