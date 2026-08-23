using NovelM_App.Application.Abstractions;
using NovelM_App.Domain.Auth;
using NovelM_App.Domain.Configuration;
using NovelM_App.Domain.Connection;
using NovelM_App.Domain.Errors;
using NovelM_App.Presentation.Common;
using NovelM_App.Presentation.Settings;

namespace NovelM.Tests.Presentation;

[TestClass]
public sealed class SettingsViewModelTests
{
    [TestMethod]
    public async Task SelectServerCommand_PersistsInvalidatesAndRestartsInOrder()
    {
        var operations = new List<string>();
        var current = Server("hong-kong", "香港节点");
        var selected = Server("cloudflare", "Cloudflare 节点");
        var manager = new FakeServerManager(current, new[] { current, selected }, operations);
        var session = new FakeAuthSession(operations);
        var connection = new FakeSignalRConnection(operations);
        var viewModel = CreateViewModel(manager, session, connection);

        await viewModel.SelectServerCommand.ExecuteAsync(selected);

        CollectionAssert.AreEqual(
            new[] { "select:cloudflare", "invalidate", "restart" },
            operations.ToArray());
        Assert.AreSame(selected, viewModel.SelectedServer);
        Assert.AreSame(selected, manager.Current);
        Assert.IsNull(viewModel.ErrorMessage);
        Assert.IsFalse(viewModel.HasError);
        Assert.IsFalse(viewModel.IsBusy);
    }

    [TestMethod]
    public async Task SelectServerCommand_CurrentServer_HasNoSideEffects()
    {
        var operations = new List<string>();
        var current = Server("hong-kong", "香港节点");
        var manager = new FakeServerManager(current, new[] { current }, operations);
        var viewModel = CreateViewModel(
            manager,
            new FakeAuthSession(operations),
            new FakeSignalRConnection(operations));

        await viewModel.SelectServerCommand.ExecuteAsync(current);

        CollectionAssert.AreEqual(Array.Empty<string>(), operations.ToArray());
        Assert.AreSame(current, viewModel.SelectedServer);
        Assert.IsNull(viewModel.ErrorMessage);
        Assert.IsFalse(viewModel.IsBusy);
    }

    [TestMethod]
    public async Task SelectServerCommand_RestartFails_KeepsSelectionAndShowsFailure()
    {
        var operations = new List<string>();
        var current = Server("hong-kong", "香港节点");
        var selected = Server("cloudflare", "Cloudflare 节点");
        var manager = new FakeServerManager(current, new[] { current, selected }, operations);
        var session = new FakeAuthSession(operations);
        var connection = new FakeSignalRConnection(operations)
        {
            RestartException = new AppException(
                AppErrorKind.Transport,
                "Synthetic transport detail")
        };
        var viewModel = CreateViewModel(manager, session, connection);

        await viewModel.SelectServerCommand.ExecuteAsync(selected);

        CollectionAssert.AreEqual(
            new[] { "select:cloudflare", "invalidate", "restart" },
            operations.ToArray());
        Assert.AreSame(selected, viewModel.SelectedServer);
        Assert.AreSame(selected, manager.Current);
        Assert.AreEqual(
            "网络连接失败，请检查网络后重试。",
            viewModel.ErrorMessage);
        Assert.IsTrue(viewModel.HasError);
        Assert.IsFalse(viewModel.IsBusy);
    }

    [TestMethod]
    public void Constructor_ExposesOptionsSelectionAndReadOnlyDataDirectory()
    {
        var operations = new List<string>();
        var current = Server("hong-kong", "香港节点");
        var selected = Server("cloudflare", "Cloudflare 节点");
        var manager = new FakeServerManager(current, new[] { current, selected }, operations);

        var viewModel = CreateViewModel(
            manager,
            new FakeAuthSession(operations),
            new FakeSignalRConnection(operations));

        CollectionAssert.AreEqual(
            new[] { current, selected },
            viewModel.Options.ToArray());
        Assert.AreSame(current, viewModel.SelectedServer);
        Assert.AreEqual("C:\\NovelM\\data", viewModel.DataDirectory);
        Assert.IsFalse(typeof(SettingsViewModel)
            .GetProperty(nameof(SettingsViewModel.DataDirectory))!
            .CanWrite);
    }

    private static SettingsViewModel CreateViewModel(
        IApiServerManager manager,
        IAuthSession session,
        ISignalRConnection connection)
    {
        return new SettingsViewModel(
            manager,
            session,
            connection,
            new ErrorMessageMapper(),
            "C:\\NovelM\\data");
    }

    private static ApiServerOption Server(string id, string displayName)
    {
        return new ApiServerOption(id, displayName, new Uri($"https://{id}.example"));
    }

    private sealed class FakeServerManager : IApiServerManager
    {
        private readonly List<string> _operations;

        public FakeServerManager(
            ApiServerOption current,
            IReadOnlyList<ApiServerOption> options,
            List<string> operations)
        {
            Current = current;
            Options = options;
            _operations = operations;
        }

        public ApiServerOption Current { get; private set; }

        public IReadOnlyList<ApiServerOption> Options { get; }

        public event EventHandler<ApiServerOption>? CurrentChanged
        {
            add { }
            remove { }
        }

        public Task LoadAsync(CancellationToken cancellationToken)
        {
            throw new AssertFailedException("LoadAsync was not expected.");
        }

        public Task SelectAsync(string serverId, CancellationToken cancellationToken)
        {
            _operations.Add($"select:{serverId}");
            Current = Options.Single(option => option.Id == serverId);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeAuthSession : IAuthSession
    {
        private readonly List<string> _operations;

        public FakeAuthSession(List<string> operations)
        {
            _operations = operations;
        }

        public string? SessionToken => null;

        public Task SetTokensAsync(LoginTokens tokens, CancellationToken cancellationToken)
        {
            throw new AssertFailedException("SetTokensAsync was not expected.");
        }

        public Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken)
        {
            throw new AssertFailedException("GetAccessTokenAsync was not expected.");
        }

        public void InvalidateSessionToken()
        {
            _operations.Add("invalidate");
        }

        public Task ClearAsync(CancellationToken cancellationToken)
        {
            throw new AssertFailedException("ClearAsync was not expected.");
        }
    }

    private sealed class FakeSignalRConnection : ISignalRConnection
    {
        private readonly List<string> _operations;

        public FakeSignalRConnection(List<string> operations)
        {
            _operations = operations;
        }

        public ConnectionState State => ConnectionState.Disconnected;

        public Exception? RestartException { get; init; }

        public event EventHandler<ConnectionState>? StateChanged
        {
            add { }
            remove { }
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            throw new AssertFailedException("StartAsync was not expected.");
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            throw new AssertFailedException("StopAsync was not expected.");
        }

        public Task RestartAsync(CancellationToken cancellationToken)
        {
            _operations.Add("restart");
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
}
