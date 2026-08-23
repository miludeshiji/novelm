using System.Text.Json;
using NovelM_App.Application.Abstractions;
using NovelM_App.Domain.Common;
using NovelM_App.Domain.Errors;
using NovelM_App.Domain.Publishing;

namespace NovelM_App.Infrastructure.SignalR;

internal sealed class SignalRComicPublishingApi : IComicPublishingApi
{
    private readonly ISignalRConnection _connection;

    public SignalRComicPublishingApi(ISignalRConnection connection)
    {
        _connection = connection;
    }

    public async Task<PageResult<MyComicSummary>> GetMyBooksAsync(
        int page,
        int size,
        string keywords,
        CancellationToken cancellationToken)
    {
        var response = await _connection.InvokeAsync<ComicPublishingListResponseDto>(
            HubMethodNames.GetMyBooks,
            new { Page = page, Size = size, Type = "Comic", KeyWords = keywords },
            cancellationToken);
        var items = RequireNonNullElements(
                response.Data,
                HubMethodNames.GetMyBooks,
                "Data")
            .Select(item => new MyComicSummary(
                item.Id,
                item.Type ?? "Comic",
                item.Title,
                item.Cover,
                item.Category?.Name ?? string.Empty,
                item.LastUpdatedAt))
            .ToArray();

        return new PageResult<MyComicSummary>(
            items,
            response.Page,
            response.TotalPages);
    }

    public Task<long> QuickCreateComicAsync(
        CreateComicDraft draft,
        CancellationToken cancellationToken)
    {
        return _connection.InvokeAsync<long>(
            HubMethodNames.QuickCreateComic,
            new
            {
                draft.Cover,
                draft.Title,
                draft.Author,
                draft.Introduction,
                draft.CategoryName
            },
            cancellationToken);
    }

    public async Task DeleteBookAsync(
        long bookId,
        CancellationToken cancellationToken)
    {
        _ = await _connection.InvokeAsync<JsonElement?>(
            HubMethodNames.DeleteBook,
            new { Id = bookId },
            cancellationToken);
    }

    public async Task<ComicEditDetails> GetBookEditInfoAsync(
        long bookId,
        CancellationToken cancellationToken)
    {
        var response = await _connection.InvokeAsync<ComicBookEditResponseDto>(
            HubMethodNames.GetBookEditInfo,
            new { Id = bookId },
            cancellationToken);
        var book = response.Book;
        var classification = book.Extra?.Classification;
        var categories = RequireNonNullElements(
                response.Categories,
                HubMethodNames.GetBookEditInfo,
                "Categories")
            .Where(category => category.Name is "原创" or "连载" or "完结")
            .Select(category => new ComicCategory(category.Id, category.Name))
            .ToArray();
        var chapters = MapChapters(
            book.Chapters,
            HubMethodNames.GetBookEditInfo,
            "Book.Chapters");
        var tags = RequireNonNullElements(
                classification?.Tags,
                HubMethodNames.GetBookEditInfo,
                "Book.Extra.classification.tags")
            .ToArray();

        return new ComicEditDetails(
            book.Id,
            book.Type,
            book.Cover,
            book.Title,
            book.Author ?? string.Empty,
            book.Introduction,
            book.CategoryId,
            categories,
            book.Level,
            book.InteriorLevel,
            book.DownloadAllowed,
            classification?.SubjectId,
            classification?.SeriesId,
            classification?.SeriesName ?? string.Empty,
            classification?.SeriesNameCn ?? string.Empty,
            tags,
            chapters);
    }

    public async Task UpdateComicInfoAsync(
        long bookId,
        ComicInfoDraft draft,
        CancellationToken cancellationToken)
    {
        _ = await _connection.InvokeAsync<JsonElement?>(
            HubMethodNames.UpdateBook,
            new
            {
                Id = bookId,
                Map = new
                {
                    draft.Cover,
                    draft.Title,
                    draft.Author,
                    draft.Introduction,
                    draft.CategoryId
                }
            },
            cancellationToken);
    }

