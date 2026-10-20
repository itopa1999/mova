using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.Extensions.Logging;
using Mova.Application.Interfaces.Identity;
using Mova.Application.Interfaces.Payment;
using Mova.Application.Interfaces.Persistence;
using Mova.Application.Interfaces.Service;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Domain.ValueObjects;
using Mova.Shared.Common;
using Mova.Shared.Logging;

namespace Mova.Application.BBL.Commands.BanksAccount;

public sealed class FundAccount
{
    public sealed class Command : IRequest<BaseResult<FundAccountDto>>
    {
        [JsonIgnore]
        public string UserPublicId { get; set; } = string.Empty;

        [Range(typeof(decimal), "1000", "100000000")]
        public decimal Amount { get; init; }

        [Required, MaxLength(20), RegularExpression(@"(?i)^\s*(paystack|flutterwave|monnify|1|2|3)\s*$")]
        public string Provider { get; init; } = "paystack";
    }

    public sealed class FundAccountDto
    {
        public string AuthorizationUrl { get; set; } = string.Empty;
        public string Reference { get; set; } = string.Empty;
    }

    internal static class ProviderParser
    {
        public static bool TryParse(
            string? raw,
            out PaymentProvider provider)
        {
            provider = PaymentProvider.Paystack;

            if (string.IsNullOrWhiteSpace(raw))
                return false;

            var value = raw.Trim();

            if (int.TryParse(value, out var numeric))
            {
                if (Enum.IsDefined(typeof(PaymentProvider), numeric))
                {
                    provider = (PaymentProvider)numeric;
                    return true;
                }
                return false;
            }

            if (Enum.TryParse<PaymentProvider>(
                    value,
                    ignoreCase: true,
                    out var parsed))
            {
                provider = parsed;
                return true;
            }

            return false;
        }
    }

