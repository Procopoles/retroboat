using System;
using System.IO;
using System.Linq;
using System.Threading;

namespace Retroboat.Emulators
{
    /// <summary>
    /// Conferência do arquivo baixado (RB-033), antes de qualquer coisa ir para a pasta do emulador. Cada passo devolve nulo
    /// se está certo, ou o motivo em português. Aqui é só a integridade do pacote; o que mudou numa versão nova (nome do
    /// executável, pastas) é da <see cref="EmulatorCompatibility"/>.
    /// </summary>
    public static class PackageCheck
    {
        /// <summary>1. Chegaram exatamente os bytes que o servidor anunciou.</summary>
        public static string Length(string file, long expected)
        {
            if (!File.Exists(file))
                return "o arquivo baixado sumiu (" + Path.GetFileName(file) + ")";

            long actual = new FileInfo(file).Length;
            if (actual == 0)
                return "o arquivo baixado está vazio";

            if (expected > 0 && actual != expected)
                return "o arquivo baixado tem " + actual + " bytes, e o servidor anunciou " + expected;

            return null;
        }

        /// <summary>2. O pacote abre e o CRC de cada arquivo confere.</summary>
        public static string Archive(SevenZip sevenZip, string file, CancellationToken token)
        {
            var problem = sevenZip.Test(file, token);
            return problem == null ? null : "o pacote está corrompido (" + problem + ")";
        }

        /// <summary>
        /// 3. Depois de extrair na pasta temporária: o pacote trouxe um programa válido. Um core traz o &lt;core&gt;_libretro.dll;
        /// um emulador, pelo menos um .exe (o nome esperado é conferido na compatibilidade).
        /// </summary>
        public static string Extracted(string folder, EmulatorPackage package)
        {
            if (!Directory.Exists(folder) || !Directory.EnumerateFileSystemEntries(folder).Any())
                return "o pacote veio vazio";

            if (package.Kind == PackageKind.Core)
            {
                var dll = Directory.GetFiles(folder, package.CoreFileName, SearchOption.AllDirectories).FirstOrDefault();
                return dll == null ? "o pacote não tem o " + package.CoreFileName : Program(dll);
            }

            var executables = EmulatorPackage.FindExecutables(folder);
            if (executables.Count == 0)
                return "o pacote não tem nenhum programa (.exe)";

            var expected = package.Executables.Select(e => Path.Combine(folder, e)).FirstOrDefault(File.Exists);
            return Program(expected ?? executables[0]);
        }

        /// <summary>Um programa do Windows de verdade: cabeçalhos MZ e PE, para x64, x86 ou ARM64.</summary>
        public static string Program(string path)
        {
            try
            {
                using (var stream = File.OpenRead(path))
                using (var reader = new BinaryReader(stream))
                {
                    if (stream.Length < 0x40 || reader.ReadUInt16() != 0x5A4D) // "MZ"
                        return Path.GetFileName(path) + " não é um programa do Windows (sem o cabeçalho MZ)";

                    stream.Position = 0x3C;
                    int peOffset = reader.ReadInt32();
                    if (peOffset <= 0 || peOffset > stream.Length - 6)
                        return Path.GetFileName(path) + " está incompleto (cabeçalho PE fora do arquivo)";

                    stream.Position = peOffset;
                    if (reader.ReadUInt32() != 0x00004550) // "PE\0\0"
                        return Path.GetFileName(path) + " não é um programa do Windows (sem o cabeçalho PE)";

                    ushort machine = reader.ReadUInt16();
                    if (machine != 0x8664 && machine != 0x014C && machine != 0xAA64)
                        return Path.GetFileName(path) + " é para outro processador (0x" + machine.ToString("X4") + ")";
                }

                return null;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return "não deu para ler " + Path.GetFileName(path) + " (" + ex.Message + ")";
            }
        }
    }
}
