# RefreshToken 登录与侧边栏收窄实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在保留账号密码登录的同时，增加 RefreshToken + `x-id` 登录，并将 WinUI 主导航栏展开宽度调整为 200 像素。

**Architecture:** 新增设备身份存储接口，使认证服务能够先持久化用户导入的 `x-id`；扩展 AuthSession，使其能够覆盖受 DPAPI 保护的 RefreshToken并清除内存 SessionToken；AuthService 再复用现有刷新、SignalR 重连和 `GetMyInfo` 链路。界面继续采用 AccountPage/AccountViewModel MVVM 结构，用 Pivot 提供两种登录方式。

**Tech Stack:** C# 14、.NET 10、WinUI 3、CommunityToolkit.Mvvm、Microsoft SignalR Client、MSTest、DPAPI。

---

## 文件结构

### 新建

- `src/NovelM.App/Application/Abstractions/IDeviceIdStore.cs`：设备 `x-id` 的读取、自动创建和替换契约。
- `src/NovelM.App/Domain/Auth/ImportedCredentialValidator.cs`：无状态的 RefreshToken 与 `x-id` 规范化/安全校验。
- `tests/NovelM.Tests/Presentation/AccountPageXamlTests.cs`：登录标签、遮罩输入和导航宽度的 XAML 合约测试。

### 修改

- `src/NovelM.App/Infrastructure/Storage/DeviceIdStore.cs`：从 GUID 专用存储改为安全字符串存储，并支持替换。
- `src/NovelM.App/Infrastructure/Http/ApiHttpClient.cs`：直接把字符串设备 ID 写入 `x-id` Header。
- `src/NovelM.App/App.xaml.cs`：注册并解析 `IDeviceIdStore`。
- `src/NovelM.App/Application/Abstractions/IAuthSession.cs`：增加 RefreshToken 导入契约。
- `src/NovelM.App/Application/Auth/AuthSession.cs`：实现“先保存、后清空 SessionToken”。
- `src/NovelM.App/Application/Abstractions/IAuthService.cs`：增加 Token 登录用例。
- `src/NovelM.App/Application/Auth/AuthService.cs`：编排设备 ID、Token 刷新、SignalR 和用户资料。
- `src/NovelM.App/Presentation/Account/AccountViewModel.cs`：增加导入字段和命令。
- `src/NovelM.App/Presentation/Account/AccountPage.xaml`：增加两个登录标签及 Token 表单。
- `src/NovelM.App/Presentation/Account/AccountPage.xaml.cs`：同步并清理 RefreshToken PasswordBox。
- `src/NovelM.App/MainWindow.xaml`：设置 `OpenPaneLength="200"`。
- `tests/NovelM.Tests/Infrastructure/DeviceIdStoreTests.cs`：验证字符串设备 ID 与替换行为。
- `tests/NovelM.Tests/Infrastructure/ApiHttpClientTests.cs`：验证导入后的 Header。
- `tests/NovelM.Tests/Application/AuthSessionTests.cs`：验证 Token 导入顺序和失败语义。
- `tests/NovelM.Tests/Application/AuthServiceTests.cs`：验证新用例顺序、失败和取消。
- `tests/NovelM.Tests/Presentation/AccountViewModelTests.cs`：验证输入、命令和敏感字段清理。
- `tests/NovelM.Tests/Infrastructure/SignalRConnectionTests.cs`：给测试替身补齐新接口成员。
- `tests/NovelM.Tests/Presentation/SettingsViewModelTests.cs`：给测试替身补齐新接口成员。
- `tests/NovelM.Tests/Presentation/PublishingViewModelTests.cs`：给测试替身补齐新服务成员。
- `tests/NovelM.Tests/NovelM.Tests.csproj`：把 AccountPage 和 MainWindow XAML 链接到测试输出。

## 实施前置检查

当前工作区存在整仓 CRLF/LF 差异噪声。执行代码修改前必须先确认它们没有实质内容，然后恢复工作区，避免把整文件换行差异提交进去。

- [ ] **Step 1: 验证工作区只有行尾差异**

Run:

```bash
git diff --ignore-space-at-eol --exit-code
```

Expected: exit code `0`，没有实质内容 diff。若非 `0`，停止执行并保留现有修改。

- [ ] **Step 2: 清除已确认的行尾噪声并确认干净基线**

Run:

```bash
git restore --worktree -- .
git status --short
```

Expected: 无输出。

---

### Task 1: 让设备身份支持 Web `x-id`

**Files:**
- Create: `src/NovelM.App/Application/Abstractions/IDeviceIdStore.cs`
- Create: `src/NovelM.App/Domain/Auth/ImportedCredentialValidator.cs`
- Modify: `src/NovelM.App/Infrastructure/Storage/DeviceIdStore.cs`
- Modify: `src/NovelM.App/Infrastructure/Http/ApiHttpClient.cs`
- Modify: `src/NovelM.App/App.xaml.cs`
- Modify: `tests/NovelM.Tests/Infrastructure/DeviceIdStoreTests.cs`
- Modify: `tests/NovelM.Tests/Infrastructure/ApiHttpClientTests.cs`

- [ ] **Step 1: 把现有设备 ID 测试改为字符串契约，并添加 Web ID 替换测试**

在 `DeviceIdStoreTests.cs` 中把首次创建断言改为：

```csharp
var created = await store.GetOrCreateAsync(CancellationToken.None);
var reused = await store.GetOrCreateAsync(CancellationToken.None);

Assert.IsTrue(Guid.TryParseExact(created, "D", out var parsed));
Assert.AreNotEqual(Guid.Empty, parsed);
Assert.AreEqual(created, reused);
using var document = JsonDocument.Parse(await File.ReadAllTextAsync(paths.DeviceFile));
Assert.AreEqual(created, document.RootElement.GetProperty("Id").GetString());
```

把并发创建测试中的 GUID 解析改为字符串比较：

```csharp
var persisted = document.RootElement.GetProperty("Id").GetString();
Assert.IsNotNull(persisted);
Assert.IsTrue(Guid.TryParseExact(persisted, "D", out _));
Assert.IsTrue(results.All(result => result == persisted));
```

删除旧的 `GetOrCreateAsync_InvalidGuid_ThrowsStorageWithoutChangingFile` 和 `GetOrCreateAsync_EmptyGuid_ThrowsStorageWithoutChangingFile`，因为非 GUID 与全零 GUID 现在都是合法普通字符串。用以下持久文件安全测试替代：

```csharp
[TestMethod]
public async Task GetOrCreateAsync_NonGuidIdentity_ReturnsStoredValue()
{
    using var temporaryDirectory = new TemporaryDirectory();
    var paths = new AppPaths(temporaryDirectory.Path);
    await File.WriteAllTextAsync(
        paths.DeviceFile,
        """{"Id":"web-fingerprint-id"}""");

    var result = await new DeviceIdStore(paths)
        .GetOrCreateAsync(CancellationToken.None);

    Assert.AreEqual("web-fingerprint-id", result);
}

[TestMethod]
public Task GetOrCreateAsync_BlankIdentity_ThrowsStorageWithoutChangingFile()
{
    return AssertInvalidFileRemainsUnchangedAsync("""{"Id":"   "}""");
}

[TestMethod]
public Task GetOrCreateAsync_ControlCharacterIdentity_ThrowsStorageWithoutChangingFile()
{
    return AssertInvalidFileRemainsUnchangedAsync("""{"Id":"bad\u0001id"}""");
}

[TestMethod]
public Task GetOrCreateAsync_OversizedIdentity_ThrowsStorageWithoutChangingFile()
{
    return AssertInvalidFileRemainsUnchangedAsync(
        JsonSerializer.Serialize(new { Id = new string('x', 257) }));
}
```

