using System.Text.Json;
using NovelM.Tests.TestSupport;
using NovelM_App.Domain.Errors;
using NovelM_App.Infrastructure.Configuration;
using NovelM_App.Infrastructure.Storage;

namespace NovelM.Tests.Infrastructure;

[TestClass]
public sealed class ApiServerManagerTests
{
    [TestMethod]
    public async Task LoadAsync_MissingSettings_DefaultsToHongKongAndWritesSettings()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var paths = new AppPaths(temporaryDirectory.Path);
        var manager = new ApiServerManager(paths, includeLocalhost: false);

        Assert.AreEqual("hk", manager.Current.Id);
        await manager.LoadAsync(CancellationToken.None);

        Assert.AreEqual("hk", manager.Current.Id);
        AssertSettingsContainsOnly(paths.SettingsFile, "hk");
    }

    [TestMethod]
    public void Options_ContainExactProductionServersInOrder()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var manager = new ApiServerManager(new AppPaths(temporaryDirectory.Path), includeLocalhost: false);

        Assert.HasCount(2, manager.Options);
        AssertOption(manager.Options[0], "hk", "香港", "https://api.lightnovel.life/");
        AssertOption(manager.Options[1], "cf", "Cloudflare", "https://cf-api.lightnovel.life/");
    }

    [TestMethod]
    public async Task SelectAsync_CloudflarePersistsAndNewManagerLoadsIt()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var paths = new AppPaths(temporaryDirectory.Path);
        var manager = new ApiServerManager(paths, includeLocalhost: false);
        await manager.LoadAsync(CancellationToken.None);

        await manager.SelectAsync("cf", CancellationToken.None);

        Assert.AreEqual("cf", manager.Current.Id);
        AssertSettingsContainsOnly(paths.SettingsFile, "cf");
        var reloaded = new ApiServerManager(paths, includeLocalhost: false);
        await reloaded.LoadAsync(CancellationToken.None);
        Assert.AreEqual("cf", reloaded.Current.Id);
    }

    [TestMethod]
    public async Task SelectAsync_LocalhostExcluded_ThrowsValidationAndPreservesCurrentSettings()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var paths = new AppPaths(temporaryDirectory.Path);
        var manager = new ApiServerManager(paths, includeLocalhost: false);
        await manager.LoadAsync(CancellationToken.None);

        var exception = await Assert.ThrowsExactlyAsync<AppException>(
            () => manager.SelectAsync("local", CancellationToken.None));

        Assert.AreEqual(AppErrorKind.Validation, exception.Kind);
        Assert.IsFalse(manager.Options.Any(option => option.Id == "local"));
        Assert.AreEqual("hk", manager.Current.Id);
        AssertSettingsContainsOnly(paths.SettingsFile, "hk");
    }

    [TestMethod]
    public async Task LoadAsync_PersistedLocalhostWhenExcluded_FallsBackAndRewritesHongKong()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var paths = new AppPaths(temporaryDirectory.Path);
        await File.WriteAllTextAsync(paths.SettingsFile, """{"ApiServerId":"local"}""");
        var manager = new ApiServerManager(paths, includeLocalhost: false);

        await manager.LoadAsync(CancellationToken.None);

        Assert.AreEqual("hk", manager.Current.Id);
        AssertSettingsContainsOnly(paths.SettingsFile, "hk");
    }

    [TestMethod]
    public async Task LoadAsync_PersistedLocalhostWhenIncluded_LoadsItAndExposesOption()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var paths = new AppPaths(temporaryDirectory.Path);
        await File.WriteAllTextAsync(paths.SettingsFile, """{"ApiServerId":"local"}""");
        var manager = new ApiServerManager(paths, includeLocalhost: true);

        await manager.LoadAsync(CancellationToken.None);

        Assert.HasCount(3, manager.Options);
        AssertOption(manager.Options[2], "local", "本地调试", "http://localhost:5204/");
        Assert.AreEqual("local", manager.Current.Id);
    }

    [TestMethod]
    [DataRow("not-json")]
    [DataRow("{\"ApiServerId\":\"unknown\"}")]
    public async Task LoadAsync_InvalidSettings_FallsBackAndRewritesHongKong(string content)
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var paths = new AppPaths(temporaryDirectory.Path);
        await File.WriteAllTextAsync(paths.SettingsFile, content);
        var manager = new ApiServerManager(paths, includeLocalhost: false);

        await manager.LoadAsync(CancellationToken.None);

        Assert.AreEqual("hk", manager.Current.Id);
        AssertSettingsContainsOnly(paths.SettingsFile, "hk");
    }

    [TestMethod]
    public async Task CurrentChanged_RaisesOnlyForActualSuccessfulChange()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var manager = new ApiServerManager(
            new AppPaths(temporaryDirectory.Path),
            includeLocalhost: false);
        var changes = new List<string>();
        manager.CurrentChanged += (_, option) => changes.Add(option.Id);

        await manager.LoadAsync(CancellationToken.None);
        await manager.SelectAsync("hk", CancellationToken.None);
        await manager.SelectAsync("cf", CancellationToken.None);
        await manager.SelectAsync("cf", CancellationToken.None);

        CollectionAssert.AreEqual(new[] { "cf" }, changes);
    }

    [TestMethod]
    public async Task SelectAsync_AlreadyCancelled_PreservesCurrentSettingsWithoutTemporaryFiles()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var paths = new AppPaths(temporaryDirectory.Path);
        var manager = new ApiServerManager(paths, includeLocalhost: false);
        await manager.LoadAsync(CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => manager.SelectAsync("cf", cancellation.Token));

        Assert.AreEqual("hk", manager.Current.Id);
        AssertSettingsContainsOnly(paths.SettingsFile, "hk");
        CollectionAssert.AreEquivalent(
            new[] { paths.SettingsFile },
            Directory.GetFiles(paths.DataDirectory));
    }

    [TestMethod]
    public async Task SelectAsync_FailedPersistence_DoesNotChangeCurrentOrLeaveTemporaryFile()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var paths = new AppPaths(temporaryDirectory.Path);
        var manager = new ApiServerManager(paths, includeLocalhost: false);
        await manager.LoadAsync(CancellationToken.None);
        File.Delete(paths.SettingsFile);
        Directory.CreateDirectory(paths.SettingsFile);
        var changeCount = 0;
        manager.CurrentChanged += (_, _) => changeCount++;

        var exception = await Assert.ThrowsExactlyAsync<AppException>(
            () => manager.SelectAsync("cf", CancellationToken.None));

        Assert.AreEqual(AppErrorKind.Storage, exception.Kind);
        Assert.AreEqual("hk", manager.Current.Id);
        Assert.AreEqual(0, changeCount);
        CollectionAssert.AreEquivalent(
            new[] { paths.SettingsFile },
            Directory.GetFileSystemEntries(paths.DataDirectory));
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("  ")]
    [DataRow("unknown")]
    public async Task SelectAsync_InvalidId_ThrowsValidationWithoutChangingCurrent(string? serverId)
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var paths = new AppPaths(temporaryDirectory.Path);
        var manager = new ApiServerManager(paths, includeLocalhost: false);
        await manager.LoadAsync(CancellationToken.None);

        var exception = await Assert.ThrowsExactlyAsync<AppException>(
            () => manager.SelectAsync(serverId!, CancellationToken.None));

        Assert.AreEqual(AppErrorKind.Validation, exception.Kind);
        Assert.AreEqual("hk", manager.Current.Id);
        AssertSettingsContainsOnly(paths.SettingsFile, "hk");
    }

    private static void AssertOption(
        NovelM_App.Domain.Configuration.ApiServerOption option,
        string id,
        string displayName,
        string uri)
    {
        Assert.AreEqual(id, option.Id);
        Assert.AreEqual(displayName, option.DisplayName);
        Assert.AreEqual(new Uri(uri), option.BaseUri);
    }

    private static void AssertSettingsContainsOnly(string settingsFile, string expectedId)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(settingsFile));
        var properties = document.RootElement.EnumerateObject().ToArray();
        Assert.HasCount(1, properties);
        Assert.AreEqual("ApiServerId", properties[0].Name);
        Assert.AreEqual(expectedId, properties[0].Value.GetString());
    }
}
