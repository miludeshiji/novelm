using NovelM.Tests.TestSupport;
using NovelM_App.Domain.Errors;
using NovelM_App.Infrastructure.Configuration;
using NovelM_App.Infrastructure.Storage;

namespace NovelM.Tests.Infrastructure;

[TestClass]
public sealed class AppPathsTests
{
    [TestMethod]
    public void Constructor_MapsAllPathsBelowNormalizedDataDirectory()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var input = Path.Combine(temporaryDirectory.Path, ".", "nested", "..");

        var paths = new AppPaths(input);

        Assert.AreEqual(Path.GetFullPath(temporaryDirectory.Path), paths.DataDirectory);
        Assert.AreEqual(Path.Combine(paths.DataDirectory, "device.json"), paths.DeviceFile);
        Assert.AreEqual(Path.Combine(paths.DataDirectory, "settings.json"), paths.SettingsFile);
        Assert.AreEqual(Path.Combine(paths.DataDirectory, "auth.dat"), paths.AuthFile);
        Assert.AreEqual(Path.Combine(paths.DataDirectory, "logs"), paths.LogDirectory);
    }

    [TestMethod]
    public void ForRuntime_UsesDataDirectoryBelowApplicationBaseDirectory()
    {
        var paths = AppPaths.ForRuntime();

        Assert.AreEqual(
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "data")),
            paths.DataDirectory);
    }

    [TestMethod]
    public async Task EnsureWritableAsync_CreatesDataAndLogDirectoriesWithoutLeavingProbe()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var dataDirectory = Path.Combine(temporaryDirectory.Path, "data");
        var paths = new AppPaths(dataDirectory);

        await paths.EnsureWritableAsync(CancellationToken.None);

        Assert.IsTrue(Directory.Exists(paths.DataDirectory));
        Assert.IsTrue(Directory.Exists(paths.LogDirectory));
        Assert.HasCount(0, Directory.GetFiles(paths.DataDirectory));
    }

    [TestMethod]
    public async Task EnsureWritableAsync_AlreadyCancelled_LeavesNoProbe()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var paths = new AppPaths(Path.Combine(temporaryDirectory.Path, "data"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => paths.EnsureWritableAsync(cancellation.Token));

        Assert.IsTrue(Directory.Exists(paths.LogDirectory));
        Assert.HasCount(0, Directory.GetFiles(paths.DataDirectory));
    }

    [TestMethod]
    public void InfrastructureImplementations_AreInternal()
    {
        Assert.IsFalse(typeof(AppPaths).IsPublic);
        Assert.IsFalse(typeof(DeviceIdStore).IsPublic);
        Assert.IsFalse(typeof(DpapiTokenStore).IsPublic);
        Assert.IsFalse(typeof(ApiServerManager).IsPublic);
    }

    [TestMethod]
    public async Task EnsureWritableAsync_FileAtDataDirectory_ThrowsStorageAndPreservesFile()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var dataDirectory = Path.Combine(temporaryDirectory.Path, "data");
        const string originalContent = "keep-this-file";
        await File.WriteAllTextAsync(dataDirectory, originalContent);
        var paths = new AppPaths(dataDirectory);

        var exception = await Assert.ThrowsExactlyAsync<AppException>(
            () => paths.EnsureWritableAsync(CancellationToken.None));

        Assert.AreEqual(AppErrorKind.Storage, exception.Kind);
        StringAssert.Contains(exception.Message, Path.GetFullPath(dataDirectory));
        Assert.AreEqual(originalContent, await File.ReadAllTextAsync(dataDirectory));
    }
}
