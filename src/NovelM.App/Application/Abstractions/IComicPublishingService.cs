using NovelM_App.Domain.Common;
using NovelM_App.Domain.Publishing;

namespace NovelM_App.Application.Abstractions;

public interface IComicPublishingService
{
    Task<PageResult<MyComicSummary>> GetMyComicsAsync(
        int page,
        int size,
        string keywords,
        CancellationToken cancellationToken);

    Task<long> CreateComicAsync(
        CreateComicDraft draft,
        CancellationToken cancellationToken);

    Task DeleteComicAsync(
        long bookId,
        CancellationToken cancellationToken);

    Task<ComicEditDetails> GetEditDetailsAsync(
        long bookId,
        CancellationToken cancellationToken);

    Task UpdateInfoAsync(
        long bookId,
        ComicInfoDraft draft,
        CancellationToken cancellationToken);

    Task UpdateSettingsAsync(
        long bookId,
        ComicSettingsDraft draft,
        int maximumInteriorLevel,
        CancellationToken cancellationToken);

    Task<ComicChapterDraft> GetChapterAsync(
        long bookId,
        long chapterId,
        CancellationToken cancellationToken);

    Task UpdateChapterAsync(
        long chapterId,
        ComicChapterDraft draft,
        CancellationToken cancellationToken);

    Task<CreateChapterResult> CreateChapterAsync(
        long bookId,
        int sortNum,
        ComicChapterDraft draft,
        CancellationToken cancellationToken);

    Task DeleteChapterAsync(
        long bookId,
        int sortNum,
        CancellationToken cancellationToken);

    Task ReorderChapterAsync(
        long bookId,
        int oldSortNum,
        int newSortNum,
        CancellationToken cancellationToken);

    Task<ImageUploadBatchResult> UploadImagesAsync(
        IReadOnlyList<LocalImageSource> files,
        CancellationToken cancellationToken);
}
