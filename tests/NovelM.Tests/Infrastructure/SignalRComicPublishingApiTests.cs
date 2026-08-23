using System.Text.Json;
using System.Text.Json.Nodes;
using NovelM.Tests.TestSupport;
using NovelM_App.Application.Abstractions;
using NovelM_App.Domain.Connection;
using NovelM_App.Domain.Errors;
using NovelM_App.Domain.Publishing;
using NovelM_App.Infrastructure.SignalR;

namespace NovelM.Tests.Infrastructure;

[TestClass]
public sealed class SignalRComicPublishingApiTests
{
    [TestMethod]
    public async Task GetMyBooksAsync_InvokesExactMethodAndMapsResponse()
    {
        var firstUpdatedAt = DateTimeOffset.Parse("2026-08-20T12:34:56+08:00");
        var secondUpdatedAt = DateTimeOffset.Parse("2026-08-21T12:34:56+08:00");
        var response = new ComicPublishingListResponseDto
        {
            Data =
            [
                new ComicPublishingListItemDto
                {
                    Id = 101,
                    Type = "Comic",
                    Title = "First",
                    Cover = "first.png",
                    LastUpdatedAt = firstUpdatedAt,
                    Category = new ComicPublishingListCategoryDto
                    {
                        Name = "原创"
                    }
                },
                new ComicPublishingListItemDto
                {
                    Id = 102,
                    Type = null,
                    Title = "Second",
                    Cover = "second.png",
                    LastUpdatedAt = secondUpdatedAt,
                    Category = null
                }
            ],
            Page = 3,
            TotalPages = 8
        };
        var connection = new RecordingSignalRConnection(response);
        var api = new SignalRComicPublishingApi(connection);
        using var cancellation = new CancellationTokenSource();

        var result = await api.GetMyBooksAsync(
            3,
            24,
            "keyword",
            cancellation.Token);

        Assert.AreEqual(3, result.Page);
        Assert.AreEqual(8, result.TotalPages);
        Assert.AreEqual(2, result.Items.Count);
        Assert.AreEqual(
            new MyComicSummary(
                101,
                "Comic",
                "First",
                "first.png",
                "原创",
                firstUpdatedAt),
            result.Items[0]);
        Assert.AreEqual("Comic", result.Items[1].Type);
        Assert.AreEqual(string.Empty, result.Items[1].CategoryName);

        AssertSingleCall(
            connection,
            "GetMyBooks",
            """{"Page":3,"Size":24,"Type":"Comic","KeyWords":"keyword"}""",
            typeof(ComicPublishingListResponseDto),
            cancellation.Token);
    }

    [TestMethod]
    public async Task QuickCreateComicAsync_InvokesExactMethodAndReturnsId()
    {
        var connection = new RecordingSignalRConnection(456L);
        var api = new SignalRComicPublishingApi(connection);
        using var cancellation = new CancellationTokenSource();

        var result = await api.QuickCreateComicAsync(
            new CreateComicDraft(
                "cover.png",
                "Title",
                "Author",
                "Introduction",
                "连载"),
            cancellation.Token);

        Assert.AreEqual(456L, result);
        AssertSingleCall(
            connection,
            "QuickCreateComic",
            """{"Cover":"cover.png","Title":"Title","Author":"Author","Introduction":"Introduction","CategoryName":"连载"}""",
            typeof(long),
            cancellation.Token);
    }

    [TestMethod]
    public async Task DeleteBookAsync_InvokesExactMethodAndIgnoresResponse()
    {
        var connection = VoidConnection();
        var api = new SignalRComicPublishingApi(connection);
        using var cancellation = new CancellationTokenSource();

        await api.DeleteBookAsync(77, cancellation.Token);

        AssertSingleCall(
            connection,
            "DeleteBook",
            """{"Id":77}""",
            typeof(JsonElement?),
            cancellation.Token);
    }

