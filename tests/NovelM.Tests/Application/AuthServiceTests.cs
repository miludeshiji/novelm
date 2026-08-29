using System.Reflection;
using System.Runtime.CompilerServices;
using NovelM_App.Application.Abstractions;
using NovelM_App.Application.Auth;
using NovelM_App.Domain.Auth;
using NovelM_App.Domain.Connection;
using NovelM_App.Domain.Errors;

namespace NovelM.Tests.Application;

[TestClass]
public sealed class AuthServiceTests
{
    private const string RawPassword = "password123";
    private const string Password123Sha256 =
        "ef92b778bafe771e89245b89ecbc08a44a4e166c06659911881f383d4473e94f";

    [TestMethod]
    public async Task LoginAsync_ValidCredentials_HashesAndCallsDependenciesInOrder()
    {
        var fixture = CreateFixture();
        using var cancellation = new CancellationTokenSource();

        var result = await fixture.Service.LoginAsync(
            "  reader@example.com  ",
            RawPassword,
            cancellation.Token);

        Assert.AreSame(fixture.UserApi.Profile, result);
        Assert.AreSame(result, fixture.Service.CurrentUser);
        CollectionAssert.AreEqual(
            new[] { "login", "set-tokens", "restart", "get-my-info" },
            fixture.Operations.Select(operation => operation.Name).ToArray());
        Assert.AreEqual("reader@example.com", fixture.Operations[0].First);
        Assert.AreEqual(Password123Sha256, fixture.Operations[0].Second);
        Assert.AreSame(fixture.AuthApi.Tokens, fixture.Operations[1].First);
        Assert.IsTrue(fixture.Operations.Take(3).All(operation =>
            operation.CancellationToken == cancellation.Token));
        Assert.IsTrue(fixture.Operations[3].CancellationToken.CanBeCanceled);
        Assert.IsFalse(fixture.Operations[3].CancellationToken.IsCancellationRequested);
    }

    [TestMethod]
    public void Constructor_HasOnlyRequiredDependencies()
    {
        var constructor = typeof(AuthService).GetConstructors().Single();

        CollectionAssert.AreEqual(
            new[]
            {
                typeof(IAuthApi),
                typeof(IAuthSession),
                typeof(ISignalRConnection),
                typeof(IUserApi)
            },
            constructor.GetParameters()
                .Select(parameter => parameter.ParameterType)
                .ToArray());
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("readerexample.com")]
    [DataRow("@example.com")]
    [DataRow("reader@.com")]
    [DataRow("reader@examplecom")]
    [DataRow("reader@example.")]
    [DataRow("reader@example.com.")]
    [DataRow("reader@@example.com")]
    [DataRow("read er@example.com")]
    public async Task LoginAsync_InvalidEmail_RejectsBeforeDependencies(string? email)
    {
        var fixture = CreateFixture();

        var exception = await Assert.ThrowsExactlyAsync<AppException>(() =>
            fixture.Service.LoginAsync(email!, RawPassword, CancellationToken.None));

        Assert.AreEqual(AppErrorKind.Validation, exception.Kind);
        Assert.IsFalse(exception.Message.Contains(RawPassword, StringComparison.Ordinal));
        Assert.AreEqual(0, fixture.Operations.Count);
        Assert.IsNull(fixture.Service.CurrentUser);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("1234567")]
    public async Task LoginAsync_ShortOrMissingPassword_RejectsBeforeDependencies(
        string? rawPassword)
    {
        var fixture = CreateFixture();

        var exception = await Assert.ThrowsExactlyAsync<AppException>(() =>
            fixture.Service.LoginAsync(
                "reader@example.com",
                rawPassword!,
                CancellationToken.None));

        Assert.AreEqual(AppErrorKind.Validation, exception.Kind);
        if (!string.IsNullOrEmpty(rawPassword))
        {
            Assert.IsFalse(exception.Message.Contains(rawPassword, StringComparison.Ordinal));
        }

        Assert.AreEqual(0, fixture.Operations.Count);
        Assert.IsNull(fixture.Service.CurrentUser);
    }

