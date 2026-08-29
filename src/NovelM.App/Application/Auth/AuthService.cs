using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using NovelM_App.Application.Abstractions;
using NovelM_App.Domain.Auth;
using NovelM_App.Domain.Errors;

namespace NovelM_App.Application.Auth;

public sealed class AuthService : IAuthService
{
    private readonly IAuthApi _authApi;
    private readonly IAuthSession _authSession;
    private readonly ISignalRConnection _signalRConnection;
    private readonly IUserApi _userApi;
    private readonly IDeviceIdStore _deviceIdStore;
    private readonly SemaphoreSlim _authLifecycleGate = new(1, 1);
    private readonly object _userStateLock = new();
    private UserProfile? _currentUser;
    private CancellationTokenSource _userOperationsCancellation = new();
    private long _userGeneration;
    private int _logoutCount;

    public AuthService(
        IAuthApi authApi,
        IAuthSession authSession,
        ISignalRConnection signalRConnection,
        IUserApi userApi,
        IDeviceIdStore deviceIdStore)
    {
        _authApi = authApi;
        _authSession = authSession;
        _signalRConnection = signalRConnection;
        _userApi = userApi;
        _deviceIdStore = deviceIdStore;
    }

    public UserProfile? CurrentUser => Volatile.Read(ref _currentUser);

    public async Task<UserProfile?> RestoreAsync(CancellationToken cancellationToken)
    {
        using var operation = BeginUserOperation(cancellationToken);
        var accessToken = await _authSession.GetAccessTokenAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return null;
        }

        await _authLifecycleGate.WaitAsync(cancellationToken);
        try
        {
            EnsureUserOperationIsActive(operation.Generation);
            await _signalRConnection.StartAsync(cancellationToken);
        }
        finally
        {
            _authLifecycleGate.Release();
        }

