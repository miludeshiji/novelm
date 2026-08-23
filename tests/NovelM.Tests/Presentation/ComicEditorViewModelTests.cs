using NovelM_App.Application.Abstractions;
using NovelM_App.Domain.Common;
using NovelM_App.Domain.Auth;
using NovelM_App.Domain.Errors;
using NovelM_App.Domain.Publishing;
using NovelM_App.Presentation.Common;
using NovelM_App.Presentation.Publishing;

namespace NovelM.Tests.Presentation;

[TestClass]
public sealed class ComicEditorViewModelTests
{
    [TestMethod]
    public async Task LoadAsync_LoadsAllInformationSettingsCategoriesAndChapters()
    {
        var details = Details();
        var service = new FakeComicPublishingService
        {
            GetEditDetailsHandler = (_, _) => Task.FromResult(details)
        };
        var viewModel = CreateViewModel(service);

        await viewModel.LoadAsync(42, Profile(interiorLevel: 4), CancellationToken.None);

        Assert.AreEqual(42L, viewModel.BookId);
        Assert.IsTrue(viewModel.IsLoaded);
        Assert.AreEqual(details.Cover, viewModel.Cover);
        Assert.AreEqual(details.Title, viewModel.Title);
        Assert.AreEqual(details.Author, viewModel.Author);
        Assert.AreEqual(details.Introduction, viewModel.Introduction);
        Assert.AreEqual(details.CategoryId, viewModel.CategoryId);
        CollectionAssert.AreEqual(details.Categories.ToArray(), viewModel.Categories.ToArray());
        Assert.AreEqual(0, viewModel.MinimumLevel);
        Assert.AreEqual(6, viewModel.MaximumLevel);
        Assert.AreEqual(4, viewModel.MaximumInteriorLevel);
        Assert.IsTrue(viewModel.IsInteriorLevelVisible);
        Assert.AreEqual(details.Level, viewModel.Level);
        Assert.AreEqual(details.InteriorLevel, viewModel.InteriorLevel);
        Assert.AreEqual(details.DownloadAllowed, viewModel.DownloadAllowed);
        Assert.AreEqual(details.SubjectId, viewModel.SubjectId);
        Assert.AreEqual(details.SeriesId, viewModel.SeriesId);
        Assert.AreEqual(details.SeriesName, viewModel.SeriesName);
        Assert.AreEqual(details.SeriesNameCn, viewModel.SeriesNameCn);
        CollectionAssert.AreEqual(details.Tags.ToArray(), viewModel.Tags.ToArray());
        CollectionAssert.AreEqual(details.Chapters.ToArray(), viewModel.Chapters.ToArray());
        Assert.IsFalse(viewModel.HasUnsavedChanges);
        Assert.IsFalse(viewModel.IsBusy);
    }

    [TestMethod]
    public async Task LoadAsync_ClearWhilePending_LateSuccessDoesNotRestoreEditor()
    {
        var completion = new TaskCompletionSource<ComicEditDetails>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new FakeComicPublishingService
        {
            GetEditDetailsHandler = (_, _) => completion.Task
        };
        var viewModel = CreateViewModel(service);

        var load = viewModel.LoadAsync(42, Profile(), CancellationToken.None);
        viewModel.Clear();
        completion.SetResult(Details());
        await load;

        Assert.IsNull(viewModel.BookId);
        Assert.IsFalse(viewModel.IsLoaded);
        Assert.AreEqual(string.Empty, viewModel.Title);
        Assert.AreEqual(0, viewModel.Chapters.Count);
        Assert.IsNull(viewModel.NoticeMessage);
    }

    [TestMethod]
    public async Task SaveInfoAsync_SendsCompleteDraftAndClearsOnlyInfoDirty()
    {
        var service = LoadedService();
        var viewModel = CreateViewModel(service);
        await viewModel.LoadAsync(42, Profile(), CancellationToken.None);
        viewModel.Cover = "https://images.example/new-cover.jpg";
        viewModel.Title = "New title";
        viewModel.Author = "New author";
        viewModel.Introduction = "New introduction";
        viewModel.CategoryId = 9;
        viewModel.Level = 5;

        await viewModel.SaveInfoAsync(CancellationToken.None);

        Assert.AreEqual(
            new ComicInfoDraft(
                "https://images.example/new-cover.jpg",
                "New title",
                "New author",
                "New introduction",
                9),
            service.InfoDraft);
        Assert.IsFalse(viewModel.InfoHasUnsavedChanges);
        Assert.IsTrue(viewModel.SettingsHasUnsavedChanges);
        Assert.IsTrue(viewModel.HasUnsavedChanges);
    }

    [TestMethod]
    public async Task SaveSettingsAsync_SendsCompleteDraftAndUserMaximum()
    {
        var service = LoadedService();
        var viewModel = CreateViewModel(service);
        await viewModel.LoadAsync(42, Profile(interiorLevel: 5), CancellationToken.None);
        viewModel.Level = 6;
        viewModel.InteriorLevel = 5;
        viewModel.DownloadAllowed = false;
        viewModel.SubjectId = 90;
        viewModel.SeriesId = 91;
        viewModel.SeriesName = "Series";
        viewModel.SeriesNameCn = "系列";
        viewModel.Tags.Clear();
        viewModel.Tags.Add("new-tag");

        await viewModel.SaveSettingsAsync(CancellationToken.None);

        Assert.IsNotNull(service.SettingsDraft);
        Assert.AreEqual(6, service.SettingsDraft.Level);
        Assert.AreEqual(5, service.SettingsDraft.InteriorLevel);
        Assert.IsFalse(service.SettingsDraft.DownloadAllowed);
        Assert.AreEqual(90L, service.SettingsDraft.SubjectId);
        Assert.AreEqual(91L, service.SettingsDraft.SeriesId);
        Assert.AreEqual("Series", service.SettingsDraft.SeriesName);
        Assert.AreEqual("系列", service.SettingsDraft.SeriesNameCn);
        CollectionAssert.AreEqual(new[] { "new-tag" }, service.SettingsDraft.Tags.ToArray());
        Assert.AreEqual(5, service.MaximumInteriorLevel);
        Assert.IsFalse(viewModel.SettingsHasUnsavedChanges);
    }

