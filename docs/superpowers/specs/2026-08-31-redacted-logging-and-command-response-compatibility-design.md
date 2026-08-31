# 脱敏日志与写操作响应兼容设计

## 1. 背景与目标

NovelM 当前通过 SignalR 调用 `UpdateBook` 保存漫画信息和高级设置。服务端已经完成修改，但部分成功响应不携带可供客户端解压、反序列化的 `Response`。客户端目前把所有调用都当作“必须返回数据”的查询，通过 `CompressedResponseDecoder.Decode<T>` 强制解析响应体，因此在服务端提交成功后仍抛出协议异常，界面显示“服务器响应格式不兼容”。

本次改动有两个目标：

1. 区分“需要返回数据的查询”和“只需确认成功的命令”，让成功但无业务响应体的写操作正常完成，同时继续检查 envelope 中的失败状态。
2. 增加持久化、脱敏、可轮转的诊断日志，便于定位 HTTP、SignalR、协议和未处理异常，不记录凭据或业务正文。

## 2. 方案比较与选择

### 方案 A：增加显式命令调用接口（采用）

在 `ISignalRConnection` 增加 `InvokeCommandAsync`。该方法仍按既有 MessagePack envelope 调用服务器，并检查 `Success`、`Status` 和 `Msg`；当 `Success == true` 时不要求 `Response` 存在，也不解析无契约的响应体。所有不消费返回值的发布管理操作使用该接口。

优点：查询与命令语义明确；不会影响查询响应的严格校验；能覆盖删除、更新和重排等同类问题。缺点：需要同步更新测试替身和调用点。

### 方案 B：让 `Decode<JsonElement?>` 全局接受空响应

改动较少，但把“空响应合法”隐含在泛型类型中，容易让其他本应有响应的调用漏报协议错误。

### 方案 C：ViewModel 捕获协议异常并当作成功

改动最小，但无法区分“服务端已成功、响应为空”和真实的传输/协议损坏，也会在错误层级隐藏问题，因此不采用。

## 3. 架构与组件

### 3.1 SignalR 命令语义

`ISignalRConnection` 保留现有 `InvokeAsync<T>` 供查询使用，并新增：

```csharp
Task InvokeCommandAsync(
    string methodName,
    object? request,
    CancellationToken cancellationToken);
```

`SignalRConnection` 的查询和命令共用连接建立、未授权刷新与单次重试流程。底层调用分别执行：

- 查询：取得 `HubEnvelope<byte[]>`，验证 envelope，再解压、解析为 `T`。
- 命令：取得 `HubEnvelope<byte[]>`，验证 envelope；成功后忽略可选 `Response`。

`CompressedResponseDecoder` 增加只验证 envelope 结果的命令入口，复用失败状态分类，避免查询与命令对 `Success == false` 的处理发生偏差。

以下无返回值操作改用命令接口：

- `DeleteBook`
- `UpdateBook`（漫画信息与高级设置）
- `UpdateComicChapter`
- `DeleteChapter`
- `ReorderChapter`

`QuickCreateComic`、`CreateNewComicChapter`、上传和所有读取操作仍使用严格的 `InvokeAsync<T>`。

### 3.2 脱敏文件日志

新增 `Infrastructure/Logging`：

- `IDiagnosticLog`：基础设施依赖的内部日志接口。
- `RedactedFileLog`：JSON Lines 文件实现。
- `NullDiagnosticLog`：仅供不需要落盘的测试替身使用；运行时依赖注入必须注册 `RedactedFileLog`。

日志写入 `AppPaths.LogDirectory/app.log`，达到 1 MiB 后轮转：

- 当前文件：`app.log`
- 最近历史：`app.1.log`
- 更早历史：`app.2.log`

写入由单个异步锁串行化。日志目录不存在时自动创建。任何日志写入失败都由日志组件内部吞掉，不能覆盖原始业务结果或异常。

每条日志包含 UTC 时间、事件名、关联 ID，以及白名单中的安全字段。白名单限定为：

- `operation`
- `host`
- `httpStatus`
- `serverStatus`
- `hubMethod`
- `stage`
- `responseType`
- `byteLength`
- `elapsedMs`
- `connectionState`
- `correlationId`
- `errorKind`

