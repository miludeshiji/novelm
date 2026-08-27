namespace NovelM_App.Application.Abstractions;

public interface ILocalImageReader
{
    Task<byte[]> ReadAsync(string filePath, CancellationToken cancellationToken);
}
