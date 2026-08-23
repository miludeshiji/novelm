using System.Text.Json;
using System.Text.Json.Nodes;
using NovelM_App.Application.Abstractions;
using NovelM_App.Domain.Connection;
using NovelM_App.Domain.Manga;
using NovelM_App.Infrastructure.SignalR;

namespace NovelM.Tests.Infrastructure;

[TestClass]
public sealed class SignalRMangaApiTests
{
    [TestMethod]
    public async Task GetListAsync_InvokesExactMethodAndPayload()
    {
        var updatedAt = DateTimeOffset.Parse("2026-08-20T12:34:56+08:00");
        var response = new ComicListResponseDto
        {
            Data = new[]
            {
                new ComicListItemDto
                {
                    Id = 123,
                    Title = "Frieren",
                    OriginalTitle = null,
                    Cover = "frieren.png",
                    Count = 37,
                    LastUpdatedAt = updatedAt
                }
            },
            Page = 2,
            TotalPages = 9
        };
        var connection = new TypedFakeSignalRConnection<ComicListResponseDto>(response);
        var api = new SignalRMangaApi(connection);
        using var cancellation = new CancellationTokenSource();

        var result = await api.GetListAsync(2, 24, ComicOrder.View, cancellation.Token);

        Assert.AreEqual(2, result.Page);
        Assert.AreEqual(9, result.TotalPages);
        Assert.AreEqual(1, result.Items.Count);
        Assert.AreEqual("Frieren", result.Items[0].SeriesTitle);
        Assert.AreEqual("Frieren", result.Items[0].Title);
        Assert.IsNull(result.Items[0].OriginalTitle);
        Assert.AreEqual("frieren.png", result.Items[0].Cover);
        Assert.AreEqual(37, result.Items[0].ChapterCount);
        Assert.AreEqual(updatedAt, result.Items[0].LastUpdatedAt);

        var call = AssertSingleCall(connection);
        Assert.AreEqual(HubMethodNames.GetComicList, call.MethodName);
        Assert.AreEqual(typeof(ComicListResponseDto), call.ResponseType);
        Assert.AreEqual(cancellation.Token, call.CancellationToken);
        AssertJsonEquivalent(
            """{"Page":2,"Size":24,"Order":"view"}""",
            call.Request);
    }

    [TestMethod]
    public async Task SearchAsync_DoesNotSendOrder()
    {
        var connection = new TypedFakeSignalRConnection<ComicListResponseDto>(
            EmptyListResponse());
        var api = new SignalRMangaApi(connection);
        using var cancellation = new CancellationTokenSource();

        await api.SearchAsync("芙莉莲", "fuzzy", 1, 24, cancellation.Token);

        var call = AssertSingleCall(connection);
        Assert.AreEqual(HubMethodNames.SearchComicSeries, call.MethodName);
        Assert.AreEqual(typeof(ComicListResponseDto), call.ResponseType);
        Assert.AreEqual(cancellation.Token, call.CancellationToken);
        AssertJsonEquivalent(
            """{"KeyWords":"芙莉莲","Mode":"fuzzy","Page":1,"Size":24}""",
            call.Request);
    }

    [TestMethod]
    [DataRow(ComicOrder.Latest, "latest")]
    [DataRow(ComicOrder.New, "new")]
    [DataRow(ComicOrder.View, "view")]
    public async Task GetListAsync_MapsOrderToWireValue(
        ComicOrder order,
        string wireValue)
    {
        var connection = new TypedFakeSignalRConnection<ComicListResponseDto>(
            EmptyListResponse());
        var api = new SignalRMangaApi(connection);

        await api.GetListAsync(1, 1, order, CancellationToken.None);

        var call = AssertSingleCall(connection);
        using var request = JsonDocument.Parse(JsonSerializer.Serialize(call.Request));
        Assert.AreEqual(
            wireValue,
            request.RootElement.GetProperty("Order").GetString());
    }

