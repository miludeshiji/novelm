namespace NovelM_App.Domain.Publishing;

public sealed record MyComicSummary(
    long Id,
    string Type,
    string Title,
    string Cover,
    string CategoryName,
    DateTimeOffset LastUpdatedAt);

public sealed record CreateComicDraft(
    string Cover,
    string Title,
    string Author,
    string Introduction,
    string CategoryName);

public sealed record ComicInfoDraft(
    string Cover,
    string Title,
    string Author,
    string Introduction,
    int CategoryId);

public sealed record ComicSettingsDraft(
    int Level,
    int InteriorLevel,
    bool DownloadAllowed,
    long? SubjectId,
    long? SeriesId,
    string SeriesName,
    string SeriesNameCn,
    IReadOnlyList<string> Tags);

public sealed record ComicChapterSummary(
    long Id,
    int SortNum,
    string Title);

public sealed record ComicChapterDraft(
    long Id,
    string Title,
    IReadOnlyList<string> Images);

public sealed record LocalImageFile(
    string FileName,
    byte[] Content);

public sealed record UploadedImage(
    string FileName,
    string Url);

public sealed record FailedImage(
    string FileName,
    string Message);

public sealed record ImageUploadBatchResult(
    IReadOnlyList<UploadedImage> Successes,
    IReadOnlyList<FailedImage> Failures);

public sealed record ComicCategory(
    int Id,
    string Name);

public sealed record ComicEditDetails(
    long Id,
    string Type,
    string Cover,
    string Title,
    string Author,
    string Introduction,
    int CategoryId,
    IReadOnlyList<ComicCategory> Categories,
    int Level,
    int InteriorLevel,
    bool DownloadAllowed,
    long? SubjectId,
    long? SeriesId,
    string SeriesName,
    string SeriesNameCn,
    IReadOnlyList<string> Tags,
    IReadOnlyList<ComicChapterSummary> Chapters);

public sealed record CreateChapterResult(
    long NewChapterId,
    IReadOnlyList<ComicChapterSummary> Chapters);
