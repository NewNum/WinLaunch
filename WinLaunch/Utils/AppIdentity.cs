using System;
using System.Collections.Concurrent;
using System.IO;

namespace WinLaunch
{
    /// <summary>
    /// The launcher entry values needed to identify an application, copied off the UI thread
    /// so a scan can resolve shortcuts in the background while the grid stays interactive.
    /// </summary>
    public class AppIdentitySnapshot
    {
        public SBItem Item;

        public bool IsFolder;
        public string ApplicationPath;
        public string Arguments;
        public string Name;

        public static AppIdentitySnapshot From(SBItem item)
        {
            return new AppIdentitySnapshot
            {
                Item = item,
                IsFolder = item.IsFolder,
                ApplicationPath = item.ApplicationPath,
                Arguments = item.Arguments,
                Name = item.Name
            };
        }
    }

    /// <summary>
    /// Resolves launcher entries and candidate files on disk down to a comparable identity,
    /// so the same application discovered through different shortcuts is recognised as one app.
    /// </summary>
    public static class AppIdentity
    {
        private struct ShortcutTarget
        {
            public string Path;
            public string Arguments;
        }

        //resolving a shortcut is a COM round trip, and a scan touches thousands of them
        private static readonly ConcurrentDictionary<string, ShortcutTarget> ShortcutCache =
            new ConcurrentDictionary<string, ShortcutTarget>(StringComparer.OrdinalIgnoreCase);

        public static void ClearCache()
        {
            ShortcutCache.Clear();
        }

        #region identity

        /// <summary>
        /// Identity of a candidate file found while scanning, e.g. a start menu shortcut.
        /// </summary>
        public static string ForFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;

            if (Uri.IsWellFormedUriString(path, UriKind.Absolute) && !IsExistingLocalPath(path))
                return AppIdentityKey.ForUrl(path);

            string extension = Path.GetExtension(path).ToLowerInvariant();

            if (extension == ".url")
            {
                string url = ReadInternetShortcutUrl(path);

                return AppIdentityKey.ForUrl(url) ?? AppIdentityKey.ForDisplayName(Path.GetFileNameWithoutExtension(path));
            }

            if (extension == ".lnk")
            {
                ShortcutTarget target = ResolveShortcut(path);

                return AppIdentityKey.ForPath(target.Path, target.Arguments)
                    ?? AppIdentityKey.ForDisplayName(Path.GetFileNameWithoutExtension(path));
            }

            return AppIdentityKey.ForPath(path, null) ?? AppIdentityKey.ForDisplayName(Path.GetFileNameWithoutExtension(path));
        }

        public static string ForItem(SBItem item)
        {
            return item == null ? null : ForItem(AppIdentitySnapshot.From(item));
        }

        /// <summary>
        /// Identity of an entry already on the springboard. Returns null for folders,
        /// which are containers rather than applications.
        /// </summary>
        public static string ForItem(AppIdentitySnapshot item)
        {
            if (item == null || item.IsFolder)
                return null;

            string applicationPath = item.ApplicationPath;

            if (string.IsNullOrWhiteSpace(applicationPath))
                return AppIdentityKey.ForDisplayName(item.Name);

            if (Uri.IsWellFormedUriString(applicationPath, UriKind.Absolute) && !IsExistingLocalPath(applicationPath))
                return AppIdentityKey.ForUrl(applicationPath);

            string fullPath = ExpandItemPath(applicationPath);
            string extension = Path.GetExtension(fullPath).ToLowerInvariant();

            if (extension == ".url")
            {
                string url = ReadInternetShortcutUrl(fullPath);

                return AppIdentityKey.ForUrl(url) ?? AppIdentityKey.ForDisplayName(item.Name);
            }

            if (extension == ".lnk")
            {
                ShortcutTarget target = ResolveShortcut(fullPath);

                //the item may add its own arguments on top of the ones baked into the shortcut
                string arguments = CombineArguments(target.Arguments, item.Arguments);

                return AppIdentityKey.ForPath(target.Path, arguments) ?? AppIdentityKey.ForDisplayName(item.Name);
            }

            return AppIdentityKey.ForPath(fullPath, item.Arguments) ?? AppIdentityKey.ForDisplayName(item.Name);
        }