    [TestMethod]
    public async Task GetSeriesAsync_MapsVolumesChaptersAndTags()
    {
        var seriesUpdatedAt = DateTimeOffset.Parse("2026-08-21T10:00:00+08:00");
        var chapterCreatedAt = DateTimeOffset.Parse("2026-08-01T08:00:00+08:00");
        var response = new ComicSeriesInfoResponseDto
        {
            Series = new ComicSeriesDto
            {
                Id = "series-frieren",
                Title = "Frieren",
                OriginalTitle = "葬送のフリーレン",
                Cover = "series.png",
                Author = null,
                Views = 9876,
                Favorite = 432,
                Introduction = "Journey after the adventure.",
                CreatedAt = DateTimeOffset.Parse("2025-01-01T00:00:00Z"),
                LastUpdatedChapter = "Chapter 12",
                LastUpdatedAt = seriesUpdatedAt,
                Extra = new ComicExtraDto
                {
                    Classification = new ComicClassificationDto
                    {
                        Author = "Kanehito Yamada",
                        SubjectId = 40000,
                        SeriesId = 50000,
                        SeriesName = "Sousou no Frieren",
                        SeriesNameCn = "葬送的芙莉莲",
                        Tags = new[] { "奇幻", "冒险" },
                        ClassifiedAt = DateTimeOffset.Parse("2026-08-20T00:00:00Z")
                    }
                }
            },
            Books = new[]
            {
                new ComicBookDto
                {
                    Id = 88,
                    Title = "Volume 1",
                    Uploader = new ComicUploaderDto
                    {
                        UserName = "reader",
                        Avatar = "avatar.png"
                    },
                    CanDownload = true,
                    Cover = "volume.png",
                    CreatedAt = DateTimeOffset.Parse("2026-07-01T00:00:00Z"),
                    LastUpdatedChapter = "Chapter 12",
                    LastUpdatedAt = seriesUpdatedAt,
                    ReadPosition = new ComicReadPositionDto
                    {
                        ChapterId = 901,
                        Position = "3",
                        ReadAt = DateTimeOffset.Parse("2026-08-22T00:00:00Z")
                    },
                    Chapters = new[]
                    {
                        new ComicChapterSummaryDto
                        {
                            Id = 901,
                            SortNum = 12,
                            Title = "A new beginning",
                            CreatedAt = chapterCreatedAt,
                            UpdatedAt = null,
                            PageCount = 12,
                            DownloadCost = 5
                        }
                    }
                }
            }
        };
        var connection = new TypedFakeSignalRConnection<ComicSeriesInfoResponseDto>(response);
        var api = new SignalRMangaApi(connection);
        using var cancellation = new CancellationTokenSource();

        var result = await api.GetSeriesAsync(
            "Frieren",
            ComicOrder.Latest,
            cancellation.Token);

        Assert.AreEqual("series-frieren", result.Id);
        Assert.AreEqual("Frieren", result.Title);
        Assert.AreEqual("葬送のフリーレン", result.OriginalTitle);
        Assert.AreEqual("series.png", result.Cover);
        Assert.AreEqual("Kanehito Yamada", result.Author);
        Assert.AreEqual(9876L, result.Views);
        Assert.AreEqual(432L, result.Favorite);
        Assert.AreEqual("Journey after the adventure.", result.Introduction);
        Assert.AreEqual("Chapter 12", result.LastUpdatedChapter);
        Assert.AreEqual(seriesUpdatedAt, result.LastUpdatedAt);
        CollectionAssert.AreEqual(
            new[] { "奇幻", "冒险" },
            result.Tags.ToArray());

        Assert.AreEqual(1, result.Volumes.Count);
        var volume = result.Volumes[0];
        Assert.AreEqual(88L, volume.Id);
        Assert.AreEqual("Volume 1", volume.Title);
        Assert.AreEqual("volume.png", volume.Cover);
        Assert.AreEqual("reader", volume.UploaderName);
        Assert.AreEqual(1, volume.Chapters.Count);
        Assert.AreEqual(
            new MangaChapterSummary(
                901,
                12,
                "A new beginning",
                chapterCreatedAt,
                null,
                12,
                5),
            volume.Chapters[0]);

        var call = AssertSingleCall(connection);
        Assert.AreEqual(HubMethodNames.GetComicSeriesInfo, call.MethodName);
        Assert.AreEqual(typeof(ComicSeriesInfoResponseDto), call.ResponseType);
        Assert.AreEqual(cancellation.Token, call.CancellationToken);
        AssertJsonEquivalent(
            """{"SeriesTitle":"Frieren","Order":"latest"}""",
            call.Request);
    }

