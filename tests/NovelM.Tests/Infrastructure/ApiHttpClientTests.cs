using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using NovelM.Tests.TestSupport;
using NovelM_App.Application.Abstractions;
using NovelM_App.Domain.Errors;
using NovelM_App.Infrastructure.Configuration;
using NovelM_App.Infrastructure.Http;
using NovelM_App.Infrastructure.Storage;

namespace NovelM.Tests.Infrastructure;

[TestClass]
public sealed class ApiHttpClientTests
{
    [TestMethod]
    public async Task LoginAsync_PostsExactRequestAndMapsTokens()
    {
        using var fixture = await ApiFixture.CreateAsync(_ => JsonResponse(
            """{"success":true,"response":{"token":"session-token","refreshToken":"refresh-token"},"status":200}"""));

        var result = await fixture.Api.LoginAsync(
            "reader@example.test",
            "already-hashed-password",
            CancellationToken.None);

        Assert.AreEqual("session-token", result.SessionToken);
        Assert.AreEqual("refresh-token", result.RefreshToken);
        Assert.HasCount(1, fixture.Handler.Requests);
        var request = fixture.Handler.Requests[0];
        Assert.AreEqual(HttpMethod.Post, request.Method);
        Assert.AreEqual(
            new Uri("https://api.lightnovel.life/api/user/login"),
            request.RequestUri);
        CollectionAssert.AreEqual(
            new[] { "application/json" },
            request.Headers["Accept"]);
        CollectionAssert.AreEqual(
            new[] { "application/json; charset=utf-8" },
            request.ContentHeaders["Content-Type"]);
        Assert.IsFalse(request.Headers.ContainsKey("Authorization"));

        var persistedDeviceId = await fixture.DeviceIdStore.GetOrCreateAsync(
            CancellationToken.None);
        CollectionAssert.AreEqual(
            new[] { persistedDeviceId.ToString("D") },
            request.Headers["x-id"]);
        AssertBody(
            request.Body,
            ("email", "reader@example.test"),
            ("password", "already-hashed-password"));
    }

    [TestMethod]
    public async Task LoginAsync_MixedCaseEnvelopeAndResponsePropertiesMapTokens()
    {
        using var fixture = await ApiFixture.CreateAsync(_ => JsonResponse(
            """{"sUcCeSs":true,"rEsPoNsE":{"tOkEn":"mixed-session","rEfReShToKeN":"mixed-refresh"},"sTaTuS":200}"""));

        var result = await fixture.Api.LoginAsync(
            "reader@example.test",
            "already-hashed-password",
            CancellationToken.None);

        Assert.AreEqual("mixed-session", result.SessionToken);
        Assert.AreEqual("mixed-refresh", result.RefreshToken);
    }

    [TestMethod]
    public async Task LoginAsync_SecondRequestReusesSameDeviceHeader()
    {
        using var fixture = await ApiFixture.CreateAsync(_ => SuccessfulLoginResponse());

        await fixture.Api.LoginAsync("first@example.test", "hash-one", CancellationToken.None);
        await fixture.Api.LoginAsync("second@example.test", "hash-two", CancellationToken.None);

        Assert.HasCount(2, fixture.Handler.Requests);
        var firstId = fixture.Handler.Requests[0].Headers["x-id"];
        var secondId = fixture.Handler.Requests[1].Headers["x-id"];
        Assert.HasCount(1, firstId);
        CollectionAssert.AreEqual(firstId, secondId);
    }

