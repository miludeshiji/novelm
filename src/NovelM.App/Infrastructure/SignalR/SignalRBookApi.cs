using NovelM_App.Application.Abstractions;
using NovelM_App.Domain.Books;

namespace NovelM_App.Infrastructure.SignalR;

internal sealed class SignalRBookApi : IBookApi
{
    private readonly ISignalRConnection _connection;

    public SignalRBookApi(ISignalRConnection connection)
    {
        _connection = connection;
    }

    public async Task<BookDetails> GetBookAsync(
        long bookId,
        CancellationToken cancellationToken)
    {
        var response = await _connection.InvokeAsync<BookResponseDto>(
            HubMethodNames.GetBookInfo,
            new { Id = bookId },
            cancellationToken);
        var book = response.Book;
        var author = !string.IsNullOrWhiteSpace(book.Author)
            ? book.Author
            : !string.IsNullOrWhiteSpace(book.Arthur)
                ? book.Arthur
                : string.Empty;
        var chapters = book.Chapter
            .Select((chapter, index) => new ChapterSummary(
                chapter.Id,
                chapter.Title,
                index + 1))
            .ToArray();

        return new BookDetails(
            book.Id,
            book.Title,
            author,
            book.Cover,
            book.Introduction,
            chapters);
    }

    public async Task<ChapterContent> GetChapterAsync(
        long bookId,
        int sortNum,
        CancellationToken cancellationToken)
    {
        var response = await _connection.InvokeAsync<ChapterResponseDto>(
            HubMethodNames.GetNovelContent,
            new { Bid = bookId, SortNum = sortNum, Convert = (string?)null },
            cancellationToken);
        var chapter = response.Chapter;
        return new ChapterContent(
            chapter.Id,
            chapter.BookId,
            chapter.SortNum,
            chapter.Title,
            chapter.Content);
    }
}
