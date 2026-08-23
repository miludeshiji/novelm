using System.Collections.Concurrent;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MessagePack.Resolvers;
using NovelM_App.Infrastructure.SignalR;

namespace NovelM.Tests.TestSupport;

internal sealed class LocalSignalRHost : IAsyncDisposable
{
    private readonly WebApplication _application;

    private LocalSignalRHost(
        WebApplication application,
        Uri baseUri,
        LocalSignalRState state)
    {
        _application = application;
        BaseUri = baseUri;
        State = state;
    }

    public Uri BaseUri { get; }

    public LocalSignalRState State { get; }

    public static async Task<LocalSignalRHost> StartAsync(
        bool unauthorizedOnce = false,
        string? firstHubExceptionMessage = null,
        int? envelopeFailureStatus = null,
        bool repeatHubException = false,
        bool blockNegotiate = false,
        int port = 0)
    {
        var state = new LocalSignalRState(
            unauthorizedOnce ? "user is unauthorized" : firstHubExceptionMessage,
            envelopeFailureStatus,
            repeatHubException,
            blockNegotiate);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(LocalSignalRHost).Assembly.GetName().Name,
            EnvironmentName = Environments.Development
        });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options =>
            options.Listen(IPAddress.Loopback, port));
        builder.Services.AddSingleton(state);
        builder.Services
            .AddSignalR(options =>
            {
                options.EnableDetailedErrors = true;
                options.SupportedProtocols = ["messagepack"];
            })
            .AddMessagePackProtocol(options =>
                options.SerializerOptions = options.SerializerOptions
                    .WithResolver(ContractlessStandardResolverAllowPrivate.Instance));

        var application = builder.Build();
        application.Use(async (context, next) =>
        {
            state.RecordBearerToken(context);
            if (context.Request.Path == "/hub/api/negotiate")
            {
                state.RecordNegotiate();
                await state.BlockNegotiateAsync(context.RequestAborted);
            }

            await next(context);
        });
        application.MapHub<LocalApiHub>("/hub/api");
        using var startupCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await application.StartAsync(startupCancellation.Token);

        var addresses = application.Services
            .GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses;
        return new LocalSignalRHost(application, new Uri(addresses.Single()), state);
    }

    public async ValueTask DisposeAsync()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await _application.StopAsync(cancellation.Token);
        await _application.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    }

    private sealed class LocalApiHub(LocalSignalRState state) : Hub
    {
        public override async Task OnConnectedAsync()
        {
            state.RecordBearerToken(Context.GetHttpContext());
            state.RecordConnected();
            await base.OnConnectedAsync();
        }

        public HubEnvelope<byte[]> GetMyInfo(object? request, LocalGzipOption option)
        {
            state.RecordInvocation(nameof(GetMyInfo), request, option.UseGzip);
            return state.CreateEnvelope(
                """{"Id":11,"UserName":"reader","Avatar":"avatar.png","Role":{"Name":"member"}}""");
        }

        public HubEnvelope<byte[]> GetBookInfo(
            LocalBookInfoRequest request,
            LocalGzipOption option)
        {
            state.RecordInvocation(nameof(GetBookInfo), request, option.UseGzip);
            return state.CreateEnvelope(
                """{"Book":{"Id":7,"Title":"SignalR Book","Author":"Writer","Arthur":"Legacy Writer","Cover":"cover.png","Introduction":"Integration fixture","Chapter":[{"Id":701,"Title":"Opening"},{"Id":702,"Title":"Second"}]}}""");
        }

        public HubEnvelope<byte[]> GetNovelContent(
            LocalNovelContentRequest request,
            LocalGzipOption option)
        {
            state.RecordInvocation(nameof(GetNovelContent), request, option.UseGzip);
            return state.CreateEnvelope(
                """{"Chapter":{"Id":702,"BookId":7,"SortNum":2,"Title":"Second","Content":"Chapter body"}}""");
        }
    }
}

internal sealed class LocalSignalRState(
    string? firstHubExceptionMessage,
    int? envelopeFailureStatus,
    bool repeatHubException,
    bool blockNegotiate)
{
    private readonly ConcurrentQueue<string> _bearerTokens = new();
    private readonly ConcurrentQueue<LocalHubInvocation> _invocations = new();
    private readonly TaskCompletionSource _negotiateStarted = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly SemaphoreSlim _connected = new(0);
    private int _connectedCount;
    private int _invocationCount;
    private int _negotiateCount;

    public IReadOnlyCollection<string> BearerTokens => _bearerTokens.ToArray();

    public int ConnectedCount => Volatile.Read(ref _connectedCount);

    public int InvocationCount => Volatile.Read(ref _invocationCount);

    public IReadOnlyList<LocalHubInvocation> Invocations => _invocations.ToArray();

    public int NegotiateCount => Volatile.Read(ref _negotiateCount);

    public Task NegotiateStarted => _negotiateStarted.Task;

    public object? FirstArgument { get; private set; }

    public bool UseGzip { get; private set; }

    public void RecordBearerToken(HttpContext? context)
    {
        if (context is null)
        {
            return;
        }

        var token = context.Request.Query["access_token"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(token))
        {
            var authorization = context.Request.Headers.Authorization.FirstOrDefault();
            if (authorization?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) == true)
            {
                token = authorization["Bearer ".Length..];
            }
        }

        if (!string.IsNullOrWhiteSpace(token))
        {
            _bearerTokens.Enqueue(token);
        }
    }

    public void RecordNegotiate()
    {
        Interlocked.Increment(ref _negotiateCount);
    }

    public async Task BlockNegotiateAsync(CancellationToken cancellationToken)
    {
        if (!blockNegotiate)
        {
            return;
        }

        _negotiateStarted.TrySetResult();
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    }

    public void RecordConnected()
    {
        Interlocked.Increment(ref _connectedCount);
        _connected.Release();
    }

    public async Task WaitForConnectedCountAsync(
        int expectedCount,
        CancellationToken cancellationToken)
    {
        while (ConnectedCount < expectedCount)
        {
            await _connected.WaitAsync(cancellationToken);
        }
    }

    public void RecordInvocation(string methodName, object? firstArgument, bool useGzip)
    {
        FirstArgument = firstArgument;
        UseGzip = useGzip;
        _invocations.Enqueue(new LocalHubInvocation(methodName, firstArgument, useGzip));
        var invocation = Interlocked.Increment(ref _invocationCount);
        if (firstHubExceptionMessage is not null
            && (repeatHubException || invocation == 1))
        {
            throw new HubException(firstHubExceptionMessage);
        }
    }

    public HubEnvelope<byte[]> CreateEnvelope(string json)
    {
        return envelopeFailureStatus is int status && InvocationCount == 1
            ? new HubEnvelope<byte[]>
            {
                Success = false,
                Status = status,
                Msg = "Synthetic envelope failure"
            }
            : new HubEnvelope<byte[]>
            {
                Success = true,
                Response = GzipJson.Compress(json)
            };
    }
}

internal sealed record LocalHubInvocation(
    string MethodName,
    object? Request,
    bool UseGzip);

public sealed class LocalGzipOption
{
    public bool UseGzip { get; set; }
}

public sealed class LocalBookInfoRequest
{
    public long Id { get; set; }
}

public sealed class LocalNovelContentRequest
{
    public long Bid { get; set; }

    public int SortNum { get; set; }

    public string? Convert { get; set; }
}
