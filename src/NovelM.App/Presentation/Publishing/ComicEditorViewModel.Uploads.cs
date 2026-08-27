using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using NovelM_App.Domain.Common;
using NovelM_App.Domain.Errors;
using NovelM_App.Domain.Publishing;

namespace NovelM_App.Presentation.Publishing;

public sealed partial class ComicEditorViewModel
{
    private readonly HashSet<PendingComicImage> _observedPendingChapterImages = [];
    private readonly HashSet<PendingComicChapter> _observedPendingBatchChapters = [];
    private UploadPropertiesSnapshot _uploadProperties;

    public ObservableCollection<PendingComicImage> PendingChapterImages { get; } = [];

    public ObservableCollection<PendingComicChapter> PendingBatchChapters { get; } = [];

    public bool HasPendingChapterImages => PendingChapterImages.Count > 0;

    public bool HasPendingBatchChapters =>
        PendingBatchChapters.Any(
            chapter => chapter.State != ComicChapterUploadState.Completed);

    public string BatchProgressText
    {
        get
        {
            var images = PendingBatchChapters
                .SelectMany(chapter => chapter.Images)
                .ToArray();
            var finishedImages = images.Count(image =>
                image.State is ComicImageUploadState.Uploaded
                    or ComicImageUploadState.Failed);
            var finishedChapters = PendingBatchChapters.Count(chapter =>
                chapter.State == ComicChapterUploadState.Completed);
            return $"已处理图片 {finishedImages}/{images.Length}，章节 {finishedChapters}/{PendingBatchChapters.Count}";
        }
    }

    public bool CanUploadPendingChapterImages =>
        HasPendingChapterImages
        && PendingChapterImages.Any(
            item => item.State == ComicImageUploadState.Pending)
        && !IsBusy
        && !IsUploading;

    public bool CanSaveChapter =>
        (IsCreatingChapter || SelectedChapter is not null)
        && !HasPendingChapterImages
        && !IsBusy
        && !IsUploading;

    public bool CanUploadBatchChapters
    {
        get
        {
            if (!HasPendingBatchChapters || IsBusy || IsUploading)
            {
                return false;
            }

            var incomplete = PendingBatchChapters
                .Where(chapter => chapter.State != ComicChapterUploadState.Completed)
                .ToArray();
            return incomplete.Any(chapter => chapter.Images.Any(
                       image => image.State == ComicImageUploadState.Pending))
                || incomplete.FirstOrDefault() is
                {
                    AllImagesUploaded: true,
                    State: ComicChapterUploadState.Ready
                        or ComicChapterUploadState.WaitingForPreviousChapter
                };
        }
    }

    public void StageChapterImages(IReadOnlyList<LocalImageSource> sources)
    {
        if (!IsLoaded
            || BookId is null
            || (SelectedChapter is null && !IsCreatingChapter)
            || IsBusy
            || IsUploading)
        {
            return;
        }

        var paths = PendingChapterImages
            .Select(item => NormalizePath(item.FilePath))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var additions = new List<LocalImageSource>();
        var ignoredCount = 0;
        foreach (var source in sources)
        {
            if (paths.Add(NormalizePath(source.FilePath)))
            {
                additions.Add(source);
            }
            else
            {
                ignoredCount++;
            }
        }

        foreach (var source in additions.OrderBy(
                     item => item.FileName,
                     NaturalNameComparer.Instance))
        {
            PendingChapterImages.Add(new PendingComicImage(
                source,
                PendingChapterImages.Count));
        }

        NoticeMessage = ignoredCount > 0
            ? $"已忽略 {ignoredCount} 个重复路径。"
            : null;
    }

    public async Task UploadPendingChapterImagesAsync(
        CancellationToken cancellationToken)
    {
        var targets = PendingChapterImages
            .Where(item => item.State == ComicImageUploadState.Pending)
            .ToArray();
        await UploadCurrentChapterItemsAsync(targets, cancellationToken);
    }

    public async Task ReplaceFailedChapterImageAsync(
        Guid imageId,
        string fileName,
        string filePath,
        CancellationToken cancellationToken)
    {
        var item = PendingChapterImages.FirstOrDefault(
            image => image.Id == imageId);
        if (item is null || !item.CanReplace || IsBusy || IsUploading)
        {
            return;
        }

        item.Replace(fileName, filePath);
        await UploadCurrentChapterItemsAsync([item], cancellationToken);
    }