    [TestMethod]
    public async Task LoginAsync_ParallelCallsCaptureIsolatedRequestsWithSameDeviceHeader()
    {
        const int requestCount = 128;
        using var fixture = await ApiFixture.CreateAsync(_ => SuccessfulLoginResponse());
        var expectedBodies = Enumerable.Range(0, requestCount)
            .Select(index => $"reader-{index}@example.test|hash-{index}")
            .ToArray();

        var results = await Task.WhenAll(Enumerable.Range(0, requestCount)
            .Select(index => fixture.Api.LoginAsync(
                $"reader-{index}@example.test",
                $"hash-{index}",
                CancellationToken.None)));

        Assert.HasCount(requestCount, results);
        var requests = fixture.Handler.Requests;
        Assert.HasCount(requestCount, requests);
        CollectionAssert.AreEquivalent(
            expectedBodies,
            requests.Select(request => ReadLoginBody(request.Body)).ToArray());
        var persistedDeviceId = await fixture.DeviceIdStore.GetOrCreateAsync(
            CancellationToken.None);
        var expectedHeader = persistedDeviceId.ToString("D");
        Assert.IsTrue(requests.All(request =>
            request.Headers.TryGetValue("x-id", out var values)
            && values.Length == 1
            && values[0] == expectedHeader));
        Assert.AreNotSame(
            requests[0].Headers["x-id"],
            requests[1].Headers["x-id"]);
    }

    [TestMethod]
    public async Task LoginAsync_AfterServerSelectionUsesNewCurrentServer()
    {
        using var fixture = await ApiFixture.CreateAsync(_ => SuccessfulLoginResponse());

        await fixture.Api.LoginAsync("reader@example.test", "hash-one", CancellationToken.None);
        await fixture.ServerManager.SelectAsync("cf", CancellationToken.None);
        await fixture.Api.LoginAsync("reader@example.test", "hash-two", CancellationToken.None);

        Assert.AreEqual(
            new Uri("https://api.lightnovel.life/api/user/login"),
            fixture.Handler.Requests[0].RequestUri);
        Assert.AreEqual(
            new Uri("https://cf-api.lightnovel.life/api/user/login"),
            fixture.Handler.Requests[1].RequestUri);
    }

    [TestMethod]
    public async Task RefreshAsync_PostsExactRequestAndReturnsJsonString()
    {
        using var fixture = await ApiFixture.CreateAsync(_ => JsonResponse(
            """{"success":true,"response":"new-session-token","status":200}"""));

        var result = await fixture.Api.RefreshAsync(
            "synthetic-refresh-token",
            CancellationToken.None);

        Assert.AreEqual("new-session-token", result);
        var request = fixture.Handler.Requests.Single();
        Assert.AreEqual(HttpMethod.Post, request.Method);
        Assert.AreEqual(
            new Uri("https://api.lightnovel.life/api/user/refresh_token"),
            request.RequestUri);
        Assert.IsFalse(request.Headers.ContainsKey("Authorization"));
        AssertBody(request.Body, ("token", "synthetic-refresh-token"));
    }

    [TestMethod]
    public async Task LoginAsync_Unsuccessful403ThrowsServerWithStatusAndMessage()
    {
        using var fixture = await ApiFixture.CreateAsync(_ => JsonResponse(
            """{"success":false,"response":null,"status":403,"msg":"Access denied"}"""));

        var exception = await Assert.ThrowsExactlyAsync<AppException>(() =>
            fixture.Api.LoginAsync("reader@example.test", "hash", CancellationToken.None));

        Assert.AreEqual(AppErrorKind.Server, exception.Kind);
        Assert.AreEqual(403, exception.Status);
        Assert.AreEqual("Access denied", exception.Message);
    }

    [TestMethod]
    public async Task LoginAsync_UnsuccessfulEnvelopeIgnoresUnusedResponseShape()
    {
        using var fixture = await ApiFixture.CreateAsync(_ => JsonResponse(
            """{"success":false,"response":"unused-shape","status":403,"msg":"Access denied"}"""));

        var exception = await Assert.ThrowsExactlyAsync<AppException>(() =>
            fixture.Api.LoginAsync("reader@example.test", "hash", CancellationToken.None));

        Assert.AreEqual(AppErrorKind.Server, exception.Kind);
        Assert.AreEqual(403, exception.Status);
        Assert.AreEqual("Access denied", exception.Message);
    }