        var user = await _userApi.GetMyInfoAsync(operation.CancellationToken);
        CompleteUserOperation(operation.Generation, user);
        return user;
    }

    public Task<UserProfile> LoginAsync(
        string email,
        string rawPassword,
        CancellationToken cancellationToken)
    {
        var normalizedEmail = ValidateCredentials(email, rawPassword);
        string passwordSha256;
        var passwordBytes = Encoding.UTF8.GetBytes(rawPassword);
        try
        {
            var hashBytes = SHA256.HashData(passwordBytes);
            try
            {
                passwordSha256 = Convert.ToHexString(hashBytes).ToLowerInvariant();
            }
            finally
            {
                CryptographicOperations.ZeroMemory(hashBytes);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
        }

        var operation = BeginUserOperation(cancellationToken);
        return CompleteLoginAsync(
            normalizedEmail,
            passwordSha256,
            cancellationToken,
            operation);
    }

    private async Task<UserProfile> CompleteLoginAsync(
        string normalizedEmail,
        string passwordSha256,
        CancellationToken cancellationToken,
        UserOperation operation)
    {
        using (operation)
        {
            var tokens = await _authApi.LoginAsync(
                normalizedEmail,
                passwordSha256,
                cancellationToken);
            await _authLifecycleGate.WaitAsync(cancellationToken);
            try
            {
                EnsureUserOperationIsActive(operation.Generation);
                await _authSession.SetTokensAsync(tokens, cancellationToken);
                await _signalRConnection.RestartAsync(cancellationToken);
            }
            finally
            {
                _authLifecycleGate.Release();
            }

            var user = await _userApi.GetMyInfoAsync(operation.CancellationToken);
            CompleteUserOperation(operation.Generation, user);
            return user;
        }
    }

    public Task<UserProfile> LoginWithRefreshTokenAsync(
        string refreshToken,
        string deviceId,
        CancellationToken cancellationToken)
    {
        var normalizedRefreshToken =
            ImportedCredentialValidator.NormalizeRefreshToken(refreshToken);
        var normalizedDeviceId =
            ImportedCredentialValidator.NormalizeDeviceId(deviceId);
        var operation = BeginUserOperation(cancellationToken);
        return CompleteRefreshTokenLoginAsync(
            normalizedRefreshToken,
            normalizedDeviceId,
            operation);
    }

    private async Task<UserProfile> CompleteRefreshTokenLoginAsync(
        string refreshToken,
        string deviceId,
        UserOperation operation)
    {
        using (operation)
        {
            await _authLifecycleGate.WaitAsync(operation.CancellationToken);
            try
            {
                EnsureUserOperationIsActive(operation.Generation);
                await _deviceIdStore.SetAsync(
                    deviceId,
                    operation.CancellationToken);
                await _authSession.ImportRefreshTokenAsync(
                    refreshToken,
                    operation.CancellationToken);
                var accessToken = await _authSession.GetAccessTokenAsync(
                    operation.CancellationToken);
                if (string.IsNullOrWhiteSpace(accessToken))
                {
                    _authSession.InvalidateSessionToken();
                    throw new AppException(
                        AppErrorKind.Unauthorized,
                        "The imported refresh token could not establish a session.");
                }

                EnsureUserOperationIsActive(operation.Generation);
                await _signalRConnection.RestartAsync(operation.CancellationToken);
            }
            finally
            {
                _authLifecycleGate.Release();
            }

            var user = await _userApi.GetMyInfoAsync(operation.CancellationToken);
            CompleteUserOperation(operation.Generation, user);
            return user;
        }
    }

    public async Task LogoutAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        BeginLogout();
        Exception? failure = null;
        await _authLifecycleGate.WaitAsync(CancellationToken.None);

        try
        {
            try
            {
                await _authSession.ClearAsync(CancellationToken.None);
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            try
            {
                await _signalRConnection.StopAsync(CancellationToken.None);
            }
            catch (Exception exception)
            {
                failure ??= exception;
            }
        }
        finally
        {
            CompleteLogout();
            _authLifecycleGate.Release();
        }

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private UserOperation BeginUserOperation(CancellationToken cancellationToken)
    {
        lock (_userStateLock)
        {
            Volatile.Write(ref _currentUser, null);
            var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _userOperationsCancellation.Token);
            return new UserOperation(_userGeneration, linkedCancellation);
        }
    }

    private void CompleteUserOperation(long userGeneration, UserProfile user)
    {
        lock (_userStateLock)
        {
            if (_logoutCount != 0 || _userGeneration != userGeneration)
            {
                throw new OperationCanceledException(
                    "Authentication operation was superseded by logout.");
            }

            Volatile.Write(ref _currentUser, user);
        }
    }

    private void EnsureUserOperationIsActive(long userGeneration)
    {
        lock (_userStateLock)
        {
            if (_logoutCount != 0 || _userGeneration != userGeneration)
            {
                throw new OperationCanceledException(
                    "Authentication operation was superseded by logout.");
            }
        }
    }

    private void BeginLogout()
    {
        CancellationTokenSource operationsCancellation;
        lock (_userStateLock)
        {
            _userGeneration++;
            _logoutCount++;
            Volatile.Write(ref _currentUser, null);
            operationsCancellation = _userOperationsCancellation;
        }

        CancelUserOperations(operationsCancellation);
    }

    private void CompleteLogout()
    {
        CancellationTokenSource? completedOperations = null;
        lock (_userStateLock)
        {
            _userGeneration++;
            _logoutCount--;
            Volatile.Write(ref _currentUser, null);
            if (_logoutCount == 0)
            {
                completedOperations = _userOperationsCancellation;
                _userOperationsCancellation = new CancellationTokenSource();
            }
        }

        completedOperations?.Dispose();
    }

    private static void CancelUserOperations(
        CancellationTokenSource operationsCancellation)
    {
        try
        {
            operationsCancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
        catch (AggregateException)
        {
        }
    }

    private static string ValidateCredentials(string email, string rawPassword)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new AppException(AppErrorKind.Validation, "Email is required.");
        }

        var normalizedEmail = email.Trim();
        var atIndex = normalizedEmail.IndexOf('@');
        var dotIndex = normalizedEmail.LastIndexOf('.');
        if (atIndex <= 0
            || atIndex != normalizedEmail.LastIndexOf('@')
            || atIndex == normalizedEmail.Length - 1
            || dotIndex <= atIndex + 1
            || dotIndex == normalizedEmail.Length - 1
            || normalizedEmail.Any(char.IsWhiteSpace))
        {
            throw new AppException(AppErrorKind.Validation, "Email format is invalid.");
        }

        if (rawPassword is null || rawPassword.Length < 8)
        {
            throw new AppException(
                AppErrorKind.Validation,
                "Password must be at least 8 characters.");
        }

        return normalizedEmail;
    }

    private sealed class UserOperation : IDisposable
    {
        private readonly CancellationTokenSource _cancellation;

        public UserOperation(
            long generation,
            CancellationTokenSource cancellation)
        {
            Generation = generation;
            _cancellation = cancellation;
        }

        public long Generation { get; }

        public CancellationToken CancellationToken => _cancellation.Token;

        public void Dispose()
        {
            _cancellation.Dispose();
        }
    }
}
