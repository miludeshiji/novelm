using System.Collections.Concurrent;
using NovelM_App.Application.Abstractions;
using NovelM_App.Application.Publishing;
using NovelM_App.Domain.Common;
using NovelM_App.Domain.Errors;
using NovelM_App.Domain.Publishing;

namespace NovelM.Tests.Application;

[TestClass]
public sealed class ComicPublishingServiceTests
{
    [TestMethod]
    public async Task GetMyComicsAsync_DropsNonComicItems()
    {
        var comic = Summary(1, "Comic", "Comic one");
        var lowerCaseComic = Summary(2, "comic", "Comic two");
        var api = new FakeComicPublishingApi
        {
            MyBooksResult = new(
                [comic, Summary(3, "Novel", "Novel"), lowerCaseComic],
                4,
                9)
        };
        var service = new ComicPublishingService(api);
        using var cancellation = new CancellationTokenSource();

        var result = await service.GetMyComicsAsync(
            4,
            12,
            "  title  ",
            cancellation.Token);

        CollectionAssert.AreEqual(
            new[] { comic, lowerCaseComic },
            result.Items.ToArray());
        Assert.AreEqual(4, result.Page);
        Assert.AreEqual(9, result.TotalPages);
        Assert.AreEqual(1, api.GetMyBooksCallCount);
        Assert.AreEqual(4, api.Page);
        Assert.AreEqual(12, api.Size);
        Assert.AreEqual("title", api.Keywords);
        Assert.AreEqual(cancellation.Token, api.CancellationToken);
    }

    [TestMethod]
    public async Task CreateComicAsync_InvalidCover_ThrowsValidation()
    {
        var api = new FakeComicPublishingApi();
        var service = new ComicPublishingService(api);
        var draft = ValidCreateDraft() with { Cover = "http://cover" };

        var exception = await Assert.ThrowsExactlyAsync<AppException>(() =>
            service.CreateComicAsync(draft, CancellationToken.None));

        Assert.AreEqual(AppErrorKind.Validation, exception.Kind);
        Assert.AreEqual(0, api.TotalCallCount);
    }

    [TestMethod]
    [DataRow("", "Title", "Author", "Introduction", "原创")]
    [DataRow("https://example.com/cover.jpg", " ", "Author", "Introduction", "原创")]
    [DataRow("https://example.com/cover.jpg", "Title", " ", "Introduction", "原创")]
    [DataRow("https://example.com/cover.jpg", "Title", "Author", " ", "原创")]
    [DataRow("https://example.com/cover.jpg", "Title", "Author", "Introduction", " ")]
    public async Task CreateComicAsync_BlankRequiredField_ThrowsValidation(
        string cover,
        string title,
        string author,
        string introduction,
        string categoryName)
    {
        var api = new FakeComicPublishingApi();
        var service = new ComicPublishingService(api);
        var draft = new CreateComicDraft(
            cover,
            title,
            author,
            introduction,
            categoryName);

        var exception = await Assert.ThrowsExactlyAsync<AppException>(() =>
            service.CreateComicAsync(draft, CancellationToken.None));

        Assert.AreEqual(AppErrorKind.Validation, exception.Kind);
        Assert.AreEqual(0, api.TotalCallCount);
    }

    [TestMethod]
    public async Task CreateComicAsync_UnknownCategory_ThrowsValidation()
    {
        var api = new FakeComicPublishingApi();
        var service = new ComicPublishingService(api);
        var draft = ValidCreateDraft() with { CategoryName = "未知" };

        var exception = await Assert.ThrowsExactlyAsync<AppException>(() =>
            service.CreateComicAsync(draft, CancellationToken.None));

        Assert.AreEqual(AppErrorKind.Validation, exception.Kind);
        Assert.AreEqual(0, api.TotalCallCount);
    }

