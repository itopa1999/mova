
using System.Net.Http;
using System.Text;
using System.Text.Json.Serialization;

namespace Mova.Infrastructure.Payment;

public sealed partial class MonnifyService
{
    private async Task<string?> AuthenticateAsync(
        CancellationToken cancellationToken)
    {
        var credentials =
            $"{_settings.ApiKey}:{_settings.SecretKey}";

        var authUrl =
            $"{_settings.BaseUrl.TrimEnd('/')}/api/v1/auth/login";

        var basicToken = Convert.ToBase64String(
            Encoding.UTF8.GetBytes(credentials));

        var headers = new Dictionary<string, string>
        {
            ["Authorization"] = $"Basic {basicToken}"
        };

        var response =
            await _externalApiClient.PostAsync<
                MonnifyAuthRequest,
                MonnifyAuthResponse>(
                    authUrl,
                    new MonnifyAuthRequest(),
                    headers,
                    cancellationToken);

        if (response is not
            {
                RequestSuccessful: true,
                ResponseBody.AccessToken.Length: > 0
            })
        {
            return null;
        }

        return response.ResponseBody.AccessToken;
    }

    private sealed class MonnifyAuthRequest
    {
    }

    private sealed class MonnifyAuthResponse
    {
        [JsonPropertyName("requestSuccessful")]
        public bool RequestSuccessful { get; set; }

        [JsonPropertyName("responseBody")]
        public MonnifyAuthResponseBody? ResponseBody { get; set; }
    }

    private sealed class MonnifyAuthResponseBody
    {
        [JsonPropertyName("accessToken")]
        public string AccessToken { get; set; } = string.Empty;

        [JsonPropertyName("expiresIn")]
        public int ExpiresIn { get; set; }
    }
}
