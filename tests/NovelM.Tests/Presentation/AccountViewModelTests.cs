using NovelM_App.Application.Abstractions;
using NovelM_App.Domain.Auth;
using NovelM_App.Domain.Errors;
using NovelM_App.Presentation.Account;
using NovelM_App.Presentation.Common;

namespace NovelM.Tests.Presentation;

[TestClass]
public sealed class AccountViewModelTests
{
    [TestMethod]
    [DataRow("not-an-email", "password123", "请输入有效的邮箱地址。")]
    [DataRow("reader@example.com", "short", "密码至少需要 8 个字符。")]
    public async Task LoginCommand_InvalidInput_ShowsValidationWithoutServiceCall(
        string email,
        string password,
        string expectedMessage)
    {
        var service = new FakeAuthService();
        var viewModel = CreateViewModel(service);
        viewModel.Email = email;
        viewModel.Password = password;

        await viewModel.LoginCommand.ExecuteAsync(null);

        Assert.AreEqual(0, service.LoginCount);
        Assert.AreEqual(expectedMessage, viewModel.ErrorMessage);
        Assert.IsTrue(viewModel.HasError);
        Assert.AreEqual(email, viewModel.Email);
        Assert.AreEqual(string.Empty, viewModel.Password);
        Assert.IsFalse(viewModel.IsBusy);
        Assert.IsNull(viewModel.CurrentUser);
    }

    [TestMethod]
    public async Task LoginCommand_WhilePending_DisablesSecondExecution()
    {
        var loginEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var profileCompletion = new TaskCompletionSource<UserProfile>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new FakeAuthService
        {
            LoginHandler = (_, _, _) =>
            {
                loginEntered.TrySetResult();
                return profileCompletion.Task;
            }
        };
        var viewModel = CreateViewModel(service);
        viewModel.Email = "reader@example.com";
        viewModel.Password = "password123";

        var firstExecution = viewModel.LoginCommand.ExecuteAsync(null);
        await loginEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.IsTrue(viewModel.IsBusy);
        Assert.IsFalse(viewModel.LoginCommand.CanExecute(null));
        viewModel.LoginCommand.Execute(null);
        Assert.AreEqual(1, service.LoginCount);

        profileCompletion.SetResult(Profile());
        await firstExecution.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.IsFalse(viewModel.IsBusy);
    }

    [TestMethod]
    public async Task LoginCommand_Success_ClearsPasswordAndPublishesProfile()
    {
        var profile = Profile();
        var service = new FakeAuthService
        {
            LoginHandler = (_, _, _) => Task.FromResult(profile)
        };
        var viewModel = CreateViewModel(service);
        viewModel.Email = "  reader@example.com  ";
        viewModel.Password = "password123";

        await viewModel.LoginCommand.ExecuteAsync(null);

        Assert.AreEqual(1, service.LoginCount);
        Assert.AreEqual("reader@example.com", service.LoginEmail);
        Assert.AreEqual("password123", service.LoginPassword);
        Assert.AreSame(profile, viewModel.CurrentUser);
        Assert.AreEqual(string.Empty, viewModel.Password);
        Assert.AreEqual("  reader@example.com  ", viewModel.Email);
        Assert.IsNull(viewModel.ErrorMessage);
        Assert.IsFalse(viewModel.HasError);
        Assert.IsFalse(viewModel.IsBusy);
    }

    [TestMethod]
    public async Task LoginCommand_Failure_ClearsPasswordRetainsEmailAndShowsMessage()
    {
        var service = new FakeAuthService
        {
            LoginHandler = (_, _, _) => Task.FromException<UserProfile>(
                Error(AppErrorKind.Transport, "Synthetic transport detail"))
        };
        var viewModel = CreateViewModel(service);
        viewModel.Email = "reader@example.com";
        viewModel.Password = "password123";

        await viewModel.LoginCommand.ExecuteAsync(null);

        Assert.AreEqual(1, service.LoginCount);
        Assert.AreEqual("reader@example.com", viewModel.Email);
        Assert.AreEqual(string.Empty, viewModel.Password);
        Assert.IsNull(viewModel.CurrentUser);
        Assert.AreEqual(
            "网络连接失败，请检查网络后重试。",
            viewModel.ErrorMessage);
        Assert.IsTrue(viewModel.HasError);
        Assert.IsFalse(viewModel.IsBusy);
    }

    [TestMethod]
    public async Task LogoutCommand_Success_ClearsProfile()
    {
        var profile = Profile();
        var service = new FakeAuthService
        {
            LoginHandler = (_, _, _) => Task.FromResult(profile),
            LogoutHandler = _ => Task.CompletedTask
        };
        var viewModel = CreateViewModel(service);
        viewModel.Email = "reader@example.com";
        viewModel.Password = "password123";
        await viewModel.LoginCommand.ExecuteAsync(null);

        await viewModel.LogoutCommand.ExecuteAsync(null);

        Assert.AreEqual(1, service.LogoutCount);
        Assert.IsNull(viewModel.CurrentUser);
        Assert.IsNull(viewModel.ErrorMessage);
        Assert.IsFalse(viewModel.IsBusy);
    }

    [TestMethod]
    public async Task RestoreAsync_ProfileReturned_PopulatesAccount()
    {
        var profile = Profile();
        var service = new FakeAuthService
        {
            RestoreHandler = _ => Task.FromResult<UserProfile?>(profile)
        };
        var viewModel = CreateViewModel(service);
        using var cancellation = new CancellationTokenSource();

        await viewModel.RestoreAsync(cancellation.Token);

        Assert.AreEqual(1, service.RestoreCount);
        Assert.AreEqual(cancellation.Token, service.RestoreCancellationToken);
        Assert.AreSame(profile, viewModel.CurrentUser);
        Assert.IsNull(viewModel.ErrorMessage);
        Assert.IsFalse(viewModel.IsBusy);
    }

