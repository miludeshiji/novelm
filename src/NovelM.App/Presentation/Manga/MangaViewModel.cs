using CommunityToolkit.Mvvm.ComponentModel;
using NovelM_App.Application.Abstractions;
using NovelM_App.Domain.Common;
using NovelM_App.Domain.Manga;
using NovelM_App.Presentation.Common;

namespace NovelM_App.Presentation.Manga;

public sealed class MangaViewModel : ObservableObject
{
    private const int PageSize = 24;
    private const string SearchMode = "fuzzy";
    private const string ReaderUnavailableMessage = "漫画阅读器将在后续版本提供";

    private readonly IMangaService _mangaService;
    private readonly ErrorMessageMapper _errorMessageMapper;
    private IReadOnlyList<MangaListItem> _items = Array.Empty<MangaListItem>();
    private MangaSeriesDetails? _selectedSeries;
    private string _searchText = string.Empty;
    private ComicOrder _selectedOrder = ComicOrder.Latest;
    private int _currentPage = 1;
    private int _totalPages = 1;
    private bool _isBusy;
    private string? _errorMessage;
    private string? _noticeMessage;
    private int _requestVersion;

    public MangaViewModel(
        IMangaService mangaService,
        ErrorMessageMapper errorMessageMapper)
    {
        _mangaService = mangaService;
        _errorMessageMapper = errorMessageMapper;
    }

    public IReadOnlyList<MangaListItem> Items
    {
        get => _items;
        private set => SetProperty(ref _items, value);
    }

    public MangaSeriesDetails? SelectedSeries
    {
        get => _selectedSeries;
        private set
        {
            if (SetProperty(ref _selectedSeries, value))
            {
                OnPropertyChanged(nameof(IsDetailsVisible));
                OnPropertyChanged(nameof(IsCatalogVisible));
            }
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                OnPropertyChanged(nameof(IsSortEnabled));
            }
        }
    }

    public ComicOrder SelectedOrder
    {
        get => _selectedOrder;
        set => SetProperty(ref _selectedOrder, value);
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

    public bool IsDetailsVisible => SelectedSeries is not null;

    public bool IsCatalogVisible => SelectedSeries is null;

    public bool IsSortEnabled => string.IsNullOrWhiteSpace(SearchText);

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

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public string? NoticeMessage
    {
        get => _noticeMessage;
        private set => SetProperty(ref _noticeMessage, value);
    }

    public Task LoadAsync(CancellationToken cancellationToken)
    {
        return LoadPageAsync(CurrentPage, cancellationToken);
    }

    public Task SearchAsync(CancellationToken cancellationToken)
    {
        return LoadPageAsync(1, cancellationToken);
    }

    public Task ChangeOrderAsync(
        ComicOrder order,
        CancellationToken cancellationToken)
    {
        SelectedOrder = order;
        return LoadPageAsync(1, cancellationToken);
    }

    public Task PreviousPageAsync(CancellationToken cancellationToken)
    {
        return CurrentPage <= 1
            ? Task.CompletedTask
            : LoadPageAsync(CurrentPage - 1, cancellationToken);
    }

    public Task NextPageAsync(CancellationToken cancellationToken)
    {
        return CurrentPage >= TotalPages
            ? Task.CompletedTask
            : LoadPageAsync(CurrentPage + 1, cancellationToken);
    }

    public async Task OpenSeriesAsync(
        MangaListItem item,
        CancellationToken cancellationToken)
    {
        var requestVersion = StartRequest();
        try
        {
            var series = await _mangaService.GetSeriesAsync(
                item.SeriesTitle,
                SelectedOrder,
                cancellationToken);
            if (IsLatestRequest(requestVersion))
            {
                SelectedSeries = series;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            if (IsLatestRequest(requestVersion))
            {
                ErrorMessage = _errorMessageMapper.Map(exception);
            }
        }
        finally
        {
            FinishRequest(requestVersion);
        }
    }

    public void BackToCatalog()
    {
        SelectedSeries = null;
    }

    public void ShowReaderUnavailable(MangaChapterSummary chapter)
    {
        NoticeMessage = ReaderUnavailableMessage;
    }

    private async Task LoadPageAsync(
        int page,
        CancellationToken cancellationToken)
    {
        var requestVersion = StartRequest();
        var searchText = SearchText.Trim();
        var selectedOrder = SelectedOrder;
        try
        {
            PageResult<MangaListItem> result;
            if (string.IsNullOrWhiteSpace(searchText))
            {
                result = await _mangaService.GetListAsync(
                    page,
                    PageSize,
                    selectedOrder,
                    cancellationToken);
            }
            else
            {
                result = await _mangaService.SearchAsync(
                    searchText,
                    SearchMode,
                    page,
                    PageSize,
                    cancellationToken);
            }

            if (!IsLatestRequest(requestVersion))
            {
                return;
            }

            Items = result.Items;
            if (!IsLatestRequest(requestVersion))
            {
                return;
            }

            CurrentPage = result.Page;
            if (!IsLatestRequest(requestVersion))
            {
                return;
            }

            TotalPages = result.TotalPages;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            if (IsLatestRequest(requestVersion))
            {
                ErrorMessage = _errorMessageMapper.Map(exception);
            }
        }
        finally
        {
            FinishRequest(requestVersion);
        }
    }

    private int StartRequest()
    {
        var requestVersion = Interlocked.Increment(ref _requestVersion);
        if (IsLatestRequest(requestVersion))
        {
            IsBusy = true;
        }

        if (IsLatestRequest(requestVersion))
        {
            ErrorMessage = null;
        }

        return requestVersion;
    }

    private void FinishRequest(int requestVersion)
    {
        if (IsLatestRequest(requestVersion))
        {
            IsBusy = false;
        }
    }

    private bool IsLatestRequest(int requestVersion)
    {
        return requestVersion == Volatile.Read(ref _requestVersion);
    }
}
