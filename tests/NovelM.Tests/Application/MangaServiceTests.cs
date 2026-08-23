using NovelM_App.Application.Abstractions;
using NovelM_App.Application.Manga;
using NovelM_App.Domain.Common;
using NovelM_App.Domain.Errors;
using NovelM_App.Domain.Manga;

namespace NovelM.Tests.Application;

[TestClass]
public sealed class MangaServiceTests
{
    [TestMethod]
    public async Task SearchAsync_TrimsKeywordAndUsesFuzzyMode()
    {
        var api = new FakeMangaApi();
        var service = new MangaService(api);
        using var cancellation = new CancellationTokenSource();

        var result = await service.SearchAsync(
            "  芙莉莲  ",
            "exact",
            2,
            24,
            cancellation.Token);

        Assert.AreSame(api.SearchPage, result);
        Assert.AreEqual(1, api.SearchCallCount);
        Assert.AreEqual("芙莉莲", api.Keywords);
        Assert.AreEqual("fuzzy", api.Mode);
        Assert.AreEqual(2, api.Page);
        Assert.AreEqual(24, api.Size);
        Assert.AreEqual(cancellation.Token, api.CancellationToken);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    public async Task GetListAsync_InvalidPage_ThrowsValidation(int page)
    {
        var api = new FakeMangaApi();
        var service = new MangaService(api);

        var exception = await Assert.ThrowsExactlyAsync<AppException>(() =>
            service.GetListAsync(page, 24, ComicOrder.Latest, CancellationToken.None));

        Assert.AreEqual(AppErrorKind.Validation, exception.Kind);
        Assert.AreEqual(0, api.TotalCallCount);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    public async Task GetListAsync_InvalidPageSize_ThrowsValidation(int size)
    {
        var api = new FakeMangaApi();
        var service = new MangaService(api);

        var exception = await Assert.ThrowsExactlyAsync<AppException>(() =>
            service.GetListAsync(1, size, ComicOrder.Latest, CancellationToken.None));

        Assert.AreEqual(AppErrorKind.Validation, exception.Kind);
        Assert.AreEqual(0, api.TotalCallCount);
    }

    [TestMethod]
    [DataRow(0, 24)]
    [DataRow(1, 0)]
    public async Task SearchAsync_InvalidPaging_ThrowsValidation(int page, int size)
    {
        var api = new FakeMangaApi();
        var service = new MangaService(api);

        var exception = await Assert.ThrowsExactlyAsync<AppException>(() =>
            service.SearchAsync("芙莉莲", "ignored", page, size, CancellationToken.None));

        Assert.AreEqual(AppErrorKind.Validation, exception.Kind);
        Assert.AreEqual(0, api.TotalCallCount);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public async Task SearchAsync_BlankTrimmedKeyword_ThrowsValidation(string keywords)
    {
        var api = new FakeMangaApi();
        var service = new MangaService(api);

        var exception = await Assert.ThrowsExactlyAsync<AppException>(() =>
            service.SearchAsync(keywords, "ignored", 1, 24, CancellationToken.None));

        Assert.AreEqual(AppErrorKind.Validation, exception.Kind);
        Assert.AreEqual(0, api.TotalCallCount);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public async Task GetSeriesAsync_BlankSeriesTitle_ThrowsValidation(string seriesTitle)
    {
        var api = new FakeMangaApi();
        var service = new MangaService(api);

        var exception = await Assert.ThrowsExactlyAsync<AppException>(() =>
            service.GetSeriesAsync(seriesTitle, ComicOrder.View, CancellationToken.None));

        Assert.AreEqual(AppErrorKind.Validation, exception.Kind);
        Assert.AreEqual(0, api.TotalCallCount);
    }

    [TestMethod]
    public async Task GetListAsync_ValidInput_DelegatesUnchanged()
    {
        var api = new FakeMangaApi();
        var service = new MangaService(api);
        using var cancellation = new CancellationTokenSource();

        var result = await service.GetListAsync(
            3,
            12,
            ComicOrder.New,
            cancellation.Token);

        Assert.AreSame(api.ListPage, result);
        Assert.AreEqual(1, api.ListCallCount);
        Assert.AreEqual(3, api.Page);
        Assert.AreEqual(12, api.Size);
        Assert.AreEqual(ComicOrder.New, api.Order);
        Assert.AreEqual(cancellation.Token, api.CancellationToken);
    }

    [TestMethod]
    public async Task GetSeriesAsync_ValidInput_TrimsTitleAndDelegates()
    {
        var api = new FakeMangaApi();
        var service = new MangaService(api);
        using var cancellation = new CancellationTokenSource();

        var result = await service.GetSeriesAsync(
            "  葬送的芙莉莲  ",
            ComicOrder.View,
            cancellation.Token);

        Assert.AreSame(api.Series, result);
        Assert.AreEqual(1, api.SeriesCallCount);
        Assert.AreEqual("葬送的芙莉莲", api.SeriesTitle);
        Assert.AreEqual(ComicOrder.View, api.Order);
        Assert.AreEqual(cancellation.Token, api.CancellationToken);
    }

    private sealed class FakeMangaApi : IMangaApi
    {
        private static readonly MangaListItem Item = new(
            "葬送的芙莉莲",
            "葬送的芙莉莲",
            null,
            "cover.jpg",
            1,
            DateTimeOffset.UnixEpoch);

        public PageResult<MangaListItem> ListPage { get; } =
            new([Item], 3, 5);

        public PageResult<MangaListItem> SearchPage { get; } =
            new([Item], 2, 4);

        public MangaSeriesDetails Series { get; } = new(
            "frieren",
            "葬送的芙莉莲",
            null,
            "cover.jpg",
            "山田钟人",
            100,
            20,
            "Introduction",
            "Chapter 1",
            DateTimeOffset.UnixEpoch,
            ["奇幻"],
            []);

        public int ListCallCount { get; private set; }

        public int SearchCallCount { get; private set; }

        public int SeriesCallCount { get; private set; }

        public int TotalCallCount => ListCallCount + SearchCallCount + SeriesCallCount;

        public int Page { get; private set; }

        public int Size { get; private set; }

        public ComicOrder Order { get; private set; }

        public string? Keywords { get; private set; }

        public string? Mode { get; private set; }

        public string? SeriesTitle { get; private set; }

        public CancellationToken CancellationToken { get; private set; }

        public Task<PageResult<MangaListItem>> GetListAsync(
            int page,
            int size,
            ComicOrder order,
            CancellationToken cancellationToken)
        {
            ListCallCount++;
            Page = page;
            Size = size;
            Order = order;
            CancellationToken = cancellationToken;
            return Task.FromResult(ListPage);
        }

        public Task<PageResult<MangaListItem>> SearchAsync(
            string keywords,
            string mode,
            int page,
            int size,
            CancellationToken cancellationToken)
        {
            SearchCallCount++;
            Keywords = keywords;
            Mode = mode;
            Page = page;
            Size = size;
            CancellationToken = cancellationToken;
            return Task.FromResult(SearchPage);
        }

        public Task<MangaSeriesDetails> GetSeriesAsync(
            string seriesTitle,
            ComicOrder order,
            CancellationToken cancellationToken)
        {
            SeriesCallCount++;
            SeriesTitle = seriesTitle;
            Order = order;
            CancellationToken = cancellationToken;
            return Task.FromResult(Series);
        }
    }
}