新增：

```csharp
[TestMethod]
public async Task SetAsync_WebVisitorId_ReplacesPersistedIdentityWithoutTemporaryFiles()
{
    using var temporaryDirectory = new TemporaryDirectory();
    var paths = new AppPaths(temporaryDirectory.Path);
    var store = new DeviceIdStore(paths);
    await store.GetOrCreateAsync(CancellationToken.None);

    await store.SetAsync("web-fingerprint-0123456789", CancellationToken.None);

    Assert.AreEqual(
        "web-fingerprint-0123456789",
        await store.GetOrCreateAsync(CancellationToken.None));
    CollectionAssert.AreEquivalent(
        new[] { paths.DeviceFile },
        Directory.GetFiles(paths.DataDirectory));
}

[TestMethod]
[DataRow("")]
[DataRow("   ")]
[DataRow("valid\rmalicious")]
public async Task SetAsync_InvalidIdentity_RejectsBeforeChangingFile(string value)
{
    using var temporaryDirectory = new TemporaryDirectory();
    var paths = new AppPaths(temporaryDirectory.Path);
    var store = new DeviceIdStore(paths);
    await store.SetAsync("original-web-id", CancellationToken.None);

    var exception = await Assert.ThrowsExactlyAsync<AppException>(
        () => store.SetAsync(value, CancellationToken.None));

    Assert.AreEqual(AppErrorKind.Validation, exception.Kind);
    Assert.AreEqual(
        "original-web-id",
        await store.GetOrCreateAsync(CancellationToken.None));
}

[TestMethod]
public async Task SetAsync_IdentityLongerThanLimit_RejectsBeforeChangingFile()
{
    using var temporaryDirectory = new TemporaryDirectory();
    var paths = new AppPaths(temporaryDirectory.Path);
    var store = new DeviceIdStore(paths);
    await store.SetAsync("original-web-id", CancellationToken.None);

    var exception = await Assert.ThrowsExactlyAsync<AppException>(() =>
        store.SetAsync(new string('x', 257), CancellationToken.None));

    Assert.AreEqual(AppErrorKind.Validation, exception.Kind);
    Assert.AreEqual(
        "original-web-id",
        await store.GetOrCreateAsync(CancellationToken.None));
}
```

- [ ] **Step 2: 添加下一次 HTTP 请求使用替换后 `x-id` 的失败测试**

在 `ApiHttpClientTests.cs` 新增：

```csharp
[TestMethod]
public async Task RefreshAsync_AfterDeviceIdentityImportUsesImportedHeader()
{
    using var fixture = await ApiFixture.CreateAsync(_ => JsonResponse(
        """{"success":true,"response":"new-session-token","status":200}"""));
    await fixture.DeviceIdStore.SetAsync(
        "web-fingerprint-0123456789",
        CancellationToken.None);

    await fixture.Api.RefreshAsync("synthetic-refresh-token", CancellationToken.None);

    CollectionAssert.AreEqual(
        new[] { "web-fingerprint-0123456789" },
        fixture.Handler.Requests.Single().Headers["x-id"]);
}
```

同时把该文件中所有 `persistedDeviceId.ToString("D")` 改为 `persistedDeviceId`。

- [ ] **Step 3: 运行测试并确认新契约尚未实现**

Run:

```powershell
dotnet test tests\NovelM.Tests\NovelM.Tests.csproj -p:Platform=x64 --filter "FullyQualifiedName~DeviceIdStoreTests|FullyQualifiedName~ApiHttpClientTests"
```

Expected: FAIL/compile error，指出 `SetAsync` 不存在或返回类型仍为 `Guid`。

- [ ] **Step 4: 增加设备身份接口和统一校验器**

创建 `IDeviceIdStore.cs`：

```csharp
namespace NovelM_App.Application.Abstractions;

public interface IDeviceIdStore
{
    Task<string> GetOrCreateAsync(CancellationToken cancellationToken);

    Task SetAsync(string deviceId, CancellationToken cancellationToken);
}
```

创建 `ImportedCredentialValidator.cs`：

```csharp
using NovelM_App.Domain.Errors;

namespace NovelM_App.Domain.Auth;

public static class ImportedCredentialValidator
{
    public const int MaximumDeviceIdLength = 256;
    public const int MaximumRefreshTokenLength = 16_384;

    public static string NormalizeDeviceId(string? deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            throw new AppException(AppErrorKind.Validation, "请输入有效的 x-id。");
        }

        var normalized = deviceId.Trim();
        if (normalized.Length > MaximumDeviceIdLength
            || normalized.Any(char.IsControl))
        {
            throw new AppException(AppErrorKind.Validation, "x-id 格式无效。");
        }

        return normalized;
    }

    public static string NormalizeRefreshToken(string? refreshToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            throw new AppException(AppErrorKind.Validation, "请输入 RefreshToken。");
        }

        var normalized = refreshToken.Trim();
        if (normalized.Length > MaximumRefreshTokenLength
            || normalized.Any(char.IsControl))
        {
            throw new AppException(AppErrorKind.Validation, "RefreshToken 格式无效。");
        }

        return normalized;
    }
}
```

- [ ] **Step 5: 改造 DeviceIdStore 为字符串实现并增加原子替换**

让类实现接口，并把公开返回类型改成字符串：

```csharp
internal sealed class DeviceIdStore : IDeviceIdStore
{
    public async Task<string> GetOrCreateAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_paths.DeviceFile))
        {
            return await CreateAsync(cancellationToken);
        }

        return await ReadExistingAsync(cancellationToken);
    }

    public async Task SetAsync(
        string deviceId,
        CancellationToken cancellationToken)
    {
        var normalized = ImportedCredentialValidator.NormalizeDeviceId(deviceId);
        var temporaryFile = Path.Combine(
            _paths.DataDirectory,
            $"{Path.GetFileName(_paths.DeviceFile)}.{Guid.NewGuid():N}.tmp");
        var hasPrimaryFailure = false;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var json = JsonSerializer.SerializeToUtf8Bytes(
                new DeviceDocument(normalized),
                JsonOptions);
            await File.WriteAllBytesAsync(temporaryFile, json, cancellationToken);
            File.Move(temporaryFile, _paths.DeviceFile, overwrite: true);
        }
        catch (OperationCanceledException)
        {
            hasPrimaryFailure = true;
            throw;
        }
        catch (AppException)
        {
            hasPrimaryFailure = true;
            throw;
        }
        catch (Exception exception) when (IsStorageOrJsonFailure(exception))
        {
            hasPrimaryFailure = true;
            throw StorageError("The device identity could not be saved", exception);
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
                        "The temporary device identity could not be removed",
                        exception);
                }
            }
        }
    }
}
```

将读取实现替换为字符串校验；持久文件非法时继续映射为 Storage，而不是把 Validation 暴露给启动流程：

```csharp
private async Task<string> ReadExistingAsync(
    CancellationToken cancellationToken)
{
    try
    {
        var json = await ReadAllBytesAsync(cancellationToken);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var properties = root.ValueKind == JsonValueKind.Object
            ? root.EnumerateObject().ToArray()
            : [];

        if (properties.Length != 1
            || properties[0].Name != "Id"
            || properties[0].Value.ValueKind != JsonValueKind.String)
        {
            throw InvalidDeviceFile();
        }

        try
        {
            return ImportedCredentialValidator.NormalizeDeviceId(
                properties[0].Value.GetString());
        }
        catch (AppException exception)
            when (exception.Kind == AppErrorKind.Validation)
        {
            throw InvalidDeviceFile();
        }
    }
    catch (OperationCanceledException)
    {
        throw;
    }
    catch (AppException)
    {
        throw;
    }
    catch (Exception exception) when (IsStorageOrJsonFailure(exception))
    {
        throw StorageError("The device identity could not be read", exception);
    }
}
```