    [TestMethod]
    public async Task SaveInfoAsync_EditWhilePending_PreservesLatestDraftAndDirtyState()
    {
        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var service = LoadedService();
        service.UpdateInfoHandler = (_, _, _) => completion.Task;
        var viewModel = CreateViewModel(service);
        await viewModel.LoadAsync(42, Profile(), CancellationToken.None);
        viewModel.Title = "submitted title";
        viewModel.Level = 4;

        var save = viewModel.SaveInfoAsync(CancellationToken.None);
        viewModel.Title = "latest title";
        completion.SetResult();
        await save;

        Assert.AreEqual("submitted title", service.InfoDraft?.Title);
        Assert.AreEqual("latest title", viewModel.Title);
        Assert.IsTrue(viewModel.InfoHasUnsavedChanges);
        Assert.IsTrue(viewModel.SettingsHasUnsavedChanges);
        Assert.IsFalse(viewModel.ChapterHasUnsavedChanges);
    }

    [TestMethod]
    public async Task SaveSettingsAsync_EditAndTagsWhilePending_PreservesLatestDraftAndDirtyState()
    {
        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var service = LoadedService();
        service.UpdateSettingsHandler = (_, _, _, _) => completion.Task;
        var viewModel = CreateViewModel(service);
        await viewModel.LoadAsync(42, Profile(), CancellationToken.None);
        viewModel.SeriesName = "submitted series";
        viewModel.Tags.Clear();
        viewModel.Tags.Add("submitted-tag");
        viewModel.Introduction = "dirty info";

        var save = viewModel.SaveSettingsAsync(CancellationToken.None);
        viewModel.SeriesName = "latest series";
        viewModel.Tags.Add("latest-tag");
        completion.SetResult();
        await save;

        Assert.AreEqual("submitted series", service.SettingsDraft?.SeriesName);
        CollectionAssert.AreEqual(
            new[] { "submitted-tag" },
            service.SettingsDraft?.Tags.ToArray());
        Assert.AreEqual("latest series", viewModel.SeriesName);
        CollectionAssert.AreEqual(
            new[] { "submitted-tag", "latest-tag" },
            viewModel.Tags.ToArray());
        Assert.IsTrue(viewModel.SettingsHasUnsavedChanges);
        Assert.IsTrue(viewModel.InfoHasUnsavedChanges);
        Assert.IsFalse(viewModel.ChapterHasUnsavedChanges);
    }

    [TestMethod]
    public async Task SelectChapterAsync_LoadsExactTitleAndImages()
    {
        var service = LoadedService();
        service.GetChapterHandler = (_, _, _) => Task.FromResult(
            new ComicChapterDraft(70, "Strict title", ["https://i/1.jpg", "https://i/2.jpg"]));
        var viewModel = CreateViewModel(service);
        await viewModel.LoadAsync(42, Profile(), CancellationToken.None);

        var selected = await viewModel.SelectChapterAsync(
            viewModel.Chapters[0],
            discardChapterChanges: false,
            CancellationToken.None);

        Assert.IsTrue(selected);
        Assert.AreSame(viewModel.Chapters[0], viewModel.SelectedChapter);
        Assert.AreEqual("Strict title", viewModel.ChapterTitle);
        CollectionAssert.AreEqual(
            new[] { "https://i/1.jpg", "https://i/2.jpg" },
            viewModel.ChapterImages.ToArray());
        Assert.IsFalse(viewModel.ChapterHasUnsavedChanges);
    }

    [TestMethod]
    public async Task BeginNewAndSaveChapterAsync_CreatesAndAppliesServerChapterList()
    {
        var service = LoadedService();
        service.CreateChapterHandler = (_, _, _, _) => Task.FromResult(
            new CreateChapterResult(
                88,
                [new ComicChapterSummary(70, 1, "One"), new ComicChapterSummary(88, 2, "New") ]));
        var viewModel = CreateViewModel(service);
        await viewModel.LoadAsync(42, Profile(), CancellationToken.None);

        Assert.IsTrue(viewModel.BeginNewChapter(discardChapterChanges: false));
        viewModel.ChapterTitle = "New";
        viewModel.ChapterImages.Add("https://i/new.jpg");
        await viewModel.SaveChapterAsync(CancellationToken.None);

        Assert.AreEqual(2, service.CreatedSortNum);
        Assert.IsNotNull(service.CreatedChapterDraft);
        Assert.AreEqual(0L, service.CreatedChapterDraft.Id);
        Assert.AreEqual("New", service.CreatedChapterDraft.Title);
        CollectionAssert.AreEqual(
            new[] { "https://i/new.jpg" },
            service.CreatedChapterDraft.Images.ToArray());
        Assert.AreEqual(2, viewModel.Chapters.Count);
        Assert.AreEqual(88L, viewModel.SelectedChapter?.Id);
        Assert.IsFalse(viewModel.IsCreatingChapter);
        Assert.IsFalse(viewModel.ChapterHasUnsavedChanges);
    }

    [TestMethod]
    public async Task SaveChapterAsync_UpdatesSummaryTitle()
    {
        var service = LoadedService();
        service.GetChapterHandler = (_, _, _) => Task.FromResult(
            new ComicChapterDraft(70, "One", ["https://i/1.jpg"]));
        var viewModel = CreateViewModel(service);
        await viewModel.LoadAsync(42, Profile(), CancellationToken.None);
        await viewModel.SelectChapterAsync(viewModel.Chapters[0], false, CancellationToken.None);
        viewModel.ChapterTitle = "Renamed";

        await viewModel.SaveChapterAsync(CancellationToken.None);

        Assert.IsNotNull(service.UpdatedChapterDraft);
        Assert.AreEqual(70L, service.UpdatedChapterDraft.Id);
        Assert.AreEqual("Renamed", service.UpdatedChapterDraft.Title);
        CollectionAssert.AreEqual(
            new[] { "https://i/1.jpg" },
            service.UpdatedChapterDraft.Images.ToArray());
        Assert.AreEqual("Renamed", viewModel.Chapters[0].Title);
        Assert.AreEqual("Renamed", viewModel.SelectedChapter?.Title);
        Assert.IsFalse(viewModel.ChapterHasUnsavedChanges);
    }

