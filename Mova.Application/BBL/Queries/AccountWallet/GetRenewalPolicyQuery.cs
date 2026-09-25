using System.Net;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mova.Application.Interfaces.Persistence;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Shared.Common;
using Mova.Shared.Logging;

namespace Mova.Application.BBL.Queries.AccountWallet;

public sealed class GetRenewalPolicyQuery
{
    public sealed class Query : IRequest<BaseResult<GetRenewalPolicyResponseDto>>
    {
        [JsonIgnore]
        public string UserPublicId { get; set; } = string.Empty;

        public long WalletId { get; set; }
    }

    public sealed class GetRenewalPolicyResponseDto
    {
        public long RenewalPolicyId { get; init; }
        public long WalletId { get; init; }
        public string WalletName { get; init; } = string.Empty;

        public bool IsEnabled { get; init; }
         public string Status { get; init; } = string.Empty;

        public string TriggerType { get; init; } = string.Empty;
        public decimal TriggerAmount { get; init; }

        public string RefillAmountType { get; init; } = string.Empty;
        public decimal RefillAmount { get; init; }
        public decimal RefillAmountEffective { get; init; }

        public bool RefillUntilMainBalanceExhausted { get; set; }


        public decimal MinMainBalance { get; init; }

        public int? MaxRenewals { get; init; }
        public int RenewalsCount { get; init; }
        public int? RenewalsRemaining { get; init; }

        public DateTimeOffset CreatedAt { get; init; }
        public DateTimeOffset? ModifiedAt { get; init; }
    }

    public sealed class Handler
        : IRequestHandler<Query, BaseResult<GetRenewalPolicyResponseDto>>
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly ILogger<Handler> _logger;

        public Handler(
            IUnitOfWork unitOfWork,
            ILogger<Handler> logger)
        {
            _unitOfWork = unitOfWork;
            _logger = logger;
        }

        public async Task<BaseResult<GetRenewalPolicyResponseDto>> Handle(
            Query request,
            CancellationToken cancellationToken)
        {
            using var op = OperationLogger.Start(
                _logger,
                "GetRenewalPolicy",
                ("UserId", request.UserPublicId),
                ("WalletId", request.WalletId));

            var wallet = await _unitOfWork.Query<Wallet>()
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.Id == request.WalletId
                         && x.UserPublicId == request.UserPublicId,
                    cancellationToken);

            if (wallet is null)
            {
                op.Fail("Wallet not found.");
                return new BaseResult<GetRenewalPolicyResponseDto>(
                    HttpStatusCode.NotFound,
                    "Wallet not found.");
            }

            var policy = await _unitOfWork.Query<RenewalPolicy>()
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.WalletId == wallet.Id,
                    cancellationToken);

            if (policy is null)
            {
                op.Success("No automation policy for this wallet.");
                return new BaseResult<GetRenewalPolicyResponseDto>(
                    HttpStatusCode.NotFound,
                    "This wallet has no automation policy.");
            }

            var refillAmountEffective =
                policy.RefillAmountType == RefillAmountType.Fixed
                    ? wallet.TargetAmount.ToDecimal()
                    : policy.RefillAmount.ToDecimal();

            int? renewalsRemaining = policy.MaxRenewals is null
                ? null
                : Math.Max(0, policy.MaxRenewals.Value - policy.RenewalsCount);

            var dto = new GetRenewalPolicyResponseDto
            {
                RenewalPolicyId = policy.Id,
                WalletId = wallet.Id,
                WalletName = wallet.Name,
                IsEnabled = policy.IsEnabled,
                Status = policy.Status.ToString(),
                TriggerType = policy.TriggerType.ToString(),
                TriggerAmount = policy.TriggerAmount.ToDecimal(),
                RefillAmountType = policy.RefillAmountType.ToString(),
                RefillAmount = policy.RefillAmount.ToDecimal(),
                RefillAmountEffective = refillAmountEffective,
                MinMainBalance = policy.MinMainBalance.ToDecimal(),
                RefillUntilMainBalanceExhausted = policy.RefillUntilMainBalanceExhausted,
                MaxRenewals = policy.MaxRenewals,
                RenewalsCount = policy.RenewalsCount,
                RenewalsRemaining = renewalsRemaining,
                CreatedAt = policy.CreatedAt,
                ModifiedAt = policy.ModifiedAt,
            };

            op.Success($"Renewal policy returned. PolicyId: {policy.Id}");

            return new BaseResult<GetRenewalPolicyResponseDto>(
                HttpStatusCode.OK,
                "Automation policy retrieved.",
                dto);
        }
    }
}