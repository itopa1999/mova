using System.Net;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.Extensions.Logging;
using Mova.Application.Interfaces.Identity;
using Mova.Application.Interfaces.Payment;
using Mova.Application.Interfaces.Persistence;
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

        public decimal Amount { get; init; }

        public string Provider { get; init; } = "paystack";
    }

    public sealed class FundAccountDto
    {
        public string AuthorizationUrl { get; set; } = string.Empty;
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

        public Handler(
            IIdentityService identityService,
            IPaystackService paystackService,
            IFlutterwaveService flutterwaveService,
            IMonnifyService monnifyService,
            IUnitOfWork unitOfWork,
            ILogger<Handler> logger)
        {
            _identityService = identityService;
            _paystackService = paystackService;
            _flutterwaveService = flutterwaveService;
            _monnifyService = monnifyService;
            _unitOfWork = unitOfWork;
            _logger = logger;
        }

        public async Task<BaseResult<FundAccountDto>> Handle(
            Command request,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(request.UserPublicId))
            {
                return new BaseResult<FundAccountDto>(
                    HttpStatusCode.BadRequest,
                    "User ID is required.");
            }

            if (request.Amount < MinimumFundingAmount ||
                request.Amount > MaximumFundingAmount ||
                Math.Round(request.Amount, 2) != request.Amount)
            {
                return new BaseResult<FundAccountDto>(
                    HttpStatusCode.BadRequest,
                    $"Funding amount must be at least ₦{MinimumFundingAmount:N0}, " +
                    $"no more than ₦{MaximumFundingAmount:N0}, " +
                    "and have at most two decimal places.");
            }

            if (!ProviderParser.TryParse(request.Provider, out var provider))
            {
                return new BaseResult<FundAccountDto>(
                    HttpStatusCode.BadRequest,
                    "Unsupported payment provider. " +
                    "Use 'paystack', 'flutterwave', or 'monnify' " +
                    "(or 1, 2, 3).");
            }

            using var op = OperationLogger.Start(
                _logger,
                "FundAccount",
                ("UserPublicId", request.UserPublicId),
                ("Amount", request.Amount),
                ("Provider", provider.ToString()));

            var user = await _identityService.GetByIdentifierAsync(
                request.UserPublicId,
                cancellationToken);

            if (user == null)
            {
                op.Fail("User not found.");
                return new BaseResult<FundAccountDto>(
                    HttpStatusCode.NotFound,
                    "User not found.");
            }

            var reference = $"MOVA-{Guid.NewGuid():N}".ToUpperInvariant();

            string authorizationUrl;

            switch (provider)
            {
                case PaymentProvider.Paystack:
                {
                    var response = await _paystackService.InitializePaymentAsync(
                        user.Email,
                        request.Amount,
                        reference,
                        cancellationToken);

                    if (!response.Success)
                    {
                        op.Fail($"Paystack init failed: {response.Message}");
                        return new BaseResult<FundAccountDto>(
                            HttpStatusCode.BadRequest,
                            response.Message ?? "Unable to initialize Paystack payment.");
                    }

                    authorizationUrl = response.AuthorizationUrl!;
                    break;
                }

                case PaymentProvider.Flutterwave:
                {
                    var response = await _flutterwaveService.InitializePaymentAsync(
                        user.Email,
                        request.Amount,
                        reference,
                        cancellationToken);

                    if (!response.Success)
                    {
                        op.Fail($"Flutterwave init failed: {response.Message}");
                        return new BaseResult<FundAccountDto>(
                            HttpStatusCode.BadRequest,
                            response.Message ?? "Unable to initialize Flutterwave payment.");
                    }

                    authorizationUrl = response.AuthorizationUrl!;
                    break;
                }

                case PaymentProvider.Monnify:
                {
                    var response = await _monnifyService.InitializePaymentAsync(
                        user.Email,
                        request.Amount,
                        reference,
                        cancellationToken);

                    if (!response.Success)
                    {
                        op.Fail($"Monnify init failed: {response.Message}");
                        return new BaseResult<FundAccountDto>(
                            HttpStatusCode.BadRequest,
                            response.Message ?? "Unable to initialize Monnify payment.");
                    }

                    authorizationUrl = response.AuthorizationUrl!;
                    break;
                }

                default:
                    op.Fail("Unsupported payment provider.");
                    return new BaseResult<FundAccountDto>(
                        HttpStatusCode.BadRequest,
                        "Unsupported payment provider.");
            }

            await _unitOfWork.BeginTransactionAsync(cancellationToken);

            try
            {
                var amount = Money.FromNaira(request.Amount);

                var transaction = new Transaction
                {
                    UserPublicId = request.UserPublicId,
                    WalletId = null,
                    Title = $"Fund account via {provider}",
                    Provider = provider,
                    Amount = amount,
                    Type = TransactionType.Deposit,
                    Status = TransactionStatus.Processing,
                    Reference = reference,
                    CompletedAt = null,
                };

                await _unitOfWork.AddAsync(transaction, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                await _unitOfWork.CommitTransactionAsync(cancellationToken);

                op.Success($"Payment initialized. TransactionId: {transaction.Id}");

                return new BaseResult<FundAccountDto>(
                    HttpStatusCode.OK,
                    "Proceed to gateway",
                    new FundAccountDto
                    {
                        AuthorizationUrl = authorizationUrl,
                    });
            }
            catch (Exception exception)
            {
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);

                op.Fail("Unable to initialize funding transaction.", exception);

                return new BaseResult<FundAccountDto>(
                    HttpStatusCode.InternalServerError,
                    "Unable to initialize funding. Please try again.");
            }
        }
    }
}