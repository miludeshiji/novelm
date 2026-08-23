using CommunityToolkit.Mvvm.ComponentModel;
using NovelM_App.Domain.Configuration;
using NovelM_App.Domain.Connection;

namespace NovelM_App.Presentation.Shell;

public partial class ShellViewModel : ObservableObject
{
    [ObservableProperty]
    public partial string CurrentNodeDisplayName { get; set; }

    [ObservableProperty]
    public partial string ConnectionStatusText { get; set; }

    public ShellViewModel()
    {
        CurrentNodeDisplayName = string.Empty;
        ConnectionStatusText = "未连接";
    }

    public void Update(ApiServerOption server, ConnectionState state)
    {
        CurrentNodeDisplayName = server.DisplayName;
        ConnectionStatusText = state switch
        {
            ConnectionState.Disconnected => "未连接",
            ConnectionState.Connecting => "连接中",
            ConnectionState.Connected => "已连接",
            ConnectionState.Reconnecting => "重连中",
            ConnectionState.Failed => "失败",
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, null)
        };
    }
}
