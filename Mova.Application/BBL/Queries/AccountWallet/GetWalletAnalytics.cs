using System.Net;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Mova.Application.Interfaces.Persistence;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Shared.Common;

namespace Mova.Application.BBL.Queries.AccountWallet;

public sealed class GetWalletAnalytics
{
    public sealed class Query : IRequest<BaseResult<WalletAnalyticsDto>>
    {
        [JsonIgnore]
        public string UserPublicId { get; set; } = string.Empty;

        public DateTime? Date { get; init; }
    }

    public sealed class WalletAnalyticsDto
    {
        public string Month { get; set; } = string.Empty;
        public decimal MoneyProtected { get; set; }
        public decimal MoneyReleased { get; set; }
        public decimal MoneySpent { get; set; }
        public decimal Remaining { get; set; }
        public decimal ProtectedPercentage { get; set; }
    }

    public sealed class Handler : IRequestHandler<Query, BaseResult<WalletAnalyticsDto>>
    {
        private readonly IUnitOfWork _unitOfWork;

        public Handler(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }

        public async Task<BaseResult<WalletAnalyticsDto>> Handle(
            Query request,
            CancellationToken cancellationToken)
        {
            var selectedDate = request.Date ?? DateTime.UtcNow;

            var startDate = new DateTime(
                selectedDate.Year,
                selectedDate.Month,
                1,
                0,
                0,
                0,
                DateTimeKind.Utc);

            var endDate = startDate.AddMonths(1);

            var wallets = await _unitOfWork.Query<Wallet>()
                .AsNoTracking()
                .Where(x =>
                    x.UserPublicId == request.UserPublicId &&
                    x.CreatedAt < endDate)
                .Select(x => new
                {
                    x.Id,
                    x.CreatedAt,
                    x.FundedAmount
                })
                .ToListAsync(cancellationToken);

            if (wallets.Count == 0)
            {
                return new BaseResult<WalletAnalyticsDto>(
                    HttpStatusCode.OK,
                    "Wallet analytics retrieved successfully.",
                    new WalletAnalyticsDto
                    {
                        Month = startDate.ToString("MMMM yyyy"),
                        MoneyProtected = 0,
                        MoneyReleased = 0,
                        MoneySpent = 0,
                        Remaining = 0,
                        ProtectedPercentage = 0
                    });
            }

            var walletIds = wallets
                .Select(x => x.Id)
                .ToList();

            var transactions = await _unitOfWork.Query<Transaction>()
                .AsNoTracking()
                .Where(x =>
                    x.WalletId.HasValue &&
                    walletIds.Contains(x.WalletId.Value) &&
                    x.Status == TransactionStatus.Completed &&
                    x.CompletedAt.HasValue &&
                    x.CompletedAt.Value >= startDate &&
                    x.CompletedAt.Value < endDate)
                .Select(x => new
                {
                    x.Type,
                    x.Amount
                })
                .ToListAsync(cancellationToken);

            var moneyProtected = wallets
                .Where(x =>
                    x.CreatedAt >= startDate &&
                    x.CreatedAt < endDate)
                .Sum(x => x.FundedAmount.ToDecimal());

            var moneyReleased = transactions
                .Where(x => x.Type == TransactionType.Release)
                .Sum(x => x.Amount.ToDecimal());

            var moneySpent = transactions
                .Where(x => x.Type == TransactionType.Withdrawal)
                .Sum(x => x.Amount.ToDecimal());

            var remaining = Math.Max(
                0,
                moneyReleased - moneySpent);

            var totalProtectedBase = moneyProtected + moneyReleased;

            var protectedPercentage = totalProtectedBase > 0
                ? Math.Round(
                    moneyProtected / totalProtectedBase * 100,
                    2)
                : 0;

            var response = new WalletAnalyticsDto
            {
                Month = startDate.ToString("MMMM yyyy"),
                MoneyProtected = moneyProtected,
                MoneyReleased = moneyReleased,
                MoneySpent = moneySpent,
                Remaining = remaining,
                ProtectedPercentage = protectedPercentage
            };

            return new BaseResult<WalletAnalyticsDto>(
                HttpStatusCode.OK,
                "Wallet analytics retrieved successfully.",
                response);
        }
    }
}