    public void RemovePendingChapterImage(Guid imageId)
    {
        var item = PendingChapterImages.FirstOrDefault(
            image => image.Id == imageId);
        if (item is null || !item.CanRemove || IsUploading)
        {
            return;
        }

        PendingChapterImages.Remove(item);
        RenumberPositions(PendingChapterImages);
        CommitPendingChapterImagesIfComplete();
    }

    public void ClearPendingChapterImages()
    {
        if (IsUploading
            || PendingChapterImages.Any(item => !item.CanRemove))
        {
            return;
        }

        PendingChapterImages.Clear();
    }

    public void StageBatchChapters(
        IReadOnlyList<LocalComicChapterSelection> selections)
    {
        if (!IsLoaded || BookId is null || IsBusy || IsUploading)
        {
            return;
        }

        var folders = PendingBatchChapters
            .Select(chapter => NormalizePath(chapter.FolderPath))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var ignoredCount = 0;
        foreach (var selection in selections)
        {
            if (!folders.Add(NormalizePath(selection.FolderPath)))
            {
                ignoredCount++;
                continue;
            }

            var images = selection.Images
                .OrderBy(image => image.FileName, NaturalNameComparer.Instance)
                .Select((image, index) => new PendingComicImage(image, index))
                .ToArray();
            PendingBatchChapters.Add(new PendingComicChapter(
                Guid.NewGuid(),
                selection.FolderPath,
                selection.Title,
                images,
                selection.ErrorMessage));
        }

        SortCollection(PendingBatchChapters, chapter => chapter.Title);
        NoticeMessage = ignoredCount > 0
            ? $"已忽略 {ignoredCount} 个重复路径。"
            : null;
    }

    public Task UploadBatchChaptersAsync(CancellationToken cancellationToken)
    {
        if (!CanUploadBatchChapters)
        {
            return Task.CompletedTask;
        }

        return RunBatchOperationAsync(async (bookId, generation) =>
        {
            foreach (var chapter in PendingBatchChapters.Where(
                         item => item.State != ComicChapterUploadState.Completed))
            {
                var targets = chapter.Images
                    .Where(image => image.State == ComicImageUploadState.Pending)
                    .ToArray();
                if (targets.Length == 0)
                {
                    continue;
                }

                chapter.State = ComicChapterUploadState.UploadingImages;
                chapter.ErrorMessage = null;
                foreach (var image in targets)
                {
                    image.BeginUpload();
                }

                try
                {
                    var result = await _publishingService.UploadImagesAsync(
                        targets.Select(image => image.ToSource()).ToArray(),
                        cancellationToken);
                    if (!IsCurrentBookContext(generation, bookId))
                    {
                        return;
                    }

                    ApplyImageResults(targets, result);
                    UpdateBatchChapterAfterImageUpload(chapter);
                }
                catch (OperationCanceledException)
                {
                    RestoreBatchChapterImageUpload(chapter, targets);
                    throw;
                }
                catch (AppException exception)
                    when (exception.Kind == AppErrorKind.Unauthorized)
                {
                    RestoreBatchChapterImageUpload(chapter, targets);
                    throw;
                }
                catch (Exception exception)
                {
                    if (!IsCurrentBookContext(generation, bookId))
                    {
                        return;
                    }

                    var message = _errorMessageMapper.Map(exception);
                    foreach (var image in targets.Where(
                                 item => item.State == ComicImageUploadState.Uploading))
                    {
                        image.Fail(message);
                    }

                    UpdateBatchChapterAfterImageUpload(chapter);
                }
            }

            await CreateReadyBatchPrefixAsync(bookId, generation, cancellationToken);
        });
    }

    public Task ReplaceFailedBatchImageAsync(
        Guid imageId,
        string fileName,
        string filePath,
        CancellationToken cancellationToken) =>
        ResumeBatchImageAsync(imageId, fileName, filePath, cancellationToken);

