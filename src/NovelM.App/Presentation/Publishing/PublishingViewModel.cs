using CommunityToolkit.Mvvm.ComponentModel;
using NovelM_App.Application.Abstractions;
using NovelM_App.Domain.Auth;
using NovelM_App.Domain.Errors;
using NovelM_App.Domain.Publishing;
using NovelM_App.Presentation.Common;

namespace NovelM_App.Presentation.Publishing;

public sealed class PublishingViewModel : ObservableObject
{
    private const int PageSize = 24;
    private const string UnsavedComicNotice = "当前漫画有未保存的更改，请先保存或确认放弃。";

    private readonly IAuthService _authService;
    private readonly IComicPublishingService _publishingService;
    private readonly ErrorMessageMapper _errorMessageMapper;
    private bool _isCheckingSession = true;
    private bool _isSignedOut;
    private IReadOnlyList<MyComicSummary> _comics = Array.Empty<MyComicSummary>();
    private MyComicSummary? _selectedComic;
    private string _searchText = string.Empty;
    private int _currentPage = 1;
    private int _totalPages = 1;
    private bool _isBusy;
    private string? _errorMessage;
    private string? _noticeMessage;
    private UserProfile? _currentUser;

    public PublishingViewModel(
        IAuthService authService,
        IComicPublishingService publishingService,
        ComicEditorViewModel editor,
        ErrorMessageMapper errorMessageMapper)
    {
        _authService = authService;
        _publishingService = publishingService;
        Editor = editor;
        _errorMessageMapper = errorMessageMapper;
        Editor.SessionExpired += OnEditorSessionExpired;
    }

    public event EventHandler? AccountNavigationRequested;

    public bool IsCheckingSession
    {
        get => _isCheckingSession;
        private set
        {
            if (SetProperty(ref _isCheckingSession, value))
            {
                OnPropertyChanged(nameof(IsWorkbenchVisible));
            }
        }
    }

    public bool IsSignedOut
    {
        get => _isSignedOut;
        private set
        {
            if (SetProperty(ref _isSignedOut, value))
            {
                OnPropertyChanged(nameof(IsWorkbenchVisible));
            }
        }
    }

    public bool IsWorkbenchVisible => !IsCheckingSession && !IsSignedOut;

    public IReadOnlyList<MyComicSummary> Comics
    {
        get => _comics;
        private set => SetProperty(ref _comics, value);
    }

    public MyComicSummary? SelectedComic
    {
        get => _selectedComic;
        private set => SetProperty(ref _selectedComic, value);
    }

    public string SearchText
    {
        get => _searchText;
        set => SetProperty(ref _searchText, value);
    }

    public int CurrentPage
    {
        get => _currentPage;
        private set => SetProperty(ref _currentPage, value);
    }

