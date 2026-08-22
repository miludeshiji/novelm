using System.Text;
using System.Text.Json.Serialization;
using NovelM.Tests.TestSupport;
using NovelM_App.Domain.Books;
using NovelM_App.Domain.Errors;
using NovelM_App.Infrastructure.SignalR;

namespace NovelM.Tests.Infrastructure;

[TestClass]
public sealed class CompressedResponseDecoderTests
{
    private readonly CompressedResponseDecoder _decoder = new();

    [TestMethod]
    public void Decode_RealGzipWithMixedPropertyCasing_ReturnsTypedValue()
    {
        var envelope = Successful(GzipJson.Compress("""{"iD":42,"userNAME":"reader"}"""));

        var result = _decoder.Decode<TestPayload>(envelope, "GetProfile");

        Assert.AreEqual(42, result.Id);
        Assert.AreEqual("reader", result.UserName);
    }

    [TestMethod]
    public void Decode_Unsuccessful403_ThrowsServerErrorWithStatus()
    {
        var envelope = new HubEnvelope<byte[]>
        {
            Success = false,
            Status = 403,
            Msg = "Forbidden"
        };

        var exception = Assert.ThrowsExactly<AppException>(
            () => _decoder.Decode<TestPayload>(envelope, "GetProfile"));

        Assert.AreEqual(AppErrorKind.Server, exception.Kind);
        Assert.AreEqual(403, exception.Status);
    }

    [TestMethod]
    [DataRow(-100)]
    [DataRow(404)]
    public void Decode_UnauthorizedStatus_ThrowsUnauthorizedError(int status)
    {
        var envelope = new HubEnvelope<byte[]>
        {
            Success = false,
            Status = status,
            Msg = "Sign in again"
        };

        var exception = Assert.ThrowsExactly<AppException>(
            () => _decoder.Decode<TestPayload>(envelope, "GetProfile"));

        Assert.AreEqual(AppErrorKind.Unauthorized, exception.Kind);
        Assert.AreEqual(status, exception.Status);
    }

    [TestMethod]
    public void Decode_SuccessfulNullResponse_ThrowsProtocolError()
    {
        var envelope = new HubEnvelope<byte[]>
        {
            Success = true,
            Response = null
        };

        var exception = Assert.ThrowsExactly<AppException>(
            () => _decoder.Decode<TestPayload>(envelope, "GetProfile"));

        Assert.AreEqual(AppErrorKind.Protocol, exception.Kind);
    }

    [TestMethod]
    public void Decode_InvalidGzip_ThrowsSafeProtocolErrorWithMethodName()
    {
        const string payloadContent = "secret-compressed-payload";
        var envelope = Successful(Encoding.UTF8.GetBytes(payloadContent));

        var exception = Assert.ThrowsExactly<AppException>(
            () => _decoder.Decode<TestPayload>(envelope, "GetProfile"));

        Assert.AreEqual(AppErrorKind.Protocol, exception.Kind);
        StringAssert.Contains(exception.Message, "GetProfile");
        Assert.IsFalse(exception.Message.Contains(payloadContent, StringComparison.Ordinal));
        Assert.IsNotNull(exception.InnerException);
    }

    [TestMethod]
    public void Decode_InvalidUtf8_ThrowsProtocolError()
    {
        var envelope = Successful(GzipJson.Compress([0xC3, 0x28]));

        var exception = Assert.ThrowsExactly<AppException>(
            () => _decoder.Decode<TestPayload>(envelope, "GetProfile"));

        Assert.AreEqual(AppErrorKind.Protocol, exception.Kind);
        Assert.IsNotNull(exception.InnerException);
    }

    [TestMethod]
    public void Decode_MalformedJson_ThrowsSafeProtocolError()
    {
        const string malformedJson = "{\"Id\":42,\"UserName\":\"secret-reader\"";
        var envelope = Successful(GzipJson.Compress(malformedJson));

        var exception = Assert.ThrowsExactly<AppException>(
            () => _decoder.Decode<TestPayload>(envelope, "GetProfile"));

        Assert.AreEqual(AppErrorKind.Protocol, exception.Kind);
        Assert.IsFalse(exception.Message.Contains(malformedJson, StringComparison.Ordinal));
        Assert.IsFalse(exception.Message.Contains("secret-reader", StringComparison.Ordinal));
        Assert.IsNotNull(exception.InnerException);
    }

    [TestMethod]
    public void Decode_MissingJsonRequiredField_ThrowsProtocolError()
    {
        var envelope = Successful(GzipJson.Compress("""{"Id":42}"""));

        var exception = Assert.ThrowsExactly<AppException>(
            () => _decoder.Decode<TestPayload>(envelope, "GetProfile"));

        Assert.AreEqual(AppErrorKind.Protocol, exception.Kind);
        Assert.IsNotNull(exception.InnerException);
    }

    [TestMethod]
    public void Decode_ChapterContentMissingRequiredConstructorParameter_ThrowsProtocolError()
    {
        var envelope = Successful(GzipJson.Compress(
            """{"Id":1,"BookId":2,"SortNum":3,"Title":"Chapter"}"""));

        var exception = Assert.ThrowsExactly<AppException>(
            () => _decoder.Decode<ChapterContent>(envelope, "GetChapter"));

        Assert.AreEqual(AppErrorKind.Protocol, exception.Kind);
        Assert.IsNotNull(exception.InnerException);
    }

    [TestMethod]
    public void Decode_ChapterContentWithNullNonNullableString_ThrowsProtocolError()
    {
        var envelope = Successful(GzipJson.Compress(
            """{"Id":1,"BookId":2,"SortNum":3,"Title":"Chapter","Content":null}"""));

        var exception = Assert.ThrowsExactly<AppException>(
            () => _decoder.Decode<ChapterContent>(envelope, "GetChapter"));

        Assert.AreEqual(AppErrorKind.Protocol, exception.Kind);
        Assert.IsNotNull(exception.InnerException);
    }

    [TestMethod]
    public void Decode_JsonNull_ThrowsProtocolError()
    {
        var envelope = Successful(GzipJson.Compress("null"));

        var exception = Assert.ThrowsExactly<AppException>(
            () => _decoder.Decode<TestPayload>(envelope, "GetProfile"));

        Assert.AreEqual(AppErrorKind.Protocol, exception.Kind);
    }

    private static HubEnvelope<byte[]> Successful(byte[] response)
    {
        return new HubEnvelope<byte[]>
        {
            Success = true,
            Response = response
        };
    }

    private sealed class TestPayload
    {
        [JsonRequired]
        public required long Id { get; init; }

        [JsonRequired]
        public required string UserName { get; init; }
    }
}
