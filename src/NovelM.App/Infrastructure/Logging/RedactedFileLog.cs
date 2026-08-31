using System.Text.Json;
using NovelM_App.Infrastructure.Storage;

namespace NovelM_App.Infrastructure.Logging;

internal sealed class RedactedFileLog : IDiagnosticLog
{
    private const long DefaultMaximumBytes = 1024 * 1024;
    private const int DefaultHistoryCount = 2;
    private static readonly HashSet<string> AllowedFields = new(
        StringComparer.Ordinal)
    {
        "operation",
        "host",
        "httpStatus",
        "serverStatus",
        "hubMethod",
        "stage",
        "responseType",
        "byteLength",
        "elapsedMs",
        "connectionState",
        "correlationId",
        "errorKind"
    };
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _logDirectory;
    private readonly string _currentFile;
    private readonly long _maximumBytes;
    private readonly int _historyCount;
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    public RedactedFileLog(AppPaths paths)
        : this(paths.LogDirectory, DefaultMaximumBytes, DefaultHistoryCount)
    {
    }

    internal RedactedFileLog(
        string logDirectory,
        long maximumBytes,
        int historyCount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logDirectory);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(historyCount);

        _logDirectory = Path.GetFullPath(logDirectory);
        _currentFile = Path.Combine(_logDirectory, "app.log");
        _maximumBytes = maximumBytes;
        _historyCount = historyCount;
    }

    public async Task WriteAsync(
        string eventName,
        IReadOnlyDictionary<string, object?> safeFields,
        Exception? exception,
        CancellationToken cancellationToken)
    {
        var gateEntered = false;
        try
        {
            var entry = new LogEntry(
                DateTimeOffset.UtcNow,
                NormalizeEventName(eventName),
                FilterFields(safeFields),
                Snapshot(exception, depth: 0));
            var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(entry, JsonOptions);
            var lineBytes = new byte[jsonBytes.Length + 1];
            jsonBytes.CopyTo(lineBytes, 0);
            lineBytes[^1] = (byte)'\n';

            await _writeGate.WaitAsync(cancellationToken);
            gateEntered = true;
            Directory.CreateDirectory(_logDirectory);
            RotateIfRequired(lineBytes.Length);

            await using var stream = new FileStream(
                _currentFile,
                FileMode.Append,
                FileAccess.Write,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.Asynchronous);
            await stream.WriteAsync(lineBytes, CancellationToken.None);
            await stream.FlushAsync(CancellationToken.None);
        }
        catch
        {
        }
        finally
        {
            if (gateEntered)
            {
                _writeGate.Release();
            }
        }
    }

    private void RotateIfRequired(int incomingBytes)
    {
        if (!File.Exists(_currentFile)
            || new FileInfo(_currentFile).Length + incomingBytes <= _maximumBytes)
        {
            return;
        }

        for (var index = _historyCount; index >= 1; index--)
        {
            var source = index == 1
                ? _currentFile
                : Path.Combine(_logDirectory, $"app.{index - 1}.log");
            var destination = Path.Combine(_logDirectory, $"app.{index}.log");
            if (File.Exists(source))
            {
                File.Move(source, destination, overwrite: true);
            }
        }

        if (_historyCount == 0)
        {
            File.Delete(_currentFile);
        }
    }

    private static Dictionary<string, object?> FilterFields(
        IReadOnlyDictionary<string, object?> fields)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (name, value) in fields)
        {
            if (AllowedFields.Contains(name))
            {
                result[name] = SafeValue(value);
            }
        }

        return result;
    }

    private static object? SafeValue(object? value)
    {
        return value switch
        {
            null => null,
            string text => text.Length <= 300 ? text : text[..300],
            bool or byte or sbyte or short or ushort or int or uint or long or ulong
                or float or double or decimal => value,
            Enum enumValue => enumValue.ToString(),
            _ => value.GetType().FullName
        };
    }

    private static string NormalizeEventName(string eventName)
    {
        if (string.IsNullOrWhiteSpace(eventName))
        {
            return "invalid-event";
        }

        var normalized = new string(eventName
            .Where(character => char.IsAsciiLetterOrDigit(character)
                || character is '.' or '-' or '_')
            .Take(80)
            .ToArray());
        return normalized.Length == 0 ? "invalid-event" : normalized;
    }

    private static ExceptionEntry? Snapshot(Exception? exception, int depth)
    {
        if (exception is null || depth >= 4)
        {
            return null;
        }

        return new ExceptionEntry(
            exception.GetType().FullName ?? exception.GetType().Name,
            exception.StackTrace,
            Snapshot(exception.InnerException, depth + 1));
    }

    private sealed record LogEntry(
        DateTimeOffset TimestampUtc,
        string EventName,
        IReadOnlyDictionary<string, object?> Fields,
        ExceptionEntry? Exception);

    private sealed record ExceptionEntry(
        string Type,
        string? StackTrace,
        ExceptionEntry? Inner);
}
