namespace Mova.Application.Interfaces.ExternalAPI;
public interface IExternalApiClient
{
    Task<T?> GetAsync<T>(
        string url,
        CancellationToken cancellationToken = default);
}