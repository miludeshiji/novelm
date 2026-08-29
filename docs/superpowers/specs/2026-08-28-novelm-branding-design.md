# NovelM 品牌名称统一设计

## 1. 目标

将当前生效的 Windows 应用和安装器中所有用户可见的“轻书架”统一改为大小写准确的 `NovelM`。

## 2. 修改范围

### 2.1 安装器

修改 `installer/NovelM.iss`：

- `AppName=NovelM`
- 开始菜单快捷方式名称改为 `{autoprograms}\NovelM`

Inno Setup 会据此将 Windows“已安装的应用”及卸载器显示名更新为 `NovelM`。

### 2.2 主窗口

修改 `src/NovelM.App/MainWindow.xaml`：

- `Window.Title` 改为 `NovelM`
- 左上角 `TitleBar.Title` 改为 `NovelM`

### 2.3 启动失败界面

修改 `src/NovelM.App/App.xaml.cs`：

- 启动失败备用窗口标题改为 `NovelM`
- 错误对话框标题改为“无法启动 NovelM”

## 3. 保持不变

以下内容已经使用正确名称，不需要修改：

- `NovelM.exe`
- 程序集名称 `NovelM`
- 安装目录 `{localappdata}\Programs\NovelM`
- 安装包及 GitHub Release 产物名称
- 项目文件名、命名空间和 Inno Setup AppId

历史规格、脑暴文件和 Web 架构参考文档保留原文，不参与品牌替换。

## 4. 实现方式

采用针对当前生效位置的直接替换，不增加品牌常量或本地化资源系统。当前名称只有少量固定位置，直接替换具有最小改动面，也不会为静态品牌名引入额外抽象。

## 5. 测试

增加或扩展源文件合约测试，验证：

1. `MainWindow.xaml` 的窗口标题和左上角标题均为 `NovelM`。
2. `App.xaml.cs` 的备用窗口标题为 `NovelM`，错误对话框标题为“无法启动 NovelM”。
3. `installer/NovelM.iss` 的 `AppName` 和开始菜单快捷方式均为 `NovelM`。
4. `src/NovelM.App/` 与 `installer/` 中不再出现“轻书架”。
5. 完整 Release 构建与测试通过。

## 6. 验收标准

- 安装后，Windows 已安装应用中的名称为 `NovelM`。
- 开始菜单快捷方式名称为 `NovelM`。
- 应用窗口标题及界面左上角标题为 `NovelM`。
- 启动失败界面不再显示旧品牌名。
- 可执行文件、安装目录、程序集和发布产物继续使用现有 `NovelM` 命名。
- 当前生效的源码和安装脚本中不存在“轻书架”。