    public sealed class Handler
        : IRequestHandler<Command, BaseResult<FundAccountDto>>
    {
        private const decimal MinimumFundingAmount = 1000m;
        private const decimal MaximumFundingAmount = 100_000_000m;

        private readonly IIdentityService _identityService;
        private readonly IPaystackService _paystackService;
        private readonly IFlutterwaveService _flutterwaveService;
        private readonly IMonnifyService _monnifyService;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ILogger<Handler> _logger;
        private readonly IFeatureFlagService _featureFlagService;

        public Handler(
            IIdentityService identityService,
            IPaystackService paystackService,
            IFlutterwaveService flutterwaveService,
            IMonnifyService monnifyService,
            IUnitOfWork unitOfWork,
            ILogger<Handler> logger,
            IFeatureFlagService featureFlagService)
        {
            _identityService = identityService;
            _paystackService = paystackService;
            _flutterwaveService = flutterwaveService;
            _monnifyService = monnifyService;
            _unitOfWork = unitOfWork;
            _logger = logger;
            _featureFlagService = featureFlagService;
        }

        public async Task<BaseResult<FundAccountDto>> Handle(
            Command request,
            CancellationToken cancellationToken)
        {
            using var op = OperationLogger.Start(
                _logger,
                "FundAccount",
                ("UserPublicId", request.UserPublicId),
                ("Amount", request.Amount),
                ("Provider", request.Provider));

            if (string.IsNullOrWhiteSpace(request.UserPublicId))
            {
                op.Warning("User ID is required.");
                return new BaseResult<FundAccountDto>(
                    HttpStatusCode.BadRequest,
                    "User ID is required.");
            }

            if (request.Amount < MinimumFundingAmount ||
                request.Amount > MaximumFundingAmount ||
                Math.Round(request.Amount, 2) != request.Amount)
            {
                op.Warning($"Invalid amount: {request.Amount}.");
                return new BaseResult<FundAccountDto>(
                    HttpStatusCode.BadRequest,
                    $"Funding amount must be at least ₦{MinimumFundingAmount:N0}, " +
                    $"no more than ₦{MaximumFundingAmount:N0}, " +
                    "and have at most two decimal places.");
            }

            if (!ProviderParser.TryParse(request.Provider, out var provider))
            {
                op.Warning($"Unsupported provider: {request.Provider}.");
                return new BaseResult<FundAccountDto>(
                    HttpStatusCode.BadRequest,
                    "Unsupported payment provider. " +
                    "Use 'paystack', 'flutterwave', or 'monnify' (or 1, 2, 3).");
            }

            if (!await _featureFlagService.IsEnabledAsync(
                FeatureFlagName.AllowDepositFunds,
                cancellationToken))
            {
                op.Warning("Deposits are disabled globally.");
                return new BaseResult<FundAccountDto>(
                    HttpStatusCode.BadRequest,
                    "Payment cannot be completed at this time.");
            }

            var user = await _identityService.GetByIdentifierAsync(
                request.UserPublicId,
                cancellationToken);

            if (user is null)
            {
                op.Warning($"User not found: {request.UserPublicId}.");
                return new BaseResult<FundAccountDto>(
                    HttpStatusCode.NotFound,
                    "User not found.");
            }

            var (providerEnabled, providerName) = provider switch
            {
                PaymentProvider.Paystack => (
                    await _featureFlagService.IsEnabledAsync(
                        FeatureFlagName.DepositViaPaystack, cancellationToken),
                    "Paystack"),
                PaymentProvider.Flutterwave => (
                    await _featureFlagService.IsEnabledAsync(
                        FeatureFlagName.DepositViaFlutterwave, cancellationToken),
                    "Flutterwave"),
                PaymentProvider.Monnify => (
                    await _featureFlagService.IsEnabledAsync(
                        FeatureFlagName.DepositViaMonnify, cancellationToken),
                    "Monnify"),
                _ => (false, "Unknown"),
            };

            if (!providerEnabled)
            {
                op.Warning($"{providerName} is disabled.");
                return new BaseResult<FundAccountDto>(
                    HttpStatusCode.BadRequest,
                    "Payment cannot be completed at this time, please choose another gateway method.");
            }

            var reference = $"mova_{Guid.NewGuid():N}";
            var transaction = new Transaction
            {
                UserPublicId = request.UserPublicId,
                WalletId = null,
                Title = $"Fund account via {provider}",
                Provider = provider,
                Amount = Money.FromNaira(request.Amount),
                Type = TransactionType.Deposit,
                Status = TransactionStatus.Pending,
                Reference = reference,
                CompletedAt = null,
            };

            await _unitOfWork.AddAsync(transaction, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            PaymentInitializationResultDto response = provider switch
            {
                PaymentProvider.Paystack => await _paystackService.InitializePaymentAsync(
                    user.Email, request.Amount, reference, cancellationToken),

                PaymentProvider.Flutterwave => await _flutterwaveService.InitializePaymentAsync(
                    user.Email, request.Amount, reference, cancellationToken),

                PaymentProvider.Monnify => await _monnifyService.InitializePaymentAsync(
                    user.Email, request.Amount, reference, cancellationToken),

                _ => new PaymentInitializationResultDto
                {
                    CallSucceeded = false,
                    Message = "Unsupported payment provider.",
                },
            };

            if (!response.CallSucceeded ||
                string.IsNullOrWhiteSpace(response.AuthorizationUrl))
            {
                op.Warning($"{providerName} init failed: {response.Message}");

                transaction.Status = TransactionStatus.Failed;
                _unitOfWork.Update(transaction);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                return new BaseResult<FundAccountDto>(
                    HttpStatusCode.BadRequest,
                    response.Message ?? $"Unable to initialize {providerName} payment.");
            }

            transaction.Status = TransactionStatus.Processing;
            _unitOfWork.Update(transaction);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            op.Success(
                $"Payment initialized. TransactionId: {transaction.Id}, " +
                $"Reference: {reference}, AccessCode: {response.AccessCode ?? "(none)"}.");

            return new BaseResult<FundAccountDto>(
                HttpStatusCode.OK,
                "Proceed to gateway",
                new FundAccountDto
                {
                    AuthorizationUrl = response.AuthorizationUrl,
                    Reference = reference,
                });
        }
    }
}