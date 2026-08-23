using System.Text.Json;
using NovelM.Tests.TestSupport;
using NovelM_App.Domain.Errors;
using NovelM_App.Infrastructure.Storage;

namespace NovelM.Tests.Infrastructure;

[TestClass]
public sealed class DeviceIdStoreTests
{
    [TestMethod]
    public async Task GetOrCreateAsync_MissingFile_PersistsAndReusesNonEmptyGuid()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var paths = new AppPaths(temporaryDirectory.Path);
        var store = new DeviceIdStore(paths);

        var created = await store.GetOrCreateAsync(CancellationToken.None);
        var reused = await store.GetOrCreateAsync(CancellationToken.None);

        Assert.AreNotEqual(Guid.Empty, created);
        Assert.AreEqual(created, reused);
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(paths.DeviceFile));
        var properties = document.RootElement.EnumerateObject().ToArray();
        Assert.HasCount(1, properties);
        Assert.AreEqual("Id", properties[0].Name);
        Assert.AreEqual(created.ToString("D"), properties[0].Value.GetString());
    }

    [TestMethod]
    public async Task GetOrCreateAsync_ConcurrentCreators_ReturnPersistedWinnerWithoutTemporaryFiles()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var paths = new AppPaths(temporaryDirectory.Path);
        const int creatorCount = 32;
        using var ready = new CountdownEvent(creatorCount);
        var release = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var creators = Enumerable.Range(0, creatorCount)
            .Select(_ => Task.Run(async () =>
            {
                ready.Signal();
                await release.Task;
                return await new DeviceIdStore(paths)
                    .GetOrCreateAsync(CancellationToken.None);
            }))
            .ToArray();

        Assert.IsTrue(ready.Wait(TimeSpan.FromSeconds(10)));
        release.SetResult(true);
        var results = await Task.WhenAll(creators);

        using var document = JsonDocument.Parse(
            await File.ReadAllTextAsync(paths.DeviceFile));
        var persisted = Guid.ParseExact(
            document.RootElement.GetProperty("Id").GetString()!,
            "D");
        Assert.IsTrue(results.All(result => result == persisted));
        CollectionAssert.AreEquivalent(
            new[] { paths.DeviceFile },
            Directory.GetFiles(paths.DataDirectory));
    }

    [TestMethod]
    public async Task GetOrCreateAsync_AlreadyCancelledCreation_LeavesNoFiles()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var paths = new AppPaths(temporaryDirectory.Path);
        var store = new DeviceIdStore(paths);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => store.GetOrCreateAsync(cancellation.Token));

        Assert.HasCount(0, Directory.GetFileSystemEntries(paths.DataDirectory));
    }

    [TestMethod]
    public Task GetOrCreateAsync_InvalidGuid_ThrowsStorageWithoutChangingFile()
    {
        return AssertInvalidFileRemainsUnchangedAsync("""{"Id":"not-a-guid"}""");
    }

    [TestMethod]
    public Task GetOrCreateAsync_EmptyGuid_ThrowsStorageWithoutChangingFile()
    {
        return AssertInvalidFileRemainsUnchangedAsync("""{"Id":"00000000-0000-0000-0000-000000000000"}""");
    }

    [TestMethod]
    public Task GetOrCreateAsync_MissingId_ThrowsStorageWithoutChangingFile()
    {
        return AssertInvalidFileRemainsUnchangedAsync("""{"Other":"value"}""");
    }

    [TestMethod]
    public Task GetOrCreateAsync_CorruptJson_ThrowsStorageWithoutChangingFile()
    {
        return AssertInvalidFileRemainsUnchangedAsync("{\"Id\":\"unterminated");
    }

    private static async Task AssertInvalidFileRemainsUnchangedAsync(string originalContent)
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var paths = new AppPaths(temporaryDirectory.Path);
        await File.WriteAllTextAsync(paths.DeviceFile, originalContent);
        var store = new DeviceIdStore(paths);

        var exception = await Assert.ThrowsExactlyAsync<AppException>(
            () => store.GetOrCreateAsync(CancellationToken.None));

        Assert.AreEqual(AppErrorKind.Storage, exception.Kind);
        Assert.AreEqual(originalContent, await File.ReadAllTextAsync(paths.DeviceFile));
    }
}
