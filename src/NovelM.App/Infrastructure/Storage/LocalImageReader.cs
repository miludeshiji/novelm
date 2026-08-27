using NovelM_App.Application.Abstractions;

namespace NovelM_App.Infrastructure.Storage;

public sealed class LocalImageReader : ILocalImageReader
{
    public Task<byte[]> ReadAsync(string filePath, CancellationToken cancellationToken) =>
        File.ReadAllBytesAsync(filePath, cancellationToken);
}
