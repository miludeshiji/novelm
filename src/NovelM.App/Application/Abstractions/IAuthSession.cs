using NovelM_App.Domain.Auth;

namespace NovelM_App.Application.Abstractions;

public interface IAuthSession
{
    string? SessionToken { get; }

    Task SetTokensAsync(LoginTokens tokens, CancellationToken cancellationToken);

    Task ImportRefreshTokenAsync(
        string refreshToken,
        CancellationToken cancellationToken);

    Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken);

    void InvalidateSessionToken();

    Task ClearAsync(CancellationToken cancellationToken);
}
