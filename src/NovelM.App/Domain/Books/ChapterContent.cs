namespace NovelM_App.Domain.Books;

public sealed record ChapterContent(
    long Id,
    long BookId,
    int SortNum,
    string Title,
    string Content);
