using NovelM_App.Application.Abstractions;
using NovelM_App.Domain.Common;
using NovelM_App.Domain.Errors;
using NovelM_App.Domain.Manga;
using NovelM_App.Presentation.Common;
using NovelM_App.Presentation.Manga;

namespace NovelM.Tests.Presentation;

[TestClass]
public sealed class MangaViewModelTests
{
    [TestMethod]
    public async Task LoadAsync_NoKeyword_UsesListAndEnablesSort()
    {
        var page = Page("latest", page: 1, totalPages: 3);
        var service = new FakeMangaService
        {
            GetListHandler = (_, _, _, _) => Task.FromResult(page)
        };
        var viewModel = CreateViewModel(service);
        using var cancellation = new CancellationTokenSource();

        await viewModel.LoadAsync(cancellation.Token);

        Assert.AreEqual(1, service.ListRequests.Count);
        Assert.AreEqual(0, service.SearchRequests.Count);
        Assert.AreEqual((1, 24, ComicOrder.Latest), service.ListRequests[0].Arguments);
        Assert.AreEqual(cancellation.Token, service.ListRequests[0].CancellationToken);
        Assert.AreSame(page.Items, viewModel.Items);
        Assert.AreEqual(1, viewModel.CurrentPage);
        Assert.AreEqual(3, viewModel.TotalPages);
        Assert.IsTrue(viewModel.IsSortEnabled);
        Assert.IsFalse(viewModel.IsBusy);
    }

    [TestMethod]
    public async Task SearchAsync_WithKeyword_UsesSearchAndDisablesSort()
    {
        var service = new FakeMangaService
        {
            GetListHandler = (page, _, _, _) => Task.FromResult(Page("cached", page, 3)),
            SearchHandler = (_, _, page, _, _) => Task.FromResult(Page("search", page, 2))
        };
        var viewModel = CreateViewModel(service);
        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.NextPageAsync(CancellationToken.None);
        viewModel.SearchText = "芙莉莲";

        await viewModel.SearchAsync(CancellationToken.None);

        Assert.AreEqual(1, service.SearchRequests.Count);
        Assert.AreEqual(("芙莉莲", "fuzzy", 1, 24), service.SearchRequests[0].Arguments);
        Assert.AreEqual(1, viewModel.CurrentPage);
        Assert.AreEqual("search", viewModel.Items[0].SeriesTitle);
        Assert.IsFalse(viewModel.IsSortEnabled);
    }

    [TestMethod]
    public async Task SearchAsync_PaddedKeyword_PassesTrimmedSnapshot()
    {
        var service = new FakeMangaService
        {
            SearchHandler = (_, _, page, _, _) => Task.FromResult(Page("search", page, 1))
        };
        var viewModel = CreateViewModel(service);
        viewModel.SearchText = "  芙莉莲  ";

        await viewModel.SearchAsync(CancellationToken.None);

        Assert.AreEqual(1, service.SearchRequests.Count);
        Assert.AreEqual("芙莉莲", service.SearchRequests[0].Keywords);
        Assert.AreEqual("  芙莉莲  ", viewModel.SearchText);
        Assert.IsFalse(viewModel.IsSortEnabled);
    }

    [TestMethod]
    public async Task SearchAsync_BlankKeyword_UsesListAndEnablesSort()
    {
        var service = new FakeMangaService
        {
            GetListHandler = (page, _, _, _) => Task.FromResult(Page("catalog", page, 1))
        };
        var viewModel = CreateViewModel(service);
        viewModel.SearchText = "   ";

        await viewModel.SearchAsync(CancellationToken.None);

        Assert.AreEqual(1, service.ListRequests.Count);
        Assert.AreEqual(0, service.SearchRequests.Count);
        Assert.IsTrue(viewModel.IsSortEnabled);
        Assert.AreEqual("catalog", viewModel.Items[0].SeriesTitle);
    }

