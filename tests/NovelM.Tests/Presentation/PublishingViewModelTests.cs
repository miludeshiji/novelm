using NovelM_App.Application.Abstractions;
using NovelM_App.Domain.Auth;
using NovelM_App.Domain.Common;
using NovelM_App.Domain.Errors;
using NovelM_App.Domain.Publishing;
using NovelM_App.Presentation.Common;
using NovelM_App.Presentation.Publishing;

namespace NovelM.Tests.Presentation;

[TestClass]
public sealed class PublishingViewModelTests
{
    [TestMethod]
    public async Task LoadAsync_NoRestoredUser_ShowsLoginPromptWithoutManagementCall()
    {
        var auth = new FakeAuthService
        {
            RestoreHandler = _ => Task.FromResult<UserProfile?>(null)
        };
        var service = new FakeComicPublishingService();
        var viewModel = CreateViewModel(auth, service);

        await viewModel.LoadAsync(CancellationToken.None);

        Assert.AreEqual(1, auth.RestoreCalls);
        Assert.AreEqual(0, service.GetMyComicsCalls);
        Assert.IsTrue(viewModel.IsSignedOut);
        Assert.IsFalse(viewModel.IsCheckingSession);
        Assert.IsFalse(viewModel.IsWorkbenchVisible);
        Assert.IsNull(viewModel.CurrentUser);
    }

    [TestMethod]
    public async Task LoadAsync_RestoredUser_LoadsComicWorkbench()
    {
        var profile = Profile();
        var auth = new FakeAuthService
        {
            RestoreHandler = _ => Task.FromResult<UserProfile?>(profile)
        };
        var page = Page([Summary(1), Summary(2)], 1, 3);
        var service = new FakeComicPublishingService
        {
            GetMyComicsHandler = (_, _, _, _) => Task.FromResult(page)
        };
        var viewModel = CreateViewModel(auth, service);

        await viewModel.LoadAsync(CancellationToken.None);

        Assert.AreSame(profile, viewModel.CurrentUser);
        Assert.IsFalse(viewModel.IsSignedOut);
        Assert.IsTrue(viewModel.IsWorkbenchVisible);
        Assert.AreSame(page.Items, viewModel.Comics);
        Assert.AreEqual(3, viewModel.TotalPages);
        Assert.AreEqual((1, 24, string.Empty),
            (service.RequestedPage, service.RequestedSize, service.RequestedKeywords));
    }

    [TestMethod]
    public async Task LoadAsync_CurrentUserSkipsRestore()
    {
        var auth = new FakeAuthService { CurrentUser = Profile() };
        var service = new FakeComicPublishingService
        {
            GetMyComicsHandler = (_, _, _, _) => Task.FromResult(Page([], 1, 1))
        };
        var viewModel = CreateViewModel(auth, service);

        await viewModel.LoadAsync(CancellationToken.None);

        Assert.AreEqual(0, auth.RestoreCalls);
        Assert.AreSame(auth.CurrentUser, viewModel.CurrentUser);
        Assert.IsTrue(viewModel.IsWorkbenchVisible);
    }

    [TestMethod]
    public async Task SearchAsync_ResetsToFirstPageAndUsesKeyword()
    {
        var service = new FakeComicPublishingService
        {
            GetMyComicsHandler = (page, _, keyword, _) => Task.FromResult(
                Page([Summary(page)], page, 3))
        };
        var viewModel = CreateLoadedViewModel(service);
        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.NextPageAsync(CancellationToken.None);
        viewModel.SearchText = "  magic  ";

        await viewModel.SearchAsync(CancellationToken.None);

        Assert.AreEqual(1, viewModel.CurrentPage);
        Assert.AreEqual("magic", service.RequestedKeywords);
        Assert.AreEqual(1, service.RequestedPage);
    }

    [TestMethod]
    public async Task NextPageAsync_AtLastPage_DoesNotRequest()
    {
        var service = new FakeComicPublishingService
        {
            GetMyComicsHandler = (_, _, _, _) => Task.FromResult(Page([], 1, 1))
        };
        var viewModel = CreateLoadedViewModel(service);
        await viewModel.LoadAsync(CancellationToken.None);

        await viewModel.NextPageAsync(CancellationToken.None);

        Assert.AreEqual(1, service.GetMyComicsCalls);
    }

    [TestMethod]
    public async Task LoadAsync_Unauthorized_ReturnsToLoginPrompt()
    {
        var service = new FakeComicPublishingService
        {
            GetMyComicsHandler = (_, _, _, _) => Task.FromException<PageResult<MyComicSummary>>(
                new AppException(AppErrorKind.Unauthorized, "unsafe"))
        };
        var viewModel = CreateLoadedViewModel(service);

        await viewModel.LoadAsync(CancellationToken.None);

        Assert.IsTrue(viewModel.IsSignedOut);
        Assert.IsFalse(viewModel.IsWorkbenchVisible);
        Assert.IsNull(viewModel.CurrentUser);
        Assert.AreEqual(0, viewModel.Comics.Count);
        Assert.AreEqual("登录已失效，请重新登录。", viewModel.ErrorMessage);
    }