    [TestMethod]
    public async Task GetSeriesAsync_PrefersSeriesAuthorAndMapsMissingTagsToEmpty()
    {
        var response = new ComicSeriesInfoResponseDto
        {
            Series = new ComicSeriesDto
            {
                Id = "series",
                Title = "Title",
                OriginalTitle = null,
                Cover = "cover.png",
                Author = "primary author",
                Views = 0,
                Favorite = 0,
                Introduction = string.Empty,
                CreatedAt = DateTimeOffset.UnixEpoch,
                LastUpdatedChapter = string.Empty,
                LastUpdatedAt = DateTimeOffset.UnixEpoch,
                Extra = new ComicExtraDto
                {
                    Classification = new ComicClassificationDto
                    {
                        Author = "fallback author",
                        Tags = null
                    }
                }
            },
            Books = Array.Empty<ComicBookDto>()
        };
        var api = new SignalRMangaApi(
            new TypedFakeSignalRConnection<ComicSeriesInfoResponseDto>(response));

        var result = await api.GetSeriesAsync(
            "Title",
            ComicOrder.New,
            CancellationToken.None);

        Assert.AreEqual("primary author", result.Author);
        Assert.AreEqual(0, result.Tags.Count);
        Assert.AreEqual(0, result.Volumes.Count);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public async Task GetSeriesAsync_BlankAuthorFallsBackToClassification(
        string author)
    {
        var response = new ComicSeriesInfoResponseDto
        {
            Series = new ComicSeriesDto
            {
                Id = "series",
                Title = "Title",
                OriginalTitle = null,
                Cover = "cover.png",
                Author = author,
                Views = 0,
                Favorite = 0,
                Introduction = string.Empty,
                CreatedAt = DateTimeOffset.UnixEpoch,
                LastUpdatedChapter = string.Empty,
                LastUpdatedAt = DateTimeOffset.UnixEpoch,
                Extra = new ComicExtraDto
                {
                    Classification = new ComicClassificationDto
                    {
                        Author = "fallback author"
                    }
                }
            },
            Books = Array.Empty<ComicBookDto>()
        };
        var api = new SignalRMangaApi(
            new TypedFakeSignalRConnection<ComicSeriesInfoResponseDto>(response));

        var result = await api.GetSeriesAsync(
            "Title",
            ComicOrder.Latest,
            CancellationToken.None);

        Assert.AreEqual("fallback author", result.Author);
    }

    private static ComicListResponseDto EmptyListResponse()
    {
        return new ComicListResponseDto
        {
            Data = Array.Empty<ComicListItemDto>(),
            Page = 1,
            TotalPages = 0
        };
    }

    private static Invocation AssertSingleCall<TResponse>(
        TypedFakeSignalRConnection<TResponse> connection)
    {
        Assert.AreEqual(1, connection.Calls.Count);
        return connection.Calls[0];
    }

    private static void AssertJsonEquivalent(string expected, object? actual)
    {
        Assert.IsNotNull(actual);
        var expectedNode = JsonNode.Parse(expected);
        var actualNode = JsonNode.Parse(JsonSerializer.Serialize(actual));
        Assert.IsTrue(
            JsonNode.DeepEquals(expectedNode, actualNode),
            $"Expected {expectedNode}, but got {actualNode}.");
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
    }
}
