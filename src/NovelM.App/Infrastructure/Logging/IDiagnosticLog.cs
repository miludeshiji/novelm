namespace NovelM_App.Infrastructure.Logging;

internal interface IDiagnosticLog
{
    Task WriteAsync(
        string eventName,
        IReadOnlyDictionary<string, object?> safeFields,
        Exception? exception,
        CancellationToken cancellationToken);
}
