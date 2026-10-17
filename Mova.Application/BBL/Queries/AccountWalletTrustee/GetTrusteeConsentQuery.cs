using System.ComponentModel.DataAnnotations;
using System.Net;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Mova.Application.Helpers;
using Mova.Application.Interfaces.Identity;
using Mova.Application.Interfaces.Persistence;
using Mova.Domain.Entities;
using Mova.Shared.Common;

namespace Mova.Application.BBL.Queries.AccountWalletTrustee;

public sealed class GetTrusteeConsentQuery
{
    public sealed class Query : IRequest<BaseResult<TrusteeConsentInfoDto>>
    {
        [Required, MinLength(20), MaxLength(100)]
        public string Token { get; set; } = string.Empty;
    }

    public sealed class TrusteeConsentInfoDto
    {
        public string OwnerFirstName { get; init; } = string.Empty;
        public string OwnerMaskedEmail { get; init; } = string.Empty;
        public string TrusteeEmail { get; init; } = string.Empty;
        public DateTimeOffset InvitationExpiresAt { get; init; }
    }

    public sealed class Handler : IRequestHandler<Query, BaseResult<TrusteeConsentInfoDto>>
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

        public async Task<BaseResult<TrusteeConsentInfoDto>> Handle(
            Query request,
            CancellationToken cancellationToken)
        {
            var tokenHash = TrusteeTokenHelper.Hash(request.Token);

            var trustee = await _unitOfWork.Query<WalletTrustee>()
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.VerificationTokenHash == tokenHash,
                    cancellationToken);

            if (trustee is null)
            {
                return new BaseResult<TrusteeConsentInfoDto>(
                    HttpStatusCode.NotFound,
                    "This invitation link is invalid or has already been used.");
            }

            if (trustee.Status != WalletTrusteeStatus.Verified &&
                trustee.Status != WalletTrusteeStatus.Pending)
            {
                return new BaseResult<TrusteeConsentInfoDto>(
                    HttpStatusCode.Gone,
                    "This invitation is no longer available.");
            }

            if (trustee.Status == WalletTrusteeStatus.Verified)
            {
                return new BaseResult<TrusteeConsentInfoDto>(
                    HttpStatusCode.Gone,
                    "This invitation has already been accepted.");
            }

            if (trustee.TokenExpiresAt is null ||
                trustee.TokenExpiresAt <= DateTimeOffset.UtcNow)
            {
                return new BaseResult<TrusteeConsentInfoDto>(
                    HttpStatusCode.Gone,
                    "This invitation has expired. Ask the wallet owner to send a new one.");
            }

            var owner = await _identityService.GetByIdentifierAsync(
                trustee.OwnerPublicId,
                cancellationToken);

            var ownerFirstName = string.IsNullOrWhiteSpace(owner?.FirstName)
                ? "The wallet owner"
                : owner.FirstName;

            var maskedOwnerEmail = MaskEmail(owner?.Email);

            return new BaseResult<TrusteeConsentInfoDto>(
                HttpStatusCode.OK,
                "Invitation is valid.",
                new TrusteeConsentInfoDto
                {
                    OwnerFirstName = ownerFirstName,
                    OwnerMaskedEmail = maskedOwnerEmail,
                    TrusteeEmail = trustee.Email,
                    InvitationExpiresAt = trustee.TokenExpiresAt.Value,
                });
        }

        private static string MaskEmail(string? email)
        {
            if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
                return "***";

            var atIndex = email.IndexOf('@');
            var local = email[..atIndex];
            var domain = email[(atIndex + 1)..];

            var visible = local.Length <= 2
                ? local[..1]
                : local[..2];

            return $"{visible}***@{domain}";
        }
    }
}