    [TestMethod]
    public async Task ChangeOrderAsync_NoKeyword_UpdatesOrderAndReturnsToFirstPage()
    {
        var service = new FakeMangaService
        {
            GetListHandler = (page, _, order, _) =>
                Task.FromResult(Page($"{order}-{page}", page, 3))
        };
        var viewModel = CreateViewModel(service);
        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.NextPageAsync(CancellationToken.None);

        await viewModel.ChangeOrderAsync(ComicOrder.View, CancellationToken.None);

        Assert.AreEqual(ComicOrder.View, viewModel.SelectedOrder);
        Assert.AreEqual(1, viewModel.CurrentPage);
        Assert.AreEqual((1, 24, ComicOrder.View), service.ListRequests[^1].Arguments);
        Assert.AreEqual("View-1", viewModel.Items[0].SeriesTitle);
    }

    [TestMethod]
    public async Task ChangeOrderAsync_WithKeyword_StillUsesSearchWithoutOrderArgument()
    {
        var service = new FakeMangaService
        {
            SearchHandler = (keywords, _, page, _, _) =>
                Task.FromResult(Page($"{keywords}-{page}", page, 2))
        };
        var viewModel = CreateViewModel(service);
        viewModel.SearchText = "芙莉莲";

        await viewModel.ChangeOrderAsync(ComicOrder.New, CancellationToken.None);

        Assert.AreEqual(ComicOrder.New, viewModel.SelectedOrder);
        Assert.AreEqual(0, service.ListRequests.Count);
        Assert.AreEqual(1, service.SearchRequests.Count);
        Assert.AreEqual(("芙莉莲", "fuzzy", 1, 24), service.SearchRequests[0].Arguments);
        Assert.IsFalse(viewModel.IsSortEnabled);
    }

    [TestMethod]
    public async Task SlowerOldRequest_DoesNotOverwriteLatestResult()
    {
        var oldCompletion = NewPageCompletion();
        var newCompletion = NewPageCompletion();
        var service = new FakeMangaService
        {
            SearchHandler = (keywords, _, _, _, _) => keywords == "old"
                ? oldCompletion.Task
                : newCompletion.Task
        };
        var viewModel = CreateViewModel(service);
        viewModel.SearchText = "old";
        var oldRequest = viewModel.SearchAsync(CancellationToken.None);
        viewModel.SearchText = "new";
        var newRequest = viewModel.SearchAsync(CancellationToken.None);

        newCompletion.SetResult(Page("new", page: 1, totalPages: 2));
        await newRequest;
        Assert.IsFalse(viewModel.IsBusy);

        oldCompletion.SetResult(Page("old", page: 1, totalPages: 7));
        await oldRequest;

        Assert.AreEqual("new", viewModel.Items[0].SeriesTitle);
        Assert.AreEqual(2, viewModel.TotalPages);
        Assert.IsNull(viewModel.ErrorMessage);
        Assert.IsFalse(viewModel.IsBusy);
    }

    [TestMethod]
    public async Task OlderFailure_DoesNotSetErrorOrClearBusyForLatestRequest()
    {
        var oldCompletion = NewPageCompletion();
        var newCompletion = NewPageCompletion();
        var service = new FakeMangaService
        {
            SearchHandler = (keywords, _, _, _, _) => keywords == "old"
                ? oldCompletion.Task
                : newCompletion.Task
        };
        var viewModel = CreateViewModel(service);
        viewModel.SearchText = "old";
        var oldRequest = viewModel.SearchAsync(CancellationToken.None);
        viewModel.SearchText = "new";
        var newRequest = viewModel.SearchAsync(CancellationToken.None);

        oldCompletion.SetException(Error(AppErrorKind.Transport));
        await oldRequest;

        Assert.IsTrue(viewModel.IsBusy);
        Assert.IsNull(viewModel.ErrorMessage);

        newCompletion.SetResult(Page("new", page: 1, totalPages: 1));
        await newRequest;

        Assert.AreEqual("new", viewModel.Items[0].SeriesTitle);
        Assert.IsFalse(viewModel.IsBusy);
    }

