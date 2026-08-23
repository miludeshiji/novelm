using NovelM_App.Application.Abstractions;
using NovelM_App.Domain.Auth;

namespace NovelM_App.Infrastructure.SignalR;

internal sealed class SignalRUserApi : IUserApi
{
    private readonly ISignalRConnection _connection;

    public SignalRUserApi(ISignalRConnection connection)
    {
        _connection = connection;
    }

    public async Task<UserProfile> GetMyInfoAsync(CancellationToken cancellationToken)
    {
        var response = await _connection.InvokeAsync<UserProfileDto>(
            HubMethodNames.GetMyInfo,
            null,
            cancellationToken);
        return new UserProfile(
            response.Id,
            response.UserName,
            response.Avatar,
            response.Role.Name,
            response.InteriorLevel ?? 0);
    }
}
