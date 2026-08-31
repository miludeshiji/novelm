namespace NovelM_App.Infrastructure.Logging;

internal sealed class NullDiagnosticLog : IDiagnosticLog
{
    public static NullDiagnosticLog Instance { get; } = new();

    private NullDiagnosticLog()
    {
    }

    public Task WriteAsync(
        string eventName,
        IReadOnlyDictionary<string, object?> safeFields,
        Exception? exception,
        CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
