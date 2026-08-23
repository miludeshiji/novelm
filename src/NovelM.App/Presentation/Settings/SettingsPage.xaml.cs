using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NovelM_App.Domain.Configuration;

namespace NovelM_App.Presentation.Settings;

public sealed partial class SettingsPage : Page
{
    private bool _isLoaded;

    public SettingsPage(SettingsViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        Loaded += SettingsPage_Loaded;
        Unloaded += SettingsPage_Unloaded;
    }

    public SettingsViewModel ViewModel { get; }

    private void SettingsPage_Loaded(object sender, RoutedEventArgs args)
    {
        _isLoaded = true;
    }

    private void SettingsPage_Unloaded(object sender, RoutedEventArgs args)
    {
        _isLoaded = false;
    }

    private void ServerOptions_SelectionChanged(
        object sender,
        SelectionChangedEventArgs args)
    {
        if (_isLoaded
            && ServerOptions.SelectedItem is ApiServerOption server
            && ViewModel.SelectServerCommand.CanExecute(server))
        {
            ViewModel.SelectServerCommand.Execute(server);
        }
    }
}
