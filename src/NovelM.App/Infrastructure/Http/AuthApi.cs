using NovelM_App.Application.Abstractions;
using NovelM_App.Domain.Auth;

namespace NovelM_App.Infrastructure.Http;

internal sealed class AuthApi : IAuthApi
{
    private const string LoginEndpoint = "/api/user/login";
    private const string RefreshEndpoint = "/api/user/refresh_token";

    private readonly ApiHttpClient _apiHttpClient;

    public AuthApi(ApiHttpClient apiHttpClient)
    {
        _apiHttpClient = apiHttpClient;
    }

    public async Task<LoginTokens> LoginAsync(
        string email,
        string passwordSha256,
        CancellationToken cancellationToken)
    {
        var response = await _apiHttpClient.PostAsync<LoginRequest, LoginResponse>(
            LoginEndpoint,
            "Login",
            new LoginRequest
            {
                Email = email,
                Password = passwordSha256
            },
            cancellationToken);

        return new LoginTokens(response.Token, response.RefreshToken);
    }

    public Task<string> RefreshAsync(
        string refreshToken,
        CancellationToken cancellationToken)
    {
        return _apiHttpClient.PostAsync<RefreshRequest, string>(
            RefreshEndpoint,
            "Refresh",
            new RefreshRequest { Token = refreshToken },
            cancellationToken);
    }
}
