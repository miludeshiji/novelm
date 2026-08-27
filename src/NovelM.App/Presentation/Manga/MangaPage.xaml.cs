using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using NovelM_App.Domain.Manga;
using Windows.System;

namespace NovelM_App.Presentation.Manga;

public sealed partial class MangaPage : Page
{
    private CancellationTokenSource? _pageCancellation;
    private string? _dismissedNoticeMessage;
    private bool _hasLoadedInitialData;
    private bool _isInitialLoadInProgress;
    private bool _isLoaded;
    private bool _isSubscribed;
    private bool _isUpdatingControls;

    public MangaPage(MangaViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        Loaded += MangaPage_Loaded;
        Unloaded += MangaPage_Unloaded;
    }

    public MangaViewModel ViewModel { get; }

    private async void MangaPage_Loaded(object sender, RoutedEventArgs args)
    {
        _pageCancellation ??= new CancellationTokenSource();
        if (!_isSubscribed)
        {
            ViewModel.PropertyChanged += ViewModel_PropertyChanged;
            _isSubscribed = true;
        }

        UpdateViewState();
        UpdateDetailsCover();
        _isLoaded = true;

        if (_hasLoadedInitialData || _isInitialLoadInProgress)
        {
            return;
        }

        var pageCancellation = _pageCancellation;
        _isInitialLoadInProgress = true;
        try
        {
            var didLoad = await ExecuteAsync(ViewModel.LoadAsync);
            if (ReferenceEquals(_pageCancellation, pageCancellation))
            {
                _hasLoadedInitialData = didLoad;
            }
        }
        finally
        {
            if (ReferenceEquals(_pageCancellation, pageCancellation))
            {
                _isInitialLoadInProgress = false;
            }
        }
    }

    private void MangaPage_Unloaded(object sender, RoutedEventArgs args)
    {
        _isLoaded = false;
        _isInitialLoadInProgress = false;
        if (_isSubscribed)
        {
            ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
            _isSubscribed = false;
        }

        var cancellation = _pageCancellation;
        _pageCancellation = null;
        cancellation?.Cancel();
        cancellation?.Dispose();
    }

    private async void SearchButton_Click(object sender, RoutedEventArgs args)
    {
        await ExecuteAsync(ViewModel.SearchAsync);
    }

    private async void CatalogSearchBox_KeyDown(
        object sender,
        KeyRoutedEventArgs args)
    {
        if (args.Key != VirtualKey.Enter)
        {
            return;
        }

        args.Handled = true;
        await ExecuteAsync(ViewModel.SearchAsync);
    }