    [TestMethod]
    public async Task SaveChapterAsync_EditWhilePending_PreservesLatestDraftAndDirtyState()
    {
        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var service = LoadedService();
        service.GetChapterHandler = (_, _, _) => Task.FromResult(
            new ComicChapterDraft(70, "One", ["https://i/1.jpg"]));
        service.UpdateChapterHandler = (_, _, _) => completion.Task;
        var viewModel = CreateViewModel(service);
        await viewModel.LoadAsync(42, Profile(), CancellationToken.None);
        await viewModel.SelectChapterAsync(viewModel.Chapters[0], false, CancellationToken.None);
        viewModel.ChapterTitle = "submitted chapter";
        viewModel.ChapterImages.Add("https://i/submitted.jpg");
        viewModel.Title = "dirty info";

        var save = viewModel.SaveChapterAsync(CancellationToken.None);
        viewModel.ChapterTitle = "latest chapter";
        viewModel.ChapterImages.Add("https://i/latest.jpg");
        completion.SetResult();
        await save;

        Assert.AreEqual("submitted chapter", service.UpdatedChapterDraft?.Title);
        Assert.AreEqual("submitted chapter", viewModel.SelectedChapter?.Title);
        Assert.AreEqual("latest chapter", viewModel.ChapterTitle);
        CollectionAssert.AreEqual(
            new[] { "https://i/1.jpg", "https://i/submitted.jpg", "https://i/latest.jpg" },
            viewModel.ChapterImages.ToArray());
        Assert.IsTrue(viewModel.ChapterHasUnsavedChanges);
        Assert.IsTrue(viewModel.InfoHasUnsavedChanges);
        Assert.IsFalse(viewModel.SettingsHasUnsavedChanges);
    }

    [TestMethod]
    public async Task SaveNewChapterAsync_EditWhilePending_CommitsIdentityAndKeepsLatestDraftDirty()
    {
        var completion = new TaskCompletionSource<CreateChapterResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var service = LoadedService();
        service.CreateChapterHandler = (_, _, _, _) => completion.Task;
        var viewModel = CreateViewModel(service);
        await viewModel.LoadAsync(42, Profile(), CancellationToken.None);
        Assert.IsTrue(viewModel.BeginNewChapter(false));
        viewModel.ChapterTitle = "submitted new chapter";
        viewModel.ChapterImages.Add("https://i/submitted.jpg");

        var save = viewModel.SaveChapterAsync(CancellationToken.None);
        viewModel.ChapterTitle = "latest new chapter";
        viewModel.ChapterImages.Add("https://i/latest.jpg");
        completion.SetResult(new CreateChapterResult(
            88,
            [
                new ComicChapterSummary(70, 1, "One"),
                new ComicChapterSummary(88, 2, "submitted new chapter")
            ]));
        await save;

        Assert.AreEqual(88L, viewModel.SelectedChapter?.Id);
        Assert.IsFalse(viewModel.IsCreatingChapter);
        Assert.AreEqual("latest new chapter", viewModel.ChapterTitle);
        CollectionAssert.AreEqual(
            new[] { "https://i/submitted.jpg", "https://i/latest.jpg" },
            viewModel.ChapterImages.ToArray());
        Assert.IsTrue(viewModel.ChapterHasUnsavedChanges);
    }

    [TestMethod]
    public async Task DeleteSelectedChapterAsync_RemovesAndSelectsAdjacentWithRenumberedSorts()
    {
        var service = LoadedService(ThreeChapterDetails());
        service.GetChapterHandler = (_, chapterId, _) => Task.FromResult(
            new ComicChapterDraft(chapterId, $"Chapter {chapterId}", ["https://i/1.jpg"]));
        var viewModel = CreateViewModel(service);
        await viewModel.LoadAsync(42, Profile(), CancellationToken.None);
        await viewModel.SelectChapterAsync(viewModel.Chapters[1], false, CancellationToken.None);

        await viewModel.DeleteSelectedChapterAsync(CancellationToken.None);

        Assert.AreEqual(2, service.DeletedSortNum);
        CollectionAssert.AreEqual(new[] { 1, 2 }, viewModel.Chapters.Select(x => x.SortNum).ToArray());
        Assert.AreEqual(72L, viewModel.SelectedChapter?.Id);
        Assert.AreEqual(2, service.GetChapterCalls);
    }

    [TestMethod]
    public async Task DeleteSelectedChapterAsync_LastChapterClearsDraftAndResetsNewSort()
    {
        var service = LoadedService();
        service.GetChapterHandler = (_, chapterId, _) => Task.FromResult(
            new ComicChapterDraft(chapterId, "One", ["https://i/1.jpg"]));
        var viewModel = CreateViewModel(service);
        await viewModel.LoadAsync(42, Profile(), CancellationToken.None);
        await viewModel.SelectChapterAsync(viewModel.Chapters[0], false, CancellationToken.None);

        await viewModel.DeleteSelectedChapterAsync(CancellationToken.None);

        Assert.AreEqual(0, viewModel.Chapters.Count);
        Assert.IsNull(viewModel.SelectedChapter);
        Assert.AreEqual(string.Empty, viewModel.ChapterTitle);
        Assert.AreEqual(0, viewModel.ChapterImages.Count);
        Assert.AreEqual(1, viewModel.NewChapterSortNum);
        Assert.IsFalse(viewModel.ChapterHasUnsavedChanges);
    }

    [TestMethod]
    public async Task DeleteSelectedChapterAsync_AdjacentLoadFailure_KeepsCommittedDeleteWithEmptyDraft()
    {
        var chapterLoadCalls = 0;
        var service = LoadedService(ThreeChapterDetails());
        service.GetChapterHandler = (_, chapterId, _) => ++chapterLoadCalls == 1
            ? Task.FromResult(
                new ComicChapterDraft(chapterId, "Deleted chapter", ["https://i/deleted.jpg"]))
            : Task.FromException<ComicChapterDraft>(
                new AppException(AppErrorKind.Transport, "unsafe"));
        var viewModel = CreateViewModel(service);
        await viewModel.LoadAsync(42, Profile(), CancellationToken.None);
        await viewModel.SelectChapterAsync(viewModel.Chapters[1], false, CancellationToken.None);
        viewModel.ChapterTitle = "dirty deleted chapter";
        viewModel.ChapterImages.Add("https://i/dirty.jpg");

        await viewModel.DeleteSelectedChapterAsync(CancellationToken.None);

        CollectionAssert.AreEqual(
            new long[] { 70, 72 },
            viewModel.Chapters.Select(x => x.Id).ToArray());
        CollectionAssert.AreEqual(
            new[] { 1, 2 },
            viewModel.Chapters.Select(x => x.SortNum).ToArray());
        Assert.IsNull(viewModel.SelectedChapter);
        Assert.AreEqual(string.Empty, viewModel.ChapterTitle);
        Assert.AreEqual(0, viewModel.ChapterImages.Count);
        Assert.IsFalse(viewModel.IsCreatingChapter);
        Assert.IsFalse(viewModel.ChapterHasUnsavedChanges);
        Assert.AreEqual(3, viewModel.NewChapterSortNum);
        Assert.AreEqual("网络连接失败，请检查网络后重试。", viewModel.ErrorMessage);
    }