    [TestMethod]
    public async Task GetBookEditInfoAsync_InvokesExactMethodAndMapsComicFields()
    {
        var response = FullEditResponse();
        var connection = new RecordingSignalRConnection(response);
        var api = new SignalRComicPublishingApi(connection);
        using var cancellation = new CancellationTokenSource();

        var result = await api.GetBookEditInfoAsync(77, cancellation.Token);

        Assert.AreEqual(77L, result.Id);
        Assert.AreEqual("Comic", result.Type);
        Assert.AreEqual("cover.png", result.Cover);
        Assert.AreEqual("Title", result.Title);
        Assert.AreEqual(string.Empty, result.Author);
        Assert.AreEqual("Introduction", result.Introduction);
        Assert.AreEqual(8, result.CategoryId);
        Assert.AreEqual(3, result.Level);
        Assert.AreEqual(2, result.InteriorLevel);
        Assert.IsTrue(result.DownloadAllowed);
        Assert.AreEqual(1234L, result.SubjectId);
        Assert.AreEqual(5678L, result.SeriesId);
        Assert.AreEqual("series", result.SeriesName);
        Assert.AreEqual(string.Empty, result.SeriesNameCn);
        CollectionAssert.AreEqual(
            new[] { "奇幻", "冒险" },
            result.Tags.ToArray());
        CollectionAssert.AreEqual(
            new[]
            {
                new ComicCategory(7, "原创"),
                new ComicCategory(9, "完结"),
                new ComicCategory(10, "连载")
            },
            result.Categories.ToArray());
        CollectionAssert.AreEqual(
            new[]
            {
                new ComicChapterSummary(701, 9, "Explicit sort"),
                new ComicChapterSummary(702, 2, "Fallback sort")
            },
            result.Chapters.ToArray());

        AssertSingleCall(
            connection,
            "GetBookEditInfo",
            """{"Id":77}""",
            typeof(ComicBookEditResponseDto),
            cancellation.Token);
    }

    [TestMethod]
    public async Task UpdateComicInfoAsync_InvokesUpdateBookWithOnlyInfoFields()
    {
        var connection = VoidConnection();
        var api = new SignalRComicPublishingApi(connection);
        using var cancellation = new CancellationTokenSource();

        await api.UpdateComicInfoAsync(
            77,
            new ComicInfoDraft(
                "new-cover.png",
                "New title",
                "New author",
                "New introduction",
                9),
            cancellation.Token);

        AssertSingleCall(
            connection,
            "UpdateBook",
            """{"Id":77,"Map":{"Cover":"new-cover.png","Title":"New title","Author":"New author","Introduction":"New introduction","CategoryId":9}}""",
            typeof(JsonElement?),
            cancellation.Token);
    }

    [TestMethod]
    public async Task UpdateComicSettingsAsync_InvokesUpdateBookWithOnlySettingsFieldsAndKeepsNullIds()
    {
        var connection = VoidConnection();
        var api = new SignalRComicPublishingApi(connection);
        using var cancellation = new CancellationTokenSource();

        await api.UpdateComicSettingsAsync(
            77,
            new ComicSettingsDraft(
                4,
                3,
                false,
                null,
                null,
                "Series",
                "系列",
                ["奇幻", "治愈"]),
            cancellation.Token);

        AssertSingleCall(
            connection,
            "UpdateBook",
            """{"Id":77,"Map":{"Level":4,"InteriorLevel":3,"DownloadAllowed":false,"SubjectId":null,"SeriesId":null,"SeriesName":"Series","SeriesNameCn":"系列","Tags":["奇幻","治愈"]}}""",
            typeof(JsonElement?),
            cancellation.Token);
    }

