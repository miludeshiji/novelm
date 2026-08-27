using NovelM_App.Application.Abstractions;
using NovelM_App.Domain.Common;
using NovelM_App.Domain.Errors;
using NovelM_App.Domain.Publishing;

namespace NovelM_App.Application.Publishing;

public sealed class ComicPublishingService : IComicPublishingService
{
    private const int MaximumUploadConcurrency = 3;

    private static readonly HashSet<string> ComicCategories =
        new(StringComparer.Ordinal)
        {
            "原创",
            "连载",
            "完结"
        };

    private readonly IComicPublishingApi _publishingApi;
    private readonly ILocalImageReader _localImageReader;

    public ComicPublishingService(
        IComicPublishingApi publishingApi,
        ILocalImageReader localImageReader)
    {
        _publishingApi = publishingApi;
        _localImageReader = localImageReader;
    }

    public async Task<PageResult<MyComicSummary>> GetMyComicsAsync(
        int page,
        int size,
        string keywords,
        CancellationToken cancellationToken)
    {
        ValidatePaging(page, size);

        var result = await _publishingApi.GetMyBooksAsync(
            page,
            size,
            NormalizeOptionalText(keywords),
            cancellationToken);

        return new PageResult<MyComicSummary>(
            result.Items
                .Where(item => string.Equals(
                    item.Type,
                    "Comic",
                    StringComparison.OrdinalIgnoreCase))
                .ToArray(),
            result.Page,
            result.TotalPages);
    }

    public Task<long> CreateComicAsync(
        CreateComicDraft draft,
        CancellationToken cancellationToken)
    {
        var normalizedDraft = new CreateComicDraft(
            NormalizeHttpsUrl(draft.Cover),
            NormalizeRequiredText(draft.Title, "Title"),
            NormalizeRequiredText(draft.Author, "Author"),
            NormalizeRequiredText(draft.Introduction, "Introduction"),
            NormalizeRequiredText(draft.CategoryName, "Category name"));

        if (!ComicCategories.Contains(normalizedDraft.CategoryName))
        {
            throw Validation("Comic category is not supported.");
        }

        return _publishingApi.QuickCreateComicAsync(
            normalizedDraft,
            cancellationToken);
    }

    public Task DeleteComicAsync(
        long bookId,
        CancellationToken cancellationToken)
    {
        ValidatePositiveId(bookId, "Book id");
        return _publishingApi.DeleteBookAsync(bookId, cancellationToken);
    }

    public Task<ComicEditDetails> GetEditDetailsAsync(
        long bookId,
        CancellationToken cancellationToken)
    {
        ValidatePositiveId(bookId, "Book id");
        return _publishingApi.GetBookEditInfoAsync(bookId, cancellationToken);
    }

    public Task UpdateInfoAsync(
        long bookId,
        ComicInfoDraft draft,
        CancellationToken cancellationToken)
    {
        ValidatePositiveId(bookId, "Book id");
        if (draft.CategoryId <= 0)
        {
            throw Validation("Category id must be greater than zero.");
        }

        var normalizedDraft = new ComicInfoDraft(
            NormalizeHttpsUrl(draft.Cover),
            NormalizeRequiredText(draft.Title, "Title"),
            NormalizeRequiredText(draft.Author, "Author"),
            NormalizeRequiredText(draft.Introduction, "Introduction"),
            draft.CategoryId);

        return _publishingApi.UpdateComicInfoAsync(
            bookId,
            normalizedDraft,
            cancellationToken);
    }

    public Task UpdateSettingsAsync(
        long bookId,
        ComicSettingsDraft draft,
        int maximumInteriorLevel,
        CancellationToken cancellationToken)
    {
        ValidatePositiveId(bookId, "Book id");

        if (draft.Level is < 0 or > 6)
        {
            throw Validation("Level must be between zero and six.");
        }

        if (draft.InteriorLevel < 0 ||
            draft.InteriorLevel > maximumInteriorLevel)
        {
            throw Validation("Interior level exceeds the permitted range.");
        }

        ValidateOptionalPositiveId(draft.SubjectId, "Subject id");
        ValidateOptionalPositiveId(draft.SeriesId, "Series id");

        var normalizedDraft = new ComicSettingsDraft(
            draft.Level,
            draft.InteriorLevel,
            draft.DownloadAllowed,
            draft.SubjectId,
            draft.SeriesId,
            NormalizeOptionalText(draft.SeriesName),
            NormalizeOptionalText(draft.SeriesNameCn),
            draft.Tags
                .Select(NormalizeOptionalText)
                .Where(tag => tag.Length > 0)
                .ToArray());

        return _publishingApi.UpdateComicSettingsAsync(
            bookId,
            normalizedDraft,
            cancellationToken);
    }

    public Task<ComicChapterDraft> GetChapterAsync(
        long bookId,
        long chapterId,
        CancellationToken cancellationToken)
    {
        ValidatePositiveId(bookId, "Book id");
        ValidatePositiveId(chapterId, "Chapter id");
        return _publishingApi.GetComicEditInfoAsync(
            bookId,
            chapterId,
            cancellationToken);
    }

    public Task UpdateChapterAsync(
        long chapterId,
        ComicChapterDraft draft,
        CancellationToken cancellationToken)
    {
        ValidatePositiveId(chapterId, "Chapter id");
        if (draft.Images.Count == 0)
        {
            throw Validation("A comic chapter must contain at least one image.");
        }

        var normalizedDraft = NormalizeChapterDraft(draft);
        return _publishingApi.UpdateComicChapterAsync(
            chapterId,
            normalizedDraft,
            cancellationToken);
    }

