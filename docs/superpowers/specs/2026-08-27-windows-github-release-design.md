# Windows GitHub Release 自动构建设计

## 目标

为 NovelM 增加 Windows GitHub Actions 发布流水线：每次推送 `main` 时验证并生成可下载构建产物；推送严格格式的 `vX.Y.Z` 标签时，使用同一批产物创建正式 GitHub Release。每次构建同时生成一个框架依赖 ZIP 和一个 Inno Setup Windows 安装程序。

## 已确认范围

- 仅构建 Windows x64 版本。
- 保持现有框架依赖发布方式，不改为自包含发布。
- 使用 Inno Setup 生成未签名的 `.exe` 安装程序。
- ZIP 用户自行准备运行库。
- 安装程序仅检查运行库；缺失时列出缺失项与微软官方下载地址并中止，不下载或安装运行库。
- 仅 `vX.Y.Z` 标签创建正式 Release；普通 `main` 推送只构建并上传 Action Artifacts。
- 不增加 MSIX、MSI、自动更新、代码签名、ARM64 或 x86 产物。

## 工作流架构

新增 `.github/workflows/windows-release.yml`，包含两个 Job：

1. `build` 在 `windows-2025` Runner 上执行还原、测试、发布、ZIP 打包、安装器编译和产物冒烟验证。该 Job 只有仓库内容读取权限。
2. `release` 仅在严格匹配 `vX.Y.Z` 的标签构建成功后运行。它下载 `build` 上传的产物，并以 `contents: write` 权限调用 GitHub CLI 创建正式 Release。

工作流触发条件：

- 推送到 `main`。
- 推送以 `v` 开头的标签；Job 内再次使用严格表达式校验 `^v[0-9]+\.[0-9]+\.[0-9]+$`，不合规标签立即失败。

主分支构建使用 `0.0.0-ci.<run_number>` 作为临时产物版本。标签构建移除前导 `v` 后，把 `X.Y.Z` 同步传给 .NET 发布和 Inno Setup。

## 构建与产物

`build` Job 按以下顺序执行：

1. 检出仓库并按 `global.json` 安装 .NET 10 SDK。
2. 还原 `NovelM.sln`。
3. 运行 x64 全量测试。
4. 以 Release、x64、`SelfContained=false`、`WindowsAppSDKSelfContained=false` 发布 `NovelM.App.csproj`。
5. 验证发布目录包含 `NovelM.exe`，且不包含 `data` 或 `runtime`。
6. 将发布目录压缩为 ZIP，并重新展开验证 `NovelM.exe`。
7. 使用 Runner 自带的 `ISCC.exe` 编译 `installer/NovelM.iss`。
8. 在 Runner 临时目录静默安装和卸载，验证程序文件能正确写入并删除。
9. 上传 ZIP 与安装器作为 Action Artifacts。

标签 `v1.2.3` 生成：

- `NovelM-v1.2.3-win-x64.zip`
- `NovelM-v1.2.3-win-x64-setup.exe`

`release` Job 使用标签作为 Release 标题，生成发布说明，并附加运行库要求、未签名 SmartScreen 提示及两个产物。创建命令必须校验远程标签确实存在；已有同名 Release 时失败，不覆盖已有资产。

## 安装程序

新增 `installer/NovelM.iss`：

- 安装器产品名和快捷方式名使用“轻书架”，程序文件继续使用 `NovelM.exe`。
- 固定 `AppId` 为 `7F45FC28-AE14-4D8A-B594-3C01C86BF53D`，使后续版本识别为同一应用并支持覆盖升级。
- `PrivilegesRequired=lowest`，按当前用户安装。
- 默认安装到 `{localappdata}\Programs\NovelM`，保证应用可以继续在自身目录创建 `data` 与 `runtime`。
- 创建开始菜单快捷方式和标准卸载入口；不默认创建桌面快捷方式。
- 安装发布目录中的全部应用文件，但构建产物本身不得包含 `data` 或 `runtime`。
- 升级时不覆盖用户生成的 `data`、`runtime`；卸载时也不主动删除它们。
- 安装前检查 x64 .NET 10 Desktop Runtime、Windows App Runtime 2.4 和 Microsoft Visual C++ 2015–2022 Redistributable。
- 任一依赖缺失时，以中文列出缺失项及微软官方下载地址，然后终止安装。
- 安装器首版不进行代码签名；Release 说明明确提示可能出现 SmartScreen“未知发布者”。

## 依赖检测

- .NET 10 Desktop Runtime：检查 x64 `Microsoft.WindowsDesktop.App` 已安装的 10.x 版本。
- Windows App Runtime 2.4：查询当前用户已注册的 x64 `Microsoft.WindowsAppRuntime.2.4` 运行时包。
- Visual C++ 2015–2022 Redistributable：检查微软定义的 x64 VC Runtime 注册表项及安装状态。

安装器显示以下固定官方下载地址：

- .NET 10：https://dotnet.microsoft.com/download/dotnet/10.0
- Windows App Runtime：https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads
- Visual C++ x64：https://aka.ms/vc14/vc_redist.x64.exe

检测只读取本机状态。安装程序不会自动访问网络、下载文件、启动浏览器或修改这些依赖。

## 权限与失败行为

- Workflow 默认 `contents: read`。
- 只有 `release` Job 获得 `contents: write`。
- 不使用仓库 Secrets，不保存签名证书。
- 标签格式、测试、发布、ZIP、Inno 编译或冒烟验证任一步失败，`release` Job 都不运行。
- Release 创建使用现有 `GITHUB_TOKEN`；不引入第三方 Release Action。
- 重复标签或已有同名 Release 不覆盖，直接失败并保留原 Release。

## 验证标准

- 现有测试在 Windows x64 环境全部通过；当前基线为 413 项。
- 框架依赖发布成功且包含 `NovelM.exe`。
- ZIP 解压后包含可执行文件，不包含仓库、本地缓存、`data` 或 `runtime`。
- Inno Setup 编译成功并生成预期文件名。
- 静默安装把 `NovelM.exe` 放入指定临时安装目录；静默卸载删除安装的程序文件。
- `main` 推送只产生 Action Artifacts，不创建 Release。
- 合法 `vX.Y.Z` 标签创建一个正式 Release，并上传 ZIP 与安装器。
- 非法 `v*` 标签不创建 Release。

本功能仅增加构建和安装配置，不修改 C# 业务行为。经确认，采用配置编译、安装冒烟与产物断言作为测试方式，不新增先失败的 C# 单元测试。
