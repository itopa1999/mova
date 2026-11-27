using System.Net;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Mova.Application.Interfaces.Persistence;
using Mova.Domain.Entities;
using Mova.Shared.Common.Results;

namespace Mova.Application.BBL.Queries.Admin.UserQuery;

public sealed class GetUserBankAccount
{
    public sealed class Query : IRequest<BaseResult<UserBankAccountsDto>>
    {
        public string UserId { get; init; } = string.Empty;
    }

    public sealed class Handler : IRequestHandler<Query, BaseResult<UserBankAccountsDto>>
    {
        private readonly IUnitOfWork _unitOfWork;
        public Handler(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }

        public async Task<BaseResult<UserBankAccountsDto>> Handle(
            Query request,
            CancellationToken cancellationToken)
        {
            bool userExists = await _unitOfWork.Query<User>()
                .AsNoTracking()
                .IgnoreQueryFilters()
                .AnyAsync(u => u.PublicId == request.UserId, cancellationToken);

            if (!userExists)
            {
                return new BaseResult<UserBankAccountsDto>
                (
                    HttpStatusCode.NotFound,
                    "User not found.",
                    null
                );
            }

            var accounts = await _unitOfWork.Query<BankAccount>()
                .AsNoTracking()
                .IgnoreQueryFilters()
                .Where(b => b.UserPublicId == request.UserId)
                .OrderByDescending(b => b.IsDefault)
                .ThenByDescending(b => b.CreatedAt)
                .ThenBy(b => b.Id)
                .Select(b => new UserBankAccountDto
                {
                    Id = b.Id.ToString(),
                    UserPublicId = b.UserPublicId,
                    AccountNumber = b.AccountNumber,
                    AccountName = b.AccountName,
                    BankCode = b.BankCode,
                    BankName = b.BankName,
                    BankImageUrl = b.BankImageUrl,

                    PaystackRecipientCode = b.PaystackRecipientCode,
                    FlutterwaveRecipientId = b.FlutterwaveRecipientId,

                    Status = b.Status.ToString(),
                    IsDefault = b.IsDefault,

                    VerifiedAt = b.VerifiedAt,
                    VerificationMessage = b.VerificationMessage,

                    ConsentGiven = b.ConsentGiven,
                    ConsentGivenAt = b.ConsentGivenAt,
                    ConsentVersion = b.ConsentVersion,

                    Institution = b.Institution,
                    Currency = b.Currency,

                    CreatedAt = b.CreatedAt,
                    ModifiedAt = b.ModifiedAt,
                    IsDeleted = b.IsDeleted,
                    DeletedAt = b.DeletedAt,
                    CreatedBy = b.CreatedBy,
                    ModifiedBy = b.ModifiedBy,
                    DeletedBy = b.DeletedBy,
                })
                .ToListAsync(cancellationToken);

            return new BaseResult<UserBankAccountsDto>
            (
                HttpStatusCode.OK,
                "User bank accounts retrieved successfully.",
                new UserBankAccountsDto
                {
                    Items = accounts,
                    TotalCount = accounts.Count,
                }
            );
        }
    }
}

public sealed record UserBankAccountDto
{
    public string Id { get; init; } = string.Empty;
    public string UserPublicId { get; init; } = string.Empty;
    public string AccountNumber { get; init; } = string.Empty;
    public string AccountName { get; init; } = string.Empty;
    public string BankCode { get; init; } = string.Empty;
    public string BankName { get; init; } = string.Empty;
    public string BankImageUrl { get; init; } = string.Empty;

    public string? PaystackRecipientCode { get; init; }
    public string? FlutterwaveRecipientId { get; init; }

    public string Status { get; init; } = string.Empty;
    public bool IsDefault { get; init; }

    public DateTimeOffset? VerifiedAt { get; init; }
    public string? VerificationMessage { get; init; }

    public bool ConsentGiven { get; init; }
    public DateTimeOffset? ConsentGivenAt { get; init; }
    public string? ConsentVersion { get; init; }

    public string? Institution { get; init; }
    public string? Currency { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? ModifiedAt { get; init; }
    public bool IsDeleted { get; init; }
    public DateTimeOffset? DeletedAt { get; init; }
    public string? CreatedBy { get; init; }
    public string? ModifiedBy { get; init; }
    public string? DeletedBy { get; init; }
}

public sealed record UserBankAccountsDto
{
    public IReadOnlyList<UserBankAccountDto> Items { get; init; } = [];
    public int TotalCount { get; init; }
}