using System.Security.Cryptography;
using System.Text;
using NovelM_App.Application.Abstractions;
using NovelM_App.Domain.Errors;

namespace NovelM_App.Infrastructure.Storage;

internal sealed class DpapiTokenStore : ITokenStore
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    private readonly AppPaths _paths;

    public DpapiTokenStore(AppPaths paths)
    {
        _paths = paths;
    }

    public async Task<string?> ReadAsync(CancellationToken cancellationToken)
    {
        byte[] protectedBytes;

        try
        {
            protectedBytes = await File.ReadAllBytesAsync(
                _paths.AuthFile,
                cancellationToken);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (IsStorageFailure(exception))
        {
            throw StorageError("The saved refresh token could not be read", exception);
        }

        byte[]? tokenBytes = null;

        try
        {
            tokenBytes = ProtectedData.Unprotect(
                protectedBytes,
                optionalEntropy: null,
                DataProtectionScope.CurrentUser);
            return StrictUtf8.GetString(tokenBytes);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (IsStorageOrProtectionFailure(exception))
        {
            throw StorageError("The saved refresh token could not be read", exception);
        }
        finally
        {
            if (tokenBytes is not null)
            {
                CryptographicOperations.ZeroMemory(tokenBytes);
            }
        }
    }

    public async Task SaveAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var temporaryFile = Path.Combine(
            _paths.DataDirectory,
            $"{Path.GetFileName(_paths.AuthFile)}.{Guid.NewGuid():N}.tmp");
        byte[]? tokenBytes = null;
        var hasPrimaryFailure = false;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            tokenBytes = StrictUtf8.GetBytes(refreshToken);
            var protectedBytes = ProtectedData.Protect(
                tokenBytes,
                optionalEntropy: null,
                DataProtectionScope.CurrentUser);

            await using (var temporaryStream = new FileStream(
                temporaryFile,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.Asynchronous))
            {
                await temporaryStream.WriteAsync(protectedBytes, cancellationToken);
            }

            File.Move(temporaryFile, _paths.AuthFile, overwrite: true);
        }
        catch (OperationCanceledException)
        {
            hasPrimaryFailure = true;
            throw;
        }
        catch (Exception exception) when (IsStorageOrProtectionFailure(exception))
        {
            hasPrimaryFailure = true;
            throw StorageError("The refresh token could not be saved", exception);
        }
        finally
        {
            if (tokenBytes is not null)
            {
                CryptographicOperations.ZeroMemory(tokenBytes);
            }

            try
            {
                File.Delete(temporaryFile);
            }
            catch (Exception exception) when (IsStorageFailure(exception))
            {
                if (!hasPrimaryFailure)
                {
                    throw StorageError(
                        "The temporary refresh token could not be removed",
                        exception);
                }
            }
        }
    }

    public Task DeleteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            File.Delete(_paths.AuthFile);
            return Task.CompletedTask;
        }
        catch (Exception exception) when (IsStorageFailure(exception))
        {
            throw StorageError("The saved refresh token could not be deleted", exception);
        }
    }

    private AppException StorageError(string context, Exception exception)
    {
        return new AppException(
            AppErrorKind.Storage,
            $"{context}: '{_paths.AuthFile}'.",
            innerException: exception);
    }

    private static bool IsStorageOrProtectionFailure(Exception exception)
    {
        return IsStorageFailure(exception)
            || exception is CryptographicException
            or DecoderFallbackException
            or EncoderFallbackException;
    }

    private static bool IsStorageFailure(Exception exception)
    {
        return exception is IOException
            or UnauthorizedAccessException
            or System.Security.SecurityException
            or NotSupportedException;
    }
}
