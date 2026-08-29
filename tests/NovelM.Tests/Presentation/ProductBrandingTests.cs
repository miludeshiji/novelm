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