将 `CreateAsync` 替换为字符串版本，同时保留并发创建胜出逻辑：

```csharp
private async Task<string> CreateAsync(CancellationToken cancellationToken)
{
    var id = Guid.NewGuid().ToString("D");
    var temporaryFile = Path.Combine(
        _paths.DataDirectory,
        $"{Path.GetFileName(_paths.DeviceFile)}.{Guid.NewGuid():N}.tmp");
    var hasPrimaryFailure = false;

    try
    {
        cancellationToken.ThrowIfCancellationRequested();
        var json = JsonSerializer.SerializeToUtf8Bytes(
            new DeviceDocument(id),
            JsonOptions);

        await using (var temporaryStream = new FileStream(
            temporaryFile,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            FileOptions.Asynchronous))
        {
            await temporaryStream.WriteAsync(json, cancellationToken);
        }

        try
        {
            File.Move(temporaryFile, _paths.DeviceFile, overwrite: false);
        }
        catch (IOException) when (File.Exists(_paths.DeviceFile))
        {
            return await ReadExistingAsync(cancellationToken);
        }

        return id;
    }
    catch (OperationCanceledException)
    {
        hasPrimaryFailure = true;
        throw;
    }
    catch (AppException)
    {
        hasPrimaryFailure = true;
        throw;
    }
    catch (Exception exception) when (IsStorageOrJsonFailure(exception))
    {
        hasPrimaryFailure = true;
        throw StorageError("The device identity could not be saved", exception);
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
                    "The temporary device identity could not be removed",
                    exception);
            }
        }
    }
}
```

同时把 `InvalidDeviceFile()` 的内部说明改成不泄露旧 GUID 限制：

```csharp
private AppException InvalidDeviceFile()
{
    return StorageError(
        "The existing device identity is invalid",
        new InvalidDataException(
            "device.json does not contain one safe non-empty Id."));
}
```

- [ ] **Step 6: 让 HTTP、启动流程和 DI 使用接口**

在 `ApiHttpClient` 中将字段和构造参数改为 `IDeviceIdStore`，并替换 Header 写入：

```csharp
var deviceId = await _deviceIdStore.GetOrCreateAsync(cancellationToken);
request.Headers.TryAddWithoutValidation("x-id", deviceId);
```

在 `App.xaml.cs` 中替换注册和启动解析：

```csharp
services.AddSingleton<IDeviceIdStore>(provider =>
    new DeviceIdStore(provider.GetRequiredService<AppPaths>()));
```

```csharp
await Services
    .GetRequiredService<IDeviceIdStore>()
    .GetOrCreateAsync(CancellationToken.None);
```

- [ ] **Step 7: 运行目标测试**

Run:

```powershell
dotnet test tests\NovelM.Tests\NovelM.Tests.csproj -p:Platform=x64 --filter "FullyQualifiedName~DeviceIdStoreTests|FullyQualifiedName~ApiHttpClientTests|FullyQualifiedName~AppPathsTests"
```

Expected: PASS。

- [ ] **Step 8: 提交设备身份改造**

```bash
git add src/NovelM.App/Application/Abstractions/IDeviceIdStore.cs src/NovelM.App/Domain/Auth/ImportedCredentialValidator.cs src/NovelM.App/Infrastructure/Storage/DeviceIdStore.cs src/NovelM.App/Infrastructure/Http/ApiHttpClient.cs src/NovelM.App/App.xaml.cs tests/NovelM.Tests/Infrastructure/DeviceIdStoreTests.cs tests/NovelM.Tests/Infrastructure/ApiHttpClientTests.cs
git commit -m "feat: 支持导入 Web 设备身份"
```

---

### Task 2: 支持导入 RefreshToken 到 AuthSession

**Files:**
- Modify: `src/NovelM.App/Application/Abstractions/IAuthSession.cs`
- Modify: `src/NovelM.App/Application/Auth/AuthSession.cs`
- Modify: `tests/NovelM.Tests/Application/AuthSessionTests.cs`
- Modify: `tests/NovelM.Tests/Application/AuthServiceTests.cs`
- Modify: `tests/NovelM.Tests/Infrastructure/SignalRConnectionTests.cs`
- Modify: `tests/NovelM.Tests/Presentation/SettingsViewModelTests.cs`

- [ ] **Step 1: 写入导入顺序和保存失败测试**

在 `AuthSessionTests.cs` 新增：

```csharp
[TestMethod]
public async Task ImportRefreshTokenAsync_SavesBeforeClearingSessionToken()
{
    var store = new FakeTokenStore();
    var session = new AuthSession(new FakeAuthApi(), store);
    await session.SetTokensAsync(
        new LoginTokens("existing-session", "existing-refresh"),
        CancellationToken.None);
    store.OnSaveAsync = (value, _) =>
    {
        Assert.AreEqual("imported-refresh", value);
        Assert.AreEqual("existing-session", session.SessionToken);
        return Task.CompletedTask;
    };

    await session.ImportRefreshTokenAsync(
        "imported-refresh",
        CancellationToken.None);

    Assert.AreEqual("imported-refresh", store.StoredToken);
    Assert.IsNull(session.SessionToken);
}

[TestMethod]
public async Task ImportRefreshTokenAsync_SaveFailurePreservesExistingSession()
{
    var store = new FakeTokenStore();
    var session = new AuthSession(new FakeAuthApi(), store);
    await session.SetTokensAsync(
        new LoginTokens("existing-session", "existing-refresh"),
        CancellationToken.None);
    var failure = Error(AppErrorKind.Storage);
    store.SaveException = failure;

    var actual = await Assert.ThrowsExactlyAsync<AppException>(() =>
        session.ImportRefreshTokenAsync("imported-refresh", CancellationToken.None));

    Assert.AreSame(failure, actual);
    Assert.AreEqual("existing-session", session.SessionToken);
    Assert.AreEqual("existing-refresh", store.StoredToken);
}
```

- [ ] **Step 2: 运行测试并确认接口缺失**

Run:

```powershell
dotnet test tests\NovelM.Tests\NovelM.Tests.csproj -p:Platform=x64 --filter "FullyQualifiedName~AuthSessionTests"
```

Expected: FAIL/compile error，`ImportRefreshTokenAsync` 不存在。

- [ ] **Step 3: 扩展接口并实现串行导入**

在 `IAuthSession` 增加：

```csharp
Task ImportRefreshTokenAsync(
    string refreshToken,
    CancellationToken cancellationToken);
```

在 `AuthSession` 增加：

```csharp
public async Task ImportRefreshTokenAsync(
    string refreshToken,
    CancellationToken cancellationToken)
{
    await _gate.WaitAsync(cancellationToken);
    try
    {
        await _tokenStore.SaveAsync(refreshToken, cancellationToken);
        Volatile.Write(ref _sessionToken, null);
    }
    finally
    {
        _gate.Release();
    }
}
```

该顺序必须保持不变：只有 DPAPI 持久化成功后才能清除旧 SessionToken。

- [ ] **Step 4: 给所有 IAuthSession 测试替身补齐接口**

在 `AuthServiceTests.cs` 的 `FakeAuthSession` 中记录操作：

```csharp
public Task ImportRefreshTokenAsync(
    string refreshToken,
    CancellationToken cancellationToken)
{
    _operations.Add(new Operation(
        "import-refresh-token",
        refreshToken,
        null,
        cancellationToken));
    return Task.CompletedTask;
}
```

在 `SignalRConnectionTests.cs` 和 `SettingsViewModelTests.cs` 的替身中增加不会被调用的实现：

