using System.Security.Cryptography;
using System.Text;
using NovelM.Tests.TestSupport;
using NovelM_App.Domain.Errors;
using NovelM_App.Infrastructure.Storage;

namespace NovelM.Tests.Infrastructure;

[TestClass]
public sealed class DpapiTokenStoreTests
{
    [TestMethod]
    public async Task ReadAsync_AbsentFile_ReturnsNull()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var store = new DpapiTokenStore(new AppPaths(temporaryDirectory.Path));

        var result = await store.ReadAsync(CancellationToken.None);

        Assert.IsNull(result);
    }

    [TestMethod]
    public async Task SaveAndReadAsync_UsesDpapiAndRoundTripsForCurrentUser()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var paths = new AppPaths(temporaryDirectory.Path);
        var store = new DpapiTokenStore(paths);
        const string refreshToken = "refresh-secret";

        await store.SaveAsync(refreshToken, CancellationToken.None);

        var protectedBytes = await File.ReadAllBytesAsync(paths.AuthFile);
        Assert.IsFalse(ContainsSequence(protectedBytes, Encoding.UTF8.GetBytes(refreshToken)));
        Assert.AreEqual(refreshToken, await store.ReadAsync(CancellationToken.None));
    }

    [TestMethod]
    public async Task SaveAsync_ReplacementBlocked_PreservesOldTokenWithoutTemporaryFiles()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var paths = new AppPaths(temporaryDirectory.Path);
        var store = new DpapiTokenStore(paths);
        const string oldToken = "old-refresh-secret";
        await store.SaveAsync(oldToken, CancellationToken.None);

        await using (var canonicalHandle = new FileStream(
            paths.AuthFile,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite))
        {
            var exception = await Assert.ThrowsExactlyAsync<AppException>(
                () => store.SaveAsync("new-refresh-secret", CancellationToken.None));
            Assert.AreEqual(AppErrorKind.Storage, exception.Kind);
            Assert.IsTrue(
                exception.InnerException is IOException or UnauthorizedAccessException);
        }

        Assert.AreEqual(oldToken, await store.ReadAsync(CancellationToken.None));
        CollectionAssert.AreEquivalent(
            new[] { paths.AuthFile },
            Directory.GetFiles(paths.DataDirectory));
    }

    [TestMethod]
    public async Task SaveAsync_AlreadyCancelled_PreservesOldTokenWithoutTemporaryFiles()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var paths = new AppPaths(temporaryDirectory.Path);
        var store = new DpapiTokenStore(paths);
        const string oldToken = "old-refresh-secret";
        await store.SaveAsync(oldToken, CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => store.SaveAsync("new-refresh-secret", cancellation.Token));

        Assert.AreEqual(oldToken, await store.ReadAsync(CancellationToken.None));
        CollectionAssert.AreEquivalent(
            new[] { paths.AuthFile },
            Directory.GetFiles(paths.DataDirectory));
    }

    [TestMethod]
    public async Task SaveAsync_InvalidUtf16_ThrowsSafeStorageWithoutFiles()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var paths = new AppPaths(temporaryDirectory.Path);
        var store = new DpapiTokenStore(paths);
        const string secretFragment = "refresh-secret";
        var invalidToken = "\ud800" + secretFragment;

        var exception = await Assert.ThrowsExactlyAsync<AppException>(
            () => store.SaveAsync(invalidToken, CancellationToken.None));

        Assert.AreEqual(AppErrorKind.Storage, exception.Kind);
        Assert.IsFalse(exception.Message.Contains(secretFragment, StringComparison.Ordinal));
        Assert.HasCount(0, Directory.GetFileSystemEntries(paths.DataDirectory));
    }

    [TestMethod]
    public async Task DeleteAsync_RemovesOnlyAuthFileAndSubsequentReadReturnsNull()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var paths = new AppPaths(temporaryDirectory.Path);
        var store = new DpapiTokenStore(paths);
        var neighbor = Path.Combine(paths.DataDirectory, "keep.txt");
        await File.WriteAllTextAsync(neighbor, "keep");
        await store.SaveAsync("refresh-secret", CancellationToken.None);

        await store.DeleteAsync(CancellationToken.None);

        Assert.IsFalse(File.Exists(paths.AuthFile));
        Assert.IsTrue(File.Exists(neighbor));
        Assert.IsNull(await store.ReadAsync(CancellationToken.None));
    }

    [TestMethod]
    public async Task ReadAsync_CorruptFile_ThrowsSafeStorageError()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var paths = new AppPaths(temporaryDirectory.Path);
        const string corruptContent = "corrupt-secret-content";
        await File.WriteAllTextAsync(paths.AuthFile, corruptContent);
        var store = new DpapiTokenStore(paths);

        var exception = await Assert.ThrowsExactlyAsync<AppException>(
            () => store.ReadAsync(CancellationToken.None));

        Assert.AreEqual(AppErrorKind.Storage, exception.Kind);
        Assert.IsFalse(exception.Message.Contains(corruptContent, StringComparison.Ordinal));
        Assert.IsNotNull(exception.InnerException);
    }

    [TestMethod]
    public async Task ReadAsync_AuthPathIsDirectory_ThrowsStorageError()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var paths = new AppPaths(temporaryDirectory.Path);
        Directory.CreateDirectory(paths.AuthFile);
        var store = new DpapiTokenStore(paths);

        var exception = await Assert.ThrowsExactlyAsync<AppException>(
            () => store.ReadAsync(CancellationToken.None));

        Assert.AreEqual(AppErrorKind.Storage, exception.Kind);
        Assert.IsNotNull(exception.InnerException);
    }

    [TestMethod]
    public async Task DeleteAsync_AuthPathIsDirectory_ThrowsStorageError()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var paths = new AppPaths(temporaryDirectory.Path);
        Directory.CreateDirectory(paths.AuthFile);
        var store = new DpapiTokenStore(paths);

        var exception = await Assert.ThrowsExactlyAsync<AppException>(
            () => store.DeleteAsync(CancellationToken.None));

        Assert.AreEqual(AppErrorKind.Storage, exception.Kind);
        Assert.IsNotNull(exception.InnerException);
    }

    [TestMethod]
    public async Task ReadAsync_ProtectedInvalidUtf8_ThrowsStorageError()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var paths = new AppPaths(temporaryDirectory.Path);
        var invalidUtf8 = ProtectedData.Protect(
            [0xC3, 0x28],
            optionalEntropy: null,
            DataProtectionScope.CurrentUser);
        await File.WriteAllBytesAsync(paths.AuthFile, invalidUtf8);
        var store = new DpapiTokenStore(paths);

        var exception = await Assert.ThrowsExactlyAsync<AppException>(
            () => store.ReadAsync(CancellationToken.None));

        Assert.AreEqual(AppErrorKind.Storage, exception.Kind);
        Assert.IsInstanceOfType<DecoderFallbackException>(exception.InnerException);
    }

    private static bool ContainsSequence(byte[] bytes, byte[] sequence)
    {
        return bytes.AsSpan().IndexOf(sequence) >= 0;
    }
}