    public Task RemoveBatchChapterAsync(
        Guid chapterId,
        CancellationToken cancellationToken)
    {
        var chapter = PendingBatchChapters.FirstOrDefault(
            item => item.Id == chapterId);
        if (chapter is null
            || !chapter.CanRemove
            || IsUploading
            || IsBusy)
        {
            return Task.CompletedTask;
        }

        var shouldResume = IsEarliestIncompleteBatchChapter(chapter.Id);
        return RunBatchOperationAsync(async (bookId, generation) =>
        {
            PendingBatchChapters.Remove(chapter);
            if (shouldResume)
            {
                await CreateReadyBatchPrefixAsync(
                    bookId,
                    generation,
                    cancellationToken);
            }
        });
    }

    public Task RemoveBatchImageAsync(
        Guid imageId,
        CancellationToken cancellationToken)
    {
        var chapter = PendingBatchChapters.FirstOrDefault(item =>
            item.Images.Any(image => image.Id == imageId));
        var image = chapter?.Images.FirstOrDefault(item => item.Id == imageId);
        if (chapter is null
            || image is null
            || !image.CanRemove
            || IsUploading
            || IsBusy)
        {
            return Task.CompletedTask;
        }

        var shouldResume = IsEarliestIncompleteBatchChapter(chapter.Id);
        return RunBatchOperationAsync(async (bookId, generation) =>
        {
            chapter.Images.Remove(image);
            RenumberPositions(chapter.Images);
            if (!chapter.HasValidImages)
            {
                chapter.ErrorMessage = "没有支持的图片。";
                chapter.State = ComicChapterUploadState.Failed;
            }
            else if (chapter.AllImagesUploaded)
            {
                UpdateBatchChapterAfterImageUpload(chapter);
                if (chapter.State == ComicChapterUploadState.Ready
                    && shouldResume)
                {
                    await CreateReadyBatchPrefixAsync(
                        bookId,
                        generation,
                        cancellationToken);
                }
                else if (chapter.State == ComicChapterUploadState.Ready)
                {
                    chapter.State = ComicChapterUploadState.WaitingForPreviousChapter;
                }
            }
        });
    }

    public Task RetryBatchChapterCreationAsync(
        Guid chapterId,
        CancellationToken cancellationToken)
    {
        var chapter = PendingBatchChapters.FirstOrDefault(
            item => item.Id == chapterId);
        return chapter is { CanRetryCreate: true }
            ? ResumeReadyBatchCreationAsync(chapter.Id, cancellationToken)
            : Task.CompletedTask;
    }

    private void ObserveUploadQueues()
    {
        PendingChapterImages.CollectionChanged += OnPendingChapterImagesChanged;
        PendingBatchChapters.CollectionChanged += OnPendingBatchChaptersChanged;
    }

    private void OnPendingChapterImagesChanged(
        object? sender,
        NotifyCollectionChangedEventArgs args)
    {
        UpdatePendingChapterImageSubscriptions(args);
        RefreshUploadProperties();
    }

    private void UpdatePendingChapterImageSubscriptions(
        NotifyCollectionChangedEventArgs args)
    {
        if (args.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var image in _observedPendingChapterImages.ToArray())
            {
                StopObservingPendingChapterImage(image);
            }

            foreach (var image in PendingChapterImages)
            {
                ObservePendingChapterImage(image);
            }

            return;
        }

        if (args.OldItems is not null)
        {
            foreach (PendingComicImage image in args.OldItems)
            {
                StopObservingPendingChapterImage(image);
            }
        }