```csharp
public Task ImportRefreshTokenAsync(
    string refreshToken,
    CancellationToken cancellationToken)
{
    throw new AssertFailedException(
        "ImportRefreshTokenAsync was not expected.");
}
```

- [ ] **Step 5: 运行认证和连接测试**

Run:

```powershell
dotnet test tests\NovelM.Tests\NovelM.Tests.csproj -p:Platform=x64 --filter "FullyQualifiedName~AuthSessionTests|FullyQualifiedName~AuthServiceTests|FullyQualifiedName~SignalRConnectionTests|FullyQualifiedName~SettingsViewModelTests"
```

Expected: PASS。

- [ ] **Step 6: 提交 AuthSession 导入能力**

```bash
git add src/NovelM.App/Application/Abstractions/IAuthSession.cs src/NovelM.App/Application/Auth/AuthSession.cs tests/NovelM.Tests/Application/AuthSessionTests.cs tests/NovelM.Tests/Application/AuthServiceTests.cs tests/NovelM.Tests/Infrastructure/SignalRConnectionTests.cs tests/NovelM.Tests/Presentation/SettingsViewModelTests.cs
git commit -m "feat: 支持导入 RefreshToken 会话"
```

---

### Task 3: 在 AuthService 中编排 Token 登录

**Files:**
- Modify: `src/NovelM.App/Application/Abstractions/IAuthService.cs`
- Modify: `src/NovelM.App/Application/Auth/AuthService.cs`
- Modify: `tests/NovelM.Tests/Application/AuthServiceTests.cs`
- Modify: `tests/NovelM.Tests/Presentation/AccountViewModelTests.cs`
- Modify: `tests/NovelM.Tests/Presentation/PublishingViewModelTests.cs`

- [ ] **Step 1: 添加成功顺序和非回滚失败测试**

在 `AuthServiceTests.cs` 增加一个 `FakeDeviceIdStore`，让 `CreateFixture` 把它传给 `AuthService` 并暴露在 `Fixture` 中：

```csharp
private sealed class FakeDeviceIdStore : IDeviceIdStore
{
    private readonly List<Operation> _operations;

    public FakeDeviceIdStore(List<Operation> operations)
    {
        _operations = operations;
    }

    public string? Current { get; private set; }

    public Exception? SetException { get; set; }

    public Task<string> GetOrCreateAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Current ?? "generated-device-id");

    public Task SetAsync(string deviceId, CancellationToken cancellationToken)
    {
        _operations.Add(new Operation(
            "set-device-id",
            deviceId,
            null,
            cancellationToken));
        if (SetException is not null)
        {
            return Task.FromException(SetException);
        }

        Current = deviceId;
        return Task.CompletedTask;
    }
}
```

把 `FakeAuthSession` 的导入与读取实现扩展为可观察、可注入失败的替身：

```csharp
public string? ImportedRefreshToken { get; private set; }
public Exception? ImportException { get; set; }
public Exception? GetAccessTokenException { get; set; }

public Task ImportRefreshTokenAsync(
    string refreshToken,
    CancellationToken cancellationToken)
{
    _operations.Add(new Operation(
        "import-refresh-token",
        refreshToken,
        null,
        cancellationToken));
    if (ImportException is not null)
    {
        return Task.FromException(ImportException);
    }

    ImportedRefreshToken = refreshToken;
    LastTokens = null;
    return Task.CompletedTask;
}

public Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken)
{
    _operations.Add(new Operation(
        "get-access-token",
        null,
        null,
        cancellationToken));
    return GetAccessTokenException is null
        ? Task.FromResult(AccessToken)
        : Task.FromException<string?>(GetAccessTokenException);
}
```

把 fixture 构造固定为第五个依赖传入设备存储：

```csharp
private static Fixture CreateFixture()
{
    var operations = new List<Operation>();
    var authApi = new FakeAuthApi(operations);
    var session = new FakeAuthSession(operations);
    var connection = new FakeSignalRConnection(operations);
    var userApi = new FakeUserApi(operations);
    var deviceIdStore = new FakeDeviceIdStore(operations);
    return new Fixture(
        new AuthService(
            authApi,
            session,
            connection,
            userApi,
            deviceIdStore),
        authApi,
        session,
        connection,
        userApi,
        deviceIdStore,
        operations);
}

private sealed record Fixture(
    AuthService Service,
    FakeAuthApi AuthApi,
    FakeAuthSession Session,
    FakeSignalRConnection Connection,
    FakeUserApi UserApi,
    FakeDeviceIdStore DeviceIdStore,
    List<Operation> Operations);
```

新增成功测试：

```csharp
[TestMethod]
public async Task LoginWithRefreshTokenAsync_ValidInput_ReplacesCredentialsInOrder()
{
    var fixture = CreateFixture();
    fixture.Session.AccessToken = "imported-session-token";

    var result = await fixture.Service.LoginWithRefreshTokenAsync(
        "  imported-refresh-token  ",
        "  web-fingerprint-id  ",
        CancellationToken.None);

    Assert.AreSame(fixture.UserApi.Profile, result);
    Assert.AreSame(result, fixture.Service.CurrentUser);
    CollectionAssert.AreEqual(
        new[]
        {
            "set-device-id",
            "import-refresh-token",
            "get-access-token",
            "restart",
            "get-my-info"
        },
        fixture.Operations.Select(operation => operation.Name).ToArray());
    Assert.AreEqual("web-fingerprint-id", fixture.DeviceIdStore.Current);
    Assert.AreEqual(
        "imported-refresh-token",
        fixture.Session.ImportedRefreshToken);
}
```

新增无 SessionToken 测试：

```csharp
[TestMethod]
public async Task LoginWithRefreshTokenAsync_NoSessionToken_ThrowsUnauthorizedAfterImport()
{
    var fixture = CreateFixture();
    const string refreshToken = "imported-refresh-secret";

    var exception = await Assert.ThrowsExactlyAsync<AppException>(() =>
        fixture.Service.LoginWithRefreshTokenAsync(
            refreshToken,
            "web-fingerprint-id",
            CancellationToken.None));

    Assert.AreEqual(AppErrorKind.Unauthorized, exception.Kind);
    Assert.IsFalse(exception.Message.Contains(refreshToken, StringComparison.Ordinal));
    CollectionAssert.AreEqual(
        new[] { "set-device-id", "import-refresh-token", "get-access-token" },
        fixture.Operations.Select(operation => operation.Name).ToArray());
    Assert.AreEqual("web-fingerprint-id", fixture.DeviceIdStore.Current);
    Assert.AreEqual(refreshToken, fixture.Session.ImportedRefreshToken);
    Assert.IsNull(fixture.Service.CurrentUser);
}
```

新增每个提交阶段的非回滚测试：

