# NovelM

[![CI](https://github.com/miludeshiji/novelm/actions/workflows/ci.yml/badge.svg)](https://github.com/miludeshiji/novelm/actions/workflows/ci.yml)
[![Latest Release](https://img.shields.io/github/v/release/miludeshiji/novelm?sort=semver)](https://github.com/miludeshiji/novelm/releases/latest)
![Platform](https://img.shields.io/badge/platform-Windows%20x64-0078D4)

> 面向轻书架的 Windows 漫画上传与发布管理工具。

NovelM 是连接轻书架服务的 Windows 桌面客户端，重点覆盖漫画创建、资料维护、分卷编辑和图片上传流程。应用需要连接轻书架；发布管理功能需要具有相应权限的轻书架账户。

## 功能

### 漫画上传与发布管理

- 搜索、创建、选择和删除本人漫画。
- 使用 HTTPS 地址或本地图片设置封面。
- 编辑标题、作者、简介、分类、标签、等级和下载设置。
- 创建、删除、重排分卷并修改分卷标题。
- 批量选择本地图片，查看上传进度和预览，并调整、删除或清空图片。
- 在香港与 Cloudflare API 节点之间切换。

### 辅助浏览

应用提供公开漫画目录、关键词搜索、排序和系列详情，便于查看已发布内容。当前不提供漫画阅读器。

## 下载与运行

前往 [GitHub Releases](https://github.com/miludeshiji/novelm/releases/latest) 下载最新版本：

- `NovelM-vX.Y.Z-win-x64-setup.exe`：推荐使用的安装程序。
- `NovelM-vX.Y.Z-win-x64.zip`：无需安装的框架依赖文件，请解压到当前用户有写权限的目录后运行 `NovelM.exe`。

### 系统要求

| 项目 | 要求 |
| --- | --- |
| 操作系统 | Windows 10 1809（build 17763）或更高版本 |
| 架构 | x64 |
| .NET | [.NET 10 Desktop Runtime x64](https://dotnet.microsoft.com/download/dotnet/10.0) |
| Windows App Runtime | [Windows App Runtime 2.4 x64](https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads) |
| Visual C++ | [Microsoft Visual C++ 2015–2022 Redistributable x64](https://aka.ms/vc14/vc_redist.x64.exe) |
| 服务 | 可用的轻书架 API、网络连接和轻书架账户 |

安装程序会检查上述运行库，但不会自动下载或安装缺失组件。框架依赖 ZIP 同样不包含这些运行库。

> [!WARNING]
> 当前安装程序和 ZIP 中的 `NovelM.exe` 均未进行代码签名，Windows SmartScreen 可能显示“未知发布者”。请仅从本仓库的 GitHub Releases 下载文件并确认来源。

## 基本使用

1. 安装所需运行库并启动 NovelM。
2. 打开“账户”，使用邮箱与密码登录。
3. 打开“发布管理”，创建新漫画或选择已有漫画。
4. 在“信息”和“设置”中维护封面、资料、分类和发布选项。
5. 在“分卷”中创建分卷、选择图片、检查顺序并上传。
6. 保存修改，并在执行删除漫画、删除分卷或清空图片前确认操作目标。

## 本地开发

### 技术栈

- .NET 10 / C#
- WinUI 3 / Windows App SDK 2.4
- CommunityToolkit.Mvvm
- ASP.NET Core SignalR Client / MessagePack
- MSTest

### 环境

- Windows x64
- [`.NET SDK 10.0.400`](https://dotnet.microsoft.com/download/dotnet/10.0)，版本选择遵循仓库中的 `global.json`

### 构建与测试

在仓库根目录使用 PowerShell：

```powershell
dotnet restore NovelM.sln --runtime win-x64 -p:Platform=x64
dotnet build NovelM.sln -c Release -p:Platform=x64 --no-restore
dotnet test NovelM.sln -c Release -p:Platform=x64 --no-build --no-restore
```

CI 在 Windows x64 环境执行 Release 构建与测试；位于 `main` 的提交打上严格格式的 `vX.Y.Z` 标签后，会触发 Windows 安装包和 ZIP 的 GitHub Release 构建。

## 项目结构

```text
novelm/
├── src/NovelM.App/       # WinUI 3 应用、领域模型和基础设施
├── tests/NovelM.Tests/   # MSTest 自动化测试
├── installer/            # Inno Setup 安装脚本与中文语言文件
├── .github/workflows/    # CI 与 Windows Release 工作流
└── docs/                 # 设计与实施记录
```

## 本地数据与诊断日志

应用“设置”页会显示当前数据目录。诊断日志保存在该目录的 `logs` 子目录，使用 JSON Lines 格式，并在达到大小限制后轮转为 `app.log`、`app.1.log` 和 `app.2.log`。

ZIP 版会在 `NovelM.exe` 所在目录创建 `data`，因此解压目录必须允许当前用户写入。

日志采用字段白名单和脱敏策略，不记录密码、Token、HTTP Header、请求或响应正文、漫画图片内容及章节正文。

## 当前限制

- 仅提供 Windows x64 构建。
- 不提供漫画阅读器或离线漫画缓存。
- 不提供自动更新。
- 当前发布文件尚未进行代码签名。
- 框架依赖 ZIP 不包含运行库。
- 功能依赖轻书架服务端 API 的可用性与账户权限。
