using Microsoft.Extensions.Options;
using Mova.Application.ExternalAPI;
using Mova.Application.Interfaces.Payment;
using Mova.Infrastructure.ExternalAPI;
using Mova.Infrastructure.Payment.Paystack;
namespace Mova.Infrastructure.Payment.Paystack;

public sealed partial class PaystackService(
    IOptions<PaystackSettings> options,
    IExternalApiClient externalApiClient,
    IOptions<ExternalApiSettings> externalApiSettings) 
    : IPaystackService
{
    private readonly PaystackSettings _settings = options.Value;
    private readonly IExternalApiClient _externalApiClient = externalApiClient;
    private readonly ExternalApiSettings _externalApiSettings = externalApiSettings.Value;
}
