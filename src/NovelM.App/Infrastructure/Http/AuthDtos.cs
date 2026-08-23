using System.Text.Json.Serialization;

namespace NovelM_App.Infrastructure.Http;

internal sealed class LoginRequest
{
    [JsonPropertyName("email")]
    public required string Email { get; init; }

    [JsonPropertyName("password")]
    public required string Password { get; init; }
}

internal sealed class LoginResponse
{
    [JsonRequired]
    public required string Token { get; init; }

    [JsonRequired]
    public required string RefreshToken { get; init; }
}

internal sealed class RefreshRequest
{
    [JsonPropertyName("token")]
    public required string Token { get; init; }
}
