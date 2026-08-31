# NovelM README 设计

## 1. 目标

为仓库根目录新增符合 GitHub 阅读习惯的中文 `README.md`，同时服务普通用户与贡献者。README 将 NovelM 定位为轻书架漫画上传管理工具，只描述仓库当前已实现的能力。

## 2. 产品定位与范围

README 首屏使用以下定位：

> NovelM 是面向轻书架的 Windows 漫画上传与发布管理工具。

核心内容聚焦：

- 登录轻书架账户；
- 创建、删除和选择本人漫画；
- 上传封面并编辑漫画基础信息与高级设置；
- 创建、删除、排序和编辑分卷；
- 批量上传、预览、删除、清空和排序分卷图片；
- 切换轻书架 API 节点。

公开漫画目录、搜索、排序和系列详情作为辅助检查能力简要提及，不与上传管理并列为产品定位。

README 不将 NovelM 描述为完整轻书架桌面客户端或漫画阅读器，也不公开未经确认的路线图。

## 3. 信息架构

README 按以下顺序组织：

1. `NovelM` 标题、产品定位和状态徽章；
2. 项目简介；
3. 核心功能；
4. 下载与系统要求；
5. 基本使用流程；
6. 本地开发、构建与测试；
7. 项目目录结构；
8. 本地数据与诊断日志；
9. 当前限制。

首屏优先回答“项目是什么、能做什么、在哪里下载”。不增加手写目录，使用 GitHub 自动生成的标题锚点。

## 4. GitHub 展示规则

- 正文使用中文，框架名、运行库名、命令和路径保留英文。
- 使用 GitHub Flavored Markdown。
- 顶部展示动态 GitHub Release 徽章、现有 CI 工作流徽章和 Windows x64 平台徽章。
- Release 徽章和下载入口指向 `https://github.com/miludeshiji/novelm/releases`。
- CI 徽章和链接对应 `.github/workflows/ci.yml`。
- 不硬编码当前版本号，避免版本发布后文档失效。
- 仓库没有许可证文件，因此不展示许可证徽章或许可声明。
- 仓库没有稳定截图素材，因此不增加截图占位或外部图片。
- 系统要求使用表格；功能、使用流程和限制使用短列表；命令使用带语言标识的代码块。

## 5. 下载与运行信息

README 应准确说明：

- 仅支持 Windows x64；最低目标版本为 Windows 10 1809；
- GitHub Release 提供安装程序和框架依赖 ZIP；
- 运行依赖为 .NET 10 Desktop Runtime、Windows App Runtime 2.4 和 Microsoft Visual C++ 2015–2022 Redistributable x64；
- 安装程序会检查依赖但不会自动下载；
- 当前安装包未进行代码签名，Windows SmartScreen 可能显示“未知发布者”；
- 应用需要网络连接、可用的轻书架 API 和相应账户权限。

## 6. 使用说明

基本流程保持简短且对应现有界面：

1. 安装运行依赖并从 GitHub Releases 下载 NovelM；
2. 使用账号密码或 RefreshToken 与 `x-id` 登录；
3. 打开“发布管理”，创建或选择漫画；
4. 编辑信息和设置；
5. 创建分卷，选择本地图片并调整顺序；
6. 上传并保存修改。

README 应提醒用户，删除漫画、删除分卷和清空图片等操作具有破坏性，需要确认后执行。

## 7. 开发者信息

开发环境和命令以仓库配置及 CI 为准：

- Windows x64；
- `.NET SDK 10.0.400`，遵循 `global.json` 的 roll-forward 设置；
- WinUI 3 / Windows App SDK 2.4；
- CommunityToolkit.Mvvm；
- SignalR Client 与 MessagePack；
- MSTest。

README 提供可复制的 PowerShell 命令，覆盖还原、Release 构建和测试，并使用 `NovelM.sln`、`win-x64` 与 `Platform=x64`。目录结构只解释 `src/NovelM.App`、`tests/NovelM.Tests`、`installer`、`.github/workflows` 和 `docs`。

## 8. 数据、日志与隐私

README 说明数据目录可在应用“设置”页查看。诊断日志位于该数据目录下的 `logs` 子目录，采用 JSON Lines、脱敏和轮转策略。日志不记录 Token、密码、请求正文、漫画图片内容或章节正文。

不在 README 中猜测固定的绝对数据目录，避免与实际运行位置不一致。

## 9. 当前限制

明确列出：

- 仅提供 Windows x64 构建；
- 不提供漫画阅读器；
- 不支持离线漫画缓存、自动更新或安装包代码签名；
- 框架依赖 ZIP 不包含所需运行库；
- 功能依赖轻书架服务端 API。

## 10. 验证

完成 README 后执行其中记录的还原、Release 构建和测试命令，确认命令可以直接使用。逐项检查：

- GitHub 徽章与 Release、Actions 链接对应当前仓库；
- README 引用的本地路径实际存在；
- 系统要求与 `NovelM.App.csproj`、`global.json` 和安装脚本一致；
- 功能描述与当前导航、发布管理、账户和设置界面一致；
- 没有占位文本、未实现功能、虚假许可证或版本承诺。

## 11. 验收标准

- 仓库根目录存在中文 `README.md`；
- 首屏明确 NovelM 是轻书架漫画上传管理工具；
- 普通用户能找到下载入口、依赖要求、SmartScreen 提示和基本操作流程；
- 贡献者能找到技术栈、构建测试命令和主要目录说明；
- 公开漫画浏览只作为辅助能力出现；
- 当前限制清楚且不包含路线图；
- README 中的命令完成实际验证。
