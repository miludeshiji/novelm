using NovelM_App.Domain.Auth;

namespace NovelM.Tests.Domain;

[TestClass]
public sealed class LoginTokensTests
{
    [TestMethod]
    public void ToString_DoesNotExposeTokenValues()
    {
        const string sessionToken = "synthetic-session-secret";
        const string refreshToken = "synthetic-refresh-secret";
        var tokens = new LoginTokens(sessionToken, refreshToken);

        var text = tokens.ToString();

        Assert.IsFalse(text.Contains(sessionToken, StringComparison.Ordinal));
        Assert.IsFalse(text.Contains(refreshToken, StringComparison.Ordinal));
    }
}
