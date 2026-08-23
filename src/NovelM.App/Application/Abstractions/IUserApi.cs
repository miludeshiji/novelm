using NovelM_App.Domain.Auth;

namespace NovelM_App.Application.Abstractions;

public interface IUserApi
{
    Task<UserProfile> GetMyInfoAsync(CancellationToken cancellationToken);
}
