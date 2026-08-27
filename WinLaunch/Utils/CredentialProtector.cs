using System;
using System.Security.Cryptography;
using System.Text;

namespace WinLaunch
{
    /// <summary>
    /// Protects stored credentials with DPAPI, so the ciphertext is bound to the current
    /// Windows user and cannot be decrypted by copying the settings file to another machine.
    /// </summary>
    public static class CredentialProtector
    {
        private const string Prefix = "dpapi:";

        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("WinLaunch.Credentials.v1");

        public static bool IsProtected(string value)
        {
            return value != null && value.StartsWith(Prefix, StringComparison.Ordinal);
        }

        public static string Protect(string plainText)
        {
            if (plainText == null)
                return null;

            byte[] protectedData = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(plainText),
                Entropy,
                DataProtectionScope.CurrentUser);

            return Prefix + Convert.ToBase64String(protectedData);
        }

        /// <summary>
        /// Returns null if the value is not DPAPI protected or cannot be decrypted by this user.
        /// </summary>
        public static string Unprotect(string value)
        {
            if (!IsProtected(value))
                return null;

            try
            {
                byte[] plainData = ProtectedData.Unprotect(
                    Convert.FromBase64String(value.Substring(Prefix.Length)),
                    Entropy,
                    DataProtectionScope.CurrentUser);

                return Encoding.UTF8.GetString(plainData);
            }
            catch
            {
                return null;
            }
        }
    }
}
