using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using NovelM_App.Application.Abstractions;
using NovelM_App.Domain.Errors;
using NovelM_App.Infrastructure.Logging;

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
    private readonly IDeviceIdStore _deviceIdStore;
    private readonly IDiagnosticLog _diagnosticLog;

    public ApiHttpClient(
        HttpClient httpClient,
        IApiServerManager serverManager,
        IDeviceIdStore deviceIdStore,
        IDiagnosticLog diagnosticLog)
    {
        _httpClient = httpClient;
        _serverManager = serverManager;
        _deviceIdStore = deviceIdStore;
        _diagnosticLog = diagnosticLog;
    }

    public async Task<TResponse> PostAsync<TRequest, TResponse>(
        string relativeEndpoint,
        string operation,
        TRequest payload,
        CancellationToken cancellationToken)
    {
        var server = _serverManager.Current;
        var requestUri = new Uri(server.BaseUri, relativeEndpoint);
        var correlationId = Guid.NewGuid().ToString("N");
        var stopwatch = Stopwatch.StartNew();
        int? httpStatus = null;
        int? byteLength = null;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_httpClient.DefaultRequestHeaders.Contains("Authorization"))
            {
                throw new AppException(
                    AppErrorKind.Unexpected,
                    "The HTTP client is not configured for anonymous authentication requests.");
            }

            var deviceId = await _deviceIdStore.GetOrCreateAsync(cancellationToken);
            using var request = CreateRequest(requestUri, payload, deviceId);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            httpStatus = (int)response.StatusCode;
            var responseBody = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            byteLength = responseBody.Length;

            if (!response.IsSuccessStatusCode)
            {
                ThrowNonSuccessResponse(
                    responseBody,
                    response.StatusCode,
                    operation,
                    requestUri.Host);
            }

            var result = DecodeSuccessResponse<TResponse>(
                responseBody,
                operation,
                requestUri.Host);
            await WriteDiagnosticAsync(
                "http.completed",
                operation,
                requestUri.Host,
                typeof(TResponse).Name,
                httpStatus,
                byteLength,
                stopwatch.ElapsedMilliseconds,
                correlationId,
                exception: null);
            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (AppException exception)
        {
            await WriteDiagnosticAsync(
                "http.failed",
                operation,
                requestUri.Host,
                typeof(TResponse).Name,
                httpStatus,
                byteLength,
                stopwatch.ElapsedMilliseconds,
                correlationId,
                exception);
            throw;
        }
        catch (HttpRequestException exception)
        {
            int? status = exception.StatusCode is null
                ? null
                : (int)exception.StatusCode.Value;
            var error = TransportError(operation, requestUri.Host, status, exception);
            await WriteDiagnosticAsync(
                "http.failed",
                operation,
                requestUri.Host,
                typeof(TResponse).Name,
                httpStatus ?? status,
                byteLength,
                stopwatch.ElapsedMilliseconds,
                correlationId,
                error);
            throw error;
        }
        catch (IOException exception)
        {
            var error = TransportError(operation, requestUri.Host, null, exception);
            await WriteDiagnosticAsync(
                "http.failed",
                operation,
                requestUri.Host,
                typeof(TResponse).Name,
                httpStatus,
                byteLength,
                stopwatch.ElapsedMilliseconds,
                correlationId,
                error);
            throw error;
        }
        catch (Exception exception)
        {
            await WriteDiagnosticAsync(
                "http.failed",
                operation,
                requestUri.Host,
                typeof(TResponse).Name,
                httpStatus,
                byteLength,
                stopwatch.ElapsedMilliseconds,
                correlationId,
                exception);
            throw;
        }
    }

    private Task WriteDiagnosticAsync(
        string eventName,
        string operation,
        string host,
        string responseType,
        int? httpStatus,
        int? byteLength,
        long elapsedMilliseconds,
        string correlationId,
        Exception? exception)
    {
        var fields = new Dictionary<string, object?>
        {
            ["operation"] = operation,
            ["host"] = host,
            ["stage"] = eventName == "http.completed" ? "completed" : "failed",
            ["responseType"] = responseType,
            ["elapsedMs"] = elapsedMilliseconds,
            ["correlationId"] = correlationId
        };
        if (httpStatus is not null)
        {
            fields["httpStatus"] = httpStatus.Value;
        }

        if (byteLength is not null)
        {
            fields["byteLength"] = byteLength.Value;
        }

        if (exception is AppException appException)
        {
            fields["errorKind"] = appException.Kind.ToString();
            if (appException.Kind is AppErrorKind.Server or AppErrorKind.Unauthorized
                && appException.Status is not null)
            {
                fields["serverStatus"] = appException.Status.Value;
            }
        }

        return _diagnosticLog.TryWriteAsync(
            eventName,
            fields,
            exception,
            CancellationToken.None);
    }

    private static HttpRequestMessage CreateRequest<TRequest>(
        Uri requestUri,
        TRequest payload,
        string deviceId)
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
        request.Headers.TryAddWithoutValidation("x-id", deviceId);
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