    [TestMethod]
    public async Task GetComicEditInfoAsync_InvokesExactMethodAndUsesRequestedChapterId()
    {
        var response = new ComicChapterEditResponseDto
        {
            Title = "Chapter title",
            Images = ["1.png", "2.png"]
        };
        var connection = new RecordingSignalRConnection(response);
        var api = new SignalRComicPublishingApi(connection);
        using var cancellation = new CancellationTokenSource();

        var result = await api.GetComicEditInfoAsync(77, 701, cancellation.Token);

        Assert.AreEqual(701L, result.Id);
        Assert.AreEqual("Chapter title", result.Title);
        CollectionAssert.AreEqual(
            new[] { "1.png", "2.png" },
            result.Images.ToArray());
        AssertSingleCall(
            connection,
            "GetComicEditInfo",
            """{"Bid":77,"Cid":701}""",
            typeof(ComicChapterEditResponseDto),
            cancellation.Token);
    }

    [TestMethod]
    public async Task UpdateComicChapterAsync_InvokesExactMethodAndIgnoresResponse()
    {
        var connection = VoidConnection();
        var api = new SignalRComicPublishingApi(connection);
        using var cancellation = new CancellationTokenSource();

        await api.UpdateComicChapterAsync(
            701,
            new ComicChapterDraft(701, "Updated", ["1.png", "2.png"]),
            cancellation.Token);

        AssertSingleCall(
            connection,
            "UpdateComicChapter",
            """{"Cid":701,"Map":{"Title":"Updated","Images":["1.png","2.png"]}}""",
            typeof(JsonElement?),
            cancellation.Token);
    }

    [TestMethod]
    public async Task CreateNewComicChapterAsync_InvokesExactMethodAndMapsResponse()
    {
        var response = new ComicChapterCreateResponseDto
        {
            Chapters =
            [
                new ComicPublishingChapterDto
                {
                    Id = 701,
                    SortNum = null,
                    Title = "First"
                },
                new ComicPublishingChapterDto
                {
                    Id = 702,
                    SortNum = 8,
                    Title = "Second"
                }
            ],
            NewCid = 702
        };
        var connection = new RecordingSignalRConnection(response);
        var api = new SignalRComicPublishingApi(connection);
        using var cancellation = new CancellationTokenSource();

        var result = await api.CreateNewComicChapterAsync(
            77,
            2,
            new ComicChapterDraft(0, "Second", ["2.png"]),
            cancellation.Token);

        Assert.AreEqual(702L, result.NewChapterId);
        CollectionAssert.AreEqual(
            new[]
            {
                new ComicChapterSummary(701, 1, "First"),
                new ComicChapterSummary(702, 8, "Second")
            },
            result.Chapters.ToArray());
        AssertSingleCall(
            connection,
            "CreateNewComicChapter",
            """{"Bid":77,"SortNum":2,"Map":{"Title":"Second","Images":["2.png"]}}""",
            typeof(ComicChapterCreateResponseDto),
            cancellation.Token);
    }

    [TestMethod]
    public async Task DeleteChapterAsync_InvokesExactMethodAndIgnoresResponse()
    {
        var connection = VoidConnection();
        var api = new SignalRComicPublishingApi(connection);
        using var cancellation = new CancellationTokenSource();

        await api.DeleteChapterAsync(77, 3, cancellation.Token);

        AssertSingleCall(
            connection,
            "DeleteChapter",
            """{"Bid":77,"SortNum":3}""",
            typeof(JsonElement?),
            cancellation.Token);
    }

    [TestMethod]
    public async Task ReorderChapterAsync_InvokesExactMethodAndIgnoresResponse()
    {
        var connection = VoidConnection();
        var api = new SignalRComicPublishingApi(connection);
        using var cancellation = new CancellationTokenSource();

        await api.ReorderChapterAsync(77, 3, 1, cancellation.Token);

        AssertSingleCall(
            connection,
            "ReorderChapter",
            """{"BookId":77,"OldSortNum":3,"NewSortNum":1}""",
            typeof(JsonElement?),
            cancellation.Token);
    }

