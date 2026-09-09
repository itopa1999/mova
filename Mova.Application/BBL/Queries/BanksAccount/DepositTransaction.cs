using System.Net;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Mova.Application.Interfaces.Persistence;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Shared.Common;

namespace Mova.Application.BBL.Queries.BanksAccount;

public sealed class DepositTransaction
{
    public sealed class Query
        : IRequest<BaseResult<List<TransactionDto>>>
    {
        [JsonIgnore]
        public string UserPublicId { get; set; } = string.Empty;
    }

    public sealed class TransactionDto
    {
        public long Id { get; init; }

        public string Title { get; init; } = string.Empty;

        public decimal Amount { get; init; }

        public TransactionType Type { get; init; }

        public TransactionStatus Status { get; init; }

        public string Reference { get; init; } = string.Empty;

        public DateTimeOffset? CompletedAt { get; init; }
        public DateTimeOffset? CreatedAt { get; init; }

    }

    public sealed class Handler
        : IRequestHandler<Query, BaseResult<List<TransactionDto>>>
    {
        private readonly IUnitOfWork _unitOfWork;

        public Handler(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }

        public async Task<BaseResult<List<TransactionDto>>> Handle(
            Query request,
            CancellationToken cancellationToken)
        {
            var transactions = await _unitOfWork
                .Query<Transaction>()
                .AsNoTracking()
                .Where(x =>
                    x.UserPublicId == request.UserPublicId &&
                    x.WalletId == null &&
                    x.Type == TransactionType.Deposit)
                .OrderByDescending(x => x.CreatedAt)
                .Select(x => new TransactionDto
                {
                    Id = x.Id,
                    Title = x.Title ?? string.Empty,
                    Amount = x.Amount.ToDecimal(),
                    Type = x.Type,
                    Status = x.Status,
                    Reference = x.Reference ?? string.Empty,
                    CompletedAt = x.CompletedAt,
                    CreatedAt = x.CreatedAt
                })
                .ToListAsync(cancellationToken);

            return new BaseResult<List<TransactionDto>>(
                HttpStatusCode.OK,
                "Deposit transactions retrieved successfully.",
                transactions);
        }
    }
}