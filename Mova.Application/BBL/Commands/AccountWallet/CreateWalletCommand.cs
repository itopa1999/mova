using System.Net;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mova.Application.BBL.MovaAPIs;
using Mova.Application.Interfaces.Identity;
using Mova.Application.Interfaces.Notification;
using Mova.Application.Interfaces.Persistence;
using Mova.Application.Interfaces.Service;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Domain.ValueObjects;
using Mova.Shared.Common;
using Mova.Shared.Logging;

namespace Mova.Application.BBL.Commands.AccountWallet;

public sealed class CreateWalletCommand
{
    public sealed class Command : IRequest<BaseResult<CreateWalletResponseDto>>
    {
        [JsonIgnore]
        public string UserPublicId { get; set; } = string.Empty;

        [JsonIgnore]
        public string Email { get; set; } = string.Empty;

        [JsonIgnore]
        public string FirstName { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string? Description { get; set; }

        public long CategoryId { get; set; }

        public long BankAccountId { get; set; }

        public decimal TargetAmount { get; set; }

        public ReleaseFrequency Frequency { get; set; }

        public string FrequencyConfig { get; set; } = string.Empty;

        public decimal AmountToBeReleased { get; set; }

        public DateTimeOffset StartDate { get; set; }

        /// <summary>
        /// Where scheduled releases should go.
        /// "bank"   → sent straight to the linked bank account (BankAccountId required)
        /// "wallet" → kept in the wallet's available balance, withdrawable anytime
        /// "main"   → added to the user's main MOVA balance (non-withdrawable, spend-only)
        /// </summary>
        public string PayoutDestination { get; set; } = "bank";
    }

    public sealed class CreateWalletResponseDto
    {
        public long WalletId { get; init; }

        public DateTimeOffset FirstReleaseDate { get; init; }

        public bool Notification { get; init; }

        /// <summary>
        /// The user's main MOVA balance after the upfront debit.
        /// Authoritative — the FE uses this to sync its session balance.
        /// </summary>
        public decimal NewMainBalance { get; init; }
    }

