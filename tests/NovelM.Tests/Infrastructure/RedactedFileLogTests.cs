using System.Reflection;
using System.Text.Json;
using NovelM.Tests.TestSupport;
using NovelM_App.Infrastructure.Logging;

namespace NovelM.Tests.Infrastructure;

[TestClass]
public sealed class RedactedFileLogTests
{
    [TestMethod]
    public async Task WriteAsync_StoresOnlyAllowListedFieldsAndOmitsExceptionMessages()
    {
        using var directory = new TemporaryDirectory();
        var logger = new RedactedFileLog(directory.Path, 1024 * 1024, 2);
        const string email = "secret-reader@example.test";
        const string token = "synthetic-session-token-secret";
        const string password = "synthetic-password-secret";
        var exception = new InvalidOperationException(
            $"{email} {token} {password}");

        await logger.WriteAsync(
            "http.failed",
            new Dictionary<string, object?>
            {
                ["operation"] = "Login",
                ["host"] = "api.lightnovel.life",
                ["httpStatus"] = 503,
                ["byteLength"] = 41,
                ["email"] = email,
                ["token"] = token,
                ["password"] = password,
                ["requestBody"] = new string('x', 5000)
            },
            exception,
            CancellationToken.None);

        var text = await File.ReadAllTextAsync(Path.Combine(directory.Path, "app.log"));
        StringAssert.Contains(text, "http.failed");
        StringAssert.Contains(text, "Login");
        StringAssert.Contains(text, "api.lightnovel.life");
        StringAssert.Contains(text, "503");
        StringAssert.Contains(text, "InvalidOperationException");
        Assert.IsFalse(text.Contains(email, StringComparison.Ordinal));
        Assert.IsFalse(text.Contains(token, StringComparison.Ordinal));
        Assert.IsFalse(text.Contains(password, StringComparison.Ordinal));
        Assert.IsFalse(text.Contains(new string('x', 100), StringComparison.Ordinal));

        using var document = JsonDocument.Parse((await File.ReadAllLinesAsync(
            Path.Combine(directory.Path, "app.log"))).Single());
        var fields = document.RootElement.GetProperty("fields");
        Assert.IsFalse(fields.TryGetProperty("email", out _));
        Assert.IsFalse(fields.TryGetProperty("token", out _));
        Assert.IsFalse(fields.TryGetProperty("password", out _));
        Assert.IsFalse(fields.TryGetProperty("requestBody", out _));
    }

    [TestMethod]
    public async Task WriteAsync_ConcurrentCallsProduceCompleteJsonLines()
    {
        using var directory = new TemporaryDirectory();
        var logger = new RedactedFileLog(directory.Path, 1024 * 1024, 2);

        await Task.WhenAll(Enumerable.Range(0, 100).Select(index =>
            logger.WriteAsync(
                "signalr.completed",
                new Dictionary<string, object?>
                {
                    ["hubMethod"] = "GetMyInfo",
                    ["elapsedMs"] = index
                },
                exception: null,
                CancellationToken.None)));

        var lines = await File.ReadAllLinesAsync(Path.Combine(directory.Path, "app.log"));
        Assert.HasCount(100, lines);
        foreach (var line in lines)
        {
            using var document = JsonDocument.Parse(line);
            Assert.AreEqual(
                "signalr.completed",
                document.RootElement.GetProperty("eventName").GetString());
        }
    }

    [TestMethod]
    public async Task WriteAsync_CancellationAfterGateAcquisition_PreservesJsonLineBoundary()
    {
        using var directory = new TemporaryDirectory();
        var logger = new RedactedFileLog(directory.Path, 1024 * 1024, 2);
        var gate = GetWriteGate(logger);
        await gate.WaitAsync();
        using var cancellation = new CancellationTokenSource();
        var cancellationContext = new CancelOnPostSynchronizationContext(cancellation);
        var previousContext = SynchronizationContext.Current;
        Task firstWrite;

        try
        {
            SynchronizationContext.SetSynchronizationContext(cancellationContext);
            firstWrite = logger.WriteAsync(
                "boundary.before",
                new Dictionary<string, object?> { ["stage"] = "before" },
                exception: null,
                cancellation.Token);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }

        Assert.IsFalse(firstWrite.IsCompleted);
        gate.Release();

        await firstWrite.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.IsTrue(cancellation.IsCancellationRequested);
        await logger.WriteAsync(
            "boundary.after",
            new Dictionary<string, object?> { ["stage"] = "after" },
            exception: null,
            CancellationToken.None);

        var lines = await File.ReadAllLinesAsync(Path.Combine(directory.Path, "app.log"));
        Assert.HasCount(2, lines);
        var eventNames = lines.Select(line =>
        {
            using var document = JsonDocument.Parse(line);
            return document.RootElement.GetProperty("eventName").GetString();
        }).ToArray();
        CollectionAssert.AreEqual(
            new[] { "boundary.before", "boundary.after" },
            eventNames);
    }

    [TestMethod]
    public async Task WriteAsync_WhenLimitIsExceeded_RetainsCurrentAndTwoHistoryFiles()
    {
        using var directory = new TemporaryDirectory();
        var logger = new RedactedFileLog(directory.Path, 300, 2);

        for (var index = 0; index < 30; index++)
        {
            await logger.WriteAsync(
                "rotation.event",
                new Dictionary<string, object?>
                {
                    ["operation"] = "Rotate",
                    ["correlationId"] = $"event-{index:D2}"
                },
                exception: null,
                CancellationToken.None);
        }

        CollectionAssert.AreEquivalent(
            new[] { "app.log", "app.1.log", "app.2.log" },
            Directory.GetFiles(directory.Path)
                .Select(Path.GetFileName)
                .ToArray());
    }

    [TestMethod]
    public async Task WriteAsync_WhenDirectoryCannotBeCreated_DoesNotThrow()
    {
        using var directory = new TemporaryDirectory();
        var blockedPath = Path.Combine(directory.Path, "blocked");
        await File.WriteAllTextAsync(blockedPath, "not-a-directory");
        var logger = new RedactedFileLog(blockedPath, 1024, 2);

        await logger.WriteAsync(
            "storage.failed",
            new Dictionary<string, object?> { ["stage"] = "write" },
            new IOException("synthetic failure"),
            CancellationToken.None);
    }

    private static SemaphoreSlim GetWriteGate(RedactedFileLog logger)
    {
        var field = typeof(RedactedFileLog).GetField(
            "_writeGate",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(field);
        var gate = field.GetValue(logger) as SemaphoreSlim;
        Assert.IsNotNull(gate);
        return gate;
    }

    private sealed class CancelOnPostSynchronizationContext(
        CancellationTokenSource cancellation) : SynchronizationContext
    {
        private int _hasCancelled;

        public override void Post(SendOrPostCallback callback, object? state)
        {
            if (Interlocked.Exchange(ref _hasCancelled, 1) == 0)
            {
                cancellation.Cancel();
            }

            ThreadPool.QueueUserWorkItem(_ => callback(state));
        }
    }
}
