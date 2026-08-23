using NovelM_App.Application.Abstractions;
using NovelM_App.Domain.Common;
using NovelM_App.Domain.Manga;

namespace NovelM_App.Infrastructure.SignalR;

internal sealed class SignalRMangaApi : IMangaApi
{
    private readonly ISignalRConnection _connection;

    public SignalRMangaApi(ISignalRConnection connection)
    {
        _connection = connection;
    }

    public async Task<PageResult<MangaListItem>> GetListAsync(
        int page,
        int size,
        ComicOrder order,
        CancellationToken cancellationToken)
    {
        var response = await _connection.InvokeAsync<ComicListResponseDto>(
            HubMethodNames.GetComicList,
            new { Page = page, Size = size, Order = ToWireValue(order) },
            cancellationToken);

        return ToPageResult(response);
    }

    public async Task<PageResult<MangaListItem>> SearchAsync(
        string keywords,
        string mode,
        int page,
        int size,
        CancellationToken cancellationToken)
    {
        var response = await _connection.InvokeAsync<ComicListResponseDto>(
            HubMethodNames.SearchComicSeries,
            new { KeyWords = keywords, Mode = mode, Page = page, Size = size },
            cancellationToken);

        return ToPageResult(response);
    }

    public async Task<MangaSeriesDetails> GetSeriesAsync(
        string seriesTitle,
        ComicOrder order,
        CancellationToken cancellationToken)
    {
        var response = await _connection.InvokeAsync<ComicSeriesInfoResponseDto>(
            HubMethodNames.GetComicSeriesInfo,
            new { SeriesTitle = seriesTitle, Order = ToWireValue(order) },
            cancellationToken);
        var series = response.Series;
        var classification = series.Extra?.Classification;
        var author = !string.IsNullOrWhiteSpace(series.Author)
            ? series.Author
            : classification?.Author;
        var volumes = response.Books
            .Select(book => new MangaVolume(
                book.Id,
                book.Title,
                book.Cover,
                book.Uploader.UserName,
                book.Chapters
                    .Select(chapter => new MangaChapterSummary(
                        chapter.Id,
                        chapter.SortNum,
                        chapter.Title,
                        chapter.CreatedAt,
                        chapter.UpdatedAt,
                        chapter.PageCount,
                        chapter.DownloadCost))
                    .ToArray()))
            .ToArray();

        return new MangaSeriesDetails(
            series.Id,
            series.Title,
            series.OriginalTitle,
            series.Cover,
            author,
            series.Views,
            series.Favorite,
            series.Introduction,
            series.LastUpdatedChapter,
            series.LastUpdatedAt,
            classification?.Tags ?? Array.Empty<string>(),
            volumes);
    }

    private static PageResult<MangaListItem> ToPageResult(
        ComicListResponseDto response)
    {
        var items = response.Data
            .Select(item => new MangaListItem(
                item.Title,
                item.Title,
                item.OriginalTitle,
                item.Cover,
                item.Count,
                item.LastUpdatedAt))
            .ToArray();

        return new PageResult<MangaListItem>(
            items,
            response.Page,
            response.TotalPages);
    }

    private static string ToWireValue(ComicOrder order)
    {
        return order switch
        {
            ComicOrder.Latest => "latest",
            ComicOrder.New => "new",
            ComicOrder.View => "view",
            _ => throw new ArgumentOutOfRangeException(nameof(order), order, null)
        };
    }
}