    [TestMethod]
    public async Task BeginRequest_ReentrantLatestFailurePreservesLatestState()
    {
        var cached = Page("cached", page: 1, totalPages: 2);
        var stale = Page("stale", page: 1, totalPages: 9);
        var service = new FakeMangaService
        {
            GetListHandler = (_, _, _, _) => Task.FromResult(cached)
        };
        var viewModel = CreateViewModel(service);
        await viewModel.LoadAsync(CancellationToken.None);

        var requestCount = 0;
        service.GetListHandler = (_, _, _, _) => ++requestCount == 1
            ? Task.FromException<PageResult<MangaListItem>>(
                Error(AppErrorKind.Transport))
            : Task.FromResult(stale);
        var didReenter = false;
        Task? latestRequest = null;
        viewModel.PropertyChanged += (_, args) =>
        {
            if (!didReenter
                && args.PropertyName == nameof(MangaViewModel.IsBusy)
                && viewModel.IsBusy)
            {
                didReenter = true;
                latestRequest = viewModel.LoadAsync(CancellationToken.None);
            }
        };

        await viewModel.LoadAsync(CancellationToken.None);
        await latestRequest!;

        Assert.IsTrue(didReenter);
        Assert.AreEqual(2, requestCount);
        Assert.AreSame(cached.Items, viewModel.Items);
        Assert.AreEqual(1, viewModel.CurrentPage);
        Assert.AreEqual(2, viewModel.TotalPages);
        Assert.AreEqual("网络连接失败，请检查网络后重试。", viewModel.ErrorMessage);
        Assert.IsTrue(viewModel.HasError);
        Assert.IsFalse(viewModel.IsBusy);
    }

    [TestMethod]
    public async Task ListSuccess_ItemsNotificationReentryPreservesAllLatestState()
    {
        var stale = Page("stale", page: 1, totalPages: 2);
        var latest = Page("latest", page: 3, totalPages: 7);
        var requestCount = 0;
        var service = new FakeMangaService
        {
            GetListHandler = (_, _, _, _) => Task.FromResult(
                ++requestCount == 1 ? stale : latest)
        };
        var viewModel = CreateViewModel(service);
        var didReenter = false;
        Task? latestRequest = null;
        viewModel.PropertyChanged += (_, args) =>
        {
            if (!didReenter
                && args.PropertyName == nameof(MangaViewModel.Items))
            {
                didReenter = true;
                latestRequest = viewModel.LoadAsync(CancellationToken.None);
            }
        };

        await viewModel.LoadAsync(CancellationToken.None);
        await latestRequest!;

        Assert.IsTrue(didReenter);
        Assert.AreEqual(2, requestCount);
        Assert.AreSame(latest.Items, viewModel.Items);
        Assert.AreEqual(3, viewModel.CurrentPage);
        Assert.AreEqual(7, viewModel.TotalPages);
        Assert.IsFalse(viewModel.IsBusy);
    }

    [TestMethod]
    public async Task FailedPageLoad_PreservesPreviousItems()
    {
        var cached = Page("cached", page: 1, totalPages: 2);
        var service = new FakeMangaService
        {
            GetListHandler = (page, _, _, _) => page == 1
                ? Task.FromResult(cached)
                : Task.FromException<PageResult<MangaListItem>>(
                    Error(AppErrorKind.Transport))
        };
        var viewModel = CreateViewModel(service);
        await viewModel.LoadAsync(CancellationToken.None);

        await viewModel.NextPageAsync(CancellationToken.None);

        Assert.AreSame(cached.Items, viewModel.Items);
        Assert.AreEqual(1, viewModel.CurrentPage);
        Assert.AreEqual(2, viewModel.TotalPages);
        Assert.AreEqual("网络连接失败，请检查网络后重试。", viewModel.ErrorMessage);
        Assert.IsTrue(viewModel.HasError);
        Assert.IsFalse(viewModel.IsBusy);
    }