    private async void SortComboBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs args)
    {
        if (!_isLoaded
            || _isUpdatingControls
            || !ViewModel.IsSortEnabled
            || SortComboBox.SelectedIndex < 0)
        {
            return;
        }

        var order = SortComboBox.SelectedIndex switch
        {
            1 => ComicOrder.New,
            2 => ComicOrder.View,
            _ => ComicOrder.Latest
        };

        if (order != ViewModel.SelectedOrder)
        {
            await ExecuteAsync(token => ViewModel.ChangeOrderAsync(order, token));
        }
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs args)
    {
        await ExecuteAsync(ViewModel.LoadAsync);
    }

    private async void PreviousPageButton_Click(object sender, RoutedEventArgs args)
    {
        await ExecuteAsync(ViewModel.PreviousPageAsync);
    }

    private async void NextPageButton_Click(object sender, RoutedEventArgs args)
    {
        await ExecuteAsync(ViewModel.NextPageAsync);
    }

    private async void SeriesGridView_ItemClick(
        object sender,
        ItemClickEventArgs args)
    {
        if (args.ClickedItem is MangaListItem item)
        {
            await ExecuteAsync(token => ViewModel.OpenSeriesAsync(item, token));
        }
    }

    private void BackButton_Click(object sender, RoutedEventArgs args)
    {
        DismissCurrentNotice();
        ViewModel.BackToCatalog();
    }

    private void ChapterButton_Click(object sender, RoutedEventArgs args)
    {
        if (sender is FrameworkElement { DataContext: MangaChapterSummary chapter })
        {
            _dismissedNoticeMessage = null;
            ViewModel.ShowReaderUnavailable(chapter);
            UpdateViewState();
        }
    }

    private void CatalogCover_Loaded(object sender, RoutedEventArgs args)
    {
        if (sender is Image image)
        {
            UpdateTemplateCover(image, image.DataContext);
        }
    }

    private void CatalogCover_DataContextChanged(
        FrameworkElement sender,
        DataContextChangedEventArgs args)
    {
        if (sender is Image image)
        {
            UpdateTemplateCover(image, args.NewValue);
        }
    }

    private void VolumeCover_Loaded(object sender, RoutedEventArgs args)
    {
        if (sender is Image image)
        {
            UpdateTemplateCover(image, image.DataContext);
        }
    }

    private void VolumeCover_DataContextChanged(
        FrameworkElement sender,
        DataContextChangedEventArgs args)
    {
        if (sender is Image image)
        {
            UpdateTemplateCover(image, args.NewValue);
        }
    }

    private void NoticeInfoBar_Closed(object sender, InfoBarClosedEventArgs args)
    {
        _dismissedNoticeMessage = ViewModel.NoticeMessage;
    }

    private void ViewModel_PropertyChanged(
        object? sender,
        PropertyChangedEventArgs args)
    {
        if (string.IsNullOrEmpty(args.PropertyName)
            || args.PropertyName == nameof(MangaViewModel.SelectedSeries))
        {
            UpdateDetailsCover();
        }

        UpdateViewState();
    }

    private void UpdateViewState()
    {
        CatalogPanel.Visibility = ViewModel.IsCatalogVisible
            ? Visibility.Visible
            : Visibility.Collapsed;
        DetailsPanel.Visibility = ViewModel.IsDetailsVisible
            ? Visibility.Visible
            : Visibility.Collapsed;
        var noticeMessage = ViewModel.NoticeMessage;
        if (string.IsNullOrWhiteSpace(noticeMessage))
        {
            _dismissedNoticeMessage = null;
            NoticeInfoBar.IsOpen = false;
        }
        else if (!string.Equals(
                     noticeMessage,
                     _dismissedNoticeMessage,
                     StringComparison.Ordinal))
        {
            NoticeInfoBar.IsOpen = true;
        }

        var isBusy = ViewModel.IsBusy;
        var hasPages = ViewModel.TotalPages > 0;
        SearchButton.IsEnabled = !isBusy;
        RefreshButton.IsEnabled = !isBusy;
        CatalogSearchBox.IsEnabled = !isBusy;
        SortComboBox.IsEnabled = !isBusy && ViewModel.IsSortEnabled;
        SeriesGridView.IsEnabled = !isBusy;
        PaginationPanel.Visibility = hasPages
            ? Visibility.Visible
            : Visibility.Collapsed;
        PreviousPageButton.IsEnabled = hasPages
            && !isBusy
            && ViewModel.CurrentPage > 1;
        NextPageButton.IsEnabled = hasPages
            && !isBusy
            && ViewModel.CurrentPage < ViewModel.TotalPages;
        EmptyCatalogText.Visibility = !isBusy && ViewModel.Items.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        PageNumberText.Text = hasPages
            ? $"第 {ViewModel.CurrentPage} / {ViewModel.TotalPages} 页"
            : string.Empty;

        var selectedIndex = ViewModel.SelectedOrder switch
        {
            ComicOrder.New => 1,
            ComicOrder.View => 2,
            _ => 0
        };
        if (SortComboBox.SelectedIndex != selectedIndex)
        {
            _isUpdatingControls = true;
            SortComboBox.SelectedIndex = selectedIndex;
            _isUpdatingControls = false;
        }
    }

    private void UpdateDetailsCover()
    {
        DetailsCoverImage.Source = CreateHttpImage(ViewModel.SelectedSeries?.Cover);
    }

    private void DismissCurrentNotice()
    {
        _dismissedNoticeMessage = ViewModel.NoticeMessage;
        NoticeInfoBar.IsOpen = false;
    }

    private async Task<bool> ExecuteAsync(
        Func<CancellationToken, Task> operation)
    {
        var cancellation = _pageCancellation;
        if (cancellation is null || cancellation.IsCancellationRequested)
        {
            return false;
        }

        var cancellationToken = cancellation.Token;
        try
        {
            await operation(cancellationToken);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    private static void UpdateTemplateCover(Image image, object? dataContext)
    {
        image.Source = null;
        var source = dataContext switch
        {
            MangaListItem item => item.Cover,
            MangaVolume volume => volume.Cover,
            _ => null
        };
        image.Source = CreateHttpImage(source);
    }

    private static BitmapImage? CreateHttpImage(string? source)
    {
        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri)
            || (!uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                && !uri.Scheme.Equals(
                    Uri.UriSchemeHttps,
                    StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        return new BitmapImage(uri);
    }
}
