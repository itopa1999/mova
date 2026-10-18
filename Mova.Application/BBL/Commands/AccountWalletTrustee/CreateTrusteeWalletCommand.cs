using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mova.Application.Interfaces.Identity;
using Mova.Application.Interfaces.Notification;
using Mova.Application.Interfaces.Persistence;
using Mova.Application.Interfaces.Security;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Domain.ValueObjects;
using Mova.Shared.Common;
using Mova.Shared.Logging;

namespace Mova.Application.BBL.Commands.AccountWalletTrustee;

public sealed class CreateTrusteeWalletCommand
{
    public sealed class Command : IRequest<BaseResult<CreateTrusteeWalletResponseDto>>
    {
        [JsonIgnore]
        public string UserPublicId { get; set; } = string.Empty;

        [JsonIgnore]
        public string Email { get; set; } = string.Empty;

        [JsonIgnore]
        public string FirstName { get; set; } = string.Empty;

        [Required, StringLength(150, MinimumLength = 1)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(2000)]
        public string? Description { get; set; }

        [Range(1, long.MaxValue)]
        public long CategoryId { get; set; }

        [Range(typeof(decimal), "2000", "100000000")]
        public decimal TargetAmount { get; set; }

        [Range(1, long.MaxValue)]
        public long? WalletTrusteeId { get; set; }

        [Range(1, long.MaxValue)]
        public long BankAccountId { get; set; }
    }

    public sealed class CreateTrusteeWalletResponseDto
    {
        public long WalletId { get; init; }
        public string WalletName { get; init; } = string.Empty;
        public decimal FundedAmount { get; init; }
        public decimal TotalCharges { get; init; }
        public decimal NewMainBalance { get; init; }
        public long? WalletTrusteeId { get; init; }
    }

