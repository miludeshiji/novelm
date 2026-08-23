using System.Text.Json.Serialization;

namespace NovelM_App.Infrastructure.Http;

internal sealed class ApiEnvelope<T>
{
    [JsonRequired]
    public required bool Success { get; init; }

    public T? Response { get; init; }

    [JsonRequired]
    public required int Status { get; init; }

    public string? Msg { get; init; }
}
