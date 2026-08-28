namespace NovelM_App.Application.Abstractions;

public interface IDeviceIdStore
{
    Task<string> GetOrCreateAsync(CancellationToken cancellationToken);

    Task SetAsync(string deviceId, CancellationToken cancellationToken);
}