    [TestMethod]
    public async Task DeleteSelectedChapterAsync_AdjacentLoadCancellation_PropagatesWithCommittedDelete()
    {
        using var source = new CancellationTokenSource();
        var chapterLoadCalls = 0;
        var service = LoadedService(ThreeChapterDetails());
        service.GetChapterHandler = (_, chapterId, _) => ++chapterLoadCalls == 1
            ? Task.FromResult(
                new ComicChapterDraft(chapterId, "Deleted chapter", ["https://i/deleted.jpg"]))
            : Task.FromException<ComicChapterDraft>(
                new OperationCanceledException(source.Token));
        var viewModel = CreateViewModel(service);
        await viewModel.LoadAsync(42, Profile(), CancellationToken.None);
        await viewModel.SelectChapterAsync(viewModel.Chapters[1], false, CancellationToken.None);

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            viewModel.DeleteSelectedChapterAsync(source.Token));

        CollectionAssert.AreEqual(
            new long[] { 70, 72 },
            viewModel.Chapters.Select(x => x.Id).ToArray());
        Assert.IsNull(viewModel.SelectedChapter);
        Assert.AreEqual(string.Empty, viewModel.ChapterTitle);
        Assert.AreEqual(0, viewModel.ChapterImages.Count);
        Assert.IsFalse(viewModel.ChapterHasUnsavedChanges);
        Assert.IsFalse(viewModel.IsBusy);
    }

    [TestMethod]
    public async Task MoveSelectedChapterAsync_ReordersLocallyAfterApiSuccess()
    {
        var service = LoadedService(ThreeChapterDetails());
        service.GetChapterHandler = (_, chapterId, _) => Task.FromResult(
            new ComicChapterDraft(chapterId, $"Chapter {chapterId}", ["https://i/1.jpg"]));
        var viewModel = CreateViewModel(service);
        await viewModel.LoadAsync(42, Profile(), CancellationToken.None);
        await viewModel.SelectChapterAsync(viewModel.Chapters[1], false, CancellationToken.None);

        await viewModel.MoveSelectedChapterAsync(-1, CancellationToken.None);

        Assert.AreEqual((2, 1), (service.OldSortNum, service.NewSortNum));
        CollectionAssert.AreEqual(new long[] { 71, 70, 72 }, viewModel.Chapters.Select(x => x.Id).ToArray());
        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, viewModel.Chapters.Select(x => x.SortNum).ToArray());
        Assert.AreEqual(71L, viewModel.SelectedChapter?.Id);
    }

    [TestMethod]
    public async Task UploadChapterImagesAsync_AppendsSuccessesAndKeepsPartialFailures()
    {
        var service = LoadedService();
        service.UploadHandler = (_, _) => Task.FromResult(
            new ImageUploadBatchResult(
                [new UploadedImage("1.jpg", "https://i/1.jpg"), new UploadedImage("3.jpg", "https://i/3.jpg")],
                [new FailedImage("2.jpg", "failed") ]));
        var viewModel = CreateViewModel(service);
        await viewModel.LoadAsync(42, Profile(), CancellationToken.None);
        Assert.IsTrue(viewModel.BeginNewChapter(false));

        await viewModel.UploadChapterImagesAsync(
            [File("3.jpg"), File("1.jpg"), File("2.jpg")],
            CancellationToken.None);

        CollectionAssert.AreEqual(
            new[] { "https://i/1.jpg", "https://i/3.jpg" },
            viewModel.ChapterImages.ToArray());
        CollectionAssert.AreEqual(new[] { "2.jpg" }, viewModel.FailedUploads.Select(x => x.FileName).ToArray());
        StringAssert.Contains(viewModel.NoticeMessage, "2.jpg");
        Assert.IsTrue(viewModel.ChapterHasUnsavedChanges);
        Assert.IsFalse(viewModel.IsUploading);
    }

    [TestMethod]
    public async Task UploadCoverAsync_SuccessUpdatesCoverAndMarksInfoDirty()
    {
        var service = LoadedService();
        service.UploadHandler = (_, _) => Task.FromResult(
            new ImageUploadBatchResult(
                [new UploadedImage("cover.jpg", "https://i/cover.jpg")],
                []));
        var viewModel = CreateViewModel(service);
        await viewModel.LoadAsync(42, Profile(), CancellationToken.None);

        await viewModel.UploadCoverAsync(File("cover.jpg"), CancellationToken.None);

        Assert.AreEqual("https://i/cover.jpg", viewModel.Cover);
        Assert.IsTrue(viewModel.InfoHasUnsavedChanges);
        Assert.AreEqual(0, viewModel.FailedUploads.Count);
    }

    [TestMethod]
    public async Task UploadCoverAsync_LoadAnotherBookWhilePending_LateSuccessDoesNotChangeNewBook()
    {
        var uploadCompletion = new TaskCompletionSource<ImageUploadBatchResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var detailsA = Details() with { Id = 42, Cover = "https://i/a-cover.jpg" };
        var detailsB = Details() with { Id = 43, Cover = "https://i/b-cover.jpg", Title = "Book B" };
        var service = LoadedService();
        service.GetEditDetailsHandler = (bookId, _) => Task.FromResult(
            bookId == 42 ? detailsA : detailsB);
        service.UploadHandler = (_, _) => uploadCompletion.Task;
        var viewModel = CreateViewModel(service);
        await viewModel.LoadAsync(42, Profile(), CancellationToken.None);

        var upload = viewModel.UploadCoverAsync(File("a-cover.jpg"), CancellationToken.None);
        await viewModel.LoadAsync(43, Profile(), CancellationToken.None);
        uploadCompletion.SetResult(new ImageUploadBatchResult(
            [new UploadedImage("a-cover.jpg", "https://i/uploaded-a.jpg")],
            []));
        await upload;

        Assert.AreEqual(43L, viewModel.BookId);
        Assert.AreEqual("Book B", viewModel.Title);
        Assert.AreEqual("https://i/b-cover.jpg", viewModel.Cover);
        Assert.IsFalse(viewModel.InfoHasUnsavedChanges);
    }

    [TestMethod]
    public async Task UploadCoverAsync_StaleUnauthorizedStillRaisesSessionExpired()
    {
        var uploadCompletion = new TaskCompletionSource<ImageUploadBatchResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var detailsB = Details() with { Id = 43, Cover = "https://i/b-cover.jpg", Title = "Book B" };
        var service = LoadedService();
        service.GetEditDetailsHandler = (bookId, _) => Task.FromResult(
            bookId == 42 ? Details() : detailsB);
        service.UploadHandler = (_, _) => uploadCompletion.Task;
        var viewModel = CreateViewModel(service);
        await viewModel.LoadAsync(42, Profile(), CancellationToken.None);
        var expired = 0;
        viewModel.SessionExpired += (_, _) => expired++;

        var upload = viewModel.UploadCoverAsync(File("a-cover.jpg"), CancellationToken.None);
        await viewModel.LoadAsync(43, Profile(), CancellationToken.None);
        uploadCompletion.SetException(
            new AppException(AppErrorKind.Unauthorized, "unsafe"));
        await upload;

        Assert.AreEqual(1, expired);
        Assert.AreEqual(43L, viewModel.BookId);
        Assert.AreEqual("https://i/b-cover.jpg", viewModel.Cover);
        Assert.IsNull(viewModel.ErrorMessage);
    }

    [TestMethod]
    public async Task UploadChapterImagesAsync_SwitchChapterWhilePending_LateSuccessDoesNotChangeNewChapter()
    {
        var uploadCompletion = new TaskCompletionSource<ImageUploadBatchResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var service = LoadedService(ThreeChapterDetails());
        service.GetChapterHandler = (_, chapterId, _) => Task.FromResult(
            new ComicChapterDraft(chapterId, $"Chapter {chapterId}", [$"https://i/{chapterId}.jpg"]));
        service.UploadHandler = (_, _) => uploadCompletion.Task;
        var viewModel = CreateViewModel(service);
        await viewModel.LoadAsync(42, Profile(), CancellationToken.None);
        await viewModel.SelectChapterAsync(viewModel.Chapters[0], false, CancellationToken.None);

        var upload = viewModel.UploadChapterImagesAsync([File("late.jpg")], CancellationToken.None);
        await viewModel.SelectChapterAsync(viewModel.Chapters[1], false, CancellationToken.None);
        uploadCompletion.SetResult(new ImageUploadBatchResult(
            [new UploadedImage("late.jpg", "https://i/late.jpg")],
            []));
        await upload;

        Assert.AreEqual(71L, viewModel.SelectedChapter?.Id);
        CollectionAssert.AreEqual(
            new[] { "https://i/71.jpg" },
            viewModel.ChapterImages.ToArray());
        Assert.IsFalse(viewModel.ChapterHasUnsavedChanges);
    }

    [TestMethod]
    public async Task UploadCoverAsync_ClearWhilePending_LateSuccessDoesNotRestoreState()
    {
        var uploadCompletion = new TaskCompletionSource<ImageUploadBatchResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var service = LoadedService();
        service.UploadHandler = (_, _) => uploadCompletion.Task;
        var viewModel = CreateViewModel(service);
        await viewModel.LoadAsync(42, Profile(), CancellationToken.None);

        var upload = viewModel.UploadCoverAsync(File("late.jpg"), CancellationToken.None);
        viewModel.Clear();
        uploadCompletion.SetResult(new ImageUploadBatchResult(
            [new UploadedImage("late.jpg", "https://i/late.jpg")],
            [new FailedImage("failed.jpg", "failed") ]));
        await upload;

        Assert.IsNull(viewModel.BookId);
        Assert.IsFalse(viewModel.IsLoaded);
        Assert.AreEqual(string.Empty, viewModel.Cover);
        Assert.AreEqual(0, viewModel.FailedUploads.Count);
        Assert.IsNull(viewModel.NoticeMessage);
        Assert.IsFalse(viewModel.InfoHasUnsavedChanges);
    }

    [TestMethod]
    public async Task UploadImagesAsync_RequiresLoadedBookAndChapterContext()
    {
        var service = new FakeComicPublishingService();
        var viewModel = CreateViewModel(service);

        await viewModel.UploadCoverAsync(File("cover.jpg"), CancellationToken.None);

        Assert.AreEqual(0, service.UploadCalls);

        service.GetEditDetailsHandler = (_, _) => Task.FromResult(Details());
        await viewModel.LoadAsync(42, Profile(), CancellationToken.None);
        await viewModel.UploadChapterImagesAsync([File("chapter.jpg")], CancellationToken.None);

        Assert.AreEqual(0, service.UploadCalls);
        Assert.IsFalse(viewModel.IsUploading);
    }

    [TestMethod]
    public async Task ChapterImageEditing_HandlesMoveRemoveAndClear()
    {
        var service = LoadedService();
        service.GetChapterHandler = (_, _, _) => Task.FromResult(
            new ComicChapterDraft(70, "One", ["a", "b", "c"]));
        var viewModel = CreateViewModel(service);
        await viewModel.LoadAsync(42, Profile(), CancellationToken.None);
        await viewModel.SelectChapterAsync(viewModel.Chapters[0], false, CancellationToken.None);

        viewModel.MoveChapterImage(0, 2);
        CollectionAssert.AreEqual(new[] { "b", "c", "a" }, viewModel.ChapterImages.ToArray());
        viewModel.RemoveChapterImageAt(1);
        CollectionAssert.AreEqual(new[] { "b", "a" }, viewModel.ChapterImages.ToArray());
        viewModel.RemoveChapterImageAt(99);
        viewModel.ClearChapterImages();

        Assert.AreEqual(0, viewModel.ChapterImages.Count);
        Assert.IsTrue(viewModel.ChapterHasUnsavedChanges);
    }

    [TestMethod]
    public async Task DirtySections_AreIndependentAndChapterSwitchRequiresDiscard()
    {
        var service = LoadedService(ThreeChapterDetails());
        service.GetChapterHandler = (_, chapterId, _) => Task.FromResult(
            new ComicChapterDraft(chapterId, $"Chapter {chapterId}", ["image"]));
        var viewModel = CreateViewModel(service);
        await viewModel.LoadAsync(42, Profile(), CancellationToken.None);
        await viewModel.SelectChapterAsync(viewModel.Chapters[0], false, CancellationToken.None);
        viewModel.Title = "dirty info";
        viewModel.Level = 3;
        viewModel.ChapterTitle = "dirty chapter";

        var rejected = await viewModel.SelectChapterAsync(
            viewModel.Chapters[1],
            discardChapterChanges: false,
            CancellationToken.None);

        Assert.IsFalse(rejected);
        Assert.AreEqual(1, service.GetChapterCalls);
        Assert.AreEqual(70L, viewModel.SelectedChapter?.Id);
        Assert.IsTrue(viewModel.InfoHasUnsavedChanges);
        Assert.IsTrue(viewModel.SettingsHasUnsavedChanges);
        Assert.IsTrue(viewModel.ChapterHasUnsavedChanges);
        Assert.IsTrue(viewModel.HasUnsavedChanges);

        Assert.IsTrue(await viewModel.SelectChapterAsync(viewModel.Chapters[1], true, CancellationToken.None));
        Assert.AreEqual(71L, viewModel.SelectedChapter?.Id);
        Assert.IsFalse(viewModel.ChapterHasUnsavedChanges);
        Assert.IsTrue(viewModel.InfoHasUnsavedChanges);
        Assert.IsTrue(viewModel.SettingsHasUnsavedChanges);
    }

    [TestMethod]
    public async Task InfoDirtyTransition_RaisesBindableSectionAndAggregateNotifications()
    {
        var service = LoadedService();
        var viewModel = CreateViewModel(service);
        await viewModel.LoadAsync(42, Profile(), CancellationToken.None);
        var notifications = new List<(string? Name, bool SectionDirty, bool HasDirty)>();
        viewModel.PropertyChanged += (_, args) => notifications.Add(
            (args.PropertyName, viewModel.InfoHasUnsavedChanges, viewModel.HasUnsavedChanges));

        viewModel.Title = "changed";

        Assert.IsTrue(notifications.Any(item =>
            item.Name == nameof(ComicEditorViewModel.InfoHasUnsavedChanges)
            && item.SectionDirty));
        Assert.IsTrue(notifications.Any(item =>
            item.Name == nameof(ComicEditorViewModel.HasUnsavedChanges)
            && item.HasDirty));
        Assert.IsFalse(notifications.Any(item => item.Name == "SetDirtyProperty"));

        notifications.Clear();
        await viewModel.SaveInfoAsync(CancellationToken.None);

        Assert.IsTrue(notifications.Any(item =>
            item.Name == nameof(ComicEditorViewModel.InfoHasUnsavedChanges)
            && !item.SectionDirty));
        Assert.IsTrue(notifications.Any(item =>
            item.Name == nameof(ComicEditorViewModel.HasUnsavedChanges)
            && !item.HasDirty));
        Assert.IsFalse(notifications.Any(item => item.Name == "SetDirtyProperty"));
    }

    [TestMethod]
    public async Task SettingsDirtyTransition_RaisesBindableSectionAndAggregateNotifications()
    {
        var viewModel = CreateViewModel(LoadedService());
        await viewModel.LoadAsync(42, Profile(), CancellationToken.None);
        var changedProperties = new List<string?>();
        viewModel.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);

        viewModel.Level++;

        CollectionAssert.Contains(
            changedProperties,
            nameof(ComicEditorViewModel.SettingsHasUnsavedChanges));
        CollectionAssert.Contains(
            changedProperties,
            nameof(ComicEditorViewModel.HasUnsavedChanges));
        CollectionAssert.DoesNotContain(changedProperties, "SetDirtyProperty");
    }

    [TestMethod]
    public async Task ChapterDirtyTransition_RaisesBindableSectionAndAggregateNotifications()
    {
        var viewModel = CreateViewModel(LoadedService());
        await viewModel.LoadAsync(42, Profile(), CancellationToken.None);
        var changedProperties = new List<string?>();
        viewModel.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);

        viewModel.ChapterImages.Add("https://i/new.jpg");

        CollectionAssert.Contains(
            changedProperties,
            nameof(ComicEditorViewModel.ChapterHasUnsavedChanges));
        CollectionAssert.Contains(
            changedProperties,
            nameof(ComicEditorViewModel.HasUnsavedChanges));
        CollectionAssert.DoesNotContain(changedProperties, "SetDirtyProperty");
    }

    [TestMethod]
    public async Task WriteFailure_PreservesDraftDirtyAndReportsError()
    {
        var service = LoadedService();
        service.UpdateInfoHandler = (_, _, _) => Task.FromException(
            new AppException(AppErrorKind.Transport, "unsafe"));
        var viewModel = CreateViewModel(service);
        await viewModel.LoadAsync(42, Profile(), CancellationToken.None);
        viewModel.Title = "unsaved";

        await viewModel.SaveInfoAsync(CancellationToken.None);

        Assert.AreEqual("unsaved", viewModel.Title);
        Assert.IsTrue(viewModel.InfoHasUnsavedChanges);
        Assert.IsTrue(viewModel.HasUnsavedChanges);
        Assert.AreEqual("网络连接失败，请检查网络后重试。", viewModel.ErrorMessage);
    }

    [TestMethod]
    public async Task Unauthorized_RaisesSessionExpiredAndPreservesOldEditorUntilCoordinatorClears()
    {
        var service = LoadedService();
        var viewModel = CreateViewModel(service);
        await viewModel.LoadAsync(42, Profile(), CancellationToken.None);
        service.UpdateInfoHandler = (_, _, _) => Task.FromException(
            new AppException(AppErrorKind.Unauthorized, "unsafe"));
        viewModel.Title = "unsaved";
        var raised = 0;
        viewModel.SessionExpired += (_, _) => raised++;

        await viewModel.SaveInfoAsync(CancellationToken.None);

        Assert.AreEqual(1, raised);
        Assert.AreEqual(42L, viewModel.BookId);
        Assert.AreEqual("unsaved", viewModel.Title);
        Assert.IsTrue(viewModel.InfoHasUnsavedChanges);
    }

    [TestMethod]
    public async Task Cancellation_IsRethrownAndBusyResets()
    {
        using var source = new CancellationTokenSource();
        var service = new FakeComicPublishingService
        {
            GetEditDetailsHandler = (_, token) => Task.FromException<ComicEditDetails>(
                new OperationCanceledException(token))
        };
        var viewModel = CreateViewModel(service);

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            viewModel.LoadAsync(42, Profile(), source.Token));

        Assert.IsFalse(viewModel.IsBusy);
        Assert.IsNull(viewModel.ErrorMessage);
    }

    [TestMethod]
    public async Task UploadChapterImagesAsync_WhilePending_DoesNotStartSecondBatch()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completion = new TaskCompletionSource<ImageUploadBatchResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var service = LoadedService();
        service.UploadHandler = (_, _) =>
        {
            entered.TrySetResult();
            return completion.Task;
        };
        var viewModel = CreateViewModel(service);
        await viewModel.LoadAsync(42, Profile(), CancellationToken.None);
        Assert.IsTrue(viewModel.BeginNewChapter(false));

        var first = viewModel.UploadChapterImagesAsync([File("1.jpg")], CancellationToken.None);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await viewModel.UploadChapterImagesAsync([File("2.jpg")], CancellationToken.None);

        Assert.AreEqual(1, service.UploadCalls);
        Assert.IsTrue(viewModel.IsUploading);
        completion.SetResult(new ImageUploadBatchResult([], []));
        await first;
        Assert.IsFalse(viewModel.IsUploading);
    }

    [TestMethod]
    public async Task SaveNewChapterAsync_WhileUploadPending_DoesNotCreateUntilUploadCompletes()
    {
        var uploadCompletion = new TaskCompletionSource<ImageUploadBatchResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var service = LoadedService();
        service.UploadHandler = (_, _) => uploadCompletion.Task;
        service.CreateChapterHandler = (_, _, _, _) => Task.FromResult(
            new CreateChapterResult(
                88,
                [new ComicChapterSummary(88, 2, "New") ]));
        var viewModel = CreateViewModel(service);
        await viewModel.LoadAsync(42, Profile(), CancellationToken.None);
        Assert.IsTrue(viewModel.BeginNewChapter(false));
        viewModel.ChapterTitle = "New";

        var upload = viewModel.UploadChapterImagesAsync(
            [File("chapter.jpg")],
            CancellationToken.None);
        await viewModel.SaveChapterAsync(CancellationToken.None);

        Assert.AreEqual(0, service.CreateChapterCalls);

        uploadCompletion.SetResult(new ImageUploadBatchResult(
            [new UploadedImage("chapter.jpg", "https://i/chapter.jpg")],
            []));
        await upload;
        CollectionAssert.AreEqual(
            new[] { "https://i/chapter.jpg" },
            viewModel.ChapterImages.ToArray());

        await viewModel.SaveChapterAsync(CancellationToken.None);

        Assert.AreEqual(1, service.CreateChapterCalls);
        Assert.AreEqual(88L, viewModel.SelectedChapter?.Id);
    }

    [TestMethod]
    public async Task UploadChapterImagesAsync_WhileCreatePending_DoesNotUpload()
    {
        var createCompletion = new TaskCompletionSource<CreateChapterResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var service = LoadedService();
        service.CreateChapterHandler = (_, _, _, _) => createCompletion.Task;
        service.UploadHandler = (_, _) => Task.FromResult(
            new ImageUploadBatchResult(
                [new UploadedImage("late.jpg", "https://i/late.jpg")],
                []));
        var viewModel = CreateViewModel(service);
        await viewModel.LoadAsync(42, Profile(), CancellationToken.None);
        Assert.IsTrue(viewModel.BeginNewChapter(false));
        viewModel.ChapterTitle = "New";
        viewModel.ChapterImages.Add("https://i/existing.jpg");

        var save = viewModel.SaveChapterAsync(CancellationToken.None);
        await viewModel.UploadChapterImagesAsync([File("late.jpg")], CancellationToken.None);

        Assert.AreEqual(0, service.UploadCalls);
        CollectionAssert.AreEqual(
            new[] { "https://i/existing.jpg" },
            viewModel.ChapterImages.ToArray());

        createCompletion.SetResult(new CreateChapterResult(
            88,
            [new ComicChapterSummary(88, 2, "New") ]));
        await save;

        Assert.AreEqual(88L, viewModel.SelectedChapter?.Id);
        Assert.IsFalse(viewModel.IsCreatingChapter);
    }

    internal static ComicEditorViewModel CreateViewModel(IComicPublishingService service) =>
        new(service, new ErrorMessageMapper());

    internal static UserProfile Profile(int interiorLevel = 3) =>
        new(7, "author", "avatar", "Creator", interiorLevel);

    internal static LocalImageFile File(string name) => new(name, [1, 2, 3]);

    internal static ComicEditDetails Details() => new(
        42,
        "Comic",
        "https://images.example/cover.jpg",
        "Title",
        "Author",
        "Introduction",
        5,
        [new ComicCategory(5, "Adventure"), new ComicCategory(9, "Fantasy")],
        2,
        1,
        true,
        11,
        12,
        "Series",
        "系列",
        ["tag-a", "tag-b"],
        [new ComicChapterSummary(70, 1, "One")]);

    internal static ComicEditDetails ThreeChapterDetails() => Details() with
    {
        Chapters =
        [
            new ComicChapterSummary(70, 1, "One"),
            new ComicChapterSummary(71, 2, "Two"),
            new ComicChapterSummary(72, 3, "Three")
        ]
    };

    internal static FakeComicPublishingService LoadedService(ComicEditDetails? details = null) =>
        new()
        {
            GetEditDetailsHandler = (_, _) => Task.FromResult(details ?? Details()),
            UpdateInfoHandler = (_, _, _) => Task.CompletedTask,
            UpdateSettingsHandler = (_, _, _, _) => Task.CompletedTask,
            UpdateChapterHandler = (_, _, _) => Task.CompletedTask,
            DeleteChapterHandler = (_, _, _) => Task.CompletedTask,
            ReorderChapterHandler = (_, _, _, _) => Task.CompletedTask
        };
}

