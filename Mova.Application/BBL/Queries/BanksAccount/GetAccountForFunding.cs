using System.Net;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Mova.Application.Interfaces.Persistence;
using Mova.Application.Interfaces.Service;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Shared.Common;

namespace Mova.Application.BBL.Queries.BanksAccount;

public sealed class GetAccountForFunding
{
    public sealed class Query
        : IRequest<BaseResult<GetAccountForFundingResponse>>
    {
        [JsonIgnore]
        public string UserPublicId { get; set; } = string.Empty;
    }

    public sealed class GetAccountForFundingResponse
    {
        public AccountDetails? AccountDetails { get; set; }
        public List<string> AllowedGateways { get; set; } = new();
        public bool IsDepositAllowed { get; set; }
    }

    public sealed class AccountDetails
    {
        public string BankName { get; set; } = string.Empty;
        public string AccountNumber { get; set; } = string.Empty;
        public string AccountName { get; set; } = string.Empty;
    }

    public sealed class Handler
        : IRequestHandler<Query, BaseResult<GetAccountForFundingResponse>>
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IFeatureFlagService _featureFlagService;

        public Handler(
            IUnitOfWork unitOfWork,
            IFeatureFlagService featureFlagService)
        {
            _unitOfWork = unitOfWork;
            _featureFlagService = featureFlagService;
        }

        public async Task<BaseResult<GetAccountForFundingResponse>> Handle(
            Query request,
            CancellationToken cancellationToken)
        {
            var isDepositAllowed = await _featureFlagService.IsEnabledAsync(
                FeatureFlagName.AllowDepositFunds,
                cancellationToken);

            var allowedGateways = await ResolveAllowedDepositGatewaysAsync(cancellationToken);

            AccountDetails? accountDetails = null;

            if (isDepositAllowed && allowedGateways.Count > 0)
            {
                var activeProvider = ParseProvider(allowedGateways[0]);

                accountDetails = await _unitOfWork.Query<VirtualAccount>()
                    .AsNoTracking()
                    .Where(v =>
                        v.UserPublicId == request.UserPublicId &&
                        v.Provider == activeProvider &&
                        v.Status == VirtualAccountStatus.Active)
                    .Select(v => new AccountDetails
                    {
                        BankName = v.BankName,
                        AccountNumber = v.AccountNumber,
                        AccountName = v.AccountName,
                    })
                    .FirstOrDefaultAsync(cancellationToken);
            }

            var response = new GetAccountForFundingResponse
            {
                AccountDetails = accountDetails,
                AllowedGateways = allowedGateways,
                IsDepositAllowed = isDepositAllowed,
            };

            return new BaseResult<GetAccountForFundingResponse>(
                HttpStatusCode.OK,
                "Funding method retrieved successfully.",
                response);
        }

        private async Task<List<string>> ResolveAllowedDepositGatewaysAsync(
            CancellationToken cancellationToken)
        {
            var gateways = new List<string>();

            if (await _featureFlagService.IsEnabledAsync(
                    FeatureFlagName.DepositViaPaystack, cancellationToken))
            {
                gateways.Add(PaymentProvider.Paystack.ToString());
            }

            if (await _featureFlagService.IsEnabledAsync(
                    FeatureFlagName.DepositViaMonnify, cancellationToken))
            {
                gateways.Add(PaymentProvider.Monnify.ToString());
            }

            if (await _featureFlagService.IsEnabledAsync(
                    FeatureFlagName.DepositViaFlutterwave, cancellationToken))
            {
                gateways.Add(PaymentProvider.Flutterwave.ToString());
            }

            return gateways;
        }

        private static PaymentProvider ParseProvider(string gateway)
        {
            return Enum.TryParse<PaymentProvider>(gateway, ignoreCase: true, out var provider)
                ? provider
                : PaymentProvider.Paystack;
        }
    }
}