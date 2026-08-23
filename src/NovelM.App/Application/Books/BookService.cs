using NovelM_App.Application.Abstractions;
using NovelM_App.Domain.Books;
using NovelM_App.Domain.Errors;

namespace NovelM_App.Application.Books;

public sealed class BookService : IBookService
{
    private readonly IBookApi _bookApi;

    public BookService(IBookApi bookApi)
    {
        _bookApi = bookApi;
    }

    public Task<BookDetails> GetBookAsync(
        long bookId,
        CancellationToken cancellationToken)
    {
        ValidateBookId(bookId);
        return _bookApi.GetBookAsync(bookId, cancellationToken);
    }

    public Task<ChapterContent> GetChapterAsync(
        long bookId,
        int sortNum,
        CancellationToken cancellationToken)
    {
        ValidateBookId(bookId);
        if (sortNum <= 0)
        {
            throw new AppException(
                AppErrorKind.Validation,
                "Chapter number must be greater than zero.");
        }

        return _bookApi.GetChapterAsync(bookId, sortNum, cancellationToken);
    }

    private static void ValidateBookId(long bookId)
    {
        if (bookId <= 0)
        {
            throw new AppException(
                AppErrorKind.Validation,
                "Book identifier must be greater than zero.");
        }
    }
}