    [TestMethod]
    public async Task LoginAsync_PasswordWhitespace_IsHashedWithoutModification()
    {
        const string rawPassword = " password123 ";
        var fixture = CreateFixture();
        var expectedHash = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(rawPassword)))
            .ToLowerInvariant();

        await fixture.Service.LoginAsync(
            "reader@example.com",
            rawPassword,
            CancellationToken.None);

        Assert.AreEqual(expectedHash, fixture.Operations[0].Second);
    }

    [TestMethod]
    public void LoginAsync_IsSynchronousEntryPoint()
    {
        var loginMethod = typeof(AuthService).GetMethod(nameof(AuthService.LoginAsync));
        Assert.IsNotNull(loginMethod);
        Assert.IsNull(loginMethod.GetCustomAttribute<AsyncStateMachineAttribute>());
    }

    [TestMethod]
    public void AuthService_ContainsNoRawPasswordStateFields()
    {
        var inspectedTypes = new[] { typeof(AuthService) }
            .Concat(typeof(AuthService).GetNestedTypes(
                BindingFlags.Public | BindingFlags.NonPublic));
        var rawPasswordFields = inspectedTypes
            .SelectMany(type => type.GetFields(
                BindingFlags.Instance
                | BindingFlags.Static
                | BindingFlags.Public
                | BindingFlags.NonPublic))
            .Where(field => field.Name.Contains(
                "rawPassword",
                StringComparison.OrdinalIgnoreCase))
            .Select(field => $"{field.DeclaringType?.FullName}.{field.Name}")
            .ToArray();

        CollectionAssert.AreEqual(Array.Empty<string>(), rawPasswordFields);
    }

    [TestMethod]
    public async Task LoginAsync_PublishesCurrentUserOnlyAfterGetMyInfoSucceeds()
    {
        var fixture = CreateFixture();
        var profileCompletion = new TaskCompletionSource<UserProfile>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.UserApi.Handler = _ => profileCompletion.Task;

        var login = fixture.Service.LoginAsync(
            "reader@example.com",
            RawPassword,
            CancellationToken.None);

        Assert.IsNull(fixture.Service.CurrentUser);
        Assert.IsFalse(login.IsCompleted);
        profileCompletion.SetResult(fixture.UserApi.Profile);
        Assert.AreSame(fixture.UserApi.Profile, await login);
        Assert.AreSame(fixture.UserApi.Profile, fixture.Service.CurrentUser);
    }

    [TestMethod]
    public async Task LoginAsync_GetMyInfoFails_KeepsTokensAndClearsPreviousUser()
    {
        var fixture = CreateFixture();
        await SeedCurrentUserAsync(fixture);
        var failure = Error(AppErrorKind.Transport);
        fixture.UserApi.Handler = _ => Task.FromException<UserProfile>(failure);

        var exception = await Assert.ThrowsExactlyAsync<AppException>(() =>
            fixture.Service.LoginAsync(
                "reader@example.com",
                RawPassword,
                CancellationToken.None));

        Assert.AreSame(failure, exception);
        CollectionAssert.AreEqual(
            new[] { "login", "set-tokens", "restart", "get-my-info" },
            fixture.Operations.Select(operation => operation.Name).ToArray());
        Assert.AreSame(fixture.AuthApi.Tokens, fixture.Session.LastTokens);
        Assert.AreEqual(0, fixture.Session.ClearCount);
        Assert.IsNull(fixture.Service.CurrentUser);
    }

    [TestMethod]
    public async Task LoginAsync_RestartFails_KeepsTokensAndDoesNotPublishUser()
    {
        var fixture = CreateFixture();
        await SeedCurrentUserAsync(fixture);
        var failure = Error(AppErrorKind.Transport);
        fixture.Connection.RestartException = failure;

        var exception = await Assert.ThrowsExactlyAsync<AppException>(() =>
            fixture.Service.LoginAsync(
                "reader@example.com",
                RawPassword,
                CancellationToken.None));

        Assert.AreSame(failure, exception);
        CollectionAssert.AreEqual(
            new[] { "login", "set-tokens", "restart" },
            fixture.Operations.Select(operation => operation.Name).ToArray());
        Assert.AreSame(fixture.AuthApi.Tokens, fixture.Session.LastTokens);
        Assert.AreEqual(0, fixture.Session.ClearCount);
        Assert.IsNull(fixture.Service.CurrentUser);
    }

    [TestMethod]
    public async Task RestoreAsync_AccessTokenExists_StartsAndPublishesUserInOrder()
    {
        var fixture = CreateFixture();
        fixture.Session.AccessToken = "synthetic-access-token";
        using var cancellation = new CancellationTokenSource();

        var result = await fixture.Service.RestoreAsync(cancellation.Token);

        Assert.AreSame(fixture.UserApi.Profile, result);
        Assert.AreSame(result, fixture.Service.CurrentUser);
        CollectionAssert.AreEqual(
            new[] { "get-access-token", "start", "get-my-info" },
            fixture.Operations.Select(operation => operation.Name).ToArray());
        Assert.IsTrue(fixture.Operations.Take(2).All(operation =>
            operation.CancellationToken == cancellation.Token));
        Assert.IsTrue(fixture.Operations[2].CancellationToken.CanBeCanceled);
        Assert.IsFalse(fixture.Operations[2].CancellationToken.IsCancellationRequested);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    public async Task RestoreAsync_NoAccessToken_ReturnsNullWithoutStarting(string? accessToken)
    {
        var fixture = CreateFixture();
        await SeedCurrentUserAsync(fixture);
        fixture.Session.AccessToken = accessToken;

        var result = await fixture.Service.RestoreAsync(CancellationToken.None);

        Assert.IsNull(result);
        Assert.IsNull(fixture.Service.CurrentUser);
        CollectionAssert.AreEqual(
            new[] { "get-access-token" },
            fixture.Operations.Select(operation => operation.Name).ToArray());
        Assert.AreEqual(0, fixture.Session.ClearCount);
    }

    [TestMethod]
    public async Task RestoreAsync_PublishesCurrentUserOnlyAfterGetMyInfoSucceeds()
    {
        var fixture = CreateFixture();
        fixture.Session.AccessToken = "synthetic-access-token";
        var profileCompletion = new TaskCompletionSource<UserProfile>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.UserApi.Handler = _ => profileCompletion.Task;

        var restore = fixture.Service.RestoreAsync(CancellationToken.None);

        Assert.IsNull(fixture.Service.CurrentUser);
        Assert.IsFalse(restore.IsCompleted);
        profileCompletion.SetResult(fixture.UserApi.Profile);
        Assert.AreSame(fixture.UserApi.Profile, await restore);
        Assert.AreSame(fixture.UserApi.Profile, fixture.Service.CurrentUser);
    }

    [TestMethod]
    public async Task LoginAsync_LogoutCancelsProfileRequestAndRejectsStaleResult()
    {
        var fixture = CreateFixture();
        var profileCompletion = new TaskCompletionSource<UserProfile>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var profileCancellation = CancellationToken.None;
        fixture.UserApi.Handler = cancellationToken =>
        {
            profileCancellation = cancellationToken;
            return profileCompletion.Task;
        };
        var login = fixture.Service.LoginAsync(
            "reader@example.com",
            RawPassword,
            CancellationToken.None);
        Assert.AreEqual("get-my-info", fixture.Operations[^1].Name);

        await fixture.Service.LogoutAsync(CancellationToken.None);
        profileCompletion.SetResult(fixture.UserApi.Profile);

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => login);
        Assert.IsTrue(profileCancellation.IsCancellationRequested);
        Assert.IsNull(fixture.Service.CurrentUser);
    }

    [TestMethod]
    public async Task RestoreAsync_LogoutCancelsProfileRequestAndRejectsStaleResult()
    {
        var fixture = CreateFixture();
        fixture.Session.AccessToken = "synthetic-access-token";
        var profileCompletion = new TaskCompletionSource<UserProfile>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var profileCancellation = CancellationToken.None;
        fixture.UserApi.Handler = cancellationToken =>
        {
            profileCancellation = cancellationToken;
            return profileCompletion.Task;
        };
        var restore = fixture.Service.RestoreAsync(CancellationToken.None);
        Assert.AreEqual("get-my-info", fixture.Operations[^1].Name);

        await fixture.Service.LogoutAsync(CancellationToken.None);
        profileCompletion.SetResult(fixture.UserApi.Profile);

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => restore);
        Assert.IsTrue(profileCancellation.IsCancellationRequested);
        Assert.IsNull(fixture.Service.CurrentUser);
    }

    [TestMethod]
    public async Task LoginAsync_StartedDuringLogout_RejectsBeforeChangingAuthState()
    {
        var fixture = CreateFixture();
        var clearEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseClear = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Session.ClearHandler = _ =>
        {
            clearEntered.SetResult();
            return releaseClear.Task;
        };
        var logout = fixture.Service.LogoutAsync(CancellationToken.None);
        await clearEntered.Task;

        var login = fixture.Service.LoginAsync(
            "reader@example.com",
            RawPassword,
            CancellationToken.None);
        Assert.IsFalse(login.IsCompleted);

        releaseClear.SetResult();
        await logout;

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => login);

        Assert.IsNull(fixture.Service.CurrentUser);
        CollectionAssert.AreEqual(
            new[] { "clear", "login", "stop" },
            fixture.Operations.Select(operation => operation.Name).ToArray());
        Assert.IsNull(fixture.Session.LastTokens);
    }

    [TestMethod]
    public async Task LoginAsync_AuthenticationCompletesAfterLogout_RejectsBeforeChangingAuthState()
    {
        var fixture = CreateFixture();
        var loginEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseLogin = new TaskCompletionSource<LoginTokens>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.AuthApi.LoginHandler = (_, _, _) =>
        {
            loginEntered.SetResult();
            return releaseLogin.Task;
        };
        var login = fixture.Service.LoginAsync(
            "reader@example.com",
            RawPassword,
            CancellationToken.None);
        await loginEntered.Task;

        await fixture.Service.LogoutAsync(CancellationToken.None);
        releaseLogin.SetResult(fixture.AuthApi.Tokens);

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => login);

        Assert.IsNull(fixture.Service.CurrentUser);
        CollectionAssert.AreEqual(
            new[] { "login", "clear", "stop" },
            fixture.Operations.Select(operation => operation.Name).ToArray());
        Assert.IsNull(fixture.Session.LastTokens);
    }

    [TestMethod]
    public async Task RestoreAsync_StartFails_KeepsRefreshAndClearsPreviousUser()
    {
        var fixture = CreateFixture();
        await SeedCurrentUserAsync(fixture);
        fixture.Session.AccessToken = "synthetic-access-token";
        var failure = Error(AppErrorKind.Transport);
        fixture.Connection.StartException = failure;

        var exception = await Assert.ThrowsExactlyAsync<AppException>(() =>
            fixture.Service.RestoreAsync(CancellationToken.None));

        Assert.AreSame(failure, exception);
        CollectionAssert.AreEqual(
            new[] { "get-access-token", "start" },
            fixture.Operations.Select(operation => operation.Name).ToArray());
        Assert.AreEqual(0, fixture.Session.ClearCount);
        Assert.IsNull(fixture.Service.CurrentUser);
    }

    [TestMethod]
    public async Task RestoreAsync_GetMyInfoFails_KeepsRefreshAndClearsPreviousUser()
    {
        var fixture = CreateFixture();
        await SeedCurrentUserAsync(fixture);
        fixture.Session.AccessToken = "synthetic-access-token";
        var failure = Error(AppErrorKind.Protocol);
        fixture.UserApi.Handler = _ => Task.FromException<UserProfile>(failure);

        var exception = await Assert.ThrowsExactlyAsync<AppException>(() =>
            fixture.Service.RestoreAsync(CancellationToken.None));

        Assert.AreSame(failure, exception);
        CollectionAssert.AreEqual(
            new[] { "get-access-token", "start", "get-my-info" },
            fixture.Operations.Select(operation => operation.Name).ToArray());
        Assert.AreEqual(0, fixture.Session.ClearCount);
        Assert.IsNull(fixture.Service.CurrentUser);
    }

    [TestMethod]
    public async Task LogoutAsync_ClearsUserAtCommitThenClearsAndStopsInOrder()
    {
        var fixture = CreateFixture();
        await SeedCurrentUserAsync(fixture);
        fixture.Session.ClearHandler = _ =>
        {
            Assert.IsNull(fixture.Service.CurrentUser);
            return Task.CompletedTask;
        };
        fixture.Connection.StopHandler = _ =>
        {
            Assert.IsNull(fixture.Service.CurrentUser);
            return Task.CompletedTask;
        };

        await fixture.Service.LogoutAsync(CancellationToken.None);

        CollectionAssert.AreEqual(
            new[] { "clear", "stop" },
            fixture.Operations.Select(operation => operation.Name).ToArray());
        Assert.IsNull(fixture.Service.CurrentUser);
    }

    [TestMethod]
    public async Task LogoutAsync_PreCanceled_LeavesUserAndDependenciesUnchanged()
    {
        var fixture = CreateFixture();
        await SeedCurrentUserAsync(fixture);
        var currentUser = fixture.Service.CurrentUser;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var exception = await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            fixture.Service.LogoutAsync(cancellation.Token));

        Assert.AreEqual(cancellation.Token, exception.CancellationToken);
        Assert.AreSame(currentUser, fixture.Service.CurrentUser);
        Assert.AreEqual(0, fixture.Session.ClearCount);
        Assert.AreEqual(0, fixture.Operations.Count);
    }

    [TestMethod]
    public async Task LogoutAsync_AfterCommit_IgnoresCallerCancellationAndCompletes()
    {
        var fixture = CreateFixture();
        await SeedCurrentUserAsync(fixture);
        using var cancellation = new CancellationTokenSource();
        fixture.Session.ClearHandler = dependencyToken =>
        {
            Assert.AreEqual(CancellationToken.None, dependencyToken);
            cancellation.Cancel();
            return Task.CompletedTask;
        };
        fixture.Connection.StopHandler = dependencyToken =>
        {
            Assert.AreEqual(CancellationToken.None, dependencyToken);
            return Task.CompletedTask;
        };

        await fixture.Service.LogoutAsync(cancellation.Token);

        CollectionAssert.AreEqual(
            new[] { "clear", "stop" },
            fixture.Operations.Select(operation => operation.Name).ToArray());
        Assert.IsTrue(fixture.Operations.All(operation =>
            operation.CancellationToken == CancellationToken.None));
        Assert.IsTrue(cancellation.IsCancellationRequested);
        Assert.IsNull(fixture.Service.CurrentUser);
    }

    [TestMethod]
    public async Task LogoutAsync_StopFails_PropagatesAndRemovesCurrentUser()
    {
        var fixture = CreateFixture();
        await SeedCurrentUserAsync(fixture);
        var failure = Error(AppErrorKind.Transport);
        fixture.Connection.StopException = failure;

        var exception = await Assert.ThrowsExactlyAsync<AppException>(() =>
            fixture.Service.LogoutAsync(CancellationToken.None));

        Assert.AreSame(failure, exception);
        CollectionAssert.AreEqual(
            new[] { "clear", "stop" },
            fixture.Operations.Select(operation => operation.Name).ToArray());
        Assert.IsNull(fixture.Service.CurrentUser);
    }

    [TestMethod]
    public async Task LogoutAsync_ClearFails_StillStopsPropagatesAndRemovesCurrentUser()
    {
        var fixture = CreateFixture();
        await SeedCurrentUserAsync(fixture);
        var failure = Error(AppErrorKind.Storage);
        fixture.Session.ClearException = failure;

        var exception = await Assert.ThrowsExactlyAsync<AppException>(() =>
            fixture.Service.LogoutAsync(CancellationToken.None));

        Assert.AreSame(failure, exception);
        CollectionAssert.AreEqual(
            new[] { "clear", "stop" },
            fixture.Operations.Select(operation => operation.Name).ToArray());
        Assert.IsNull(fixture.Service.CurrentUser);
    }

    private static Fixture CreateFixture()
    {
        var operations = new List<Operation>();
        var authApi = new FakeAuthApi(operations);
        var session = new FakeAuthSession(operations);
        var connection = new FakeSignalRConnection(operations);
        var userApi = new FakeUserApi(operations);
        return new Fixture(
            new AuthService(authApi, session, connection, userApi),
            authApi,
            session,
            connection,
            userApi,
            operations);
    }

    private static async Task SeedCurrentUserAsync(Fixture fixture)
    {
        await fixture.Service.LoginAsync(
            "reader@example.com",
            RawPassword,
            CancellationToken.None);
        Assert.IsNotNull(fixture.Service.CurrentUser);
        fixture.Operations.Clear();
    }

    private static AppException Error(AppErrorKind kind)
    {
        return new AppException(kind, "Synthetic safe failure");
    }

    private sealed record Fixture(
        AuthService Service,
        FakeAuthApi AuthApi,
        FakeAuthSession Session,
        FakeSignalRConnection Connection,
        FakeUserApi UserApi,
        List<Operation> Operations);

    private sealed record Operation(
        string Name,
        object? First,
        object? Second,
        CancellationToken CancellationToken);

    private sealed class FakeAuthApi : IAuthApi
    {
        private readonly List<Operation> _operations;

        public FakeAuthApi(List<Operation> operations)
        {
            _operations = operations;
        }

        public LoginTokens Tokens { get; } =
            new("synthetic-session-token", "synthetic-refresh-token");

        public Func<string, string, CancellationToken, Task<LoginTokens>>? LoginHandler
        {
            get;
            set;
        }

        public Task<LoginTokens> LoginAsync(
            string email,
            string passwordSha256,
            CancellationToken cancellationToken)
        {
            _operations.Add(new Operation(
                "login",
                email,
                passwordSha256,
                cancellationToken));
            return LoginHandler?.Invoke(email, passwordSha256, cancellationToken)
                ?? Task.FromResult(Tokens);
        }

        public Task<string> RefreshAsync(
            string refreshToken,
            CancellationToken cancellationToken)
        {
            throw new AssertFailedException("RefreshAsync was not expected.");
        }
    }

    private sealed class FakeAuthSession : IAuthSession
    {
        private readonly List<Operation> _operations;

        public FakeAuthSession(List<Operation> operations)
        {
            _operations = operations;
        }

        public string? SessionToken => LastTokens?.SessionToken;

        public string? AccessToken { get; set; }

        public LoginTokens? LastTokens { get; private set; }

        public int ClearCount { get; private set; }

        public Exception? ClearException { get; set; }

        public Func<CancellationToken, Task>? ClearHandler { get; set; }

        public Task SetTokensAsync(
            LoginTokens tokens,
            CancellationToken cancellationToken)
        {
            _operations.Add(new Operation(
                "set-tokens",
                tokens,
                null,
                cancellationToken));
            LastTokens = tokens;
            return Task.CompletedTask;
        }

        public Task ImportRefreshTokenAsync(
            string refreshToken,
            CancellationToken cancellationToken)
        {
            _operations.Add(new Operation(
                "import-refresh-token",
                refreshToken,
                null,
                cancellationToken));
            return Task.CompletedTask;
        }

        public Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken)
        {
            _operations.Add(new Operation(
                "get-access-token",
                null,
                null,
                cancellationToken));
            return Task.FromResult(AccessToken);
        }

        public void InvalidateSessionToken()
        {
            throw new AssertFailedException("InvalidateSessionToken was not expected.");
        }

        public Task ClearAsync(CancellationToken cancellationToken)
        {
            _operations.Add(new Operation("clear", null, null, cancellationToken));
            ClearCount++;
            if (ClearException is not null)
            {
                return Task.FromException(ClearException);
            }

            return ClearHandler?.Invoke(cancellationToken) ?? Task.CompletedTask;
        }
    }

    private sealed class FakeSignalRConnection : ISignalRConnection
    {
        private readonly List<Operation> _operations;

        public FakeSignalRConnection(List<Operation> operations)
        {
            _operations = operations;
        }

        public ConnectionState State => ConnectionState.Disconnected;

        public Exception? StartException { get; set; }

        public Exception? StopException { get; set; }

        public Exception? RestartException { get; set; }

        public Func<CancellationToken, Task>? StopHandler { get; set; }

        public event EventHandler<ConnectionState>? StateChanged
        {
            add { }
            remove { }
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _operations.Add(new Operation("start", null, null, cancellationToken));
            return StartException is null
                ? Task.CompletedTask
                : Task.FromException(StartException);
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _operations.Add(new Operation("stop", null, null, cancellationToken));
            if (StopException is not null)
            {
                return Task.FromException(StopException);
            }

            return StopHandler?.Invoke(cancellationToken) ?? Task.CompletedTask;
        }

        public Task RestartAsync(CancellationToken cancellationToken)
        {
            _operations.Add(new Operation("restart", null, null, cancellationToken));
            return RestartException is null
                ? Task.CompletedTask
                : Task.FromException(RestartException);
        }

        public Task<T> InvokeAsync<T>(
            string methodName,
            object? request,
            CancellationToken cancellationToken)
        {
            throw new AssertFailedException("InvokeAsync was not expected.");
        }
    }

    private sealed class FakeUserApi : IUserApi
    {
        private readonly List<Operation> _operations;

        public FakeUserApi(List<Operation> operations)
        {
            _operations = operations;
        }

        public UserProfile Profile { get; } =
            new(42, "reader", "avatar.png", "Member");

        public Func<CancellationToken, Task<UserProfile>>? Handler { get; set; }

        public Task<UserProfile> GetMyInfoAsync(CancellationToken cancellationToken)
        {
            _operations.Add(new Operation(
                "get-my-info",
                null,
                null,
                cancellationToken));
            return Handler?.Invoke(cancellationToken) ?? Task.FromResult(Profile);
        }
    }
}