```csharp
[TestMethod]
public async Task LoginWithRefreshTokenAsync_DeviceSaveFails_DoesNotImportToken()
{
    var fixture = CreateFixture();
    var failure = Error(AppErrorKind.Storage);
    fixture.DeviceIdStore.SetException = failure;

    var actual = await Assert.ThrowsExactlyAsync<AppException>(() =>
        fixture.Service.LoginWithRefreshTokenAsync(
            "imported-refresh",
            "web-id",
            CancellationToken.None));

    Assert.AreSame(failure, actual);
    CollectionAssert.AreEqual(
        new[] { "set-device-id" },
        fixture.Operations.Select(operation => operation.Name).ToArray());
    Assert.IsNull(fixture.Session.ImportedRefreshToken);
}

[TestMethod]
public async Task LoginWithRefreshTokenAsync_TokenImportFails_KeepsNewDeviceId()
{
    var fixture = CreateFixture();
    var failure = Error(AppErrorKind.Storage);
    fixture.Session.ImportException = failure;

    var actual = await Assert.ThrowsExactlyAsync<AppException>(() =>
        fixture.Service.LoginWithRefreshTokenAsync(
            "imported-refresh",
            "web-id",
            CancellationToken.None));

    Assert.AreSame(failure, actual);
    CollectionAssert.AreEqual(
        new[] { "set-device-id", "import-refresh-token" },
        fixture.Operations.Select(operation => operation.Name).ToArray());
    Assert.AreEqual("web-id", fixture.DeviceIdStore.Current);
    Assert.IsNull(fixture.Session.ImportedRefreshToken);
}

[TestMethod]
public async Task LoginWithRefreshTokenAsync_RefreshFails_KeepsImportedCredentials()
{
    var fixture = CreateFixture();
    var failure = Error(AppErrorKind.Transport);
    fixture.Session.GetAccessTokenException = failure;

    var actual = await Assert.ThrowsExactlyAsync<AppException>(() =>
        fixture.Service.LoginWithRefreshTokenAsync(
            "imported-refresh",
            "web-id",
            CancellationToken.None));

    Assert.AreSame(failure, actual);
    CollectionAssert.AreEqual(
        new[] { "set-device-id", "import-refresh-token", "get-access-token" },
        fixture.Operations.Select(operation => operation.Name).ToArray());
    Assert.AreEqual("web-id", fixture.DeviceIdStore.Current);
    Assert.AreEqual("imported-refresh", fixture.Session.ImportedRefreshToken);
    Assert.IsNull(fixture.Service.CurrentUser);
}

[TestMethod]
public async Task LoginWithRefreshTokenAsync_RestartFails_KeepsImportedCredentials()
{
    var fixture = CreateFixture();
    fixture.Session.AccessToken = "imported-session";
    var failure = Error(AppErrorKind.Transport);
    fixture.Connection.RestartException = failure;

    var actual = await Assert.ThrowsExactlyAsync<AppException>(() =>
        fixture.Service.LoginWithRefreshTokenAsync(
            "imported-refresh",
            "web-id",
            CancellationToken.None));

    Assert.AreSame(failure, actual);
    CollectionAssert.AreEqual(
        new[]
        {
            "set-device-id",
            "import-refresh-token",
            "get-access-token",
            "restart"
        },
        fixture.Operations.Select(operation => operation.Name).ToArray());
    Assert.AreEqual("web-id", fixture.DeviceIdStore.Current);
    Assert.AreEqual("imported-refresh", fixture.Session.ImportedRefreshToken);
    Assert.IsNull(fixture.Service.CurrentUser);
}

[TestMethod]
public async Task LoginWithRefreshTokenAsync_GetMyInfoFails_KeepsImportedCredentials()
{
    var fixture = CreateFixture();
    fixture.Session.AccessToken = "imported-session";
    var failure = Error(AppErrorKind.Protocol);
    fixture.UserApi.Handler = _ => Task.FromException<UserProfile>(failure);

    var actual = await Assert.ThrowsExactlyAsync<AppException>(() =>
        fixture.Service.LoginWithRefreshTokenAsync(
            "imported-refresh",
            "web-id",
            CancellationToken.None));

    Assert.AreSame(failure, actual);
    CollectionAssert.AreEqual(
        new[]
        {
            "set-device-id",
            "import-refresh-token",
            "get-access-token",
            "restart",
            "get-my-info"
        },
        fixture.Operations.Select(operation => operation.Name).ToArray());
    Assert.AreEqual("web-id", fixture.DeviceIdStore.Current);
    Assert.AreEqual("imported-refresh", fixture.Session.ImportedRefreshToken);
    Assert.IsNull(fixture.Service.CurrentUser);
}
```

- [ ] **Step 2: 添加输入校验和退出登录竞态测试**

增加短输入校验数据测试：

```csharp
[TestMethod]
[DataRow("", "web-id")]
[DataRow("token", "")]
[DataRow("valid\rmalicious", "web-id")]
[DataRow("token", "valid\rmalicious")]
public async Task LoginWithRefreshTokenAsync_InvalidInputRejectsBeforeDependencies(
    string refreshToken,
    string deviceId)
{
    var fixture = CreateFixture();

    var exception = await Assert.ThrowsExactlyAsync<AppException>(() =>
        fixture.Service.LoginWithRefreshTokenAsync(
            refreshToken,
            deviceId,
            CancellationToken.None));

    Assert.AreEqual(AppErrorKind.Validation, exception.Kind);
    if (refreshToken.Length > 0)
    {
        Assert.IsFalse(exception.Message.Contains(refreshToken, StringComparison.Ordinal));
    }
    if (deviceId.Length > 0)
    {
        Assert.IsFalse(exception.Message.Contains(deviceId, StringComparison.Ordinal));
    }
    Assert.HasCount(0, fixture.Operations);
}

[TestMethod]
public async Task LoginWithRefreshTokenAsync_OversizedInputRejectsBeforeDependencies()
{
    foreach (var (refreshToken, deviceId) in new[]
             {
                 (new string('r', 16_385), "web-id"),
                 ("token", new string('x', 257))
             })
    {
        var fixture = CreateFixture();

        var exception = await Assert.ThrowsExactlyAsync<AppException>(() =>
            fixture.Service.LoginWithRefreshTokenAsync(
                refreshToken,
                deviceId,
                CancellationToken.None));

        Assert.AreEqual(AppErrorKind.Validation, exception.Kind);
        Assert.HasCount(0, fixture.Operations);
    }
}
```

增加退出登录竞态测试：

```csharp
[TestMethod]
public async Task LoginWithRefreshTokenAsync_LogoutRejectsStaleProfileResult()
{
    var fixture = CreateFixture();
    fixture.Session.AccessToken = "imported-session";
    var profileCompletion = new TaskCompletionSource<UserProfile>(
        TaskCreationOptions.RunContinuationsAsynchronously);
    fixture.UserApi.Handler = _ => profileCompletion.Task;

    var login = fixture.Service.LoginWithRefreshTokenAsync(
        "imported-refresh",
        "web-id",
        CancellationToken.None);
    Assert.AreEqual("get-my-info", fixture.Operations[^1].Name);

    await fixture.Service.LogoutAsync(CancellationToken.None);
    profileCompletion.SetResult(fixture.UserApi.Profile);

    await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => login);
    Assert.IsNull(fixture.Service.CurrentUser);
}
```

- [ ] **Step 3: 运行测试并确认服务方法与依赖尚未实现**

Run:

```powershell
dotnet test tests\NovelM.Tests\NovelM.Tests.csproj -p:Platform=x64 --filter "FullyQualifiedName~AuthServiceTests"
```

Expected: FAIL/compile error，缺少 `LoginWithRefreshTokenAsync` 或新的构造依赖。

- [ ] **Step 4: 扩展 IAuthService 并实现 Token 登录用例**

在 `IAuthService` 增加：

```csharp
Task<UserProfile> LoginWithRefreshTokenAsync(
    string refreshToken,
    string deviceId,
    CancellationToken cancellationToken);
```

在 `AuthService` 中追加第五个构造依赖并保存字段，参数顺序固定如下：

```csharp
private readonly IDeviceIdStore _deviceIdStore;

public AuthService(
    IAuthApi authApi,
    IAuthSession authSession,
    ISignalRConnection signalRConnection,
    IUserApi userApi,
    IDeviceIdStore deviceIdStore)
{
    _authApi = authApi;
    _authSession = authSession;
    _signalRConnection = signalRConnection;
    _userApi = userApi;
    _deviceIdStore = deviceIdStore;
}
```

增加同步入口和异步完成方法：

