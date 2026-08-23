using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using NovelM_App.Domain.Books;

namespace NovelM_App.Presentation.BookProbe;

public sealed partial class BookProbePage : Page
{
    public BookProbePage(BookProbeViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        Loaded += BookProbePage_Loaded;
        Unloaded += BookProbePage_Unloaded;
    }

    public BookProbeViewModel ViewModel { get; }

    private void BookProbePage_Loaded(object sender, RoutedEventArgs args)
    {
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        UpdateCover();
    }

    private void BookProbePage_Unloaded(object sender, RoutedEventArgs args)
    {
        ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
    }

    private void ChapterList_SelectionChanged(
        object sender,
        SelectionChangedEventArgs args)
    {
        if (ChapterList.SelectedItem is ChapterSummary chapter
            && ViewModel.LoadChapterCommand.CanExecute(chapter))
        {
            ViewModel.LoadChapterCommand.Execute(chapter);
        }
    }

    private void ViewModel_PropertyChanged(
        object? sender,
        PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(BookProbeViewModel.Book))
        {
            UpdateCover();
        }
    }

    private void UpdateCover()
    {
        var cover = ViewModel.Book?.Cover;
        if (Uri.TryCreate(cover, UriKind.Absolute, out var uri)
            && uri.Scheme is "http" or "https")
        {
            CoverImage.Source = new BitmapImage(uri);
            return;
        }

        CoverImage.Source = null;
    }
}