    [TestMethod]
    public async Task RestoreAsync_NoStoredSession_RemainsSignedOutWithoutError()
    {
        var service = new FakeAuthService
        {
            RestoreHandler = _ => Task.FromResult<UserProfile?>(null)
        };
        var viewModel = CreateViewModel(service);

        await viewModel.RestoreAsync(CancellationToken.None);

        Assert.IsNull(viewModel.CurrentUser);
        Assert.IsNull(viewModel.ErrorMessage);
        Assert.IsFalse(viewModel.IsBusy);
    }

    [TestMethod]
    public async Task RestoreAsync_NetworkFailure_RemainsSignedOutWithRetryableMessage()
    {
        var service = new FakeAuthService
        {
            RestoreHandler = _ => Task.FromException<UserProfile?>(
                Error(AppErrorKind.Transport, "Synthetic transport detail"))
        };
        var viewModel = CreateViewModel(service);

        await viewModel.RestoreAsync(CancellationToken.None);

        Assert.IsNull(viewModel.CurrentUser);
        Assert.AreEqual(
            "网络连接失败，请检查网络后重试。",
            viewModel.ErrorMessage);
        Assert.IsFalse(viewModel.IsBusy);
    }

    [TestMethod]
    [DataRow(AppErrorKind.Validation, "请输入有效值。", "请输入有效值。")]
    [DataRow(AppErrorKind.Transport, "unsafe detail", "网络连接失败，请检查网络后重试。")]
    [DataRow(AppErrorKind.Unauthorized, "unsafe detail", "登录已失效，请重新登录。")]
    [DataRow(AppErrorKind.Protocol, "unsafe detail", "服务器响应格式不兼容。")]
    [DataRow(AppErrorKind.Storage, "C:\\secret\\data", "本地数据存储失败，请检查应用数据目录权限。")]
    [DataRow(AppErrorKind.Unexpected, "unsafe detail", "发生未预期错误，请查看诊断日志。")]
    public void ErrorMessageMapper_AppError_MapsSafeLocalizedMessage(
        AppErrorKind kind,
        string detail,
        string expected)
    {
        var mapper = new ErrorMessageMapper();

        var result = mapper.Map(Error(kind, detail));

        Assert.AreEqual(expected, result);
    }

    [TestMethod]
    public void ErrorMessageMapper_ServerMessage_RemovesControlsTrimsAndCapsLength()
    {
        var mapper = new ErrorMessageMapper();
        var detail = " \r\nServer\tmessage " + new string('x', 400) + "\u0000 ";

        var result = mapper.Map(Error(AppErrorKind.Server, detail));

        Assert.AreEqual(300, result.Length);
        Assert.IsTrue(result.StartsWith("Servermessage", StringComparison.Ordinal));
        Assert.IsFalse(result.Any(char.IsControl));
        Assert.AreEqual(result.Trim(), result);
    }

    [TestMethod]
    public void ErrorMessageMapper_NonAppError_ReturnsUnexpectedMessage()
    {
        var mapper = new ErrorMessageMapper();

        var result = mapper.Map(new InvalidOperationException("unsafe detail"));

        Assert.AreEqual("发生未预期错误，请查看诊断日志。", result);
    }

    private static AccountViewModel CreateViewModel(IAuthService service)
    {
        return new AccountViewModel(service, new ErrorMessageMapper());
    }

    private static UserProfile Profile()
    {
        return new UserProfile(42, "reader", "avatar.png", "Member");
    }

    private static AppException Error(AppErrorKind kind, string detail)
    {
        return new AppException(kind, detail);
    }

    private sealed class FakeAuthService : IAuthService
    {
        public Func<CancellationToken, Task<UserProfile?>>? RestoreHandler { get; init; }

        public Func<string, string, CancellationToken, Task<UserProfile>>? LoginHandler
        {
            get;
            init;
        }

        public Func<CancellationToken, Task>? LogoutHandler { get; init; }

        public UserProfile? CurrentUser { get; private set; }

        public int RestoreCount { get; private set; }

        public int LoginCount { get; private set; }

        public int LogoutCount { get; private set; }

        public string? LoginEmail { get; private set; }

        public string? LoginPassword { get; private set; }

        public CancellationToken RestoreCancellationToken { get; private set; }

        public async Task<UserProfile?> RestoreAsync(CancellationToken cancellationToken)
        {
            RestoreCount++;
            RestoreCancellationToken = cancellationToken;
            var profile = await (RestoreHandler?.Invoke(cancellationToken)
                ?? throw new AssertFailedException("RestoreAsync was not expected."));
            CurrentUser = profile;
            return profile;
        }

        public async Task<UserProfile> LoginAsync(
            string email,
            string rawPassword,
            CancellationToken cancellationToken)
        {
            LoginCount++;
            LoginEmail = email;
            LoginPassword = rawPassword;
            var profile = await (LoginHandler?.Invoke(email, rawPassword, cancellationToken)
                ?? throw new AssertFailedException("LoginAsync was not expected."));
            CurrentUser = profile;
            return profile;
        }

        public Task<UserProfile> LoginWithRefreshTokenAsync(
            string refreshToken,
            string deviceId,
            CancellationToken cancellationToken)
        {
            throw new AssertFailedException(
                "LoginWithRefreshTokenAsync was not expected.");
        }

        public async Task LogoutAsync(CancellationToken cancellationToken)
        {
            LogoutCount++;
            await (LogoutHandler?.Invoke(cancellationToken)
                ?? throw new AssertFailedException("LogoutAsync was not expected."));
            CurrentUser = null;
        }
    }
}
