using System;
using System.IO;
using System.Text;

namespace Retroboat.Bios
{
    /// <summary>
    /// Valida uma BIOS de PS2 pelo conteúdo. A BIOS começa com uma tabela de arquivos (ROMDIR, entradas de
    /// 16 bytes a partir de "RESET"); o arquivo ROMVER traz versão, região e data, ex.: "0160AC20020207".
    /// </summary>
    public static class Ps2Bios
    {
        const int EntrySize = 16;
        const long MaxSize = 32L << 20;

        static readonly byte[] ResetEntry = Encoding.ASCII.GetBytes("RESET\0\0\0\0\0");

        public static BiosInspection Inspect(string path)
        {
            byte[] data;
            try
            {
                var length = new FileInfo(path).Length;
                if (length == 0)
                    return Problem("arquivo vazio");

                if (length > MaxSize)
                    return Problem("grande demais para ser uma BIOS de PS2");

                data = File.ReadAllBytes(path);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return Problem("não foi possível ler: " + ex.Message);
            }

            return Inspect(data);
        }

        public static BiosInspection Inspect(byte[] data)
        {
            if (Array.TrueForAll(data, b => b == 0))
                return Problem("arquivo zerado, sem conteúdo");

            int romdir = IndexOf(data, ResetEntry);
            if (romdir < 0)
                return Problem("não é uma BIOS de PS2 (tabela ROMDIR não encontrada)");

            long offset = 0;
            for (int pos = romdir; pos + EntrySize <= data.Length; pos += EntrySize)
            {
                var name = Encoding.ASCII.GetString(data, pos, 10).Split('\0')[0];
                if (name.Length == 0)
                    break;

                uint size = BitConverter.ToUInt32(data, pos + 12);
                if (name == "ROMVER" && offset + size <= data.Length && size >= 14)
                    return new BiosInspection { IsValid = true, Description = DescribeRomver(Encoding.ASCII.GetString(data, (int)offset, 14)) };

                offset += (size + 15) & ~15L;
            }

            // BIOS sem ROMVER ainda pode funcionar; vale como válida, sem descrição.
            return new BiosInspection { IsValid = true, Description = "versão desconhecida" };
        }

        internal static string DescribeRomver(string romver)
        {
            string region;
            switch (romver.Length > 4 ? romver[4] : '?')
            {
                case 'J': region = "Japão"; break;
                case 'A': region = "EUA"; break;
                case 'E': region = "Europa"; break;
                case 'H': region = "Ásia"; break;
                case 'C': region = "China"; break;
                case 'T': region = "kit de desenvolvimento"; break;
                default: region = "região desconhecida"; break;
            }

            return "v" + romver.Substring(0, 2).TrimStart('0') + "." + romver.Substring(2, 2) + ", " + region + ", " +
                   romver.Substring(6, 4) + "-" + romver.Substring(10, 2) + "-" + romver.Substring(12, 2);
        }

        static BiosInspection Problem(string problem) => new BiosInspection { IsValid = false, Problem = problem };

        static int IndexOf(byte[] data, byte[] pattern)
        {
            for (int i = 0; i <= data.Length - pattern.Length; i++)
            {
                int j = 0;
                while (j < pattern.Length && data[i + j] == pattern[j])
                    j++;

                if (j == pattern.Length)
                    return i;
            }

            return -1;
        }
    }
}