    [TestMethod]
    [DataRow(-1)]
    [DataRow(7)]
    public async Task UpdateSettingsAsync_LevelOutsideZeroToSix_ThrowsValidation(int level)
    {
        var api = new FakeComicPublishingApi();
        var service = new ComicPublishingService(api);
        var draft = ValidSettingsDraft() with { Level = level };

        var exception = await Assert.ThrowsExactlyAsync<AppException>(() =>
            service.UpdateSettingsAsync(42, draft, 3, CancellationToken.None));

        Assert.AreEqual(AppErrorKind.Validation, exception.Kind);
        Assert.AreEqual(0, api.TotalCallCount);
    }

    [TestMethod]
    public async Task UpdateSettingsAsync_InteriorLevelAboveUserMaximum_ThrowsValidation()
    {
        var api = new FakeComicPublishingApi();
        var service = new ComicPublishingService(api);
        var draft = ValidSettingsDraft() with { InteriorLevel = 4 };

        var exception = await Assert.ThrowsExactlyAsync<AppException>(() =>
            service.UpdateSettingsAsync(42, draft, 3, CancellationToken.None));

        Assert.AreEqual(AppErrorKind.Validation, exception.Kind);
        Assert.AreEqual(0, api.TotalCallCount);
    }

    [TestMethod]
    [DataRow(0L)]
    [DataRow(-1L)]
    public async Task GetEditDetailsAsync_InvalidBookId_ThrowsValidation(long bookId)
    {
        var api = new FakeComicPublishingApi();
        var service = new ComicPublishingService(api);

        var exception = await Assert.ThrowsExactlyAsync<AppException>(() =>
            service.GetEditDetailsAsync(bookId, CancellationToken.None));

        Assert.AreEqual(AppErrorKind.Validation, exception.Kind);
        Assert.AreEqual(0, api.TotalCallCount);
    }

    [TestMethod]
    public async Task CreateChapterAsync_WithoutImages_ThrowsValidation()
    {
        var api = new FakeComicPublishingApi();
        var service = new ComicPublishingService(api);
        var draft = new ComicChapterDraft(0, "Chapter", []);

        var exception = await Assert.ThrowsExactlyAsync<AppException>(() =>
            service.CreateChapterAsync(42, 1, draft, CancellationToken.None));

        Assert.AreEqual(AppErrorKind.Validation, exception.Kind);
        Assert.AreEqual(0, api.TotalCallCount);
    }

    [TestMethod]
    public async Task UpdateChapterAsync_WithoutImages_ThrowsValidation()
    {
        var api = new FakeComicPublishingApi();
        var service = new ComicPublishingService(api);
        var draft = new ComicChapterDraft(70, "Chapter", []);

        var exception = await Assert.ThrowsExactlyAsync<AppException>(() =>
            service.UpdateChapterAsync(70, draft, CancellationToken.None));

        Assert.AreEqual(AppErrorKind.Validation, exception.Kind);
        Assert.AreEqual(0, api.UpdateChapterCallCount);
        Assert.AreEqual(0, api.TotalCallCount);
    }

    [TestMethod]
    public async Task UploadImagesAsync_UsesNaturalOrderAndAtMostThreeWorkers()
    {
        var api = new FakeComicPublishingApi();
        var service = new ComicPublishingService(api);
        var currentConcurrency = 0;
        var maximumConcurrency = 0;
        api.UploadHandler = async (file, cancellationToken) =>
        {
            var current = Interlocked.Increment(ref currentConcurrency);
            UpdateMaximum(ref maximumConcurrency, current);
            try
            {
                var number = int.Parse(Path.GetFileNameWithoutExtension(file.FileName));
                await Task.Delay((11 - number) * 5, cancellationToken);
                return $"https://images.example/{file.FileName}";
            }
            finally
            {
                Interlocked.Decrement(ref currentConcurrency);
            }
        };

        var result = await service.UploadImagesAsync(
            [File("10.jpg"), File("2.jpg"), File("1.jpg"), File("3.jpg")],
            CancellationToken.None);

        CollectionAssert.AreEqual(
            new[] { "1.jpg", "2.jpg", "3.jpg", "10.jpg" },
            result.Successes.Select(item => item.FileName).ToArray());
        Assert.AreEqual(0, result.Failures.Count);
        Assert.IsLessThanOrEqualTo(3, maximumConcurrency);
        Assert.AreEqual(3, maximumConcurrency);
    }

