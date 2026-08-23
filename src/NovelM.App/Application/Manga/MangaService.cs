using NovelM_App.Application.Abstractions;
using NovelM_App.Domain.Common;
using NovelM_App.Domain.Errors;
using NovelM_App.Domain.Manga;

namespace NovelM_App.Application.Manga;

public sealed class MangaService : IMangaService
{
    private readonly IMangaApi _mangaApi;

    public MangaService(IMangaApi mangaApi)
    {
        _mangaApi = mangaApi;
    }

    public Task<PageResult<MangaListItem>> GetListAsync(
        int page,
        int size,
        ComicOrder order,
        CancellationToken cancellationToken)
    {
        ValidatePaging(page, size);
        return _mangaApi.GetListAsync(page, size, order, cancellationToken);
    }

    public Task<PageResult<MangaListItem>> SearchAsync(
        string keywords,
        string mode,
        int page,
        int size,
        CancellationToken cancellationToken)
    {
        ValidatePaging(page, size);

        var normalizedKeywords = keywords.Trim();
        if (normalizedKeywords.Length == 0)
        {
            throw new AppException(
                AppErrorKind.Validation,
                "Search keywords must not be empty.");
        }

        return _mangaApi.SearchAsync(
            normalizedKeywords,
            "fuzzy",
            page,
            size,
            cancellationToken);
    }

    public Task<MangaSeriesDetails> GetSeriesAsync(
        string seriesTitle,
        ComicOrder order,
        CancellationToken cancellationToken)
    {
        var normalizedSeriesTitle = seriesTitle.Trim();
        if (normalizedSeriesTitle.Length == 0)
        {
            throw new AppException(
                AppErrorKind.Validation,
                "Series title must not be empty.");
        }

        return _mangaApi.GetSeriesAsync(
            normalizedSeriesTitle,
            order,
            cancellationToken);
    }

    private static void ValidatePaging(int page, int size)
    {
        if (page <= 0)
        {
            throw new AppException(
                AppErrorKind.Validation,
                "Page number must be greater than zero.");
        }

        if (size <= 0)
        {
            throw new AppException(
                AppErrorKind.Validation,
                "Page size must be greater than zero.");
        }
    }
}
