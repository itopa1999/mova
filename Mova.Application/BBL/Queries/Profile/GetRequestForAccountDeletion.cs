using System.Net;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Mova.Application.Interfaces.Identity;
using Mova.Application.Interfaces.Persistence;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Shared.Common;

namespace Mova.Application.BBL.Queries.Profile;

public sealed class GetRequestForAccountDeletion
{
    public sealed class Query : IRequest<BaseResult<GetRequestForAccountDeletionDto>>
    {
        public string UserPublicId { get; set; } = string.Empty;
    }

    public sealed class GetRequestForAccountDeletionDto
    {
        public string? AccountStatus { get; set; }
        public string? AccountStatusLabel { get; set; }
        public string? AccountStatusDescription { get; set; }

        public decimal WalletBalance { get; set; }
        public bool WalletBalanceNeedsResolution { get; set; }

        public decimal WalletAvailableBalance { get; set; }
        public bool AvailableBalanceNeedsResolution { get; set; }

        public bool HasPendingScheduleRelease { get; set; }
        public bool ScheduleReleaseNeedsResolution { get; set; }

        public bool HasActiveWallet { get; set; }
        public bool ActiveWalletNeedsResolution { get; set; }

        public bool IsTransactionActive { get; set; }
        public bool IsUserActionActive { get; set; }
        public bool UserActionNeedsResolution { get; set; }

        public bool CanRequestAccountDeletion { get; set; }
    }

    public sealed class Handler
        : IRequestHandler<Query, BaseResult<GetRequestForAccountDeletionDto>>
    {
        private const decimal ResolutionThreshold = 100m;

        private readonly IUnitOfWork _unitOfWork;
        private readonly IIdentityService _identityService;

        public Handler(IUnitOfWork unitOfWork, IIdentityService identityService)
        {
            _unitOfWork = unitOfWork;
            _identityService = identityService;
        }

        public async Task<BaseResult<GetRequestForAccountDeletionDto>> Handle(
            Query request,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(request.UserPublicId))
            {
                return new BaseResult<GetRequestForAccountDeletionDto>(
                    HttpStatusCode.BadRequest,
                    "User identity is required.");
            }

            var permissions = await _identityService.GetPermissionsAsync(
                request.UserPublicId,
                cancellationToken);

            if (permissions is null)
            {
                return new BaseResult<GetRequestForAccountDeletionDto>(
                    HttpStatusCode.NotFound,
                    "User account was not found.");
            }

            var dto = new GetRequestForAccountDeletionDto();

            if (permissions.AccountStatus != UserAccountStatus.Active)
            {
                dto.AccountStatus = permissions.AccountStatus.ToString();
                dto.AccountStatusLabel = permissions.StatusLabel;
                dto.AccountStatusDescription = permissions.StatusDescription;
                dto.CanRequestAccountDeletion = false;

                return new BaseResult<GetRequestForAccountDeletionDto>(
                    HttpStatusCode.OK,
                    "Account deletion eligibility retrieved successfully.",
                    dto);
            }

            var wallets = await _unitOfWork.Query<Wallet>()
                .AsNoTracking()
                .Where(w => w.UserPublicId == request.UserPublicId)
                .ToListAsync(cancellationToken);

            dto.WalletBalance = wallets
                .Sum(w => w.LockedAmount.ToDecimal());

            dto.WalletAvailableBalance = wallets
                .Sum(w => w.AvailableAmount.ToDecimal());

            dto.HasActiveWallet = wallets.Any(w =>
                w.Status == WalletStatus.Active ||
                w.Status == WalletStatus.Paused);

            var walletIds = wallets.Select(w => w.Id).ToList();

            dto.HasPendingScheduleRelease = walletIds.Count > 0 &&
                await _unitOfWork.Query<ScheduledRelease>()
                    .AsNoTracking()
                    .AnyAsync(r =>
                        walletIds.Contains(r.WalletId) &&
                        (r.Status == ReleaseStatus.Scheduled ||
                         r.Status == ReleaseStatus.Processing ||
                         r.Status == ReleaseStatus.Paused),
                        cancellationToken);

            dto.IsTransactionActive = await _unitOfWork
                .Query<Transaction>()
                .AsNoTracking()
                .AnyAsync(t =>
                    t.UserPublicId == request.UserPublicId &&
                    t.Status == TransactionStatus.Processing,
                    cancellationToken);

            dto.WalletBalanceNeedsResolution =
                dto.WalletBalance > ResolutionThreshold;

            dto.AvailableBalanceNeedsResolution =
                dto.WalletAvailableBalance > ResolutionThreshold;

            dto.ScheduleReleaseNeedsResolution =
                dto.HasPendingScheduleRelease;

            dto.ActiveWalletNeedsResolution =
                dto.HasActiveWallet;

            dto.UserActionNeedsResolution =
                dto.IsTransactionActive;

            dto.CanRequestAccountDeletion =
                !dto.WalletBalanceNeedsResolution &&
                !dto.AvailableBalanceNeedsResolution &&
                !dto.ScheduleReleaseNeedsResolution &&
                !dto.ActiveWalletNeedsResolution &&
                !dto.UserActionNeedsResolution;

            return new BaseResult<GetRequestForAccountDeletionDto>(
                HttpStatusCode.OK,
                "Account deletion eligibility retrieved successfully.",
                dto);
        }
    }
}