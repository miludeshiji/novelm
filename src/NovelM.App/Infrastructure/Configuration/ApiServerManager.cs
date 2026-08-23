using System.Text.Json;
using System.Text.Json.Serialization;
using NovelM_App.Application.Abstractions;
using NovelM_App.Domain.Configuration;
using NovelM_App.Domain.Errors;
using NovelM_App.Infrastructure.Storage;

namespace NovelM_App.Infrastructure.Configuration;

internal sealed class ApiServerManager : IApiServerManager
{
    private static readonly ApiServerOption HongKong = new(
        "hk",
        "香港",
        new Uri("https://api.lightnovel.life"));

    private static readonly ApiServerOption Cloudflare = new(
        "cf",
        "Cloudflare",
        new Uri("https://cf-api.lightnovel.life"));

    private static readonly ApiServerOption Localhost = new(
        "local",
        "本地调试",
        new Uri("http://localhost:5204"));

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        RespectRequiredConstructorParameters = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true
    };

    private readonly AppPaths _paths;

    public ApiServerManager(AppPaths paths, bool includeLocalhost)
    {
        _paths = paths;
        Options = includeLocalhost
            ? Array.AsReadOnly([HongKong, Cloudflare, Localhost])
            : Array.AsReadOnly([HongKong, Cloudflare]);
        Current = HongKong;
    }

    public ApiServerOption Current { get; private set; }

    public IReadOnlyList<ApiServerOption> Options { get; }

    public event EventHandler<ApiServerOption>? CurrentChanged;

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_paths.SettingsFile))
        {
            await PersistAsync(HongKong.Id, cancellationToken);
            ChangeCurrent(HongKong);
            return;
        }

        SettingsDocument? settings;

        try
        {
            var json = await File.ReadAllBytesAsync(_paths.SettingsFile, cancellationToken);
            settings = JsonSerializer.Deserialize<SettingsDocument>(json, JsonOptions);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (JsonException)
        {
            await ResetToHongKongAsync(cancellationToken);
            return;
        }
        catch (Exception exception) when (IsStorageFailure(exception))
        {
            throw StorageError("The API server settings could not be read", exception);
        }

        var selected = settings is null
            ? null
            : FindOption(settings.ApiServerId);

        if (selected is null)
        {
            await ResetToHongKongAsync(cancellationToken);
            return;
        }

        ChangeCurrent(selected);
    }

    public async Task SelectAsync(string serverId, CancellationToken cancellationToken)
    {
        var selected = string.IsNullOrWhiteSpace(serverId)
            ? null
            : FindOption(serverId);

        if (selected is null)
        {
            throw new AppException(
                AppErrorKind.Validation,
                "Select a known API server.");
        }

        await PersistAsync(selected.Id, cancellationToken);
        ChangeCurrent(selected);
    }

    private ApiServerOption? FindOption(string serverId)
    {
        return Options.FirstOrDefault(
            option => string.Equals(option.Id, serverId, StringComparison.Ordinal));
    }

    private async Task ResetToHongKongAsync(CancellationToken cancellationToken)
    {
        await PersistAsync(HongKong.Id, cancellationToken);
        ChangeCurrent(HongKong);
    }

    private async Task PersistAsync(string serverId, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_paths.SettingsFile)!;
        var temporaryFile = Path.Combine(
            directory,
            $"{Path.GetFileName(_paths.SettingsFile)}.{Guid.NewGuid():N}.tmp");
        var hasPrimaryFailure = false;

        try
        {
            var json = JsonSerializer.Serialize(
                new SettingsDocument(serverId),
                JsonOptions);
            await File.WriteAllTextAsync(temporaryFile, json, cancellationToken);
            File.Move(temporaryFile, _paths.SettingsFile, overwrite: true);
        }
        catch (OperationCanceledException)
        {
            hasPrimaryFailure = true;
            throw;
        }
        catch (Exception exception) when (IsStorageOrJsonFailure(exception))
        {
            hasPrimaryFailure = true;
            throw StorageError("The API server settings could not be saved", exception);
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
                        "The temporary API server settings could not be removed",
                        exception);
                }
            }
        }
    }

    private void ChangeCurrent(ApiServerOption option)
    {
        if (Current.Id == option.Id)
        {
            return;
        }

        Current = option;
        CurrentChanged?.Invoke(this, option);
    }

    private AppException StorageError(string context, Exception exception)
    {
        return new AppException(
            AppErrorKind.Storage,
            $"{context}: '{_paths.SettingsFile}'.",
            innerException: exception);
    }

    private static bool IsStorageOrJsonFailure(Exception exception)
    {
        return exception is JsonException || IsStorageFailure(exception);
    }

    private static bool IsStorageFailure(Exception exception)
    {
        return exception is IOException
            or UnauthorizedAccessException
            or System.Security.SecurityException
            or NotSupportedException;
    }
}
