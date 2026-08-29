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
        Assert.AreEqual(
            "Peek",
            (string?)tokenInput.Attribute("PasswordRevealMode"));
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

        Assert.AreEqual(
            "200",
            (string?)navigation.Attribute("OpenPaneLength"));
        Assert.AreEqual(
            "Auto",
            (string?)navigation.Attribute("PaneDisplayMode"));
    }

    private static XDocument ReadXaml(string fileName)
    {
        return XDocument.Load(Path.Combine(
            AppContext.BaseDirectory,
            "TestSources",
            fileName));
    }

    private static XElement FindNamedElement(
        XDocument document,
        string name)
    {
        return document.Descendants()
            .Single(element =>
                (string?)element.Attribute(XamlNamespace + "Name") == name);
    }
}
