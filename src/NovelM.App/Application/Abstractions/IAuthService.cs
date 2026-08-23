using NovelM_App.Domain.Auth;

namespace NovelM_App.Application.Abstractions;

public interface IAuthService
{
    UserProfile? CurrentUser { get; }

    Task<UserProfile?> RestoreAsync(CancellationToken cancellationToken);

    Task<UserProfile> LoginAsync(
        string email,
        string rawPassword,
        CancellationToken cancellationToken);

    Task LogoutAsync(CancellationToken cancellationToken);
}
