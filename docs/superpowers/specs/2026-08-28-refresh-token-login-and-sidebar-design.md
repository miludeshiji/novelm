# RefreshToken 登录与侧边栏收窄设计

## 1. 背景

当前 WinUI 客户端只支持邮箱和密码登录。Web 端的长期认证凭据由 RefreshToken 与请求头 `x-id` 共同工作，用户需要一种方式把已有 Web 登录凭据导入客户端。

主窗口的 `NavigationView` 使用默认展开宽度，当前视觉占用过大，需要将展开后的左侧边栏固定收窄到 200 像素。

## 2. 目标

1. 保留现有邮箱密码登录。
2. 新增 RefreshToken 登录方式，要求用户输入 RefreshToken 和 `x-id`。
3. 支持 Web FingerprintJS 产生的非 GUID `x-id`。
4. Token 登录时立即覆盖并持久化本机的 `x-id` 和 RefreshToken，然后复用现有恢复会话链路完成登录。
5. 登录完成后继续使用现有用户资料卡片和退出登录行为。
6. 将主窗口导航栏的展开宽度设置为 200 像素。

## 3. 非目标

- 不增加扫码登录、浏览器 OAuth 或 Cookie 导入。
- 不改变邮箱密码登录协议。
- 不改变 API 节点切换规则。
- 不为导入凭据提供事务式回滚。
- 不改变导航栏的折叠宽度、自动折叠模式或菜单内容。

## 4. 已确认的产品决策

- 登录页使用“账号密码”和“RefreshToken”两个标签，默认打开账号密码登录。
- RefreshToken 使用带系统显示能力的遮罩输入框。
- `x-id` 使用普通单行文本框。
- 用户输入的 `x-id` 成为应用后续所有 HTTP 请求使用的持久设备 ID。
- 采用“先覆盖本地凭据，再恢复会话”的简单流程。
- 登录失败不回滚新 `x-id`；RefreshToken 无效时，现有认证逻辑可以删除已导入的无效 Token。
- Token 登录成功或失败后清空 RefreshToken 输入，保留 `x-id` 方便修正后重试。
- 导航栏展开宽度为 `200`。

## 5. 架构调整

### 5.1 设备身份抽象

在 `Application/Abstractions` 增加 `IDeviceIdStore`，提供：

- `GetOrCreateAsync`：读取已有 `x-id`；不存在时生成 GUID 字符串并持久化。
- `SetAsync`：以原子临时文件替换方式持久化用户输入的 `x-id`。

`Infrastructure/Storage/DeviceIdStore` 实现该接口。`ApiHttpClient` 和 `AuthService` 均依赖接口，而不直接依赖基础设施类型。

现有 `device.json` 结构保持不变：

```json
{
  "Id": "device-identity-value"
}
```

读取时不再要求 `Id` 必须是 GUID，但仍执行与用户输入相同的安全校验。首次自动创建的值仍是 `Guid.NewGuid().ToString("D")`。

### 5.2 AuthSession 导入能力

`IAuthSession` 增加导入 RefreshToken 的方法。该操作：

1. 先通过现有 `ITokenStore` 持久化 RefreshToken；
2. 持久化成功后清除内存 SessionToken；
3. 持久化失败时保留原内存 SessionToken并抛出原异常。

之后调用现有 `GetAccessTokenAsync` 时，会读取新 RefreshToken，通过 `/api/user/refresh_token` 获取 SessionToken。无效 Token 继续沿用现有行为：删除持久化 Token并返回空结果。

### 5.3 AuthService 新用例

`IAuthService` 增加：

```csharp
Task<UserProfile> LoginWithRefreshTokenAsync(
    string refreshToken,
    string deviceId,
    CancellationToken cancellationToken);
```

`AuthService` 负责输入规范化、认证生命周期互斥和用户状态发布。调用顺序固定为：

```text
规范化 RefreshToken 与 x-id
    → IDeviceIdStore.SetAsync(x-id)
    → IAuthSession.ImportRefreshTokenAsync(refreshToken)
    → IAuthSession.GetAccessTokenAsync
    → ISignalRConnection.RestartAsync
    → IUserApi.GetMyInfoAsync
    → 发布 CurrentUser
```

如果 `GetAccessTokenAsync` 没有返回 SessionToken，`AuthService` 抛出 `Unauthorized` 类型的 `AppException`，由现有 `ErrorMessageMapper` 显示“登录已失效，请重新登录。”。

该用例复用现有用户操作 generation、取消和 `_authLifecycleGate` 规则，确保退出登录可以使尚未完成的 Token 登录结果失效。

### 5.4 依赖注入

`App.xaml.cs` 注册：

```text
IDeviceIdStore → DeviceIdStore（Singleton）
```

应用启动检查、`ApiHttpClient` 和 `AuthService` 统一解析该接口，保证它们操作同一份 `device.json`。

## 6. 数据与失败语义

采用非事务流程，具体提交边界如下：

1. `x-id` 保存成功后立即成为后续 HTTP 请求头值。
2. RefreshToken 保存成功后立即替换旧 Token，内存 SessionToken 被清除。
3. 刷新 SessionToken、重启 SignalR 或获取用户资料失败，不恢复旧 `x-id` 或旧 RefreshToken。
4. 无效 RefreshToken 被刷新接口拒绝时，现有 `AuthSession` 会删除该 Token。
5. 失败时 `CurrentUser` 保持为空，界面仍处于未登录状态。
6. 如果 `x-id` 保存失败，RefreshToken 尚未写入。
7. 如果 RefreshToken 保存失败，新 `x-id` 已保留，但旧内存 SessionToken不被清除。

