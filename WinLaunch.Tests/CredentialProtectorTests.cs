using Xunit;

namespace WinLaunch.Tests
{
    public class CredentialProtectorTests
    {
        [Theory]
        [InlineData("hunter2")]
        [InlineData("")]
        [InlineData("unicode \u5bc6\u7801 \ud83d\udd11")]
        public void RoundTripsPlainText(string plainText)
        {
            string ciphertext = CredentialProtector.Protect(plainText);

            Assert.NotEqual(plainText, ciphertext);
            Assert.Equal(plainText, CredentialProtector.Unprotect(ciphertext));
        }

        [Fact]
        public void ProtectedValueIsRecognizable()
        {
            Assert.True(CredentialProtector.IsProtected(CredentialProtector.Protect("secret")));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("q29tZSBsZWdhY3kgYWVzIGJsb2I=")]
        public void LegacyOrEmptyValuesAreNotTreatedAsProtected(string value)
        {
            Assert.False(CredentialProtector.IsProtected(value));

            //callers rely on null to detect that a migration is needed
            Assert.Null(CredentialProtector.Unprotect(value));
        }

        [Fact]
        public void ReturnsNullForCorruptedCiphertext()
        {
            string ciphertext = CredentialProtector.Protect("secret");
            string corrupted = ciphertext.Substring(0, ciphertext.Length - 6) + "AAAAA=";

            Assert.Null(CredentialProtector.Unprotect(corrupted));
        }

        [Fact]
        public void ProtectPassesNullThrough()
        {
            Assert.Null(CredentialProtector.Protect(null));
        }
    }
}
