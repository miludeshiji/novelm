using System.Collections.Concurrent;

namespace NovelM.Tests.TestSupport;

internal sealed class LocalApiHandler : HttpMessageHandler
{
    private readonly Func<int, HttpResponseMessage> _responseFactory;
    private readonly ConcurrentQueue<CapturedApiRequest> _requests = new();
    private int _nextRequestIndex;

    public LocalApiHandler(Func<int, HttpResponseMessage> responseFactory)
    {
        _responseFactory = responseFactory;
    }

    public LocalApiHandler(Exception failure)
        : this(_ => throw failure)
    {
    }

    public IReadOnlyList<CapturedApiRequest> Requests => _requests.ToArray();

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var requestIndex = Interlocked.Increment(ref _nextRequestIndex) - 1;
        var body = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken);

        _requests.Enqueue(new CapturedApiRequest(
            request.Method,
            request.RequestUri,
            CloneHeaders(request.Headers),
            request.Content is null
                ? new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
                : CloneHeaders(request.Content.Headers),
            body));

        return _responseFactory(requestIndex);
    }

    private static IReadOnlyDictionary<string, string[]> CloneHeaders(
        IEnumerable<KeyValuePair<string, IEnumerable<string>>> headers)
    {
        return headers.ToDictionary(
            header => header.Key,
            header => header.Value.ToArray(),
            StringComparer.OrdinalIgnoreCase);
    }
}

internal sealed record CapturedApiRequest(
    HttpMethod Method,
    Uri? RequestUri,
    IReadOnlyDictionary<string, string[]> Headers,
    IReadOnlyDictionary<string, string[]> ContentHeaders,
    string? Body);