    [TestMethod]
    public async Task UploadImagesAsync_OversizedNumericPart_DoesNotOverflow()
    {
        const string oversizedFileName = "999999999999999999999.png";
        var api = new FakeComicPublishingApi();
        var service = new ComicPublishingService(api);

        var result = await service.UploadImagesAsync(
            [File(oversizedFileName), File("2.png")],
            CancellationToken.None);

        CollectionAssert.AreEqual(
            new[] { "2.png", oversizedFileName },
            result.Successes.Select(item => item.FileName).ToArray());
        Assert.AreEqual(0, result.Failures.Count);
        Assert.AreEqual(2, api.UploadCallCount);
    }

    [TestMethod]
    public async Task UploadImagesAsync_PreservesFileNameWhitespace()
    {
        const string fileName = " 2.png ";
        var api = new FakeComicPublishingApi();
        var service = new ComicPublishingService(api);

        var result = await service.UploadImagesAsync(
            [File(fileName)],
            CancellationToken.None);

        CollectionAssert.AreEqual(
            new[] { fileName },
            api.UploadedFileNames.ToArray());
        CollectionAssert.AreEqual(
            new[] { fileName },
            result.Successes.Select(item => item.FileName).ToArray());
        Assert.AreEqual(0, result.Failures.Count);
    }

    [TestMethod]
    public async Task UploadImagesAsync_PartialFailure_PreservesSuccessfulItems()
    {
        var api = new FakeComicPublishingApi
        {
            UploadHandler = (file, _) => file.FileName is "2.jpg" or "10.jpg"
                ? Task.FromException<string>(new InvalidOperationException($"failed {file.FileName}"))
                : Task.FromResult($"https://images.example/{file.FileName}")
        };
        var service = new ComicPublishingService(api);

        var result = await service.UploadImagesAsync(
            [File("10.jpg"), File("2.jpg"), File("1.jpg"), File("3.jpg")],
            CancellationToken.None);

        CollectionAssert.AreEqual(
            new[] { "1.jpg", "3.jpg" },
            result.Successes.Select(item => item.FileName).ToArray());
        CollectionAssert.AreEqual(
            new[] { "2.jpg", "10.jpg" },
            result.Failures.Select(item => item.FileName).ToArray());
        CollectionAssert.AreEqual(
            new[] { "failed 2.jpg", "failed 10.jpg" },
            result.Failures.Select(item => item.Message).ToArray());
    }

    [TestMethod]
    public async Task PublishingOperations_ValidInputs_DelegateNormalizedValues()
    {
        var api = new FakeComicPublishingApi();
        var service = new ComicPublishingService(api);
        using var cancellation = new CancellationTokenSource();

        var newBookId = await service.CreateComicAsync(
            new(
                "  https://example.com/cover.jpg  ",
                "  Title  ",
                "  Author  ",
                "  Introduction  ",
                "  连载  "),
            cancellation.Token);
        await service.DeleteComicAsync(42, cancellation.Token);
        var details = await service.GetEditDetailsAsync(42, cancellation.Token);
        await service.UpdateInfoAsync(
            42,
            new(
                "  https://example.com/updated.jpg  ",
                "  Updated title  ",
                "  Updated author  ",
                "  Updated introduction  ",
                7),
            cancellation.Token);
        await service.UpdateSettingsAsync(
            42,
            new(
                6,
                3,
                true,
                12,
                13,
                "  Series  ",
                "  系列  ",
                ["  tag one  ", " ", "tag two"]),
            3,
            cancellation.Token);

        Assert.AreEqual(84L, newBookId);
        Assert.AreSame(api.EditDetails, details);
        Assert.AreEqual(
            new CreateComicDraft(
                "https://example.com/cover.jpg",
                "Title",
                "Author",
                "Introduction",
                "连载"),
            api.CreateDraft);
        Assert.AreEqual(42L, api.BookId);
        Assert.AreEqual(
            new ComicInfoDraft(
                "https://example.com/updated.jpg",
                "Updated title",
                "Updated author",
                "Updated introduction",
                7),
            api.InfoDraft);
        Assert.IsNotNull(api.SettingsDraft);
        Assert.AreEqual("Series", api.SettingsDraft.SeriesName);
        Assert.AreEqual("系列", api.SettingsDraft.SeriesNameCn);
        CollectionAssert.AreEqual(
            new[] { "tag one", "tag two" },
            api.SettingsDraft.Tags.ToArray());
        Assert.AreEqual(1, api.QuickCreateCallCount);
        Assert.AreEqual(1, api.DeleteBookCallCount);
        Assert.AreEqual(1, api.GetEditDetailsCallCount);
        Assert.AreEqual(1, api.UpdateInfoCallCount);
        Assert.AreEqual(1, api.UpdateSettingsCallCount);
        Assert.AreEqual(cancellation.Token, api.CancellationToken);
    }

