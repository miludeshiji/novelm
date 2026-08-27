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
    public async Task StageChapterImages_SortsAndDoesNotUpload()
    {
        var service = LoadedService();
        var viewModel = CreateViewModel(service);
        await viewModel.LoadAsync(42, Profile(), CancellationToken.None);
        Assert.IsTrue(viewModel.BeginNewChapter(false));

        viewModel.StageChapterImages(
        [
            Source("10.jpg", @"C:\a\10.jpg"),
            Source("2.jpg", @"C:\a\2.jpg"),
            Source("2-copy.jpg", @"C:\a\2.jpg")
        ]);

        CollectionAssert.AreEqual(
            new[] { "2.jpg", "10.jpg" },
            viewModel.PendingChapterImages.Select(item => item.FileName).ToArray());
        Assert.AreEqual(0, service.UploadCalls);
        Assert.IsTrue(viewModel.ChapterHasUnsavedChanges);
        Assert.IsTrue(viewModel.HasPendingChapterImages);
    }

    [TestMethod]
    public async Task UploadPendingChapterImages_PartialFailureKeepsWholeBatchOutOfChapter()
    {
        var service = LoadedService();
        service.UploadHandler = (sources, _) => Task.FromResult(
            new ImageUploadBatchResult(
                sources
                    .Where(source => source.FileName != "2.jpg")
                    .Select(source => new UploadedImage(
                        source.FileName,
                        $"https://i/{source.FileName}",
                        source.Id))
                    .ToArray(),
                sources
                    .Where(source => source.FileName == "2.jpg")
                    .Select(source => new FailedImage(
                        source.FileName,
                        "failed",
                        source.Id))
                    .ToArray()));
        var viewModel = CreateViewModel(service);
        await viewModel.LoadAsync(42, Profile(), CancellationToken.None);
        Assert.IsTrue(viewModel.BeginNewChapter(false));
        viewModel.StageChapterImages(
            [Source("1.jpg"), Source("2.jpg"), Source("3.jpg")]);

        await viewModel.UploadPendingChapterImagesAsync(CancellationToken.None);

        Assert.AreEqual(0, viewModel.ChapterImages.Count);
        CollectionAssert.AreEqual(
            new[]
            {
                ComicImageUploadState.Uploaded,
                ComicImageUploadState.Failed,
                ComicImageUploadState.Uploaded
            },
            viewModel.PendingChapterImages.Select(item => item.State).ToArray());
        Assert.IsFalse(viewModel.CanSaveChapter);
    }

    [TestMethod]
    public async Task ReplaceFailedChapterImage_UploadsOnlyReplacementAndRestoresPosition()
    {
        var service = LoadedService();
        service.UploadHandler = (sources, _) => Task.FromResult(
            new ImageUploadBatchResult(
                sources.Select(source => new UploadedImage(
                    source.FileName,
                    $"https://i/{source.FileName}",
                    source.Id)).ToArray(),
                []));
        var viewModel = CreateViewModel(service);
        await viewModel.LoadAsync(42, Profile(), CancellationToken.None);
        Assert.IsTrue(viewModel.BeginNewChapter(false));
        viewModel.StageChapterImages(
            [Source("1.jpg"), Source("2.jpg"), Source("3.jpg")]);
        viewModel.PendingChapterImages[0].Complete("https://i/1.jpg");
        viewModel.PendingChapterImages[1].Fail("failed");
        viewModel.PendingChapterImages[2].Complete("https://i/3.jpg");
        var failedId = viewModel.PendingChapterImages[1].Id;

        await viewModel.ReplaceFailedChapterImageAsync(
            failedId,
            "replacement.png",
            @"C:\images\replacement.png",
            CancellationToken.None);

        CollectionAssert.AreEqual(
            new[]
            {
                "https://i/1.jpg",
                "https://i/replacement.png",
                "https://i/3.jpg"
            },
            viewModel.ChapterImages.ToArray());
        Assert.AreEqual(0, viewModel.PendingChapterImages.Count);
        Assert.AreEqual(1, service.LastUploadSources.Count);
        Assert.AreEqual(failedId, service.LastUploadSources[0].Id);
        Assert.AreEqual("replacement.png", service.LastUploadSources[0].FileName);
    }

    [TestMethod]
    public async Task ReplaceFailedChapterImage_AppendAndRemoveKeepOriginalPosition()
    {
        var service = LoadedService();
        service.UploadHandler = (sources, _) => Task.FromResult(
            new ImageUploadBatchResult(
                sources.Select(source => new UploadedImage(
                    source.FileName,
                    $"https://i/{source.FileName}",
                    source.Id)).ToArray(),
                []));
        var viewModel = CreateViewModel(service);
        await viewModel.LoadAsync(42, Profile(), CancellationToken.None);
        Assert.IsTrue(viewModel.BeginNewChapter(false));
        viewModel.StageChapterImages(
            [Source("1.jpg"), Source("2.jpg"), Source("3.jpg")]);
        viewModel.PendingChapterImages[0].Complete("https://i/1.jpg");
        var second = viewModel.PendingChapterImages[1];
        var third = viewModel.PendingChapterImages[2];
        second.Fail("failed");
        third.Fail("failed");

        await viewModel.ReplaceFailedChapterImageAsync(
            second.Id,
            "99.jpg",
            @"C:\images\99.jpg",
            CancellationToken.None);
        var appended20 = Source("20.jpg");
        var appended4 = Source("4.jpg");
        viewModel.StageChapterImages([appended20, appended4]);

        CollectionAssert.AreEqual(
            new[] { "1.jpg", "99.jpg", "3.jpg", "4.jpg", "20.jpg" },
            viewModel.PendingChapterImages.Select(item => item.FileName).ToArray());

        viewModel.RemovePendingChapterImage(appended4.Id);
        viewModel.RemovePendingChapterImage(appended20.Id);
        await viewModel.ReplaceFailedChapterImageAsync(
            third.Id,
            "3.jpg",
            @"C:\images\3-retry.jpg",
            CancellationToken.None);

        CollectionAssert.AreEqual(
            new[]
            {
                "https://i/1.jpg",
                "https://i/99.jpg",
                "https://i/3.jpg"
            },
            viewModel.ChapterImages.ToArray());
        Assert.AreEqual(0, viewModel.PendingChapterImages.Count);
    }

    [TestMethod]
    public async Task SaveChapterAsync_PendingLocalImageDoesNotCreateChapter()
    {
        var service = LoadedService();
        service.CreateChapterHandler = (_, _, _, _) => Task.FromResult(
            new CreateChapterResult(
                88,
                [new ComicChapterSummary(88, 2, "New") ]));
        var viewModel = CreateViewModel(service);
        await viewModel.LoadAsync(42, Profile(), CancellationToken.None);
        Assert.IsTrue(viewModel.BeginNewChapter(false));
        viewModel.ChapterTitle = "New";
        viewModel.StageChapterImages([Source("1.jpg")]);

        await viewModel.SaveChapterAsync(CancellationToken.None);

        Assert.AreEqual(0, service.CreateChapterCalls);
        Assert.IsFalse(viewModel.CanSaveChapter);
    }

    [TestMethod]
    public async Task UploadPendingChapterImages_MissingResultMarksImageFailed()
    {
        var service = LoadedService();
        service.UploadHandler = (_, _) => Task.FromResult(
            new ImageUploadBatchResult([], []));
        var viewModel = CreateViewModel(service);
        await viewModel.LoadAsync(42, Profile(), CancellationToken.None);
        Assert.IsTrue(viewModel.BeginNewChapter(false));
        viewModel.StageChapterImages([Source("1.jpg")]);

        await viewModel.UploadPendingChapterImagesAsync(CancellationToken.None);

        var image = viewModel.PendingChapterImages.Single();
        Assert.AreEqual(ComicImageUploadState.Failed, image.State);
        Assert.AreEqual("未返回上传结果", image.ErrorMessage);
        Assert.IsTrue(image.CanReplace);
    }

    [TestMethod]
    public async Task RemovePendingChapterImage_ReordersRemainingItems()
    {
        var viewModel = CreateViewModel(LoadedService());
        await viewModel.LoadAsync(42, Profile(), CancellationToken.None);
        Assert.IsTrue(viewModel.BeginNewChapter(false));
        viewModel.StageChapterImages(
            [Source("1.jpg"), Source("2.jpg"), Source("3.jpg")]);
        var first = viewModel.PendingChapterImages[0];
        var second = viewModel.PendingChapterImages[1];
        first.Complete("https://i/1.jpg");

        viewModel.RemovePendingChapterImage(first.Id);
        viewModel.RemovePendingChapterImage(second.Id);

        Assert.AreEqual(2, viewModel.PendingChapterImages.Count);
        CollectionAssert.AreEqual(
            new[] { 0, 1 },
            viewModel.PendingChapterImages.Select(item => item.Position).ToArray());
        CollectionAssert.AreEqual(
            new[] { "1.jpg", "3.jpg" },
            viewModel.PendingChapterImages.Select(item => item.FileName).ToArray());
    }

    [TestMethod]
    public async Task RemovePendingChapterImage_WhenRemainingUploadedCommitsInPosition()
    {
        var viewModel = CreateViewModel(LoadedService());
        await viewModel.LoadAsync(42, Profile(), CancellationToken.None);
        Assert.IsTrue(viewModel.BeginNewChapter(false));
        viewModel.StageChapterImages(
            [Source("1.jpg"), Source("2.jpg"), Source("3.jpg")]);
        viewModel.PendingChapterImages[0].Complete("https://i/1.jpg");
        var failed = viewModel.PendingChapterImages[1];
        failed.Fail("failed");
        viewModel.PendingChapterImages[2].Complete("https://i/3.jpg");

        viewModel.RemovePendingChapterImage(failed.Id);

        CollectionAssert.AreEqual(
            new[] { "https://i/1.jpg", "https://i/3.jpg" },
            viewModel.ChapterImages.ToArray());
        Assert.AreEqual(0, viewModel.PendingChapterImages.Count);
    }

    [TestMethod]
    public async Task ClearPendingChapterImages_UnremovableItemKeepsQueue()
    {
        var viewModel = CreateViewModel(LoadedService());
        await viewModel.LoadAsync(42, Profile(), CancellationToken.None);
        Assert.IsTrue(viewModel.BeginNewChapter(false));
        viewModel.StageChapterImages([Source("1.jpg"), Source("2.jpg")]);
        viewModel.PendingChapterImages[0].Complete("https://i/1.jpg");

        viewModel.ClearPendingChapterImages();

        Assert.AreEqual(2, viewModel.PendingChapterImages.Count);
    }

    [TestMethod]
    public async Task ClearPendingChapterImages_FromCleanDraftRestoresDirtyState()
    {
        var viewModel = CreateViewModel(LoadedService());
        await viewModel.LoadAsync(42, Profile(), CancellationToken.None);
        Assert.IsTrue(viewModel.BeginNewChapter(false));
        viewModel.StageChapterImages([Source("1.jpg")]);
        var changedProperties = new List<string?>();
        viewModel.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);

        viewModel.ClearPendingChapterImages();

        Assert.IsFalse(viewModel.ChapterHasUnsavedChanges);
        Assert.IsFalse(viewModel.HasUnsavedChanges);
        CollectionAssert.Contains(
            changedProperties,
            nameof(ComicEditorViewModel.ChapterHasUnsavedChanges));
        CollectionAssert.Contains(
            changedProperties,
            nameof(ComicEditorViewModel.HasUnsavedChanges));
    }

    [TestMethod]
    public async Task RemovePendingChapterImage_LastItemFromCleanDraftRestoresDirtyState()
    {
        var viewModel = CreateViewModel(LoadedService());
        await viewModel.LoadAsync(42, Profile(), CancellationToken.None);
        Assert.IsTrue(viewModel.BeginNewChapter(false));
        viewModel.StageChapterImages([Source("1.jpg")]);

        viewModel.RemovePendingChapterImage(
            viewModel.PendingChapterImages.Single().Id);

        Assert.IsFalse(viewModel.ChapterHasUnsavedChanges);
        Assert.IsFalse(viewModel.HasUnsavedChanges);
    }

    [TestMethod]
    public async Task ClearPendingChapterImages_PreservesExistingDraftDirtyState()
    {
        var viewModel = CreateViewModel(LoadedService());
        await viewModel.LoadAsync(42, Profile(), CancellationToken.None);
        Assert.IsTrue(viewModel.BeginNewChapter(false));
        viewModel.ChapterTitle = "Dirty title";
        viewModel.StageChapterImages([Source("1.jpg")]);

        viewModel.ClearPendingChapterImages();

        Assert.IsTrue(viewModel.ChapterHasUnsavedChanges);
        Assert.IsTrue(viewModel.HasUnsavedChanges);
    }

    [TestMethod]
    public async Task UploadPendingChapterImages_SuccessKeepsChapterDirtyAfterQueueClears()
    {
        var service = LoadedService();
        service.UploadHandler = (sources, _) => Task.FromResult(
            new ImageUploadBatchResult(
                sources.Select(source => new UploadedImage(
                    source.FileName,
                    $"https://i/{source.FileName}",
                    source.Id)).ToArray(),
                []));
        var viewModel = CreateViewModel(service);
        await viewModel.LoadAsync(42, Profile(), CancellationToken.None);
        Assert.IsTrue(viewModel.BeginNewChapter(false));
        viewModel.StageChapterImages([Source("1.jpg")]);

        await viewModel.UploadPendingChapterImagesAsync(CancellationToken.None);

        Assert.AreEqual(0, viewModel.PendingChapterImages.Count);
        CollectionAssert.AreEqual(
            new[] { "https://i/1.jpg" },
            viewModel.ChapterImages.ToArray());
        Assert.IsTrue(viewModel.ChapterHasUnsavedChanges);
        Assert.IsTrue(viewModel.HasUnsavedChanges);
    }

    [TestMethod]
    public async Task StageBatchChapters_SortsFoldersAndImagesWithoutChangingChapterDraft()
    {
        var service = LoadedService();
        service.GetChapterHandler = (_, chapterId, _) => Task.FromResult(
            new ComicChapterDraft(chapterId, "One", ["https://i/1.jpg"]));
        var viewModel = CreateViewModel(service);
        await viewModel.LoadAsync(42, Profile(), CancellationToken.None);
        await viewModel.SelectChapterAsync(viewModel.Chapters[0], false, CancellationToken.None);

        viewModel.StageBatchChapters(
        [
            Folder("第10章", Source("10.jpg"), Source("2.jpg")),
            Folder("第2章", Source("1.jpg"))
        ]);

        CollectionAssert.AreEqual(
            new[] { "第2章", "第10章" },
            viewModel.PendingBatchChapters.Select(item => item.Title).ToArray());
        CollectionAssert.AreEqual(
            new[] { "2.jpg", "10.jpg" },
            viewModel.PendingBatchChapters[1].Images.Select(item => item.FileName).ToArray());
        Assert.AreEqual(70L, viewModel.SelectedChapter?.Id);
        Assert.IsTrue(viewModel.HasPendingBatchChapters);
        Assert.IsTrue(viewModel.HasUnsavedChanges);
        Assert.AreEqual(0, service.UploadCalls);
    }

    [TestMethod]
    public async Task UploadBatchChapters_FailedEarlierImagesBlockCreationButLaterImagesUpload()
    {
        var service = LoadedService();
        service.UploadHandler = (sources, _) => Task.FromResult(
            new ImageUploadBatchResult(
                sources.Where(source => source.FileName != "bad.jpg")
                    .Select(source => new UploadedImage(
                        source.FileName,
                        $"https://i/{source.FileName}",
                        source.Id))
                    .ToArray(),
                sources.Where(source => source.FileName == "bad.jpg")
                    .Select(source => new FailedImage(
                        source.FileName,
                        "failed",
                        source.Id))
                    .ToArray()));
        var viewModel = CreateViewModel(service);
        await viewModel.LoadAsync(42, Profile(), CancellationToken.None);
        viewModel.StageBatchChapters(
        [
            Folder("第1章", Source("bad.jpg")),
            Folder("第2章", Source("2.jpg"))
        ]);

        await viewModel.UploadBatchChaptersAsync(CancellationToken.None);

        Assert.AreEqual(2, service.UploadCalls);
        Assert.AreEqual(0, service.CreateChapterCalls);
        Assert.AreEqual(
            ComicChapterUploadState.Failed,
            viewModel.PendingBatchChapters[0].State);
        Assert.AreEqual(
            ComicChapterUploadState.WaitingForPreviousChapter,
            viewModel.PendingBatchChapters[1].State);
    }

    [TestMethod]
    public async Task UploadBatchChapters_InvalidFirstChapterStillUploadsLaterImages()
    {
        var service = LoadedService();
        service.UploadHandler = SuccessfulUpload;
        var viewModel = CreateViewModel(service);
        await viewModel.LoadAsync(42, Profile(), CancellationToken.None);
        viewModel.StageBatchChapters(
        [
            new LocalComicChapterSelection(
                @"C:\chapters\第1章",
                "第1章",
                [],
                "没有支持的图片。"),
            Folder("第2章", Source("2.jpg"))
        ]);

        Assert.IsTrue(viewModel.CanUploadBatchChapters);

        await viewModel.UploadBatchChaptersAsync(CancellationToken.None);

        Assert.AreEqual(1, service.UploadCalls);
        Assert.AreEqual(0, service.CreateChapterCalls);
        Assert.AreEqual(
            ComicChapterUploadState.Failed,
            viewModel.PendingBatchChapters[0].State);
        Assert.AreEqual(
            "没有支持的图片。",
            viewModel.PendingBatchChapters[0].ErrorMessage);
        Assert.AreEqual(
            ComicImageUploadState.Uploaded,
            viewModel.PendingBatchChapters[1].Images.Single().State);
        Assert.AreEqual(
            ComicChapterUploadState.WaitingForPreviousChapter,
            viewModel.PendingBatchChapters[1].State);
        Assert.IsFalse(viewModel.CanUploadBatchChapters);
        Assert.IsNull(viewModel.NoticeMessage);
    }

    [TestMethod]
    public async Task UploadBatchChapters_UnauthorizedRestoresPendingImageAndIdleChapter()
    {
        var service = LoadedService();
        service.UploadHandler = (_, _) => Task.FromException<ImageUploadBatchResult>(
            new AppException(AppErrorKind.Unauthorized, "unsafe"));
        var viewModel = CreateViewModel(service);
        await viewModel.LoadAsync(42, Profile(), CancellationToken.None);
        viewModel.StageBatchChapters([Folder("第1章", Source("1.jpg"))]);
        var expired = 0;
        viewModel.SessionExpired += (_, _) => expired++;

        await viewModel.UploadBatchChaptersAsync(CancellationToken.None);

        var chapter = viewModel.PendingBatchChapters.Single();
        Assert.AreEqual(1, expired);
        Assert.AreEqual(ComicImageUploadState.Pending, chapter.Images.Single().State);
        Assert.AreEqual(ComicChapterUploadState.Ready, chapter.State);
        Assert.IsFalse(viewModel.IsUploading);
        Assert.IsTrue(viewModel.CanUploadBatchChapters);
    }

    [TestMethod]
    public async Task ReplaceFailedBatchImage_UnauthorizedKeepsReplacementAndRestoresPendingState()
    {
        var service = LoadedService();
        service.UploadHandler = (_, _) => Task.FromException<ImageUploadBatchResult>(
            new AppException(AppErrorKind.Unauthorized, "unsafe"));
        var viewModel = CreateViewModel(service);
        await viewModel.LoadAsync(42, Profile(), CancellationToken.None);
        viewModel.StageBatchChapters([Folder("第1章", Source("bad.jpg"))]);
        var chapter = viewModel.PendingBatchChapters.Single();
        var image = chapter.Images.Single();
        var originalId = image.Id;
        var originalPosition = image.Position;
        image.Fail("failed");
        chapter.State = ComicChapterUploadState.Failed;
        var expired = 0;
        viewModel.SessionExpired += (_, _) => expired++;

        await viewModel.ReplaceFailedBatchImageAsync(
            image.Id,
            "replacement.jpg",
            @"C:\replacement\replacement.jpg",
            CancellationToken.None);

        Assert.AreEqual(1, expired);
        Assert.AreEqual(originalId, image.Id);
        Assert.AreEqual(originalPosition, image.Position);
        Assert.AreEqual("replacement.jpg", image.FileName);
        Assert.AreEqual(@"C:\replacement\replacement.jpg", image.FilePath);
        Assert.AreEqual(ComicImageUploadState.Pending, image.State);
        Assert.AreEqual(ComicChapterUploadState.Ready, chapter.State);
        Assert.IsFalse(viewModel.IsUploading);
    }

    [TestMethod]
    public async Task ReplaceFailedBatchImage_CreateUnauthorizedPreservesUploadedImageAndRetryableFailure()
    {
        var service = LoadedService();
        service.UploadHandler = SuccessfulUpload;
        service.CreateChapterHandler = (_, _, _, _) =>
            Task.FromException<CreateChapterResult>(
                new AppException(AppErrorKind.Unauthorized, "unsafe"));
        var viewModel = CreateViewModel(service);
        await viewModel.LoadAsync(42, Profile(), CancellationToken.None);
        viewModel.StageBatchChapters([Folder("第1章", Source("bad.jpg"))]);
        var chapter = viewModel.PendingBatchChapters.Single();
        var image = chapter.Images.Single();
        image.Fail("failed");
        chapter.State = ComicChapterUploadState.Failed;
        var expired = 0;
        viewModel.SessionExpired += (_, _) => expired++;

        await viewModel.ReplaceFailedBatchImageAsync(
            image.Id,
            "replacement.jpg",
            @"C:\replacement\replacement.jpg",
            CancellationToken.None);

        Assert.AreEqual(1, expired);
        Assert.AreEqual(ComicImageUploadState.Uploaded, image.State);
        Assert.AreEqual(ComicChapterUploadState.Failed, chapter.State);
        Assert.AreEqual("登录已失效，请重新登录。", chapter.ErrorMessage);
        Assert.IsTrue(chapter.CanRetryCreate);
        Assert.IsFalse(viewModel.IsUploading);
    }

    [TestMethod]
    public async Task UploadBatchChapters_CreateUnauthorizedRestoresRetryableChapter()
    {
        var service = LoadedService();
        service.UploadHandler = SuccessfulUpload;
        service.CreateChapterHandler = (_, _, _, _) =>
            Task.FromException<CreateChapterResult>(
                new AppException(AppErrorKind.Unauthorized, "unsafe"));
        var viewModel = CreateViewModel(service);
        await viewModel.LoadAsync(42, Profile(), CancellationToken.None);
        viewModel.StageBatchChapters([Folder("第1章", Source("1.jpg"))]);
        var expired = 0;
        viewModel.SessionExpired += (_, _) => expired++;

        await viewModel.UploadBatchChaptersAsync(CancellationToken.None);

        var chapter = viewModel.PendingBatchChapters.Single();
        Assert.AreEqual(1, expired);
        Assert.AreEqual(ComicImageUploadState.Uploaded, chapter.Images.Single().State);
        Assert.AreEqual(ComicChapterUploadState.Failed, chapter.State);
        Assert.AreEqual("登录已失效，请重新登录。", chapter.ErrorMessage);
        Assert.IsTrue(chapter.CanRetryCreate);
        Assert.IsFalse(viewModel.IsUploading);
    }

    [TestMethod]
    public async Task UploadBatchChapters_CancellationRestoresPendingImageAndIdleChapter()
    {
        using var source = new CancellationTokenSource();
        var service = LoadedService();
        service.UploadHandler = (_, token) =>
            Task.FromCanceled<ImageUploadBatchResult>(token);
        var viewModel = CreateViewModel(service);
        await viewModel.LoadAsync(42, Profile(), CancellationToken.None);
        viewModel.StageBatchChapters([Folder("第1章", Source("1.jpg"))]);
        source.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            viewModel.UploadBatchChaptersAsync(source.Token));

        var chapter = viewModel.PendingBatchChapters.Single();
        Assert.AreEqual(ComicImageUploadState.Pending, chapter.Images.Single().State);
        Assert.AreEqual(ComicChapterUploadState.Ready, chapter.State);
        Assert.IsFalse(viewModel.IsUploading);
    }

    [TestMethod]
    public async Task UploadBatchChapters_StructuralCancellationRestoresFailedChapter()
    {
        using var source = new CancellationTokenSource();
        var service = LoadedService();
        service.UploadHandler = (_, token) =>
            Task.FromCanceled<ImageUploadBatchResult>(token);
        var viewModel = CreateViewModel(service);
        await viewModel.LoadAsync(42, Profile(), CancellationToken.None);
        viewModel.StageBatchChapters(
        [
            new LocalComicChapterSelection(
                @"C:\chapters\第1章",
                "第1章",
                [Source("1.jpg")],
                "文件夹扫描失败。")
        ]);
        source.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            viewModel.UploadBatchChaptersAsync(source.Token));

        var chapter = viewModel.PendingBatchChapters.Single();
        Assert.AreEqual(ComicImageUploadState.Pending, chapter.Images.Single().State);
        Assert.AreEqual(ComicChapterUploadState.Failed, chapter.State);
        Assert.AreEqual("文件夹扫描失败。", chapter.ErrorMessage);
        Assert.IsFalse(viewModel.IsUploading);
    }

    [TestMethod]
    public async Task BatchSelectionErrorWithSuccessfulImagesStillBlocksCreation()
    {
        var service = LoadedService();
        service.UploadHandler = SuccessfulUpload;
        var viewModel = CreateViewModel(service);
        await viewModel.LoadAsync(42, Profile(), CancellationToken.None);
        viewModel.StageBatchChapters(
        [
            new LocalComicChapterSelection(
                @"C:\chapters\第1章",
                "第1章",
                [Source("1.jpg")],
                "文件夹扫描失败。"),
            Folder("第2章", Source("2.jpg"))
        ]);

        await viewModel.UploadBatchChaptersAsync(CancellationToken.None);

        Assert.AreEqual(2, service.UploadCalls);
        Assert.AreEqual(0, service.CreateChapterCalls);
        Assert.AreEqual(
            ComicImageUploadState.Uploaded,
            viewModel.PendingBatchChapters[0].Images.Single().State);
        Assert.AreEqual(
            ComicChapterUploadState.Failed,
            viewModel.PendingBatchChapters[0].State);
        Assert.AreEqual(
            "文件夹扫描失败。",
            viewModel.PendingBatchChapters[0].ErrorMessage);
        Assert.AreEqual(
            ComicChapterUploadState.WaitingForPreviousChapter,
            viewModel.PendingBatchChapters[1].State);
    }

    [TestMethod]
    public async Task BatchSelectionErrorSurvivesUploadReplacementAndImageRemoval()
    {
        var service = LoadedService();
        service.UploadHandler = (sources, _) => Task.FromResult(
            new ImageUploadBatchResult(
                sources.Where(source => !source.FileName.StartsWith("bad", StringComparison.Ordinal))
                    .Select(source => new UploadedImage(
                        source.FileName,
                        $"https://i/{source.FileName}",
                        source.Id))
                    .ToArray(),
                sources.Where(source => source.FileName.StartsWith("bad", StringComparison.Ordinal))
                    .Select(source => new FailedImage(
                        source.FileName,
                        "failed",
                        source.Id))
                    .ToArray()));
        service.CreateChapterHandler = (_, sortNum, draft, _) => Task.FromResult(
            new CreateChapterResult(
                88,
                [.. Details().Chapters, new ComicChapterSummary(88, sortNum, draft.Title)]));
        var viewModel = CreateViewModel(service);
        await viewModel.LoadAsync(42, Profile(), CancellationToken.None);
        viewModel.StageBatchChapters(
        [
            new LocalComicChapterSelection(
                @"C:\chapters\第1章",
                "第1章",
                [Source("bad1.jpg"), Source("bad2.jpg")],
                "文件夹扫描失败。"),
            Folder("第2章", Source("2.jpg"))
        ]);

        await viewModel.UploadBatchChaptersAsync(CancellationToken.None);

        var structural = viewModel.PendingBatchChapters[0];
        var waiting = viewModel.PendingBatchChapters[1];
        Assert.IsTrue(structural.HasSelectionError);
        Assert.AreEqual("文件夹扫描失败。", structural.SelectionErrorMessage);
        StringAssert.Contains(structural.ErrorMessage, "bad1.jpg");
        Assert.AreEqual(ComicChapterUploadState.Failed, structural.State);
        Assert.AreEqual(ComicChapterUploadState.WaitingForPreviousChapter, waiting.State);

        service.UploadHandler = SuccessfulUpload;
        var firstFailed = structural.Images.First(
            image => image.State == ComicImageUploadState.Failed);
        await viewModel.ReplaceFailedBatchImageAsync(
            firstFailed.Id,
            "replacement.jpg",
            @"C:\replacement\replacement.jpg",
            CancellationToken.None);

        var remainingFailed = structural.Images.Single(
            image => image.State == ComicImageUploadState.Failed);
        StringAssert.Contains(structural.ErrorMessage, remainingFailed.FileName);
        await viewModel.RemoveBatchImageAsync(
            remainingFailed.Id,
            CancellationToken.None);

        Assert.AreEqual(ComicChapterUploadState.Failed, structural.State);
        Assert.AreEqual("文件夹扫描失败。", structural.ErrorMessage);
        Assert.IsFalse(structural.CanRetryCreate);
        Assert.AreEqual(0, service.CreateChapterCalls);
        Assert.AreEqual(ComicChapterUploadState.WaitingForPreviousChapter, waiting.State);

        await viewModel.RemoveBatchChapterAsync(
            structural.Id,
            CancellationToken.None);

        Assert.AreEqual(1, service.CreateChapterCalls);
        Assert.AreEqual(ComicChapterUploadState.Completed, waiting.State);
    }

    [TestMethod]
    public async Task ReplaceFailedBatchImage_CreatesReadyChaptersInOrderWithoutReuploadingLaterChapter()
    {
        var service = LoadedService();
        service.GetChapterHandler = (_, chapterId, _) => Task.FromResult(
            new ComicChapterDraft(chapterId, "One", ["https://i/1.jpg"]));
        service.UploadHandler = (sources, _) => Task.FromResult(
            new ImageUploadBatchResult(
                sources.Select(source => new UploadedImage(
                    source.FileName,
                    $"https://i/{source.FileName}",
                    source.Id)).ToArray(),
                []));
        service.CreateChapterHandler = (_, sortNum, draft, _) =>
        {
            var newChapterId = 79L + service.CreateChapterCalls;
            var chapters = Details().Chapters
                .Concat(service.CreatedChapterDrafts.Select((createdDraft, index) =>
                    new ComicChapterSummary(80L + index, 2 + index, createdDraft.Title)))
                .ToArray();
            return Task.FromResult(new CreateChapterResult(newChapterId, chapters));
        };
        var viewModel = CreateViewModel(service);
        await viewModel.LoadAsync(42, Profile(), CancellationToken.None);
        await viewModel.SelectChapterAsync(
            viewModel.Chapters[0],
            false,
            CancellationToken.None);
        viewModel.StageBatchChapters(
        [
            Folder("第1章", Source("bad.jpg")),
            Folder("第2章", Source("2.jpg"))
        ]);
        var first = viewModel.PendingBatchChapters[0];
        first.Images[0].Fail("failed");
        first.State = ComicChapterUploadState.Failed;
        var second = viewModel.PendingBatchChapters[1];
        second.Images[0].Complete("https://i/2.jpg");
        second.State = ComicChapterUploadState.WaitingForPreviousChapter;

        await viewModel.ReplaceFailedBatchImageAsync(
            first.Images[0].Id,
            "replacement.jpg",
            @"C:\replacement.jpg",
            CancellationToken.None);

        Assert.AreEqual(1, service.UploadCalls);
        CollectionAssert.AreEqual(
            new[] { "第1章", "第2章" },
            service.CreatedChapterDrafts.Select(item => item.Title).ToArray());
        CollectionAssert.AreEqual(new[] { 2, 3 }, service.CreatedSortNums.ToArray());
        Assert.AreEqual(70L, viewModel.SelectedChapter?.Id);
        Assert.IsFalse(viewModel.HasPendingBatchChapters);
    }

    [TestMethod]
    public async Task UploadBatchChapters_MissingCreatedSummaryAddsFallbackAndPreservesSelectedInstance()
    {
        var service = LoadedService();
        service.GetChapterHandler = (_, chapterId, _) => Task.FromResult(
            new ComicChapterDraft(chapterId, "One", ["https://i/1.jpg"]));
        service.UploadHandler = SuccessfulUpload;
        service.CreateChapterHandler = (_, _, _, _) => Task.FromResult(
            new CreateChapterResult(88, Details().Chapters));
        var viewModel = CreateViewModel(service);
        await viewModel.LoadAsync(42, Profile(), CancellationToken.None);
        await viewModel.SelectChapterAsync(
            viewModel.Chapters.Single(),
            false,
            CancellationToken.None);
        viewModel.StageBatchChapters([Folder("第2章", Source("2.jpg"))]);

        await viewModel.UploadBatchChaptersAsync(CancellationToken.None);

        var fallback = viewModel.Chapters.Single(chapter => chapter.Id == 88);
        Assert.AreEqual(2, fallback.SortNum);
        Assert.AreEqual("第2章", fallback.Title);
        Assert.AreEqual(70L, viewModel.SelectedChapter?.Id);
        Assert.AreSame(
            viewModel.Chapters.Single(chapter => chapter.Id == 70),
            viewModel.SelectedChapter);
    }

    [TestMethod]
    public async Task RetryBatchChapterCreation_DoesNotUploadImagesAgain()
    {
        var service = LoadedService();
        service.UploadHandler = (sources, _) => Task.FromResult(
            new ImageUploadBatchResult(
                sources.Select(source => new UploadedImage(
                    source.FileName,
                    $"https://i/{source.FileName}",
                    source.Id)).ToArray(),
                []));
        var createAttempt = 0;
        service.CreateChapterHandler = (_, sortNum, draft, _) =>
            ++createAttempt == 1
                ? Task.FromException<CreateChapterResult>(
                    new AppException(AppErrorKind.Transport, "create failed"))
                : Task.FromResult(new CreateChapterResult(
                    88,
                    [.. Details().Chapters, new ComicChapterSummary(88, sortNum, draft.Title)]));
        var viewModel = CreateViewModel(service);
        await viewModel.LoadAsync(42, Profile(), CancellationToken.None);
        viewModel.StageBatchChapters([Folder("第2章", Source("1.jpg"))]);

        await viewModel.UploadBatchChaptersAsync(CancellationToken.None);
        var uploadCalls = service.UploadCalls;
        await viewModel.RetryBatchChapterCreationAsync(
            viewModel.PendingBatchChapters.Single().Id,
            CancellationToken.None);

        Assert.AreEqual(2, service.CreateChapterCalls);
        Assert.AreEqual(uploadCalls, service.UploadCalls);
        Assert.AreEqual(
            ComicChapterUploadState.Completed,
            viewModel.PendingBatchChapters.Single().State);
    }

    [TestMethod]
    public async Task UploadBatchChapters_MissingImageResultBlocksCreationByActualImageState()
    {
        var service = LoadedService();
        service.UploadHandler = (sources, _) =>
        {
            var source = sources.Single();
            return Task.FromResult(source.FileName == "missing.jpg"
                ? new ImageUploadBatchResult([], [])
                : new ImageUploadBatchResult(
                    [new UploadedImage(source.FileName, $"https://i/{source.FileName}", source.Id)],
                    []));
        };
        var viewModel = CreateViewModel(service);
        await viewModel.LoadAsync(42, Profile(), CancellationToken.None);
        viewModel.StageBatchChapters(
        [
            Folder("第1章", Source("missing.jpg")),
            Folder("第2章", Source("2.jpg"))
        ]);

        await viewModel.UploadBatchChaptersAsync(CancellationToken.None);

        Assert.AreEqual(2, service.UploadCalls);
        Assert.AreEqual(0, service.CreateChapterCalls);
        Assert.AreEqual(
            ComicImageUploadState.Failed,
            viewModel.PendingBatchChapters[0].Images.Single().State);
        Assert.AreEqual(
            ComicChapterUploadState.WaitingForPreviousChapter,
            viewModel.PendingBatchChapters[1].State);
        Assert.IsNull(viewModel.NoticeMessage);
    }

    [TestMethod]
    public async Task UploadBatchChapters_CreateFailureBlocksLaterReadyChapter()
    {
        var service = LoadedService();
        service.UploadHandler = SuccessfulUpload;
        service.CreateChapterHandler = (_, _, _, _) =>
            Task.FromException<CreateChapterResult>(
                new AppException(AppErrorKind.Transport, "create failed"));
        var viewModel = CreateViewModel(service);
        await viewModel.LoadAsync(42, Profile(), CancellationToken.None);
        viewModel.StageBatchChapters(
        [
            Folder("第1章", Source("1.jpg")),
            Folder("第2章", Source("2.jpg"))
        ]);

        await viewModel.UploadBatchChaptersAsync(CancellationToken.None);

        Assert.AreEqual(2, service.UploadCalls);
        Assert.AreEqual(1, service.CreateChapterCalls);
        Assert.AreEqual(
            ComicChapterUploadState.Failed,
            viewModel.PendingBatchChapters[0].State);
        Assert.AreEqual(
            ComicChapterUploadState.WaitingForPreviousChapter,
            viewModel.PendingBatchChapters[1].State);
        Assert.IsNull(viewModel.NoticeMessage);
    }

    [TestMethod]
    public async Task UploadBatchChapters_ThrownImageFailureStillUploadsLaterChapter()
    {
        var service = LoadedService();
        service.UploadHandler = (sources, _) =>
        {
            var source = sources.Single();
            return source.FileName == "bad.jpg"
                ? Task.FromException<ImageUploadBatchResult>(
                    new AppException(AppErrorKind.Transport, "upload failed"))
                : SuccessfulUpload(sources, CancellationToken.None);
        };
        var viewModel = CreateViewModel(service);
        await viewModel.LoadAsync(42, Profile(), CancellationToken.None);
        viewModel.StageBatchChapters(
        [
            Folder("第1章", Source("bad.jpg")),
            Folder("第2章", Source("2.jpg"))
        ]);

        await viewModel.UploadBatchChaptersAsync(CancellationToken.None);

        Assert.AreEqual(2, service.UploadCalls);
        Assert.AreEqual(0, service.CreateChapterCalls);
        Assert.AreEqual(
            ComicImageUploadState.Failed,
            viewModel.PendingBatchChapters[0].Images.Single().State);
        Assert.AreEqual(
            ComicChapterUploadState.WaitingForPreviousChapter,
            viewModel.PendingBatchChapters[1].State);
    }

    [TestMethod]
    public async Task UploadBatchChapters_NewLaterImagesDoNotImplicitlyRetryEarlierFailedCreation()
    {
        var service = LoadedService();
        service.UploadHandler = SuccessfulUpload;
        service.CreateChapterHandler = (_, _, _, _) =>
            Task.FromException<CreateChapterResult>(
                new AppException(AppErrorKind.Transport, "create failed"));
        var viewModel = CreateViewModel(service);
        await viewModel.LoadAsync(42, Profile(), CancellationToken.None);
        viewModel.StageBatchChapters([Folder("第1章", Source("1.jpg"))]);
        await viewModel.UploadBatchChaptersAsync(CancellationToken.None);
        viewModel.StageBatchChapters([Folder("第2章", Source("2.jpg"))]);

        await viewModel.UploadBatchChaptersAsync(CancellationToken.None);

        Assert.AreEqual(2, service.UploadCalls);
        Assert.AreEqual(1, service.CreateChapterCalls);
        Assert.AreEqual(
            ComicChapterUploadState.Failed,
            viewModel.PendingBatchChapters[0].State);
        Assert.AreEqual(
            ComicChapterUploadState.WaitingForPreviousChapter,
            viewModel.PendingBatchChapters[1].State);
    }

    [TestMethod]
    public async Task RemoveBlockingBatchChapter_ResumesReadyPrefix()
    {
        var service = LoadedService();
        service.CreateChapterHandler = (_, sortNum, draft, _) => Task.FromResult(
            new CreateChapterResult(
                88,
                [.. Details().Chapters, new ComicChapterSummary(88, sortNum, draft.Title)]));
        var viewModel = CreateViewModel(service);
        await viewModel.LoadAsync(42, Profile(), CancellationToken.None);
        viewModel.StageBatchChapters(
        [
            Folder("第1章", Source("bad.jpg")),
            Folder("第2章", Source("2.jpg"))
        ]);
        var blocker = viewModel.PendingBatchChapters[0];
        blocker.Images[0].Fail("failed");
        blocker.State = ComicChapterUploadState.Failed;
        var ready = viewModel.PendingBatchChapters[1];
        ready.Images[0].Complete("https://i/2.jpg");
        ready.State = ComicChapterUploadState.WaitingForPreviousChapter;

        await viewModel.RemoveBatchChapterAsync(
            blocker.Id,
            CancellationToken.None);

        Assert.AreEqual(1, service.CreateChapterCalls);
        Assert.AreEqual("第2章", service.CreatedChapterDrafts.Single().Title);
        Assert.AreEqual(ComicChapterUploadState.Completed, ready.State);
        Assert.IsFalse(viewModel.HasPendingBatchChapters);
    }

    [TestMethod]
    public async Task RemoveEarliestReadyBatchChapter_ResumesWaitingChapter()
    {
        var service = LoadedService();
        service.CreateChapterHandler = (_, sortNum, draft, _) => Task.FromResult(
            new CreateChapterResult(
                88,
                [.. Details().Chapters, new ComicChapterSummary(88, sortNum, draft.Title)]));
        var viewModel = CreateViewModel(service);
        await viewModel.LoadAsync(42, Profile(), CancellationToken.None);
        viewModel.StageBatchChapters(
        [
            Folder("第1章", Source("1.jpg")),
            Folder("第2章", Source("2.jpg"))
        ]);
        var earliest = viewModel.PendingBatchChapters[0];
        earliest.Images.Single().Complete("https://i/1.jpg");
        earliest.State = ComicChapterUploadState.Ready;
        var waiting = viewModel.PendingBatchChapters[1];
        waiting.Images.Single().Complete("https://i/2.jpg");
        waiting.State = ComicChapterUploadState.WaitingForPreviousChapter;

        await viewModel.RemoveBatchChapterAsync(
            earliest.Id,
            CancellationToken.None);

        Assert.AreEqual(1, service.CreateChapterCalls);
        Assert.AreEqual("第2章", service.CreatedChapterDrafts.Single().Title);
        Assert.AreEqual(ComicChapterUploadState.Completed, waiting.State);
    }

    [TestMethod]
    public async Task RemoveLaterWaitingBatchChapter_DoesNotRetryEarlierFailedCreation()
    {
        var createAttempt = 0;
        var service = LoadedService();
        service.UploadHandler = SuccessfulUpload;
        service.CreateChapterHandler = (_, sortNum, draft, _) =>
            ++createAttempt == 1
                ? Task.FromException<CreateChapterResult>(
                    new AppException(AppErrorKind.Transport, "create failed"))
                : Task.FromResult(new CreateChapterResult(
                    88,
                    [.. Details().Chapters, new ComicChapterSummary(88, sortNum, draft.Title)]));
        var viewModel = CreateViewModel(service);
        await viewModel.LoadAsync(42, Profile(), CancellationToken.None);
        viewModel.StageBatchChapters(
        [
            Folder("第1章", Source("1.jpg")),
            Folder("第2章", Source("2.jpg"))
        ]);
        await viewModel.UploadBatchChaptersAsync(CancellationToken.None);
        var later = viewModel.PendingBatchChapters[1];

        await viewModel.RemoveBatchChapterAsync(
            later.Id,
            CancellationToken.None);

        Assert.AreEqual(1, service.CreateChapterCalls);
        Assert.AreEqual(
            ComicChapterUploadState.Failed,
            viewModel.PendingBatchChapters.Single().State);
    }

    [TestMethod]
    public async Task RemoveBatchImage_PreservesCurrentOrderAndEmptyChapterBlocksCreation()
    {
        var service = LoadedService();
        var viewModel = CreateViewModel(service);
        await viewModel.LoadAsync(42, Profile(), CancellationToken.None);
        viewModel.StageBatchChapters(
        [
            Folder(
                "第1章",
                Source("1.jpg"),
                Source("2.jpg"),
                Source("3.jpg")),
            Folder("第2章", Source("only.jpg"))
        ]);
        var orderedChapter = viewModel.PendingBatchChapters[0];
        orderedChapter.Images[0].Fail("replace");
        orderedChapter.Images[0].Replace("99.jpg", @"C:\replacement\99.jpg");
        var removed = orderedChapter.Images[1];
        removed.Fail("remove");

        await viewModel.RemoveBatchImageAsync(removed.Id, CancellationToken.None);

        CollectionAssert.AreEqual(
            new[] { "99.jpg", "3.jpg" },
            orderedChapter.Images.Select(image => image.FileName).ToArray());
        CollectionAssert.AreEqual(
            new[] { 0, 1 },
            orderedChapter.Images.Select(image => image.Position).ToArray());

        var emptyChapter = viewModel.PendingBatchChapters[1];
        emptyChapter.Images[0].Fail("remove");
        await viewModel.RemoveBatchImageAsync(
            emptyChapter.Images[0].Id,
            CancellationToken.None);

        Assert.AreEqual(ComicChapterUploadState.Failed, emptyChapter.State);
        Assert.AreEqual("没有支持的图片。", emptyChapter.ErrorMessage);
        Assert.AreEqual(0, service.CreateChapterCalls);
    }

    [TestMethod]
    public async Task RemoveLaterBatchImage_DoesNotRetryEarlierFailedCreation()
    {
        var service = LoadedService();
        service.UploadHandler = (sources, _) => Task.FromResult(
            new ImageUploadBatchResult(
                sources.Where(source => source.FileName != "bad2.jpg")
                    .Select(source => new UploadedImage(
                        source.FileName,
                        $"https://i/{source.FileName}",
                        source.Id))
                    .ToArray(),
                sources.Where(source => source.FileName == "bad2.jpg")
                    .Select(source => new FailedImage(
                        source.FileName,
                        "failed",
                        source.Id))
                    .ToArray()));
        service.CreateChapterHandler = (_, _, _, _) =>
            Task.FromException<CreateChapterResult>(
                new AppException(AppErrorKind.Transport, "create failed"));
        var viewModel = CreateViewModel(service);
        await viewModel.LoadAsync(42, Profile(), CancellationToken.None);
        viewModel.StageBatchChapters(
        [
            Folder("第1章", Source("1.jpg")),
            Folder("第2章", Source("2.jpg"), Source("bad2.jpg"))
        ]);
        await viewModel.UploadBatchChaptersAsync(CancellationToken.None);
        var later = viewModel.PendingBatchChapters[1];
        var failedImage = later.Images.Single(
            image => image.State == ComicImageUploadState.Failed);

        await viewModel.RemoveBatchImageAsync(
            failedImage.Id,
            CancellationToken.None);

        Assert.AreEqual(1, service.CreateChapterCalls);
        Assert.AreEqual(
            ComicChapterUploadState.Failed,
            viewModel.PendingBatchChapters[0].State);
        Assert.AreEqual(
            ComicChapterUploadState.WaitingForPreviousChapter,
            later.State);
    }

    [TestMethod]
    public async Task UploadBatchChapters_ConcurrentSecondCallDoesNotStartAnotherCoordinator()
    {
        var uploadCompletion = new TaskCompletionSource<ImageUploadBatchResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var service = LoadedService();
        service.UploadHandler = (_, _) => uploadCompletion.Task;
        service.CreateChapterHandler = (_, sortNum, draft, _) => Task.FromResult(
            new CreateChapterResult(
                88,
                [.. Details().Chapters, new ComicChapterSummary(88, sortNum, draft.Title)]));
        var viewModel = CreateViewModel(service);
        await viewModel.LoadAsync(42, Profile(), CancellationToken.None);
        var source = Source("1.jpg");
        viewModel.StageBatchChapters([Folder("第2章", source)]);

        var firstUpload = viewModel.UploadBatchChaptersAsync(CancellationToken.None);
        var secondUpload = viewModel.UploadBatchChaptersAsync(CancellationToken.None);

        Assert.AreEqual(1, service.UploadCalls);
        uploadCompletion.SetResult(new ImageUploadBatchResult(
            [new UploadedImage(source.FileName, "https://i/1.jpg", source.Id)],
            []));
        await Task.WhenAll(firstUpload, secondUpload);

        Assert.AreEqual(1, service.UploadCalls);
        Assert.AreEqual(1, service.CreateChapterCalls);
        Assert.IsFalse(viewModel.IsUploading);
    }

    [TestMethod]
    public async Task RetryBatchChapterCreation_RequiresFailedCreateState()
    {
        var service = LoadedService();
        service.CreateChapterHandler = (_, sortNum, draft, _) => Task.FromResult(
            new CreateChapterResult(
                88,
                [.. Details().Chapters, new ComicChapterSummary(88, sortNum, draft.Title)]));
        var viewModel = CreateViewModel(service);
        await viewModel.LoadAsync(42, Profile(), CancellationToken.None);
        viewModel.StageBatchChapters([Folder("第2章", Source("1.jpg"))]);
        var chapter = viewModel.PendingBatchChapters.Single();
        chapter.Images.Single().Complete("https://i/1.jpg");
        chapter.State = ComicChapterUploadState.Ready;

        await viewModel.RetryBatchChapterCreationAsync(
            chapter.Id,
            CancellationToken.None);

        Assert.AreEqual(0, service.CreateChapterCalls);

        chapter.State = ComicChapterUploadState.Failed;
        await viewModel.RetryBatchChapterCreationAsync(
            chapter.Id,
            CancellationToken.None);
        await viewModel.RetryBatchChapterCreationAsync(
            chapter.Id,
            CancellationToken.None);

        Assert.AreEqual(1, service.CreateChapterCalls);
        Assert.AreEqual(ComicChapterUploadState.Completed, chapter.State);
    }

    [TestMethod]
    public void PendingComicImage_TransitionsAndReplacementKeepIdentityAndPosition()
    {
        var source = Source("1.jpg", @"C:\images\1.jpg");
        var image = new PendingComicImage(source, 3);
        var changedProperties = new List<string?>();
        image.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);

        Assert.AreEqual(ComicImageUploadState.Pending, image.State);
        Assert.AreEqual("待上传", image.StatusText);
        Assert.IsTrue(image.CanRemove);
        Assert.IsFalse(image.CanReplace);

        image.BeginUpload();
        Assert.AreEqual(ComicImageUploadState.Uploading, image.State);
        Assert.AreEqual("上传中", image.StatusText);
        Assert.IsFalse(image.CanRemove);

        image.Complete("https://i/1.jpg");
        Assert.AreEqual("https://i/1.jpg", image.UploadedUrl);
        Assert.AreEqual("已上传", image.StatusText);

        image.Fail("failed");
        Assert.AreEqual("failed", image.ErrorMessage);
        Assert.AreEqual("上传失败", image.StatusText);
        Assert.IsTrue(image.CanRemove);
        Assert.IsTrue(image.CanReplace);

        image.Replace("replacement.png", @"C:\images\replacement.png");

        Assert.AreEqual(source.Id, image.Id);
        Assert.AreEqual(3, image.Position);
        Assert.AreEqual("replacement.png", image.FileName);
        Assert.AreEqual(@"C:\images\replacement.png", image.FilePath);
        Assert.IsNull(image.UploadedUrl);
        Assert.IsNull(image.ErrorMessage);
        Assert.AreEqual(ComicImageUploadState.Pending, image.State);
        Assert.AreEqual(image.Id, image.ToSource().Id);
        CollectionAssert.Contains(changedProperties, nameof(PendingComicImage.CanRemove));
        CollectionAssert.Contains(changedProperties, nameof(PendingComicImage.CanReplace));
        CollectionAssert.Contains(changedProperties, nameof(PendingComicImage.StatusText));
    }

    [TestMethod]
    public void PendingComicChapter_DerivedStateMatchesImagesAndErrors()
    {
        var chapter = new PendingComicChapter(
            Guid.NewGuid(),
            @"C:\chapters\第2章",
            "第2章",
            [],
            null);
        var changedProperties = new List<string?>();
        chapter.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);
        var image = new PendingComicImage(Source("1.jpg"), 0);

        Assert.AreEqual(ComicChapterUploadState.Ready, chapter.State);
        Assert.AreEqual("待上传", chapter.StatusText);
        Assert.IsFalse(chapter.HasValidImages);
        Assert.IsFalse(chapter.AllImagesUploaded);
        Assert.IsFalse(chapter.CanRetryCreate);

        chapter.Images.Add(image);
        CollectionAssert.Contains(changedProperties, nameof(PendingComicChapter.HasValidImages));
        CollectionAssert.Contains(changedProperties, nameof(PendingComicChapter.AllImagesUploaded));
        CollectionAssert.Contains(changedProperties, nameof(PendingComicChapter.CanRetryCreate));
        Assert.IsTrue(chapter.HasValidImages);

        chapter.Images.Remove(image);
        chapter.Images.Add(image);
        image.Complete("https://i/1.jpg");
        chapter.State = ComicChapterUploadState.Failed;
        chapter.ErrorMessage = "create failed";

        Assert.IsTrue(chapter.AllImagesUploaded);
        Assert.IsTrue(chapter.CanRetryCreate);
        Assert.AreEqual("处理失败", chapter.StatusText);

        changedProperties.Clear();
        image.Fail("failed again");
        Assert.AreEqual(
            1,
            changedProperties.Count(
                name => name == nameof(PendingComicChapter.AllImagesUploaded)));
        Assert.AreEqual(
            1,
            changedProperties.Count(
                name => name == nameof(PendingComicChapter.CanRetryCreate)));

        chapter.Images.Remove(image);
        changedProperties.Clear();
        image.Replace("replacement.jpg", @"C:\images\replacement.jpg");
        Assert.AreEqual(0, changedProperties.Count);
    }

    [TestMethod]
    public async Task UploadAvailability_BusyTransitionsNotifyBothDerivedProperties()
    {
        var saveCompletion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var service = LoadedService();
        service.UpdateInfoHandler = (_, _, _) => saveCompletion.Task;
        var viewModel = CreateViewModel(service);
        await viewModel.LoadAsync(42, Profile(), CancellationToken.None);
        Assert.IsTrue(viewModel.BeginNewChapter(false));
        viewModel.StageChapterImages([Source("1.jpg")]);
        viewModel.StageBatchChapters([Folder("第2章", Source("1.jpg"))]);
        var changedProperties = new List<string?>();
        viewModel.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);

        var save = viewModel.SaveInfoAsync(CancellationToken.None);

        Assert.IsFalse(viewModel.CanUploadPendingChapterImages);
        Assert.IsFalse(viewModel.CanUploadBatchChapters);
        CollectionAssert.Contains(
            changedProperties,
            nameof(ComicEditorViewModel.CanUploadPendingChapterImages));
        CollectionAssert.Contains(
            changedProperties,
            nameof(ComicEditorViewModel.CanUploadBatchChapters));

        changedProperties.Clear();
        saveCompletion.SetResult();
        await save;

        Assert.IsTrue(viewModel.CanUploadPendingChapterImages);
        Assert.IsTrue(viewModel.CanUploadBatchChapters);
        CollectionAssert.Contains(
            changedProperties,
            nameof(ComicEditorViewModel.CanUploadPendingChapterImages));
        CollectionAssert.Contains(
            changedProperties,
            nameof(ComicEditorViewModel.CanUploadBatchChapters));
    }

    [TestMethod]
    public async Task UploadAvailability_UploadingTransitionsNotifyBothDerivedProperties()
    {
        var uploadCompletion = new TaskCompletionSource<ImageUploadBatchResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var service = LoadedService();
        service.UploadHandler = (_, _) => uploadCompletion.Task;
        var viewModel = CreateViewModel(service);
        await viewModel.LoadAsync(42, Profile(), CancellationToken.None);
        Assert.IsTrue(viewModel.BeginNewChapter(false));
        viewModel.StageChapterImages([Source("1.jpg")]);
        viewModel.StageBatchChapters([Folder("第2章", Source("1.jpg"))]);
        var cover = Source("cover.jpg");
        var changedProperties = new List<string?>();
        viewModel.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);

        var upload = viewModel.UploadCoverAsync(cover, CancellationToken.None);

        Assert.IsFalse(viewModel.CanUploadPendingChapterImages);
        Assert.IsFalse(viewModel.CanUploadBatchChapters);
        CollectionAssert.Contains(
            changedProperties,
            nameof(ComicEditorViewModel.CanUploadPendingChapterImages));
        CollectionAssert.Contains(
            changedProperties,
            nameof(ComicEditorViewModel.CanUploadBatchChapters));

        changedProperties.Clear();
        uploadCompletion.SetResult(new ImageUploadBatchResult(
            [new UploadedImage("cover.jpg", "https://i/cover.jpg", cover.Id)],
            []));
        await upload;

        Assert.IsTrue(viewModel.CanUploadPendingChapterImages);
        Assert.IsTrue(viewModel.CanUploadBatchChapters);
        CollectionAssert.Contains(
            changedProperties,
            nameof(ComicEditorViewModel.CanUploadPendingChapterImages));
        CollectionAssert.Contains(
            changedProperties,
            nameof(ComicEditorViewModel.CanUploadBatchChapters));
    }

    [TestMethod]
    public async Task PendingChapterImageState_BubblesUploadAvailabilityAndDirtyNotifications()
    {
        var viewModel = CreateViewModel(LoadedService());
        await viewModel.LoadAsync(42, Profile(), CancellationToken.None);
        Assert.IsTrue(viewModel.BeginNewChapter(false));
        viewModel.StageChapterImages([Source("1.jpg")]);
        var changedProperties = new List<string?>();
        viewModel.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);

        viewModel.PendingChapterImages.Single().BeginUpload();

        Assert.IsFalse(viewModel.CanUploadPendingChapterImages);
        Assert.IsTrue(viewModel.HasUnsavedChanges);
        CollectionAssert.Contains(
            changedProperties,
            nameof(ComicEditorViewModel.CanUploadPendingChapterImages));
        CollectionAssert.Contains(
            changedProperties,
            nameof(ComicEditorViewModel.HasUnsavedChanges));
    }

    [TestMethod]
    public async Task PendingBatchChanges_BubbleAvailabilityAndLastCompletionNotifications()
    {
        var viewModel = CreateViewModel(LoadedService());
        await viewModel.LoadAsync(42, Profile(), CancellationToken.None);
        viewModel.StageBatchChapters([Folder("第2章", Source("1.jpg"))]);
        var chapter = viewModel.PendingBatchChapters.Single();
        var changedProperties = new List<string?>();
        viewModel.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);

        chapter.Images.Clear();

        Assert.IsFalse(viewModel.CanUploadBatchChapters);
        CollectionAssert.Contains(
            changedProperties,
            nameof(ComicEditorViewModel.CanUploadBatchChapters));

        chapter.Images.Add(new PendingComicImage(Source("replacement.jpg"), 0));
        changedProperties.Clear();
        chapter.State = ComicChapterUploadState.Completed;

        Assert.IsFalse(viewModel.HasPendingBatchChapters);
        Assert.IsFalse(viewModel.HasUnsavedChanges);
        CollectionAssert.Contains(
            changedProperties,
            nameof(ComicEditorViewModel.HasPendingBatchChapters));
        CollectionAssert.Contains(
            changedProperties,
            nameof(ComicEditorViewModel.HasUnsavedChanges));
    }

    [TestMethod]
    public void BatchProgressText_EmptyQueueShowsZeroTotals()
    {
        var viewModel = CreateViewModel(LoadedService());

        Assert.AreEqual("图片 0/0，章节 0/0", viewModel.BatchProgressText);
    }

    [TestMethod]
    public async Task BatchProgressText_CountsOnlyTerminalImagesAndCompletedChapters()
    {
        var viewModel = CreateViewModel(LoadedService());
        await viewModel.LoadAsync(42, Profile(), CancellationToken.None);
        viewModel.StageBatchChapters(
        [
            Folder("第1章", Source("1.jpg"), Source("2.jpg")),
            Folder("第2章", Source("3.jpg"), Source("4.jpg"))
        ]);
        var first = viewModel.PendingBatchChapters[0];
        var second = viewModel.PendingBatchChapters[1];

        first.Images[0].BeginUpload();
        first.Images[1].Complete("https://i/2.jpg");
        second.Images[0].Fail("failed");
        first.State = ComicChapterUploadState.Completed;

        Assert.AreEqual("图片 2/4，章节 1/2", viewModel.BatchProgressText);
    }

    [TestMethod]
    public async Task BatchProgressText_ImageChapterAndCollectionChangesNotify()
    {
        var viewModel = CreateViewModel(LoadedService());
        await viewModel.LoadAsync(42, Profile(), CancellationToken.None);
        var changedProperties = new List<string?>();
        viewModel.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);

        viewModel.StageBatchChapters([Folder("第1章", Source("1.jpg"))]);
        CollectionAssert.Contains(
            changedProperties,
            nameof(ComicEditorViewModel.BatchProgressText));

        changedProperties.Clear();
        var chapter = viewModel.PendingBatchChapters.Single();
        chapter.Images.Single().BeginUpload();
        CollectionAssert.Contains(
            changedProperties,
            nameof(ComicEditorViewModel.BatchProgressText));

        changedProperties.Clear();
        chapter.State = ComicChapterUploadState.Completed;
        CollectionAssert.Contains(
            changedProperties,
            nameof(ComicEditorViewModel.BatchProgressText));

        changedProperties.Clear();
        chapter.Images.Clear();
        CollectionAssert.Contains(
            changedProperties,
            nameof(ComicEditorViewModel.BatchProgressText));
    }

    [TestMethod]
    public async Task UploadCoverAsync_SuccessUpdatesCoverAndMarksInfoDirty()
    {
        var source = Source("cover.jpg");
        var service = LoadedService();
        service.UploadHandler = (sources, _) => Task.FromResult(
            new ImageUploadBatchResult(
                [
                    new UploadedImage(
                        "unrelated.jpg",
                        "https://i/unrelated.jpg",
                        Guid.NewGuid()),
                    new UploadedImage(
                        "renamed-by-server.jpg",
                        "https://i/cover.jpg",
                        sources.Single().Id)
                ],
                []));
        var viewModel = CreateViewModel(service);
        await viewModel.LoadAsync(42, Profile(), CancellationToken.None);

        await viewModel.UploadCoverAsync(source, CancellationToken.None);

        Assert.AreEqual("https://i/cover.jpg", viewModel.Cover);
        Assert.IsTrue(viewModel.InfoHasUnsavedChanges);
    }

    [TestMethod]
    public async Task UploadCoverAsync_MatchingFailureKeepsCoverAndShowsNotice()
    {
        var source = Source("cover.jpg");
        var service = LoadedService();
        service.UploadHandler = (_, _) => Task.FromResult(
            new ImageUploadBatchResult(
                [],
                [
                    new FailedImage(
                        "unrelated.jpg",
                        "unrelated failure",
                        Guid.NewGuid()),
                    new FailedImage(
                        source.FileName,
                        "upload rejected",
                        source.Id)
                ]));
        var viewModel = CreateViewModel(service);
        await viewModel.LoadAsync(42, Profile(), CancellationToken.None);
        var originalCover = viewModel.Cover;

        await viewModel.UploadCoverAsync(source, CancellationToken.None);

        Assert.AreEqual(originalCover, viewModel.Cover);
        Assert.AreEqual("封面上传失败：upload rejected", viewModel.NoticeMessage);
        Assert.IsFalse(viewModel.IsUploading);
    }

    [TestMethod]
    public async Task UploadCoverAsync_TransportFailureUsesMappedErrorAndResetsUploading()
    {
        var service = LoadedService();
        service.UploadHandler = (_, _) => Task.FromException<ImageUploadBatchResult>(
            new AppException(AppErrorKind.Transport, "unsafe"));
        var viewModel = CreateViewModel(service);
        await viewModel.LoadAsync(42, Profile(), CancellationToken.None);
        var originalCover = viewModel.Cover;

        await viewModel.UploadCoverAsync(
            Source("cover.jpg"),
            CancellationToken.None);

        Assert.AreEqual(originalCover, viewModel.Cover);
        Assert.AreEqual(
            "网络连接失败，请检查网络后重试。",
            viewModel.ErrorMessage);
        Assert.IsFalse(viewModel.IsUploading);
    }

    [TestMethod]
    public async Task UploadCoverAsync_CancellationRethrowsAndResetsUploading()
    {
        using var source = new CancellationTokenSource();
        var service = LoadedService();
        service.UploadHandler = (_, token) =>
            Task.FromCanceled<ImageUploadBatchResult>(token);
        var viewModel = CreateViewModel(service);
        await viewModel.LoadAsync(42, Profile(), CancellationToken.None);
        var originalCover = viewModel.Cover;
        source.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            viewModel.UploadCoverAsync(Source("cover.jpg"), source.Token));

        Assert.AreEqual(originalCover, viewModel.Cover);
        Assert.IsFalse(viewModel.IsUploading);
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
        var source = Source("a-cover.jpg");

        var upload = viewModel.UploadCoverAsync(source, CancellationToken.None);
        await viewModel.LoadAsync(43, Profile(), CancellationToken.None);
        uploadCompletion.SetResult(new ImageUploadBatchResult(
            [new UploadedImage("a-cover.jpg", "https://i/uploaded-a.jpg", source.Id)],
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
        var source = Source("a-cover.jpg");

        var upload = viewModel.UploadCoverAsync(source, CancellationToken.None);
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
    public async Task UploadPendingChapterImagesAsync_SwitchChapterWhilePending_LateSuccessDoesNotChangeNewChapter()
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
        var source = Source("late.jpg");
        viewModel.StageChapterImages([source]);

        var upload = viewModel.UploadPendingChapterImagesAsync(CancellationToken.None);
        await viewModel.SelectChapterAsync(
            viewModel.Chapters[1],
            discardChapterChanges: true,
            CancellationToken.None);
        uploadCompletion.SetResult(new ImageUploadBatchResult(
            [new UploadedImage("late.jpg", "https://i/late.jpg", source.Id)],
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
        var source = Source("late.jpg");

        var upload = viewModel.UploadCoverAsync(source, CancellationToken.None);
        viewModel.Clear();
        uploadCompletion.SetResult(new ImageUploadBatchResult(
            [new UploadedImage("late.jpg", "https://i/late.jpg", source.Id)],
            [new FailedImage("failed.jpg", "failed", source.Id) ]));
        await upload;

        Assert.IsNull(viewModel.BookId);
        Assert.IsFalse(viewModel.IsLoaded);
        Assert.AreEqual(string.Empty, viewModel.Cover);
        Assert.IsNull(viewModel.NoticeMessage);
        Assert.IsFalse(viewModel.InfoHasUnsavedChanges);
    }

    [TestMethod]
    public async Task UploadImagesAsync_RequiresLoadedBookAndChapterContext()
    {
        var service = new FakeComicPublishingService();
        var viewModel = CreateViewModel(service);

        await viewModel.UploadCoverAsync(Source("cover.jpg"), CancellationToken.None);

        Assert.AreEqual(0, service.UploadCalls);

        service.GetEditDetailsHandler = (_, _) => Task.FromResult(Details());
        await viewModel.LoadAsync(42, Profile(), CancellationToken.None);
        viewModel.StageChapterImages([Source("chapter.jpg")]);
        await viewModel.UploadPendingChapterImagesAsync(CancellationToken.None);

        Assert.AreEqual(0, service.UploadCalls);
        Assert.AreEqual(0, viewModel.PendingChapterImages.Count);
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
    public async Task UploadPendingChapterImagesAsync_CancellationRethrowsAndRestoresPendingState()
    {
        using var source = new CancellationTokenSource();
        var service = LoadedService();
        service.UploadHandler = (_, token) =>
            Task.FromCanceled<ImageUploadBatchResult>(token);
        var viewModel = CreateViewModel(service);
        await viewModel.LoadAsync(42, Profile(), CancellationToken.None);
        Assert.IsTrue(viewModel.BeginNewChapter(false));
        viewModel.StageChapterImages([Source("1.jpg")]);
        source.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            viewModel.UploadPendingChapterImagesAsync(source.Token));

        Assert.IsFalse(viewModel.IsUploading);
        Assert.AreEqual(
            ComicImageUploadState.Pending,
            viewModel.PendingChapterImages.Single().State);
    }

    [TestMethod]
    public async Task UploadPendingChapterImagesAsync_WhilePending_DoesNotStartSecondBatch()
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
        viewModel.StageChapterImages([Source("1.jpg"), Source("2.jpg")]);

        var first = viewModel.UploadPendingChapterImagesAsync(CancellationToken.None);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await viewModel.UploadPendingChapterImagesAsync(CancellationToken.None);
        viewModel.ClearPendingChapterImages();

        Assert.AreEqual(1, service.UploadCalls);
        Assert.AreEqual(2, viewModel.PendingChapterImages.Count);
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
        var source = Source("chapter.jpg");
        viewModel.StageChapterImages([source]);

        var upload = viewModel.UploadPendingChapterImagesAsync(CancellationToken.None);
        await viewModel.SaveChapterAsync(CancellationToken.None);

        Assert.AreEqual(0, service.CreateChapterCalls);

        uploadCompletion.SetResult(new ImageUploadBatchResult(
            [new UploadedImage("chapter.jpg", "https://i/chapter.jpg", source.Id)],
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
    public async Task UploadPendingChapterImagesAsync_WhileCreatePending_DoesNotUpload()
    {
        var createCompletion = new TaskCompletionSource<CreateChapterResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var service = LoadedService();
        service.CreateChapterHandler = (_, _, _, _) => createCompletion.Task;
        service.UploadHandler = (_, _) => Task.FromResult(
            new ImageUploadBatchResult([], []));
        var viewModel = CreateViewModel(service);
        await viewModel.LoadAsync(42, Profile(), CancellationToken.None);
        Assert.IsTrue(viewModel.BeginNewChapter(false));
        viewModel.ChapterTitle = "New";
        viewModel.ChapterImages.Add("https://i/existing.jpg");

        var save = viewModel.SaveChapterAsync(CancellationToken.None);
        var pending = new PendingComicImage(Source("late.jpg"), 0);
        viewModel.PendingChapterImages.Add(pending);
        await viewModel.UploadPendingChapterImagesAsync(CancellationToken.None);

        Assert.AreEqual(0, service.UploadCalls);
        Assert.AreEqual(ComicImageUploadState.Pending, pending.State);
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

    internal static LocalImageSource Source(string name, string? path = null) =>
        new(Guid.NewGuid(), name, path ?? $@"C:\images\{name}");

    internal static LocalComicChapterSelection Folder(
        string title,
        params LocalImageSource[] images) =>
        new($@"C:\chapters\{title}", title, images, null);

    internal static Task<ImageUploadBatchResult> SuccessfulUpload(
        IReadOnlyList<LocalImageSource> sources,
        CancellationToken cancellationToken) =>
        Task.FromResult(new ImageUploadBatchResult(
            sources.Select(source => new UploadedImage(
                source.FileName,
                $"https://i/{source.FileName}",
                source.Id)).ToArray(),
            []));

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
    public Func<IReadOnlyList<LocalImageSource>, CancellationToken, Task<ImageUploadBatchResult>>? UploadHandler { get; set; }

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
    public IReadOnlyList<LocalImageSource> LastUploadSources { get; private set; } = [];
    public List<IReadOnlyList<LocalImageSource>> UploadedBatches { get; } = [];
    public List<ComicChapterDraft> CreatedChapterDrafts { get; } = [];
    public List<int> CreatedSortNums { get; } = [];

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
        CreatedChapterDrafts.Add(draft);
        CreatedSortNums.Add(sortNum);
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

    public Task<ImageUploadBatchResult> UploadImagesAsync(IReadOnlyList<LocalImageSource> files, CancellationToken cancellationToken)
    {
        UploadCalls++;
        UploadedBatches.Add(files.ToArray());
        LastUploadSources = files.ToArray();
        LastCancellationToken = cancellationToken;
        return UploadHandler?.Invoke(files, cancellationToken)
            ?? throw new AssertFailedException("UploadImagesAsync was not expected.");
    }
}
