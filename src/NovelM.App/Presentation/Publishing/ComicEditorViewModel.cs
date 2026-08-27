using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.ComponentModel;
using NovelM_App.Application.Abstractions;
using NovelM_App.Domain.Auth;
using NovelM_App.Domain.Errors;
using NovelM_App.Domain.Publishing;
using NovelM_App.Presentation.Common;

namespace NovelM_App.Presentation.Publishing;

public sealed partial class ComicEditorViewModel : ObservableObject
{
    private const string ChapterDiscardNotice = "当前章节有未保存的更改，请先保存或确认放弃。";

    private readonly IComicPublishingService _publishingService;
    private readonly ErrorMessageMapper _errorMessageMapper;
    private long? _bookId;
    private bool _isLoaded;
    private bool _isBusy;
    private bool _isUploading;
    private string? _errorMessage;
    private string? _noticeMessage;
    private string _cover = string.Empty;
    private string _title = string.Empty;
    private string _author = string.Empty;
    private string _introduction = string.Empty;
    private int _categoryId;
    private int _level;
    private int _interiorLevel;
    private bool _downloadAllowed;
    private long? _subjectId;
    private long? _seriesId;
    private string _seriesName = string.Empty;
    private string _seriesNameCn = string.Empty;
    private int _maximumInteriorLevel;
    private ComicChapterSummary? _selectedChapter;
    private string _chapterTitle = string.Empty;
    private bool _isCreatingChapter;
    private int _newChapterSortNum = 1;
    private bool _infoHasUnsavedChanges;
    private bool _settingsHasUnsavedChanges;
    private bool _chapterHasUnsavedChanges;
    private bool _suppressDirty;
    private long _bookGeneration;
    private long _chapterGeneration;

    public ComicEditorViewModel(
        IComicPublishingService publishingService,
        ErrorMessageMapper errorMessageMapper)
    {
        _publishingService = publishingService;
        _errorMessageMapper = errorMessageMapper;
        ObserveUploadQueues();
        Tags.CollectionChanged += (_, _) => MarkSettingsDirty();
        ChapterImages.CollectionChanged += (_, _) => MarkChapterDirty();
    }

    public event EventHandler? SessionExpired;

    public long? BookId
    {
        get => _bookId;
        private set => SetProperty(ref _bookId, value);
    }

