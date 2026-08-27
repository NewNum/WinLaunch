using System;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace WinLaunch.Tests
{
    public class AtomicFileTests : IDisposable
    {
        private readonly string directory;

        public AtomicFileTests()
        {
            directory = Path.Combine(Path.GetTempPath(), "WinLaunchTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
        }

        public void Dispose()
        {
            try { Directory.Delete(directory, true); } catch { }
        }

        private string PathFor(string name)
        {
            return Path.Combine(directory, name);
        }

        private static void WriteText(Stream stream, string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            stream.Write(bytes, 0, bytes.Length);
        }

        [Fact]
        public void CreatesFileThatDoesNotExistYet()
        {
            string path = PathFor("new.txt");

            AtomicFile.Write(path, fs => WriteText(fs, "hello"));

            Assert.Equal("hello", File.ReadAllText(path));
        }

        [Fact]
        public void CreatesMissingDirectories()
        {
            string path = Path.Combine(directory, "nested", "deeper", "new.txt");

            AtomicFile.Write(path, fs => WriteText(fs, "hello"));

            Assert.Equal("hello", File.ReadAllText(path));
        }

        [Fact]
        public void OverwritesExistingFile()
        {
            string path = PathFor("existing.txt");
            File.WriteAllText(path, "a much longer original content");

            AtomicFile.Write(path, fs => WriteText(fs, "short"));

            Assert.Equal("short", File.ReadAllText(path));
        }

        [Fact]
        public void LeavesOriginalIntactWhenWriterThrows()
        {
            string path = PathFor("existing.txt");
            File.WriteAllText(path, "original");

            Assert.Throws<InvalidOperationException>(() =>
                AtomicFile.Write(path, fs =>
                {
                    WriteText(fs, "partial");
                    throw new InvalidOperationException("writer failed");
                }));

            Assert.Equal("original", File.ReadAllText(path));
        }

        [Fact]
        public void DoesNotLeaveTemporaryFilesBehind()
        {
            string path = PathFor("existing.txt");

            AtomicFile.Write(path, fs => WriteText(fs, "one"));

            Assert.Throws<InvalidOperationException>(() =>
                AtomicFile.Write(path, fs => throw new InvalidOperationException()));

            AtomicFile.Write(path, fs => WriteText(fs, "two"));

            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        }

        [Fact]
        public void ToleratesWritersThatCloseTheStream()
        {
            string path = PathFor("closed.txt");

            AtomicFile.Write(path, fs =>
            {
                using (StreamWriter writer = new StreamWriter(fs))
                {
                    writer.Write("closed by writer");
                }
            });

            Assert.Equal("closed by writer", File.ReadAllText(path));
        }
    }
}
