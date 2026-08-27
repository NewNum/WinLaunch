using System;
using System.IO;
using System.Text.RegularExpressions;

namespace WinLaunch
{
    /// <summary>
    /// Builds the key that decides whether two launcher entries point at the same thing.
    /// Pure string and path handling, no disk or COM access, so it can be unit tested.
    /// </summary>
    public static class AppIdentityKey
    {
        private static readonly Regex WhitespaceRuns = new Regex(@"\s+", RegexOptions.Compiled);

        /// <summary>
        /// Key for something launched by path, e.g. a resolved shortcut target or a plain executable.
        /// </summary>
        public static string ForPath(string target, string arguments)
        {
            string normalizedTarget = NormalizePath(target);

            if (string.IsNullOrEmpty(normalizedTarget))
                return null;

            return "app:" + normalizedTarget + "|" + NormalizeArguments(arguments);
        }

        public static string ForUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return null;

            string trimmed = url.Trim().TrimEnd('/');

            return "url:" + trimmed.ToLowerInvariant();
        }

        /// <summary>
        /// Last resort for entries whose target cannot be resolved, e.g. advertised installer
        /// shortcuts or Store apps. Two entries only collide here if they also collide by name.
        /// </summary>
        public static string ForDisplayName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;

            return "name:" + WhitespaceRuns.Replace(name.Trim(), " ").ToLowerInvariant();
        }

        public static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;

            string result = path.Trim().Trim('"').Trim();

            if (result.Length == 0)
                return null;

            try
            {
                result = Environment.ExpandEnvironmentVariables(result);
            }
            catch { }

            //collapse ".." segments and mixed separators so the same target written two ways matches
            if (Path.IsPathRooted(result) && !IsShellPath(result))
            {
                try
                {
                    result = Path.GetFullPath(result);
                }
                catch { }
            }

            result = result.TrimEnd('\\', '/');

            //a bare drive root loses its separator above, put it back
            if (result.Length == 2 && result[1] == ':')
                result += "\\";

            return result.ToLowerInvariant();
        }

        public static string NormalizeArguments(string arguments)
        {
            if (string.IsNullOrWhiteSpace(arguments))
                return string.Empty;

            return WhitespaceRuns.Replace(arguments.Trim(), " ").ToLowerInvariant();
        }

        /// <summary>
        /// Whether a missing file at this path is meaningful evidence that the app is gone.
        /// Deliberately conservative: anything we cannot reason about locally returns false,
        /// so it never becomes a reason to delete a user's icon.
        /// </summary>
        public static bool CanVerifyExistence(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            if (IsShellPath(path))
                return false;

            //network shares may just be unreachable right now
            if (path.StartsWith(@"\\", StringComparison.Ordinal))
                return false;

            if (!Path.IsPathRooted(path))
                return false;

            return true;
        }

        /// <summary>
        /// Store apps, control panel entries and other virtual shell locations.
        /// </summary>
        public static bool IsShellPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            return path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)
                || path.IndexOf("::{", StringComparison.Ordinal) >= 0
                || path.IndexOf("AppsFolder", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
