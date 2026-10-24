using System;
using System.IO;

namespace Retroboat.Tests
{
    sealed class TempDir : IDisposable
    {
        public TempDir()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "retroboat-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string Combine(params string[] parts) => System.IO.Path.Combine(Path, System.IO.Path.Combine(parts));

        public string WriteFile(string relativePath, byte[] content)
        {
            var full = Combine(relativePath);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full));
            File.WriteAllBytes(full, content);
            return full;
        }

        public string WriteText(string relativePath, string content)
        {
            var full = Combine(relativePath);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full));
            File.WriteAllText(full, content);
            return full;
        }

        public static byte[] RandomBytes(int length, int seed = 42)
        {
            var bytes = new byte[length];
            new Random(seed).NextBytes(bytes);
            return bytes;
        }

        public void Dispose()
        {
            try { Directory.Delete(Path, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
