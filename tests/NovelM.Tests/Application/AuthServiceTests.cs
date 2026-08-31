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
                typeof(IUserApi),
                typeof(IDeviceIdStore)
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
    public async Task LoginWithRefreshTokenAsync_ValidInput_ReplacesCredentialsInOrder()
    {
        var fixture = CreateFixture();
        fixture.Session.AccessToken = "imported-session-token";

        var result = await fixture.Service.LoginWithRefreshTokenAsync(
            "  imported-refresh-token  ",
            "  web-fingerprint-id  ",
            CancellationToken.None);

        Assert.AreSame(fixture.UserApi.Profile, result);
        Assert.AreSame(result, fixture.Service.CurrentUser);
        CollectionAssert.AreEqual(
            new[]
            {
                "set-device-id",
                "import-refresh-token",
                "get-access-token",
                "restart",
                "get-my-info"
            },
            fixture.Operations.Select(operation => operation.Name).ToArray());
        Assert.AreEqual("web-fingerprint-id", fixture.DeviceIdStore.Current);
        Assert.AreEqual(
            "imported-refresh-token",
            fixture.Session.ImportedRefreshToken);
    }

    [TestMethod]
    public void LoginWithRefreshTokenAsync_IsSynchronousEntryPoint()
    {
        var method = typeof(AuthService).GetMethod(
            nameof(AuthService.LoginWithRefreshTokenAsync));

        Assert.IsNotNull(method);
        Assert.IsNull(method.GetCustomAttribute<AsyncStateMachineAttribute>());
    }

    [TestMethod]
    [DataRow("", "web-id")]
    [DataRow("token", "")]
    [DataRow("valid\rmalicious", "web-id")]
    [DataRow("token", "valid\rmalicious")]
    public async Task LoginWithRefreshTokenAsync_InvalidInputRejectsBeforeDependencies(
        string refreshToken,
        string deviceId)
    {
        var fixture = CreateFixture();

        var exception = await Assert.ThrowsExactlyAsync<AppException>(() =>
            fixture.Service.LoginWithRefreshTokenAsync(
                refreshToken,
                deviceId,
                CancellationToken.None));

        Assert.AreEqual(AppErrorKind.Validation, exception.Kind);
        if (refreshToken.Length > 0)
        {
            Assert.IsFalse(exception.Message.Contains(
                refreshToken,
                StringComparison.Ordinal));
        }

        if (deviceId.Length > 0)
        {
            Assert.IsFalse(exception.Message.Contains(
                deviceId,
                StringComparison.Ordinal));
        }

        Assert.HasCount(0, fixture.Operations);
        Assert.IsNull(fixture.Service.CurrentUser);
    }

    [TestMethod]
    public async Task LoginWithRefreshTokenAsync_OversizedInputRejectsBeforeDependencies()
    {
        foreach (var (refreshToken, deviceId) in new[]
                 {
                     (new string('r', 16_385), "web-id"),
                     ("token", new string('x', 257))
                 })
        {
            var fixture = CreateFixture();

            var exception = await Assert.ThrowsExactlyAsync<AppException>(() =>
                fixture.Service.LoginWithRefreshTokenAsync(
                    refreshToken,
                    deviceId,
                    CancellationToken.None));

            Assert.AreEqual(AppErrorKind.Validation, exception.Kind);
            Assert.HasCount(0, fixture.Operations);
        }
    }

    [TestMethod]
    public async Task LoginWithRefreshTokenAsync_DeviceSaveFails_DoesNotImportToken()
    {
        var fixture = CreateFixture();
        var failure = Error(AppErrorKind.Storage);
        fixture.DeviceIdStore.SetException = failure;

        var actual = await Assert.ThrowsExactlyAsync<AppException>(() =>
            fixture.Service.LoginWithRefreshTokenAsync(
                "imported-refresh",
                "web-id",
                CancellationToken.None));

        Assert.AreSame(failure, actual);
        CollectionAssert.AreEqual(
            new[] { "set-device-id" },
            fixture.Operations.Select(operation => operation.Name).ToArray());
        Assert.IsNull(fixture.Session.ImportedRefreshToken);
        Assert.IsNull(fixture.Service.CurrentUser);
    }

    [TestMethod]
    public async Task LoginWithRefreshTokenAsync_TokenImportFails_KeepsNewDeviceId()
    {
        var fixture = CreateFixture();
        var failure = Error(AppErrorKind.Storage);
        fixture.Session.ImportException = failure;

        var actual = await Assert.ThrowsExactlyAsync<AppException>(() =>
            fixture.Service.LoginWithRefreshTokenAsync(
                "imported-refresh",
                "web-id",
                CancellationToken.None));

        Assert.AreSame(failure, actual);
        CollectionAssert.AreEqual(
            new[] { "set-device-id", "import-refresh-token" },
            fixture.Operations.Select(operation => operation.Name).ToArray());
        Assert.AreEqual("web-id", fixture.DeviceIdStore.Current);
        Assert.IsNull(fixture.Session.ImportedRefreshToken);
        Assert.IsNull(fixture.Service.CurrentUser);
    }

    [TestMethod]
    public async Task LoginWithRefreshTokenAsync_RefreshFails_KeepsImportedCredentials()
    {
        var fixture = CreateFixture();
        var failure = Error(AppErrorKind.Transport);
        fixture.Session.GetAccessTokenException = failure;

        var actual = await Assert.ThrowsExactlyAsync<AppException>(() =>
            fixture.Service.LoginWithRefreshTokenAsync(
                "imported-refresh",
                "web-id",
                CancellationToken.None));

        Assert.AreSame(failure, actual);
        CollectionAssert.AreEqual(
            new[]
            {
                "set-device-id",
                "import-refresh-token",
                "get-access-token"
            },
            fixture.Operations.Select(operation => operation.Name).ToArray());
        Assert.AreEqual("web-id", fixture.DeviceIdStore.Current);
        Assert.AreEqual("imported-refresh", fixture.Session.ImportedRefreshToken);
        Assert.IsNull(fixture.Service.CurrentUser);
    }

    [TestMethod]
    public async Task LoginWithRefreshTokenAsync_NoSessionToken_ThrowsUnauthorizedAfterImport()
    {
        var fixture = CreateFixture();
        const string refreshToken = "imported-refresh-secret";

        var exception = await Assert.ThrowsExactlyAsync<AppException>(() =>
            fixture.Service.LoginWithRefreshTokenAsync(
                refreshToken,
                "web-id",
                CancellationToken.None));

        Assert.AreEqual(AppErrorKind.Unauthorized, exception.Kind);
        Assert.IsFalse(exception.Message.Contains(
            refreshToken,
            StringComparison.Ordinal));
        CollectionAssert.AreEqual(
            new[]
            {
                "set-device-id",
                "import-refresh-token",
                "get-access-token"
            },
            fixture.Operations.Select(operation => operation.Name).ToArray());
        Assert.AreEqual("web-id", fixture.DeviceIdStore.Current);
        Assert.AreEqual(refreshToken, fixture.Session.ImportedRefreshToken);
        Assert.IsNull(fixture.Service.CurrentUser);
    }

    [TestMethod]
    public async Task LoginWithRefreshTokenAsync_RestartFails_KeepsImportedCredentials()
    {
        var fixture = CreateFixture();
        fixture.Session.AccessToken = "imported-session";
        var failure = Error(AppErrorKind.Transport);
        fixture.Connection.RestartException = failure;

        var actual = await Assert.ThrowsExactlyAsync<AppException>(() =>
            fixture.Service.LoginWithRefreshTokenAsync(
                "imported-refresh",
                "web-id",
                CancellationToken.None));

        Assert.AreSame(failure, actual);
        CollectionAssert.AreEqual(
            new[]
            {
                "set-device-id",
                "import-refresh-token",
                "get-access-token",
                "restart"
            },
            fixture.Operations.Select(operation => operation.Name).ToArray());
        Assert.AreEqual("web-id", fixture.DeviceIdStore.Current);
        Assert.AreEqual("imported-refresh", fixture.Session.ImportedRefreshToken);
        Assert.IsNull(fixture.Service.CurrentUser);
    }

    [TestMethod]
    public async Task LoginWithRefreshTokenAsync_GetMyInfoFails_KeepsImportedCredentials()
    {
        var fixture = CreateFixture();
        fixture.Session.AccessToken = "imported-session";
        var failure = Error(AppErrorKind.Protocol);
        fixture.UserApi.Handler = _ => Task.FromException<UserProfile>(failure);

        var actual = await Assert.ThrowsExactlyAsync<AppException>(() =>
            fixture.Service.LoginWithRefreshTokenAsync(
                "imported-refresh",
                "web-id",
                CancellationToken.None));

        Assert.AreSame(failure, actual);
        CollectionAssert.AreEqual(
            new[]
            {
                "set-device-id",
                "import-refresh-token",
                "get-access-token",
                "restart",
                "get-my-info"
            },
            fixture.Operations.Select(operation => operation.Name).ToArray());
        Assert.AreEqual("web-id", fixture.DeviceIdStore.Current);
        Assert.AreEqual("imported-refresh", fixture.Session.ImportedRefreshToken);
        Assert.IsNull(fixture.Service.CurrentUser);
    }

    [TestMethod]
    public async Task LoginWithRefreshTokenAsync_LogoutRejectsStaleProfileResult()
    {
        var fixture = CreateFixture();
        fixture.Session.AccessToken = "imported-session";
        var profileCompletion = new TaskCompletionSource<UserProfile>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.UserApi.Handler = _ => profileCompletion.Task;

        var login = fixture.Service.LoginWithRefreshTokenAsync(
            "imported-refresh",
            "web-id",
            CancellationToken.None);
        Assert.AreEqual("get-my-info", fixture.Operations[^1].Name);

        await fixture.Service.LogoutAsync(CancellationToken.None);
        profileCompletion.SetResult(fixture.UserApi.Profile);

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => login);
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
        var deviceIdStore = new FakeDeviceIdStore(operations);
        return new Fixture(
            new AuthService(
                authApi,
                session,
                connection,
                userApi,
                deviceIdStore),
            authApi,
            session,
            connection,
            userApi,
            deviceIdStore,
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
        FakeDeviceIdStore DeviceIdStore,
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

        public string? ImportedRefreshToken { get; private set; }

        public int ClearCount { get; private set; }

        public Exception? ImportException { get; set; }

        public Exception? GetAccessTokenException { get; set; }

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
            if (ImportException is not null)
            {
                return Task.FromException(ImportException);
            }

            ImportedRefreshToken = refreshToken;
            LastTokens = null;
            return Task.CompletedTask;
        }

        public Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken)
        {
            _operations.Add(new Operation(
                "get-access-token",
                null,
                null,
                cancellationToken));
            return GetAccessTokenException is null
                ? Task.FromResult(AccessToken)
                : Task.FromException<string?>(GetAccessTokenException);
        }

        public void InvalidateSessionToken()
        {
            AccessToken = null;
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

    private sealed class FakeDeviceIdStore : IDeviceIdStore
    {
        private readonly List<Operation> _operations;

        public FakeDeviceIdStore(List<Operation> operations)
        {
            _operations = operations;
        }

        public string? Current { get; private set; }

        public Exception? SetException { get; set; }

        public Task<string> GetOrCreateAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(Current ?? "generated-device-id");
        }

        public Task SetAsync(
            string deviceId,
            CancellationToken cancellationToken)
        {
            _operations.Add(new Operation(
                "set-device-id",
                deviceId,
                null,
                cancellationToken));
            if (SetException is not null)
            {
                return Task.FromException(SetException);
            }

            Current = deviceId;
            return Task.CompletedTask;
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

        public Task InvokeCommandAsync(
            string methodName,
            object? request,
            CancellationToken cancellationToken)
        {
            throw new AssertFailedException("InvokeCommandAsync was not expected.");
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
