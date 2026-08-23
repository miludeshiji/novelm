using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NovelM_App.Application.Abstractions;
using NovelM_App.Domain.Configuration;
using NovelM_App.Domain.Connection;
using NovelM_App.Presentation.Account;
using NovelM_App.Presentation.BookProbe;
using NovelM_App.Presentation.Settings;
using NovelM_App.Presentation.Shell;

namespace NovelM_App;

public sealed partial class MainWindow : Window
{
    private readonly IApiServerManager _serverManager;
    private readonly ISignalRConnection _signalRConnection;

    public MainWindow(
        ShellViewModel viewModel,
        IApiServerManager serverManager,
        ISignalRConnection signalRConnection)
    {
        ViewModel = viewModel;
        _serverManager = serverManager;
        _signalRConnection = signalRConnection;

        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        AppWindow.SetIcon(Path.Combine(
            AppContext.BaseDirectory,
            "Assets",
            "AppIcon.ico"));

        _serverManager.CurrentChanged += ServerManager_CurrentChanged;
        _signalRConnection.StateChanged += SignalRConnection_StateChanged;
        Closed += MainWindow_Closed;
        UpdateShell();
    }

    public ShellViewModel ViewModel { get; }

    private void RootGrid_Loaded(object sender, RoutedEventArgs args)
    {
        if (ContentHost.Content is null)
        {
            NavView.SelectedItem = BookItem;
            ShowPage("book");
        }
    }

    private void TitleBar_PaneToggleRequested(TitleBar sender, object args)
    {
        NavView.IsPaneOpen = !NavView.IsPaneOpen;
    }

    private void NavView_SelectionChanged(
        NavigationView sender,
        NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem { Tag: string tag })
        {
            ShowPage(tag);
        }
    }

    private void ShowPage(string tag)
    {
        ContentHost.Content = tag switch
        {
            "book" => App.Services.GetRequiredService<BookProbePage>(),
            "settings" => App.Services.GetRequiredService<SettingsPage>(),
            "account" => App.Services.GetRequiredService<AccountPage>(),
            _ => throw new InvalidOperationException(
                $"Unknown navigation item tag: {tag}")
        };
    }

    private void ServerManager_CurrentChanged(
        object? sender,
        ApiServerOption option)
    {
        QueueShellUpdate();
    }

    private void SignalRConnection_StateChanged(
        object? sender,
        ConnectionState state)
    {
        QueueShellUpdate();
    }

    private void QueueShellUpdate()
    {
        DispatcherQueue.TryEnqueue(UpdateShell);
    }

    private void UpdateShell()
    {
        ViewModel.Update(_serverManager.Current, _signalRConnection.State);
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        _serverManager.CurrentChanged -= ServerManager_CurrentChanged;
        _signalRConnection.StateChanged -= SignalRConnection_StateChanged;
        Closed -= MainWindow_Closed;
    }
}
