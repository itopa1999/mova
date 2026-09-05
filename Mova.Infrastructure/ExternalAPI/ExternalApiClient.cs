using System.Net.Http.Json;
using Mova.Application.Interfaces.ExternalAPI;

public sealed class ExternalApiClient : IExternalApiClient
{
    private readonly HttpClient _httpClient;

    public ExternalApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<T?> GetAsync<T>(
        string url,
        CancellationToken cancellationToken = default)
    {
        return await _httpClient.GetFromJsonAsync<T>(
            url,
            cancellationToken);
    }
}