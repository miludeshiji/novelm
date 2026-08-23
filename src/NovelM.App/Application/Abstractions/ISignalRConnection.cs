using NovelM_App.Domain.Connection;

namespace NovelM_App.Application.Abstractions;

public interface ISignalRConnection
{
    ConnectionState State { get; }

    event EventHandler<ConnectionState>? StateChanged;

    Task StartAsync(CancellationToken cancellationToken);

    Task StopAsync(CancellationToken cancellationToken);

    Task RestartAsync(CancellationToken cancellationToken);

    Task<T> InvokeAsync<T>(
        string methodName,
        object? request,
        CancellationToken cancellationToken);
}
