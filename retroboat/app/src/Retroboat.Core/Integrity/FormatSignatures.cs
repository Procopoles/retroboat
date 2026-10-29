using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Retroboat.Integrity
{
    /// <summary>Bytes fixos que um formato de jogo sempre tem numa posição conhecida.</summary>
    public sealed class FormatSignature
    {
        public FormatSignature(string name, long offset, byte[] magic, params string[] extensions)
        {
            Name = name;
            Offset = offset;
            Magic = magic;
            Extensions = extensions;
        }

        public string Name { get; }
        public long Offset { get; }
        public byte[] Magic { get; }
        public string[] Extensions { get; }

        public bool Matches(Stream stream, long length)
        {
            if (Offset + Magic.Length > length)
                return false;

            var bytes = StreamReading.ReadAt(stream, Offset, Magic.Length);
            return bytes.Length == Magic.Length && bytes.SequenceEqual(Magic);
        }
    }

    public static class FormatSignatures
    {
        static readonly FormatSignature[] All =
        {
            // Discos
            new FormatSignature("ISO 9660", 0x8001, Ascii("CD001"), ".iso"),
            new FormatSignature("UDF", 0x8001, Ascii("BEA01"), ".iso"),
            new FormatSignature("GameCube", 0x1C, Hex("C2339F3D"), ".iso", ".gcm"),
            new FormatSignature("Wii", 0x18, Hex("5D1C9EA3"), ".iso"),
            new FormatSignature("Xbox (XISO)", 0x10000, Ascii("MICROSOFT*XBOX*MEDIA"), ".iso", ".xiso"),
            new FormatSignature("Saturn", 0, Ascii("SEGA SEGASATURN"), ".iso"),
            new FormatSignature("Dreamcast", 0, Ascii("SEGA SEGAKATANA"), ".iso"),
            new FormatSignature("CD bruto (2352)", 0, Hex("00FFFFFFFFFFFFFFFFFFFF00"), ".iso", ".bin", ".img"),
            new FormatSignature("CHD", 0, Ascii("MComprHD"), ".chd"),
            new FormatSignature("RVZ", 0, Hex("52565A01"), ".rvz"),
            new FormatSignature("WIA", 0, Hex("57494101"), ".wia"),
            new FormatSignature("WBFS", 0, Ascii("WBFS"), ".wbfs"),
            new FormatSignature("GCZ", 0, Hex("01C00BB1"), ".gcz"),
            new FormatSignature("CSO", 0, Ascii("CISO"), ".cso"),
            new FormatSignature("ZSO", 0, Ascii("ZISO"), ".zso"),
            new FormatSignature("PBP", 0, Hex("00504250"), ".pbp"),
            new FormatSignature("NSP", 0, Ascii("PFS0"), ".nsp"),
            new FormatSignature("XCI", 0x100, Ascii("HEAD"), ".xci"),
            new FormatSignature("PKG do PS3", 0, Hex("7F504B47"), ".pkg"),

            // Compactados
            new FormatSignature("ZIP", 0, Hex("504B0304"), ".zip", ".apk", ".vpk", ".pk3"),
            new FormatSignature("ZIP vazio", 0, Hex("504B0506"), ".zip"),
            new FormatSignature("7-Zip", 0, Hex("377ABCAF271C"), ".7z"),
            new FormatSignature("RAR", 0, Ascii("Rar!"), ".rar"),

            // Cartuchos
            new FormatSignature("NES (iNES)", 0, Hex("4E45531A"), ".nes"),
            new FormatSignature("N64 (big-endian)", 0, Hex("80371240"), ".z64", ".n64", ".v64"),
            new FormatSignature("N64 (byte-swapped)", 0, Hex("37804012"), ".z64", ".n64", ".v64"),
            new FormatSignature("N64 (little-endian)", 0, Hex("40123780"), ".z64", ".n64", ".v64"),
            new FormatSignature("Game Boy", 0x104, Hex("CEED6666"), ".gb", ".gbc"),
            new FormatSignature("Game Boy Advance", 0x04, Hex("24FFAE51"), ".gba"),
            new FormatSignature("Nintendo DS", 0xC0, Hex("24FFAE51"), ".nds"),
            new FormatSignature("Mega Drive", 0x100, Ascii("SEGA"), ".md", ".gen", ".32x"),
            new FormatSignature("Mega Drive", 0x101, Ascii("SEGA"), ".md", ".gen", ".32x"),
            new FormatSignature("3DS (NCSD)", 0x100, Ascii("NCSD"), ".3ds", ".cci"),
        };

        // Extensões usadas por formatos diferentes (ex.: .bin é CD bruto ou cartucho de Atari/Mega Drive):
        // sem assinatura reconhecida, o resultado é "não verificado", e não "suspeito".
        static readonly HashSet<string> AmbiguousExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".bin", ".img" };

        public static IReadOnlyList<FormatSignature> ForExtension(string extension)
        {
            return All.Where(s => s.Extensions.Contains(extension ?? "", StringComparer.OrdinalIgnoreCase)).ToList();
        }

        public static bool IsAmbiguous(string extension) => AmbiguousExtensions.Contains(extension ?? "");

        static byte[] Ascii(string text) => Encoding.ASCII.GetBytes(text);

        static byte[] Hex(string hex)
        {
            var bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);

            return bytes;
        }
    }

    static class StreamReading
    {
        /// <summary>Lê até <paramref name="count"/> bytes a partir de <paramref name="offset"/>.</summary>
        public static byte[] ReadAt(Stream stream, long offset, int count)
        {
            var buffer = new byte[count];
            stream.Seek(offset, SeekOrigin.Begin);

            int total = 0;
            while (total < count)
            {
                int read = stream.Read(buffer, total, count - total);
                if (read == 0)
                    break;

                total += read;
            }

            if (total < count)
                Array.Resize(ref buffer, total);

            return buffer;
        }
    }
}