```csharp
public Task<UserProfile> LoginWithRefreshTokenAsync(
    string refreshToken,
    string deviceId,
    CancellationToken cancellationToken)
{
    var normalizedRefreshToken =
        ImportedCredentialValidator.NormalizeRefreshToken(refreshToken);
    var normalizedDeviceId =
        ImportedCredentialValidator.NormalizeDeviceId(deviceId);
    var operation = BeginUserOperation(cancellationToken);
    return CompleteRefreshTokenLoginAsync(
        normalizedRefreshToken,
        normalizedDeviceId,
        operation);
}

private async Task<UserProfile> CompleteRefreshTokenLoginAsync(
    string refreshToken,
    string deviceId,
    UserOperation operation)
{
    using (operation)
    {
        await _authLifecycleGate.WaitAsync(operation.CancellationToken);
        try
        {
            EnsureUserOperationIsActive(operation.Generation);
            await _deviceIdStore.SetAsync(
                deviceId,
                operation.CancellationToken);
            await _authSession.ImportRefreshTokenAsync(
                refreshToken,
                operation.CancellationToken);
            var accessToken = await _authSession.GetAccessTokenAsync(
                operation.CancellationToken);
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                throw new AppException(
                    AppErrorKind.Unauthorized,
                    "The imported refresh token could not establish a session.");
            }

            EnsureUserOperationIsActive(operation.Generation);
            await _signalRConnection.RestartAsync(operation.CancellationToken);
        }
        finally
        {
            _authLifecycleGate.Release();
        }

        var user = await _userApi.GetMyInfoAsync(operation.CancellationToken);
        CompleteUserOperation(operation.Generation, user);
        return user;
    }
}
```

- [ ] **Step 5: 更新 DI 和 IAuthService 测试替身**

在 `App.xaml.cs` 构造 `AuthService` 时 DI 会自动解析新增的 `IDeviceIdStore`，无需工厂代码；确认接口已经注册在 Task 1。

在 `AccountViewModelTests.cs` 和 `PublishingViewModelTests.cs` 的 `FakeAuthService` 暂时补齐方法；Account 替身在 Task 4 中扩展记录能力，Publishing 替身直接抛未预期调用：

```csharp
public Task<UserProfile> LoginWithRefreshTokenAsync(
    string refreshToken,
    string deviceId,
    CancellationToken cancellationToken) =>
    throw new AssertFailedException(
        "LoginWithRefreshTokenAsync was not expected.");
```

更新 `AuthServiceTests.Constructor_HasOnlyRequiredDependencies` 的精确期望顺序：

```csharp
CollectionAssert.AreEqual(
    new[]
    {
        typeof(IAuthApi),
        typeof(IAuthSession),
        typeof(ISignalRConnection),
        typeof(IUserApi),
        typeof(IDeviceIdStore)
    },
    constructor.GetParameters()
        .Select(parameter => parameter.ParameterType)
        .ToArray());
```

- [ ] **Step 6: 运行应用层测试**

Run:

```powershell
dotnet test tests\NovelM.Tests\NovelM.Tests.csproj -p:Platform=x64 --filter "FullyQualifiedName~AuthServiceTests|FullyQualifiedName~PublishingViewModelTests|FullyQualifiedName~AccountViewModelTests"
```

Expected: PASS。

- [ ] **Step 7: 提交 Token 登录业务流**

```bash
git add src/NovelM.App/Application/Abstractions/IAuthService.cs src/NovelM.App/Application/Auth/AuthService.cs tests/NovelM.Tests/Application/AuthServiceTests.cs tests/NovelM.Tests/Presentation/AccountViewModelTests.cs tests/NovelM.Tests/Presentation/PublishingViewModelTests.cs
git commit -m "feat: 增加 RefreshToken 登录业务流"
```

---

### Task 4: 扩展 AccountViewModel

**Files:**
- Modify: `src/NovelM.App/Presentation/Account/AccountViewModel.cs`
- Modify: `tests/NovelM.Tests/Presentation/AccountViewModelTests.cs`

- [ ] **Step 1: 添加输入校验、成功和失败测试**

在 `AccountViewModelTests.cs` 中把 Task 3 的占位实现替换为可观察替身，并增加：

```csharp
public Func<string, string, CancellationToken, Task<UserProfile>>?
    RefreshTokenLoginHandler { get; init; }
public int RefreshTokenLoginCount { get; private set; }
public string? LoginRefreshToken { get; private set; }
public string? LoginDeviceId { get; private set; }

public async Task<UserProfile> LoginWithRefreshTokenAsync(
    string refreshToken,
    string deviceId,
    CancellationToken cancellationToken)
{
    RefreshTokenLoginCount++;
    LoginRefreshToken = refreshToken;
    LoginDeviceId = deviceId;
    var profile = await (RefreshTokenLoginHandler?.Invoke(
        refreshToken,
        deviceId,
        cancellationToken)
        ?? throw new AssertFailedException(
            "LoginWithRefreshTokenAsync was not expected."));
    CurrentUser = profile;
    return profile;
}
```

新增成功测试：

```csharp
[TestMethod]
public async Task LoginWithRefreshTokenCommand_SuccessNormalizesClearsSecretAndPublishesProfile()
{
    var profile = Profile();
    var service = new FakeAuthService
    {
        RefreshTokenLoginHandler = (_, _, _) => Task.FromResult(profile)
    };
    var viewModel = CreateViewModel(service)
    {
        RefreshToken = "  imported-refresh  ",
        DeviceId = "  web-fingerprint-id  "
    };

    await viewModel.LoginWithRefreshTokenCommand.ExecuteAsync(null);

    Assert.AreEqual(1, service.RefreshTokenLoginCount);
    Assert.AreEqual("imported-refresh", service.LoginRefreshToken);
    Assert.AreEqual("web-fingerprint-id", service.LoginDeviceId);
    Assert.AreSame(profile, viewModel.CurrentUser);
    Assert.AreEqual(string.Empty, viewModel.RefreshToken);
    Assert.AreEqual("  web-fingerprint-id  ", viewModel.DeviceId);
    Assert.IsNull(viewModel.ErrorMessage);
    Assert.IsFalse(viewModel.IsBusy);
}
```

新增失败测试：

```csharp
[TestMethod]
public async Task LoginWithRefreshTokenCommand_FailureClearsSecretAndRetainsDeviceId()
{
    var service = new FakeAuthService
    {
        RefreshTokenLoginHandler = (_, _, _) =>
            Task.FromException<UserProfile>(
                Error(AppErrorKind.Transport, "Synthetic transport detail"))
    };
    var viewModel = CreateViewModel(service)
    {
        RefreshToken = "imported-refresh",
        DeviceId = "web-fingerprint-id"
    };

    await viewModel.LoginWithRefreshTokenCommand.ExecuteAsync(null);

    Assert.AreEqual(1, service.RefreshTokenLoginCount);
    Assert.AreEqual(string.Empty, viewModel.RefreshToken);
    Assert.AreEqual("web-fingerprint-id", viewModel.DeviceId);
    Assert.IsNull(viewModel.CurrentUser);
    Assert.AreEqual(
        "网络连接失败，请检查网络后重试。",
        viewModel.ErrorMessage);
    Assert.IsFalse(viewModel.IsBusy);
}
```

新增校验测试：

```csharp
[TestMethod]
[DataRow("", "web-id", "请输入 RefreshToken。")]
[DataRow("token", "", "请输入有效的 x-id。")]
[DataRow("valid\rmalicious", "web-id", "RefreshToken 格式无效。")]
[DataRow("token", "valid\rmalicious", "x-id 格式无效。")]
public async Task LoginWithRefreshTokenCommand_InvalidInputDoesNotCallService(
    string refreshToken,
    string deviceId,
    string expectedMessage)
{
    var service = new FakeAuthService();
    var viewModel = CreateViewModel(service);
    viewModel.RefreshToken = refreshToken;
    viewModel.DeviceId = deviceId;

    await viewModel.LoginWithRefreshTokenCommand.ExecuteAsync(null);

    Assert.AreEqual(0, service.RefreshTokenLoginCount);
    Assert.AreEqual(expectedMessage, viewModel.ErrorMessage);
    Assert.AreEqual(string.Empty, viewModel.RefreshToken);
    Assert.AreEqual(deviceId, viewModel.DeviceId);
}
```