    [TestMethod]
    [DataRow(-100)]
    [DataRow(404)]
    public async Task LoginAsync_UnsuccessfulAuthStatusThrowsUnauthorized(int status)
    {
        using var fixture = await ApiFixture.CreateAsync(_ => JsonResponse(
            $$"""{"success":false,"response":null,"status":{{status}},"msg":"Sign in again"}"""));

        var exception = await Assert.ThrowsExactlyAsync<AppException>(() =>
            fixture.Api.LoginAsync("reader@example.test", "hash", CancellationToken.None));

        Assert.AreEqual(AppErrorKind.Unauthorized, exception.Kind);
        Assert.AreEqual(status, exception.Status);
        Assert.AreEqual("Sign in again", exception.Message);
    }

    [TestMethod]
    [DataRow("plain response body with body-secret")]
    [DataRow("{\"success\":false,\"status\":")]
    public async Task LoginAsync_NonSuccessHttpWithUnusableBodyThrowsSafeTransport(string body)
    {
        using var fixture = await ApiFixture.CreateAsync(_ => TextResponse(
            body,
            HttpStatusCode.BadGateway));

        var exception = await Assert.ThrowsExactlyAsync<AppException>(() =>
            fixture.Api.LoginAsync("reader@example.test", "hash", CancellationToken.None));

        Assert.AreEqual(AppErrorKind.Transport, exception.Kind);
        Assert.AreEqual(502, exception.Status);
        Assert.IsFalse(exception.Message.Contains(body, StringComparison.Ordinal));
        Assert.IsFalse(exception.Message.Contains("body-secret", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task LoginAsync_NonSuccessHttpServerEnvelopeThrowsTransportWithHttpStatus()
    {
        const string serverMessage = "server-body-message";
        using var fixture = await ApiFixture.CreateAsync(_ => JsonResponse(
            $$"""{"success":false,"response":null,"status":403,"msg":"{{serverMessage}}"}""",
            HttpStatusCode.BadGateway));

        var exception = await Assert.ThrowsExactlyAsync<AppException>(() =>
            fixture.Api.LoginAsync("reader@example.test", "hash", CancellationToken.None));

        Assert.AreEqual(AppErrorKind.Transport, exception.Kind);
        Assert.AreEqual(502, exception.Status);
        Assert.IsFalse(exception.Message.Contains(serverMessage, StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow(-100)]
    [DataRow(404)]
    public async Task RefreshAsync_NonSuccessHttpAuthEnvelopeThrowsUnauthorized(int status)
    {
        using var fixture = await ApiFixture.CreateAsync(_ => JsonResponse(
            $$"""{"success":false,"response":null,"status":{{status}},"msg":"Session expired"}""",
            HttpStatusCode.Unauthorized));

        var exception = await Assert.ThrowsExactlyAsync<AppException>(() =>
            fixture.Api.RefreshAsync("synthetic-refresh-token", CancellationToken.None));

        Assert.AreEqual(AppErrorKind.Unauthorized, exception.Kind);
        Assert.AreEqual(status, exception.Status);
        Assert.AreEqual("Session expired", exception.Message);
    }

    [TestMethod]
    public async Task RefreshAsync_NonSuccessHttpAuthEnvelopeIgnoresUnusedResponseShape()
    {
        using var fixture = await ApiFixture.CreateAsync(_ => JsonResponse(
            """{"success":false,"response":{"unused":"shape"},"status":-100,"msg":"Session expired"}""",
            HttpStatusCode.Unauthorized));

        var exception = await Assert.ThrowsExactlyAsync<AppException>(() =>
            fixture.Api.RefreshAsync("synthetic-refresh-token", CancellationToken.None));

        Assert.AreEqual(AppErrorKind.Unauthorized, exception.Kind);
        Assert.AreEqual(-100, exception.Status);
        Assert.AreEqual("Session expired", exception.Message);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("{\"success\":true,\"response\":\"body-secret\"")]
    public async Task RefreshAsync_SuccessHttpWithUnusableBodyThrowsSafeProtocol(string body)
    {
        using var fixture = await ApiFixture.CreateAsync(_ => TextResponse(body, HttpStatusCode.OK));

        var exception = await Assert.ThrowsExactlyAsync<AppException>(() =>
            fixture.Api.RefreshAsync("synthetic-refresh-token", CancellationToken.None));

        Assert.AreEqual(AppErrorKind.Protocol, exception.Kind);
        if (body.Length > 0)
        {
            Assert.IsFalse(exception.Message.Contains(body, StringComparison.Ordinal));
        }

        Assert.IsFalse(exception.Message.Contains("body-secret", StringComparison.Ordinal));
        Assert.IsNotNull(exception.InnerException);
    }

    [TestMethod]
    [DataRow("{\"response\":\"value\",\"status\":200}")]
    [DataRow("{\"success\":true,\"response\":\"value\"}")]
    public async Task RefreshAsync_SuccessHttpMissingRequiredEnvelopeFieldThrowsProtocol(string body)
    {
        using var fixture = await ApiFixture.CreateAsync(_ => JsonResponse(body));

        var exception = await Assert.ThrowsExactlyAsync<AppException>(() =>
            fixture.Api.RefreshAsync("synthetic-refresh-token", CancellationToken.None));

        Assert.AreEqual(AppErrorKind.Protocol, exception.Kind);
        Assert.IsNotNull(exception.InnerException);
    }

    [TestMethod]
    public async Task RefreshAsync_SuccessEnvelopeWithNullResponseThrowsProtocol()
    {
        using var fixture = await ApiFixture.CreateAsync(_ => JsonResponse(
            """{"success":true,"response":null,"status":200}"""));

        var exception = await Assert.ThrowsExactlyAsync<AppException>(() =>
            fixture.Api.RefreshAsync("synthetic-refresh-token", CancellationToken.None));

        Assert.AreEqual(AppErrorKind.Protocol, exception.Kind);
    }

    [TestMethod]
    public async Task LoginAsync_SuccessResponseWithNullRequiredTokenThrowsProtocol()
    {
        using var fixture = await ApiFixture.CreateAsync(_ => JsonResponse(
            """{"success":true,"response":{"token":null,"refreshToken":"refresh-token"},"status":200}"""));

        var exception = await Assert.ThrowsExactlyAsync<AppException>(() =>
            fixture.Api.LoginAsync(
                "reader@example.test",
                "already-hashed-password",
                CancellationToken.None));

        Assert.AreEqual(AppErrorKind.Protocol, exception.Kind);
        Assert.IsNotNull(exception.InnerException);
    }

    [TestMethod]
    public async Task LoginAsync_HttpRequestExceptionThrowsSafeTransportWithInnerException()
    {
        const string email = "credential-secret@example.test";
        const string password = "synthetic-password-hash-secret";
        var failure = new HttpRequestException(
            $"send failed for {email} using {password}",
            inner: null,
            HttpStatusCode.ServiceUnavailable);
        using var fixture = await ApiFixture.CreateAsync(new LocalApiHandler(failure));

        var exception = await Assert.ThrowsExactlyAsync<AppException>(() =>
            fixture.Api.LoginAsync(email, password, CancellationToken.None));

        Assert.AreEqual(AppErrorKind.Transport, exception.Kind);
        Assert.AreEqual(503, exception.Status);
        Assert.AreSame(failure, exception.InnerException);
        StringAssert.Contains(exception.Message, "Login");
        StringAssert.Contains(exception.Message, "api.lightnovel.life");
        StringAssert.Contains(exception.Message, "503");
        Assert.IsFalse(exception.Message.Contains(email, StringComparison.Ordinal));
        Assert.IsFalse(exception.Message.Contains(password, StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task LoginAsync_AlreadyCancelledPreservesCancellationWithoutInvokingHandler()
    {
        using var fixture = await ApiFixture.CreateAsync(_ => SuccessfulLoginResponse());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            fixture.Api.LoginAsync("reader@example.test", "hash", cancellation.Token));

        Assert.HasCount(0, fixture.Handler.Requests);
    }

    [TestMethod]
    public async Task RefreshAsync_UsesAbsoluteCurrentUriAndOverridesContaminatingDefaultHeaders()
    {
        using var fixture = await ApiFixture.CreateAsync(_ => JsonResponse(
            """{"success":true,"response":"new-session-token","status":200}"""));
        fixture.HttpClient.BaseAddress = new Uri("https://wrong.example.test/base/");
        fixture.HttpClient.DefaultRequestHeaders.TryAddWithoutValidation(
            "x-id",
            "default-device-contamination");
        fixture.HttpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("text/plain"));
        await fixture.ServerManager.SelectAsync("cf", CancellationToken.None);

        await fixture.Api.RefreshAsync("synthetic-refresh-token", CancellationToken.None);

        var request = fixture.Handler.Requests.Single();
        Assert.AreEqual(
            new Uri("https://cf-api.lightnovel.life/api/user/refresh_token"),
            request.RequestUri);
        var persistedDeviceId = await fixture.DeviceIdStore.GetOrCreateAsync(
            CancellationToken.None);
        CollectionAssert.AreEqual(
            new[] { persistedDeviceId.ToString("D") },
            request.Headers["x-id"]);
        CollectionAssert.AreEqual(
            new[] { "application/json" },
            request.Headers["Accept"]);
        CollectionAssert.AreEqual(
            new[] { "default-device-contamination" },
            fixture.HttpClient.DefaultRequestHeaders.GetValues("x-id").ToArray());
        CollectionAssert.AreEqual(
            new[] { "text/plain" },
            fixture.HttpClient.DefaultRequestHeaders.Accept.Select(value => value.MediaType).ToArray());
    }

    [TestMethod]
    public async Task LoginAsync_DefaultAuthorizationFailsClosedWithoutSendingOrMutatingHeader()
    {
        using var fixture = await ApiFixture.CreateAsync(_ => SuccessfulLoginResponse());
        const string scheme = "Bearer";
        const string credential = "synthetic-contaminating-default";
        fixture.HttpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(scheme, credential);
        var exception = await Assert.ThrowsExactlyAsync<AppException>(() =>
            fixture.Api.LoginAsync(
                "reader@example.test",
                "already-hashed-password",
                CancellationToken.None));

        Assert.HasCount(0, fixture.Handler.Requests);
        Assert.IsFalse(File.Exists(fixture.Paths.DeviceFile));
        Assert.AreEqual(AppErrorKind.Unexpected, exception.Kind);
        Assert.IsFalse(exception.Message.Contains(scheme, StringComparison.Ordinal));
        Assert.IsFalse(exception.Message.Contains(credential, StringComparison.Ordinal));
        Assert.IsNotNull(fixture.HttpClient.DefaultRequestHeaders.Authorization);
        Assert.AreEqual(
            scheme,
            fixture.HttpClient.DefaultRequestHeaders.Authorization.Scheme);
        Assert.AreEqual(
            credential,
            fixture.HttpClient.DefaultRequestHeaders.Authorization.Parameter);
    }

    [TestMethod]
    public async Task LoginAsync_RawDefaultAuthorizationFailsClosedWithoutSendingOrMutatingHeader()
    {
        using var fixture = await ApiFixture.CreateAsync(_ => SuccessfulLoginResponse());
        const string scheme = "Bearer";
        const string credential = "credential-secret";
        var originalValues = new[] { string.Empty, $"{scheme} {credential}" };
        Assert.IsTrue(fixture.HttpClient.DefaultRequestHeaders.TryAddWithoutValidation(
            "Authorization",
            originalValues));
        Assert.IsNull(fixture.HttpClient.DefaultRequestHeaders.Authorization);
        var exception = await Assert.ThrowsExactlyAsync<AppException>(() =>
            fixture.Api.LoginAsync(
                "reader@example.test",
                "already-hashed-password",
                CancellationToken.None));

        Assert.HasCount(0, fixture.Handler.Requests);
        Assert.IsFalse(File.Exists(fixture.Paths.DeviceFile));
        Assert.AreEqual(AppErrorKind.Unexpected, exception.Kind);
        Assert.IsFalse(exception.Message.Contains(scheme, StringComparison.Ordinal));
        Assert.IsFalse(exception.Message.Contains(credential, StringComparison.Ordinal));
        Assert.IsTrue(fixture.HttpClient.DefaultRequestHeaders.Contains("Authorization"));
        CollectionAssert.AreEqual(
            originalValues,
            fixture.HttpClient.DefaultRequestHeaders.GetValues("Authorization").ToArray());
    }

    private static HttpResponseMessage SuccessfulLoginResponse()
    {
        return JsonResponse(
            """{"success":true,"response":{"token":"session-token","refreshToken":"refresh-token"},"status":200}""");
    }

    private static HttpResponseMessage JsonResponse(
        string body,
        HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
    }

    private static HttpResponseMessage TextResponse(
        string body,
        HttpStatusCode statusCode)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "text/plain")
        };
    }

    private static void AssertBody(
        string? body,
        params (string Name, string Value)[] expectedProperties)
    {
        Assert.IsNotNull(body);
        using var document = JsonDocument.Parse(body);
        var properties = document.RootElement.EnumerateObject().ToArray();
        Assert.AreEqual(expectedProperties.Length, properties.Length);

        for (var index = 0; index < expectedProperties.Length; index++)
        {
            Assert.AreEqual(expectedProperties[index].Name, properties[index].Name);
            Assert.AreEqual(expectedProperties[index].Value, properties[index].Value.GetString());
        }
    }

    private static string ReadLoginBody(string? body)
    {
        Assert.IsNotNull(body);
        using var document = JsonDocument.Parse(body);
        return $"{document.RootElement.GetProperty("email").GetString()}|"
            + document.RootElement.GetProperty("password").GetString();
    }

    private sealed class ApiFixture : IDisposable
    {
        private readonly TemporaryDirectory _temporaryDirectory;

        private ApiFixture(
            TemporaryDirectory temporaryDirectory,
            AppPaths paths,
            ApiServerManager serverManager,
            DeviceIdStore deviceIdStore,
            LocalApiHandler handler)
        {
            _temporaryDirectory = temporaryDirectory;
            Paths = paths;
            ServerManager = serverManager;
            DeviceIdStore = deviceIdStore;
            Handler = handler;
            HttpClient = new HttpClient(handler);
            Api = new AuthApi(new ApiHttpClient(HttpClient, serverManager, deviceIdStore));
        }

        public IAuthApi Api { get; }

        public AppPaths Paths { get; }

        public ApiServerManager ServerManager { get; }

        public DeviceIdStore DeviceIdStore { get; }

        public LocalApiHandler Handler { get; }

        public HttpClient HttpClient { get; }

        public static Task<ApiFixture> CreateAsync(
            Func<int, HttpResponseMessage> responseFactory)
        {
            return CreateAsync(new LocalApiHandler(responseFactory));
        }

        public static async Task<ApiFixture> CreateAsync(LocalApiHandler handler)
        {
            var temporaryDirectory = new TemporaryDirectory();

            try
            {
                var paths = new AppPaths(temporaryDirectory.Path);
                var serverManager = new ApiServerManager(paths, includeLocalhost: false);
                await serverManager.LoadAsync(CancellationToken.None);
                return new ApiFixture(
                    temporaryDirectory,
                    paths,
                    serverManager,
                    new DeviceIdStore(paths),
                    handler);
            }
            catch
            {
                handler.Dispose();
                temporaryDirectory.Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            HttpClient.Dispose();
            _temporaryDirectory.Dispose();
        }
    }
}
