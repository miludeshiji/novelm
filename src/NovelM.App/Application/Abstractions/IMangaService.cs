using NovelM_App.Domain.Common;
using NovelM_App.Domain.Manga;

namespace NovelM_App.Application.Abstractions;

public interface IMangaService
{
    Task<PageResult<MangaListItem>> GetListAsync(
        int page,
        int size,
        ComicOrder order,
        CancellationToken cancellationToken);

    Task<PageResult<MangaListItem>> SearchAsync(
        string keywords,
        string mode,
        int page,
        int size,
        CancellationToken cancellationToken);

    Task<MangaSeriesDetails> GetSeriesAsync(
        string seriesTitle,
        ComicOrder order,
        CancellationToken cancellationToken);
}