    [TestMethod]
    public async Task ChapterOperations_ValidInputs_DelegateNormalizedValues()
    {
        var api = new FakeComicPublishingApi();
        var service = new ComicPublishingService(api);
        using var cancellation = new CancellationTokenSource();

        var chapter = await service.GetChapterAsync(42, 70, cancellation.Token);
        await service.UpdateChapterAsync(
            70,
            new(70, "  Updated chapter  ", ["image-1", "image-2"]),
            cancellation.Token);
        var created = await service.CreateChapterAsync(
            42,
            3,
            new(0, "  New chapter  ", ["new-image"]),
            cancellation.Token);
        await service.DeleteChapterAsync(42, 3, cancellation.Token);
        await service.ReorderChapterAsync(42, 3, 1, cancellation.Token);

        Assert.AreSame(api.Chapter, chapter);
        Assert.AreSame(api.CreateChapterResult, created);
        Assert.AreEqual("Updated chapter", api.UpdatedChapterDraft?.Title);
        Assert.AreEqual("New chapter", api.CreatedChapterDraft?.Title);
        Assert.AreEqual(42L, api.BookId);
        Assert.AreEqual(70L, api.ChapterId);
        Assert.AreEqual(3, api.SortNum);
        Assert.AreEqual(3, api.OldSortNum);
        Assert.AreEqual(1, api.NewSortNum);
        Assert.AreEqual(1, api.GetChapterCallCount);
        Assert.AreEqual(1, api.UpdateChapterCallCount);
        Assert.AreEqual(1, api.CreateChapterCallCount);
        Assert.AreEqual(1, api.DeleteChapterCallCount);
        Assert.AreEqual(1, api.ReorderChapterCallCount);
        Assert.AreEqual(cancellation.Token, api.CancellationToken);
    }