    [TestMethod]
    public async Task RefreshAsync_Failure_PreservesPreviousComicList()
    {
        var original = Page([Summary(1), Summary(2)], 2, 4);
        var service = new FakeComicPublishingService
        {
            GetMyComicsHandler = (_, _, _, _) => Task.FromResult(original)
        };
        var viewModel = CreateLoadedViewModel(service);
        await viewModel.LoadAsync(CancellationToken.None);
        viewModel.SelectComic(original.Items[1]);
        service.GetMyComicsHandler = (_, _, _, _) => Task.FromException<PageResult<MyComicSummary>>(
            new AppException(AppErrorKind.Transport, "unsafe"));

        await viewModel.RefreshAsync(CancellationToken.None);

        Assert.AreSame(original.Items, viewModel.Comics);
        Assert.AreEqual(2, viewModel.CurrentPage);
        Assert.AreEqual(4, viewModel.TotalPages);
        Assert.AreSame(original.Items[1], viewModel.SelectedComic);
    }

    [TestMethod]
    public void RequestAccountNavigation_RaisesSingleEvent()
    {
        var viewModel = CreateViewModel(new FakeAuthService(), new FakeComicPublishingService());
        var calls = 0;
        viewModel.AccountNavigationRequested += (_, _) => calls++;

        viewModel.RequestAccountNavigation();

        Assert.AreEqual(1, calls);
    }

    [TestMethod]
    public async Task CreateAsync_SuccessRefreshesAndSelectsNewComic()
    {
        var old = Summary(1);
        var created = Summary(88);
        var listCalls = 0;
        var service = new FakeComicPublishingService
        {
            CreateComicHandler = (_, _) => Task.FromResult(88L),
            GetMyComicsHandler = (_, _, _, _) => Task.FromResult(
                ++listCalls == 1 ? Page([old], 1, 1) : Page([old, created], 1, 1)),
            GetEditDetailsHandler = (_, _) => Task.FromResult(ComicEditorViewModelTests.Details() with { Id = 88 })
        };
        var viewModel = CreateLoadedViewModel(service);
        await viewModel.LoadAsync(CancellationToken.None);
        var draft = CreateDraft();

        await viewModel.CreateComicAsync(draft, CancellationToken.None);

        Assert.AreEqual(88L, viewModel.SelectedComic?.Id);
        Assert.AreEqual(88L, viewModel.Editor.BookId);
        Assert.AreEqual(1, service.CreateComicCalls);
        Assert.AreEqual(2, service.GetMyComicsCalls);
        Assert.AreEqual(1, service.GetEditDetailsCalls);
    }

    [TestMethod]
    public async Task DeleteSelectedAsync_SelectsAdjacentComic()
    {
        var one = Summary(1);
        var two = Summary(2);
        var three = Summary(3);
        var service = new FakeComicPublishingService
        {
            GetMyComicsHandler = (_, _, _, _) => Task.FromResult(Page([one, two, three], 1, 1)),
            DeleteComicHandler = (_, _) => Task.CompletedTask,
            GetEditDetailsHandler = (bookId, _) => Task.FromResult(
                ComicEditorViewModelTests.Details() with { Id = bookId })
        };
        var viewModel = CreateLoadedViewModel(service);
        await viewModel.LoadAsync(CancellationToken.None);
        viewModel.SelectComic(two);

        await viewModel.DeleteSelectedAsync(CancellationToken.None);

        Assert.AreEqual(2L, service.DeletedBookId);
        Assert.AreEqual(3L, viewModel.SelectedComic?.Id);
        CollectionAssert.AreEqual(new long[] { 1, 3 }, viewModel.Comics.Select(x => x.Id).ToArray());
        Assert.AreEqual(3L, viewModel.Editor.BookId);
    }

    [TestMethod]
    public async Task SelectComicAsync_DirtyEditorRequiresExplicitDiscard()
    {
        var one = Summary(1);
        var two = Summary(2);
        var service = new FakeComicPublishingService
        {
            GetMyComicsHandler = (_, _, _, _) => Task.FromResult(Page([one, two], 1, 1)),
            GetEditDetailsHandler = (id, _) => Task.FromResult(
                ComicEditorViewModelTests.Details() with { Id = id })
        };
        var viewModel = CreateLoadedViewModel(service);
        await viewModel.LoadAsync(CancellationToken.None);
        Assert.IsTrue(await viewModel.SelectComicAsync(one, false, CancellationToken.None));
        viewModel.Editor.Title = "dirty";

        var rejected = await viewModel.SelectComicAsync(two, false, CancellationToken.None);

        Assert.IsFalse(rejected);
        Assert.AreEqual(1, service.GetEditDetailsCalls);
        Assert.AreEqual(1L, viewModel.SelectedComic?.Id);
        Assert.IsTrue(viewModel.Editor.HasUnsavedChanges);

        Assert.IsTrue(await viewModel.SelectComicAsync(two, true, CancellationToken.None));
        Assert.AreEqual(2L, viewModel.SelectedComic?.Id);
    }

