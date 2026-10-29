namespace Retroboat.Integrity
{
    /// <summary>CRC-32 (IEEE), o mesmo usado pelos DATs do Redump e do No-Intro.</summary>
    public sealed class Crc32
    {
        static readonly uint[] Table = BuildTable();

        uint _crc = 0xFFFFFFFF;

        public void Append(byte[] buffer, int offset, int count)
        {
            uint crc = _crc;
            for (int i = offset; i < offset + count; i++)
                crc = Table[(crc ^ buffer[i]) & 0xFF] ^ (crc >> 8);

            _crc = crc;
        }

        public uint Value => ~_crc;

        static uint[] BuildTable()
        {
            var table = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint value = i;
                for (int bit = 0; bit < 8; bit++)
                    value = (value & 1) != 0 ? 0xEDB88320 ^ (value >> 1) : value >> 1;

                table[i] = value;
            }

            return table;
        }
    }
}