任意其他字段直接丢弃。不得把请求对象、响应正文、章节内容、图片字节、HTTP Header 或 Token 传给日志接口。

异常日志只记录异常类型、`AppException.Kind`/状态和堆栈；不记录任意异常消息，从源头避免消息中夹带邮箱、密码、Session Token 或 Refresh Token。内部异常按相同规则记录有限层级。

### 3.3 日志接入点

- `ApiHttpClient`：记录请求完成/失败、操作名、主机、HTTP 状态、响应字节数、耗时和关联 ID，不记录 payload 或响应正文。
- `SignalRConnection`：记录连接状态、Hub 调用完成/失败、方法名、服务器状态、响应字节数、耗时和关联 ID。
- `App`：记录启动阶段失败和未处理异常；未处理异常日志不改变 WinUI 原有终止语义，避免应用在未知状态下继续运行。

日志在 Debug 与 Release 中都启用。

## 4. 数据流

### 4.1 保存漫画信息

```text
ComicEditorViewModel.SaveInfoAsync
  → ComicPublishingService.UpdateInfoAsync
  → SignalRComicPublishingApi.UpdateComicInfoAsync
  → ISignalRConnection.InvokeCommandAsync("UpdateBook", request)
  → HubEnvelope<byte[]>
  → Success == false：按 Server/Unauthorized 抛错
  → Success == true：不要求 Response，记录安全诊断并返回
  → ViewModel 显示“漫画信息已保存”
```

高级设置及其他无返回值写操作使用相同流程。

### 4.2 查询调用

查询仍要求成功 envelope 含有效 GZIP、UTF-8 JSON 和符合 DTO 约束的数据。空响应或错误格式继续映射为 `AppErrorKind.Protocol`，不会因为本次兼容改动而放宽。

## 5. 错误处理与安全

- `Success == false` 始终优先于响应体处理，原有 Unauthorized/Server 分类保持不变。
- 取消操作保持 `OperationCanceledException`，不作为失败日志制造噪声；可记录安全的取消阶段但不记录异常正文。
- 日志失败不得改变命令成功、查询成功或原始异常。
- 日志中禁止出现邮箱、密码、Authorization、设备 ID、Session Token、Refresh Token、请求/响应正文、章节正文和图片内容。
- 日志仅保存在应用现有 `data/logs` 目录，不增加网络上传或日志查看 UI。

## 6. 测试策略

### 6.1 命令响应兼容

- 单元测试：成功且 `Response == null` 的命令验证通过。
- 单元测试：成功但携带任意/无效业务响应字节的命令仍成功，因为命令不消费响应体。
- 单元测试：失败 envelope 仍产生正确的 Server/Unauthorized 异常。
- SignalR 集成测试：本地 MessagePack Hub 对命令返回成功空响应，调用只执行一次并成功完成。
- API 映射测试：所有无返回值发布操作使用 `InvokeCommandAsync`；查询仍使用 `InvokeAsync<T>`。

### 6.2 日志

- 只保留白名单字段，未知字段不落盘。
- 异常消息中的模拟邮箱、密码和 Token 不落盘。
- 请求/响应正文不进入日志。
- 超过可注入的小尺寸上限后只保留当前文件和两份历史文件。
- 并发写入产生完整的逐行 JSON，不互相覆盖。
- 模拟日志目录不可写时，原始业务异常或成功结果保持不变。
- HTTP 与 SignalR 测试验证日志包含操作/方法、阶段、状态、长度、耗时和关联 ID。

最终执行：

```powershell
dotnet test NovelM.sln -p:Platform=x64
dotnet build NovelM.sln -p:Platform=x64
```

## 7. 验收标准

- 修改漫画信息或高级设置时，服务端成功且返回空业务响应，客户端显示保存成功，不再显示“服务器响应格式不兼容”。
- 查询接口的空响应仍被视为协议错误。
- 其他无返回值漫画管理操作采用相同命令语义。
- `data/logs` 中持续生成脱敏、轮转的诊断日志。
- 日志可定位请求类型、方法、阶段、状态和响应长度，但不包含凭据或业务正文。
- 日志写入故障不影响业务操作。
- 自动化测试与 x64 构建全部通过。
