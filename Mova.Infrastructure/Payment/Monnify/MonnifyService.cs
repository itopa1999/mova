using Microsoft.Extensions.Options;
using Mova.Application.Interfaces.ExternalAPI;
using Mova.Application.Interfaces.Payment;
using Mova.Infrastructure.ExternalAPI;
using Mova.Infrastructure.Payment.Monnify;

namespace Mova.Infrastructure.Payment;

public sealed partial class MonnifyService(
    IOptions<MonnifySettings> options,
    IExternalApiClient externalApiClient,
    IOptions<ExternalApiSettings> externalApiSettings)
    : IMonnifyService
{
    private readonly MonnifySettings _settings = options.Value;
    private readonly IExternalApiClient _externalApiClient = externalApiClient;
    private readonly ExternalApiSettings _externalApiSettings = externalApiSettings.Value;
}