    [TestMethod]
    public async Task UploadImageAsync_InvokesExactMethodAndReturnsUrl()
    {
        var response = new UploadComicImageResponseDto
        {
            Url = "https://example.test/image.png"
        };
        var connection = new RecordingSignalRConnection(response);
        var api = new SignalRComicPublishingApi(connection);
        using var cancellation = new CancellationTokenSource();

        var result = await api.UploadImageAsync(
            new LocalImageFile("image.png", [1, 2, 3]),
            cancellation.Token);

        Assert.AreEqual("https://example.test/image.png", result);
        AssertSingleCall(
            connection,
            "UploadImage",
            """{"FileName":"image.png","ImageData":"AQID"}""",
            typeof(UploadComicImageResponseDto),
            cancellation.Token);
    }

    [TestMethod]
    public async Task DecodeMyBooksResponse_RealGzipMapsDateTimeOffsetList()
    {
        var response = Decode<ComicPublishingListResponseDto>(
            """
            {
              "data": [
                {
                  "id": 101,
                  "title": "First",
                  "cover": "first.png",
                  "lastUpdatedAt": "2026-08-20T12:34:56+08:00",
                  "category": { "name": "原创" }
                }
              ],
              "page": 1,
              "totalPages": 2
            }
            """,
            "GetMyBooks");
        var api = new SignalRComicPublishingApi(
            new RecordingSignalRConnection(response));

        var result = await api.GetMyBooksAsync(
            1,
            24,
            string.Empty,
            CancellationToken.None);

        Assert.AreEqual(
            DateTimeOffset.Parse("2026-08-20T12:34:56+08:00"),
            result.Items[0].LastUpdatedAt);
        Assert.AreEqual("Comic", result.Items[0].Type);
        Assert.AreEqual("原创", result.Items[0].CategoryName);
    }

    [TestMethod]
    public async Task DecodeBookEditResponse_RealGzipMapsSnakeCaseAndNullableFields()
    {
        var response = Decode<ComicBookEditResponseDto>(
            """
            {
              "book": {
                "id": 77,
                "type": "Comic",
                "cover": "cover.png",
                "title": "Title",
                "author": null,
                "introduction": "Introduction",
                "categoryId": 8,
                "level": 3,
                "interiorLevel": 2,
                "downloadAllowed": true,
                "extra": {
                  "classification": {
                    "subject_id": 1234,
                    "series_id": null,
                    "series_name": null,
                    "series_name_cn": "系列",
                    "tags": null,
                    "classified_at": "2026-08-20T12:34:56+08:00"
                  }
                },
                "chapters": [
                  { "id": 701, "title": "Chapter" }
                ]
              },
              "categories": [
                { "id": 1, "name": "翻译中" },
                { "id": 7, "name": "原创" }
              ]
            }
            """,
            "GetBookEditInfo");
        var api = new SignalRComicPublishingApi(
            new RecordingSignalRConnection(response));

        var result = await api.GetBookEditInfoAsync(
            77,
            CancellationToken.None);

        Assert.AreEqual(string.Empty, result.Author);
        Assert.AreEqual(1234L, result.SubjectId);
        Assert.IsNull(result.SeriesId);
        Assert.AreEqual(string.Empty, result.SeriesName);
        Assert.AreEqual("系列", result.SeriesNameCn);
        Assert.AreEqual(0, result.Tags.Count);
        Assert.AreEqual(1, result.Chapters[0].SortNum);
        CollectionAssert.AreEqual(
            new[] { new ComicCategory(7, "原创") },
            result.Categories.ToArray());
        Assert.AreEqual(
            DateTimeOffset.Parse("2026-08-20T12:34:56+08:00"),
            response.Book.Extra?.Classification?.ClassifiedAt);
    }

