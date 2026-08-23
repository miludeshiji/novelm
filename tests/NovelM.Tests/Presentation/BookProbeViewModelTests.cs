using NovelM_App.Application.Abstractions;
using NovelM_App.Domain.Books;
using NovelM_App.Domain.Errors;
using NovelM_App.Presentation.BookProbe;
using NovelM_App.Presentation.Common;

namespace NovelM.Tests.Presentation;

[TestClass]
public sealed class BookProbeViewModelTests
{
    [TestMethod]
    [DataRow("")]
    [DataRow("0")]
    [DataRow("-1")]
    [DataRow("not-a-number")]
    public async Task LoadBookCommand_InvalidId_ShowsInlineValidationWithoutServiceCall(
        string bookIdText)
    {
        var service = new FakeBookService();
        var viewModel = CreateViewModel(service);
        viewModel.BookIdText = bookIdText;

        await viewModel.LoadBookCommand.ExecuteAsync(null);

        Assert.AreEqual(0, service.GetBookCount);
        Assert.AreEqual("书籍 ID 必须是大于零的整数。", viewModel.ErrorMessage);
        Assert.IsTrue(viewModel.HasError);
        Assert.AreEqual(bookIdText, viewModel.BookIdText);
        Assert.IsNull(viewModel.Book);
        Assert.IsFalse(viewModel.IsBusy);
    }

    [TestMethod]
    public async Task LoadBookCommand_Success_PopulatesBookAndChapters()
    {
        var book = Book();
        var service = new FakeBookService
        {
            GetBookHandler = (_, _) => Task.FromResult(book)
        };
        var viewModel = CreateViewModel(service);
        viewModel.BookIdText = "42";

        await viewModel.LoadBookCommand.ExecuteAsync(null);

        Assert.AreEqual(1, service.GetBookCount);
        Assert.AreEqual(42L, service.BookId);
        Assert.AreSame(book, viewModel.Book);
        Assert.AreEqual(2, viewModel.Book!.Chapters.Count);
        Assert.IsNull(viewModel.Chapter);
        Assert.IsNull(viewModel.ErrorMessage);
        Assert.IsFalse(viewModel.HasError);
        Assert.IsFalse(viewModel.IsBusy);
    }

    [TestMethod]
    public async Task LoadBookCommand_LaterFailure_PreservesBookAndTypedId()
    {
        var book = Book();
        var service = new FakeBookService
        {
            GetBookHandler = (_, _) => Task.FromResult(book)
        };
        var viewModel = CreateViewModel(service);
        viewModel.BookIdText = "42";
        await viewModel.LoadBookCommand.ExecuteAsync(null);
        service.GetBookHandler = (_, _) => Task.FromException<BookDetails>(
            Error(AppErrorKind.Transport));
        viewModel.BookIdText = "99";

        await viewModel.LoadBookCommand.ExecuteAsync(null);

        Assert.AreEqual(2, service.GetBookCount);
        Assert.AreSame(book, viewModel.Book);
        Assert.AreEqual("99", viewModel.BookIdText);
        Assert.AreEqual(
            "网络连接失败，请检查网络后重试。",
            viewModel.ErrorMessage);
        Assert.IsTrue(viewModel.HasError);
        Assert.IsFalse(viewModel.IsBusy);
    }

    [TestMethod]
    public async Task LoadChapterCommand_Success_PopulatesPreview()
    {
        var book = Book();
        var content = Content(sortNum: 1, title: "First chapter");
        var service = new FakeBookService
        {
            GetBookHandler = (_, _) => Task.FromResult(book),
            GetChapterHandler = (_, _, _) => Task.FromResult(content)
        };
        var viewModel = CreateViewModel(service);
        viewModel.BookIdText = "42";
        await viewModel.LoadBookCommand.ExecuteAsync(null);

        await viewModel.LoadChapterCommand.ExecuteAsync(book.Chapters[0]);

        Assert.AreEqual(1, service.GetChapterCount);
        Assert.AreEqual(42L, service.BookId);
        Assert.AreEqual(1, service.SortNum);
        Assert.AreSame(content, viewModel.Chapter);
        Assert.IsNull(viewModel.ErrorMessage);
        Assert.IsFalse(viewModel.IsBusy);
    }

    [TestMethod]
    public async Task LoadChapterCommand_LaterFailure_PreservesLastPreview()
    {
        var book = Book();
        var content = Content(sortNum: 1, title: "First chapter");
        var service = new FakeBookService
        {
            GetBookHandler = (_, _) => Task.FromResult(book),
            GetChapterHandler = (_, _, _) => Task.FromResult(content)
        };
        var viewModel = CreateViewModel(service);
        viewModel.BookIdText = "42";
        await viewModel.LoadBookCommand.ExecuteAsync(null);
        await viewModel.LoadChapterCommand.ExecuteAsync(book.Chapters[0]);
        service.GetChapterHandler = (_, _, _) => Task.FromException<ChapterContent>(
            Error(AppErrorKind.Protocol));

        await viewModel.LoadChapterCommand.ExecuteAsync(book.Chapters[1]);

        Assert.AreEqual(2, service.GetChapterCount);
        Assert.AreSame(content, viewModel.Chapter);
        Assert.AreEqual("服务器响应格式不兼容。", viewModel.ErrorMessage);
        Assert.IsTrue(viewModel.HasError);
        Assert.IsFalse(viewModel.IsBusy);
    }

    private static BookProbeViewModel CreateViewModel(IBookService service)
    {
        return new BookProbeViewModel(service, new ErrorMessageMapper());
    }

    private static BookDetails Book()
    {
        return new BookDetails(
            42,
            "Book title",
            "Author",
            "cover.png",
            "Introduction",
            new[]
            {
                new ChapterSummary(700, "First chapter", 1),
                new ChapterSummary(701, "Second chapter", 2)
            });
    }

    private static ChapterContent Content(int sortNum, string title)
    {
        return new ChapterContent(700, 42, sortNum, title, "Chapter body");
    }

    private static AppException Error(AppErrorKind kind)
    {
        return new AppException(kind, "Synthetic safe detail");
    }

    private sealed class FakeBookService : IBookService
    {
        public Func<long, CancellationToken, Task<BookDetails>>? GetBookHandler
        {
            get;
            set;
        }

        public Func<long, int, CancellationToken, Task<ChapterContent>>? GetChapterHandler
        {
            get;
            set;
        }

        public int GetBookCount { get; private set; }

        public int GetChapterCount { get; private set; }

        public long BookId { get; private set; }

        public int SortNum { get; private set; }

        public Task<BookDetails> GetBookAsync(
            long bookId,
            CancellationToken cancellationToken)
        {
            GetBookCount++;
            BookId = bookId;
            return GetBookHandler?.Invoke(bookId, cancellationToken)
                ?? throw new AssertFailedException("GetBookAsync was not expected.");
        }

        public Task<ChapterContent> GetChapterAsync(
            long bookId,
            int sortNum,
            CancellationToken cancellationToken)
        {
            GetChapterCount++;
            BookId = bookId;
            SortNum = sortNum;
            return GetChapterHandler?.Invoke(bookId, sortNum, cancellationToken)
                ?? throw new AssertFailedException("GetChapterAsync was not expected.");
        }
    }
}