增加超长输入测试：

```csharp
[TestMethod]
public async Task LoginWithRefreshTokenCommand_OversizedInputDoesNotCallService()
{
    foreach (var (refreshToken, deviceId, expectedMessage) in new[]
             {
                 (
                     new string('r', 16_385),
                     "web-id",
                     "RefreshToken 格式无效。"),
                 (
                     "token",
                     new string('x', 257),
                     "x-id 格式无效。")
             })
    {
        var service = new FakeAuthService();
        var viewModel = CreateViewModel(service)
        {
            RefreshToken = refreshToken,
            DeviceId = deviceId
        };

        await viewModel.LoginWithRefreshTokenCommand.ExecuteAsync(null);

        Assert.AreEqual(0, service.RefreshTokenLoginCount);
        Assert.AreEqual(expectedMessage, viewModel.ErrorMessage);
        Assert.AreEqual(string.Empty, viewModel.RefreshToken);
        Assert.AreEqual(deviceId, viewModel.DeviceId);
    }
}
```

- [ ] **Step 2: 添加并发保护测试**

```csharp
[TestMethod]
public async Task LoginWithRefreshTokenCommand_WhilePendingBlocksPasswordLogin()
{
    var tokenLoginEntered = new TaskCompletionSource(
        TaskCreationOptions.RunContinuationsAsynchronously);
    var profileCompletion = new TaskCompletionSource<UserProfile>(
        TaskCreationOptions.RunContinuationsAsynchronously);
    var service = new FakeAuthService
    {
        RefreshTokenLoginHandler = (_, _, _) =>
        {
            tokenLoginEntered.TrySetResult();
            return profileCompletion.Task;
        }
    };
    var viewModel = CreateViewModel(service)
    {
        RefreshToken = "imported-refresh",
        DeviceId = "web-id",
        Email = "reader@example.com",
        Password = "password123"
    };

    var tokenLogin =
        viewModel.LoginWithRefreshTokenCommand.ExecuteAsync(null);
    await tokenLoginEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
    viewModel.LoginCommand.Execute(null);

    Assert.IsTrue(viewModel.IsBusy);
    Assert.AreEqual(1, service.RefreshTokenLoginCount);
    Assert.AreEqual(0, service.LoginCount);

    profileCompletion.SetResult(Profile());
    await tokenLogin.WaitAsync(TimeSpan.FromSeconds(5));
    Assert.IsFalse(viewModel.IsBusy);
}
```

- [ ] **Step 3: 运行测试并确认 ViewModel 尚未实现**

Run:

```powershell
dotnet test tests\NovelM.Tests\NovelM.Tests.csproj -p:Platform=x64 --filter "FullyQualifiedName~AccountViewModelTests"
```

Expected: FAIL/compile error，缺少属性或命令。

- [ ] **Step 4: 增加属性和 Token 登录命令**

在构造函数中把两个新字符串初始化为空，并增加属性：

```csharp
[ObservableProperty]
public partial string RefreshToken { get; set; }

[ObservableProperty]
public partial string DeviceId { get; set; }
```

增加命令：

```csharp
[RelayCommand(AllowConcurrentExecutions = false)]
private async Task LoginWithRefreshTokenAsync()
{
    if (IsBusy)
    {
        return;
    }

    IsBusy = true;
    ErrorMessage = null;
    CurrentUser = null;
    try
    {
        var normalizedRefreshToken =
            ImportedCredentialValidator.NormalizeRefreshToken(RefreshToken);
        var normalizedDeviceId =
            ImportedCredentialValidator.NormalizeDeviceId(DeviceId);
        CurrentUser = await _authService.LoginWithRefreshTokenAsync(
            normalizedRefreshToken,
            normalizedDeviceId,
            CancellationToken.None);
    }
    catch (Exception exception)
    {
        ErrorMessage = _errorMessageMapper.Map(exception);
    }
    finally
    {
        RefreshToken = string.Empty;
        IsBusy = false;
    }
}
```

- [ ] **Step 5: 运行 ViewModel 测试**

Run:

```powershell
dotnet test tests\NovelM.Tests\NovelM.Tests.csproj -p:Platform=x64 --filter "FullyQualifiedName~AccountViewModelTests"
```

Expected: PASS。

- [ ] **Step 6: 提交 ViewModel**

```bash
git add src/NovelM.App/Presentation/Account/AccountViewModel.cs tests/NovelM.Tests/Presentation/AccountViewModelTests.cs
git commit -m "feat: 增加 Token 登录视图模型"
```

---

### Task 5: 增加登录标签并收窄导航栏

**Files:**
- Modify: `src/NovelM.App/Presentation/Account/AccountPage.xaml`
- Modify: `src/NovelM.App/Presentation/Account/AccountPage.xaml.cs`
- Modify: `src/NovelM.App/MainWindow.xaml`
- Create: `tests/NovelM.Tests/Presentation/AccountPageXamlTests.cs`
- Modify: `tests/NovelM.Tests/NovelM.Tests.csproj`

- [ ] **Step 1: 链接 XAML 测试源并写失败测试**

在测试项目 Content ItemGroup 中增加：

```xml
<Content Include="..\..\src\NovelM.App\Presentation\Account\AccountPage.xaml"
         Link="TestSources\AccountPage.xaml"
         CopyToOutputDirectory="PreserveNewest" />
<Content Include="..\..\src\NovelM.App\MainWindow.xaml"
         Link="TestSources\MainWindow.xaml"
         CopyToOutputDirectory="PreserveNewest" />
```

创建 `AccountPageXamlTests.cs`：

```csharp
using System.Xml.Linq;

namespace NovelM.Tests.Presentation;

[TestClass]
public sealed class AccountPageXamlTests
{
    private static readonly XNamespace XamlNamespace =
        "http://schemas.microsoft.com/winfx/2006/xaml";

    [TestMethod]
    public void SignedOutUi_ProvidesPasswordAndRefreshTokenTabs()
    {
        var document = ReadXaml("AccountPage.xaml");
        var pivot = FindNamedElement(document, "LoginMethodPivot");
        Assert.AreEqual("Pivot", pivot.Name.LocalName);
        Assert.AreEqual("0", (string?)pivot.Attribute("SelectedIndex"));
        CollectionAssert.AreEqual(
            new[] { "账号密码", "RefreshToken" },
            pivot.Elements()
                .Where(element => element.Name.LocalName == "PivotItem")
                .Select(element => (string?)element.Attribute("Header"))
                .ToArray());

        var tokenInput = FindNamedElement(document, "RefreshTokenInput");
        Assert.AreEqual("PasswordBox", tokenInput.Name.LocalName);
        Assert.AreEqual("Peek", (string?)tokenInput.Attribute("PasswordRevealMode"));
        Assert.AreEqual(
            "RefreshTokenInput_PasswordChanged",
            (string?)tokenInput.Attribute("PasswordChanged"));

        var deviceIdInput = FindNamedElement(document, "DeviceIdInput");
        Assert.AreEqual(
            "{x:Bind ViewModel.DeviceId, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}",
            (string?)deviceIdInput.Attribute("Text"));
        var notice = FindNamedElement(document, "CredentialReplacementNotice");
        Assert.AreEqual(
            "登录时会替换本机保存的 x-id 和 RefreshToken。",
            (string?)notice.Attribute("Text"));

        var button = FindNamedElement(document, "RefreshTokenLoginButton");
        Assert.AreEqual(
            "{x:Bind ViewModel.LoginWithRefreshTokenCommand}",
            (string?)button.Attribute("Command"));
    }

    [TestMethod]
    public void MainNavigation_UsesTwoHundredPixelOpenPane()
    {
        var document = ReadXaml("MainWindow.xaml");
        var navigation = FindNamedElement(document, "NavView");

        Assert.AreEqual("200", (string?)navigation.Attribute("OpenPaneLength"));
        Assert.AreEqual("Auto", (string?)navigation.Attribute("PaneDisplayMode"));
    }

    private static XDocument ReadXaml(string fileName) =>
        XDocument.Load(Path.Combine(
            AppContext.BaseDirectory,
            "TestSources",
            fileName));

    private static XElement FindNamedElement(XDocument document, string name) =>
        document.Descendants()
            .Single(element =>
                (string?)element.Attribute(XamlNamespace + "Name") == name);
}
```

