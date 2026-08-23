namespace NovelM_App.Domain.Manga;

public enum ComicOrder
{
    Latest,
    New,
    View
}

public sealed record MangaListItem(
    string SeriesTitle,
    string Title,
    string? OriginalTitle,
    string Cover,
    int ChapterCount,
    DateTimeOffset LastUpdatedAt);

public sealed record MangaChapterSummary(
    long Id,
    int SortNum,
    string Title,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    int PageCount,
    int DownloadCost);

public sealed record MangaVolume(
    long Id,
    string Title,
    string Cover,
    string UploaderName,
    IReadOnlyList<MangaChapterSummary> Chapters);

public sealed record MangaSeriesDetails(
    string Id,
    string Title,
    string? OriginalTitle,
    string Cover,
    string? Author,
    long Views,
    long Favorite,
    string Introduction,
    string LastUpdatedChapter,
    DateTimeOffset LastUpdatedAt,
    IReadOnlyList<string> Tags,
    IReadOnlyList<MangaVolume> Volumes);
