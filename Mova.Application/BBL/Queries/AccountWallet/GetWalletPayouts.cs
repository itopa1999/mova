using System.Net;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Mova.Application.Interfaces.Persistence;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Shared.Common;

namespace Mova.Application.BBL.Queries.AccountWallet;

public sealed class GetWalletPayouts
{
    public sealed class Query : IRequest<BaseResult<List<WalletPayoutGroupDto>>>
    {
        [JsonIgnore]
        public string UserPublicId { get; set; } = string.Empty;
        public long WalletId { get; init; }
    }

    public sealed class WalletPayoutGroupDto
    {
        public DateTime Date { get; set; }
        public List<WalletPayoutDto> Activities { get; set; } = [];
    }

    public sealed class WalletPayoutDto
    {
        public long Id { get; set; }
        public string Type { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Subtitle { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public bool IsCredit { get; set; }
        public DateTimeOffset Date { get; set; }
    }

    public sealed class Handler : IRequestHandler<Query, BaseResult<List<WalletPayoutGroupDto>>>
    {
        private readonly IUnitOfWork _unitOfWork;

        public Handler(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }

        public async Task<BaseResult<List<WalletPayoutGroupDto>>> Handle(
            Query request,
            CancellationToken cancellationToken)
        {
            var payouts = await _unitOfWork.Query<Payout>()
                .AsNoTracking()
                .Where(x => x.WalletId == request.WalletId &&
                            x.UserPublicId == request.UserPublicId)
                .OrderByDescending(x => x.CreatedAt)
                .Select(x => new
                {
                    x.Id,
                    x.Status,
                    x.Amount,
                    x.Fee,
                    x.NetAmount,
                    x.Provider,
                    x.ProviderReference,
                    x.FailureReason,
                    x.CreatedAt,
                    x.InitiatedAt,
                    x.CompletedAt,
                    x.FailedAt
                })
                .ToListAsync(cancellationToken);

            var activities = payouts
                .Select(x =>
                {
                    var timestamp =
                        x.CompletedAt
                        ?? x.FailedAt
                        ?? x.InitiatedAt
                        ?? x.CreatedAt;

                    return new WalletPayoutDto
                    {
                        Id = x.Id,
                        Type = x.Status.ToString(),
                        Title = GetTitle(x.Status),
                        Subtitle = GetSubtitle(
                            x.Status,
                            x.Provider,
                            x.ProviderReference,
                            x.FailureReason),
                        Amount = x.NetAmount.ToDecimal(),
                        IsCredit = x.Status == PayoutStatus.Successful,
                        Date = timestamp
                    };
                })
                .ToList();

            var groupedActivities = activities
                .GroupBy(x => x.Date.Date)
                .OrderByDescending(x => x.Key)
                .Select(x => new WalletPayoutGroupDto
                {
                    Date = x.Key,
                    Activities = x
                        .OrderByDescending(a => a.Date)
                        .ToList()
                })
                .ToList();

            return new BaseResult<List<WalletPayoutGroupDto>>(
                HttpStatusCode.OK,
                "Wallet payouts retrieved successfully.",
                groupedActivities);
        }

        private static string GetTitle(PayoutStatus status) => status switch
        {
            PayoutStatus.Pending => "Payout Pending",
            PayoutStatus.Processing => "Payout Processing",
            PayoutStatus.Successful => "Payout Successful",
            PayoutStatus.Failed => "Payout Failed",
            PayoutStatus.Reversed => "Payout Reversed",
            _ => "Payout"
        };

        private static string GetSubtitle(
            PayoutStatus status,
            string? provider,
            string? providerReference,
            string? failureReason)
        {
            return status switch
            {
                PayoutStatus.Pending =>
                    "Waiting to be sent to your bank",
                PayoutStatus.Processing =>
                    "Sent to your bank, awaiting confirmation",
                PayoutStatus.Successful =>
                    string.IsNullOrWhiteSpace(providerReference)
                        ? "Funds delivered to your bank"
                        : $"Ref: {providerReference}",
                PayoutStatus.Failed =>
                    string.IsNullOrWhiteSpace(failureReason)
                        ? "Payout failed"
                        : failureReason!,
                PayoutStatus.Reversed =>
                    "Payout was reversed and returned to your wallet",
                _ => string.IsNullOrWhiteSpace(provider)
                    ? "Payout"
                    : provider!
            };
        }
    }
}