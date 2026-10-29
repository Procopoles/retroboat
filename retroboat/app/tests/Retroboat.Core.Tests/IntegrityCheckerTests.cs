using System;
using System.Linq;
using System.Text;
using Retroboat.Integrity;
using Xunit;

namespace Retroboat.Tests
{
    public class IntegrityCheckerTests
    {
        [Fact]
        public void Zero_filled_iso_is_corrupt()
        {
            using (var dir = new TempDir())
            {
                var path = dir.WriteFile("ps2/Jogo.iso", new byte[1 << 20]);

                var result = IntegrityChecker.QuickCheck(path);

                Assert.Equal(IntegrityStatus.Zeroed, result.Status);
                Assert.True(result.IsCorrupt);
            }
        }

        [Fact]
        public void Empty_file_is_corrupt()
        {
            using (var dir = new TempDir())
            {
                var result = IntegrityChecker.QuickCheck(dir.WriteFile("snes/Jogo.sfc", new byte[0]));

                Assert.Equal(IntegrityStatus.Empty, result.Status);
                Assert.True(result.IsCorrupt);
            }
        }

        [Fact]
        public void Iso_with_iso9660_descriptor_is_ok()
        {
            using (var dir = new TempDir())
            {
                var content = new byte[1 << 20];
                Encoding.ASCII.GetBytes("CD001").CopyTo(content, 0x8001);

                var result = IntegrityChecker.QuickCheck(dir.WriteFile("ps2/Jogo.iso", content));

                Assert.Equal(IntegrityStatus.Ok, result.Status);
                Assert.Equal("ISO 9660", result.Format);
            }
        }

        [Fact]
        public void Iso_without_any_known_signature_is_suspicious_but_not_corrupt()
        {
            using (var dir = new TempDir())
            {
                var result = IntegrityChecker.QuickCheck(dir.WriteFile("ps2/Jogo.iso", TempDir.RandomBytes(1 << 20)));

                Assert.Equal(IntegrityStatus.SignatureMissing, result.Status);
                Assert.False(result.IsCorrupt);
            }
        }

        [Fact]
        public void Bin_without_cd_sync_is_unverified_because_cartridges_also_use_bin()
        {
            using (var dir = new TempDir())
            {
                var result = IntegrityChecker.QuickCheck(dir.WriteFile("atari2600/Jogo.bin", TempDir.RandomBytes(4096)));

                Assert.Equal(IntegrityStatus.Unverified, result.Status);
            }
        }

        [Fact]
        public void Format_without_signature_is_unverified()
        {
            using (var dir = new TempDir())
            {
                var result = IntegrityChecker.QuickCheck(dir.WriteFile("snes/Jogo.sfc", TempDir.RandomBytes(512 * 1024)));

                Assert.Equal(IntegrityStatus.Unverified, result.Status);
            }
        }

        [Fact]
        public void Zip_header_is_recognized()
        {
            using (var dir = new TempDir())
            {
                var content = TempDir.RandomBytes(10000);
                new byte[] { 0x50, 0x4B, 0x03, 0x04 }.CopyTo(content, 0);

                var result = IntegrityChecker.QuickCheck(dir.WriteFile("mame/jogo.zip", content));

                Assert.Equal(IntegrityStatus.Ok, result.Status);
                Assert.Equal("ZIP", result.Format);
            }
        }

        [Fact]
        public void Missing_file_is_unreadable()
        {
            using (var dir = new TempDir())
            {
                var result = IntegrityChecker.QuickCheck(dir.Combine("nao-existe.iso"));

                Assert.Equal(IntegrityStatus.Unreadable, result.Status);
            }
        }

        [Fact]
        public void Samples_cover_start_end_and_signature_offset()
        {
            long length = 100L << 20;
            var offsets = IntegrityChecker.GetSampleOffsets(length, 8, FormatSignatures.ForExtension(".iso")).ToList();

            Assert.Contains(0L, offsets);
            Assert.Contains(0x8001L, offsets);
            Assert.Contains(length - IntegrityChecker.SampleSize, offsets);
            Assert.True(offsets.Count >= 8);
            Assert.All(offsets, o => Assert.InRange(o, 0, length - 1));
        }

        [Fact]
        public void Full_check_matches_reference_hashes()
        {
            using (var dir = new TempDir())
            {
                var result = IntegrityChecker.FullCheck(dir.WriteFile("check.bin", Encoding.ASCII.GetBytes("123456789")));

                Assert.Equal("cbf43926", result.Crc32);
                Assert.Equal("f7c3bc1d808e04732adf679965ccc34ca7ae3441", result.Sha1);
                Assert.False(result.AllZero);
            }
        }

        [Fact]
        public void Full_check_detects_all_zero_content()
        {
            using (var dir = new TempDir())
            {
                var result = IntegrityChecker.FullCheck(dir.WriteFile("zeros.iso", new byte[300000]));

                Assert.True(result.AllZero);
                Assert.Equal(300000, result.Length);
            }
        }
    }
}