internal sealed class FakeComicPublishingService : IComicPublishingService
{
    public Func<int, int, string, CancellationToken, Task<PageResult<MyComicSummary>>>? GetMyComicsHandler { get; set; }
    public Func<CreateComicDraft, CancellationToken, Task<long>>? CreateComicHandler { get; set; }
    public Func<long, CancellationToken, Task>? DeleteComicHandler { get; set; }
    public Func<long, CancellationToken, Task<ComicEditDetails>>? GetEditDetailsHandler { get; set; }
    public Func<long, ComicInfoDraft, CancellationToken, Task>? UpdateInfoHandler { get; set; }
    public Func<long, ComicSettingsDraft, int, CancellationToken, Task>? UpdateSettingsHandler { get; set; }
    public Func<long, long, CancellationToken, Task<ComicChapterDraft>>? GetChapterHandler { get; set; }
    public Func<long, ComicChapterDraft, CancellationToken, Task>? UpdateChapterHandler { get; set; }
    public Func<long, int, ComicChapterDraft, CancellationToken, Task<CreateChapterResult>>? CreateChapterHandler { get; set; }
    public Func<long, int, CancellationToken, Task>? DeleteChapterHandler { get; set; }
    public Func<long, int, int, CancellationToken, Task>? ReorderChapterHandler { get; set; }
    public Func<IReadOnlyList<LocalImageFile>, CancellationToken, Task<ImageUploadBatchResult>>? UploadHandler { get; set; }