    public sealed class Handler
        : IRequestHandler<Command, BaseResult<CreateTrusteeWalletResponseDto>>
    {
        private readonly IIdentityService _identityService;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ILogger<Handler> _logger;
        private readonly INotificationQueue _notificationQueue;
        private readonly IPinChallenge _pinChallenge;

        public Handler(
            IIdentityService identityService,
            IUnitOfWork unitOfWork,
            ILogger<Handler> logger,
            INotificationQueue notificationQueue,
            IPinChallenge pinChallenge)
        {
            _identityService = identityService;
            _unitOfWork = unitOfWork;
            _logger = logger;
            _notificationQueue = notificationQueue;
            _pinChallenge = pinChallenge;
        }

        public async Task<BaseResult<CreateTrusteeWalletResponseDto>> Handle(
            Command request,
            CancellationToken cancellationToken)
        {
            using var op = OperationLogger.Start(
                _logger,
                "CreateTrusteeWallet",
                ("UserId", request.UserPublicId));

            var permissions = await _identityService.GetPermissionsAsync(
                request.UserPublicId,
                cancellationToken);

            if (permissions is null)
            {
                op.Error($"Permissions unavailable for user {request.UserPublicId}.");
                return new BaseResult<CreateTrusteeWalletResponseDto>(
                    HttpStatusCode.InternalServerError,
                    "We couldn't verify your account permissions right now. Please try again in a moment.");
            }

            if (!permissions.CanCreateWallets)
            {
                op.Warning(
                    $"Wallet creation blocked for user {request.UserPublicId}. " +
                    $"Status: {permissions.StatusLabel}, " +
                    $"Reason: {permissions.RestrictionReason}");

                var userMessage = string.IsNullOrWhiteSpace(permissions.StatusDescription)
                    ? "Your account is currently restricted from creating wallets. Please contact support."
                    : permissions.StatusDescription;

                return new BaseResult<CreateTrusteeWalletResponseDto>(
                    HttpStatusCode.Locked,
                    userMessage);
            }

            var (challengeValid, challengeMessage) = await _pinChallenge.ValidateAsync(
                request.UserPublicId,
                PinChallengeType.CreateWalletTrustee,
                cancellationToken);

            if (!challengeValid)
            {
                op.Warning($"PIN challenge validation failed: {challengeMessage}");
                return new BaseResult<CreateTrusteeWalletResponseDto>(
                    HttpStatusCode.BadRequest,
                    challengeMessage);
            }

            var walletName = (request.Name ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(walletName))
            {
                op.Warning("Wallet name is required.");
                return new BaseResult<CreateTrusteeWalletResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Wallet name is required.");
            }

            if (walletName.Length > 150)
            {
                op.Warning("Wallet name is too long.");
                return new BaseResult<CreateTrusteeWalletResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Wallet name cannot exceed 150 characters.");
            }

            if (request.TargetAmount < 2000)
            {
                op.Warning("Target amount must be at least ₦2,000.");
                return new BaseResult<CreateTrusteeWalletResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Target amount must be at least ₦2,000.");
            }

            var existingWallet = await _unitOfWork.Query<Wallet>()
                .AsNoTracking()
                .FirstOrDefaultAsync(x =>
                    x.UserPublicId == request.UserPublicId &&
                    x.Name.ToLower() == walletName.ToLower() &&
                    x.Type == WalletType.Trustee &&
                    x.Status == WalletStatus.Active,
                    cancellationToken);

            if (existingWallet is not null)
            {
                op.Warning("Wallet name already exists.");
                return new BaseResult<CreateTrusteeWalletResponseDto>(
                    HttpStatusCode.BadRequest,
                    "A wallet with this name already exists.");
            }

            var categoryExists = await _unitOfWork.Query<WalletCategory>()
                .AsNoTracking()
                .AnyAsync(x => x.Id == request.CategoryId, cancellationToken);

            if (!categoryExists)
            {
                op.Warning("Invalid wallet category.");
                return new BaseResult<CreateTrusteeWalletResponseDto>(
                    HttpStatusCode.BadRequest,
                    "The selected wallet category does not exist.");
            }

            var bankAccount = await _unitOfWork.Query<BankAccount>()
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.Id == request.BankAccountId &&
                        x.UserPublicId == request.UserPublicId,
                    cancellationToken);

            if (bankAccount is null)
            {
                return new BaseResult<CreateTrusteeWalletResponseDto>(
                    HttpStatusCode.BadRequest,
                    "The selected bank account does not exist or does not belong to you.");
            }

            if (bankAccount.Status != BankAccountStatus.Active)
            {
                return new BaseResult<CreateTrusteeWalletResponseDto>(
                    HttpStatusCode.BadRequest,
                    "The selected bank account is not active.");
            }

            var trusteeAlreadyLinked = request.WalletTrusteeId.HasValue;

            if (trusteeAlreadyLinked)
            {
                var trustee = await _unitOfWork.Query<WalletTrustee>()
                    .AsNoTracking()
                    .FirstOrDefaultAsync(
                        x => x.Id == request.WalletTrusteeId!.Value,
                        cancellationToken);

                if (trustee is null)
                {
                    op.Warning($"Trustee {request.WalletTrusteeId} not found.");
                    return new BaseResult<CreateTrusteeWalletResponseDto>(
                        HttpStatusCode.BadRequest,
                        "The selected trustee does not exist.");
                }

                if (trustee.OwnerPublicId != request.UserPublicId)
                {
                    op.Warning(
                        $"Trustee {trustee.Id} is owned by {trustee.OwnerPublicId}, " +
                        $"not by {request.UserPublicId}.");
                    return new BaseResult<CreateTrusteeWalletResponseDto>(
                        HttpStatusCode.BadRequest,
                        "The selected trustee does not belong to you.");
                }

                if (trustee.Status != WalletTrusteeStatus.Verified)
                {
                    op.Warning(
                        $"Trustee {trustee.Id} is not verified. Status: {trustee.Status}");
                    return new BaseResult<CreateTrusteeWalletResponseDto>(
                        HttpStatusCode.BadRequest,
                        "The selected trustee has not accepted their invitation yet.");
                }
            }

            var fees = WalletFeeHelper.Calculate(
                targetAmount: request.TargetAmount,
                releaseAmount: request.TargetAmount,
                payoutDestination: PayoutDestination.Trustee.ToString(),
                releases: 1);

            var totalUpfrontCharge = request.TargetAmount + fees.TotalMovaCharges;

            await using var tx = await _unitOfWork.BeginAtomicAsync(cancellationToken);

            var balanceDebited = await _identityService.DebitBalanceAsync(
                request.UserPublicId,
                totalUpfrontCharge,
                cancellationToken);

            if (!balanceDebited)
            {
                op.Warning(
                    $"Insufficient balance. Required: {totalUpfrontCharge}, " +
                    $"Target: {request.TargetAmount}, Fees: {fees.TotalMovaCharges}");
                return new BaseResult<CreateTrusteeWalletResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Insufficient account balance.");
            }

            var targetMoney = Money.FromNaira(request.TargetAmount);

            var wallet = new Wallet
            {
                UserPublicId = request.UserPublicId,
                CategoryId = request.CategoryId,
                BankAccountId = request.BankAccountId,
                PayoutDestination = PayoutDestination.Trustee,
                Name = walletName,
                Description = string.IsNullOrWhiteSpace(request.Description)
                    ? null
                    : request.Description.Trim(),
                TargetAmount = targetMoney,
                FundedAmount = targetMoney,
                AvailableAmount = Money.FromNaira(0),
                LockedAmount = targetMoney,
                ResetAmount = Money.FromNaira(0),
                Status = WalletStatus.Active,
                Type = WalletType.Trustee,
                WalletTrusteeId = request.WalletTrusteeId,
                UtilityType = null,
                UtilityConfigJson = null,
            };

            await _unitOfWork.AddAsync(wallet, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            var walletTransaction = new Transaction
            {
                UserPublicId = request.UserPublicId,
                WalletId = wallet.Id,
                Title = "Trustee Wallet Created",
                Amount = targetMoney,
                Type = TransactionType.Deposit,
                Status = TransactionStatus.Completed,
                Reference = $"trustee-wallet-created:{wallet.Id}",
                CompletedAt = DateTimeOffset.UtcNow,
            };

            await _unitOfWork.AddAsync(walletTransaction, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            await _unitOfWork.AddAsync(
                new LedgerEntry
                {
                    WalletId = wallet.Id,
                    TransactionId = walletTransaction.Id,
                    Amount = targetMoney,
                    IsCredit = true,
                },
                cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            if (fees.TotalMovaCharges > 0)
            {
                var feeTransaction = new Transaction
                {
                    UserPublicId = request.UserPublicId,
                    WalletId = wallet.Id,
                    Title = "MOVA Fee",
                    Amount = Money.FromNaira(fees.TotalMovaCharges),
                    Type = TransactionType.Fee,
                    Status = TransactionStatus.Completed,
                    Reference = $"trustee-wallet-fee:{wallet.Id}",
                    CompletedAt = DateTimeOffset.UtcNow,
                };

                await _unitOfWork.AddAsync(feeTransaction, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }

            await tx.CommitAsync(cancellationToken);

            var userAfterDebit = await _identityService.GetByIdentifierAsync(
                request.UserPublicId,
                cancellationToken);

            var newMainBalance = userAfterDebit?.Balance.ToDecimal() ?? 0m;

            op.Success(
                $"Trustee wallet created. WalletId: {wallet.Id}, " +
                $"TrusteeId: {wallet.WalletTrusteeId?.ToString() ?? "none"}, " +
                $"Target: {request.TargetAmount}, " +
                $"Fees: {fees.TotalMovaCharges}, " +
                $"TotalCharged: {totalUpfrontCharge}, " +
                $"NewBalance: {newMainBalance}");

            var nextStepMessage = trusteeAlreadyLinked
                ? $"Your trustee is already linked and will need to approve every release " +
                  $"within a 3-hour window."
                : $"Next step: add the person you trust — they'll need to approve every " +
                  $"release within a 3-hour window.";

            _notificationQueue.InAppNotificationAsync(
                request.UserPublicId,
                NotificationType.Wallet,
                $"{walletName} trustee wallet created",
                $"Your {walletName} wallet is now active and funded with " +
                $"₦{request.TargetAmount:N0}. {nextStepMessage}",
                "/dashboard/wallets",
                null,
                CancellationToken.None);

            if (!string.IsNullOrWhiteSpace(request.Email))
            {
                var emailBody = trusteeAlreadyLinked
                    ? $"Your {walletName} trustee wallet has been created and funded with " +
                      $"₦{request.TargetAmount:N0}. Your trustee is already set — every " +
                      $"release request will need their approval within 3 hours. No money " +
                      $"moves without their approval."
                    : $"Your {walletName} trustee wallet has been created and funded with " +
                      $"₦{request.TargetAmount:N0}. To complete setup, add the person you " +
                      $"trust as your trustee. Once added, they'll need to approve every " +
                      $"release request within 3 hours — no money moves without their approval.";

                _notificationQueue.QueueNotificationEmail(
                    request.FirstName,
                    request.Email,
                    emailBody,
                    $"Your {walletName} trustee wallet is ready");
            }

            var successMessage = trusteeAlreadyLinked
                ? "Trustee wallet created and linked to your trustee."
                : "Trustee wallet created successfully. Add your trustee next.";

            return new BaseResult<CreateTrusteeWalletResponseDto>(
                HttpStatusCode.Created,
                successMessage,
                new CreateTrusteeWalletResponseDto
                {
                    WalletId = wallet.Id,
                    WalletName = wallet.Name,
                    FundedAmount = request.TargetAmount,
                    TotalCharges = fees.TotalMovaCharges,
                    NewMainBalance = newMainBalance,
                    WalletTrusteeId = wallet.WalletTrusteeId,
                });
        }
    }
}

// to do: send an notification email to the trustee when a wallet is created and linked to them, informing them of their responsibilities and the need for approval on releases.