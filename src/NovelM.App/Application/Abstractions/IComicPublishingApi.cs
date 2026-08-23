using NovelM_App.Domain.Common;
using NovelM_App.Domain.Publishing;

namespace NovelM_App.Application.Abstractions;

public interface IComicPublishingApi
{
    Task<PageResult<MyComicSummary>> GetMyBooksAsync(
        int page,
        int size,
        string keywords,
        CancellationToken cancellationToken);

    Task<long> QuickCreateComicAsync(
        CreateComicDraft draft,
        CancellationToken cancellationToken);

    Task DeleteBookAsync(
        long bookId,
        CancellationToken cancellationToken);

    Task<ComicEditDetails> GetBookEditInfoAsync(
        long bookId,
        CancellationToken cancellationToken);

    Task UpdateComicInfoAsync(
        long bookId,
        ComicInfoDraft draft,
        CancellationToken cancellationToken);

    Task UpdateComicSettingsAsync(
        long bookId,
        ComicSettingsDraft draft,
        CancellationToken cancellationToken);

    Task<ComicChapterDraft> GetComicEditInfoAsync(
        long bookId,
        long chapterId,
        CancellationToken cancellationToken);

    Task UpdateComicChapterAsync(
        long chapterId,
        ComicChapterDraft draft,
        CancellationToken cancellationToken);

    Task<CreateChapterResult> CreateNewComicChapterAsync(
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

    Task<string> UploadImageAsync(
        LocalImageFile file,
        CancellationToken cancellationToken);
}
