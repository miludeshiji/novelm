using System.Text.Json.Serialization;

namespace NovelM_App.Infrastructure.SignalR;

internal sealed class RoleDto
{
    [JsonRequired]
    public required string Name { get; init; }
}

internal sealed class UserProfileDto
{
    [JsonRequired]
    public required long Id { get; init; }

    [JsonRequired]
    public required string UserName { get; init; }

    [JsonRequired]
    public required string Avatar { get; init; }

    [JsonRequired]
    public required RoleDto Role { get; init; }

    public int? InteriorLevel { get; init; }
}

internal sealed class ChapterSummaryDto
{
    [JsonRequired]
    public required long Id { get; init; }

    [JsonRequired]
    public required string Title { get; init; }
}

internal sealed class BookDto
{
    [JsonRequired]
    public required long Id { get; init; }

    [JsonRequired]
    public required string Title { get; init; }

    public string? Author { get; init; }

    public string? Arthur { get; init; }

    [JsonRequired]
    public required string Cover { get; init; }

    [JsonRequired]
    public required string Introduction { get; init; }

    [JsonRequired]
    public required IReadOnlyList<ChapterSummaryDto> Chapter { get; init; }
}

internal sealed class BookResponseDto
{
    [JsonRequired]
    public required BookDto Book { get; init; }
}

internal sealed class ChapterDto
{
    [JsonRequired]
    public required long Id { get; init; }

    [JsonRequired]
    public required long BookId { get; init; }

    [JsonRequired]
    public required int SortNum { get; init; }

    [JsonRequired]
    public required string Title { get; init; }

    [JsonRequired]
    public required string Content { get; init; }
}

internal sealed class ChapterResponseDto
{
    [JsonRequired]
    public required ChapterDto Chapter { get; init; }
}
