using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovelM_App.Application.Abstractions;
using NovelM_App.Domain.Configuration;
using NovelM_App.Presentation.Common;

namespace NovelM_App.Presentation.Settings;

public partial class SettingsViewModel : ObservableObject
{
    private readonly IApiServerManager _serverManager;
    private readonly IAuthSession _authSession;
    private readonly ISignalRConnection _signalRConnection;
    private readonly ErrorMessageMapper _errorMessageMapper;

    [ObservableProperty]
    public partial ApiServerOption? SelectedServer { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? ErrorMessage { get; set; }

    public SettingsViewModel(
        IApiServerManager serverManager,
        IAuthSession authSession,
        ISignalRConnection signalRConnection,
        ErrorMessageMapper errorMessageMapper,
        string dataDirectory)
    {
        _serverManager = serverManager;
        _authSession = authSession;
        _signalRConnection = signalRConnection;
        _errorMessageMapper = errorMessageMapper;
        Options = serverManager.Options;
        SelectedServer = serverManager.Current;
        DataDirectory = dataDirectory;
    }

    public IReadOnlyList<ApiServerOption> Options { get; }

    public string DataDirectory { get; }

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task SelectServerAsync(ApiServerOption? server)
    {
        if (IsBusy || server is null)
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        SelectedServer = server;
        try
        {
            await _serverManager.SelectAsync(server.Id, CancellationToken.None);
            _authSession.InvalidateSessionToken();
            await _signalRConnection.RestartAsync(CancellationToken.None);
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
}