这是用户明确接受的方案 3 行为。

## 7. 输入校验

### 7.1 `x-id`

- 先执行 `Trim()`。
- 长度必须为 1 至 256 个字符。
- 不允许任何 Unicode 控制字符，防止非法 HTTP Header 值。
- 不限制为 GUID；允许 Web FingerprintJS `visitorId`。

### 7.2 RefreshToken

- 先执行 `Trim()`，消除复制时带入的首尾空白。
- 长度必须为 1 至 16384 个字符。
- 不允许任何 Unicode 控制字符。
- 校验异常不得包含输入原文。

校验在写入文件或调用网络前完成。存储实现还会对 `x-id` 执行防御性校验，避免无效 `device.json` 值进入 HTTP Header。

## 8. 界面设计

`Presentation/Account/AccountPage.xaml` 在未登录区域使用两个标签：

### 8.1 账号密码

保留现有邮箱、密码和登录按钮，且作为默认标签。

### 8.2 RefreshToken

包含：

- 名称为 `RefreshTokenInput` 的 `PasswordBox`；
- 系统支持的显示/隐藏能力；
- 绑定到 ViewModel 的 `x-id` 文本框；
- 绑定到新命令的“Token 登录”按钮；
- 提示文字，明确操作会替换本机保存的 `x-id` 和 RefreshToken。

`AccountPage.xaml.cs` 沿用现有 PasswordBox 同步模式：

- 输入变化时同步到 ViewModel；
- ViewModel 清空 Token 时清空控件；
- 页面卸载时同时清空密码和 RefreshToken。

`AccountViewModel` 新增：

- `RefreshToken`；
- `DeviceId`；
- Token 登录命令。

Token 登录命令与现有登录命令共享 `IsBusy` 和错误区域。命令结束时始终清空 RefreshToken，但不清空 `DeviceId`。

## 9. 导航栏设计

在 `MainWindow.xaml` 的现有 `NavigationView` 上增加：

```xml
OpenPaneLength="200"
```

保留：

```xml
PaneDisplayMode="Auto"
```

不修改 `CompactPaneLength`、标题栏按钮、菜单项或页面切换逻辑。

## 10. 错误与安全

- 输入错误使用 `AppErrorKind.Validation` 和明确的中文提示。
- 无效或过期 RefreshToken 使用 `AppErrorKind.Unauthorized`。
- 网络、协议、服务端和存储错误继续交由 `ErrorMessageMapper` 安全映射。
- RefreshToken 使用遮罩输入，不写入普通配置文件。
- 持久化 RefreshToken 继续使用 DPAPI CurrentUser 加密。
- 操作结束和页面卸载时清空界面及 ViewModel 中的 RefreshToken。
- 异常消息、`ToString()`、断言失败消息和日志不得包含 Token 原文。
- `IsBusy` 和认证生命周期锁防止两种登录操作同时修改认证状态。

## 11. 测试策略

### 11.1 DeviceIdStore

- 缺少文件时生成并复用 GUID 字符串。
- 保存并重新读取非 GUID `x-id`。
- 替换已有值后不遗留临时文件。
- 拒绝空值、超长值和控制字符。
- 并发首次创建仍只有一个持久化结果。

### 11.2 ApiHttpClient

- 每个 HTTP 请求使用字符串形式的持久 `x-id`。
- 替换 `x-id` 后，下一次请求使用新 Header 值。

### 11.3 AuthSession

- 导入 RefreshToken 时先保存，再清除内存 SessionToken。
- 保存失败时保留原 SessionToken。
- 导入成功后的下一次访问只执行一次共享刷新。
- 无效导入 Token 继续触发删除行为。

### 11.4 AuthService

- Token 登录严格遵循保存设备 ID、导入 Token、刷新、重启、获取资料的顺序。
- 成功后发布用户资料。
- 空 SessionToken 返回 Unauthorized。
- 保存、刷新、重启和资料请求失败时的最终状态符合非回滚语义。
- 退出登录可取消并拒绝过期的 Token 登录结果。
- 测试记录和异常不包含 RefreshToken 原文。

### 11.5 AccountViewModel 与 XAML

- 合法输入会被 Trim 后传给服务。
- 无效输入不会调用服务。
- 成功和失败后均清空 RefreshToken并保留 `x-id`。
- 两个登录命令不能同时重复提交。
- XAML 包含两个标签、遮罩 Token 输入、绑定和替换凭据提示。
- `MainWindow.xaml` 的 `OpenPaneLength` 精确为 200。

### 11.6 完整验证

在 Windows x64 环境执行：

```powershell
dotnet build NovelM.sln -c Release -p:Platform=x64
dotnet test NovelM.sln -c Release -p:Platform=x64 --no-build
```

## 12. 验收标准

1. 未登录账户页默认显示账号密码标签，并可切换到 RefreshToken 标签。
2. 输入有效 RefreshToken 和 Web `x-id` 后可刷新会话、连接 SignalR 并显示当前用户资料。
3. 重启应用后可以使用导入的 `x-id` 与受 DPAPI 保护的 RefreshToken 恢复会话。
4. 导入后所有 HTTP 请求使用新 `x-id`。
5. 非 GUID `x-id` 可正常保存和使用。
6. RefreshToken 不以明文落盘，也不会出现在可见错误中。
7. 失败时遵循已确认的非回滚语义。
8. 左侧导航栏展开宽度为 200 像素，自动折叠行为不变。
9. 新增和现有自动化测试通过，Release 构建成功。
