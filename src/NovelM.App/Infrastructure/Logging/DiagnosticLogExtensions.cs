namespace NovelM_App.Infrastructure.Logging;

internal static class DiagnosticLogExtensions
{
    public static async Task TryWriteAsync(
        this IDiagnosticLog log,
        string eventName,
        IReadOnlyDictionary<string, object?> safeFields,
        Exception? exception = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await log.WriteAsync(
                eventName,
                safeFields,
                exception,
                cancellationToken);
        }
        catch
        {
        }
    }
}
