using System.ComponentModel.DataAnnotations;
using System.Net;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Mova.Application.Helpers;
using Mova.Application.Interfaces.Identity;
using Mova.Application.Interfaces.Persistence;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Shared.Common;

namespace Mova.Application.BBL.Queries.AccountWalletTrustee;

public sealed class GetReleaseRequestConsentQuery
{
    public sealed class Query : IRequest<BaseResult<ReleaseRequestConsentDto>>
    {
        [Required, MinLength(20), MaxLength(100)]
        public string Token { get; set; } = string.Empty;
    }

    public sealed class ReleaseRequestConsentDto
    {
        public long RequestId { get; init; }
        public string WalletName { get; init; } = string.Empty;
        public decimal Amount { get; init; }
        public string? UserNote { get; init; }
        public string BankName { get; init; } = string.Empty;
        public string MaskedAccountNumber { get; init; } = string.Empty;
        public string RequesterFirstName { get; init; } = string.Empty;
        public string TrusteeEmail { get; init; } = string.Empty;
        public string? TrusteeName { get; init; }
        public DateTimeOffset RequestedAt { get; init; }
        public DateTimeOffset ExpiresAt { get; init; }
    }

    public sealed class Handler
        : IRequestHandler<Query, BaseResult<ReleaseRequestConsentDto>>
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IIdentityService _identityService;

        public Handler(
            IUnitOfWork unitOfWork,
            IIdentityService identityService)
        {
            _unitOfWork = unitOfWork;
            _identityService = identityService;
        }

        public async Task<BaseResult<ReleaseRequestConsentDto>> Handle(
            Query request,
            CancellationToken cancellationToken)
        {
            var tokenHash = TrusteeTokenHelper.Hash(request.Token);

            var releaseRequest = await _unitOfWork.Query<ReleaseRequest>()
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.ApprovalTokenHash == tokenHash,
                    cancellationToken);

            if (releaseRequest is null)
            {
                return new BaseResult<ReleaseRequestConsentDto>(
                    HttpStatusCode.NotFound,
                    "This link is invalid or has already been used.");
            }

            if (releaseRequest.Status != ReleaseRequestStatus.Pending)
            {
                return new BaseResult<ReleaseRequestConsentDto>(
                    HttpStatusCode.Gone,
                    "This release request has already been decided.");
            }

            if (releaseRequest.ExpiresAt <= DateTimeOffset.UtcNow)
            {
                return new BaseResult<ReleaseRequestConsentDto>(
                    HttpStatusCode.Gone,
                    "This release request has expired.");
            }

            var wallet = await _unitOfWork.Query<Wallet>()
                .AsNoTracking()
                .FirstOrDefaultAsync(w => w.Id == releaseRequest.WalletId && w.Type == WalletType.Trustee, cancellationToken);

            if (wallet is null)
            {
                return new BaseResult<ReleaseRequestConsentDto>(
                    HttpStatusCode.NotFound,
                    "Wallet not found.");
            }

            var trustee = await _unitOfWork.Query<WalletTrustee>()
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    t => t.Id == wallet.WalletTrusteeId,
                    cancellationToken);

            var bank = releaseRequest.DestinationBankAccountId is not null
                ? await _unitOfWork.Query<BankAccount>()
                    .AsNoTracking()
                    .FirstOrDefaultAsync(
                        b => b.Id == releaseRequest.DestinationBankAccountId.Value,
                        cancellationToken)
                : null;

            var owner = await _identityService.GetByIdentifierAsync(
                releaseRequest.RequestedByUserPublicId,
                cancellationToken);

            var maskedAccount = MaskAccount(bank?.AccountNumber);

            return new BaseResult<ReleaseRequestConsentDto>(
                HttpStatusCode.OK,
                "Release request is valid.",
                new ReleaseRequestConsentDto
                {
                    RequestId = releaseRequest.Id,
                    WalletName = wallet.Name,
                    Amount = releaseRequest.Amount.ToDecimal(),
                    UserNote = releaseRequest.UserNote,
                    BankName = bank?.BankName ?? "—",
                    MaskedAccountNumber = maskedAccount,
                    RequesterFirstName = owner?.FirstName ?? "The wallet owner",
                    TrusteeEmail = trustee?.Email ?? string.Empty,
                    TrusteeName = trustee?.Name,
                    RequestedAt = releaseRequest.RequestedAt,
                    ExpiresAt = releaseRequest.ExpiresAt,
                });
        }

        private static string MaskAccount(string? number)
        {
            if (string.IsNullOrEmpty(number) || number.Length <= 4) return "••••";
            return $"••••{number[^4..]}";
        }
    }
}