using NovelM_App.Application.Abstractions;
using NovelM_App.Domain.Auth;
using NovelM_App.Domain.Errors;

namespace NovelM_App.Application.Auth;

public sealed class AuthSession : IAuthSession
{
    private readonly IAuthApi _authApi;
    private readonly ITokenStore _tokenStore;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Task<string?>? _refreshTask;
    private string? _sessionToken;

    public AuthSession(IAuthApi authApi, ITokenStore tokenStore)
    {
        _authApi = authApi;
        _tokenStore = tokenStore;
    }

    public string? SessionToken => Volatile.Read(ref _sessionToken);

    public async Task SetTokensAsync(
        LoginTokens tokens,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            await _tokenStore.SaveAsync(tokens.RefreshToken, cancellationToken);
            Volatile.Write(ref _sessionToken, tokens.SessionToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<string?> GetAccessTokenAsync(
        CancellationToken cancellationToken)
    {
        var sessionToken = Volatile.Read(ref _sessionToken);
        if (sessionToken is not null)
        {
            return sessionToken;
        }

        cancellationToken.ThrowIfCancellationRequested();

        var refreshTask = Volatile.Read(ref _refreshTask);
        TaskCompletionSource<string?>? refreshCompletion = null;
        if (refreshTask is null)
        {
            var candidate = new TaskCompletionSource<string?>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            refreshTask = Interlocked.CompareExchange(
                ref _refreshTask,
                candidate.Task,
                null);

            if (refreshTask is null)
            {
                refreshTask = candidate.Task;
                refreshCompletion = candidate;
            }
        }

        if (refreshCompletion is not null)
        {
            await CompleteRefreshAsync(refreshCompletion, cancellationToken);
        }

        return await refreshTask.WaitAsync(cancellationToken);
    }

    public void InvalidateSessionToken()
    {
        Volatile.Write(ref _sessionToken, null);
    }

    public async Task ClearAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            Volatile.Write(ref _sessionToken, null);
            await _tokenStore.DeleteAsync(CancellationToken.None);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task CompleteRefreshAsync(
        TaskCompletionSource<string?> completion,
        CancellationToken cancellationToken)
    {
        var gateHeld = false;

        try
        {
            await _gate.WaitAsync(cancellationToken);
            gateHeld = true;

            var sessionToken = Volatile.Read(ref _sessionToken);
            if (sessionToken is null)
            {
                var refreshToken = await _tokenStore.ReadAsync(cancellationToken);
                if (!string.IsNullOrWhiteSpace(refreshToken))
                {
                    try
                    {
                        sessionToken = await _authApi.RefreshAsync(
                            refreshToken,
                            cancellationToken);
                        Volatile.Write(ref _sessionToken, sessionToken);
                    }
                    catch (AppException exception)
                        when (exception.Kind == AppErrorKind.Unauthorized)
                    {
                        Volatile.Write(ref _sessionToken, null);
                        await _tokenStore.DeleteAsync(CancellationToken.None);
                        sessionToken = null;
                    }
                }
            }

            completion.TrySetResult(sessionToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            completion.TrySetCanceled(cancellationToken);
        }
        catch (Exception exception)
        {
            completion.TrySetException(exception);
        }
        finally
        {
            _ = Interlocked.CompareExchange(
                ref _refreshTask,
                null,
                completion.Task);
            if (gateHeld)
            {
                _gate.Release();
            }
        }
    }
}