    public bool IsLoaded
    {
        get => _isLoaded;
        private set => SetProperty(ref _isLoaded, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                NotifyUploadAvailabilityChanged();
            }
        }
    }

    public bool IsUploading
    {
        get => _isUploading;
        private set
        {
            if (SetProperty(ref _isUploading, value))
            {
                NotifyUploadAvailabilityChanged();
            }
        }
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetProperty(ref _errorMessage, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public string? NoticeMessage
    {
        get => _noticeMessage;
        private set => SetProperty(ref _noticeMessage, value);
    }

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public string Cover
    {
        get => _cover;
        set
        {
            if (SetProperty(ref _cover, value))
            {
                MarkInfoDirty();
            }
        }
    }

    public string Title
    {
        get => _title;
        set
        {
            if (SetProperty(ref _title, value))
            {
                MarkInfoDirty();
            }
        }
    }

    public string Author
    {
        get => _author;
        set
        {
            if (SetProperty(ref _author, value))
            {
                MarkInfoDirty();
            }
        }
    }

    public string Introduction
    {
        get => _introduction;
        set
        {
            if (SetProperty(ref _introduction, value))
            {
                MarkInfoDirty();
            }
        }
    }

    public int CategoryId
    {
        get => _categoryId;
        set
        {
            if (SetProperty(ref _categoryId, value))
            {
                MarkInfoDirty();
            }
        }
    }

    public ObservableCollection<ComicCategory> Categories { get; } = [];

    public int MinimumLevel => 0;

    public int MaximumLevel => 6;

    public int Level
    {
        get => _level;
        set
        {
            if (SetProperty(ref _level, value))
            {
                MarkSettingsDirty();
            }
        }
    }

    public int InteriorLevel
    {
        get => _interiorLevel;
        set
        {
            if (SetProperty(ref _interiorLevel, value))
            {
                MarkSettingsDirty();
            }
        }
    }

    public bool DownloadAllowed
    {
        get => _downloadAllowed;
        set
        {
            if (SetProperty(ref _downloadAllowed, value))
            {
                MarkSettingsDirty();
            }
        }
    }

    public long? SubjectId
    {
        get => _subjectId;
        set
        {
            if (SetProperty(ref _subjectId, value))
            {
                MarkSettingsDirty();
            }
        }
    }

    public long? SeriesId
    {
        get => _seriesId;
        set
        {
            if (SetProperty(ref _seriesId, value))
            {
                MarkSettingsDirty();
            }
        }
    }

    public string SeriesName
    {
        get => _seriesName;
        set
        {
            if (SetProperty(ref _seriesName, value))
            {
                MarkSettingsDirty();
            }
        }
    }

    public string SeriesNameCn
    {
        get => _seriesNameCn;
        set
        {
            if (SetProperty(ref _seriesNameCn, value))
            {
                MarkSettingsDirty();
            }
        }
    }

    public ObservableCollection<string> Tags { get; } = [];

    public int MaximumInteriorLevel
    {
        get => _maximumInteriorLevel;
        private set
        {
            if (SetProperty(ref _maximumInteriorLevel, Math.Max(0, value)))
            {
                OnPropertyChanged(nameof(IsInteriorLevelVisible));
            }
        }
    }

    public bool IsInteriorLevelVisible => MaximumInteriorLevel > 0;

    public ObservableCollection<ComicChapterSummary> Chapters { get; } = [];

    public ComicChapterSummary? SelectedChapter
    {
        get => _selectedChapter;
        private set
        {
            if (SetProperty(ref _selectedChapter, value))
            {
                OnPropertyChanged(nameof(CanSaveChapter));
            }
        }
    }

    public string ChapterTitle
    {
        get => _chapterTitle;
        set
        {
            if (SetProperty(ref _chapterTitle, value))
            {
                MarkChapterDirty();
            }
        }
    }

    public ObservableCollection<string> ChapterImages { get; } = [];

    public bool IsCreatingChapter
    {
        get => _isCreatingChapter;
        private set
        {
            if (SetProperty(ref _isCreatingChapter, value))
            {
                OnPropertyChanged(nameof(CanSaveChapter));
            }
        }
    }

    public int NewChapterSortNum
    {
        get => _newChapterSortNum;
        private set => SetProperty(ref _newChapterSortNum, value);
    }

    public bool InfoHasUnsavedChanges
    {
        get => _infoHasUnsavedChanges;
        private set => SetDirtyProperty(ref _infoHasUnsavedChanges, value);
    }

    public bool SettingsHasUnsavedChanges
    {
        get => _settingsHasUnsavedChanges;
        private set => SetDirtyProperty(ref _settingsHasUnsavedChanges, value);
    }

    public bool ChapterHasUnsavedChanges
    {
        get => _chapterHasUnsavedChanges;
        private set => SetDirtyProperty(ref _chapterHasUnsavedChanges, value);
    }

    public bool HasUnsavedChanges =>
        InfoHasUnsavedChanges
        || SettingsHasUnsavedChanges
        || ChapterHasUnsavedChanges
        || HasPendingChapterImages
        || HasPendingBatchChapters;

    public async Task LoadAsync(
        long bookId,
        UserProfile user,
        CancellationToken cancellationToken)
    {
        if (IsBusy)
        {
            return;
        }

        var generation = CurrentBookGeneration;
        StartOperation();
        try
        {
            var details = await _publishingService.GetEditDetailsAsync(
                bookId,
                cancellationToken);
            if (generation != CurrentBookGeneration)
            {
                return;
            }

            ApplyDetails(bookId, user, details);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            HandleFailure(exception, generation == CurrentBookGeneration);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void Clear()
    {
        AdvanceBookContext();
        ClearPendingBatchChaptersCore();
        WithDirtySuppressed(() =>
        {
            BookId = null;
            IsLoaded = false;
            Cover = string.Empty;
            Title = string.Empty;
            Author = string.Empty;
            Introduction = string.Empty;
            CategoryId = 0;
            Categories.Clear();
            Level = 0;
            InteriorLevel = 0;
            DownloadAllowed = false;
            SubjectId = null;
            SeriesId = null;
            SeriesName = string.Empty;
            SeriesNameCn = string.Empty;
            Tags.Clear();
            MaximumInteriorLevel = 0;
            Chapters.Clear();
            ClearChapterDraft();
            NewChapterSortNum = 1;
        });
        SetAllDirty(false);
        ErrorMessage = null;
        NoticeMessage = null;
    }

    public async Task SaveInfoAsync(CancellationToken cancellationToken)
    {
        if (BookId is not long bookId || IsBusy || IsUploading)
        {
            return;
        }

        var draft = new ComicInfoDraft(Cover, Title, Author, Introduction, CategoryId);
        var generation = CurrentBookGeneration;
        StartOperation();
        try
        {
            await _publishingService.UpdateInfoAsync(bookId, draft, cancellationToken);
            if (!IsCurrentBookContext(generation, bookId))
            {
                return;
            }

            InfoHasUnsavedChanges = !CurrentInfoMatches(draft);
            NoticeMessage = "漫画信息已保存。";
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
            IsBusy = false;
        }
    }

    public async Task SaveSettingsAsync(CancellationToken cancellationToken)
    {
        if (BookId is not long bookId || IsBusy || IsUploading)
        {
            return;
        }

        var draft = new ComicSettingsDraft(
            Level,
            InteriorLevel,
            DownloadAllowed,
            SubjectId,
            SeriesId,
            SeriesName,
            SeriesNameCn,
            Tags.ToArray());
        var generation = CurrentBookGeneration;
        StartOperation();
        try
        {
            await _publishingService.UpdateSettingsAsync(
                bookId,
                draft,
                MaximumInteriorLevel,
                cancellationToken);
            if (!IsCurrentBookContext(generation, bookId))
            {
                return;
            }

            SettingsHasUnsavedChanges = !CurrentSettingsMatch(draft);
            NoticeMessage = "漫画设置已保存。";
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
            IsBusy = false;
        }
    }

    public async Task<bool> SelectChapterAsync(
        ComicChapterSummary chapter,
        bool discardChapterChanges,
        CancellationToken cancellationToken)
    {
        if (ChapterHasUnsavedChanges && !discardChapterChanges)
        {
            NoticeMessage = ChapterDiscardNotice;
            return false;
        }

        if (BookId is not long bookId || IsBusy)
        {
            return false;
        }

        var bookGeneration = CurrentBookGeneration;
        var chapterGeneration = CurrentChapterGeneration;
        var contextChapterId = SelectedChapter?.Id;
        var contextWasCreating = IsCreatingChapter;
        StartOperation();
        try
        {
            var draft = await _publishingService.GetChapterAsync(
                bookId,
                chapter.Id,
                cancellationToken);
            if (!IsCurrentChapterContext(
                    bookGeneration,
                    bookId,
                    chapterGeneration,
                    contextChapterId,
                    contextWasCreating))
            {
                return false;
            }

            ApplyChapterDraft(chapter, draft);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            HandleFailure(
                exception,
                IsCurrentChapterContext(
                    bookGeneration,
                    bookId,
                    chapterGeneration,
                    contextChapterId,
                    contextWasCreating));
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public bool BeginNewChapter(bool discardChapterChanges)
    {
        if (ChapterHasUnsavedChanges && !discardChapterChanges)
        {
            NoticeMessage = ChapterDiscardNotice;
            return false;
        }

        if (BookId is null || IsBusy)
        {
            return false;
        }

        AdvanceChapterContext();
        WithDirtySuppressed(() =>
        {
            ClearChapterDraft();
            IsCreatingChapter = true;
            NewChapterSortNum = Chapters.Count + 1;
        });
        ChapterHasUnsavedChanges = false;
        ErrorMessage = null;
        NoticeMessage = null;
        return true;
    }

    public async Task SaveChapterAsync(CancellationToken cancellationToken)
    {
        if (BookId is not long bookId
            || IsBusy
            || IsUploading
            || HasPendingChapterImages)
        {
            return;
        }

        var selected = SelectedChapter;
        if (!IsCreatingChapter && selected is null)
        {
            return;
        }

        var chapterId = IsCreatingChapter ? 0 : selected!.Id;
        var draft = new ComicChapterDraft(chapterId, ChapterTitle, ChapterImages.ToArray());
        var wasCreating = IsCreatingChapter;
        var bookGeneration = CurrentBookGeneration;
        var chapterGeneration = CurrentChapterGeneration;
        StartOperation();
        try
        {
            var draftUnchanged = false;
            if (wasCreating)
            {
                var result = await _publishingService.CreateChapterAsync(
                    bookId,
                    NewChapterSortNum,
                    draft,
                    cancellationToken);
                if (!IsCurrentChapterContext(
                        bookGeneration,
                        bookId,
                        chapterGeneration,
                        selectedChapterId: null,
                        wasCreating: true))
                {
                    return;
                }

                draftUnchanged = CurrentNewChapterMatches(draft);
                ApplyCreatedChapter(result, draft, preserveCurrentDraft: !draftUnchanged);
            }
            else
            {
                await _publishingService.UpdateChapterAsync(
                    selected!.Id,
                    draft,
                    cancellationToken);
                if (!IsCurrentChapterContext(
                        bookGeneration,
                        bookId,
                        chapterGeneration,
                        selected.Id,
                        wasCreating: false))
                {
                    return;
                }

                draftUnchanged = CurrentChapterMatches(selected.Id, draft);
                ApplyUpdatedChapter(selected, draft);
            }

            ChapterHasUnsavedChanges = !draftUnchanged;
            NoticeMessage = "章节已保存。";
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            HandleFailure(
                exception,
                IsCurrentChapterContext(
                    bookGeneration,
                    bookId,
                    chapterGeneration,
                    selected?.Id,
                    wasCreating));
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task DeleteSelectedChapterAsync(CancellationToken cancellationToken)
    {
        if (BookId is not long bookId || SelectedChapter is not { } selected || IsBusy)
        {
            return;
        }

        var originalIndex = FindChapterIndex(selected.Id);
        if (originalIndex < 0)
        {
            return;
        }

        var bookGeneration = CurrentBookGeneration;
        var chapterGeneration = CurrentChapterGeneration;
        StartOperation();
        try
        {
            await _publishingService.DeleteChapterAsync(
                bookId,
                selected.SortNum,
                cancellationToken);
            if (!IsCurrentChapterContext(
                    bookGeneration,
                    bookId,
                    chapterGeneration,
                    selected.Id,
                    wasCreating: false))
            {
                return;
            }

            Chapters.RemoveAt(originalIndex);
            RenumberChapters();
            AdvanceChapterContext();
            WithDirtySuppressed(() =>
            {
                ClearChapterDraft();
                NewChapterSortNum = Chapters.Count + 1;
            });
            ChapterHasUnsavedChanges = false;

            if (Chapters.Count == 0)
            {
                return;
            }

            var adjacent = Chapters[Math.Min(originalIndex, Chapters.Count - 1)];
            IsBusy = false;
            await SelectChapterAsync(adjacent, discardChapterChanges: true, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            HandleFailure(
                exception,
                IsCurrentBookContext(bookGeneration, bookId));
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task MoveSelectedChapterAsync(
        int offset,
        CancellationToken cancellationToken)
    {
        if (offset is not (-1 or 1)
            || BookId is not long bookId
            || SelectedChapter is not { } selected
            || IsBusy)
        {
            return;
        }

        var oldIndex = FindChapterIndex(selected.Id);
        var newIndex = oldIndex + offset;
        if (oldIndex < 0 || newIndex < 0 || newIndex >= Chapters.Count)
        {
            return;
        }

        var oldSortNum = selected.SortNum;
        var newSortNum = Chapters[newIndex].SortNum;
        var bookGeneration = CurrentBookGeneration;
        var chapterGeneration = CurrentChapterGeneration;
        StartOperation();
        try
        {
            await _publishingService.ReorderChapterAsync(
                bookId,
                oldSortNum,
                newSortNum,
                cancellationToken);
            if (!IsCurrentChapterContext(
                    bookGeneration,
                    bookId,
                    chapterGeneration,
                    selected.Id,
                    wasCreating: false))
            {
                return;
            }

            Chapters.Move(oldIndex, newIndex);
            RenumberChapters();
            SelectedChapter = Chapters[newIndex];
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            HandleFailure(
                exception,
                IsCurrentChapterContext(
                    bookGeneration,
                    bookId,
                    chapterGeneration,
                    selected.Id,
                    wasCreating: false));
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task UploadCoverAsync(
        LocalImageSource source,
        CancellationToken cancellationToken)
    {
        if (!IsLoaded
            || BookId is not long bookId
            || IsUploading
            || IsBusy)
        {
            return;
        }

        var generation = CurrentBookGeneration;
        IsUploading = true;
        ErrorMessage = null;
        NoticeMessage = null;
        try
        {
            var result = await _publishingService.UploadImagesAsync(
                [source],
                cancellationToken);
            if (!IsCurrentBookContext(generation, bookId))
            {
                return;
            }

            var success = result.Successes.FirstOrDefault(
                item => item.SourceId == source.Id);
            if (success is not null)
            {
                Cover = success.Url;
            }

            var failure = result.Failures.FirstOrDefault(
                item => item.SourceId == source.Id);
            if (failure is not null)
            {
                NoticeMessage = $"封面上传失败：{failure.Message}";
            }
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
            NotifyUploadAvailabilityChanged();
        }
    }

    public void RemoveChapterImageAt(int index)
    {
        if (index >= 0 && index < ChapterImages.Count)
        {
            ChapterImages.RemoveAt(index);
        }
    }

    public void MoveChapterImage(int oldIndex, int newIndex)
    {
        if (oldIndex >= 0
            && oldIndex < ChapterImages.Count
            && newIndex >= 0
            && newIndex < ChapterImages.Count
            && oldIndex != newIndex)
        {
            ChapterImages.Move(oldIndex, newIndex);
        }
    }

    public void ClearChapterImages()
    {
        if (ChapterImages.Count > 0)
        {
            ChapterImages.Clear();
        }
    }

    private void ApplyDetails(long bookId, UserProfile user, ComicEditDetails details)
    {
        AdvanceBookContext();
        ClearPendingBatchChaptersCore();
        WithDirtySuppressed(() =>
        {
            BookId = bookId;
            Cover = details.Cover;
            Title = details.Title;
            Author = details.Author;
            Introduction = details.Introduction;
            CategoryId = details.CategoryId;
            ReplaceCollection(Categories, details.Categories);
            Level = details.Level;
            InteriorLevel = details.InteriorLevel;
            DownloadAllowed = details.DownloadAllowed;
            SubjectId = details.SubjectId;
            SeriesId = details.SeriesId;
            SeriesName = details.SeriesName;
            SeriesNameCn = details.SeriesNameCn;
            ReplaceCollection(Tags, details.Tags);
            MaximumInteriorLevel = user.InteriorLevel;
            ReplaceCollection(Chapters, details.Chapters.OrderBy(x => x.SortNum));
            ClearChapterDraft();
            NewChapterSortNum = Chapters.Count + 1;
            IsLoaded = true;
        });
        SetAllDirty(false);
    }

    private void ApplyChapterDraft(
        ComicChapterSummary chapter,
        ComicChapterDraft draft)
    {
        AdvanceChapterContext();
        ClearPendingChapterImagesCore();
        WithDirtySuppressed(() =>
        {
            SelectedChapter = chapter;
            ChapterTitle = draft.Title;
            ReplaceCollection(ChapterImages, draft.Images);
            IsCreatingChapter = false;
            NewChapterSortNum = Chapters.Count + 1;
        });
        ChapterHasUnsavedChanges = false;
    }

    private void ApplyCreatedChapter(
        CreateChapterResult result,
        ComicChapterDraft draft,
        bool preserveCurrentDraft)
    {
        var chapters = result.Chapters.OrderBy(x => x.SortNum).ToList();
        var created = chapters.FirstOrDefault(x => x.Id == result.NewChapterId);
        if (created is null)
        {
            created = new ComicChapterSummary(
                result.NewChapterId,
                NewChapterSortNum,
                draft.Title);
            chapters.Add(created);
        }

        AdvanceChapterContext();
        WithDirtySuppressed(() =>
        {
            ReplaceCollection(Chapters, chapters.OrderBy(x => x.SortNum));
            RenumberChapters();
            SelectedChapter = Chapters.First(x => x.Id == result.NewChapterId);
            if (!preserveCurrentDraft)
            {
                ChapterTitle = draft.Title;
                ReplaceCollection(ChapterImages, draft.Images);
            }

            IsCreatingChapter = false;
            NewChapterSortNum = Chapters.Count + 1;
        });
    }

    private void ApplyUpdatedChapter(
        ComicChapterSummary selected,
        ComicChapterDraft draft)
    {
        var index = FindChapterIndex(selected.Id);
        if (index < 0)
        {
            return;
        }

        var updated = selected with { Title = draft.Title };
        Chapters[index] = updated;
        SelectedChapter = updated;
    }

    private bool CurrentInfoMatches(ComicInfoDraft draft)
    {
        return Cover == draft.Cover
            && Title == draft.Title
            && Author == draft.Author
            && Introduction == draft.Introduction
            && CategoryId == draft.CategoryId;
    }

    private bool CurrentSettingsMatch(ComicSettingsDraft draft)
    {
        return Level == draft.Level
            && InteriorLevel == draft.InteriorLevel
            && DownloadAllowed == draft.DownloadAllowed
            && SubjectId == draft.SubjectId
            && SeriesId == draft.SeriesId
            && SeriesName == draft.SeriesName
            && SeriesNameCn == draft.SeriesNameCn
            && Tags.SequenceEqual(draft.Tags);
    }

    private bool CurrentNewChapterMatches(ComicChapterDraft draft)
    {
        return IsCreatingChapter
            && SelectedChapter is null
            && ChapterTitle == draft.Title
            && ChapterImages.SequenceEqual(draft.Images);
    }

    private bool CurrentChapterMatches(long chapterId, ComicChapterDraft draft)
    {
        return !IsCreatingChapter
            && SelectedChapter?.Id == chapterId
            && ChapterTitle == draft.Title
            && ChapterImages.SequenceEqual(draft.Images);
    }

    private void ClearChapterDraft()
    {
        ClearPendingChapterImagesCore();
        SelectedChapter = null;
        ChapterTitle = string.Empty;
        ChapterImages.Clear();
        IsCreatingChapter = false;
    }

    private void RenumberChapters()
    {
        for (var index = 0; index < Chapters.Count; index++)
        {
            var expectedSort = index + 1;
            if (Chapters[index].SortNum != expectedSort)
            {
                Chapters[index] = Chapters[index] with { SortNum = expectedSort };
            }
        }
    }

    private int FindChapterIndex(long chapterId)
    {
        for (var index = 0; index < Chapters.Count; index++)
        {
            if (Chapters[index].Id == chapterId)
            {
                return index;
            }
        }

        return -1;
    }

    private void StartOperation()
    {
        IsBusy = true;
        ErrorMessage = null;
        NoticeMessage = null;
    }

    private void HandleFailure(Exception exception, bool isContextCurrent)
    {
        if (exception is AppException { Kind: AppErrorKind.Unauthorized })
        {
            if (isContextCurrent)
            {
                ErrorMessage = _errorMessageMapper.Map(exception);
            }

            SessionExpired?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (isContextCurrent)
        {
            ErrorMessage = _errorMessageMapper.Map(exception);
        }
    }

    private long CurrentBookGeneration => Volatile.Read(ref _bookGeneration);

    private long CurrentChapterGeneration => Volatile.Read(ref _chapterGeneration);

    private void AdvanceBookContext()
    {
        Interlocked.Increment(ref _bookGeneration);
        Interlocked.Increment(ref _chapterGeneration);
    }

    private void AdvanceChapterContext()
    {
        Interlocked.Increment(ref _chapterGeneration);
    }

    private bool IsCurrentBookContext(long generation, long bookId)
    {
        return generation == CurrentBookGeneration
            && IsLoaded
            && BookId == bookId;
    }

    private bool IsCurrentChapterContext(
        long bookGeneration,
        long bookId,
        long chapterGeneration,
        long? selectedChapterId,
        bool wasCreating)
    {
        return IsCurrentBookContext(bookGeneration, bookId)
            && chapterGeneration == CurrentChapterGeneration
            && IsCreatingChapter == wasCreating
            && SelectedChapter?.Id == selectedChapterId;
    }

    private void MarkInfoDirty()
    {
        if (!_suppressDirty)
        {
            InfoHasUnsavedChanges = true;
        }
    }

    private void MarkSettingsDirty()
    {
        if (!_suppressDirty)
        {
            SettingsHasUnsavedChanges = true;
        }
    }

    private void MarkChapterDirty()
    {
        if (!_suppressDirty)
        {
            ChapterHasUnsavedChanges = true;
        }
    }

    private void SetAllDirty(bool value)
    {
        InfoHasUnsavedChanges = value;
        SettingsHasUnsavedChanges = value;
        ChapterHasUnsavedChanges = value;
    }

    private void SetDirtyProperty(
        ref bool field,
        bool value,
        [CallerMemberName] string? propertyName = null)
    {
        if (SetProperty(ref field, value, propertyName))
        {
            OnPropertyChanged(nameof(HasUnsavedChanges));
        }
    }

    private void WithDirtySuppressed(Action action)
    {
        var oldValue = _suppressDirty;
        _suppressDirty = true;
        try
        {
            action();
        }
        finally
        {
            _suppressDirty = oldValue;
        }
    }

    private static void ReplaceCollection<T>(
        ObservableCollection<T> target,
        IEnumerable<T> values)
    {
        target.Clear();
        foreach (var value in values)
        {
            target.Add(value);
        }
    }
}