    [TestMethod]
    public void DecodeBookEditResponse_MissingRequiredFieldThrowsProtocolError()
    {
        var exception = Assert.ThrowsExactly<AppException>(() =>
            Decode<ComicBookEditResponseDto>(
                """
                {
                  "Book": {
                    "Id": 77,
                    "Type": "Comic",
                    "Cover": "cover.png",
                    "Author": null,
                    "Introduction": "Introduction",
                    "CategoryId": 8,
                    "Level": 3,
                    "InteriorLevel": 2,
                    "DownloadAllowed": true,
                    "Chapters": []
                  },
                  "Categories": []
                }
                """,
                "GetBookEditInfo"));

        Assert.AreEqual(AppErrorKind.Protocol, exception.Kind);
        StringAssert.Contains(exception.Message, "GetBookEditInfo");
    }

    [TestMethod]
    public async Task DecodeComicEditResponse_NullImagesMapsEmpty()
    {
        var response = Decode<ComicChapterEditResponseDto>(
            """{"Title":"Chapter","Images":null}""",
            "GetComicEditInfo");
        var api = new SignalRComicPublishingApi(
            new RecordingSignalRConnection(response));

        var result = await api.GetComicEditInfoAsync(
            77,
            701,
            CancellationToken.None);

        Assert.AreEqual(0, result.Images.Count);
    }

    [TestMethod]
    [DataRow("Data")]
    [DataRow("Categories")]
    [DataRow("Chapters")]
    [DataRow("Tags")]
    [DataRow("Images")]
    [DataRow("CreatedChapters")]
    public async Task Mapping_NullCollectionElementThrowsProtocolError(string field)
    {
        var action = BuildNullElementAction(field);

        var exception = await Assert.ThrowsExactlyAsync<AppException>(action);

        Assert.AreEqual(AppErrorKind.Protocol, exception.Kind);
        StringAssert.Contains(exception.Message, ExpectedMethodName(field));
    }

    private static Func<Task> BuildNullElementAction(string field)
    {
        return field switch
        {
            "Data" => () => new SignalRComicPublishingApi(
                    new RecordingSignalRConnection(
                        new ComicPublishingListResponseDto
                        {
                            Data = new ComicPublishingListItemDto?[] { null },
                            Page = 1,
                            TotalPages = 1
                        }))
                .GetMyBooksAsync(1, 24, string.Empty, CancellationToken.None),
            "Categories" => () => new SignalRComicPublishingApi(
                    new RecordingSignalRConnection(
                        FullEditResponse(
                            categories: new ComicPublishingCategoryDto?[] { null })))
                .GetBookEditInfoAsync(77, CancellationToken.None),
            "Chapters" => () => new SignalRComicPublishingApi(
                    new RecordingSignalRConnection(
                        FullEditResponse(
                            chapters: new ComicPublishingChapterDto?[] { null })))
                .GetBookEditInfoAsync(77, CancellationToken.None),
            "Tags" => () => new SignalRComicPublishingApi(
                    new RecordingSignalRConnection(
                        FullEditResponse(tags: new string?[] { null })))
                .GetBookEditInfoAsync(77, CancellationToken.None),
            "Images" => () => new SignalRComicPublishingApi(
                    new RecordingSignalRConnection(
                        new ComicChapterEditResponseDto
                        {
                            Title = "Chapter",
                            Images = new string?[] { null }
                        }))
                .GetComicEditInfoAsync(77, 701, CancellationToken.None),
            "CreatedChapters" => () => new SignalRComicPublishingApi(
                    new RecordingSignalRConnection(
                        new ComicChapterCreateResponseDto
                        {
                            Chapters = new ComicPublishingChapterDto?[] { null },
                            NewCid = 701
                        }))
                .CreateNewComicChapterAsync(
                    77,
                    1,
                    new ComicChapterDraft(0, "Chapter", ["1.png"]),
                    CancellationToken.None),
            _ => throw new AssertFailedException($"Unexpected field '{field}'.")
        };
    }