    [TestMethod]
    public async Task SuccessfulLoad_ClearsPreviousError()
    {
        var attempts = 0;
        var service = new FakeMangaService
        {
            GetListHandler = (_, _, _, _) => ++attempts == 1
                ? Task.FromException<PageResult<MangaListItem>>(
                    Error(AppErrorKind.Protocol))
                : Task.FromResult(Page("recovered", page: 1, totalPages: 1))
        };
        var viewModel = CreateViewModel(service);
        await viewModel.LoadAsync(CancellationToken.None);
        Assert.IsTrue(viewModel.HasError);

        await viewModel.LoadAsync(CancellationToken.None);

        Assert.AreEqual("recovered", viewModel.Items[0].SeriesTitle);
        Assert.IsNull(viewModel.ErrorMessage);
        Assert.IsFalse(viewModel.HasError);
    }

    [TestMethod]
    public async Task OpenSeriesAsync_LoadsSelectedSeriesAndShowsDetails()
    {
        var item = Item("芙莉莲");
        var details = Details("芙莉莲");
        var service = new FakeMangaService
        {
            GetSeriesHandler = (_, _, _) => Task.FromResult(details)
        };
        var viewModel = CreateViewModel(service);
        viewModel.SelectedOrder = ComicOrder.View;

        await viewModel.OpenSeriesAsync(item, CancellationToken.None);

        Assert.AreEqual(1, service.SeriesRequests.Count);
        Assert.AreEqual(("芙莉莲", ComicOrder.View), service.SeriesRequests[0].Arguments);
        Assert.AreSame(details, viewModel.SelectedSeries);
        Assert.IsTrue(viewModel.IsDetailsVisible);
        Assert.IsFalse(viewModel.IsCatalogVisible);
    }

    [TestMethod]
    public async Task OpenSeriesAsync_FailurePreservesCatalogAndPreviousDetails()
    {
        var cachedPage = Page("cached", page: 1, totalPages: 2);
        var oldDetails = Details("old");
        var attempts = 0;
        var service = new FakeMangaService
        {
            GetListHandler = (_, _, _, _) => Task.FromResult(cachedPage),
            GetSeriesHandler = (_, _, _) => ++attempts == 1
                ? Task.FromResult(oldDetails)
                : Task.FromException<MangaSeriesDetails>(Error(AppErrorKind.Protocol))
        };
        var viewModel = CreateViewModel(service);
        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.OpenSeriesAsync(Item("old"), CancellationToken.None);

        await viewModel.OpenSeriesAsync(Item("failed"), CancellationToken.None);

        Assert.AreSame(cachedPage.Items, viewModel.Items);
        Assert.AreSame(oldDetails, viewModel.SelectedSeries);
        Assert.AreEqual("服务器响应格式不兼容。", viewModel.ErrorMessage);
        Assert.IsTrue(viewModel.IsDetailsVisible);
    }

    [TestMethod]
    public async Task Cancellation_IsRethrownWithoutUserError()
    {
        var cancellation = new CancellationTokenSource();
        var service = new FakeMangaService
        {
            GetListHandler = (_, _, _, token) =>
                Task.FromException<PageResult<MangaListItem>>(
                    new OperationCanceledException(token))
        };
        var viewModel = CreateViewModel(service);

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            viewModel.LoadAsync(cancellation.Token));

