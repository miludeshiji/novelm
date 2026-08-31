using System.Reflection;
using NovelM_App.Application.Abstractions;
using NovelM_App.Domain.Books;
using NovelM_App.Domain.Connection;
using NovelM_App.Infrastructure.SignalR;

namespace NovelM.Tests.Infrastructure;

[TestClass]
public sealed class SignalRBookApiTests
{
    [TestMethod]
    public void SignalRBookApi_IsInternal()
    {
        Assert.IsFalse(typeof(SignalRBookApi).IsPublic);
    }

    [TestMethod]
    public async Task GetBookAsync_MapsAllFieldsAndLegacyAuthorAndServerOrder()
    {
        var response = CreateBookResponse(author: null, arthur: "legacy author");
        var connection = new TypedFakeSignalRConnection<BookResponseDto>(response);
        var api = new SignalRBookApi(connection);
        using var cancellation = new CancellationTokenSource();

        var result = await api.GetBookAsync(42, cancellation.Token);

        Assert.AreEqual(42L, result.Id);
        Assert.AreEqual("Book title", result.Title);
        Assert.AreEqual("legacy author", result.Author);
        Assert.AreEqual("cover.png", result.Cover);
        Assert.AreEqual("Introduction", result.Introduction);
        Assert.AreEqual(2, result.Chapters.Count);
        Assert.AreEqual(new ChapterSummary(900, "Server first", 1), result.Chapters[0]);
        Assert.AreEqual(new ChapterSummary(3, "Server second", 2), result.Chapters[1]);

        Assert.AreEqual(1, connection.Calls.Count);
        var call = connection.Calls[0];
        Assert.AreEqual(HubMethodNames.GetBookInfo, call.MethodName);
        Assert.AreEqual(cancellation.Token, call.CancellationToken);
        Assert.AreEqual(typeof(BookResponseDto), call.ResponseType);
        AssertRequestShape(call.Request, ("Id", 42L));
    }

    [TestMethod]
    public async Task GetBookAsync_AuthorWinsWhenBothFieldsArePresent()
    {
        var response = CreateBookResponse("canonical author", "legacy author");
        var api = new SignalRBookApi(
            new TypedFakeSignalRConnection<BookResponseDto>(response));

        var result = await api.GetBookAsync(42, CancellationToken.None);

        Assert.AreEqual("canonical author", result.Author);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public async Task GetBookAsync_BlankAuthorFallsBackToArthur(string author)
    {
        var response = CreateBookResponse(author, "legacy author");
        var api = new SignalRBookApi(
            new TypedFakeSignalRConnection<BookResponseDto>(response));

        var result = await api.GetBookAsync(42, CancellationToken.None);

        Assert.AreEqual("legacy author", result.Author);
    }

    [TestMethod]
    public async Task GetBookAsync_NoUsableAuthorMapsEmptyDisplayString()
    {
        var response = CreateBookResponse(" ", null);
        var api = new SignalRBookApi(
            new TypedFakeSignalRConnection<BookResponseDto>(response));

        var result = await api.GetBookAsync(42, CancellationToken.None);

        Assert.AreEqual(string.Empty, result.Author);
    }

    [TestMethod]
    public async Task GetChapterAsync_InvokesExactRequestAndMapsAllFields()
    {
        const string body = "Chapter body that must not be cached";
        var response = new ChapterResponseDto
        {
            Chapter = new ChapterDto
            {
                Id = 700,
                BookId = 42,
                SortNum = 7,
                Title = "Chapter title",
                Content = body
            }
        };
        var connection = new TypedFakeSignalRConnection<ChapterResponseDto>(response);
        var api = new SignalRBookApi(connection);
        using var cancellation = new CancellationTokenSource();

        var result = await api.GetChapterAsync(42, 7, cancellation.Token);

        Assert.AreEqual(700L, result.Id);
        Assert.AreEqual(42L, result.BookId);
        Assert.AreEqual(7, result.SortNum);
        Assert.AreEqual("Chapter title", result.Title);
        Assert.AreEqual(body, result.Content);

        Assert.AreEqual(1, connection.Calls.Count);
        var call = connection.Calls[0];
        Assert.AreEqual(HubMethodNames.GetNovelContent, call.MethodName);
        Assert.AreEqual(cancellation.Token, call.CancellationToken);
        Assert.AreEqual(typeof(ChapterResponseDto), call.ResponseType);
        AssertRequestShape(
            call.Request,
            ("Bid", 42L),
            ("SortNum", 7),
            ("Convert", null));
        Assert.IsFalse(typeof(SignalRBookApi)
            .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Any(field => field.FieldType == typeof(ChapterContent)
                || field.FieldType == typeof(string)));
    }

    private static BookResponseDto CreateBookResponse(string? author, string? arthur)
    {
        return new BookResponseDto
        {
            Book = new BookDto
            {
                Id = 42,
                Title = "Book title",
                Author = author,
                Arthur = arthur,
                Cover = "cover.png",
                Introduction = "Introduction",
                Chapter = new[]
                {
                    new ChapterSummaryDto { Id = 900, Title = "Server first" },
                    new ChapterSummaryDto { Id = 3, Title = "Server second" }
                }
            }
        };
    }

    private static void AssertRequestShape(
        object? request,
        params (string Name, object? Value)[] expected)
    {
        Assert.IsNotNull(request);
        var properties = request.GetType()
            .GetProperties(BindingFlags.Instance | BindingFlags.Public);
        CollectionAssert.AreEquivalent(
            expected.Select(item => item.Name).ToArray(),
            properties.Select(property => property.Name).ToArray());

        foreach (var item in expected)
        {
            var property = properties.Single(candidate => candidate.Name == item.Name);
            Assert.AreEqual(item.Value, property.GetValue(request));
        }
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
