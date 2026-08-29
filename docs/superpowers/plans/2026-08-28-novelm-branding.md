# NovelM 品牌名称统一实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将当前生效的 Windows 应用、启动失败界面和安装器中的“轻书架”统一改为 `NovelM`。

**Architecture:** 直接替换四个活动品牌字符串位置，不引入品牌常量或本地化系统。通过源文件合约测试锁定窗口标题、启动失败标题、安装器名称和快捷方式名称，并以活动目录扫描防止旧名称回归。

**Tech Stack:** C# 14、.NET 10、WinUI 3 XAML、Inno Setup、MSTest。

---

## 文件结构

### 新建

- `tests/NovelM.Tests/Presentation/ProductBrandingTests.cs`：验证当前生效的应用和安装器品牌名称。

### 修改

- `src/NovelM.App/MainWindow.xaml`：窗口标题和左上角标题。
- `src/NovelM.App/App.xaml.cs`：启动失败备用窗口和错误对话框标题。
- `installer/NovelM.iss`：安装器产品名和开始菜单快捷方式。
- `tests/NovelM.Tests/NovelM.Tests.csproj`：将 `App.xaml.cs` 和 `NovelM.iss` 链接到测试输出。

### 保持不变

- `NovelM.exe`、程序集名称、安装目录、项目名、命名空间、AppId 和发布产物名称。
- `docs/superpowers/`、`.superpowers/` 等历史设计和 Web 参考材料。

---

### Task 1: 增加品牌名称合约测试并完成替换

**Files:**
- Create: `tests/NovelM.Tests/Presentation/ProductBrandingTests.cs`
- Modify: `tests/NovelM.Tests/NovelM.Tests.csproj`
- Modify: `src/NovelM.App/MainWindow.xaml`
- Modify: `src/NovelM.App/App.xaml.cs`
- Modify: `installer/NovelM.iss`

- [ ] **Step 1: 将活动品牌源文件链接到测试输出**

在 `tests/NovelM.Tests/NovelM.Tests.csproj` 的现有测试源 `Content` ItemGroup 中增加：

```xml
<Content Include="..\..\src\NovelM.App\App.xaml.cs"
         Link="TestSources\App.xaml.cs"
         CopyToOutputDirectory="PreserveNewest" />
<Content Include="..\..\installer\NovelM.iss"
         Link="TestSources\NovelM.iss"
         CopyToOutputDirectory="PreserveNewest" />
```

`MainWindow.xaml` 已由现有 AccountPage XAML 测试链接，无需重复添加。

- [ ] **Step 2: 写入失败的品牌合约测试**

创建 `tests/NovelM.Tests/Presentation/ProductBrandingTests.cs`：

```csharp
using System.Xml.Linq;

namespace NovelM.Tests.Presentation;

[TestClass]
public sealed class ProductBrandingTests
{
    private const string LegacyName = "轻书架";
    private static readonly XNamespace XamlNamespace =
        "http://schemas.microsoft.com/winfx/2006/xaml";

    [TestMethod]
    public void MainWindow_UsesNovelMForWindowAndTitleBar()
    {
        var document = XDocument.Load(TestSource("MainWindow.xaml"));
        Assert.AreEqual("NovelM", (string?)document.Root!.Attribute("Title"));

        var titleBar = document.Descendants()
            .Single(element =>
                (string?)element.Attribute(XamlNamespace + "Name")
                == "AppTitleBar");
        Assert.AreEqual("NovelM", (string?)titleBar.Attribute("Title"));
        Assert.IsFalse(document.ToString().Contains(
            LegacyName,
            StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task StartupFailureUi_UsesNovelMBranding()
    {
        var source = await File.ReadAllTextAsync(TestSource("App.xaml.cs"));

        StringAssert.Contains(source, "Title = \"NovelM\",");
        StringAssert.Contains(source, "Title = \"无法启动 NovelM\",");
        Assert.IsFalse(source.Contains(LegacyName, StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task Installer_UsesNovelMForProductAndShortcut()
    {
        var source = await File.ReadAllTextAsync(TestSource("NovelM.iss"));

        StringAssert.Contains(source, "AppName=NovelM");
        StringAssert.Contains(
            source,
            "Name: \"{autoprograms}\\NovelM\"; Filename: \"{app}\\NovelM.exe\"");
        Assert.IsFalse(source.Contains(LegacyName, StringComparison.Ordinal));
    }

    private static string TestSource(string fileName)
    {
        return Path.Combine(
            AppContext.BaseDirectory,
            "TestSources",
            fileName);
    }
}
```