    private static string ExpectedMethodName(string field)
    {
        return field switch
        {
            "Data" => "GetMyBooks",
            "Categories" or "Chapters" or "Tags" => "GetBookEditInfo",
            "Images" => "GetComicEditInfo",
            "CreatedChapters" => "CreateNewComicChapter",
            _ => throw new AssertFailedException($"Unexpected field '{field}'.")
        };
    }

    private static ComicBookEditResponseDto FullEditResponse(
        IReadOnlyList<ComicPublishingCategoryDto?>? categories = null,
        IReadOnlyList<ComicPublishingChapterDto?>? chapters = null,
        IReadOnlyList<string?>? tags = null)
    {
        return new ComicBookEditResponseDto
        {
            Book = new ComicPublishingBookEditDto
            {
                Id = 77,
                Type = "Comic",
                Cover = "cover.png",
                Title = "Title",
                Author = null,
                Introduction = "Introduction",
                CategoryId = 8,
                Level = 3,
                InteriorLevel = 2,
                DownloadAllowed = true,
                Extra = new ComicExtraDto
                {
                    Classification = new ComicClassificationDto
                    {
                        SubjectId = 1234,
                        SeriesId = 5678,
                        SeriesName = "series",
                        SeriesNameCn = null,
                        Tags = tags ?? new string?[] { "奇幻", "冒险" }
                    }
                },
                Chapters = chapters ??
                [
                    new ComicPublishingChapterDto
                    {
                        Id = 701,
                        SortNum = 9,
                        Title = "Explicit sort"
                    },
                    new ComicPublishingChapterDto
                    {
                        Id = 702,
                        SortNum = null,
                        Title = "Fallback sort"
                    }
                ]
            },
            Categories = categories ??
            [
                new ComicPublishingCategoryDto { Id = 1, Name = "翻译中" },
                new ComicPublishingCategoryDto { Id = 7, Name = "原创" },
                new ComicPublishingCategoryDto { Id = 9, Name = "完结" },
                new ComicPublishingCategoryDto { Id = 2, Name = "录入中" },
                new ComicPublishingCategoryDto { Id = 10, Name = "连载" }
            ]
        };
    }

    private static RecordingSignalRConnection VoidConnection()
    {
        using var document = JsonDocument.Parse("""{"Ignored":true}""");
        return new RecordingSignalRConnection(document.RootElement.Clone());
    }

    private static T Decode<T>(string json, string methodName)
    {
        var envelope = new HubEnvelope<byte[]>
        {
            Success = true,
            Response = GzipJson.Compress(json)
        };

        return new CompressedResponseDecoder().Decode<T>(envelope, methodName);
    }

    private static void AssertSingleCall(
        RecordingSignalRConnection connection,
        string expectedMethodName,
        string expectedPayload,
        Type expectedResponseType,
        CancellationToken expectedCancellationToken)
    {
        Assert.AreEqual(1, connection.Calls.Count);
        var call = connection.Calls[0];
        Assert.AreEqual(expectedMethodName, call.MethodName);
        Assert.AreEqual(expectedResponseType, call.ResponseType);
        Assert.AreEqual(expectedCancellationToken, call.CancellationToken);
        AssertJsonEquivalent(expectedPayload, call.Request);
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

    private sealed class RecordingSignalRConnection : ISignalRConnection
    {
        private readonly object? _response;

        public RecordingSignalRConnection(object? response)
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
            Calls.Add(new Invocation(
                methodName,
                request,
                cancellationToken,
                typeof(T)));

            if (_response is T typedResponse)
            {
                return Task.FromResult(typedResponse);
            }

            if (_response is JsonElement element)
            {
                return Task.FromResult(
                    JsonSerializer.Deserialize<T>(element.GetRawText())!);
            }

            throw new AssertFailedException(
                $"Expected response type '{typeof(T)}', but configured '{_response?.GetType()}'.");
        }
    }
}
