using NovelM_App.Domain.Errors;

namespace NovelM_App.Infrastructure.Storage;

internal sealed class AppPaths
{
    public AppPaths(string dataDirectory)
    {
        DataDirectory = Path.GetFullPath(dataDirectory);
        DeviceFile = Path.Combine(DataDirectory, "device.json");
        SettingsFile = Path.Combine(DataDirectory, "settings.json");
        AuthFile = Path.Combine(DataDirectory, "auth.dat");
        LogDirectory = Path.Combine(DataDirectory, "logs");
    }

    public string DataDirectory { get; }

    public string DeviceFile { get; }

    public string SettingsFile { get; }

    public string AuthFile { get; }

    public string LogDirectory { get; }

    public static AppPaths ForRuntime()
    {
        return new AppPaths(Path.Combine(AppContext.BaseDirectory, "data"));
    }

    public async Task EnsureWritableAsync(CancellationToken cancellationToken)
    {
        var probeFile = Path.Combine(
            DataDirectory,
            $".write-probe-{Guid.NewGuid():N}.tmp");
        var hasPrimaryFailure = false;

        try
        {
            Directory.CreateDirectory(DataDirectory);
            Directory.CreateDirectory(LogDirectory);

            await using var probe = new FileStream(
                probeFile,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 1,
                FileOptions.Asynchronous);
            await probe.FlushAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            hasPrimaryFailure = true;
            throw;
        }
        catch (Exception exception) when (IsStorageFailure(exception))
        {
            hasPrimaryFailure = true;
            throw StorageError("The application data directory is not writable", exception);
        }
        finally
        {
            try
            {
                File.Delete(probeFile);
            }
            catch (Exception exception) when (IsStorageFailure(exception))
            {
                if (!hasPrimaryFailure)
                {
                    throw StorageError(
                        "The application data directory probe could not be removed",
                        exception);
                }
            }
        }
    }

    private AppException StorageError(string context, Exception exception)
    {
        return new AppException(
            AppErrorKind.Storage,
            $"{context}: '{DataDirectory}'.",
            innerException: exception);
    }

    private static bool IsStorageFailure(Exception exception)
    {
        return exception is IOException
            or UnauthorizedAccessException
            or System.Security.SecurityException
            or NotSupportedException;
    }
}
