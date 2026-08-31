using System.Collections.Concurrent;
using NovelM_App.Infrastructure.Logging;

namespace NovelM.Tests.TestSupport;

internal sealed record RecordedDiagnosticEvent(
    string EventName,
    IReadOnlyDictionary<string, object?> Fields,
    Exception? Exception);

internal sealed class RecordingDiagnosticLog : IDiagnosticLog
{
    private readonly ConcurrentQueue<RecordedDiagnosticEvent> _events = new();

    public IReadOnlyList<RecordedDiagnosticEvent> Events => _events.ToArray();

    public Task WriteAsync(
        string eventName,
        IReadOnlyDictionary<string, object?> safeFields,
        Exception? exception,
        CancellationToken cancellationToken)
    {
        _events.Enqueue(new RecordedDiagnosticEvent(
            eventName,
            new Dictionary<string, object?>(safeFields),
            exception));
        return Task.CompletedTask;
    }
}

internal sealed class ThrowingDiagnosticLog : IDiagnosticLog
{
    public Task WriteAsync(
        string eventName,
        IReadOnlyDictionary<string, object?> safeFields,
        Exception? exception,
        CancellationToken cancellationToken)
    {
        throw new IOException("Synthetic diagnostic write failure.");
    }
}
