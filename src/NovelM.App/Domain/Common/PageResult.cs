namespace NovelM_App.Domain.Common;

public sealed record PageResult<T>(
    IReadOnlyList<T> Items,
    int Page,
    int TotalPages);