        /// <summary>
        /// Shortcuts are copied into the link cache and stored as a bare "guid.lnk" filename.
        /// </summary>
        public static string ExpandItemPath(string applicationPath)
        {
            if (string.IsNullOrWhiteSpace(applicationPath))
                return applicationPath;

            if (Path.GetExtension(applicationPath).ToLowerInvariant() != ".lnk")
                return applicationPath;

            if (!ItemCollection.IsInCache(applicationPath))
                return applicationPath;

            try
            {
                return Path.GetFullPath(Path.Combine(PortabilityManager.LinkCachePath, applicationPath));
            }
            catch
            {
                return applicationPath;
            }
        }

        public static string CombineArguments(string first, string second)
        {
            if (string.IsNullOrWhiteSpace(first))
                return second;

            if (string.IsNullOrWhiteSpace(second))
                return first;

            return first.Trim() + " " + second.Trim();
        }

        #endregion identity

        #region uninstall detection

        /// <summary>
        /// True only when we can positively establish that the target is gone.
        /// Every uncertain case answers false, because the caller deletes what this reports.
        /// </summary>
        public static bool IsTargetMissing(AppIdentitySnapshot item)
        {
            if (item == null || item.IsFolder)
                return false;

            string applicationPath = item.ApplicationPath;

            if (string.IsNullOrWhiteSpace(applicationPath))
                return false;

            //web links have no local target to verify
            if (Uri.IsWellFormedUriString(applicationPath, UriKind.Absolute) && !IsExistingLocalPath(applicationPath))
                return false;

            string fullPath = ExpandItemPath(applicationPath);
            string extension = Path.GetExtension(fullPath).ToLowerInvariant();

            if (extension == ".url")
                return false;

            string target;

            if (extension == ".lnk")
            {
                //if the cached shortcut itself vanished we cannot tell what it pointed at
                if (!File.Exists(fullPath))
                    return false;

                target = ResolveShortcut(fullPath).Path;

                //advertised installer shortcuts and Store apps resolve to nothing
                if (string.IsNullOrWhiteSpace(target))
                    return false;
            }
            else
            {
                target = fullPath;
            }

            return !TargetExists(target);
        }

        /// <summary>
        /// True when a shortcut on disk points at something that is not installed anymore.
        /// Uninstallers regularly leave their start menu shortcuts behind, and importing those
        /// would produce entries that can never launch and that the uninstall sweep would
        /// immediately delete again.
        /// </summary>
        public static bool IsFileTargetMissing(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            if (!string.Equals(Path.GetExtension(path), ".lnk", StringComparison.OrdinalIgnoreCase))
                return false;

            string target = ResolveShortcut(path).Path;

            //advertised installer shortcuts and Store apps resolve to nothing
            if (string.IsNullOrWhiteSpace(target))
                return false;

            return !TargetExists(target);
        }

        private static bool TargetExists(string target)
        {
            if (!AppIdentityKey.CanVerifyExistence(target))
                return true;

            try
            {
                string root = Path.GetPathRoot(target);

                //removable or disconnected drives must not be read as an uninstall
                if (!string.IsNullOrEmpty(root))
                {
                    DriveInfo drive = new DriveInfo(root);

                    if (!drive.IsReady)
                        return true;
                }
            }
            catch
            {
                return true;
            }

            try
            {
                return File.Exists(target) || Directory.Exists(target);
            }
            catch
            {
                return true;
            }
        }

        #endregion uninstall detection

        #region resolution

        private static ShortcutTarget ResolveShortcut(string path)
        {
            ShortcutTarget cached;

            if (ShortcutCache.TryGetValue(path, out cached))
                return cached;

            string target;
            string arguments;

            MiscUtils.GetShortcutTargetAndArguments(path, out target, out arguments);

            ShortcutTarget resolved = new ShortcutTarget { Path = target, Arguments = arguments };

            ShortcutCache[path] = resolved;

            return resolved;
        }

        private static string ReadInternetShortcutUrl(string path)
        {
            try
            {
                foreach (string line in File.ReadAllLines(path))
                {
                    if (line.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))
                        return line.Substring(4).Trim();
                }
            }
            catch { }

            return null;
        }

        /// <summary>
        /// Local paths such as "C:\dir" parse as valid file:// URIs, so they need to be
        /// distinguished from real web links before treating a value as a URL.
        /// </summary>
        private static bool IsExistingLocalPath(string value)
        {
            try
            {
                return Path.IsPathRooted(value) && (File.Exists(value) || Directory.Exists(value));
            }
            catch
            {
                return false;
            }
        }

        #endregion resolution
    }
}