    public int GetMyComicsCalls { get; private set; }
    public int CreateComicCalls { get; private set; }
    public int DeleteComicCalls { get; private set; }
    public int GetEditDetailsCalls { get; private set; }
    public int GetChapterCalls { get; private set; }
    public int UploadCalls { get; private set; }
    public int CreateChapterCalls { get; private set; }
    public int RequestedPage { get; private set; }
    public int RequestedSize { get; private set; }
    public string? RequestedKeywords { get; private set; }
    public CancellationToken LastCancellationToken { get; private set; }
    public long DeletedBookId { get; private set; }
    public int DeletedSortNum { get; private set; }
    public int OldSortNum { get; private set; }
    public int NewSortNum { get; private set; }
    public int CreatedSortNum { get; private set; }
    public int MaximumInteriorLevel { get; private set; }
    public ComicInfoDraft? InfoDraft { get; private set; }
    public ComicSettingsDraft? SettingsDraft { get; private set; }
    public ComicChapterDraft? UpdatedChapterDraft { get; private set; }
    public ComicChapterDraft? CreatedChapterDraft { get; private set; }

    public Task<PageResult<MyComicSummary>> GetMyComicsAsync(int page, int size, string keywords, CancellationToken cancellationToken)
    {
        GetMyComicsCalls++;
        RequestedPage = page;
        RequestedSize = size;
        RequestedKeywords = keywords;
        LastCancellationToken = cancellationToken;
        return GetMyComicsHandler?.Invoke(page, size, keywords, cancellationToken)
            ?? throw new AssertFailedException("GetMyComicsAsync was not expected.");
    }

