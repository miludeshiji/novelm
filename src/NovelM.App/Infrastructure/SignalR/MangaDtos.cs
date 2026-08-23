using System.Text.Json.Serialization;

namespace NovelM_App.Infrastructure.SignalR;

internal sealed class ComicListItemDto
{
    [JsonRequired]
    public required long Id { get; init; }

    [JsonRequired]
    public required string Title { get; init; }

    public string? OriginalTitle { get; init; }

    [JsonRequired]
    public required string Cover { get; init; }

    [JsonRequired]
    public required int Count { get; init; }

    [JsonRequired]
    public required DateTimeOffset LastUpdatedAt { get; init; }
}

internal sealed class ComicListResponseDto
{
    public IReadOnlyList<ComicListItemDto>? Data { get; init; }

    [JsonRequired]
    public required int Page { get; init; }

    [JsonRequired]
    public required int TotalPages { get; init; }
}

internal sealed class ComicClassificationDto
{
    [JsonPropertyName("author")]
    public string? Author { get; init; }

    [JsonPropertyName("subject_id")]
    public long? SubjectId { get; init; }

    [JsonPropertyName("series_id")]
    public long? SeriesId { get; init; }

    [JsonPropertyName("series_name")]
    public string? SeriesName { get; init; }

    [JsonPropertyName("series_name_cn")]
    public string? SeriesNameCn { get; init; }

    [JsonPropertyName("tags")]
    public IReadOnlyList<string>? Tags { get; init; }

    [JsonPropertyName("classified_at")]
    public DateTimeOffset? ClassifiedAt { get; init; }
}

internal sealed class ComicExtraDto
{
    [JsonPropertyName("classification")]
    public ComicClassificationDto? Classification { get; init; }
}

internal sealed class ComicSeriesDto
{
    [JsonRequired]
    public required string Id { get; init; }

    [JsonRequired]
    public required string Title { get; init; }

    public string? OriginalTitle { get; init; }

    [JsonRequired]
    public required string Cover { get; init; }

    public string? Author { get; init; }

    [JsonRequired]
    public required long Views { get; init; }

    [JsonRequired]
    public required long Favorite { get; init; }

    [JsonRequired]
    public required string Introduction { get; init; }

    [JsonRequired]
    public required DateTimeOffset CreatedAt { get; init; }

    [JsonRequired]
    public required string LastUpdatedChapter { get; init; }

    [JsonRequired]
    public required DateTimeOffset LastUpdatedAt { get; init; }

    public ComicExtraDto? Extra { get; init; }
}

internal sealed class ComicUploaderDto
{
    [JsonRequired]
    public required string UserName { get; init; }

    [JsonRequired]
    public required string Avatar { get; init; }
}

internal sealed class ComicReadPositionDto
{
    [JsonRequired]
    public required long ChapterId { get; init; }

    [JsonRequired]
    public required string Position { get; init; }

    public DateTimeOffset? ReadAt { get; init; }
}

internal sealed class ComicChapterSummaryDto
{
    [JsonRequired]
    public required long Id { get; init; }

    [JsonRequired]
    public required int SortNum { get; init; }

    [JsonRequired]
    public required string Title { get; init; }

    [JsonRequired]
    public required DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? UpdatedAt { get; init; }

    [JsonRequired]
    public required int PageCount { get; init; }

    [JsonRequired]
    public required int DownloadCost { get; init; }
}

internal sealed class ComicBookDto
{
    [JsonRequired]
    public required long Id { get; init; }

    [JsonRequired]
    public required string Title { get; init; }

    [JsonRequired]
    public required ComicUploaderDto Uploader { get; init; }

    [JsonRequired]
    public required bool CanDownload { get; init; }

    [JsonRequired]
    public required string Cover { get; init; }

    [JsonRequired]
    public required DateTimeOffset CreatedAt { get; init; }

    [JsonRequired]
    public required string LastUpdatedChapter { get; init; }

    [JsonRequired]
    public required DateTimeOffset LastUpdatedAt { get; init; }

    public ComicReadPositionDto? ReadPosition { get; init; }

    public IReadOnlyList<ComicChapterSummaryDto>? Chapters { get; init; }
}

internal sealed class ComicSeriesInfoResponseDto
{
    [JsonRequired]
    public required ComicSeriesDto Series { get; init; }

    public IReadOnlyList<ComicBookDto>? Books { get; init; }
}