    [TestMethod]
    [DataRow("get-page")]
    [DataRow("get-size")]
    [DataRow("delete-book")]
    [DataRow("update-info-book")]
    [DataRow("update-info-cover")]
    [DataRow("update-info-title")]
    [DataRow("update-info-author")]
    [DataRow("update-info-introduction")]
    [DataRow("update-info-category")]
    [DataRow("settings-book")]
    [DataRow("settings-interior")]
    [DataRow("settings-subject")]
    [DataRow("settings-series")]
    [DataRow("get-chapter-book")]
    [DataRow("get-chapter-id")]
    [DataRow("update-chapter-id")]
    [DataRow("update-chapter-title")]
    [DataRow("create-chapter-book")]
    [DataRow("create-chapter-sort")]
    [DataRow("create-chapter-title")]
    [DataRow("delete-chapter-book")]
    [DataRow("delete-chapter-sort")]
    [DataRow("reorder-book")]
    [DataRow("reorder-old-sort")]
    [DataRow("reorder-new-sort")]
    public async Task InvalidArguments_ThrowValidationBeforeApi(string scenario)
    {
        var api = new FakeComicPublishingApi();
        var service = new ComicPublishingService(api);

        Task Action() => scenario switch
        {
            "get-page" => service.GetMyComicsAsync(0, 12, "", CancellationToken.None),
            "get-size" => service.GetMyComicsAsync(1, 0, "", CancellationToken.None),
            "delete-book" => service.DeleteComicAsync(0, CancellationToken.None),
            "update-info-book" => service.UpdateInfoAsync(0, ValidInfoDraft(), CancellationToken.None),
            "update-info-cover" => service.UpdateInfoAsync(
                42,
                ValidInfoDraft() with { Cover = "http://example.com/cover.jpg" },
                CancellationToken.None),
            "update-info-title" => service.UpdateInfoAsync(
                42,
                ValidInfoDraft() with { Title = " " },
                CancellationToken.None),
            "update-info-author" => service.UpdateInfoAsync(
                42,
                ValidInfoDraft() with { Author = " " },
                CancellationToken.None),
            "update-info-introduction" => service.UpdateInfoAsync(
                42,
                ValidInfoDraft() with { Introduction = " " },
                CancellationToken.None),
            "update-info-category" => service.UpdateInfoAsync(
                42,
                ValidInfoDraft() with { CategoryId = 0 },
                CancellationToken.None),
            "settings-book" => service.UpdateSettingsAsync(
                0,
                ValidSettingsDraft(),
                3,
                CancellationToken.None),
            "settings-interior" => service.UpdateSettingsAsync(
                42,
                ValidSettingsDraft() with { InteriorLevel = -1 },
                3,
                CancellationToken.None),
            "settings-subject" => service.UpdateSettingsAsync(
                42,
                ValidSettingsDraft() with { SubjectId = 0 },
                3,
                CancellationToken.None),
            "settings-series" => service.UpdateSettingsAsync(
                42,
                ValidSettingsDraft() with { SeriesId = -1 },
                3,
                CancellationToken.None),
            "get-chapter-book" => service.GetChapterAsync(0, 70, CancellationToken.None),
            "get-chapter-id" => service.GetChapterAsync(42, 0, CancellationToken.None),
            "update-chapter-id" => service.UpdateChapterAsync(0, ValidChapterDraft(), CancellationToken.None),
            "update-chapter-title" => service.UpdateChapterAsync(
                70,
                ValidChapterDraft() with { Title = " " },
                CancellationToken.None),
            "create-chapter-book" => service.CreateChapterAsync(
                0,
                1,
                ValidChapterDraft() with { Id = 0 },
                CancellationToken.None),
            "create-chapter-sort" => service.CreateChapterAsync(
                42,
                0,
                ValidChapterDraft() with { Id = 0 },
                CancellationToken.None),
            "create-chapter-title" => service.CreateChapterAsync(
                42,
                1,
                ValidChapterDraft() with { Id = 0, Title = " " },
                CancellationToken.None),
            "delete-chapter-book" => service.DeleteChapterAsync(0, 1, CancellationToken.None),
            "delete-chapter-sort" => service.DeleteChapterAsync(42, 0, CancellationToken.None),
            "reorder-book" => service.ReorderChapterAsync(0, 1, 2, CancellationToken.None),
            "reorder-old-sort" => service.ReorderChapterAsync(42, 0, 2, CancellationToken.None),
            "reorder-new-sort" => service.ReorderChapterAsync(42, 1, 0, CancellationToken.None),
            _ => throw new InvalidOperationException($"Unknown scenario: {scenario}")
        };

        var exception = await Assert.ThrowsExactlyAsync<AppException>(Action);

        Assert.AreEqual(AppErrorKind.Validation, exception.Kind);
        Assert.AreEqual(0, api.TotalCallCount);
    }

