using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NovelM_App.Application.Abstractions;
using NovelM_App.Domain.Configuration;
using NovelM_App.Domain.Connection;
using NovelM_App.Presentation.Account;
using NovelM_App.Presentation.Manga;
using NovelM_App.Presentation.Publishing;
using NovelM_App.Presentation.Settings;
using NovelM_App.Presentation.Shell;

namespace NovelM_App;

public sealed partial class MainWindow : Window
{
    private readonly IApiServerManager _serverManager;
    private readonly ISignalRConnection _signalRConnection;
    private MangaPage? _mangaPage;
    private bool _isNavigationInProgress;
    private bool _isNavigationSelectionSuppressed;

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
            NavView.SelectedItem = MangaItem;
            if (ContentHost.Content is null)
            {
                ShowPage(ViewModel.DefaultNavigationTag);
            }
        }
    }

    private void TitleBar_PaneToggleRequested(TitleBar sender, object args)
    {
        NavView.IsPaneOpen = !NavView.IsPaneOpen;
    }

    private async void NavView_SelectionChanged(
        NavigationView sender,
        NavigationViewSelectionChangedEventArgs args)
    {
        if (_isNavigationSelectionSuppressed)
        {
            return;
        }

        if (args.SelectedItem is not NavigationViewItem { Tag: string tag } requestedItem)
        {
            return;
        }

        if (_isNavigationInProgress)
        {
            RestorePublishingSelection();
            return;
        }

        if (ContentHost.Content is not PublishingPage publishingPage
            || tag == "publishing")
        {
            ShowPage(tag);
            return;
        }

        _isNavigationInProgress = true;
        try
        {
            if (!await publishingPage.ConfirmNavigationAwayAsync())
            {
                RestorePublishingSelection();
                return;
            }

            SetNavigationSelection(requestedItem);
            ShowPage(tag);
        }
        finally
        {
            _isNavigationInProgress = false;
        }
    }

    private void RestorePublishingSelection()
    {
        SetNavigationSelection(PublishingItem);
    }

    private void SetNavigationSelection(NavigationViewItem item)
    {
        _isNavigationSelectionSuppressed = true;
        try
        {
            NavView.SelectedItem = item;
        }
        finally
        {
            _isNavigationSelectionSuppressed = false;
        }
    }

    private void ShowPage(string tag)
    {
        ReleaseCurrentPublishingPage();

        ContentHost.Content = tag switch
        {
            "manga" => GetOrCreateMangaPage(),
            "publishing" => CreatePublishingPage(),
            "settings" => App.Services.GetRequiredService<SettingsPage>(),
            "account" => App.Services.GetRequiredService<AccountPage>(),
            _ => throw new InvalidOperationException(
                $"Unknown navigation item tag: {tag}")
        };
    }

    private MangaPage GetOrCreateMangaPage()
    {
        return _mangaPage ??= App.Services.GetRequiredService<MangaPage>();
    }

    private PublishingPage CreatePublishingPage()
    {
        var page = App.Services.GetRequiredService<PublishingPage>();
        page.AccountNavigationRequested += PublishingPage_AccountNavigationRequested;
        return page;
    }

    private void ReleaseCurrentPublishingPage()
    {
        if (ContentHost.Content is PublishingPage page)
        {
            page.AccountNavigationRequested -= PublishingPage_AccountNavigationRequested;
        }
    }

    private void PublishingPage_AccountNavigationRequested(
        object? sender,
        EventArgs args)
    {
        SetNavigationSelection(AccountItem);

        if (ContentHost.Content is not AccountPage)
        {
            ShowPage("account");
        }
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
        ReleaseCurrentPublishingPage();
        ContentHost.Content = null;
        _mangaPage = null;
        _serverManager.CurrentChanged -= ServerManager_CurrentChanged;
        _signalRConnection.StateChanged -= SignalRConnection_StateChanged;
        Closed -= MainWindow_Closed;
    }
}
