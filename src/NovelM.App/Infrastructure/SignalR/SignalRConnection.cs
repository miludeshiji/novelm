using System.Runtime.ExceptionServices;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using MessagePack.Resolvers;
using NovelM_App.Application.Abstractions;
using NovelM_App.Domain.Connection;
using NovelM_App.Domain.Errors;

namespace NovelM_App.Infrastructure.SignalR;

internal sealed class SignalRConnection : ISignalRConnection
{
    private const string UnauthorizedMessage = "user is unauthorized";

    private readonly IApiServerManager _serverManager;
    private readonly IAuthSession _authSession;
    private readonly CompressedResponseDecoder _decoder;
    private readonly SignalRRetryPolicy _retryPolicy;
    private readonly Func<TimeSpan, CancellationToken, Task> _retryDelayAsync;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly object _stateNotificationLock = new();
    private HubConnection? _hub;
    private CancellationTokenSource? _initialRetryCancellation;
    private Task? _initialRetryTask;
    private int _state = (int)ConnectionState.Disconnected;
    private Task _stateNotificationTail = Task.CompletedTask;

    public SignalRConnection(
        IApiServerManager serverManager,
        IAuthSession authSession,
        CompressedResponseDecoder decoder,
        SignalRRetryPolicy retryPolicy)
        : this(serverManager, authSession, decoder, retryPolicy, Task.Delay)
    {
    }

    internal SignalRConnection(
        IApiServerManager serverManager,
        IAuthSession authSession,
        CompressedResponseDecoder decoder,
        SignalRRetryPolicy retryPolicy,
        Func<TimeSpan, CancellationToken, Task> retryDelayAsync)
    {
        _serverManager = serverManager;
        _authSession = authSession;
        _decoder = decoder;
        _retryPolicy = retryPolicy;
        _retryDelayAsync = retryDelayAsync;
    }

    public ConnectionState State => (ConnectionState)Volatile.Read(ref _state);

    public event EventHandler<ConnectionState>? StateChanged;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            if (ActiveHubIsRunning())
            {
                return;
            }

            if (_initialRetryTask is { IsCompleted: false })
            {
                return;
            }

            await StopUnderGateAsync(cancellationToken);
            _initialRetryCancellation?.Dispose();
            _initialRetryCancellation = null;
            _initialRetryTask = null;