    public Task<CreateChapterResult> CreateChapterAsync(
        long bookId,
        int sortNum,
        ComicChapterDraft draft,
        CancellationToken cancellationToken)
    {
        ValidatePositiveId(bookId, "Book id");
        ValidatePositiveSortNumber(sortNum, "Sort number");

        if (draft.Images.Count == 0)
        {
            throw Validation("A comic chapter must contain at least one image.");
        }

        var normalizedDraft = NormalizeChapterDraft(draft);
        return _publishingApi.CreateNewComicChapterAsync(
            bookId,
            sortNum,
            normalizedDraft,
            cancellationToken);
    }

    public Task DeleteChapterAsync(
        long bookId,
        int sortNum,
        CancellationToken cancellationToken)
    {
        ValidatePositiveId(bookId, "Book id");
        ValidatePositiveSortNumber(sortNum, "Sort number");
        return _publishingApi.DeleteChapterAsync(
            bookId,
            sortNum,
            cancellationToken);
    }

    public Task ReorderChapterAsync(
        long bookId,
        int oldSortNum,
        int newSortNum,
        CancellationToken cancellationToken)
    {
        ValidatePositiveId(bookId, "Book id");
        ValidatePositiveSortNumber(oldSortNum, "Old sort number");
        ValidatePositiveSortNumber(newSortNum, "New sort number");
        return _publishingApi.ReorderChapterAsync(
            bookId,
            oldSortNum,
            newSortNum,
            cancellationToken);
    }

    public async Task<ImageUploadBatchResult> UploadImagesAsync(
        IReadOnlyList<LocalImageSource> files,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var sortedFiles = files
            .Select((file, index) => new IndexedFile(file, index))
            .OrderBy(item => item.File.FileName, NaturalNameComparer.Instance)
            .ThenBy(item => item.OriginalIndex)
            .Select(item => item.File)
            .ToArray();

        if (sortedFiles.Length == 0)
        {
            return new ImageUploadBatchResult([], []);
        }

        using var semaphore = new SemaphoreSlim(
            MaximumUploadConcurrency,
            MaximumUploadConcurrency);
        var successes = new UploadedImage?[sortedFiles.Length];
        var failures = new FailedImage?[sortedFiles.Length];

        var uploads = sortedFiles
            .Select((file, index) => UploadOneAsync(
                file,
                index,
                semaphore,
                successes,
                failures,
                cancellationToken))
            .ToArray();

        await Task.WhenAll(uploads);

        return new ImageUploadBatchResult(
            successes.OfType<UploadedImage>().ToArray(),
            failures.OfType<FailedImage>().ToArray());
    }

    private async Task UploadOneAsync(
        LocalImageSource file,
        int index,
        SemaphoreSlim semaphore,
        UploadedImage?[] successes,
        FailedImage?[] failures,
        CancellationToken cancellationToken)
    {
        await semaphore.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var content = await _localImageReader.ReadAsync(
                    file.FilePath,
                    cancellationToken);
                var url = await _publishingApi.UploadImageAsync(
                    new LocalImageFile(file.FileName, content),
                    cancellationToken);
                successes[index] = new UploadedImage(file.FileName, url, file.Id);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (AppException exception) when (
                exception.Kind == AppErrorKind.Unauthorized)
            {
                throw;
            }
            catch (Exception exception)
            {
                failures[index] = new FailedImage(
                    file.FileName,
                    exception.Message,
                    file.Id);
            }
        }
        finally
        {
            semaphore.Release();
        }
    }

    private static ComicChapterDraft NormalizeChapterDraft(
        ComicChapterDraft draft)
    {
        var normalizedImages = draft.Images
            .Select(NormalizeOptionalText)
            .ToArray();
        if (normalizedImages.Any(image => image.Length == 0))
        {
            throw Validation("Comic chapter images must not be empty.");
        }

        return new ComicChapterDraft(
            draft.Id,
            NormalizeRequiredText(draft.Title, "Chapter title"),
            normalizedImages);
    }

    private static string NormalizeHttpsUrl(string value)
    {
        var normalizedValue = NormalizeRequiredText(value, "Cover");
        if (!Uri.TryCreate(normalizedValue, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(uri.Host))
        {
            throw Validation("Cover must be an absolute HTTPS URL.");
        }

        return normalizedValue;
    }

    private static string NormalizeRequiredText(string value, string fieldName)
    {
        var normalizedValue = NormalizeOptionalText(value);
        if (normalizedValue.Length == 0)
        {
            throw Validation($"{fieldName} must not be empty.");
        }

        return normalizedValue;
    }

    private static string NormalizeOptionalText(string value) =>
        value?.Trim() ?? string.Empty;

    private static void ValidatePaging(int page, int size)
    {
        if (page <= 0)
        {
            throw Validation("Page number must be greater than zero.");
        }

        if (size <= 0)
        {
            throw Validation("Page size must be greater than zero.");
        }
    }

    private static void ValidatePositiveId(long id, string fieldName)
    {
        if (id <= 0)
        {
            throw Validation($"{fieldName} must be greater than zero.");
        }
    }

    private static void ValidateOptionalPositiveId(long? id, string fieldName)
    {
        if (id is <= 0)
        {
            throw Validation($"{fieldName} must be greater than zero when provided.");
        }
    }

    private static void ValidatePositiveSortNumber(int sortNum, string fieldName)
    {
        if (sortNum <= 0)
        {
            throw Validation($"{fieldName} must be greater than zero.");
        }
    }

    private static AppException Validation(string message) =>
        new(AppErrorKind.Validation, message);

    private sealed record IndexedFile(
        LocalImageSource File,
        int OriginalIndex);
}
