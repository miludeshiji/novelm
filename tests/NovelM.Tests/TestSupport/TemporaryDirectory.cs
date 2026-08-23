namespace NovelM.Tests.TestSupport;

internal sealed class TemporaryDirectory : IDisposable
{
    private readonly string _tempRoot;

    public TemporaryDirectory()
    {
        _tempRoot = System.IO.Path.GetFullPath(System.IO.Path.GetTempPath());
        Path = System.IO.Path.Combine(_tempRoot, $"NovelM.Tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        var fullPath = System.IO.Path.GetFullPath(Path);
        var tempPrefix = _tempRoot.EndsWith(System.IO.Path.DirectorySeparatorChar)
            ? _tempRoot
            : _tempRoot + System.IO.Path.DirectorySeparatorChar;

        if (!fullPath.StartsWith(tempPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Refusing to delete a directory outside the OS temporary directory.");
        }

        if (Directory.Exists(fullPath))
        {
            Directory.Delete(fullPath, recursive: true);
        }
    }
}