    public int TotalPages
    {
        get => _totalPages;
        private set => SetProperty(ref _totalPages, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
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

    public UserProfile? CurrentUser
    {
        get => _currentUser;
        private set => SetProperty(ref _currentUser, value);
    }

    public ComicEditorViewModel Editor { get; }

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        IsCheckingSession = true;
        ErrorMessage = null;
        NoticeMessage = null;
        try
        {
            var user = _authService.CurrentUser
                ?? await _authService.RestoreAsync(cancellationToken);
            if (user is null)
            {
                EnterSignedOutState(errorMessage: null);
                return;
            }

            CurrentUser = user;
            IsSignedOut = false;
            await LoadPageAsync(1, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            if (IsUnauthorized(exception))
            {
                EnterSignedOutState(_errorMessageMapper.Map(exception));
            }
            else
            {
                ErrorMessage = _errorMessageMapper.Map(exception);
                if (CurrentUser is null)
                {
                    IsSignedOut = true;
                }
            }
        }
        finally
        {
            IsCheckingSession = false;
        }
    }

    public Task RefreshAsync(CancellationToken cancellationToken)
    {
        return CanManage()
            ? LoadPageAsync(CurrentPage, cancellationToken)
            : Task.CompletedTask;
    }

    public Task SearchAsync(CancellationToken cancellationToken)
    {
        return CanManage()
            ? LoadPageAsync(1, cancellationToken)
            : Task.CompletedTask;
    }

    public Task PreviousPageAsync(CancellationToken cancellationToken)
    {
        return CanManage() && CurrentPage > 1
            ? LoadPageAsync(CurrentPage - 1, cancellationToken)
            : Task.CompletedTask;
    }

    public Task NextPageAsync(CancellationToken cancellationToken)
    {
        return CanManage() && CurrentPage < TotalPages
            ? LoadPageAsync(CurrentPage + 1, cancellationToken)
            : Task.CompletedTask;
    }

    public async Task CreateComicAsync(
        CreateComicDraft draft,
        CancellationToken cancellationToken)
    {
        if (!CanManage() || IsBusy)
        {
            return;
        }

        long newId;
        StartOperation();
        try
        {
            newId = await _publishingService.CreateComicAsync(draft, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            HandleFailure(exception);
            return;
        }
        finally
        {
            IsBusy = false;
        }

        await LoadPageAsync(1, cancellationToken);
        if (!CanManage())
        {
            return;
        }

        var created = Comics.FirstOrDefault(comic => comic.Id == newId);
        if (created is null)
        {
            created = new MyComicSummary(
                newId,
                "Comic",
                draft.Title,
                draft.Cover,
                draft.CategoryName,
                DateTimeOffset.Now);
            Comics = Comics.Concat([created]).ToArray();
        }

        await SelectComicAsync(
            created,
            discardUnsavedChanges: true,
            cancellationToken);
    }

    public async Task DeleteSelectedAsync(CancellationToken cancellationToken)
    {
        if (!CanManage() || SelectedComic is not { } selected || IsBusy)
        {
            return;
        }

        var originalIndex = FindComicIndex(selected.Id);
        StartOperation();
        try
        {
            await _publishingService.DeleteComicAsync(selected.Id, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            HandleFailure(exception);
            return;
        }
        finally
        {
            IsBusy = false;
        }

        var remaining = Comics.Where(comic => comic.Id != selected.Id).ToArray();
        Comics = remaining;
        if (remaining.Length == 0)
        {
            SelectedComic = null;
            Editor.Clear();
            return;
        }

        var adjacentIndex = Math.Min(Math.Max(0, originalIndex), remaining.Length - 1);
        var adjacent = remaining[adjacentIndex];
        SelectedComic = null;
        Editor.Clear();
        await SelectComicAsync(
            adjacent,
            discardUnsavedChanges: true,
            cancellationToken);
    }

    public void SelectComic(MyComicSummary comic)
    {
        SelectedComic = comic;
    }

    public async Task<bool> SelectComicAsync(
        MyComicSummary comic,
        bool discardUnsavedChanges,
        CancellationToken cancellationToken)
    {
        if (!CanManage() || IsBusy)
        {
            return false;
        }

        if (SelectedComic?.Id == comic.Id
            && Editor.BookId == comic.Id
            && Editor.IsLoaded)
        {
            return true;
        }

        if (Editor.HasUnsavedChanges && !discardUnsavedChanges)
        {
            NoticeMessage = UnsavedComicNotice;
            return false;
        }

        var user = CurrentUser!;
        NoticeMessage = null;
        await Editor.LoadAsync(comic.Id, user, cancellationToken);
        if (!CanManage() || !Editor.IsLoaded || Editor.BookId != comic.Id)
        {
            return false;
        }

        SelectedComic = comic;
        return true;
    }

    public void RequestAccountNavigation()
    {
        AccountNavigationRequested?.Invoke(this, EventArgs.Empty);
    }

    private async Task<bool> LoadPageAsync(
        int page,
        CancellationToken cancellationToken)
    {
        if (IsBusy)
        {
            return false;
        }

        var keyword = SearchText.Trim();
        var selectedId = SelectedComic?.Id;
        StartOperation();
        try
        {
            var result = await _publishingService.GetMyComicsAsync(
                page,
                PageSize,
                keyword,
                cancellationToken);
            Comics = result.Items;
            CurrentPage = result.Page;
            TotalPages = result.TotalPages;

            if (selectedId is long id)
            {
                var refreshedSelection = result.Items.FirstOrDefault(comic => comic.Id == id);
                if (refreshedSelection is not null)
                {
                    SelectedComic = refreshedSelection;
                }
                else if (Editor.HasUnsavedChanges)
                {
                    NoticeMessage = "当前漫画含有未保存的修改，已保留编辑草稿。";
                }
                else
                {
                    SelectedComic = null;
                    Editor.Clear();
                }
            }

            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            HandleFailure(exception);
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void StartOperation()
    {
        IsBusy = true;
        ErrorMessage = null;
        NoticeMessage = null;
    }

    private void HandleFailure(Exception exception)
    {
        if (IsUnauthorized(exception))
        {
            EnterSignedOutState(_errorMessageMapper.Map(exception));
            return;
        }

        ErrorMessage = _errorMessageMapper.Map(exception);
    }

    private void EnterSignedOutState(string? errorMessage)
    {
        CurrentUser = null;
        Comics = Array.Empty<MyComicSummary>();
        SelectedComic = null;
        CurrentPage = 1;
        TotalPages = 1;
        Editor.Clear();
        IsSignedOut = true;
        ErrorMessage = errorMessage;
    }

    private bool CanManage()
    {
        return !IsSignedOut && CurrentUser is not null;
    }

    private int FindComicIndex(long comicId)
    {
        for (var index = 0; index < Comics.Count; index++)
        {
            if (Comics[index].Id == comicId)
            {
                return index;
            }
        }

        return 0;
    }

    private void OnEditorSessionExpired(object? sender, EventArgs e)
    {
        EnterSignedOutState(
            _errorMessageMapper.Map(
                new AppException(AppErrorKind.Unauthorized, "Session expired.")));
    }

    private static bool IsUnauthorized(Exception exception)
    {
        return exception is AppException { Kind: AppErrorKind.Unauthorized };
    }
}