- [ ] **Step 3: 运行品牌测试并确认旧名称导致失败**

Run:

```powershell
dotnet test tests\NovelM.Tests\NovelM.Tests.csproj `
  -p:Platform=x64 `
  --filter "FullyQualifiedName~ProductBrandingTests"
```

在 WSL 中执行时使用：

```bash
powershell.exe -NoProfile -Command "Set-Location 'F:\workspace\lightnovel\novelm'; dotnet test tests\NovelM.Tests\NovelM.Tests.csproj -p:Platform=x64 --filter 'FullyQualifiedName~ProductBrandingTests'"
```

Expected: FAIL，断言当前 `轻书架` 不等于 `NovelM`。

- [ ] **Step 4: 替换主窗口品牌名称**

在 `src/NovelM.App/MainWindow.xaml` 精确替换：

```xml
<Window
    ...
    Title="NovelM"
    ...>
```

```xml
<TitleBar
    x:Name="AppTitleBar"
    Title="NovelM"
    ... />
```

不修改 `OpenPaneLength="200"` 或其他导航属性。

- [ ] **Step 5: 替换启动失败界面品牌名称**

在 `src/NovelM.App/App.xaml.cs` 的 `ShowStartupFailureAsync` 中替换为：

```csharp
_window = new Window
{
    Title = "NovelM",
    Content = root
};
```

```csharp
var dialog = new ContentDialog
{
    XamlRoot = root.XamlRoot,
    Title = "无法启动 NovelM",
    Content = $"启动阶段：{startupStage}\n应用数据目录：\n{dataDirectory}",
    CloseButtonText = "关闭"
};
```

- [ ] **Step 6: 替换安装器产品名和快捷方式名称**

在 `installer/NovelM.iss` 精确替换：

```ini
AppName=NovelM
```

```ini
Name: "{autoprograms}\NovelM"; Filename: "{app}\NovelM.exe"
```

保留 `AppId`、`DefaultDirName`、`OutputBaseFilename` 和 `NovelM.exe` 不变。

- [ ] **Step 7: 运行品牌合约测试**

Run:

```bash
powershell.exe -NoProfile -Command "Set-Location 'F:\workspace\lightnovel\novelm'; dotnet test tests\NovelM.Tests\NovelM.Tests.csproj -p:Platform=x64 --filter 'FullyQualifiedName~ProductBrandingTests|FullyQualifiedName~AccountPageXamlTests'"
```

Expected: PASS，5 个品牌/XAML 合约测试全部通过。

- [ ] **Step 8: 扫描当前生效目录中的旧名称**

Run:

```bash
grep -RIn --exclude-dir=bin --exclude-dir=obj "轻书架" src/NovelM.App installer
```

Expected: 无输出，exit code `1` 表示没有匹配项。

- [ ] **Step 9: 提交品牌名称修改**

```bash
git add installer/NovelM.iss src/NovelM.App/MainWindow.xaml src/NovelM.App/App.xaml.cs tests/NovelM.Tests/NovelM.Tests.csproj tests/NovelM.Tests/Presentation/ProductBrandingTests.cs
git diff --cached --check
git commit -m "feat: 统一应用品牌名称为 NovelM"
```

---

### Task 2: 完整回归验证

**Files:**
- Verify only. 若验证失败，返回 Task 1 修复对应实现或测试并重新提交。

- [ ] **Step 1: 运行完整 Release 构建**

Run:

```bash
powershell.exe -NoProfile -Command "Set-Location 'F:\workspace\lightnovel\novelm'; dotnet build NovelM.sln -c Release -p:Platform=x64 --disable-build-servers -m:1 -nr:false"
```

Expected: Build succeeded，0 errors。允许项目现有的 WinAppSDK PRI 本地化警告。

- [ ] **Step 2: 运行完整测试套件**

Run:

```bash
powershell.exe -NoProfile -Command "Set-Location 'F:\workspace\lightnovel\novelm'; dotnet test NovelM.sln -c Release -p:Platform=x64 --no-build --disable-build-servers -m:1 -nr:false"
```

Expected: 全部测试通过，0 failed。

- [ ] **Step 3: 检查最终差异和工作区**

Run:

```bash
git status --short
git show --check --stat --oneline HEAD
```

Expected: 工作区无未提交文件；最新提交仅包含本计划列出的 5 个文件；没有 whitespace error 或无意义行尾替换。
