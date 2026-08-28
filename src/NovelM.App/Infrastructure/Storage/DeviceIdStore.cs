using System.Text.Json;
using NovelM_App.Application.Abstractions;
using NovelM_App.Domain.Auth;
using NovelM_App.Domain.Errors;

namespace NovelM_App.Infrastructure.Storage;

internal sealed class DeviceIdStore : IDeviceIdStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly AppPaths _paths;

    public DeviceIdStore(AppPaths paths)
    {
        _paths = paths;
    }

    public async Task<string> GetOrCreateAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_paths.DeviceFile))
        {
            return await CreateAsync(cancellationToken);
        }

        return await ReadExistingAsync(cancellationToken);
    }

    public async Task SetAsync(
        string deviceId,
        CancellationToken cancellationToken)
    {
        var normalized = ImportedCredentialValidator.NormalizeDeviceId(deviceId);
        var temporaryFile = Path.Combine(
            _paths.DataDirectory,
            $"{Path.GetFileName(_paths.DeviceFile)}.{Guid.NewGuid():N}.tmp");
        var hasPrimaryFailure = false;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var json = JsonSerializer.SerializeToUtf8Bytes(
                new DeviceDocument(normalized),
                JsonOptions);
            await File.WriteAllBytesAsync(temporaryFile, json, cancellationToken);
            File.Move(temporaryFile, _paths.DeviceFile, overwrite: true);
        }
        catch (OperationCanceledException)
        {
            hasPrimaryFailure = true;
            throw;
        }
        catch (AppException)
        {
            hasPrimaryFailure = true;
            throw;
        }
        catch (Exception exception) when (IsStorageOrJsonFailure(exception))
        {
            hasPrimaryFailure = true;
            throw StorageError("The device identity could not be saved", exception);
        }
        finally
        {
            try
            {
                File.Delete(temporaryFile);
            }
            catch (Exception exception) when (IsStorageFailure(exception))
            {
                if (!hasPrimaryFailure)
                {
                    throw StorageError(
                        "The temporary device identity could not be removed",
                        exception);
                }
            }
        }
    }

    private async Task<string> ReadExistingAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var json = await ReadAllBytesAsync(cancellationToken);
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var properties = root.ValueKind == JsonValueKind.Object
                ? root.EnumerateObject().ToArray()
                : [];

            if (properties.Length != 1
                || properties[0].Name != "Id"
                || properties[0].Value.ValueKind != JsonValueKind.String)
            {
                throw InvalidDeviceFile();
            }

            try
            {
                return ImportedCredentialValidator.NormalizeDeviceId(
                    properties[0].Value.GetString());
            }
            catch (AppException exception)
                when (exception.Kind == AppErrorKind.Validation)
            {
                throw InvalidDeviceFile();
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (AppException)
        {
            throw;
        }
        catch (Exception exception) when (IsStorageOrJsonFailure(exception))
        {
            throw StorageError("The device identity could not be read", exception);
        }
    }

    private async Task<byte[]> ReadAllBytesAsync(CancellationToken cancellationToken)
    {
        const int maximumAttempts = 5;

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await File.ReadAllBytesAsync(
                    _paths.DeviceFile,
                    cancellationToken);
            }
            catch (IOException exception) when (
                attempt < maximumAttempts
                && IsSharingViolation(exception))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken);
            }
        }
    }

    private async Task<string> CreateAsync(CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid().ToString("D");
        var temporaryFile = Path.Combine(
            _paths.DataDirectory,
            $"{Path.GetFileName(_paths.DeviceFile)}.{Guid.NewGuid():N}.tmp");
        var hasPrimaryFailure = false;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var json = JsonSerializer.SerializeToUtf8Bytes(
                new DeviceDocument(id),
                JsonOptions);

            await using (var temporaryStream = new FileStream(
                temporaryFile,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.Asynchronous))
            {
                await temporaryStream.WriteAsync(json, cancellationToken);
            }

            try
            {
                File.Move(temporaryFile, _paths.DeviceFile, overwrite: false);
            }
            catch (IOException) when (File.Exists(_paths.DeviceFile))
            {
                return await ReadExistingAsync(cancellationToken);
            }

            return id;
        }
        catch (OperationCanceledException)
        {
            hasPrimaryFailure = true;
            throw;
        }
        catch (AppException)
        {
            hasPrimaryFailure = true;
            throw;
        }
        catch (Exception exception) when (IsStorageOrJsonFailure(exception))
        {
            hasPrimaryFailure = true;
            throw StorageError("The device identity could not be saved", exception);
        }
        finally
        {
            try
            {
                File.Delete(temporaryFile);
            }
            catch (Exception exception) when (IsStorageFailure(exception))
            {
                if (!hasPrimaryFailure)
                {
                    throw StorageError(
                        "The temporary device identity could not be removed",
                        exception);
                }
            }
        }
    }

    private AppException InvalidDeviceFile()
    {
        return StorageError(
            "The existing device identity is invalid",
            new InvalidDataException(
                "device.json does not contain one safe non-empty Id."));
    }

    private AppException StorageError(string context, Exception exception)
    {
        return new AppException(
            AppErrorKind.Storage,
            $"{context}: '{_paths.DeviceFile}'.",
            innerException: exception);
    }

    private static bool IsStorageOrJsonFailure(Exception exception)
    {
        return IsStorageFailure(exception)
            || exception is JsonException;
    }

    private static bool IsStorageFailure(Exception exception)
    {
        return exception is IOException
            or UnauthorizedAccessException
            or System.Security.SecurityException
            or NotSupportedException;
    }

    private static bool IsSharingViolation(IOException exception)
    {
        var errorCode = exception.HResult & 0xFFFF;
        return errorCode is 32 or 33;
    }

    private sealed record DeviceDocument(string Id);
}