- [ ] **Step 2: 运行 XAML 测试并确认命名元素尚不存在**

Run:

```powershell
dotnet test tests\NovelM.Tests\NovelM.Tests.csproj -p:Platform=x64 --filter "FullyQualifiedName~AccountPageXamlTests"
```

Expected: FAIL，找不到 `LoginMethodPivot` 或 `OpenPaneLength`。

- [ ] **Step 3: 用 Pivot 替换未登录表单**

在 `AccountPage.xaml` 中把现有未登录 `StackPanel` 替换为：

```xml
<Pivot
    x:Name="LoginMethodPivot"
    SelectedIndex="0"
    Visibility="{x:Bind ViewModel.IsSignedOut, Mode=OneWay}">
    <PivotItem Header="账号密码">
        <StackPanel Padding="0,12,0,0" Spacing="12">
            <TextBox
                Header="邮箱"
                PlaceholderText="reader@example.com"
                Text="{x:Bind ViewModel.Email, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" />
            <PasswordBox
                x:Name="PasswordInput"
                Header="密码"
                PasswordChanged="PasswordInput_PasswordChanged" />
            <Button
                Command="{x:Bind ViewModel.LoginCommand}"
                Content="登录"
                Style="{ThemeResource AccentButtonStyle}" />
        </StackPanel>
    </PivotItem>
    <PivotItem Header="RefreshToken">
        <StackPanel Padding="0,12,0,0" Spacing="12">
            <PasswordBox
                x:Name="RefreshTokenInput"
                Header="RefreshToken"
                PasswordChanged="RefreshTokenInput_PasswordChanged"
                PasswordRevealMode="Peek" />
            <TextBox
                x:Name="DeviceIdInput"
                Header="x-id"
                PlaceholderText="从 Web 客户端复制 x-id"
                Text="{x:Bind ViewModel.DeviceId, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" />
            <TextBlock
                x:Name="CredentialReplacementNotice"
                Foreground="{ThemeResource TextFillColorSecondaryBrush}"
                Text="登录时会替换本机保存的 x-id 和 RefreshToken。"
                TextWrapping="Wrap" />
            <Button
                x:Name="RefreshTokenLoginButton"
                Command="{x:Bind ViewModel.LoginWithRefreshTokenCommand}"
                Content="Token 登录"
                Style="{ThemeResource AccentButtonStyle}" />
        </StackPanel>
    </PivotItem>
</Pivot>
```

- [ ] **Step 4: 同步并清理 RefreshToken PasswordBox**

在 `AccountPage.xaml.cs`：

1. `Loaded` 中在 `UpdatePassword()` 后调用 `UpdateRefreshToken()`。
2. `Unloaded` 中清空 `RefreshTokenInput.Password` 和 `ViewModel.RefreshToken`。
3. `PropertyChanged` 中处理 `nameof(AccountViewModel.RefreshToken)`。
4. 增加：

```csharp
private void RefreshTokenInput_PasswordChanged(
    object sender,
    RoutedEventArgs args)
{
    if (!string.Equals(
        ViewModel.RefreshToken,
        RefreshTokenInput.Password,
        StringComparison.Ordinal))
    {
        ViewModel.RefreshToken = RefreshTokenInput.Password;
    }
}

private void UpdateRefreshToken()
{
    if (string.IsNullOrEmpty(ViewModel.RefreshToken)
        && RefreshTokenInput.Password.Length != 0)
    {
        RefreshTokenInput.Password = string.Empty;
    }
}
```

- [ ] **Step 5: 设置导航栏展开宽度**

在 `MainWindow.xaml` 的 `NavView` 上增加：

```xml
OpenPaneLength="200"
```

保留 `PaneDisplayMode="Auto"`。

- [ ] **Step 6: 运行 XAML、ViewModel 和构建验证**

Run:

```powershell
dotnet test tests\NovelM.Tests\NovelM.Tests.csproj -p:Platform=x64 --filter "FullyQualifiedName~AccountPageXamlTests|FullyQualifiedName~AccountViewModelTests"
dotnet build src\NovelM.App\NovelM.App.csproj -c Release -p:Platform=x64
```

Expected: 两条命令均 PASS；WinUI XAML 编译成功。

- [ ] **Step 7: 提交界面和导航调整**

```bash
git add src/NovelM.App/Presentation/Account/AccountPage.xaml src/NovelM.App/Presentation/Account/AccountPage.xaml.cs src/NovelM.App/MainWindow.xaml tests/NovelM.Tests/Presentation/AccountPageXamlTests.cs tests/NovelM.Tests/NovelM.Tests.csproj
git commit -m "feat: 增加 Token 登录界面并收窄导航栏"
```

---

### Task 6: 完整回归和安全检查

**Files:**
- Verify only. 若失败，返回负责该行为的早期 Task，先增加或修正回归测试及实现并提交，再重新执行本 Task。

- [ ] **Step 1: 检查敏感值没有进入生产错误文本**

Run:

```bash
grep -RIn --exclude-dir=bin --exclude-dir=obj "imported-refresh-secret\|synthetic-refresh-secret" src/NovelM.App || true
```

Expected: 无输出。

- [ ] **Step 2: 运行完整 Release 构建**

Run:

```powershell
dotnet build NovelM.sln -c Release -p:Platform=x64 --disable-build-servers -m:1 -nr:false
```

Expected: Build succeeded，0 errors。

- [ ] **Step 3: 运行完整测试套件**

Run:

```powershell
dotnet test NovelM.sln -c Release -p:Platform=x64 --no-build --disable-build-servers -m:1 -nr:false
```

Expected: 全部测试通过，0 failed。

- [ ] **Step 4: 检查提交范围和行尾噪声**

Run:

```bash
git status --short
BASE=$(git merge-base origin/main HEAD)
git diff "$BASE"..HEAD --check
git diff "$BASE"..HEAD --stat
```

Expected: 工作区干净；无 whitespace error；diff 只包含本规格、实施计划及计划列出的源码/测试文件，没有整文件无意义换行替换。

- [ ] **Step 5: 执行并在最终交付消息中记录手工验收结果**

在 Windows 启动应用并确认：

1. 账户页默认打开“账号密码”。
2. 可切换到“RefreshToken”。
3. RefreshToken 默认遮罩且可使用系统显示按钮。
4. 无效输入显示中文错误并清空 Token 输入。
5. 有效 Web RefreshToken + `x-id` 能显示用户资料。
6. 重启应用后能恢复会话。
7. 展开导航栏宽度为 200 像素，自动折叠行为不变。

若只具备自动化环境而没有真实凭据，第 5–6 项明确记录为“需要真实账号手工验证”，不得用本地模拟测试冒充真实服务验收。
