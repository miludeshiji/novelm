using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace NovelM_App.Presentation.Account;

public sealed partial class AccountPage : Page
{
    public AccountPage(AccountViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        Loaded += AccountPage_Loaded;
        Unloaded += AccountPage_Unloaded;
    }

    public AccountViewModel ViewModel { get; }

    private void AccountPage_Loaded(object sender, RoutedEventArgs args)
    {
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        UpdatePassword();
        UpdateAvatar();
    }

    private void AccountPage_Unloaded(object sender, RoutedEventArgs args)
    {
        ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
        PasswordInput.Password = string.Empty;
        ViewModel.Password = string.Empty;
    }

    private void PasswordInput_PasswordChanged(
        object sender,
        RoutedEventArgs args)
    {
        if (!string.Equals(
            ViewModel.Password,
            PasswordInput.Password,
            StringComparison.Ordinal))
        {
            ViewModel.Password = PasswordInput.Password;
        }
    }

    private void ViewModel_PropertyChanged(
        object? sender,
        PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(AccountViewModel.Password))
        {
            UpdatePassword();
        }
        else if (args.PropertyName == nameof(AccountViewModel.CurrentUser))
        {
            UpdateAvatar();
        }
    }

    private void UpdatePassword()
    {
        if (string.IsNullOrEmpty(ViewModel.Password)
            && PasswordInput.Password.Length != 0)
        {
            PasswordInput.Password = string.Empty;
        }
    }

    private void UpdateAvatar()
    {
        var avatar = ViewModel.CurrentUser?.Avatar;
        if (Uri.TryCreate(avatar, UriKind.Absolute, out var uri)
            && uri.Scheme is "http" or "https")
        {
            AvatarImage.Source = new BitmapImage(uri);
            return;
        }

        AvatarImage.Source = null;
    }
}