    public Task<long> CreateComicAsync(CreateComicDraft draft, CancellationToken cancellationToken)
    {
        CreateComicCalls++;
        LastCancellationToken = cancellationToken;
        return CreateComicHandler?.Invoke(draft, cancellationToken)
            ?? throw new AssertFailedException("CreateComicAsync was not expected.");
    }

    public Task DeleteComicAsync(long bookId, CancellationToken cancellationToken)
    {
        DeleteComicCalls++;
        DeletedBookId = bookId;
        LastCancellationToken = cancellationToken;
        return DeleteComicHandler?.Invoke(bookId, cancellationToken)
            ?? throw new AssertFailedException("DeleteComicAsync was not expected.");
    }

    public Task<ComicEditDetails> GetEditDetailsAsync(long bookId, CancellationToken cancellationToken)
    {
        GetEditDetailsCalls++;
        LastCancellationToken = cancellationToken;
        return GetEditDetailsHandler?.Invoke(bookId, cancellationToken)
            ?? throw new AssertFailedException("GetEditDetailsAsync was not expected.");
    }

    public Task UpdateInfoAsync(long bookId, ComicInfoDraft draft, CancellationToken cancellationToken)
    {
        InfoDraft = draft;
        LastCancellationToken = cancellationToken;
        return UpdateInfoHandler?.Invoke(bookId, draft, cancellationToken)
            ?? throw new AssertFailedException("UpdateInfoAsync was not expected.");
    }

