using System.Collections.Concurrent;
using NovelM_App.Application.Abstractions;
using NovelM_App.Application.Auth;
using NovelM_App.Domain.Auth;
using NovelM_App.Domain.Errors;

namespace NovelM.Tests.Application;

[TestClass]
public sealed class AuthSessionTests
{
    [TestMethod]
    public async Task GetAccessTokenAsync_ConcurrentCallersShareOneRefresh()
    {
        const string storedRefreshToken = "synthetic-stored-refresh";
        const string refreshedSessionToken = "synthetic-refreshed-session";
        var refreshStarted = NewCompletionSource();
        var releaseRefresh = NewCompletionSource();
        var store = new FakeTokenStore(storedRefreshToken);
        var api = new FakeAuthApi(async (refreshToken, _) =>
        {
            Assert.AreEqual(storedRefreshToken, refreshToken);
            refreshStarted.SetResult();
            await releaseRefresh.Task;
            return refreshedSessionToken;
        });
        var session = new AuthSession(api, store);

        var calls = Enumerable.Range(0, 10)
            .Select(_ => session.GetAccessTokenAsync(CancellationToken.None))
            .ToArray();
        await refreshStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.AreEqual(1, api.RefreshCount);
        Assert.AreEqual(1, store.ReadCount);
        releaseRefresh.SetResult();

        var results = await Task.WhenAll(calls)
            .WaitAsync(TimeSpan.FromSeconds(5));
        Assert.IsTrue(results.All(result => result == refreshedSessionToken));
        Assert.AreEqual(refreshedSessionToken, session.SessionToken);
        Assert.AreEqual(1, api.RefreshCount);
        Assert.AreEqual(1, store.ReadCount);
    }

    [TestMethod]
    public async Task GetAccessTokenAsync_ConcurrentCallersShareRefreshFailure()
    {
        var failure = Error(AppErrorKind.Transport);
        var refreshStarted = NewCompletionSource();
        var releaseRefresh = NewCompletionSource();
        var store = new FakeTokenStore("synthetic-stored-refresh");
        var api = new FakeAuthApi(async (_, _) =>
        {
            refreshStarted.TrySetResult();
            await releaseRefresh.Task;
            throw failure;
        });
        var session = new AuthSession(api, store);

        var calls = Enumerable.Range(0, 10)
            .Select(_ => session.GetAccessTokenAsync(CancellationToken.None))
            .ToArray();
        await refreshStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual(1, api.RefreshCount);
        releaseRefresh.SetResult();

        var failures = await Task.WhenAll(calls.Select(call =>
                Assert.ThrowsExactlyAsync<AppException>(() =>
                    call.WaitAsync(TimeSpan.FromSeconds(5)))))
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.IsTrue(failures.All(actual => ReferenceEquals(failure, actual)));
        Assert.AreEqual(1, api.RefreshCount);
        Assert.AreEqual(1, store.ReadCount);
        Assert.AreEqual("synthetic-stored-refresh", store.StoredToken);
        Assert.IsNull(session.SessionToken);
    }

    [TestMethod]
    public async Task GetAccessTokenAsync_ConcurrentCallersShareLeaderCancellation()
    {
        var refreshStarted = NewCompletionSource();
        var releaseRefresh = NewCompletionSource();
        var store = new FakeTokenStore("synthetic-stored-refresh");
        var api = new FakeAuthApi(async (_, cancellationToken) =>
        {
            refreshStarted.TrySetResult();
            await releaseRefresh.Task;
            cancellationToken.ThrowIfCancellationRequested();
            return "synthetic-unexpected-session";
        });
        var session = new AuthSession(api, store);
        using var leaderCancellation = new CancellationTokenSource();

        var leader = session.GetAccessTokenAsync(leaderCancellation.Token);
        await refreshStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var calls = new[] { leader }
            .Concat(Enumerable.Range(0, 9)
                .Select(_ => session.GetAccessTokenAsync(CancellationToken.None)))
            .ToArray();

        leaderCancellation.Cancel();
        releaseRefresh.SetResult();

        var outcomes = await Task.WhenAll(calls.Select(CaptureFailureAsync))
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.AreEqual(1, api.RefreshCount);
        Assert.AreEqual(1, store.ReadCount);
        Assert.IsTrue(outcomes.All(exception => exception is OperationCanceledException));
        Assert.AreEqual("synthetic-stored-refresh", store.StoredToken);
        Assert.IsNull(session.SessionToken);
    }

