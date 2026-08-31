using NovelM_App.Application.Abstractions;
using NovelM_App.Domain.Connection;
using NovelM_App.Infrastructure.SignalR;

namespace NovelM.Tests.Infrastructure;

[TestClass]
public sealed class SignalRUserApiTests
{
    [TestMethod]
    public void SignalRUserApi_IsInternal()
    {
        Assert.IsFalse(typeof(SignalRUserApi).IsPublic);
    }

    [TestMethod]
    public async Task GetMyInfoAsync_InvokesExactMethodAndMapsProfile()
    {
        var response = new UserProfileDto
        {
            Id = 42,
            UserName = "reader",
            Avatar = "avatar.png",
            Role = new RoleDto { Name = "Member" }
        };
        var connection = new TypedFakeSignalRConnection<UserProfileDto>(response);
        var api = new SignalRUserApi(connection);
        using var cancellation = new CancellationTokenSource();

        var result = await api.GetMyInfoAsync(cancellation.Token);

        Assert.AreEqual(42L, result.Id);
        Assert.AreEqual("reader", result.UserName);
        Assert.AreEqual("avatar.png", result.Avatar);
        Assert.AreEqual("Member", result.RoleName);
        Assert.AreEqual(1, connection.Calls.Count);
        Assert.AreEqual(HubMethodNames.GetMyInfo, connection.Calls[0].MethodName);
        Assert.IsNull(connection.Calls[0].Request);
        Assert.AreEqual(cancellation.Token, connection.Calls[0].CancellationToken);
        Assert.AreEqual(typeof(UserProfileDto), connection.Calls[0].ResponseType);
    }

    [TestMethod]
    public async Task GetMyInfoAsync_MapsOptionalInteriorLevel()
    {
        var response = new UserProfileDto
        {
            Id = 42,
            UserName = "reader",
            Avatar = "avatar.png",
            Role = new RoleDto { Name = "Member" },
            InteriorLevel = 5
        };
        var connection = new TypedFakeSignalRConnection<UserProfileDto>(response);
        var api = new SignalRUserApi(connection);

        var result = await api.GetMyInfoAsync(CancellationToken.None);

        Assert.AreEqual(5, result.InteriorLevel);
    }

    [TestMethod]
    public async Task GetMyInfoAsync_MissingInteriorLevel_DefaultsToZero()
    {
        var response = new UserProfileDto
        {
            Id = 42,
            UserName = "reader",
            Avatar = "avatar.png",
            Role = new RoleDto { Name = "Member" }
        };
        var connection = new TypedFakeSignalRConnection<UserProfileDto>(response);
        var api = new SignalRUserApi(connection);

        var result = await api.GetMyInfoAsync(CancellationToken.None);

        Assert.AreEqual(0, result.InteriorLevel);
    }

    private sealed record Invocation(
        string MethodName,
        object? Request,
        CancellationToken CancellationToken,
        Type ResponseType);

    private sealed class TypedFakeSignalRConnection<TResponse> : ISignalRConnection
    {
        private readonly TResponse _response;

        public TypedFakeSignalRConnection(TResponse response)
        {
            _response = response;
        }

        public ConnectionState State => ConnectionState.Disconnected;

        public List<Invocation> Calls { get; } = new();

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
            throw new AssertFailedException("RestartAsync was not expected.");
        }

        public Task<T> InvokeAsync<T>(
            string methodName,
            object? request,
            CancellationToken cancellationToken)
        {
            Calls.Add(new Invocation(methodName, request, cancellationToken, typeof(T)));
            Assert.AreEqual(typeof(TResponse), typeof(T));
            Assert.IsInstanceOfType<T>(_response);
            return Task.FromResult((T)(object)_response!);
        }

        public Task InvokeCommandAsync(
            string methodName,
            object? request,
            CancellationToken cancellationToken)
        {
            throw new AssertFailedException("InvokeCommandAsync was not expected.");
        }
    }
}