    [TestMethod]
    public async Task DeleteSelectedAsync_LastComicClearsEditor()
    {
        var comic = Summary(1);
        var service = new FakeComicPublishingService
        {
            GetMyComicsHandler = (_, _, _, _) => Task.FromResult(Page([comic], 1, 1)),
            DeleteComicHandler = (_, _) => Task.CompletedTask,
            GetEditDetailsHandler = (_, _) => Task.FromResult(
                ComicEditorViewModelTests.Details() with { Id = 1 })
        };
        var viewModel = CreateLoadedViewModel(service);
        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.SelectComicAsync(comic, false, CancellationToken.None);

        await viewModel.DeleteSelectedAsync(CancellationToken.None);

        Assert.AreEqual(0, viewModel.Comics.Count);
        Assert.IsNull(viewModel.SelectedComic);
        Assert.IsNull(viewModel.Editor.BookId);
        Assert.IsFalse(viewModel.Editor.IsLoaded);
    }

    [TestMethod]
    public async Task EditorUnauthorizedEvent_ReturnsPublishingToSignedOutState()
    {
        var comic = Summary(1);
        var service = new FakeComicPublishingService
        {
            GetMyComicsHandler = (_, _, _, _) => Task.FromResult(Page([comic], 1, 1)),
            GetEditDetailsHandler = (_, _) => Task.FromResult(
                ComicEditorViewModelTests.Details() with { Id = 1 }),
            UpdateInfoHandler = (_, _, _) => Task.FromException(
                new AppException(AppErrorKind.Unauthorized, "unsafe"))
        };
        var viewModel = CreateLoadedViewModel(service);
        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.SelectComicAsync(comic, false, CancellationToken.None);
        viewModel.Editor.Title = "dirty";

        await viewModel.Editor.SaveInfoAsync(CancellationToken.None);

        Assert.IsTrue(viewModel.IsSignedOut);
        Assert.AreEqual(0, viewModel.Comics.Count);
        Assert.IsNull(viewModel.SelectedComic);
        Assert.IsNull(viewModel.Editor.BookId);
        Assert.AreEqual("登录已失效，请重新登录。", viewModel.ErrorMessage);
    }

    [TestMethod]
    public async Task Cancellation_IsRethrownAndBusyFlagsReset()
    {
        using var source = new CancellationTokenSource();
        var service = new FakeComicPublishingService
        {
            GetMyComicsHandler = (_, _, _, token) => Task.FromException<PageResult<MyComicSummary>>(
                new OperationCanceledException(token))
        };
        var viewModel = CreateLoadedViewModel(service);

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            viewModel.LoadAsync(source.Token));

        Assert.IsFalse(viewModel.IsCheckingSession);
        Assert.IsFalse(viewModel.IsBusy);
        Assert.IsNull(viewModel.ErrorMessage);
    }

    private static PublishingViewModel CreateLoadedViewModel(FakeComicPublishingService service) =>
        CreateViewModel(new FakeAuthService { CurrentUser = Profile() }, service);

    private static PublishingViewModel CreateViewModel(
        IAuthService authService,
        IComicPublishingService publishingService)
    {
        var mapper = new ErrorMessageMapper();
        var editor = new ComicEditorViewModel(publishingService, mapper);
        return new PublishingViewModel(authService, publishingService, editor, mapper);
    }

    private static UserProfile Profile() => ComicEditorViewModelTests.Profile();

    private static MyComicSummary Summary(long id) => new(
        id,
        "Comic",
        $"Comic {id}",
        $"cover-{id}.jpg",
        "Fantasy",
        DateTimeOffset.UnixEpoch.AddDays(id));

    private static PageResult<MyComicSummary> Page(
        IReadOnlyList<MyComicSummary> comics,
        int page,
        int totalPages) => new(comics, page, totalPages);

    private static CreateComicDraft CreateDraft() => new(
        "https://images.example/cover.jpg",
        "Created",
        "Author",
        "Introduction",
        "Fantasy");

    private sealed class FakeAuthService : IAuthService
    {
        public Func<CancellationToken, Task<UserProfile?>>? RestoreHandler { get; set; }
        public UserProfile? CurrentUser { get; set; }
        public int RestoreCalls { get; private set; }

        public async Task<UserProfile?> RestoreAsync(CancellationToken cancellationToken)
        {
            RestoreCalls++;
            var user = await (RestoreHandler?.Invoke(cancellationToken)
                ?? throw new AssertFailedException("RestoreAsync was not expected."));
            CurrentUser = user;
            return user;
        }

        public Task<UserProfile> LoginAsync(string email, string rawPassword, CancellationToken cancellationToken) =>
            throw new AssertFailedException("LoginAsync was not expected.");

        public Task LogoutAsync(CancellationToken cancellationToken) =>
            throw new AssertFailedException("LogoutAsync was not expected.");
    }
}
