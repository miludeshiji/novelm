using System.Text.Json.Serialization;

namespace NovelM_App.Infrastructure.SignalR;

internal sealed class ComicPublishingListCategoryDto
{
    [JsonRequired]
    public required string Name { get; init; }
}

internal sealed class ComicPublishingListItemDto
{
    [JsonRequired]
    public required long Id { get; init; }

    public string? Type { get; init; }

    [JsonRequired]
    public required string Title { get; init; }

    [JsonRequired]
    public required string Cover { get; init; }

    [JsonRequired]
    public required DateTimeOffset LastUpdatedAt { get; init; }

    public ComicPublishingListCategoryDto? Category { get; init; }
}

internal sealed class ComicPublishingListResponseDto
{
    [JsonRequired]
    public required IReadOnlyList<ComicPublishingListItemDto?> Data { get; init; }

    [JsonRequired]
    public required int Page { get; init; }

    [JsonRequired]
    public required int TotalPages { get; init; }
}

internal sealed class ComicPublishingCategoryDto
{
    [JsonRequired]
    public required int Id { get; init; }

    [JsonRequired]
    public required string Name { get; init; }
}

internal sealed class ComicPublishingChapterDto
{
    [JsonRequired]
    public required long Id { get; init; }

    public int? SortNum { get; init; }

    [JsonRequired]
    public required string Title { get; init; }
}

internal sealed class ComicPublishingBookEditDto
{
    [JsonRequired]
    public required long Id { get; init; }

    [JsonRequired]
    public required string Type { get; init; }

    [JsonRequired]
    public required string Cover { get; init; }

    [JsonRequired]
    public required string Title { get; init; }

    public string? Author { get; init; }

    [JsonRequired]
    public required string Introduction { get; init; }

    [JsonRequired]
    public required int CategoryId { get; init; }

    [JsonRequired]
    public required int Level { get; init; }

    [JsonRequired]
    public required int InteriorLevel { get; init; }

    [JsonRequired]
    public required bool DownloadAllowed { get; init; }

    public ComicExtraDto? Extra { get; init; }

    [JsonRequired]
    public required IReadOnlyList<ComicPublishingChapterDto?> Chapters { get; init; }
}

internal sealed class ComicBookEditResponseDto
{
    [JsonRequired]
    public required ComicPublishingBookEditDto Book { get; init; }

    [JsonRequired]
    public required IReadOnlyList<ComicPublishingCategoryDto?> Categories { get; init; }
}

internal sealed class ComicChapterEditResponseDto
{
    [JsonRequired]
    public required string Title { get; init; }

    public IReadOnlyList<string?>? Images { get; init; }
}

internal sealed class ComicChapterCreateResponseDto
{
    [JsonRequired]
    public required IReadOnlyList<ComicPublishingChapterDto?> Chapters { get; init; }

    [JsonRequired]
    public required long NewCid { get; init; }
}

internal sealed class UploadComicImageResponseDto
{
    [JsonRequired]
    public required string Url { get; init; }
}