    public async Task UpdateComicSettingsAsync(
        long bookId,
        ComicSettingsDraft draft,
        CancellationToken cancellationToken)
    {
        _ = await _connection.InvokeAsync<JsonElement?>(
            HubMethodNames.UpdateBook,
            new
            {
                Id = bookId,
                Map = new
                {
                    draft.Level,
                    draft.InteriorLevel,
                    draft.DownloadAllowed,
                    draft.SubjectId,
                    draft.SeriesId,
                    draft.SeriesName,
                    draft.SeriesNameCn,
                    draft.Tags
                }
            },
            cancellationToken);
    }

    public async Task<ComicChapterDraft> GetComicEditInfoAsync(
        long bookId,
        long chapterId,
        CancellationToken cancellationToken)
    {
        var response = await _connection.InvokeAsync<ComicChapterEditResponseDto>(
            HubMethodNames.GetComicEditInfo,
            new { Bid = bookId, Cid = chapterId },
            cancellationToken);
        var images = RequireNonNullElements(
                response.Images,
                HubMethodNames.GetComicEditInfo,
                "Images")
            .ToArray();

        return new ComicChapterDraft(chapterId, response.Title, images);
    }

    public async Task UpdateComicChapterAsync(
        long chapterId,
        ComicChapterDraft draft,
        CancellationToken cancellationToken)
    {
        _ = await _connection.InvokeAsync<JsonElement?>(
            HubMethodNames.UpdateComicChapter,
            new
            {
                Cid = chapterId,
                Map = new { draft.Title, draft.Images }
            },
            cancellationToken);
    }

    public async Task<CreateChapterResult> CreateNewComicChapterAsync(
        long bookId,
        int sortNum,
        ComicChapterDraft draft,
        CancellationToken cancellationToken)
    {
        var response = await _connection.InvokeAsync<ComicChapterCreateResponseDto>(
            HubMethodNames.CreateNewComicChapter,
            new
            {
                Bid = bookId,
                SortNum = sortNum,
                Map = new { draft.Title, draft.Images }
            },
            cancellationToken);
        var chapters = MapChapters(
            response.Chapters,
            HubMethodNames.CreateNewComicChapter,
            "Chapters");

        return new CreateChapterResult(response.NewCid, chapters);
    }

    public async Task DeleteChapterAsync(
        long bookId,
        int sortNum,
        CancellationToken cancellationToken)
    {
        _ = await _connection.InvokeAsync<JsonElement?>(
            HubMethodNames.DeleteChapter,
            new { Bid = bookId, SortNum = sortNum },
            cancellationToken);
    }

    public async Task ReorderChapterAsync(
        long bookId,
        int oldSortNum,
        int newSortNum,
        CancellationToken cancellationToken)
    {
        _ = await _connection.InvokeAsync<JsonElement?>(
            HubMethodNames.ReorderChapter,
            new
            {
                BookId = bookId,
                OldSortNum = oldSortNum,
                NewSortNum = newSortNum
            },
            cancellationToken);
    }

    public async Task<string> UploadImageAsync(
        LocalImageFile file,
        CancellationToken cancellationToken)
    {
        var response = await _connection.InvokeAsync<UploadComicImageResponseDto>(
            HubMethodNames.UploadImage,
            new { file.FileName, ImageData = file.Content },
            cancellationToken);

        return response.Url;
    }

    private static IReadOnlyList<ComicChapterSummary> MapChapters(
        IReadOnlyList<ComicPublishingChapterDto?> chapters,
        string methodName,
        string fieldName)
    {
        return RequireNonNullElements(chapters, methodName, fieldName)
            .Select((chapter, index) => new ComicChapterSummary(
                chapter.Id,
                chapter.SortNum ?? index + 1,
                chapter.Title))
            .ToArray();
    }

    private static IEnumerable<T> RequireNonNullElements<T>(
        IReadOnlyList<T?>? items,
        string methodName,
        string fieldName)
        where T : class
    {
        return items is null
            ? Array.Empty<T>()
            : items.Select(item => item ?? throw ProtocolError(methodName, fieldName));
    }

    private static AppException ProtocolError(string methodName, string fieldName)
    {
        return new AppException(
            AppErrorKind.Protocol,
            $"Hub method '{methodName}' response contained a null element in '{fieldName}'.");
    }
}
