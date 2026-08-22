namespace NovelM_App.Domain.Books;

public sealed record BookDetails(
    long Id,
    string Title,
    string Author,
    string Cover,
    string Introduction,
    IReadOnlyList<ChapterSummary> Chapters);