    [TestMethod]
    public async Task GetAccessTokenAsync_ExistingSessionReturnsDespiteCancellationWithoutDependencies()
    {
        var store = new FakeTokenStore();
        var api = new FakeAuthApi();
        var session = new AuthSession(api, store);
        await session.SetTokensAsync(
            new LoginTokens("synthetic-session", "synthetic-refresh"),
            CancellationToken.None);
        store.ResetCounts();
        api.ResetCounts();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await session.GetAccessTokenAsync(cancellation.Token);

        Assert.AreEqual("synthetic-session", result);
        Assert.AreEqual(0, store.ReadCount);
        Assert.AreEqual(0, store.SaveCount);
        Assert.AreEqual(0, store.DeleteCount);
        Assert.AreEqual(0, api.RefreshCount);
    }

    [TestMethod]
    public async Task SetTokensAsync_PersistsRefreshBeforePublishingSession()
    {
        const string sessionToken = "synthetic-session-secret";
        const string refreshToken = "synthetic-refresh-secret";
        var store = new FakeTokenStore();
        var api = new FakeAuthApi();
        AuthSession? session = null;
        store.OnSaveAsync = (value, _) =>
        {
            Assert.AreEqual(refreshToken, value);
            Assert.IsNull(session!.SessionToken);
            return Task.CompletedTask;
        };
        session = new AuthSession(api, store);
        var tokens = new LoginTokens(sessionToken, refreshToken);

        await session.SetTokensAsync(tokens, CancellationToken.None);

        Assert.AreEqual(refreshToken, store.StoredToken);
        CollectionAssert.AreEqual(new[] { refreshToken }, store.SavedTokens.ToArray());
        Assert.AreEqual(sessionToken, session.SessionToken);
        Assert.AreEqual(0, api.RefreshCount);
        Assert.IsFalse(tokens.ToString().Contains(sessionToken, StringComparison.Ordinal));
        Assert.IsFalse(tokens.ToString().Contains(refreshToken, StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task SetTokensAsync_SaveFailurePreservesPreviousSessionAndException()
    {
        var store = new FakeTokenStore();
        var session = new AuthSession(new FakeAuthApi(), store);
        await session.SetTokensAsync(
            new LoginTokens("synthetic-existing-session", "synthetic-existing-refresh"),
            CancellationToken.None);
        var failure = Error(AppErrorKind.Storage);
        store.SaveException = failure;

        var actual = await Assert.ThrowsExactlyAsync<AppException>(() =>
            session.SetTokensAsync(
                new LoginTokens("synthetic-new-session", "synthetic-new-refresh"),
                CancellationToken.None));

        Assert.AreSame(failure, actual);
        Assert.AreEqual("synthetic-existing-session", session.SessionToken);
        Assert.AreEqual("synthetic-existing-refresh", store.StoredToken);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    public async Task GetAccessTokenAsync_MissingRefreshReturnsNullWithoutApi(string? storedToken)
    {
        var store = new FakeTokenStore(storedToken);
        var api = new FakeAuthApi();
        var session = new AuthSession(api, store);

        var result = await session.GetAccessTokenAsync(CancellationToken.None);

        Assert.IsNull(result);
        Assert.IsNull(session.SessionToken);
        Assert.AreEqual(1, store.ReadCount);
        Assert.AreEqual(0, api.RefreshCount);
        Assert.AreEqual(0, store.DeleteCount);
    }

    [TestMethod]
    [DataRow(-100)]
    [DataRow(404)]
    public async Task GetAccessTokenAsync_UnauthorizedRefreshDeletesTokenAndReturnsNull(int status)
    {
        var store = new FakeTokenStore("synthetic-stored-refresh");
        var api = new FakeAuthApi((_, _) =>
            Task.FromException<string>(Error(AppErrorKind.Unauthorized, status)));
        var session = new AuthSession(api, store);

        var result = await session.GetAccessTokenAsync(CancellationToken.None);

        Assert.IsNull(result);
        Assert.IsNull(session.SessionToken);
        Assert.IsNull(store.StoredToken);
        Assert.AreEqual(1, api.RefreshCount);
        Assert.AreEqual(1, store.DeleteCount);
        Assert.IsFalse(store.DeleteCancellationTokens.Single().CanBeCanceled);
    }

    [TestMethod]
    public async Task GetAccessTokenAsync_UnauthorizedDeleteFailureSurfacesStorageAndLeavesSessionNull()
    {
        var deleteFailure = Error(AppErrorKind.Storage);
        var store = new FakeTokenStore("synthetic-stored-refresh")
        {
            DeleteException = deleteFailure
        };
        var api = new FakeAuthApi((_, _) => Task.FromException<string>(
            Error(AppErrorKind.Unauthorized, -100)));
        var session = new AuthSession(api, store);

        var actual = await Assert.ThrowsExactlyAsync<AppException>(() =>
            session.GetAccessTokenAsync(CancellationToken.None));

        Assert.AreSame(deleteFailure, actual);
        Assert.IsNull(session.SessionToken);
        Assert.AreEqual("synthetic-stored-refresh", store.StoredToken);
        Assert.AreEqual(1, api.RefreshCount);
        Assert.AreEqual(1, store.DeleteCount);
        Assert.IsFalse(store.DeleteCancellationTokens.Single().CanBeCanceled);
    }

    [TestMethod]
    [DataRow(AppErrorKind.Transport)]
    [DataRow(AppErrorKind.Server)]
    [DataRow(AppErrorKind.Protocol)]
    [DataRow(AppErrorKind.Storage)]
    [DataRow(AppErrorKind.Unexpected)]
    public async Task GetAccessTokenAsync_NonUnauthorizedFailurePreservesRefreshAndException(
        AppErrorKind kind)
    {
        var failure = Error(kind);
        var store = new FakeTokenStore("synthetic-stored-refresh");
        var api = new FakeAuthApi((_, _) => Task.FromException<string>(failure));
        var session = new AuthSession(api, store);

        var actual = await Assert.ThrowsExactlyAsync<AppException>(() =>
            session.GetAccessTokenAsync(CancellationToken.None));

        Assert.AreSame(failure, actual);
        Assert.IsNull(session.SessionToken);
        Assert.AreEqual("synthetic-stored-refresh", store.StoredToken);
        Assert.AreEqual(1, api.RefreshCount);
        Assert.AreEqual(0, store.DeleteCount);
    }

    [TestMethod]
    public async Task InvalidateSessionToken_ClearsOnlyMemoryAndNextGetRefreshes()
    {
        const string refreshToken = "synthetic-persisted-refresh";
        var store = new FakeTokenStore();
        var api = new FakeAuthApi((value, _) =>
        {
            Assert.AreEqual(refreshToken, value);
            return Task.FromResult("synthetic-refreshed-session");
        });
        var session = new AuthSession(api, store);
        await session.SetTokensAsync(
            new LoginTokens("synthetic-original-session", refreshToken),
            CancellationToken.None);
        store.ResetCounts();

        session.InvalidateSessionToken();

        Assert.IsNull(session.SessionToken);
        Assert.AreEqual(refreshToken, store.StoredToken);
        Assert.AreEqual(0, store.DeleteCount);

        var result = await session.GetAccessTokenAsync(CancellationToken.None);

        Assert.AreEqual("synthetic-refreshed-session", result);
        Assert.AreEqual("synthetic-refreshed-session", session.SessionToken);
        Assert.AreEqual(1, store.ReadCount);
        Assert.AreEqual(1, api.RefreshCount);
        Assert.AreEqual(0, store.DeleteCount);
    }

    [TestMethod]
    public async Task ClearAsync_ClearsMemoryAndPersistentToken()
    {
        var store = new FakeTokenStore();
        var session = new AuthSession(new FakeAuthApi(), store);
        await session.SetTokensAsync(
            new LoginTokens("synthetic-session", "synthetic-refresh"),
            CancellationToken.None);
        store.ResetCounts();

        await session.ClearAsync(CancellationToken.None);

        Assert.IsNull(session.SessionToken);
        Assert.IsNull(store.StoredToken);
        Assert.AreEqual(1, store.DeleteCount);
    }

    [TestMethod]
    public async Task ClearAsync_DeleteFailureStillClearsMemoryAndPropagatesException()
    {
        var failure = Error(AppErrorKind.Storage);
        var store = new FakeTokenStore();
        var session = new AuthSession(new FakeAuthApi(), store);
        await session.SetTokensAsync(
            new LoginTokens("synthetic-session", "synthetic-refresh"),
            CancellationToken.None);
        store.DeleteException = failure;

        var actual = await Assert.ThrowsExactlyAsync<AppException>(() =>
            session.ClearAsync(CancellationToken.None));

        Assert.AreSame(failure, actual);
        Assert.IsNull(session.SessionToken);
        Assert.AreEqual("synthetic-refresh", store.StoredToken);
        Assert.AreEqual(1, store.DeleteCount);
    }

    [TestMethod]
    public async Task ClearAsync_WaitsForRefreshThenPreventsTokenResurrection()
    {
        var refreshStarted = NewCompletionSource();
        var releaseRefresh = NewCompletionSource();
        var store = new FakeTokenStore("synthetic-stored-refresh");
        var api = new FakeAuthApi(async (_, _) =>
        {
            refreshStarted.SetResult();
            await releaseRefresh.Task;
            return "synthetic-refreshed-session";
        });
        var session = new AuthSession(api, store);

        var refresh = session.GetAccessTokenAsync(CancellationToken.None);
        await refreshStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var clear = session.ClearAsync(CancellationToken.None);

        Assert.IsFalse(clear.IsCompleted);
        releaseRefresh.SetResult();
        Assert.AreEqual(
            "synthetic-refreshed-session",
            await refresh.WaitAsync(TimeSpan.FromSeconds(5)));
        await clear.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.IsNull(session.SessionToken);
        Assert.IsNull(store.StoredToken);
        Assert.AreEqual(1, api.RefreshCount);
        Assert.AreEqual(1, store.DeleteCount);
    }

    [TestMethod]
    public async Task GetAccessTokenAsync_AfterCompletedClearDoesNotJoinEarlierRefreshGeneration()
    {
        const string refreshedSessionToken = "synthetic-refreshed-session";
        var refreshStarted = NewCompletionSource();
        var releaseRefresh = NewCompletionSource();
        var refreshHandlerReturning = NewCompletionSource();
        var context = new QueuedSynchronizationContext();
        var store = new FakeTokenStore("synthetic-stored-refresh");
        var api = new FakeAuthApi(async (_, _) =>
        {
            refreshStarted.SetResult();
            await releaseRefresh.Task;
            refreshHandlerReturning.SetResult();
            return refreshedSessionToken;
        });
        var session = new AuthSession(api, store);

        var refresh = InvokeWithContext(
            context,
            () => session.GetAccessTokenAsync(CancellationToken.None));
        await refreshStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var clear = InvokeWithContext(
            null,
            () => session.ClearAsync(CancellationToken.None));

        releaseRefresh.SetResult();
        await RunNextAsync(context);
        Assert.IsTrue(refreshHandlerReturning.Task.IsCompletedSuccessfully);
        await RunNextAsync(context);
        await clear.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.IsNull(session.SessionToken);
        Assert.IsNull(store.StoredToken);
        var afterClear = InvokeWithContext(
            null,
            () => session.GetAccessTokenAsync(CancellationToken.None));

        await RunNextAsync(context);
        await PumpUntilCompletedAsync(context, refresh);
        var refreshResult = await refresh.WaitAsync(TimeSpan.FromSeconds(5));
        var afterClearResult = await afterClear.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.AreEqual(refreshedSessionToken, refreshResult);
        Assert.IsNull(afterClearResult);
        Assert.IsNull(session.SessionToken);
        Assert.IsNull(store.StoredToken);
        Assert.AreEqual(1, api.RefreshCount);
        Assert.AreEqual(2, store.ReadCount);
        Assert.AreEqual(1, store.DeleteCount);
    }

    [TestMethod]
    public async Task GetAccessTokenAsync_AlreadyCancelledDoesNoWorkWithoutMemoryToken()
    {
        var store = new FakeTokenStore("synthetic-stored-refresh");
        var api = new FakeAuthApi();
        var session = new AuthSession(api, store);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            session.GetAccessTokenAsync(cancellation.Token));

        Assert.AreEqual(0, store.ReadCount);
        Assert.AreEqual(0, api.RefreshCount);
    }

    [TestMethod]
    public async Task GetAccessTokenAsync_WaiterCancellationDoesNotCancelLeaderRefresh()
    {
        var refreshStarted = NewCompletionSource();
        var releaseRefresh = NewCompletionSource();
        var store = new FakeTokenStore("synthetic-stored-refresh");
        var api = new FakeAuthApi(async (_, cancellationToken) =>
        {
            refreshStarted.SetResult();
            await releaseRefresh.Task;
            Assert.IsFalse(cancellationToken.IsCancellationRequested);
            return "synthetic-refreshed-session";
        });
        var session = new AuthSession(api, store);
        var leader = session.GetAccessTokenAsync(CancellationToken.None);
        await refreshStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var waiterCancellation = new CancellationTokenSource();
        var waiter = session.GetAccessTokenAsync(waiterCancellation.Token);

        waiterCancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            waiter.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.AreEqual(1, api.RefreshCount);
        Assert.IsFalse(leader.IsCompleted);
        releaseRefresh.SetResult();
        Assert.AreEqual(
            "synthetic-refreshed-session",
            await leader.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.AreEqual(1, api.RefreshCount);
    }

    [TestMethod]
    public async Task ClearAsync_CancellationWhileWaitingPreservesRefreshedSession()
    {
        var refreshStarted = NewCompletionSource();
        var releaseRefresh = NewCompletionSource();
        var store = new FakeTokenStore("synthetic-stored-refresh");
        var api = new FakeAuthApi(async (_, _) =>
        {
            refreshStarted.SetResult();
            await releaseRefresh.Task;
            return "synthetic-refreshed-session";
        });
        var session = new AuthSession(api, store);
        var refresh = session.GetAccessTokenAsync(CancellationToken.None);
        await refreshStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var clearCancellation = new CancellationTokenSource();
        var clear = session.ClearAsync(clearCancellation.Token);

        clearCancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            clear.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.AreEqual(0, store.DeleteCount);
        releaseRefresh.SetResult();
        Assert.AreEqual(
            "synthetic-refreshed-session",
            await refresh.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.AreEqual("synthetic-refreshed-session", session.SessionToken);
        Assert.AreEqual("synthetic-stored-refresh", store.StoredToken);
    }

    [TestMethod]
    public async Task SetTokensAsync_SaveExceptionReleasesGateForNextSet()
    {
        var store = new FakeTokenStore
        {
            SaveException = Error(AppErrorKind.Storage)
        };
        var session = new AuthSession(new FakeAuthApi(), store);

        await Assert.ThrowsExactlyAsync<AppException>(() =>
            session.SetTokensAsync(
                new LoginTokens("synthetic-first-session", "synthetic-first-refresh"),
                CancellationToken.None));
        store.SaveException = null;

        await session.SetTokensAsync(
            new LoginTokens("synthetic-second-session", "synthetic-second-refresh"),
            CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.AreEqual("synthetic-second-session", session.SessionToken);
        Assert.AreEqual("synthetic-second-refresh", store.StoredToken);
    }

    [TestMethod]
    public async Task ClearAsync_DeleteExceptionReleasesGateForNextClear()
    {
        var failure = Error(AppErrorKind.Storage);
        var store = new FakeTokenStore();
        var session = new AuthSession(new FakeAuthApi(), store);
        await session.SetTokensAsync(
            new LoginTokens("synthetic-session", "synthetic-refresh"),
            CancellationToken.None);
        store.DeleteException = failure;

        await Assert.ThrowsExactlyAsync<AppException>(() =>
            session.ClearAsync(CancellationToken.None));
        store.DeleteException = null;

        await session.ClearAsync(CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.IsNull(session.SessionToken);
        Assert.IsNull(store.StoredToken);
        Assert.AreEqual(2, store.DeleteCount);
    }

    private static TaskCompletionSource NewCompletionSource()
    {
        return new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private static async Task<Exception?> CaptureFailureAsync(Task<string?> task)
    {
        try
        {
            await task.WaitAsync(TimeSpan.FromSeconds(5));
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private static T InvokeWithContext<T>(
        SynchronizationContext? context,
        Func<T> callback)
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(context);

        try
        {
            return callback();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    private static async Task RunNextAsync(QueuedSynchronizationContext context)
    {
        await context.WaitForCallbackAsync().WaitAsync(TimeSpan.FromSeconds(5));
        context.RunNext();
    }

    private static async Task PumpUntilCompletedAsync(
        QueuedSynchronizationContext context,
        Task task)
    {
        for (var index = 0; index < 10 && !task.IsCompleted; index++)
        {
            await RunNextAsync(context);
        }

        Assert.IsTrue(task.IsCompleted);
    }

    private static AppException Error(AppErrorKind kind, int? status = null)
    {
        return new AppException(kind, "Synthetic failure", status);
    }

    private sealed class FakeAuthApi : IAuthApi
    {
        private readonly Func<string, CancellationToken, Task<string>> _refreshAsync;
        private int _refreshCount;

        public FakeAuthApi(
            Func<string, CancellationToken, Task<string>>? refreshAsync = null)
        {
            _refreshAsync = refreshAsync ?? ((_, _) =>
                throw new AssertFailedException("RefreshAsync was not expected."));
        }

        public int RefreshCount => Volatile.Read(ref _refreshCount);

        public Task<LoginTokens> LoginAsync(
            string email,
            string passwordSha256,
            CancellationToken cancellationToken)
        {
            throw new AssertFailedException("LoginAsync was not expected.");
        }

        public Task<string> RefreshAsync(
            string refreshToken,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _refreshCount);
            return _refreshAsync(refreshToken, cancellationToken);
        }

        public void ResetCounts()
        {
            Interlocked.Exchange(ref _refreshCount, 0);
        }
    }

    private sealed class QueuedSynchronizationContext : SynchronizationContext
    {
        private readonly object _sync = new();
        private readonly Queue<(SendOrPostCallback Callback, object? State)> _callbacks = new();
        private TaskCompletionSource _callbackQueued = NewCompletionSource();

        public override void Post(SendOrPostCallback callback, object? state)
        {
            lock (_sync)
            {
                _callbacks.Enqueue((callback, state));
                _callbackQueued.TrySetResult();
            }
        }

        public Task WaitForCallbackAsync()
        {
            lock (_sync)
            {
                return _callbacks.Count > 0
                    ? Task.CompletedTask
                    : _callbackQueued.Task;
            }
        }

        public void RunNext()
        {
            (SendOrPostCallback Callback, object? State) callback;

            lock (_sync)
            {
                if (_callbacks.Count == 0)
                {
                    throw new InvalidOperationException("No callback is queued.");
                }

                callback = _callbacks.Dequeue();
                if (_callbacks.Count == 0)
                {
                    _callbackQueued = NewCompletionSource();
                }
            }

            InvokeWithContext(null, () =>
            {
                callback.Callback(callback.State);
                return true;
            });
        }
    }

    private sealed class FakeTokenStore : ITokenStore
    {
        private readonly object _sync = new();
        private string? _storedToken;
        private int _readCount;
        private int _saveCount;
        private int _deleteCount;

        public FakeTokenStore(string? storedToken = null)
        {
            _storedToken = storedToken;
        }

        public ConcurrentQueue<string> SavedTokens { get; } = new();

        public ConcurrentQueue<CancellationToken> DeleteCancellationTokens { get; } = new();

        public Func<string, CancellationToken, Task>? OnSaveAsync { get; set; }

        public Exception? SaveException { get; set; }

        public Exception? DeleteException { get; set; }

        public int ReadCount => Volatile.Read(ref _readCount);

        public int SaveCount => Volatile.Read(ref _saveCount);

        public int DeleteCount => Volatile.Read(ref _deleteCount);

        public string? StoredToken
        {
            get
            {
                lock (_sync)
                {
                    return _storedToken;
                }
            }
        }

        public Task<string?> ReadAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _readCount);
            return Task.FromResult(StoredToken);
        }

        public async Task SaveAsync(
            string refreshToken,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _saveCount);
            if (SaveException is not null)
            {
                throw SaveException;
            }

            if (OnSaveAsync is not null)
            {
                await OnSaveAsync(refreshToken, cancellationToken);
            }

            lock (_sync)
            {
                _storedToken = refreshToken;
            }

            SavedTokens.Enqueue(refreshToken);
        }

        public Task DeleteAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _deleteCount);
            DeleteCancellationTokens.Enqueue(cancellationToken);
            if (DeleteException is not null)
            {
                throw DeleteException;
            }

            lock (_sync)
            {
                _storedToken = null;
            }

            return Task.CompletedTask;
        }

        public void ResetCounts()
        {
            Interlocked.Exchange(ref _readCount, 0);
            Interlocked.Exchange(ref _saveCount, 0);
            Interlocked.Exchange(ref _deleteCount, 0);
        }
    }
}