    public sealed class Handler
        : IRequestHandler<Command, BaseResult<CreateWalletResponseDto>>
    {
        // ─── Fee constants ────────────────────────────────────
        private const decimal CreationFeePercent = 0.013m;   // 1.3%
        private const decimal CreationFeeFlat = 5m;           // +₦5

        private const decimal PayoutTier1Max = 5_000m;        // ≤ ₦5,000
        private const decimal PayoutTier2Max = 50_000m;       // ≤ ₦50,000
        private const decimal PayoutTier1Fee = 10m;
        private const decimal PayoutTier2Fee = 25m;
        private const decimal PayoutTier3Fee = 50m;

        private const decimal StampDutyThreshold = 10_000m;   // ≥ ₦10,000
        private const decimal StampDutyAmount = 50m;

        private readonly IIdentityService _identityService;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ILogger<Handler> _logger;
        private readonly ISchedulePreviewService _schedulePreviewService;
        private readonly IWalletRuleService _walletRuleService;
        private readonly INotificationQueue _notificationQueue;

        public Handler(
            IIdentityService identityService,
            IUnitOfWork unitOfWork,
            ILogger<Handler> logger,
            ISchedulePreviewService schedulePreviewService,
            IWalletRuleService walletRuleService,
            INotificationQueue notificationQueue)
        {
            _identityService = identityService;
            _unitOfWork = unitOfWork;
            _logger = logger;
            _schedulePreviewService = schedulePreviewService;
            _walletRuleService = walletRuleService;
            _notificationQueue = notificationQueue;
        }

        public async Task<BaseResult<CreateWalletResponseDto>> Handle(
            Command request,
            CancellationToken cancellationToken)
        {
            using var op = OperationLogger.Start(
                _logger,
                "CreateWallet",
                ("UserId", request.UserPublicId));

            if (string.IsNullOrWhiteSpace(request.Name))
            {
                op.Fail("Wallet name is required.");
                return new BaseResult<CreateWalletResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Wallet name is required.");
            }

            if (request.Name.Length > 150)
            {
                op.Fail("Wallet name is too long.");
                return new BaseResult<CreateWalletResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Wallet name cannot exceed 150 characters.");
            }

            var walletName = request.Name.Trim();

            if (request.TargetAmount < 2000)
            {
                op.Fail("Target amount must be at least ₦2,000.");
                return new BaseResult<CreateWalletResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Target amount must be at least ₦2,000.");
            }

            if (request.AmountToBeReleased < 100)
            {
                op.Fail("Release amount must be greater than zero.");
                return new BaseResult<CreateWalletResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Release amount must be at least ₦100");
            }

            if (request.AmountToBeReleased > request.TargetAmount)
            {
                op.Fail("Release amount cannot exceed target amount.");
                return new BaseResult<CreateWalletResponseDto>(
                    HttpStatusCode.BadRequest,
                    $"Release amount (₦{request.AmountToBeReleased:N0}) cannot exceed target amount (₦{request.TargetAmount:N0}).");
            }

            if (string.IsNullOrWhiteSpace(request.FrequencyConfig))
            {
                op.Fail("Frequency configuration is required.");
                return new BaseResult<CreateWalletResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Frequency configuration is required.");
            }

            // ─── Resolve payout destination ────────────────────────────
            var destination = request.PayoutDestination?
                .Trim()
                .ToLowerInvariant() switch
            {
                "bank" => PayoutDestination.Bank,
                "wallet" => PayoutDestination.Wallet,
                "main" => PayoutDestination.Main,
                _ => (PayoutDestination?)null,
            };

            if (destination is null)
            {
                op.Fail($"Invalid payout destination: {request.PayoutDestination}");
                return new BaseResult<CreateWalletResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Invalid payout destination. Must be one of: bank, wallet, main.");
            }

            var goingToBank = destination == PayoutDestination.Bank;

            var normalizedFrequencyConfig =
                FrequencyConfigHelper.NormalizeConfigJson(request.FrequencyConfig);

            var existingWallet = await _unitOfWork.Query<Wallet>()
                .AsNoTracking()
                .FirstOrDefaultAsync(x =>
                    x.UserPublicId == request.UserPublicId &&
                    x.Name.ToLower() == walletName.ToLower() &&
                    x.Status == WalletStatus.Active,
                    cancellationToken);

            if (existingWallet != null)
            {
                op.Fail("Wallet name already exists.");
                return new BaseResult<CreateWalletResponseDto>(
                    HttpStatusCode.BadRequest,
                    "A wallet with this name already exists.");
            }

            var categoryExists = await _unitOfWork.Query<WalletCategory>()
                .AsNoTracking()
                .AnyAsync(
                    x => x.Id == request.CategoryId,
                    cancellationToken);

            if (!categoryExists)
            {
                op.Fail("Invalid wallet category.");
                return new BaseResult<CreateWalletResponseDto>(
                    HttpStatusCode.BadRequest,
                    "The selected wallet category does not exist.");
            }

            // ─── Bank validation is driven by the payout destination ───
            if (goingToBank)
            {
                if (request.BankAccountId <= 0)
                {
                    op.Fail("Payout destination is Bank but no bank account was provided.");
                    return new BaseResult<CreateWalletResponseDto>(
                        HttpStatusCode.BadRequest,
                        "Please select a bank account.");
                }

                var bankAccount = await _unitOfWork.Query<BankAccount>()
                    .AsNoTracking()
                    .FirstOrDefaultAsync(
                        x => x.Id == request.BankAccountId &&
                             x.UserPublicId == request.UserPublicId,
                        cancellationToken);

                if (bankAccount == null)
                {
                    op.Fail($"Bank account not found for user: {request.BankAccountId}");
                    return new BaseResult<CreateWalletResponseDto>(
                        HttpStatusCode.BadRequest,
                        "The selected bank account does not exist or does not belong to you.");
                }

                if (!bankAccount.ConsentGiven)
                {
                    op.Fail($"Consent not given for bank account: {request.BankAccountId}");
                    return new BaseResult<CreateWalletResponseDto>(
                        HttpStatusCode.BadRequest,
                        "You have not given consent for this bank account. Please provide consent first.");
                }

                if (bankAccount.Status != BankAccountStatus.Active)
                {
                    op.Fail($"Bank account is not active: {request.BankAccountId} - Status: {bankAccount.Status}");
                    return new BaseResult<CreateWalletResponseDto>(
                        HttpStatusCode.BadRequest,
                        "The selected bank account is not active. Please verify your bank account first.");
                }
            }
            else if (request.BankAccountId > 0)
            {
                op.Fail($"Payout destination is {destination} but a bank account was provided.");
                return new BaseResult<CreateWalletResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Bank account must not be provided for this payout destination.");
            }

            var previewResult = await _schedulePreviewService.PreviewScheduleAsync(
                request.TargetAmount,
                request.AmountToBeReleased,
                request.Frequency,
                normalizedFrequencyConfig,
                request.StartDate,
                1,
                cancellationToken);

            if (!previewResult.IsSuccess)
            {
                op.Fail($"Schedule preview failed: {string.Join(", ", previewResult.Errors)}");

                var errorMessage = previewResult.Errors.Any()
                    ? string.Join(" | ", previewResult.Errors)
                    : "Invalid schedule configuration.";

                return new BaseResult<CreateWalletResponseDto>(
                    HttpStatusCode.BadRequest,
                    errorMessage);
            }

            // ─── Compute the release schedule end date ─────────────────
            var ruleForEndDate = new WalletRule
            {
                Amount = Money.FromNaira(request.AmountToBeReleased),
                Frequency = request.Frequency,
                FrequencyConfig = normalizedFrequencyConfig,
                StartDate = request.StartDate,
            };
            var cursor = request.StartDate.AddTicks(-1);
            DateTimeOffset? finalEndDate = null;

            for (var releaseNumber = 0; releaseNumber < previewResult.TotalReleases; releaseNumber++)
            {
                var nextRelease = await _walletRuleService.GetNextReleaseAsync(
                    ruleForEndDate,
                    cursor,
                    cancellationToken);

                if (nextRelease is null)
                    break;

                finalEndDate = nextRelease.ScheduledFor;
                cursor = nextRelease.ScheduledFor;
            }

            if (finalEndDate is null)
            {
                op.Fail("Unable to calculate the final release date.");
                return new BaseResult<CreateWalletResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Unable to calculate the final release date.");
            }

            var targetMoney = Money.FromNaira(request.TargetAmount);
            var releaseMoney = Money.FromNaira(request.AmountToBeReleased);

            // ─── Compute the MOVA fee ──────────────────────────────────
            // releases is authoritative from the preview service — it accounts
            // for fractional final releases correctly.
            var releases = previewResult.TotalReleases;

            var fees = CalculateFees(
                target: request.TargetAmount,
                release: request.AmountToBeReleased,
                destination: destination.Value,
                releases: releases);

            var totalUpfrontCharge = fees.TotalUpfrontCharge;

            op.Success(
                $"Fee computed — releases: {releases}, " +
                $"creation: {fees.CreationFee}, payout: {fees.TotalPayoutFee}, " +
                $"total charge: {totalUpfrontCharge}");

            long walletId = 0;
            DateTimeOffset firstReleaseDate = default;
            decimal newMainBalance = 0m;

            await _unitOfWork.BeginTransactionAsync(cancellationToken);

            try
            {
                // Debit target + MOVA fee in one shot.
                var balanceDebited = await _identityService.DebitBalanceAsync(
                    request.UserPublicId,
                    totalUpfrontCharge,
                    cancellationToken);

                if (!balanceDebited)
                {
                    await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                    op.Fail("Insufficient account balance or user account not found.");

                    return new BaseResult<CreateWalletResponseDto>(
                        HttpStatusCode.BadRequest,
                        "Insufficient account balance.");
                }

                // Only the target amount is locked in the wallet.
                var wallet = new Wallet
                {
                    UserPublicId = request.UserPublicId,
                    CategoryId = request.CategoryId,
                    BankAccountId = goingToBank
                        ? request.BankAccountId
                        : null,
                    PayoutDestination = destination.Value,
                    Name = walletName,
                    Description = string.IsNullOrWhiteSpace(request.Description)
                        ? null
                        : request.Description.Trim(),
                    TargetAmount = targetMoney,
                    FundedAmount = targetMoney,
                    AvailableAmount = Money.FromNaira(0),
                    LockedAmount = targetMoney,
                    UnusedAmount = Money.FromNaira(0),
                    Status = WalletStatus.Active,
                };

                await _unitOfWork.AddAsync(wallet, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                // ─── Deposit transaction (target locked into wallet) ───
                var walletTransaction = new Transaction
                {
                    UserPublicId = request.UserPublicId,
                    WalletId = wallet.Id,
                    Title = "Wallet Created",
                    Amount = targetMoney,
                    Type = TransactionType.Deposit,
                    Status = TransactionStatus.Completed,
                    Reference = $"wallet-created:{wallet.Id}",
                    CompletedAt = DateTimeOffset.UtcNow,
                };

                await _unitOfWork.AddAsync(walletTransaction, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                var ledgerEntry = new LedgerEntry
                {
                    WalletId = wallet.Id,
                    TransactionId = walletTransaction.Id,
                    Amount = targetMoney,
                    IsCredit = true,
                };

                await _unitOfWork.AddAsync(ledgerEntry, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                // ─── Fee transaction (MOVA Fee — one-time) ─────────────
                var feeTransaction = new Transaction
                {
                    UserPublicId = request.UserPublicId,
                    WalletId = wallet.Id,
                    Title = "MOVA Fee",
                    Amount = Money.FromNaira(fees.TotalMovaCharges),
                    Type = TransactionType.Fee,
                    Status = TransactionStatus.Completed,
                    Reference = $"wallet-fee:{wallet.Id}",
                    CompletedAt = DateTimeOffset.UtcNow,
                };

                await _unitOfWork.AddAsync(feeTransaction, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                // No ledger entry for the fee — money has left the user's
                // balance and does not belong to any wallet.

                var rule = new WalletRule
                {
                    WalletId = wallet.Id,
                    Amount = releaseMoney,
                    Frequency = request.Frequency,
                    FrequencyConfig = normalizedFrequencyConfig,
                    StartDate = request.StartDate,
                    EndDate = finalEndDate.Value,
                };

                await _unitOfWork.AddAsync(rule, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                var firstRelease = await _walletRuleService.GetNextReleaseAsync(
                    rule,
                    request.StartDate.AddTicks(-1),
                    cancellationToken);

                if (firstRelease is null)
                {
                    throw new InvalidOperationException(
                        "Unable to generate the first wallet release schedule.");
                }

                var firstReleaseAmount = Math.Min(
                    firstRelease.Amount.ToDecimal(),
                    request.TargetAmount);

                var scheduledRelease = new ScheduledRelease
                {
                    WalletId = wallet.Id,
                    WalletRuleId = rule.Id,
                    Amount = Money.FromNaira(firstReleaseAmount),
                    ScheduledFor = firstRelease.ScheduledFor,
                    Status = ReleaseStatus.Scheduled,
                    ReleasedAt = null,
                };

                await _unitOfWork.AddAsync(scheduledRelease, cancellationToken);

                await _unitOfWork.SaveChangesAsync(cancellationToken);
                await _unitOfWork.CommitTransactionAsync(cancellationToken);

                walletId = wallet.Id;
                firstReleaseDate = firstRelease.ScheduledFor;

                // Read the user's new balance inside the same transaction
                // context to return the authoritative post-debit value.
                var userAfterDebit = await _identityService.GetByIdentifierAsync(
                    request.UserPublicId,
                    cancellationToken);

                newMainBalance = userAfterDebit?.Balance.ToDecimal() ?? 0m;

                op.Success(
                    $"Wallet created. WalletId: {wallet.Id}, " +
                    $"FirstRelease: {firstRelease.ScheduledFor:u}, " +
                    $"NewBalance: {newMainBalance}");
            }
            catch (Exception ex)
            {
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);

                _logger.LogError(
                    ex,
                    "Error creating wallet for user {UserPublicId}.",
                    request.UserPublicId);

                op.Fail($"Error creating wallet: {ex.Message}");

                return new BaseResult<CreateWalletResponseDto>(
                    HttpStatusCode.InternalServerError,
                    "An error occurred while creating the wallet.");
            }

            try
            {
                await SendWalletCreatedNotificationsAsync(
                    request.UserPublicId,
                    request.Email,
                    request.FirstName,
                    walletId,
                    walletName,
                    firstReleaseDate,
                    destination.Value);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Notification block failed for created wallet {WalletId}.",
                    walletId);
            }

            return new BaseResult<CreateWalletResponseDto>(
                HttpStatusCode.Created,
                "Wallet created successfully.",
                new CreateWalletResponseDto
                {
                    WalletId = walletId,
                    FirstReleaseDate = firstReleaseDate,
                    Notification = true,
                    NewMainBalance = newMainBalance,
                });
        }

        // ─── Fee calculation ──────────────────────────────────────
        private static WalletFeeBreakdown CalculateFees(
            decimal target,
            decimal release,
            PayoutDestination destination,
            int releases)
        {
            var creationFee =
                Math.Floor(target * CreationFeePercent) + CreationFeeFlat;

            decimal payoutFeePerRelease = 0m;
            decimal totalPayoutFee = 0m;

            if (destination == PayoutDestination.Bank)
            {
                var baseFee = release <= PayoutTier1Max
                    ? PayoutTier1Fee
                    : release <= PayoutTier2Max
                        ? PayoutTier2Fee
                        : PayoutTier3Fee;

                var stampDuty = release >= StampDutyThreshold
                    ? StampDutyAmount
                    : 0m;

                payoutFeePerRelease = baseFee + stampDuty;
                totalPayoutFee = payoutFeePerRelease * releases;
            }

            var totalMovaCharges = creationFee + totalPayoutFee;
            var totalUpfrontCharge = target + totalMovaCharges;

            return new WalletFeeBreakdown
            {
                Releases = releases,
                CreationFee = creationFee,
                PayoutFeePerRelease = payoutFeePerRelease,
                TotalPayoutFee = totalPayoutFee,
                TotalMovaCharges = totalMovaCharges,
                TotalUpfrontCharge = totalUpfrontCharge,
            };
        }

        private sealed class WalletFeeBreakdown
        {
            public int Releases { get; init; }
            public decimal CreationFee { get; init; }
            public decimal PayoutFeePerRelease { get; init; }
            public decimal TotalPayoutFee { get; init; }
            public decimal TotalMovaCharges { get; init; }
            public decimal TotalUpfrontCharge { get; init; }
        }

        private async Task SendWalletCreatedNotificationsAsync(
            string userPublicId,
            string email,
            string firstName,
            long walletId,
            string walletName,
            DateTimeOffset firstReleaseDate,
            PayoutDestination payoutDestination)
        {
            var title = $"{walletName} wallet created";

            var destinationClause = payoutDestination switch
            {
                PayoutDestination.Bank =>
                    "Releases will be sent to your linked bank account.",
                PayoutDestination.Wallet =>
                    "Releases will stay in your wallet available balance — withdraw anytime.",
                PayoutDestination.Main =>
                    "Releases will be added to your main MOVA balance (spend-only, non-withdrawable).",
                _ => "Releases will be handled on schedule."
            };

            var inAppMessage =
                $"Your {walletName} wallet is now active. " +
                $"First release scheduled for {firstReleaseDate:MMM d, yyyy}. " +
                destinationClause;

            var emailSubject = $"Your {walletName} wallet is ready";

            var emailMessage =
                $"Your {walletName} wallet has been created and is now active. " +
                $"Your first release is scheduled for {firstReleaseDate:MMM d, yyyy}. " +
                destinationClause + " " +
                $"MOVA will handle the schedule from here.";

            try
            {
                _notificationQueue.InAppNotificationAsync(
                    userPublicId,
                    NotificationType.Wallet,
                    title,
                    inAppMessage,
                    "/wallets",
                    null,
                    CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "In-app notification failed for created wallet {WalletId}.",
                    walletId);
            }

            try
            {
                _notificationQueue.QueueNotificationEmail(
                    firstName,
                    email,
                    emailMessage,
                    emailSubject);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Email queue failed for created wallet {WalletId}.",
                    walletId);
            }
        }
    }
}