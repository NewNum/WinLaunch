using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace WinLaunch
{
    public class ScannedAppFolder
    {
        public string Name;
        public List<string> Files = new List<string>();
    }

    public class InstalledAppScanResult
    {
        /// <summary>Shortcuts that become standalone icons.</summary>
        public List<string> LooseFiles = new List<string>();

        /// <summary>Start menu subdirectories that become launcher folders.</summary>
        public List<ScannedAppFolder> Folders = new List<ScannedAppFolder>();

        public int TotalFiles
        {
            get { return LooseFiles.Count + Folders.Sum(folder => folder.Files.Count); }
        }
    }

    /// <summary>
    /// Enumerates installed applications from the start menu. Pure file system work,
    /// safe to run off the UI thread.
    /// </summary>
    public static class InstalledAppScanner
    {
        //a start menu subdirectory needs at least this many new entries to become a folder
        private const int MinimumItemsForFolder = 2;

        public static bool IsShortcut(string path)
        {
            string extension = Path.GetExtension(path);

            return string.Equals(extension, ".lnk", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".url", StringComparison.OrdinalIgnoreCase);
        }

        public static List<string> GetStartMenuRoots()
        {
            List<string> roots = new List<string>();

            AddRoot(roots, Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms));
            AddRoot(roots, Environment.GetFolderPath(Environment.SpecialFolder.Programs));

            return roots;
        }

        private static void AddRoot(List<string> roots, string path)
        {
            if (string.IsNullOrEmpty(path))
                return;

            if (!Directory.Exists(path))
                return;

            if (roots.Any(existing => string.Equals(existing, path, StringComparison.OrdinalIgnoreCase)))
                return;

            roots.Add(path);
        }

        /// <summary>
        /// Collects shortcuts that are not represented by <paramref name="existingIdentities"/> yet.
        /// Entries that resolve to the same application are only reported once, which is what
        /// keeps the per-user and machine-wide start menus from producing duplicate icons.
        /// </summary>
        public static InstalledAppScanResult Scan(ISet<string> existingIdentities)
        {
            HashSet<string> seen = new HashSet<string>(existingIdentities ?? new HashSet<string>(), StringComparer.Ordinal);

            InstalledAppScanResult result = new InstalledAppScanResult();

            //folders of the same name in the per-user and machine-wide start menu are one folder
            Dictionary<string, ScannedAppFolder> foldersByName =
                new Dictionary<string, ScannedAppFolder>(StringComparer.OrdinalIgnoreCase);

            foreach (string root in GetStartMenuRoots())
            {
                foreach (string file in EnumerateShortcuts(root, SearchOption.TopDirectoryOnly))
                {
                    if (TryClaim(file, seen))
                        result.LooseFiles.Add(file);
                }

                foreach (string directory in EnumerateDirectories(root))
                {
                    List<string> newFiles = EnumerateShortcuts(directory, SearchOption.AllDirectories)
                        .Where(file => TryClaim(file, seen))
                        .ToList();

                    if (newFiles.Count == 0)
                        continue;

                    string name = Path.GetFileName(directory);

                    ScannedAppFolder folder;

                    if (foldersByName.TryGetValue(name, out folder))
                    {
                        folder.Files.AddRange(newFiles);
                        continue;
                    }

                    if (newFiles.Count < MinimumItemsForFolder)
                    {
                        //not enough entries to justify a folder
                        result.LooseFiles.AddRange(newFiles);
                        continue;
                    }

                    folder = new ScannedAppFolder { Name = name };
                    folder.Files.AddRange(newFiles);

                    foldersByName[name] = folder;
                    result.Folders.Add(folder);
                }
            }

            Sort(result);

            return result;
        }

        private static bool TryClaim(string file, HashSet<string> seen)
        {
            //orphaned shortcuts left behind by an uninstaller are not installed apps
            if (AppIdentity.IsFileTargetMissing(file))
                return false;

            string identity = AppIdentity.ForFile(file);

            if (identity == null)
                return false;

            return seen.Add(identity);
        }

        private static void Sort(InstalledAppScanResult result)
        {
            Comparison<string> byDisplayName = (a, b) => string.Compare(
                Path.GetFileNameWithoutExtension(a),
                Path.GetFileNameWithoutExtension(b),
                StringComparison.CurrentCultureIgnoreCase);

            result.LooseFiles.Sort(byDisplayName);
            result.Folders.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));

            foreach (ScannedAppFolder folder in result.Folders)
                folder.Files.Sort(byDisplayName);
        }

        private static IEnumerable<string> EnumerateDirectories(string root)
        {
            try
            {
                return Directory.GetDirectories(root);
            }
            catch
            {
                return Enumerable.Empty<string>();
            }
        }

        /// <summary>
        /// Recursive enumeration that keeps going when a single subdirectory is unreadable.
        /// </summary>
        private static List<string> EnumerateShortcuts(string directory, SearchOption option)
        {
            List<string> files = new List<string>();

            try
            {
                files.AddRange(Directory.GetFiles(directory).Where(IsShortcut));
            }
            catch
            {
                return files;
            }

            if (option == SearchOption.AllDirectories)
            {
                foreach (string subDirectory in EnumerateDirectories(directory))
                    files.AddRange(EnumerateShortcuts(subDirectory, option));
            }

            return files;
        }
    }
}
