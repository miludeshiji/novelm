using System.IO.Compression;
using System.Text;
using System.Text.Json;
using NovelM_App.Domain.Errors;

namespace NovelM_App.Infrastructure.SignalR;

internal sealed class CompressedResponseDecoder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true
    };

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public T Decode<T>(HubEnvelope<byte[]> envelope, string methodName)
    {
        EnsureSuccessful(envelope, methodName);

        if (envelope.Response is null)
        {
            throw ProtocolError(methodName, "returned no response payload");
        }

        try
        {
            using var compressed = new MemoryStream(envelope.Response, writable: false);
            using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
            using var decompressed = new MemoryStream();
            gzip.CopyTo(decompressed);

            var json = StrictUtf8.GetString(decompressed.ToArray());
            var response = JsonSerializer.Deserialize<T>(json, JsonOptions);

            return response ?? throw ProtocolError(methodName, "decoded to a null value");
        }
        catch (AppException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is InvalidDataException
            or IOException
            or DecoderFallbackException
            or JsonException
            or NotSupportedException)
        {
            throw ProtocolError(methodName, "could not be decoded", exception);
        }
    }

    public void ValidateCommand(HubEnvelope<byte[]> envelope, string methodName)
    {
        EnsureSuccessful(envelope, methodName);
    }

    private static void EnsureSuccessful(
        HubEnvelope<byte[]> envelope,
        string methodName)
    {
        if (envelope.Success)
        {
            return;
        }

        var kind = envelope.Status is -100 or 404
            ? AppErrorKind.Unauthorized
            : AppErrorKind.Server;

        throw new AppException(
            kind,
            ErrorMessage(envelope.Msg, methodName),
            envelope.Status);
    }

    private static string ErrorMessage(string? message, string methodName)
    {
        return string.IsNullOrWhiteSpace(message)
            ? $"Hub method '{methodName}' failed."
            : message;
    }

    private static AppException ProtocolError(
        string methodName,
        string context,
        Exception? innerException = null)
    {
        return new AppException(
            AppErrorKind.Protocol,
            $"Hub method '{methodName}' response {context}.",
            innerException: innerException);
    }
}