            try
            {
                await StartUnderGateAsync(
                    cancellationToken,
                    ConnectionState.Connecting,
                    publishFailure: true);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                StartInitialRetryLoopUnderGate(exception);
                throw;
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        CancellationTokenSource? retryCancellation = null;
        Task? retryTask = null;
        cancellationToken.ThrowIfCancellationRequested();
        CancelInitialRetryLoop();
        await _lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            (retryCancellation, retryTask) = DetachInitialRetryLoopUnderGate();
            retryCancellation?.Cancel();
            await StopUnderGateAsync(cancellationToken);
        }
        finally
        {
            _lifecycleGate.Release();
            await ObserveRetryLoopAsync(retryTask);
            retryCancellation?.Dispose();
        }
    }

    public async Task RestartAsync(CancellationToken cancellationToken)
    {
        CancellationTokenSource? retryCancellation = null;
        Task? retryTask = null;
        cancellationToken.ThrowIfCancellationRequested();
        CancelInitialRetryLoop();
        await _lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            (retryCancellation, retryTask) = DetachInitialRetryLoopUnderGate();
            retryCancellation?.Cancel();
            await StopUnderGateAsync(cancellationToken);
            try
            {
                await StartUnderGateAsync(
                    cancellationToken,
                    ConnectionState.Connecting,
                    publishFailure: true);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                StartInitialRetryLoopUnderGate(exception);
                throw;
            }
        }
        finally
        {
            _lifecycleGate.Release();
            await ObserveRetryLoopAsync(retryTask);
            retryCancellation?.Dispose();
        }
    }

    public Task<T> InvokeAsync<T>(
        string methodName,
        object? request,
        CancellationToken cancellationToken)
    {
        return InvokeWithUnauthorizedRetryAsync(
            () => InvokeCoreAsync<T>(methodName, request, cancellationToken),
            cancellationToken);
    }

    public async Task InvokeCommandAsync(
        string methodName,
        object? request,
        CancellationToken cancellationToken)
    {
        _ = await InvokeWithUnauthorizedRetryAsync(
            async () =>
            {
                await InvokeCommandCoreAsync(methodName, request, cancellationToken);
                return true;
            },
            cancellationToken);
    }

    private async Task<T> InvokeWithUnauthorizedRetryAsync<T>(
        Func<Task<T>> invokeAsync,
        CancellationToken cancellationToken)
    {
        await StartAsync(cancellationToken);

        try
        {
            return await invokeAsync();
        }
        catch (Exception exception) when (IsUnauthorized(exception))
        {
            _authSession.InvalidateSessionToken();
            var accessToken = await _authSession.GetAccessTokenAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                throw new AppException(
                    AppErrorKind.Unauthorized,
                    "The authentication session could not be refreshed.",
                    innerException: exception);
            }

            await RestartAsync(cancellationToken);
            try
            {
                return await invokeAsync();
            }
            catch (Exception retryException) when (IsUnauthorized(retryException))
            {
                if (retryException is AppException
                    {
                        Kind: AppErrorKind.Unauthorized
                    })
                {
                    throw;
                }

                throw new AppException(
                    AppErrorKind.Unauthorized,
                    "The authentication session remains unauthorized after refresh.",
                    innerException: retryException);
            }
        }
    }

    private async Task StartUnderGateAsync(
        CancellationToken cancellationToken,
        ConnectionState startingState,
        bool publishFailure)
    {
        var hub = CreateHub();
        SetActiveHubAndPublish(hub, startingState);

        try
        {
            await _authSession.GetAccessTokenAsync(cancellationToken);
            await hub.StartAsync(cancellationToken);
            PublishStateIfActiveHub(hub, ConnectionState.Connected);
        }
        catch
        {
            var wasActive = ClearActiveHub(hub);

            try
            {
                await hub.DisposeAsync();
            }
            catch
            {
            }

            if (publishFailure && wasActive)
            {
                PublishState(ConnectionState.Failed);
            }

            throw;
        }
    }

    private void StartInitialRetryLoopUnderGate(Exception firstFailure)
    {
        if (_initialRetryTask is { IsCompleted: false })
        {
            return;
        }

        _initialRetryCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _initialRetryCancellation = cancellation;
        _initialRetryTask = RunInitialRetryLoopAsync(firstFailure, cancellation.Token);
    }

    private async Task RunInitialRetryLoopAsync(
        Exception firstFailure,
        CancellationToken cancellationToken)
    {
        var retryReason = firstFailure;
        var elapsedTime = TimeSpan.Zero;

        try
        {
            for (long previousRetryCount = 0; ; previousRetryCount++)
            {
                PublishState(ConnectionState.Reconnecting);
                var retryDelay = _retryPolicy.NextRetryDelay(new RetryContext
                {
                    PreviousRetryCount = previousRetryCount,
                    ElapsedTime = elapsedTime,
                    RetryReason = retryReason
                }) ?? TimeSpan.FromSeconds(30);
                await _retryDelayAsync(retryDelay, cancellationToken);
                elapsedTime += retryDelay;

                await _lifecycleGate.WaitAsync(cancellationToken);
                try
                {
                    if (ActiveHubIsRunning())
                    {
                        return;
                    }

                    try
                    {
                        await StartUnderGateAsync(
                            cancellationToken,
                            ConnectionState.Reconnecting,
                            publishFailure: false);
                        return;
                    }
                    catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
                    {
                        retryReason = exception;
                    }
                }
                finally
                {
                    _lifecycleGate.Release();
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch
        {
            PublishState(ConnectionState.Failed);
        }
    }

    private (
        CancellationTokenSource? Cancellation,
        Task? Task) DetachInitialRetryLoopUnderGate()
    {
        var cancellation = _initialRetryCancellation;
        var task = _initialRetryTask;
        _initialRetryCancellation = null;
        _initialRetryTask = null;
        return (cancellation, task);
    }

    private void CancelInitialRetryLoop()
    {
        try
        {
            Volatile.Read(ref _initialRetryCancellation)?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private static async Task ObserveRetryLoopAsync(Task? retryTask)
    {
        if (retryTask is not null)
        {
            await retryTask;
        }
    }

    private async Task StopUnderGateAsync(CancellationToken cancellationToken)
    {
        var hub = DetachActiveHub();

        try
        {
            if (hub is not null)
            {
                Exception? stopException = null;
                try
                {
                    await hub.StopAsync(cancellationToken);
                }
                catch (Exception exception)
                {
                    stopException = exception;
                }

                try
                {
                    await hub.DisposeAsync();
                }
                catch when (stopException is not null)
                {
                }

                if (stopException is not null)
                {
                    ExceptionDispatchInfo.Capture(stopException).Throw();
                }
            }
        }
        finally
        {
            PublishState(ConnectionState.Disconnected);
        }
    }

    private HubConnection CreateHub()
    {
        var hub = new HubConnectionBuilder()
            .WithUrl(new Uri(_serverManager.Current.BaseUri, "/hub/api"), options =>
                options.AccessTokenProvider = () =>
                    _authSession.GetAccessTokenAsync(CancellationToken.None))
            .AddMessagePackProtocol(options =>
                options.SerializerOptions = options.SerializerOptions
                    .WithResolver(ContractlessStandardResolverAllowPrivate.Instance))
            .WithAutomaticReconnect(_retryPolicy)
            .Build();

        hub.Reconnecting += _ =>
        {
            PublishStateIfActiveHub(hub, ConnectionState.Reconnecting);
            return Task.CompletedTask;
        };
        hub.Reconnected += _ =>
        {
            PublishStateIfActiveHub(hub, ConnectionState.Connected);
            return Task.CompletedTask;
        };
        hub.Closed += _ =>
        {
            PublishStateIfActiveHub(hub, ConnectionState.Disconnected);
            return Task.CompletedTask;
        };

        return hub;
    }

    private async Task<T> InvokeCoreAsync<T>(
        string methodName,
        object? request,
        CancellationToken cancellationToken)
    {
        var envelope = await InvokeEnvelopeAsync(
            methodName,
            request,
            cancellationToken);
        return _decoder.Decode<T>(envelope, methodName);
    }

    private async Task InvokeCommandCoreAsync(
        string methodName,
        object? request,
        CancellationToken cancellationToken)
    {
        var envelope = await InvokeEnvelopeAsync(
            methodName,
            request,
            cancellationToken);
        _decoder.ValidateCommand(envelope, methodName);
    }

    private async Task<HubEnvelope<byte[]>> InvokeEnvelopeAsync(
        string methodName,
        object? request,
        CancellationToken cancellationToken)
    {
        var hub = GetActiveHub()
            ?? throw new AppException(
                AppErrorKind.Transport,
                "The SignalR connection is not available.");
        return await hub.InvokeCoreAsync<HubEnvelope<byte[]>>(
            methodName,
            new object?[] { request, new { UseGzip = true } },
            cancellationToken);
    }

    private static bool IsUnauthorized(Exception exception)
    {
        return exception is AppException { Kind: AppErrorKind.Unauthorized }
            || exception is HubException hubException
            && hubException.Message.Contains(
                UnauthorizedMessage,
                StringComparison.OrdinalIgnoreCase);
    }

    private void PublishState(ConnectionState state)
    {
        lock (_stateNotificationLock)
        {
            PublishStateUnderLock(state);
        }
    }

    private void SetActiveHubAndPublish(HubConnection hub, ConnectionState state)
    {
        lock (_stateNotificationLock)
        {
            _hub = hub;
            PublishStateUnderLock(state);
        }
    }

    private bool ClearActiveHub(HubConnection hub)
    {
        lock (_stateNotificationLock)
        {
            if (!ReferenceEquals(_hub, hub))
            {
                return false;
            }

            _hub = null;
            return true;
        }
    }

    private HubConnection? DetachActiveHub()
    {
        lock (_stateNotificationLock)
        {
            var hub = _hub;
            _hub = null;
            return hub;
        }
    }

    private HubConnection? GetActiveHub()
    {
        lock (_stateNotificationLock)
        {
            return _hub;
        }
    }

    private bool ActiveHubIsRunning()
    {
        lock (_stateNotificationLock)
        {
            return _hub?.State is HubConnectionState.Connected
                or HubConnectionState.Connecting
                or HubConnectionState.Reconnecting;
        }
    }

    private void PublishStateIfActiveHub(HubConnection hub, ConnectionState state)
    {
        lock (_stateNotificationLock)
        {
            if (ReferenceEquals(_hub, hub))
            {
                PublishStateUnderLock(state);
            }
        }
    }

    private void PublishStateUnderLock(ConnectionState state)
    {
        if ((ConnectionState)_state == state)
        {
            return;
        }

        Volatile.Write(ref _state, (int)state);
        var handlers = StateChanged;
        if (handlers is null)
        {
            return;
        }

        _stateNotificationTail = _stateNotificationTail.ContinueWith(
            _ => DispatchStateNotification(handlers, state),
            CancellationToken.None,
            TaskContinuationOptions.None,
            TaskScheduler.Default);
    }

    private void DispatchStateNotification(
        EventHandler<ConnectionState> handlers,
        ConnectionState state)
    {
        foreach (EventHandler<ConnectionState> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(this, state);
            }
            catch
            {
            }
        }
    }
}
