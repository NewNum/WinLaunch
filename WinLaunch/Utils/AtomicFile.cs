using System;
using System.IO;

namespace WinLaunch
{
    /// <summary>
    /// Writes files through a temporary file so that a crash or power loss mid-write
    /// cannot leave a truncated settings/item file behind.
    /// </summary>
    public static class AtomicFile
    {
        public static void Write(string path, Action<Stream> writeContents)
        {
            string directory = Path.GetDirectoryName(path);

            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            string tempPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";

            //buffered first, so a writer that closes the stream it is handed cannot
            //interfere with flushing and swapping the file
            byte[] data;

            using (MemoryStream buffer = new MemoryStream())
            {
                writeContents(buffer);
                data = buffer.ToArray();
            }

            try
            {
                using (FileStream fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    fs.Write(data, 0, data.Length);

                    //make sure the data hit the disk before we swap the files
                    fs.Flush(true);
                }

                Swap(tempPath, path);
            }
            catch
            {
                TryDelete(tempPath);
                throw;
            }
        }

        private static void Swap(string tempPath, string path)
        {
            if (!File.Exists(path))
            {
                File.Move(tempPath, path);
                return;
            }

            try
            {
                File.Replace(tempPath, path, null, true);
            }
            catch (IOException)
            {
                //File.Replace is unavailable on some file systems (e.g. network shares)
                File.Delete(path);
                File.Move(tempPath, path);
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch { }
        }
    }
}