    public Task UpdateSettingsAsync(long bookId, ComicSettingsDraft draft, int maximumInteriorLevel, CancellationToken cancellationToken)
    {
        SettingsDraft = draft;
        MaximumInteriorLevel = maximumInteriorLevel;
        LastCancellationToken = cancellationToken;
        return UpdateSettingsHandler?.Invoke(bookId, draft, maximumInteriorLevel, cancellationToken)
            ?? throw new AssertFailedException("UpdateSettingsAsync was not expected.");
    }

    public Task<ComicChapterDraft> GetChapterAsync(long bookId, long chapterId, CancellationToken cancellationToken)
    {
        GetChapterCalls++;
        LastCancellationToken = cancellationToken;
        return GetChapterHandler?.Invoke(bookId, chapterId, cancellationToken)
            ?? throw new AssertFailedException("GetChapterAsync was not expected.");
    }

    public Task UpdateChapterAsync(long chapterId, ComicChapterDraft draft, CancellationToken cancellationToken)
    {
        UpdatedChapterDraft = draft;
        LastCancellationToken = cancellationToken;
        return UpdateChapterHandler?.Invoke(chapterId, draft, cancellationToken)
            ?? throw new AssertFailedException("UpdateChapterAsync was not expected.");
    }

    public Task<CreateChapterResult> CreateChapterAsync(long bookId, int sortNum, ComicChapterDraft draft, CancellationToken cancellationToken)
    {
        CreateChapterCalls++;
        CreatedSortNum = sortNum;
        CreatedChapterDraft = draft;
        LastCancellationToken = cancellationToken;
        return CreateChapterHandler?.Invoke(bookId, sortNum, draft, cancellationToken)
            ?? throw new AssertFailedException("CreateChapterAsync was not expected.");
    }

    public Task DeleteChapterAsync(long bookId, int sortNum, CancellationToken cancellationToken)
    {
        DeletedSortNum = sortNum;
        LastCancellationToken = cancellationToken;
        return DeleteChapterHandler?.Invoke(bookId, sortNum, cancellationToken)
            ?? throw new AssertFailedException("DeleteChapterAsync was not expected.");
    }

    public Task ReorderChapterAsync(long bookId, int oldSortNum, int newSortNum, CancellationToken cancellationToken)
    {
        OldSortNum = oldSortNum;
        NewSortNum = newSortNum;
        LastCancellationToken = cancellationToken;
        return ReorderChapterHandler?.Invoke(bookId, oldSortNum, newSortNum, cancellationToken)
            ?? throw new AssertFailedException("ReorderChapterAsync was not expected.");
    }

    public Task<ImageUploadBatchResult> UploadImagesAsync(IReadOnlyList<LocalImageFile> files, CancellationToken cancellationToken)
    {
        UploadCalls++;
        LastCancellationToken = cancellationToken;
        return UploadHandler?.Invoke(files, cancellationToken)
            ?? throw new AssertFailedException("UploadImagesAsync was not expected.");
    }
}
