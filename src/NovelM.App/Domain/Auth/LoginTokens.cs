namespace NovelM_App.Domain.Auth;

public sealed record LoginTokens(string SessionToken, string RefreshToken)
{
    public override string ToString()
    {
        return "LoginTokens { SessionToken = [REDACTED], RefreshToken = [REDACTED] }";
    }
}
