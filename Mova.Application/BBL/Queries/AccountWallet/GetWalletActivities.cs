using System.Net;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Mova.Application.Interfaces.Persistence;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Shared.Common;

namespace Mova.Application.BBL.Queries.AccountWallet;

public sealed class GetWalletActivities
{
    public sealed class Query : IRequest<BaseResult<List<WalletActivityGroupDto>>>
    {
        [JsonIgnore]
        public string UserPublicId { get; set; } = string.Empty;
        public long WalletId { get; init; }
    }

    public sealed class WalletActivityGroupDto
    {
        public DateTime Date { get; set; }
        public List<WalletActivityDto> Activities { get; set; } = [];
    }

    public sealed class WalletActivityDto
    {
        public long Id { get; set; }
        public string Type { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Subtitle { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public bool IsCredit { get; set; }
        public DateTimeOffset Date { get; set; }
    }

    public sealed class Handler : IRequestHandler<Query, BaseResult<List<WalletActivityGroupDto>>>
    {
        private readonly IUnitOfWork _unitOfWork;

        public Handler(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }

        public async Task<BaseResult<List<WalletActivityGroupDto>>> Handle(
            Query request,
            CancellationToken cancellationToken)
        {
            var wallet = await _unitOfWork.Query<Wallet>()
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.Id == request.WalletId &&
                         x.UserPublicId == request.UserPublicId,
                    cancellationToken);

            if (wallet is null)
            {
                return new BaseResult<List<WalletActivityGroupDto>>(
                    HttpStatusCode.NotFound,
                    "Wallet not found.");
            }

            var activities = await _unitOfWork.Query<Transaction>()
        .AsNoTracking()
        .Where(x =>
            x.WalletId == wallet.Id &&
            x.Status == TransactionStatus.Completed)
        .OrderByDescending(x => x.CreatedAt)
        .Select(x => new WalletActivityDto
        {
            Id = x.Id,
            Type = x.Type.ToString(),
            Title = x.Title,
            Subtitle = x.Type.ToString(),
            Amount = x.Amount.ToDecimal(),
            IsCredit = x.Type == TransactionType.Deposit ||
                    x.Type == TransactionType.Release ||
                    x.Type == TransactionType.Refund,
            Date = x.CompletedAt ?? x.CreatedAt
        })
        .ToListAsync(cancellationToken);

    var groupedActivities = activities
        .GroupBy(x => x.Date.Date)
        .OrderByDescending(x => x.Key)
        .Select(x => new WalletActivityGroupDto
        {
            Date = x.Key,
            Activities = x
                .OrderByDescending(a => a.Date)
                .ToList()
        })
        .ToList();

    return new BaseResult<List<WalletActivityGroupDto>>(
        HttpStatusCode.OK,
        "Wallet activities retrieved successfully.",
        groupedActivities);
        
        }
    }
}