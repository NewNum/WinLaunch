using Xunit;

namespace WinLaunch.Tests
{
    public class AppIdentityKeyTests
    {
        [Theory]
        //the machine-wide and per-user start menu spell the same target differently
        [InlineData(@"C:\Program Files\App\app.exe", @"c:\program files\app\app.exe")]
        [InlineData(@"C:\Program Files\App\app.exe", @"C:\Program Files\Other\..\App\app.exe")]
        [InlineData(@"C:\Program Files\App\app.exe", "\"C:\\Program Files\\App\\app.exe\"")]
        [InlineData(@"C:\Program Files\App", @"C:\Program Files\App\")]
        public void SameTargetProducesSameKey(string first, string second)
        {
            Assert.Equal(AppIdentityKey.ForPath(first, null), AppIdentityKey.ForPath(second, null));
        }

        [Fact]
        public void DifferentTargetsProduceDifferentKeys()
        {
            Assert.NotEqual(
                AppIdentityKey.ForPath(@"C:\Program Files\App\app.exe", null),
                AppIdentityKey.ForPath(@"C:\Program Files\App\other.exe", null));
        }

        [Fact]
        public void ArgumentsArePartOfTheIdentity()
        {
            //same binary launched two ways is two different entries worth keeping
            Assert.NotEqual(
                AppIdentityKey.ForPath(@"C:\app.exe", "--profile work"),
                AppIdentityKey.ForPath(@"C:\app.exe", "--profile personal"));
        }

        [Theory]
        [InlineData(null, "")]
        [InlineData("", "")]
        [InlineData("   ", "")]
        [InlineData("  --a   --b  ", "--a --b")]
        [InlineData("--A", "--a")]
        public void ArgumentsAreNormalized(string arguments, string expected)
        {
            Assert.Equal(expected, AppIdentityKey.NormalizeArguments(arguments));
        }

        [Fact]
        public void EmptyAndWhitespaceArgumentsMatch()
        {
            Assert.Equal(
                AppIdentityKey.ForPath(@"C:\app.exe", null),
                AppIdentityKey.ForPath(@"C:\app.exe", "   "));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("\"\"")]
        public void UnusablePathsHaveNoKey(string path)
        {
            Assert.Null(AppIdentityKey.ForPath(path, null));
        }

        [Fact]
        public void DriveRootKeepsItsSeparator()
        {
            Assert.Equal(@"c:\", AppIdentityKey.NormalizePath(@"C:\"));
        }

        [Theory]
        [InlineData("https://example.com", "https://example.com/")]
        [InlineData("https://example.com", "HTTPS://EXAMPLE.COM")]
        public void UrlsAreNormalized(string first, string second)
        {
            Assert.Equal(AppIdentityKey.ForUrl(first), AppIdentityKey.ForUrl(second));
        }

        [Fact]
        public void UrlAndPathKeysDoNotCollide()
        {
            Assert.NotEqual(AppIdentityKey.ForUrl("app"), AppIdentityKey.ForPath(@"C:\app", null));
        }

        [Fact]
        public void DisplayNameKeyIsCaseAndSpacingInsensitive()
        {
            Assert.Equal(
                AppIdentityKey.ForDisplayName("Visual  Studio  Code"),
                AppIdentityKey.ForDisplayName(" visual studio code "));
        }

        [Theory]
        [InlineData(@"C:\Program Files\App\app.exe", true)]
        [InlineData(@"D:\games\game.exe", true)]
        //anything we cannot reason about locally must never justify deleting an icon
        [InlineData(@"\\server\share\app.exe", false)]
        [InlineData(@"shell:AppsFolder\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", false)]
        [InlineData(@"::{20D04FE0-3AEA-1069-A2D8-08002B30309D}", false)]
        [InlineData(@"app.exe", false)]
        [InlineData(null, false)]
        [InlineData("", false)]
        public void ExistenceIsOnlyVerifiableForPlainLocalPaths(string path, bool expected)
        {
            Assert.Equal(expected, AppIdentityKey.CanVerifyExistence(path));
        }

        [Theory]
        [InlineData(@"shell:AppsFolder\Something", true)]
        [InlineData(@"C:\dir\::{guid}", true)]
        [InlineData(@"C:\Program Files\App\app.exe", false)]
        public void ShellPathsAreRecognized(string path, bool expected)
        {
            Assert.Equal(expected, AppIdentityKey.IsShellPath(path));
        }
    }
}
