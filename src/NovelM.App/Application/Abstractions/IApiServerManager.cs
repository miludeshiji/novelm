using NovelM_App.Domain.Configuration;

namespace NovelM_App.Application.Abstractions;

public interface IApiServerManager
{
    ApiServerOption Current { get; }

    IReadOnlyList<ApiServerOption> Options { get; }

    event EventHandler<ApiServerOption>? CurrentChanged;

    Task LoadAsync(CancellationToken cancellationToken);

    Task SelectAsync(string serverId, CancellationToken cancellationToken);
}