    [TestMethod]
    public async Task UploadImagesAsync_CallerCancellation_Propagates()
    {
        var api = new FakeComicPublishingApi
        {
            UploadHandler = async (_, cancellationToken) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return "unreachable";
            }
        };
        var service = new ComicPublishingService(api);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            service.UploadImagesAsync([File("1.jpg")], cancellation.Token));
    }

    private static MyComicSummary Summary(long id, string type, string title) =>
        new(
            id,
            type,
            title,
            $"https://example.com/{id}.jpg",
            "连载",
            DateTimeOffset.UnixEpoch);

    private static CreateComicDraft ValidCreateDraft() =>
        new(
            "https://example.com/cover.jpg",
            "Title",
            "Author",
            "Introduction",
            "原创");

    private static ComicInfoDraft ValidInfoDraft() =>
        new(
            "https://example.com/cover.jpg",
            "Title",
            "Author",
            "Introduction",
            7);

    private static ComicSettingsDraft ValidSettingsDraft() =>
        new(
            3,
            2,
            true,
            12,
            13,
            "Series",
            "系列",
            ["tag"]);

    private static ComicChapterDraft ValidChapterDraft() =>
        new(70, "Chapter", ["image"]);

    private static LocalImageFile File(string fileName) =>
        new(fileName, [1, 2, 3]);

    private static void UpdateMaximum(ref int maximum, int current)
    {
        while (true)
        {
            var snapshot = Volatile.Read(ref maximum);
            if (snapshot >= current ||
                Interlocked.CompareExchange(ref maximum, current, snapshot) == snapshot)
            {
                return;
            }
        }
    }

    private sealed class FakeComicPublishingApi : IComicPublishingApi
    {
        private int _uploadCallCount;

        public PageResult<MyComicSummary> MyBooksResult { get; init; } =
            new([], 1, 1);

        public ComicEditDetails EditDetails { get; } = new(
            42,
            "Comic",
            "https://example.com/cover.jpg",
            "Title",
            "Author",
            "Introduction",
            7,
            [new(7, "原创")],
            3,
            2,
            true,
            12,
            13,
            "Series",
            "系列",
            ["tag"],
            [new(70, 1, "Chapter")]);

        public ComicChapterDraft Chapter { get; } =
            new(70, "Chapter", ["image"]);

        public CreateChapterResult CreateChapterResult { get; } =
            new(71, [new(71, 3, "New chapter")]);

        public Func<LocalImageFile, CancellationToken, Task<string>> UploadHandler { get; set; } =
            (file, _) => Task.FromResult($"https://images.example/{file.FileName}");

        public int GetMyBooksCallCount { get; private set; }

        public int QuickCreateCallCount { get; private set; }

        public int DeleteBookCallCount { get; private set; }

        public int GetEditDetailsCallCount { get; private set; }

        public int UpdateInfoCallCount { get; private set; }

        public int UpdateSettingsCallCount { get; private set; }

        public int GetChapterCallCount { get; private set; }

        public int UpdateChapterCallCount { get; private set; }

        public int CreateChapterCallCount { get; private set; }

        public int DeleteChapterCallCount { get; private set; }

        public int ReorderChapterCallCount { get; private set; }

        public int UploadCallCount => Volatile.Read(ref _uploadCallCount);

        public ConcurrentQueue<string> UploadedFileNames { get; } = new();

        public int TotalCallCount =>
            GetMyBooksCallCount +
            QuickCreateCallCount +
            DeleteBookCallCount +
            GetEditDetailsCallCount +
            UpdateInfoCallCount +
            UpdateSettingsCallCount +
            GetChapterCallCount +
            UpdateChapterCallCount +
            CreateChapterCallCount +
            DeleteChapterCallCount +
            ReorderChapterCallCount +
            UploadCallCount;

        public int Page { get; private set; }

        public int Size { get; private set; }

        public string? Keywords { get; private set; }

        public long BookId { get; private set; }

        public long ChapterId { get; private set; }

        public int SortNum { get; private set; }

        public int OldSortNum { get; private set; }

        public int NewSortNum { get; private set; }

        public CreateComicDraft? CreateDraft { get; private set; }

        public ComicInfoDraft? InfoDraft { get; private set; }

        public ComicSettingsDraft? SettingsDraft { get; private set; }

        public ComicChapterDraft? UpdatedChapterDraft { get; private set; }

        public ComicChapterDraft? CreatedChapterDraft { get; private set; }

        public CancellationToken CancellationToken { get; private set; }

        public Task<PageResult<MyComicSummary>> GetMyBooksAsync(
            int page,
            int size,
            string keywords,
            CancellationToken cancellationToken)
        {
            GetMyBooksCallCount++;
            Page = page;
            Size = size;
            Keywords = keywords;
            CancellationToken = cancellationToken;
            return Task.FromResult(MyBooksResult);
        }

        public Task<long> QuickCreateComicAsync(
            CreateComicDraft draft,
            CancellationToken cancellationToken)
        {
            QuickCreateCallCount++;
            CreateDraft = draft;
            CancellationToken = cancellationToken;
            return Task.FromResult(84L);
        }

        public Task DeleteBookAsync(long bookId, CancellationToken cancellationToken)
        {
            DeleteBookCallCount++;
            BookId = bookId;
            CancellationToken = cancellationToken;
            return Task.CompletedTask;
        }

        public Task<ComicEditDetails> GetBookEditInfoAsync(
            long bookId,
            CancellationToken cancellationToken)
        {
            GetEditDetailsCallCount++;
            BookId = bookId;
            CancellationToken = cancellationToken;
            return Task.FromResult(EditDetails);
        }

        public Task UpdateComicInfoAsync(
            long bookId,
            ComicInfoDraft draft,
            CancellationToken cancellationToken)
        {
            UpdateInfoCallCount++;
            BookId = bookId;
            InfoDraft = draft;
            CancellationToken = cancellationToken;
            return Task.CompletedTask;
        }

        public Task UpdateComicSettingsAsync(
            long bookId,
            ComicSettingsDraft draft,
            CancellationToken cancellationToken)
        {
            UpdateSettingsCallCount++;
            BookId = bookId;
            SettingsDraft = draft;
            CancellationToken = cancellationToken;
            return Task.CompletedTask;
        }

        public Task<ComicChapterDraft> GetComicEditInfoAsync(
            long bookId,
            long chapterId,
            CancellationToken cancellationToken)
        {
            GetChapterCallCount++;
            BookId = bookId;
            ChapterId = chapterId;
            CancellationToken = cancellationToken;
            return Task.FromResult(Chapter);
        }

        public Task UpdateComicChapterAsync(
            long chapterId,
            ComicChapterDraft draft,
            CancellationToken cancellationToken)
        {
            UpdateChapterCallCount++;
            ChapterId = chapterId;
            UpdatedChapterDraft = draft;
            CancellationToken = cancellationToken;
            return Task.CompletedTask;
        }

        public Task<CreateChapterResult> CreateNewComicChapterAsync(
            long bookId,
            int sortNum,
            ComicChapterDraft draft,
            CancellationToken cancellationToken)
        {
            CreateChapterCallCount++;
            BookId = bookId;
            SortNum = sortNum;
            CreatedChapterDraft = draft;
            CancellationToken = cancellationToken;
            return Task.FromResult(CreateChapterResult);
        }

        public Task DeleteChapterAsync(
            long bookId,
            int sortNum,
            CancellationToken cancellationToken)
        {
            DeleteChapterCallCount++;
            BookId = bookId;
            SortNum = sortNum;
            CancellationToken = cancellationToken;
            return Task.CompletedTask;
        }

        public Task ReorderChapterAsync(
            long bookId,
            int oldSortNum,
            int newSortNum,
            CancellationToken cancellationToken)
        {
            ReorderChapterCallCount++;
            BookId = bookId;
            OldSortNum = oldSortNum;
            NewSortNum = newSortNum;
            CancellationToken = cancellationToken;
            return Task.CompletedTask;
        }

        public Task<string> UploadImageAsync(
            LocalImageFile file,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _uploadCallCount);
            UploadedFileNames.Enqueue(file.FileName);
            CancellationToken = cancellationToken;
            return UploadHandler(file, cancellationToken);
        }
    }
}
