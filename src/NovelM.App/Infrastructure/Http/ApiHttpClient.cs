using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using NovelM_App.Application.Abstractions;
using NovelM_App.Domain.Errors;
using NovelM_App.Infrastructure.Storage;

namespace NovelM_App.Infrastructure.Http;

internal sealed class ApiHttpClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true
    };

    private readonly HttpClient _httpClient;
    private readonly IApiServerManager _serverManager;
    private readonly DeviceIdStore _deviceIdStore;

    public ApiHttpClient(
        HttpClient httpClient,
        IApiServerManager serverManager,
        DeviceIdStore deviceIdStore)
    {
        _httpClient = httpClient;
        _serverManager = serverManager;
        _deviceIdStore = deviceIdStore;
    }

    public async Task<TResponse> PostAsync<TRequest, TResponse>(
        string relativeEndpoint,
        string operation,
        TRequest payload,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_httpClient.DefaultRequestHeaders.Contains("Authorization"))
        {
            throw new AppException(
                AppErrorKind.Unexpected,
                "The HTTP client is not configured for anonymous authentication requests.");
        }

        var server = _serverManager.Current;
        var requestUri = new Uri(server.BaseUri, relativeEndpoint);

        try
        {
            var deviceId = await _deviceIdStore.GetOrCreateAsync(cancellationToken);
            using var request = CreateRequest(requestUri, payload, deviceId);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var responseBody = await response.Content.ReadAsByteArrayAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                ThrowNonSuccessResponse(
                    responseBody,
                    response.StatusCode,
                    operation,
                    requestUri.Host);
            }

            return DecodeSuccessResponse<TResponse>(
                responseBody,
                operation,
                requestUri.Host);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (AppException)
        {
            throw;
        }
        catch (HttpRequestException exception)
        {
            int? status = exception.StatusCode is null
                ? null
                : (int)exception.StatusCode.Value;
            throw TransportError(operation, requestUri.Host, status, exception);
        }
        catch (IOException exception)
        {
            throw TransportError(operation, requestUri.Host, null, exception);
        }
    }

    private static HttpRequestMessage CreateRequest<TRequest>(
        Uri requestUri,
        TRequest payload,
        Guid deviceId)
    {
        var content = new ByteArrayContent(
            JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json")
        {
            CharSet = "utf-8"
        };

        var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = content
        };
        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("x-id", deviceId.ToString("D"));
        return request;
    }

    private static TResponse DecodeSuccessResponse<TResponse>(
        byte[] responseBody,
        string operation,
        string host)
    {
        ApiEnvelope<JsonElement>? envelope;

        try
        {
            envelope = JsonSerializer.Deserialize<ApiEnvelope<JsonElement>>(
                responseBody,
                JsonOptions);
        }
        catch (Exception exception) when (
            exception is JsonException or NotSupportedException)
        {
            throw ProtocolError(operation, host, exception);
        }

        if (envelope is null)
        {
            throw ProtocolError(operation, host);
        }

        if (!envelope.Success)
        {
            throw ServerError(envelope, operation);
        }

        if (envelope.Response.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            throw ProtocolError(operation, host);
        }

        try
        {
            return envelope.Response.Deserialize<TResponse>(JsonOptions)
                ?? throw ProtocolError(operation, host);
        }
        catch (Exception exception) when (
            exception is JsonException or NotSupportedException)
        {
            throw ProtocolError(operation, host, exception);
        }
    }

    private static void ThrowNonSuccessResponse(
        byte[] responseBody,
        HttpStatusCode httpStatus,
        string operation,
        string host)
    {
        ApiEnvelope<JsonElement>? envelope = null;

        try
        {
            envelope = JsonSerializer.Deserialize<ApiEnvelope<JsonElement>>(
                responseBody,
                JsonOptions);
        }
        catch (Exception exception) when (
            exception is JsonException or NotSupportedException)
        {
        }

        if (envelope is { Success: false, Status: -100 or 404 })
        {
            throw ServerError(envelope, operation);
        }

        throw TransportError(operation, host, (int)httpStatus);
    }

    private static AppException ServerError<TResponse>(
        ApiEnvelope<TResponse> envelope,
        string operation)
    {
        var kind = envelope.Status is -100 or 404
            ? AppErrorKind.Unauthorized
            : AppErrorKind.Server;
        var message = string.IsNullOrWhiteSpace(envelope.Msg)
            ? $"HTTP operation '{operation}' failed."
            : envelope.Msg;

        return new AppException(kind, message, envelope.Status);
    }

    private static AppException ProtocolError(
        string operation,
        string host,
        Exception? innerException = null)
    {
        return new AppException(
            AppErrorKind.Protocol,
            $"HTTP operation '{operation}' received an invalid response from '{host}'.",
            innerException: innerException);
    }

    private static AppException TransportError(
        string operation,
        string host,
        int? status,
        Exception? innerException = null)
    {
        var statusContext = status is null
            ? string.Empty
            : $" (HTTP {status.Value})";
        return new AppException(
            AppErrorKind.Transport,
            $"HTTP operation '{operation}' to '{host}' failed during transport{statusContext}.",
            status,
            innerException);
    }
}