        if (args.NewItems is not null)
        {
            foreach (PendingComicImage image in args.NewItems)
            {
                ObservePendingChapterImage(image);
            }
        }
    }

    private void ObservePendingChapterImage(PendingComicImage image)
    {
        if (_observedPendingChapterImages.Add(image))
        {
            image.PropertyChanged += OnPendingChapterImagePropertyChanged;
        }
    }

    private void StopObservingPendingChapterImage(PendingComicImage image)
    {
        if (_observedPendingChapterImages.Remove(image))
        {
            image.PropertyChanged -= OnPendingChapterImagePropertyChanged;
        }
    }

    private void OnPendingChapterImagePropertyChanged(
        object? sender,
        PropertyChangedEventArgs args)
    {
        if (string.IsNullOrEmpty(args.PropertyName)
            || args.PropertyName == nameof(PendingComicImage.State))
        {
            RefreshUploadProperties();
        }
    }

    private void OnPendingBatchChaptersChanged(
        object? sender,
        NotifyCollectionChangedEventArgs args)
    {
        UpdatePendingBatchChapterSubscriptions(args);
        RefreshUploadProperties();
    }

    private void UpdatePendingBatchChapterSubscriptions(
        NotifyCollectionChangedEventArgs args)
    {
        if (args.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var chapter in _observedPendingBatchChapters.ToArray())
            {
                StopObservingPendingBatchChapter(chapter);
            }

            foreach (var chapter in PendingBatchChapters)
            {
                ObservePendingBatchChapter(chapter);
            }

            return;
        }

        if (args.OldItems is not null)
        {
            foreach (PendingComicChapter chapter in args.OldItems)
            {
                StopObservingPendingBatchChapter(chapter);
            }
        }

        if (args.NewItems is not null)
        {
            foreach (PendingComicChapter chapter in args.NewItems)
            {
                ObservePendingBatchChapter(chapter);
            }
        }
    }

    private void ObservePendingBatchChapter(PendingComicChapter chapter)
    {
        if (_observedPendingBatchChapters.Add(chapter))
        {
            chapter.PropertyChanged += OnPendingBatchChapterPropertyChanged;
        }
    }

    private void StopObservingPendingBatchChapter(PendingComicChapter chapter)
    {
        if (_observedPendingBatchChapters.Remove(chapter))
        {
            chapter.PropertyChanged -= OnPendingBatchChapterPropertyChanged;
        }
    }

    private void OnPendingBatchChapterPropertyChanged(
        object? sender,
        PropertyChangedEventArgs args)
    {
        if (string.IsNullOrEmpty(args.PropertyName)
            || args.PropertyName == nameof(PendingComicChapter.State))
        {
            RefreshUploadProperties();
            return;
        }

        if (args.PropertyName is nameof(PendingComicChapter.ErrorMessage)
            or nameof(PendingComicChapter.HasValidImages)
            or nameof(PendingComicChapter.AllImagesUploaded))
        {
            RefreshUploadProperties();
        }
    }

    private void RefreshUploadProperties()
    {
        var previous = _uploadProperties;
        var current = CaptureUploadProperties();
        _uploadProperties = current;

        if (previous.HasPendingChapterImages != current.HasPendingChapterImages)
        {
            OnPropertyChanged(nameof(HasPendingChapterImages));
        }

        if (previous.HasPendingBatchChapters != current.HasPendingBatchChapters)
        {
            OnPropertyChanged(nameof(HasPendingBatchChapters));
        }

        if (previous.CanUploadPendingChapterImages != current.CanUploadPendingChapterImages)
        {
            OnPropertyChanged(nameof(CanUploadPendingChapterImages));
        }

        if (previous.CanUploadBatchChapters != current.CanUploadBatchChapters)
        {
            OnPropertyChanged(nameof(CanUploadBatchChapters));
        }

        if (previous.CanSaveChapter != current.CanSaveChapter)
        {
            OnPropertyChanged(nameof(CanSaveChapter));
        }

        if (!StringComparer.Ordinal.Equals(
                previous.BatchProgressText,
                current.BatchProgressText))
        {
            OnPropertyChanged(nameof(BatchProgressText));
        }

        if (previous.ChapterHasUnsavedChanges != current.ChapterHasUnsavedChanges)
        {
            OnPropertyChanged(nameof(ChapterHasUnsavedChanges));
        }

        if (previous.HasUnsavedChanges != current.HasUnsavedChanges)
        {
            OnPropertyChanged(nameof(HasUnsavedChanges));
        }
    }

    private UploadPropertiesSnapshot CaptureUploadProperties() => new(
        HasPendingChapterImages,
        HasPendingBatchChapters,
        CanUploadPendingChapterImages,
        CanUploadBatchChapters,
        CanSaveChapter,
        BatchProgressText,
        ChapterHasUnsavedChanges,
        HasUnsavedChanges);

    private async Task UploadCurrentChapterItemsAsync(
        IReadOnlyList<PendingComicImage> targets,
        CancellationToken cancellationToken)
    {
        if (targets.Count == 0
            || IsUploading
            || IsBusy
            || BookId is not long bookId
            || (SelectedChapter is null && !IsCreatingChapter))
        {
            return;
        }

        var bookGeneration = CurrentBookGeneration;
        var chapterGeneration = CurrentChapterGeneration;
        var selectedChapterId = SelectedChapter?.Id;
        var wasCreating = IsCreatingChapter;
        IsUploading = true;
        ErrorMessage = null;
        NoticeMessage = null;
        foreach (var item in targets)
        {
            item.BeginUpload();
        }

        try
        {
            var result = await _publishingService.UploadImagesAsync(
                targets.Select(item => item.ToSource()).ToArray(),
                cancellationToken);
            if (!IsCurrentChapterContext(
                    bookGeneration,
                    bookId,
                    chapterGeneration,
                    selectedChapterId,
                    wasCreating))
            {
                return;
            }

            ApplyImageResults(targets, result);
            CommitPendingChapterImagesIfComplete();
            var failedNames = targets
                .Where(item => item.State == ComicImageUploadState.Failed)
                .Select(item => item.FileName)
                .ToArray();
            if (failedNames.Length > 0)
            {
                NoticeMessage = $"以下图片上传失败：{string.Join("、", failedNames)}";
            }
        }
        catch (OperationCanceledException)
        {
            if (IsCurrentChapterContext(
                    bookGeneration,
                    bookId,
                    chapterGeneration,
                    selectedChapterId,
                    wasCreating))
            {
                foreach (var item in targets.Where(
                             item => item.State == ComicImageUploadState.Uploading))
                {
                    item.Replace(item.FileName, item.FilePath);
                }
            }

            throw;
        }
        catch (Exception exception)
        {
            var isContextCurrent = IsCurrentChapterContext(
                bookGeneration,
                bookId,
                chapterGeneration,
                selectedChapterId,
                wasCreating);
            HandleFailure(exception, isContextCurrent);
            if (isContextCurrent)
            {
                var message = ErrorMessage ?? "上传失败";
                foreach (var item in targets.Where(
                             item => item.State == ComicImageUploadState.Uploading))
                {
                    item.Fail(message);
                }
            }
        }
        finally
        {
            IsUploading = false;
        }
    }

    private Task ResumeBatchImageAsync(
        Guid imageId,
        string fileName,
        string filePath,
        CancellationToken cancellationToken)
    {
        var chapter = PendingBatchChapters.FirstOrDefault(item =>
            item.Images.Any(image => image.Id == imageId));
        var image = chapter?.Images.FirstOrDefault(item => item.Id == imageId);
        if (chapter is null
            || image is null
            || !image.CanReplace
            || IsUploading
            || IsBusy)
        {
            return Task.CompletedTask;
        }

        return RunBatchOperationAsync(async (bookId, generation) =>
        {
            image.Replace(fileName, filePath);
            image.BeginUpload();
            chapter.State = ComicChapterUploadState.UploadingImages;
            chapter.ErrorMessage = null;
            try
            {
                var result = await _publishingService.UploadImagesAsync(
                    [image.ToSource()],
                    cancellationToken);
                if (!IsCurrentBookContext(generation, bookId))
                {
                    return;
                }

                ApplyImageResults([image], result);
                UpdateBatchChapterAfterImageUpload(chapter);
                if (chapter.State == ComicChapterUploadState.Ready)
                {
                    await CreateReadyBatchPrefixAsync(
                        bookId,
                        generation,
                        cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                RestoreBatchChapterImageUpload(chapter, [image]);
                throw;
            }
            catch (AppException exception)
                when (exception.Kind == AppErrorKind.Unauthorized)
            {
                if (image.State == ComicImageUploadState.Uploading)
                {
                    RestoreBatchChapterImageUpload(chapter, [image]);
                }

                throw;
            }
            catch (Exception exception)
            {
                if (IsCurrentBookContext(generation, bookId))
                {
                    var message = _errorMessageMapper.Map(exception);
                    image.Fail(message);
                    UpdateBatchChapterAfterImageUpload(chapter);
                }
            }
        });
    }

    private Task ResumeReadyBatchCreationAsync(
        Guid retryChapterId,
        CancellationToken cancellationToken)
    {
        return RunBatchOperationAsync((bookId, generation) =>
            CreateReadyBatchPrefixAsync(
                bookId,
                generation,
                cancellationToken,
                retryChapterId));
    }

    private async Task RunBatchOperationAsync(
        Func<long, long, Task> operation)
    {
        if (IsUploading || IsBusy || BookId is not long bookId)
        {
            return;
        }

        var generation = CurrentBookGeneration;
        IsUploading = true;
        ErrorMessage = null;
        NoticeMessage = null;
        try
        {
            await operation(bookId, generation);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            HandleFailure(exception, IsCurrentBookContext(generation, bookId));
        }
        finally
        {
            IsUploading = false;
        }
    }

    private async Task CreateReadyBatchPrefixAsync(
        long bookId,
        long generation,
        CancellationToken cancellationToken,
        Guid? retryChapterId = null)
    {
        foreach (var chapter in PendingBatchChapters.Where(
                     item => item.State != ComicChapterUploadState.Completed))
        {
            if (!IsCurrentBookContext(generation, bookId))
            {
                return;
            }

            if (chapter.HasSelectionError)
            {
                chapter.State = ComicChapterUploadState.Failed;
                MarkLaterUploadedChaptersWaiting(chapter);
                return;
            }

            if (chapter.State == ComicChapterUploadState.Failed
                && chapter.AllImagesUploaded
                && chapter.Id != retryChapterId)
            {
                MarkLaterUploadedChaptersWaiting(chapter);
                return;
            }

            if (!chapter.HasValidImages || !chapter.AllImagesUploaded)
            {
                if (!chapter.HasValidImages)
                {
                    chapter.ErrorMessage = "没有支持的图片。";
                    chapter.State = ComicChapterUploadState.Failed;
                }

                MarkLaterUploadedChaptersWaiting(chapter);
                return;
            }

            chapter.State = ComicChapterUploadState.CreatingChapter;
            chapter.ErrorMessage = null;
            if (!IsCurrentBookContext(generation, bookId))
            {
                return;
            }

            var draft = new ComicChapterDraft(
                0,
                chapter.Title,
                chapter.Images
                    .OrderBy(image => image.Position)
                    .Select(image => image.UploadedUrl!)
                    .ToArray());
            try
            {
                var result = await _publishingService.CreateChapterAsync(
                    bookId,
                    Chapters.Count + 1,
                    draft,
                    cancellationToken);
                if (!IsCurrentBookContext(generation, bookId))
                {
                    return;
                }

                ApplyBatchCreatedChapter(result, draft);
                chapter.State = ComicChapterUploadState.Completed;
            }
            catch (OperationCanceledException)
            {
                chapter.State = ComicChapterUploadState.Ready;
                throw;
            }
            catch (AppException exception)
                when (exception.Kind == AppErrorKind.Unauthorized)
            {
                chapter.ErrorMessage = _errorMessageMapper.Map(exception);
                chapter.State = ComicChapterUploadState.Failed;
                MarkLaterUploadedChaptersWaiting(chapter);
                throw;
            }
            catch (Exception exception)
            {
                chapter.ErrorMessage = _errorMessageMapper.Map(exception);
                chapter.State = ComicChapterUploadState.Failed;
                MarkLaterUploadedChaptersWaiting(chapter);
                return;
            }
        }

        if (!HasPendingBatchChapters)
        {
            NoticeMessage = "批量章节上传完成。";
        }
    }

    private void ApplyBatchCreatedChapter(
        CreateChapterResult result,
        ComicChapterDraft draft)
    {
        var selectedId = SelectedChapter?.Id;
        var chapters = result.Chapters.OrderBy(chapter => chapter.SortNum).ToList();
        if (chapters.All(chapter => chapter.Id != result.NewChapterId))
        {
            chapters.Add(new ComicChapterSummary(
                result.NewChapterId,
                Chapters.Count + 1,
                draft.Title));
        }

        WithDirtySuppressed(() =>
        {
            ReplaceCollection(Chapters, chapters.OrderBy(chapter => chapter.SortNum));
            RenumberChapters();
            if (selectedId is long id)
            {
                var selected = Chapters.FirstOrDefault(chapter => chapter.Id == id);
                if (selected is not null && !ReferenceEquals(_selectedChapter, selected))
                {
                    _selectedChapter = selected;
                    OnPropertyChanged(nameof(SelectedChapter));
                }
            }

            NewChapterSortNum = Chapters.Count + 1;
        });
    }

    private static void RestorePendingImages(
        IEnumerable<PendingComicImage> targets)
    {
        foreach (var image in targets.Where(
                     item => item.State == ComicImageUploadState.Uploading))
        {
            image.Replace(image.FileName, image.FilePath);
        }
    }

    private static void RestoreBatchChapterImageUpload(
        PendingComicChapter chapter,
        IEnumerable<PendingComicImage> targets)
    {
        RestorePendingImages(targets);
        UpdateBatchChapterAfterImageUpload(chapter);
    }

    private static void UpdateBatchChapterAfterImageUpload(
        PendingComicChapter chapter)
    {
        var failedNames = chapter.Images
            .Where(image => image.State == ComicImageUploadState.Failed)
            .Select(image => image.FileName)
            .ToArray();
        if (failedNames.Length > 0)
        {
            chapter.ErrorMessage =
                $"以下图片上传失败：{string.Join("、", failedNames)}";
            chapter.State = ComicChapterUploadState.Failed;
            return;
        }

        if (chapter.HasSelectionError)
        {
            chapter.ErrorMessage = null;
            chapter.State = ComicChapterUploadState.Failed;
            return;
        }

        if (!chapter.HasValidImages)
        {
            chapter.ErrorMessage = "没有支持的图片。";
            chapter.State = ComicChapterUploadState.Failed;
            return;
        }

        chapter.ErrorMessage = null;
        if (chapter.AllImagesUploaded)
        {
            chapter.State = ComicChapterUploadState.Ready;
            return;
        }

        chapter.State = ComicChapterUploadState.Ready;
    }

    private void MarkLaterUploadedChaptersWaiting(PendingComicChapter blocker)
    {
        var blockerFound = false;
        foreach (var chapter in PendingBatchChapters)
        {
            if (!blockerFound)
            {
                blockerFound = chapter.Id == blocker.Id;
                continue;
            }

            if (chapter.State != ComicChapterUploadState.Completed
                && chapter.AllImagesUploaded)
            {
                chapter.State = ComicChapterUploadState.WaitingForPreviousChapter;
            }
        }
    }

    private bool IsEarliestIncompleteBatchChapter(Guid chapterId) =>
        PendingBatchChapters.FirstOrDefault(
            chapter => chapter.State != ComicChapterUploadState.Completed)?.Id
        == chapterId;

    private static void ApplyImageResults(
        IReadOnlyList<PendingComicImage> targets,
        ImageUploadBatchResult result)
    {
        var byId = targets.ToDictionary(item => item.Id);
        foreach (var success in result.Successes)
        {
            if (byId.TryGetValue(success.SourceId, out var item))
            {
                item.Complete(success.Url);
            }
        }

        foreach (var failure in result.Failures)
        {
            if (byId.TryGetValue(failure.SourceId, out var item))
            {
                item.Fail(failure.Message);
            }
        }

        foreach (var item in targets.Where(
                     item => item.State == ComicImageUploadState.Uploading))
        {
            item.Fail("未返回上传结果");
        }
    }

    private void CommitPendingChapterImagesIfComplete()
    {
        if (PendingChapterImages.Count == 0
            || PendingChapterImages.Any(
                item => item.State != ComicImageUploadState.Uploaded))
        {
            return;
        }

        foreach (var item in PendingChapterImages.OrderBy(item => item.Position))
        {
            ChapterImages.Add(item.UploadedUrl!);
        }

        PendingChapterImages.Clear();
    }

    private static string NormalizePath(string path) =>
        Path.GetFullPath(path).TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);

    private static void RenumberPositions(
        ObservableCollection<PendingComicImage> images)
    {
        for (var index = 0; index < images.Count; index++)
        {
            images[index].Position = index;
        }
    }

    private static void SortCollection<T>(
        ObservableCollection<T> collection,
        Func<T, string> key)
    {
        var ordered = collection
            .OrderBy(key, NaturalNameComparer.Instance)
            .ToArray();
        collection.Clear();
        foreach (var item in ordered)
        {
            collection.Add(item);
        }
    }

    private void ClearPendingChapterImagesCore()
    {
        PendingChapterImages.Clear();
    }

    private void ClearPendingBatchChaptersCore()
    {
        PendingBatchChapters.Clear();
    }

    private readonly record struct UploadPropertiesSnapshot(
        bool HasPendingChapterImages,
        bool HasPendingBatchChapters,
        bool CanUploadPendingChapterImages,
        bool CanUploadBatchChapters,
        bool CanSaveChapter,
        string BatchProgressText,
        bool ChapterHasUnsavedChanges,
        bool HasUnsavedChanges);
}
