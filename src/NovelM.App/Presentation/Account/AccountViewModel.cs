using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovelM_App.Application.Abstractions;
using NovelM_App.Domain.Auth;
using NovelM_App.Domain.Errors;
using NovelM_App.Presentation.Common;

namespace NovelM_App.Presentation.Account;

public partial class AccountViewModel : ObservableObject
{
    private readonly IAuthService _authService;
    private readonly ErrorMessageMapper _errorMessageMapper;

    [ObservableProperty]
    public partial string Email { get; set; }

    [ObservableProperty]
    public partial string Password { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    public partial UserProfile? CurrentUser { get; set; }

    public AccountViewModel(
        IAuthService authService,
        ErrorMessageMapper errorMessageMapper)
    {
        _authService = authService;
        _errorMessageMapper = errorMessageMapper;
        Email = string.Empty;
        Password = string.Empty;
    }

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public async Task RestoreAsync(CancellationToken cancellationToken)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        CurrentUser = null;
        try
        {
            CurrentUser = await _authService.RestoreAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            ErrorMessage = _errorMessageMapper.Map(exception);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task LoginAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        CurrentUser = null;
        try
        {
            var normalizedEmail = ValidateCredentials(Email, Password);
            CurrentUser = await _authService.LoginAsync(
                normalizedEmail,
                Password,
                CancellationToken.None);
        }
        catch (Exception exception)
        {
            ErrorMessage = _errorMessageMapper.Map(exception);
        }
        finally
        {
            Password = string.Empty;
            IsBusy = false;
        }
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task LogoutAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            await _authService.LogoutAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            ErrorMessage = _errorMessageMapper.Map(exception);
        }
        finally
        {
            CurrentUser = null;
            IsBusy = false;
        }
    }

    private static string ValidateCredentials(string email, string password)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new AppException(AppErrorKind.Validation, "请输入有效的邮箱地址。");
        }

        var normalizedEmail = email.Trim();
        var atIndex = normalizedEmail.IndexOf('@');
        var dotIndex = normalizedEmail.LastIndexOf('.');
        if (atIndex <= 0
            || atIndex != normalizedEmail.LastIndexOf('@')
            || atIndex == normalizedEmail.Length - 1
            || dotIndex <= atIndex + 1
            || dotIndex == normalizedEmail.Length - 1
            || normalizedEmail.Any(char.IsWhiteSpace))
        {
            throw new AppException(AppErrorKind.Validation, "请输入有效的邮箱地址。");
        }

        if (password is null || password.Length < 8)
        {
            throw new AppException(AppErrorKind.Validation, "密码至少需要 8 个字符。");
        }

        return normalizedEmail;
    }
}