        Assert.IsNull(viewModel.ErrorMessage);
        Assert.IsFalse(viewModel.HasError);
        Assert.IsFalse(viewModel.IsBusy);
    }

    [TestMethod]
    public async Task BackToCatalog_PreservesQueryOrderPageAndItems()
    {
        var details = Details("芙莉莲");
        var service = new FakeMangaService
        {
            SearchHandler = (_, _, page, _, _) => Task.FromResult(Page($"page-{page}", page, 3)),
            GetSeriesHandler = (_, _, _) => Task.FromResult(details)
        };
        var viewModel = CreateViewModel(service);
        viewModel.SearchText = "芙莉莲";
        viewModel.SelectedOrder = ComicOrder.New;
        await viewModel.SearchAsync(CancellationToken.None);
        await viewModel.NextPageAsync(CancellationToken.None);
        var items = viewModel.Items;
        await viewModel.OpenSeriesAsync(items[0], CancellationToken.None);

        viewModel.BackToCatalog();

        Assert.IsNull(viewModel.SelectedSeries);
        Assert.IsFalse(viewModel.IsDetailsVisible);
        Assert.IsTrue(viewModel.IsCatalogVisible);
        Assert.AreEqual("芙莉莲", viewModel.SearchText);
        Assert.AreEqual(ComicOrder.New, viewModel.SelectedOrder);
        Assert.AreEqual(2, viewModel.CurrentPage);
        Assert.AreSame(items, viewModel.Items);
    }

    [TestMethod]
    public async Task PreviousPageAsync_AtFirstPage_DoesNotRequest()
    {
        var service = new FakeMangaService();
        var viewModel = CreateViewModel(service);

        await viewModel.PreviousPageAsync(CancellationToken.None);

        Assert.AreEqual(0, service.TotalRequestCount);
        Assert.AreEqual(1, viewModel.CurrentPage);
    }

    [TestMethod]
    public async Task NextPageAsync_AtLastPage_DoesNotRequest()
    {
        var service = new FakeMangaService();
        var viewModel = CreateViewModel(service);

        await viewModel.NextPageAsync(CancellationToken.None);

        Assert.AreEqual(0, service.TotalRequestCount);
        Assert.AreEqual(1, viewModel.CurrentPage);
    }

    [TestMethod]
    public void ShowReaderUnavailable_SetsLocalizedNotice()
    {
        var viewModel = CreateViewModel(new FakeMangaService());

        viewModel.ShowReaderUnavailable(Chapter());

        Assert.AreEqual("漫画阅读器将在后续版本提供", viewModel.NoticeMessage);
    }

    [TestMethod]
    public async Task PropertyChanged_NotifiesAffectedBindingState()
    {
        var service = new FakeMangaService
        {
            GetListHandler = (_, _, _, _) => Task.FromResult(Page("item", 2, 3)),
            GetSeriesHandler = (_, _, _) => Task.FromResult(Details("item"))
        };
        var viewModel = CreateViewModel(service);
        var changedProperties = new List<string?>();
        viewModel.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);

        viewModel.SearchText = "query";
        viewModel.SearchText = string.Empty;
        viewModel.SelectedOrder = ComicOrder.New;
        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.OpenSeriesAsync(viewModel.Items[0], CancellationToken.None);
        viewModel.BackToCatalog();
        viewModel.ShowReaderUnavailable(Chapter());
        service.GetListHandler = (_, _, _, _) =>
            Task.FromException<PageResult<MangaListItem>>(Error(AppErrorKind.Transport));
        await viewModel.LoadAsync(CancellationToken.None);

        CollectionAssert.IsSubsetOf(
            new[]
            {
                nameof(MangaViewModel.SearchText),
                nameof(MangaViewModel.IsSortEnabled),
                nameof(MangaViewModel.SelectedOrder),
                nameof(MangaViewModel.Items),
                nameof(MangaViewModel.CurrentPage),
                nameof(MangaViewModel.TotalPages),
                nameof(MangaViewModel.IsBusy),
                nameof(MangaViewModel.ErrorMessage),
                nameof(MangaViewModel.HasError),
                nameof(MangaViewModel.NoticeMessage),
                nameof(MangaViewModel.SelectedSeries),
                nameof(MangaViewModel.IsDetailsVisible),
                nameof(MangaViewModel.IsCatalogVisible)
            },
            changedProperties);
    }

    private static MangaViewModel CreateViewModel(IMangaService service)
    {
        return new MangaViewModel(service, new ErrorMessageMapper());
    }

    private static TaskCompletionSource<PageResult<MangaListItem>> NewPageCompletion()
    {
        return new TaskCompletionSource<PageResult<MangaListItem>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private static PageResult<MangaListItem> Page(
        string seriesTitle,
        int page,
        int totalPages)
    {
        return new PageResult<MangaListItem>([Item(seriesTitle)], page, totalPages);
    }

    private static MangaListItem Item(string seriesTitle)
    {
        return new MangaListItem(
            seriesTitle,
            $"{seriesTitle} title",
            null,
            "cover.jpg",
            2,
            DateTimeOffset.UnixEpoch);
    }

    private static MangaSeriesDetails Details(string title)
    {
        return new MangaSeriesDetails(
            title,
            title,
            null,
            "cover.jpg",
            "author",
            100,
            20,
            "introduction",
            "chapter 2",
            DateTimeOffset.UnixEpoch,
            ["fantasy"],
            []);
    }

    private static MangaChapterSummary Chapter()
    {
        return new MangaChapterSummary(
            1,
            1,
            "chapter 1",
            DateTimeOffset.UnixEpoch,
            null,
            20,
            0);
    }

    private static AppException Error(AppErrorKind kind)
    {
        return new AppException(kind, "Synthetic safe detail");
    }

    private sealed class FakeMangaService : IMangaService
    {
        public Func<int, int, ComicOrder, CancellationToken, Task<PageResult<MangaListItem>>>?
            GetListHandler { get; set; }

        public Func<string, string, int, int, CancellationToken, Task<PageResult<MangaListItem>>>?
            SearchHandler { get; set; }

        public Func<string, ComicOrder, CancellationToken, Task<MangaSeriesDetails>>?
            GetSeriesHandler { get; set; }

        public List<ListRequest> ListRequests { get; } = [];

        public List<SearchRequest> SearchRequests { get; } = [];

        public List<SeriesRequest> SeriesRequests { get; } = [];

        public int TotalRequestCount =>
            ListRequests.Count + SearchRequests.Count + SeriesRequests.Count;

        public Task<PageResult<MangaListItem>> GetListAsync(
            int page,
            int size,
            ComicOrder order,
            CancellationToken cancellationToken)
        {
            ListRequests.Add(new ListRequest(page, size, order, cancellationToken));
            return GetListHandler?.Invoke(page, size, order, cancellationToken)
                ?? throw new AssertFailedException("GetListAsync was not expected.");
        }

        public Task<PageResult<MangaListItem>> SearchAsync(
            string keywords,
            string mode,
            int page,
            int size,
            CancellationToken cancellationToken)
        {
            SearchRequests.Add(
                new SearchRequest(keywords, mode, page, size, cancellationToken));
            return SearchHandler?.Invoke(keywords, mode, page, size, cancellationToken)
                ?? throw new AssertFailedException("SearchAsync was not expected.");
        }

        public Task<MangaSeriesDetails> GetSeriesAsync(
            string seriesTitle,
            ComicOrder order,
            CancellationToken cancellationToken)
        {
            SeriesRequests.Add(new SeriesRequest(seriesTitle, order, cancellationToken));
            return GetSeriesHandler?.Invoke(seriesTitle, order, cancellationToken)
                ?? throw new AssertFailedException("GetSeriesAsync was not expected.");
        }
    }

    private sealed record ListRequest(
        int Page,
        int Size,
        ComicOrder Order,
        CancellationToken CancellationToken)
    {
        public (int Page, int Size, ComicOrder Order) Arguments => (Page, Size, Order);
    }

    private sealed record SearchRequest(
        string Keywords,
        string Mode,
        int Page,
        int Size,
        CancellationToken CancellationToken)
    {
        public (string Keywords, string Mode, int Page, int Size) Arguments =>
            (Keywords, Mode, Page, Size);
    }

    private sealed record SeriesRequest(
        string SeriesTitle,
        ComicOrder Order,
        CancellationToken CancellationToken)
    {
        public (string SeriesTitle, ComicOrder Order) Arguments => (SeriesTitle, Order);
    }
}
