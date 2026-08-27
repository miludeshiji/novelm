using NovelM_App.Domain.Configuration;
using NovelM_App.Domain.Connection;
using NovelM_App.Presentation.Shell;

namespace NovelM.Tests.Presentation;

[TestClass]
public sealed class ShellViewModelTests
{
    [TestMethod]
    public void Navigation_ExposesExpectedTagsAndMangaDefault()
    {
        var viewModel = new ShellViewModel();

        CollectionAssert.AreEqual(
            new[] { "manga", "publishing", "settings" },
            viewModel.NavigationTags.ToArray());
        Assert.AreEqual("manga", viewModel.DefaultNavigationTag);
    }

    [TestMethod]
    [DataRow(ConnectionState.Disconnected, "未连接")]
    [DataRow(ConnectionState.Connecting, "连接中")]
    [DataRow(ConnectionState.Connected, "已连接")]
    [DataRow(ConnectionState.Reconnecting, "重连中")]
    [DataRow(ConnectionState.Failed, "失败")]
    public void Update_ExposesNodeAndLocalizedConnectionState(
        ConnectionState state,
        string expectedStateText)
    {
        var viewModel = new ShellViewModel();
        var server = new ApiServerOption(
            "hong-kong",
            "香港节点",
            new Uri("https://hk.example"));

        viewModel.Update(server, state);

        Assert.AreEqual("香港节点", viewModel.CurrentNodeDisplayName);
        Assert.AreEqual(expectedStateText, viewModel.ConnectionStatusText);
    }
}
