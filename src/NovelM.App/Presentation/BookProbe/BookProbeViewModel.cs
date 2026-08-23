using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovelM_App.Application.Abstractions;
using NovelM_App.Domain.Books;
using NovelM_App.Domain.Errors;
using NovelM_App.Presentation.Common;

namespace NovelM_App.Presentation.BookProbe;

public partial class BookProbeViewModel : ObservableObject
{
    private readonly IBookService _bookService;
    private readonly ErrorMessageMapper _errorMessageMapper;

    [ObservableProperty]
    public partial string BookIdText { get; set; }

    [ObservableProperty]
    public partial BookDetails? Book { get; set; }

    [ObservableProperty]
    public partial ChapterContent? Chapter { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? ErrorMessage { get; set; }

    public BookProbeViewModel(
        IBookService bookService,
        ErrorMessageMapper errorMessageMapper)
    {
        _bookService = bookService;
        _errorMessageMapper = errorMessageMapper;
        BookIdText = string.Empty;
    }

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task LoadBookAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            if (!long.TryParse(BookIdText, out var bookId) || bookId <= 0)
            {
                throw new AppException(
                    AppErrorKind.Validation,
                    "书籍 ID 必须是大于零的整数。");
            }

            var book = await _bookService.GetBookAsync(
                bookId,
                CancellationToken.None);
            Book = book;
            Chapter = null;
        }
        catch (Exception exception)
        {
            ErrorMessage = _errorMessageMapper.Map(exception);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task LoadChapterAsync(ChapterSummary? chapter)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            if (Book is null || chapter is null)
            {
                throw new AppException(AppErrorKind.Validation, "请先选择章节。");
            }

            var content = await _bookService.GetChapterAsync(
                Book.Id,
                chapter.SortNum,
                CancellationToken.None);
            Chapter = content;
        }
        catch (Exception exception)
        {
            ErrorMessage = _errorMessageMapper.Map(exception);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
