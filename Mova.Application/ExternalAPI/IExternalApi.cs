namespace Mova.Application.Interfaces.ExternalAPI;
public interface IExternalApiClient
{
    Task<T?> GetAsync<T>(
        string url,
        IDictionary<string, string>? headers = null,
        CancellationToken cancellationToken = default);

    Task<TResponse?> PostAsync<TRequest, TResponse>(
        string url,
        TRequest payload,
        IDictionary<string, string>? headers = null,
        CancellationToken cancellationToken = default);
}