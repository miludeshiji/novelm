using NovelM_App.Application.Abstractions;
using NovelM_App.Application.Books;
using NovelM_App.Domain.Books;
using NovelM_App.Domain.Errors;

namespace NovelM.Tests.Application;

[TestClass]
public sealed class BookServiceTests
{
    [TestMethod]
    [DataRow(0L)]
    [DataRow(-1L)]
    public async Task GetBookAsync_InvalidBookId_RejectsBeforeApi(long bookId)
    {
        var api = new FakeBookApi();
        var service = new BookService(api);

        var exception = await Assert.ThrowsExactlyAsync<AppException>(() =>
            service.GetBookAsync(bookId, CancellationToken.None));

        Assert.AreEqual(AppErrorKind.Validation, exception.Kind);
        Assert.AreEqual(0, api.CallCount);
    }

    [TestMethod]
    [DataRow(0L)]
    [DataRow(-1L)]
    public async Task GetChapterAsync_InvalidBookId_RejectsBeforeApi(long bookId)
    {
        var api = new FakeBookApi();
        var service = new BookService(api);

        var exception = await Assert.ThrowsExactlyAsync<AppException>(() =>
            service.GetChapterAsync(bookId, 1, CancellationToken.None));

        Assert.AreEqual(AppErrorKind.Validation, exception.Kind);
        Assert.AreEqual(0, api.CallCount);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    public async Task GetChapterAsync_InvalidSortNum_RejectsBeforeApi(int sortNum)
    {
        var api = new FakeBookApi();
        var service = new BookService(api);

        var exception = await Assert.ThrowsExactlyAsync<AppException>(() =>
            service.GetChapterAsync(42, sortNum, CancellationToken.None));

        Assert.AreEqual(AppErrorKind.Validation, exception.Kind);
        Assert.AreEqual(0, api.CallCount);
    }

    [TestMethod]
    public async Task GetBookAsync_ValidInput_DelegatesUnchanged()
    {
        var api = new FakeBookApi();
        var service = new BookService(api);
        using var cancellation = new CancellationTokenSource();

        var result = await service.GetBookAsync(42, cancellation.Token);

        Assert.AreSame(api.Book, result);
        Assert.AreEqual(1, api.CallCount);
        Assert.AreEqual(42L, api.BookId);
        Assert.AreEqual(cancellation.Token, api.CancellationToken);
    }

    [TestMethod]
    public async Task GetChapterAsync_ValidInput_DelegatesUnchanged()
    {
        var api = new FakeBookApi();
        var service = new BookService(api);
        using var cancellation = new CancellationTokenSource();

        var result = await service.GetChapterAsync(42, 7, cancellation.Token);

        Assert.AreSame(api.Chapter, result);
        Assert.AreEqual(1, api.CallCount);
        Assert.AreEqual(42L, api.BookId);
        Assert.AreEqual(7, api.SortNum);
        Assert.AreEqual(cancellation.Token, api.CancellationToken);
    }

    private sealed class FakeBookApi : IBookApi
    {
        public BookDetails Book { get; } = new(
            42,
            "Book title",
            "Author",
            "cover.png",
            "Introduction",
            Array.Empty<ChapterSummary>());

        public ChapterContent Chapter { get; } =
            new(70, 42, 7, "Chapter title", "Chapter content");

        public int CallCount { get; private set; }

        public long BookId { get; private set; }

        public int? SortNum { get; private set; }

        public CancellationToken CancellationToken { get; private set; }

        public Task<BookDetails> GetBookAsync(
            long bookId,
            CancellationToken cancellationToken)
        {
            CallCount++;
            BookId = bookId;
            CancellationToken = cancellationToken;
            return Task.FromResult(Book);
        }

        public Task<ChapterContent> GetChapterAsync(
            long bookId,
            int sortNum,
            CancellationToken cancellationToken)
        {
            CallCount++;
            BookId = bookId;
            SortNum = sortNum;
            CancellationToken = cancellationToken;
            return Task.FromResult(Chapter);
        }
    }
}
