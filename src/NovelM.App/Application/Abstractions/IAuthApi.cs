using NovelM_App.Domain.Auth;

namespace NovelM_App.Application.Abstractions;

public interface IAuthApi
{
    Task<LoginTokens> LoginAsync(
        string email,
        string passwordSha256,
        CancellationToken cancellationToken);

    Task<string> RefreshAsync(
        string refreshToken,
        CancellationToken cancellationToken);
}
