using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using NovelM.Tests.TestSupport;
using NovelM_App.Application.Abstractions;
using NovelM_App.Domain.Configuration;
using NovelM_App.Domain.Connection;
using NovelM_App.Domain.Errors;
using NovelM_App.Infrastructure.SignalR;
using SignalRRetryContext = Microsoft.AspNetCore.SignalR.Client.RetryContext;

namespace NovelM.Tests.Infrastructure;

[TestClass]
public sealed class SignalRConnectionTests
{
    [TestMethod]
    [DataRow(0L, 0)]
    [DataRow(1L, 5000)]
    [DataRow(2L, 10000)]
    [DataRow(3L, 20000)]
    [DataRow(4L, 30000)]
    [DataRow(10L, 30000)]
    public void RetryPolicy_ReturnsRequiredSchedule(long previousRetryCount, int milliseconds)
    {
        var policy = new SignalRRetryPolicy();
        var context = new SignalRRetryContext
        {
            PreviousRetryCount = previousRetryCount,
            ElapsedTime = TimeSpan.Zero,
            RetryReason = new InvalidOperationException("synthetic failure")
        };

        Assert.AreEqual(TimeSpan.FromMilliseconds(milliseconds), policy.NextRetryDelay(context));
    }

    [TestMethod]
    public async Task InvokeAsync_RealMessagePackHost_SendsExactArgumentsBearerAndDecodesGzipDto()
    {
        await using var host = await LocalSignalRHost.StartAsync()
            .WaitAsync(TimeSpan.FromSeconds(10));
        var auth = new FakeAuthSession("synthetic-access-token");
        var connection = CreateConnection(host, auth);

        try
        {
            var result = await connection.InvokeAsync<UserProfileDto>(
                HubMethodNames.GetMyInfo,
                null,
                CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

            Assert.AreEqual(11, result.Id);
            Assert.AreEqual("reader", result.UserName);
            Assert.AreEqual("avatar.png", result.Avatar);
            Assert.AreEqual("member", result.Role.Name);
            Assert.IsNull(host.State.FirstArgument);
            Assert.IsTrue(host.State.UseGzip);
            Assert.IsTrue(host.State.BearerTokens.Contains("synthetic-access-token"));
            Assert.AreEqual(1, host.State.NegotiateCount);
            Assert.AreEqual(1, host.State.ConnectedCount);
            Assert.AreEqual(1, host.State.InvocationCount);
        }
        finally
        {
            await StopAsync(connection);
        }
    }

    [TestMethod]
    public async Task InvokeAsync_GetBookInfo_SendsTypedRequestAndDecodesAllDtoFields()
    {
        await using var host = await LocalSignalRHost.StartAsync()
            .WaitAsync(TimeSpan.FromSeconds(10));
        var connection = CreateConnection(host, new FakeAuthSession("book-token"));

        try
        {
            var result = await connection.InvokeAsync<BookResponseDto>(
                HubMethodNames.GetBookInfo,
                new { Id = 7L },
                CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

            Assert.AreEqual(7L, result.Book.Id);
            Assert.AreEqual("SignalR Book", result.Book.Title);
            Assert.AreEqual("Writer", result.Book.Author);
            Assert.AreEqual("Legacy Writer", result.Book.Arthur);
            Assert.AreEqual("cover.png", result.Book.Cover);
            Assert.AreEqual("Integration fixture", result.Book.Introduction);
            Assert.AreEqual(2, result.Book.Chapter.Count);
            Assert.AreEqual(701L, result.Book.Chapter[0].Id);
            Assert.AreEqual("Opening", result.Book.Chapter[0].Title);
            Assert.AreEqual(702L, result.Book.Chapter[1].Id);
            Assert.AreEqual("Second", result.Book.Chapter[1].Title);

            var invocation = host.State.Invocations.Single();
            Assert.AreEqual(HubMethodNames.GetBookInfo, invocation.MethodName);
            Assert.IsTrue(invocation.UseGzip);
            var request = invocation.Request as LocalBookInfoRequest;
            Assert.IsNotNull(request);
            Assert.AreEqual(7L, request.Id);
        }
        finally
        {
            await StopAsync(connection);
        }
    }

    [TestMethod]
    public async Task InvokeAsync_GetNovelContent_SendsTypedRequestAndDecodesAllDtoFields()
    {
        await using var host = await LocalSignalRHost.StartAsync()
            .WaitAsync(TimeSpan.FromSeconds(10));
        var connection = CreateConnection(host, new FakeAuthSession("chapter-token"));

        try
        {
            var result = await connection.InvokeAsync<ChapterResponseDto>(
                HubMethodNames.GetNovelContent,
                new { Bid = 7L, SortNum = 2, Convert = (string?)null },
                CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

            Assert.AreEqual(702L, result.Chapter.Id);
            Assert.AreEqual(7L, result.Chapter.BookId);
            Assert.AreEqual(2, result.Chapter.SortNum);
            Assert.AreEqual("Second", result.Chapter.Title);
            Assert.AreEqual("Chapter body", result.Chapter.Content);

            var invocation = host.State.Invocations.Single();
            Assert.AreEqual(HubMethodNames.GetNovelContent, invocation.MethodName);
            Assert.IsTrue(invocation.UseGzip);
            var request = invocation.Request as LocalNovelContentRequest;
            Assert.IsNotNull(request);
            Assert.AreEqual(7L, request.Bid);
            Assert.AreEqual(2, request.SortNum);
            Assert.IsNull(request.Convert);
        }
        finally
        {
            await StopAsync(connection);
        }
    }

    [TestMethod]
    public async Task InvokeAsync_ServerEnvelopeFailure_ThrowsClassifiedServerError()
    {
        await using var host = await LocalSignalRHost.StartAsync(envelopeFailureStatus: 403)
            .WaitAsync(TimeSpan.FromSeconds(10));
        var auth = new FakeAuthSession("envelope-token");
        var connection = CreateConnection(host, auth);

        try
        {
            var exception = await Assert.ThrowsExactlyAsync<AppException>(() =>
                connection.InvokeAsync<UserProfileDto>(
                    HubMethodNames.GetMyInfo,
                    null,
                    CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5)));

            Assert.AreEqual(AppErrorKind.Server, exception.Kind);
            Assert.AreEqual(403, exception.Status);
            Assert.AreEqual("Synthetic envelope failure", exception.Message);
            Assert.AreEqual(1, host.State.InvocationCount);
            Assert.AreEqual(0, auth.InvalidateCount);
        }
        finally
        {
            await StopAsync(connection);
        }
    }

    [TestMethod]
    public async Task InvokeAsync_UnrelatedHubException_DoesNotInvalidateRestartOrRetry()
    {
        await using var host = await LocalSignalRHost.StartAsync(
            firstHubExceptionMessage: "synthetic backend failure")
            .WaitAsync(TimeSpan.FromSeconds(10));
        var auth = new FakeAuthSession("unrelated-token", "unused-refresh-token");
        var connection = CreateConnection(host, auth);

        try
        {
            var exception = await Assert.ThrowsExactlyAsync<HubException>(() =>
                connection.InvokeAsync<UserProfileDto>(
                    HubMethodNames.GetMyInfo,
                    null,
                    CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5)));

            StringAssert.Contains(exception.Message, "synthetic backend failure");
            Assert.AreEqual(1, host.State.InvocationCount);
            Assert.AreEqual(1, host.State.NegotiateCount);
            Assert.AreEqual(0, auth.InvalidateCount);
            Assert.AreEqual(0, auth.RefreshCount);
        }
        finally
        {
            await StopAsync(connection);
        }
    }

    [TestMethod]
    public async Task StartAndStopAsync_PublishesConnectionStateEvents()
    {
        await using var host = await LocalSignalRHost.StartAsync()
            .WaitAsync(TimeSpan.FromSeconds(10));
        var connection = CreateConnection(host, new FakeAuthSession("state-token"));
        var states = new ConcurrentQueue<ConnectionState>();
        var connected = NewCompletionSource();
        var disconnected = NewCompletionSource();
        connection.StateChanged += (_, state) =>
        {
            states.Enqueue(state);
            if (state == ConnectionState.Connected)
            {
                connected.TrySetResult();
            }
            else if (state == ConnectionState.Disconnected)
            {
                disconnected.TrySetResult();
            }
        };

        try
        {
            await connection.StartAsync(CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(5));
            await connected.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.AreEqual(ConnectionState.Connected, connection.State);
            CollectionAssert.Contains(states.ToArray(), ConnectionState.Connecting);
            CollectionAssert.Contains(states.ToArray(), ConnectionState.Connected);

            await connection.StopAsync(CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(5));
            await disconnected.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.AreEqual(ConnectionState.Disconnected, connection.State);
            CollectionAssert.AreEqual(
                new[]
                {
                    ConnectionState.Connecting,
                    ConnectionState.Connected,
                    ConnectionState.Disconnected
                },
                states.ToArray());
        }
        finally
        {
            await StopAsync(connection);
        }
    }

    [TestMethod]
    public async Task RestartAsync_IgnoresCallbacksCapturedFromReplacedHub()
    {
        await using var host = await LocalSignalRHost.StartAsync()
            .WaitAsync(TimeSpan.FromSeconds(10));
        var connection = CreateConnection(host, new FakeAuthSession("stale-callback-token"));

        try
        {
            await connection.StartAsync(CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(5));
            var replacedHub = GetActiveHub(connection);
            var staleReconnecting = GetEventHandler<Func<Exception?, Task>>(
                replacedHub,
                "Reconnecting");
            var staleReconnected = GetEventHandler<Func<string?, Task>>(
                replacedHub,
                "Reconnected");
            var staleClosed = GetEventHandler<Func<Exception?, Task>>(
                replacedHub,
                "Closed");

            await connection.RestartAsync(CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(5));
            var activeHub = GetActiveHub(connection);
            var activeReconnecting = GetEventHandler<Func<Exception?, Task>>(
                activeHub,
                "Reconnecting");
            var activeReconnected = GetEventHandler<Func<string?, Task>>(
                activeHub,
                "Reconnected");
            Assert.AreNotSame(replacedHub, activeHub);
            Assert.AreEqual(ConnectionState.Connected, connection.State);

            await staleReconnecting(null).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.AreEqual(ConnectionState.Connected, connection.State);
            await activeReconnecting(null).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.AreEqual(ConnectionState.Reconnecting, connection.State);
            await staleReconnected("replaced-connection").WaitAsync(TimeSpan.FromSeconds(5));
            Assert.AreEqual(ConnectionState.Reconnecting, connection.State);
            await activeReconnected("active-connection").WaitAsync(TimeSpan.FromSeconds(5));
            Assert.AreEqual(ConnectionState.Connected, connection.State);
            await staleClosed(null).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.AreEqual(ConnectionState.Connected, connection.State);
        }
        finally
        {
            await StopAsync(connection);
        }
    }

    [TestMethod]
    public async Task StartAsync_DisposesTerminallyClosedHubBeforeStartingReplacement()
    {
        await using var host = await LocalSignalRHost.StartAsync()
            .WaitAsync(TimeSpan.FromSeconds(10));
        var connection = CreateConnection(host, new FakeAuthSession("closed-replacement-token"));
        HubConnection? closedHub = null;
        var disconnected = NewCompletionSource();
        connection.StateChanged += (_, state) =>
        {
            if (state == ConnectionState.Disconnected)
            {
                disconnected.TrySetResult();
            }
        };

        try
        {
            await connection.StartAsync(CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(5));
            closedHub = GetActiveHub(connection);
            await closedHub.StopAsync(CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(5));
            await disconnected.Task.WaitAsync(TimeSpan.FromSeconds(5));

            await connection.StartAsync(CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(5));
            using var connectedCancellation =
                new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await host.State.WaitForConnectedCountAsync(2, connectedCancellation.Token)
                .WaitAsync(TimeSpan.FromSeconds(5));
            Assert.AreNotSame(closedHub, GetActiveHub(connection));
            Assert.AreEqual(ConnectionState.Connected, connection.State);
            Assert.AreEqual(2, host.State.ConnectedCount);

            await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() =>
                closedHub.StartAsync(CancellationToken.None)
                    .WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.AreEqual(2, host.State.ConnectedCount);
        }
        finally
        {
            if (closedHub is not null)
            {
                await closedHub.DisposeAsync().AsTask()
                    .WaitAsync(TimeSpan.FromSeconds(5));
            }

            await StopAsync(connection);
        }
    }

    [TestMethod]
    public async Task StartAsync_WhenAlreadyConnected_IsIdempotent()
    {
        await using var host = await LocalSignalRHost.StartAsync()
            .WaitAsync(TimeSpan.FromSeconds(10));
        var connection = CreateConnection(host, new FakeAuthSession("idempotent-token"));

        try
        {
            await connection.StartAsync(CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(5));
            await connection.StartAsync(CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(5));

            Assert.AreEqual(ConnectionState.Connected, connection.State);
            Assert.AreEqual(1, host.State.NegotiateCount);
            Assert.AreEqual(1, host.State.ConnectedCount);
        }
        finally
        {
            await StopAsync(connection);
        }
    }

    [TestMethod]
    public async Task ConcurrentRestartStopStart_AreSerializedAndLeaveOneConnection()
    {
        await using var host = await LocalSignalRHost.StartAsync()
            .WaitAsync(TimeSpan.FromSeconds(10));
        var connection = CreateConnection(host, new FakeAuthSession("serialized-token"));

        try
        {
            await connection.StartAsync(CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(5));

            var restart = connection.RestartAsync(CancellationToken.None);
            var stop = connection.StopAsync(CancellationToken.None);
            var start = connection.StartAsync(CancellationToken.None);
            await Task.WhenAll(restart, stop, start)
                .WaitAsync(TimeSpan.FromSeconds(10));

            Assert.AreEqual(ConnectionState.Connected, connection.State);
            Assert.AreEqual(3, host.State.NegotiateCount);
            Assert.AreEqual(3, host.State.ConnectedCount);
        }
        finally
        {
            await StopAsync(connection);
        }
    }

    [TestMethod]
    public async Task StartAsync_FirstFailure_RethrowsAndSingleBackgroundLoopReconnects()
    {
        var port = GetUnusedTcpPort();
        var retryDelay = new ControlledRetryDelay();
        var connection = CreateConnection(
            new Uri($"http://127.0.0.1:{port}"),
            new FakeAuthSession("retry-token"),
            retryDelay.DelayAsync);
        var states = new ConcurrentQueue<ConnectionState>();
        var connected = NewCompletionSource();
        connection.StateChanged += (_, state) =>
        {
            states.Enqueue(state);
            if (state == ConnectionState.Connected)
            {
                connected.TrySetResult();
            }
        };

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            connection.StartAsync(CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(5)));

        var firstDelay = await retryDelay.NextAsync()
            .WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual(TimeSpan.Zero, firstDelay.Delay);

        await Task.WhenAll(
            connection.StartAsync(CancellationToken.None),
            connection.StartAsync(CancellationToken.None))
            .WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual(1, retryDelay.RequestCount);

        await using var host = await LocalSignalRHost.StartAsync(port: port)
            .WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            firstDelay.Release();
            await connected.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.AreEqual(ConnectionState.Connected, connection.State);
            CollectionAssert.Contains(states.ToArray(), ConnectionState.Failed);
            CollectionAssert.Contains(states.ToArray(), ConnectionState.Reconnecting);
            CollectionAssert.Contains(states.ToArray(), ConnectionState.Connected);
            Assert.AreEqual(1, host.State.NegotiateCount);
            Assert.AreEqual(1, host.State.ConnectedCount);
        }
        finally
        {
            await StopAsync(connection);
        }
    }

    [TestMethod]
    public async Task StopAsync_CancelsAndObservesPendingInitialRetryLoop()
    {
        var retryDelay = new ControlledRetryDelay();
        var connection = CreateConnection(
            new Uri($"http://127.0.0.1:{GetUnusedTcpPort()}"),
            new FakeAuthSession("stop-retry-token"),
            retryDelay.DelayAsync);

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            connection.StartAsync(CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(5)));
        var pendingDelay = await retryDelay.NextAsync()
            .WaitAsync(TimeSpan.FromSeconds(5));

        await connection.StopAsync(CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5));
        await pendingDelay.Completed.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.AreEqual(ConnectionState.Disconnected, connection.State);
        Assert.AreEqual(1, retryDelay.RequestCount);
    }

    [TestMethod]
    public async Task StartAsync_BackgroundLoopUsesRequiredUnboundedRetrySchedule()
    {
        var retryDelay = new ControlledRetryDelay();
        var connection = CreateConnection(
            new Uri($"http://127.0.0.1:{GetUnusedTcpPort()}"),
            new FakeAuthSession("schedule-token"),
            retryDelay.DelayAsync);
        var expected = new[] { 0, 5, 10, 20, 30, 30 };

        try
        {
            await Assert.ThrowsAsync<HttpRequestException>(() =>
                connection.StartAsync(CancellationToken.None)
                    .WaitAsync(TimeSpan.FromSeconds(5)));

            for (var index = 0; index < expected.Length; index++)
            {
                var request = await retryDelay.NextAsync()
                    .WaitAsync(TimeSpan.FromSeconds(5));
                Assert.AreEqual(TimeSpan.FromSeconds(expected[index]), request.Delay);
                if (index < expected.Length - 1)
                {
                    request.Release();
                }
            }
        }
        finally
        {
            await StopAsync(connection);
        }
    }

    [TestMethod]
    public async Task RestartAsync_CancelsPendingRetryLoopBeforeStartingReplacement()
    {
        var port = GetUnusedTcpPort();
        var retryDelay = new ControlledRetryDelay();
        var connection = CreateConnection(
            new Uri($"http://127.0.0.1:{port}"),
            new FakeAuthSession("restart-retry-token"),
            retryDelay.DelayAsync);

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            connection.StartAsync(CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(5)));
        var pendingDelay = await retryDelay.NextAsync()
            .WaitAsync(TimeSpan.FromSeconds(5));

        await using var host = await LocalSignalRHost.StartAsync(port: port)
            .WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            await connection.RestartAsync(CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(5));
            await pendingDelay.Completed.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.AreEqual(ConnectionState.Connected, connection.State);
            Assert.AreEqual(1, retryDelay.RequestCount);
            Assert.AreEqual(1, host.State.NegotiateCount);
            Assert.AreEqual(1, host.State.ConnectedCount);
        }
        finally
        {
            await StopAsync(connection);
        }
    }

    [TestMethod]
    public async Task RestartAsync_ReplacementFirstStartFailure_RethrowsAndRetriesInBackground()
    {
        var port = GetUnusedTcpPort();
        var retryDelay = new ControlledRetryDelay();
        var connection = CreateConnection(
            new Uri($"http://127.0.0.1:{port}"),
            new FakeAuthSession("replacement-token"),
            retryDelay.DelayAsync);
        var states = new ConcurrentQueue<ConnectionState>();
        var failed = NewCompletionSource();
        var connected = NewCompletionSource();
        connection.StateChanged += (_, state) =>
        {
            states.Enqueue(state);
            if (state == ConnectionState.Failed)
            {
                failed.TrySetResult();
            }
            else if (state == ConnectionState.Connected)
            {
                connected.TrySetResult();
            }
        };

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            connection.RestartAsync(CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(5)));
        await failed.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var firstDelay = await retryDelay.NextAsync()
            .WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual(TimeSpan.Zero, firstDelay.Delay);
        Assert.AreEqual(1, retryDelay.RequestCount);

        await using var host = await LocalSignalRHost.StartAsync(port: port)
            .WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            firstDelay.Release();
            await connected.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.AreEqual(ConnectionState.Connected, connection.State);
            CollectionAssert.AreEqual(
                new[]
                {
                    ConnectionState.Connecting,
                    ConnectionState.Failed,
                    ConnectionState.Reconnecting,
                    ConnectionState.Connected
                },
                states.ToArray());
            Assert.AreEqual(1, host.State.NegotiateCount);
            Assert.AreEqual(1, host.State.ConnectedCount);
        }
        finally
        {
            await StopAsync(connection);
        }
    }

    [TestMethod]
    public async Task StopAsync_CancelsRetryThatIsBlockedInsideHubStart()
    {
        var port = GetUnusedTcpPort();
        var retryDelay = new ControlledRetryDelay();
        var connection = CreateConnection(
            new Uri($"http://127.0.0.1:{port}"),
            new FakeAuthSession("blocked-start-token"),
            retryDelay.DelayAsync);

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            connection.StartAsync(CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(5)));
        var retry = await retryDelay.NextAsync()
            .WaitAsync(TimeSpan.FromSeconds(5));

        await using var host = await LocalSignalRHost.StartAsync(
            blockNegotiate: true,
            port: port).WaitAsync(TimeSpan.FromSeconds(10));
        retry.Release();
        await host.State.NegotiateStarted.WaitAsync(TimeSpan.FromSeconds(5));

        using var stopCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await connection.StopAsync(stopCancellation.Token)
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.AreEqual(ConnectionState.Disconnected, connection.State);
    }

    [TestMethod]
    public async Task StopAsync_CancelsRetryBlockedPreparingAuthentication()
    {
        var retryDelay = new ControlledRetryDelay();
        var auth = new FakeAuthSession("blocked-auth-token");
        var connection = CreateConnection(
            new Uri($"http://127.0.0.1:{GetUnusedTcpPort()}"),
            auth,
            retryDelay.DelayAsync);

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            connection.StartAsync(CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(5)));
        var retry = await retryDelay.NextAsync()
            .WaitAsync(TimeSpan.FromSeconds(5));
        auth.BlockAccessTokenRequests();
        retry.Release();
        await auth.BlockedAccessTokenRequestEntered
            .WaitAsync(TimeSpan.FromSeconds(5));

        try
        {
            using var stopCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await connection.StopAsync(stopCancellation.Token)
                .WaitAsync(TimeSpan.FromSeconds(6));

            var requestTokens = auth.AccessTokenRequestTokens.ToArray();
            Assert.AreEqual(3, auth.GetAccessTokenCount);
            Assert.AreEqual(3, requestTokens.Length);
            Assert.IsFalse(requestTokens[0].CanBeCanceled);
            Assert.IsFalse(requestTokens[1].CanBeCanceled);
            Assert.IsTrue(requestTokens[2].CanBeCanceled);
            Assert.AreEqual(ConnectionState.Disconnected, connection.State);
        }
        finally
        {
            auth.ReleaseAccessTokenRequests();
            await StopAsync(connection);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task AlreadyCanceledStopOrRestart_DoesNotCancelPendingRetry(
        bool restart)
    {
        var retryDelay = new ControlledRetryDelay();
        var connection = CreateConnection(
            new Uri($"http://127.0.0.1:{GetUnusedTcpPort()}"),
            new FakeAuthSession("already-canceled-token"),
            retryDelay.DelayAsync);

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            connection.StartAsync(CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(5)));
        var pendingRetry = await retryDelay.NextAsync()
            .WaitAsync(TimeSpan.FromSeconds(5));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        try
        {
            await Assert.ThrowsAsync<OperationCanceledException>(() =>
                restart
                    ? connection.RestartAsync(cancellation.Token)
                    : connection.StopAsync(cancellation.Token));

            Assert.IsFalse(pendingRetry.Completed.Task.IsCompleted);
        }
        finally
        {
            await StopAsync(connection);
        }
    }

    [TestMethod]
    public async Task StateChanged_BlockingAndThrowingSubscribers_DoNotBlockLifecycle()
    {
        await using var host = await LocalSignalRHost.StartAsync()
            .WaitAsync(TimeSpan.FromSeconds(10));
        var connection = CreateConnection(host, new FakeAuthSession("callback-token"));
        var callbackStarted = NewCompletionSource();
        var releaseCallback = NewCompletionSource();
        var connectedDelivered = NewCompletionSource();
        var states = new ConcurrentQueue<ConnectionState>();
        connection.StateChanged += (_, state) =>
        {
            states.Enqueue(state);
            if (state == ConnectionState.Connecting)
            {
                callbackStarted.TrySetResult();
                releaseCallback.Task.GetAwaiter().GetResult();
            }
            else if (state == ConnectionState.Connected)
            {
                connectedDelivered.TrySetResult();
            }
        };
        connection.StateChanged += (_, _) =>
            throw new InvalidOperationException("Synthetic subscriber failure");

        try
        {
            await connection.StartAsync(CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(5));
            await callbackStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.AreEqual(ConnectionState.Connected, connection.State);
            Assert.IsFalse(connectedDelivered.Task.IsCompleted);
            releaseCallback.TrySetResult();
            await connectedDelivered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            CollectionAssert.AreEqual(
                new[] { ConnectionState.Connecting, ConnectionState.Connected },
                states.ToArray());
        }
        finally
        {
            releaseCallback.TrySetResult();
            await StopAsync(connection);
        }
    }

    [TestMethod]
    public async Task StateChanged_DoesNotRunOnCallerSynchronizationContext()
    {
        await using var host = await LocalSignalRHost.StartAsync()
            .WaitAsync(TimeSpan.FromSeconds(10));
        var connection = CreateConnection(host, new FakeAuthSession("context-token"));
        var callerContext = new ForwardingSynchronizationContext();
        var observedContext = new TaskCompletionSource<SynchronizationContext?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        connection.StateChanged += (_, state) =>
        {
            if (state == ConnectionState.Connecting)
            {
                observedContext.TrySetResult(SynchronizationContext.Current);
            }
        };

        Task start;
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(callerContext);
        try
        {
            start = connection.StartAsync(CancellationToken.None);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }

        try
        {
            await start.WaitAsync(TimeSpan.FromSeconds(5));
            var actualContext = await observedContext.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.AreNotSame(callerContext, actualContext);
        }
        finally
        {
            await StopAsync(connection);
        }
    }

    [TestMethod]
    public async Task InvokeAsync_UnauthorizedHubExceptionCaseInsensitive_RefreshesAndRetriesOnce()
    {
        await using var host = await LocalSignalRHost.StartAsync(
            firstHubExceptionMessage: "USER IS UNAUTHORIZED")
            .WaitAsync(TimeSpan.FromSeconds(10));
        var auth = new FakeAuthSession(
            "synthetic-original-token",
            "synthetic-refreshed-token");
        var connection = CreateConnection(host, auth);

        try
        {
            var result = await connection.InvokeAsync<UserProfileDto>(
                HubMethodNames.GetMyInfo,
                null,
                CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

            Assert.AreEqual("reader", result.UserName);
            Assert.AreEqual(1, auth.InvalidateCount);
            Assert.AreEqual(1, auth.RefreshCount);
            Assert.AreEqual(2, host.State.InvocationCount);
            Assert.AreEqual(2, host.State.NegotiateCount);
            Assert.AreEqual(2, host.State.ConnectedCount);
            AssertInvalidateRefreshOrder(auth);
            CollectionAssert.IsSubsetOf(
                new[] { "synthetic-original-token", "synthetic-refreshed-token" },
                host.State.BearerTokens.ToArray());
        }
        finally
        {
            await StopAsync(connection);
        }
    }

    [TestMethod]
    public async Task InvokeAsync_UnauthorizedEnvelope_RefreshesRestartsAndRetriesOnce()
    {
        await using var host = await LocalSignalRHost.StartAsync(envelopeFailureStatus: -100)
            .WaitAsync(TimeSpan.FromSeconds(10));
        var auth = new FakeAuthSession(
            "envelope-original-token",
            "envelope-refreshed-token");
        var connection = CreateConnection(host, auth);

        try
        {
            var result = await connection.InvokeAsync<UserProfileDto>(
                HubMethodNames.GetMyInfo,
                null,
                CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

            Assert.AreEqual("reader", result.UserName);
            Assert.AreEqual(1, auth.InvalidateCount);
            Assert.AreEqual(1, auth.RefreshCount);
            Assert.AreEqual(2, host.State.InvocationCount);
            Assert.AreEqual(2, host.State.NegotiateCount);
            AssertInvalidateRefreshOrder(auth);
        }
        finally
        {
            await StopAsync(connection);
        }
    }

    [TestMethod]
    public async Task InvokeAsync_UnauthorizedRefreshReturnsNull_ThrowsWithoutRestartOrRetry()
    {
        await using var host = await LocalSignalRHost.StartAsync(unauthorizedOnce: true)
            .WaitAsync(TimeSpan.FromSeconds(10));
        var auth = new FakeAuthSession("expired-token");
        var connection = CreateConnection(host, auth);

        try
        {
            var exception = await Assert.ThrowsExactlyAsync<AppException>(() =>
                connection.InvokeAsync<UserProfileDto>(
                    HubMethodNames.GetMyInfo,
                    null,
                    CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5)));

            Assert.AreEqual(AppErrorKind.Unauthorized, exception.Kind);
            Assert.AreEqual(1, auth.InvalidateCount);
            Assert.AreEqual(1, auth.RefreshCount);
            Assert.AreEqual(1, host.State.InvocationCount);
            Assert.AreEqual(1, host.State.NegotiateCount);
        }
        finally
        {
            await StopAsync(connection);
        }
    }

    [TestMethod]
    public async Task InvokeAsync_SecondUnauthorizedHubException_IsNotRetriedAgain()
    {
        await using var host = await LocalSignalRHost.StartAsync(
            firstHubExceptionMessage: "user is unauthorized",
            repeatHubException: true)
            .WaitAsync(TimeSpan.FromSeconds(10));
        var auth = new FakeAuthSession("first-token", "second-token");
        var connection = CreateConnection(host, auth);

        try
        {
            await Assert.ThrowsExactlyAsync<HubException>(() =>
                connection.InvokeAsync<UserProfileDto>(
                    HubMethodNames.GetMyInfo,
                    null,
                    CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5)));

            Assert.AreEqual(1, auth.InvalidateCount);
            Assert.AreEqual(1, auth.RefreshCount);
            Assert.AreEqual(2, host.State.InvocationCount);
            Assert.AreEqual(2, host.State.NegotiateCount);
            AssertInvalidateRefreshOrder(auth);
        }
        finally
        {
            await StopAsync(connection);
        }
    }

    private static void AssertInvalidateRefreshOrder(FakeAuthSession auth)
    {
        var operations = auth.Operations.ToArray();
        var invalidateIndex = Array.IndexOf(operations, "invalidate");
        var refreshIndex = Array.IndexOf(operations, "get:refresh");
        var restartTokenIndex = Array.FindIndex(
            operations,
            refreshIndex + 1,
            operation => operation == "get:cached");

        Assert.IsTrue(invalidateIndex >= 0);
        Assert.AreEqual(invalidateIndex + 1, refreshIndex);
        Assert.IsTrue(restartTokenIndex > refreshIndex);
    }

    private static TaskCompletionSource NewCompletionSource()
    {
        return new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private static Task StopAsync(SignalRConnection connection)
    {
        return connection.StopAsync(CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static HubConnection GetActiveHub(SignalRConnection connection)
    {
        var field = typeof(SignalRConnection).GetField(
            "_hub",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(field);
        var hub = field.GetValue(connection) as HubConnection;
        Assert.IsNotNull(hub);
        return hub;
    }

    private static TDelegate GetEventHandler<TDelegate>(
        HubConnection hub,
        string eventName)
        where TDelegate : Delegate
    {
        var field = typeof(HubConnection)
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .SingleOrDefault(candidate =>
                candidate.Name.Contains(eventName, StringComparison.OrdinalIgnoreCase)
                && typeof(TDelegate).IsAssignableFrom(candidate.FieldType));
        Assert.IsNotNull(
            field,
            $"No {eventName} delegate field was found. Fields: {string.Join(", ", typeof(HubConnection).GetFields(BindingFlags.Instance | BindingFlags.NonPublic).Select(candidate => candidate.Name))}");
        var handler = field.GetValue(hub) as TDelegate;
        Assert.IsNotNull(handler);
        return handler;
    }

    private static SignalRConnection CreateConnection(
        LocalSignalRHost host,
        FakeAuthSession authSession)
    {
        return CreateConnection(host.BaseUri, authSession);
    }

    private static SignalRConnection CreateConnection(
        Uri baseUri,
        FakeAuthSession authSession,
        Func<TimeSpan, CancellationToken, Task>? retryDelayAsync = null)
    {
        var serverManager = new FakeApiServerManager(baseUri);
        return retryDelayAsync is null
            ? new SignalRConnection(
                serverManager,
                authSession,
                new CompressedResponseDecoder(),
                new SignalRRetryPolicy())
            : new SignalRConnection(
                serverManager,
                authSession,
                new CompressedResponseDecoder(),
                new SignalRRetryPolicy(),
                retryDelayAsync);
    }

    private static int GetUnusedTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private sealed class ControlledRetryDelay
    {
        private readonly ConcurrentQueue<RetryDelayRequest> _requests = new();
        private readonly SemaphoreSlim _available = new(0);
        private int _requestCount;

        public int RequestCount => Volatile.Read(ref _requestCount);

        public async Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            var request = new RetryDelayRequest(delay);
            _requests.Enqueue(request);
            Interlocked.Increment(ref _requestCount);
            _available.Release();

            try
            {
                await request.Released.Task.WaitAsync(cancellationToken);
            }
            finally
            {
                request.Completed.TrySetResult();
            }
        }

        public async Task<RetryDelayRequest> NextAsync()
        {
            await _available.WaitAsync();
            return _requests.TryDequeue(out var request)
                ? request
                : throw new InvalidOperationException("No retry delay was queued.");
        }
    }

    private sealed class ForwardingSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback callback, object? state)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                var previousContext = Current;
                SetSynchronizationContext(this);
                try
                {
                    callback(state);
                }
                finally
                {
                    SetSynchronizationContext(previousContext);
                }
            });
        }
    }

    private sealed record RetryDelayRequest(TimeSpan Delay)
    {
        public TaskCompletionSource Released { get; } = NewCompletionSource();

        public TaskCompletionSource Completed { get; } = NewCompletionSource();

        public void Release()
        {
            Released.TrySetResult();
        }
    }

    private sealed class FakeApiServerManager : IApiServerManager
    {
        public FakeApiServerManager(Uri baseUri)
        {
            Current = new ApiServerOption("local", "Local", baseUri);
            Options = new[] { Current };
        }

        public ApiServerOption Current { get; }

        public IReadOnlyList<ApiServerOption> Options { get; }

        public event EventHandler<ApiServerOption>? CurrentChanged
        {
            add { }
            remove { }
        }

        public Task LoadAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task SelectAsync(string serverId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }

    private sealed class FakeAuthSession : IAuthSession
    {
        private readonly string? _refreshedToken;
        private readonly TaskCompletionSource _blockedAccessTokenRequestEntered =
            NewCompletionSource();
        private readonly TaskCompletionSource _releaseAccessTokenRequests =
            NewCompletionSource();
        private string? _sessionToken;
        private int _blockAccessTokenRequests;
        private int _invalidateCount;
        private int _getAccessTokenCount;
        private int _refreshCount;

        public FakeAuthSession(string? sessionToken, string? refreshedToken = null)
        {
            _sessionToken = sessionToken;
            _refreshedToken = refreshedToken;
        }

        public string? SessionToken => Volatile.Read(ref _sessionToken);

        public int InvalidateCount => Volatile.Read(ref _invalidateCount);

        public int GetAccessTokenCount => Volatile.Read(ref _getAccessTokenCount);

        public int RefreshCount => Volatile.Read(ref _refreshCount);

        public ConcurrentQueue<string> Operations { get; } = new();

        public ConcurrentQueue<CancellationToken> AccessTokenRequestTokens { get; } = new();

        public Task BlockedAccessTokenRequestEntered =>
            _blockedAccessTokenRequestEntered.Task;

        public Task SetTokensAsync(
            NovelM_App.Domain.Auth.LoginTokens tokens,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Volatile.Write(ref _sessionToken, tokens.SessionToken);
            return Task.CompletedTask;
        }

        public async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AccessTokenRequestTokens.Enqueue(cancellationToken);
            Interlocked.Increment(ref _getAccessTokenCount);
            if (Volatile.Read(ref _blockAccessTokenRequests) != 0)
            {
                _blockedAccessTokenRequestEntered.TrySetResult();
                await _releaseAccessTokenRequests.Task.WaitAsync(cancellationToken);
            }

            var token = Volatile.Read(ref _sessionToken);
            if (token is not null)
            {
                Operations.Enqueue("get:cached");
                return token;
            }

            Operations.Enqueue("get:refresh");
            Interlocked.Increment(ref _refreshCount);
            Volatile.Write(ref _sessionToken, _refreshedToken);
            return _refreshedToken;
        }

        public void BlockAccessTokenRequests()
        {
            Volatile.Write(ref _blockAccessTokenRequests, 1);
        }

        public void ReleaseAccessTokenRequests()
        {
            _releaseAccessTokenRequests.TrySetResult();
        }

        public void InvalidateSessionToken()
        {
            Operations.Enqueue("invalidate");
            Interlocked.Increment(ref _invalidateCount);
            Volatile.Write(ref _sessionToken, null);
        }

        public Task ClearAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Volatile.Write(ref _sessionToken, null);
            return Task.CompletedTask;
        }
    }
}
