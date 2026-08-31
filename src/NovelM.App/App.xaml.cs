using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NovelM_App.Application.Abstractions;
using NovelM_App.Application.Auth;
using NovelM_App.Application.Books;
using NovelM_App.Application.Manga;
using NovelM_App.Application.Publishing;
using NovelM_App.Domain.Errors;
using NovelM_App.Infrastructure.Configuration;
using NovelM_App.Infrastructure.Http;
using NovelM_App.Infrastructure.Logging;
using NovelM_App.Infrastructure.SignalR;
using NovelM_App.Infrastructure.Storage;
using NovelM_App.Presentation.Account;
using NovelM_App.Presentation.Common;
using NovelM_App.Presentation.Manga;
using NovelM_App.Presentation.Publishing;
using NovelM_App.Presentation.Settings;
using NovelM_App.Presentation.Shell;

namespace NovelM_App;

public partial class App : Microsoft.UI.Xaml.Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
        Services = BuildServices();
        UnhandledException += OnUnhandledException;
    }

    public static IServiceProvider Services { get; private set; } = null!;

    internal static MainWindow MainWindow { get; private set; } = null!;

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        var paths = Services.GetRequiredService<AppPaths>();
        var diagnosticLog = Services.GetRequiredService<IDiagnosticLog>();
        var startupStage = "准备数据目录";

        try
        {
            await paths.EnsureWritableAsync(CancellationToken.None);
            startupStage = "创建设备标识";
            await Services
                .GetRequiredService<IDeviceIdStore>()
                .GetOrCreateAsync(CancellationToken.None);
            startupStage = "加载节点设置";
            await Services
                .GetRequiredService<IApiServerManager>()
                .LoadAsync(CancellationToken.None);

            startupStage = "创建主窗口";
            MainWindow = Services.GetRequiredService<MainWindow>();
            _window = MainWindow;
            startupStage = "激活主窗口";
            _window.Activate();

            _ = Services
                .GetRequiredService<AccountViewModel>()
                .RestoreAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            await diagnosticLog.TryWriteAsync(
                "application.startup.failed",
                new Dictionary<string, object?>
                {
                    ["operation"] = "Startup",
                    ["stage"] = startupStage,
                    ["errorKind"] = ErrorKind(exception)
                },
                exception,
                CancellationToken.None);
            await ShowStartupFailureAsync(
                paths.DataDirectory,
                startupStage);
        }
    }

    private static void OnUnhandledException(
        object sender,
        Microsoft.UI.Xaml.UnhandledExceptionEventArgs args)
    {
        Task.Run(() => Services.GetRequiredService<IDiagnosticLog>()
                .TryWriteAsync(
                    "application.unhandled",
                    new Dictionary<string, object?>
                    {
                        ["operation"] = "Application",
                        ["stage"] = "unhandled",
                        ["errorKind"] = ErrorKind(args.Exception)
                    },
                    args.Exception,
                    CancellationToken.None))
            .GetAwaiter()
            .GetResult();
    }

    private static string ErrorKind(Exception exception)
    {
        return exception is AppException appException
            ? appException.Kind.ToString()
            : exception.GetType().Name;
    }

    private static IServiceProvider BuildServices()
    {
        var services = new ServiceCollection();
        var paths = AppPaths.ForRuntime();

        services.AddSingleton(paths);
        services.AddSingleton<IDiagnosticLog>(provider =>
            new RedactedFileLog(provider.GetRequiredService<AppPaths>()));
        services.AddSingleton<IDeviceIdStore>(provider =>
            new DeviceIdStore(provider.GetRequiredService<AppPaths>()));
        services.AddSingleton<IApiServerManager>(provider =>
        {
            var appPaths = provider.GetRequiredService<AppPaths>();
#if DEBUG
            return new ApiServerManager(appPaths, includeLocalhost: true);
#else
            return new ApiServerManager(appPaths, includeLocalhost: false);
#endif
        });

        services.AddSingleton(new HttpClient());
        services.AddSingleton<ApiHttpClient>();
        services.AddSingleton<IAuthApi, AuthApi>();
        services.AddSingleton<ITokenStore, DpapiTokenStore>();
        services.AddSingleton<IAuthSession, AuthSession>();
        services.AddSingleton<CompressedResponseDecoder>();
        services.AddSingleton<SignalRRetryPolicy>();
        services.AddSingleton<ISignalRConnection, SignalRConnection>();
        services.AddSingleton<IUserApi, SignalRUserApi>();
        services.AddSingleton<IBookApi, SignalRBookApi>();
        services.AddSingleton<IMangaApi, SignalRMangaApi>();
        services.AddSingleton<IComicPublishingApi, SignalRComicPublishingApi>();
        services.AddSingleton<ILocalImageReader, LocalImageReader>();
        services.AddSingleton<IAuthService, AuthService>();
        services.AddSingleton<IBookService, BookService>();
        services.AddSingleton<IMangaService, MangaService>();
        services.AddSingleton<IComicPublishingService, ComicPublishingService>();

        services.AddSingleton<ErrorMessageMapper>();
        services.AddSingleton<ShellViewModel>();
        services.AddSingleton<AccountViewModel>();
        services.AddTransient<MangaViewModel>();
        services.AddTransient<ComicEditorViewModel>();
        services.AddTransient<PublishingViewModel>();
        services.AddSingleton(provider => new SettingsViewModel(
            provider.GetRequiredService<IApiServerManager>(),
            provider.GetRequiredService<IAuthSession>(),
            provider.GetRequiredService<ISignalRConnection>(),
            provider.GetRequiredService<ErrorMessageMapper>(),
            provider.GetRequiredService<AppPaths>().DataDirectory));

        services.AddTransient<AccountPage>();
        services.AddTransient<MangaPage>();
        services.AddTransient<PublishingPage>();
        services.AddTransient<SettingsPage>();
        services.AddSingleton<MainWindow>();

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
    }

    private async Task ShowStartupFailureAsync(
        string dataDirectory,
        string startupStage)
    {
        var root = new Grid();
        var loaded = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        root.Loaded += (_, _) => loaded.TrySetResult();

        _window = new Window
        {
            Title = "NovelM",
            Content = root
        };
        _window.Activate();
        await loaded.Task;

        var dialog = new ContentDialog
        {
            XamlRoot = root.XamlRoot,
            Title = "无法启动 NovelM",
            Content = $"启动阶段：{startupStage}\n应用数据目录：\n{dataDirectory}",
            CloseButtonText = "关闭"
        };
        await dialog.ShowAsync();
        _window.Close();
    }
}
