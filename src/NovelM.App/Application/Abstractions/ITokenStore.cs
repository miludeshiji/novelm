namespace NovelM_App.Application.Abstractions;

public interface ITokenStore
{
    Task<string?> ReadAsync(CancellationToken cancellationToken);

    Task SaveAsync(string refreshToken, CancellationToken cancellationToken);

    Task DeleteAsync(CancellationToken cancellationToken);
}
