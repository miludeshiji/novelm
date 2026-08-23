using NovelM_App.Domain.Books;

namespace NovelM_App.Application.Abstractions;

public interface IBookApi
{
    Task<BookDetails> GetBookAsync(
        long bookId,
        CancellationToken cancellationToken);

    Task<ChapterContent> GetChapterAsync(
        long bookId,
        int sortNum,
        CancellationToken cancellationToken);
}
