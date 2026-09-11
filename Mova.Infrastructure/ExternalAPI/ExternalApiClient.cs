using System.Net.Http.Json;
using Mova.Application.Interfaces.ExternalAPI;

namespace Mova.Infrastructure.ExternalAPI;

public sealed class ExternalApiClient(
    HttpClient httpClient) : IExternalApiClient
{
    private readonly HttpClient _httpClient = httpClient;

    public async Task<T?> GetAsync<T>(
        string url,
        IDictionary<string, string>? headers = null,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            url);

        if (headers is not null)
        {
            foreach (var header in headers)
            {
                request.Headers.TryAddWithoutValidation(
                    header.Key,
                    header.Value);
            }
        }

        var response = await _httpClient.SendAsync(
            request,
            cancellationToken);

        var json = await response.Content.ReadAsStringAsync(
            cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<T>(
            cancellationToken);
    }

    public async Task<TResponse?> PostAsync<TRequest, TResponse>(
        string url,
        TRequest payload,
        IDictionary<string, string>? headers = null,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            url)
        {
            Content = JsonContent.Create(payload)
        };

        if (headers is not null)
        {
            foreach (var header in headers)
            {
                request.Headers.TryAddWithoutValidation(
                    header.Key,
                    header.Value);
            }
        }

        var response = await _httpClient.SendAsync(
            request,
            cancellationToken);

        var json = await response.Content.ReadAsStringAsync(
            cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<TResponse>(
            cancellationToken);
    }
}