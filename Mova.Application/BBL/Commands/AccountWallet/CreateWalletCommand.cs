using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mova.Application.Interfaces.Identity;
using Mova.Application.Interfaces.Notification;
using Mova.Application.Interfaces.Persistence;
using Mova.Application.Interfaces.Security;
using Mova.Application.Interfaces.Service;
using Mova.Application.Validation;
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

        [Required, StringLength(150, MinimumLength = 1)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(2000)]
        public string? Description { get; set; }

        [Range(1, long.MaxValue)]
        public long CategoryId { get; set; }

        [Range(0, long.MaxValue)]
        public long BankAccountId { get; set; }

        [Range(typeof(decimal), "2000", "100000000")]
        public decimal TargetAmount { get; set; }

        [EnumDataType(typeof(ReleaseFrequency))]
        public ReleaseFrequency Frequency { get; set; }

        [Required, MaxLength(10000)]
        public string FrequencyConfig { get; set; } = string.Empty;

        [Range(typeof(decimal), "100", "100000000")]
        public decimal AmountToBeReleased { get; set; }

        [NotDefault]
        public DateTimeOffset StartDate { get; set; }

        [Required, RegularExpression(
            @"(?i)^\s*(bank|wallet|main|utilities)\s*$",
            ErrorMessage = "Payout destination must be bank, wallet, main, or utilities.")]
        public string PayoutDestination { get; set; } = "bank";

        public UtilityConfig? UtilityConfig { get; set; }
    }

    public sealed class CreateWalletResponseDto
    {
        public long WalletId { get; init; }
        public DateTimeOffset FirstReleaseDate { get; init; }
        public bool Notification { get; init; }
        public decimal NewMainBalance { get; init; }
    }

    public sealed class Handler
        : IRequestHandler<Command, BaseResult<CreateWalletResponseDto>>
    {
        private static readonly HashSet<string> AllowedNetworks =
            new(StringComparer.OrdinalIgnoreCase)
            { "mtn", "airtel", "glo", "9mobile" };

        private static readonly HashSet<string> AllowedCableProviders =
            new(StringComparer.OrdinalIgnoreCase)
            { "dstv", "gotv", "startimes" };

        private static readonly HashSet<string> AllowedMeterTypes =
            new(StringComparer.OrdinalIgnoreCase)
            { "prepaid", "postpaid" };

        private readonly IIdentityService _identityService;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ILogger<Handler> _logger;
        private readonly ISchedulePreviewService _schedulePreviewService;
        private readonly IWalletRuleService _walletRuleService;
        private readonly INotificationQueue _notificationQueue;
        private readonly IPinChallenge _pinChallenge;

        public Handler(
            IIdentityService identityService,
            IUnitOfWork unitOfWork,
            ILogger<Handler> logger,
            ISchedulePreviewService schedulePreviewService,
            IWalletRuleService walletRuleService,
            INotificationQueue notificationQueue,
            IPinChallenge pinChallenge)
        {
            _identityService = identityService;
            _unitOfWork = unitOfWork;
            _logger = logger;
            _schedulePreviewService = schedulePreviewService;
            _walletRuleService = walletRuleService;
            _notificationQueue = notificationQueue;
            _pinChallenge = pinChallenge;
        }

        public async Task<BaseResult<CreateWalletResponseDto>> Handle(
            Command request,
            CancellationToken cancellationToken)
        {
            using var op = OperationLogger.Start(
                _logger,
                "CreateWallet",
                ("UserId", request.UserPublicId));

            var permissions = await _identityService.GetPermissionsAsync(
                request.UserPublicId,
                cancellationToken);

            if (permissions is null)
            {
                op.Error($"Permissions unavailable for user {request.UserPublicId}.");
                return new BaseResult<CreateWalletResponseDto>(
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

                return new BaseResult<CreateWalletResponseDto>(
                    HttpStatusCode.Locked,
                    userMessage);
            }

            var (challengeValid, challengeMessage) = await _pinChallenge.ValidateAsync(
                request.UserPublicId,
                PinChallengeType.CreateWallet,
                cancellationToken);

            if (!challengeValid)
            {
                op.Warning($"PIN challenge validation failed: {challengeMessage}");
                return new BaseResult<CreateWalletResponseDto>(
                    HttpStatusCode.BadRequest,
                    challengeMessage);
            }

            if (string.IsNullOrWhiteSpace(request.Name))
            {
                op.Warning("Wallet name is required.");
                return new BaseResult<CreateWalletResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Wallet name is required.");
            }

            if (request.Name.Length > 150)
            {
                op.Warning("Wallet name is too long.");
                return new BaseResult<CreateWalletResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Wallet name cannot exceed 150 characters.");
            }

            var walletName = request.Name.Trim();

            if (request.TargetAmount < 2000)
            {
                op.Warning("Target amount must be at least ₦2,000.");
                return new BaseResult<CreateWalletResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Target amount must be at least ₦2,000.");
            }

            if (request.AmountToBeReleased < 100)
            {
                op.Warning("Release amount must be at least ₦100.");
                return new BaseResult<CreateWalletResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Release amount must be at least ₦100.");
            }

            if (request.AmountToBeReleased > request.TargetAmount)
            {
                op.Warning("Release amount cannot exceed target amount.");
                return new BaseResult<CreateWalletResponseDto>(
                    HttpStatusCode.BadRequest,
                    $"Release amount (₦{request.AmountToBeReleased:N0}) cannot exceed target amount (₦{request.TargetAmount:N0}).");
            }

            if (string.IsNullOrWhiteSpace(request.FrequencyConfig))
            {
                op.Warning("Frequency configuration is required.");
                return new BaseResult<CreateWalletResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Frequency configuration is required.");
            }

            var destination = request.PayoutDestination?
                .Trim()
                .ToLowerInvariant() switch
            {
                "bank" => PayoutDestination.Bank,
                "wallet" => PayoutDestination.Wallet,
                "main" => PayoutDestination.Main,
                "utilities" => PayoutDestination.Utilities,
                _ => (PayoutDestination?)null,
            };

            if (destination is null)
            {
                op.Warning($"Invalid payout destination: {request.PayoutDestination}");
                return new BaseResult<CreateWalletResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Invalid payout destination. Must be one of: bank, wallet, main, utilities.");
            }

            var goingToBank = destination == PayoutDestination.Bank;
            var goingToUtilities = destination == PayoutDestination.Utilities;

            UtilityType? parsedUtilityType = null;
            string? serializedUtilityConfig = null;

            if (goingToUtilities)
            {
                var validationError = ValidateUtilityConfig(
                    request.UtilityConfig,
                    out parsedUtilityType);

                if (validationError is not null)
                {
                    op.Warning($"Utility config invalid: {validationError}");
                    return new BaseResult<CreateWalletResponseDto>(
                        HttpStatusCode.BadRequest,
                        validationError);
                }

                serializedUtilityConfig = JsonSerializer.Serialize(
                    request.UtilityConfig,
                    new JsonSerializerOptions
                    {
                        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                    });
            }
            else if (request.UtilityConfig is not null)
            {
                op.Warning($"Utility config provided but destination is {destination}.");
                return new BaseResult<CreateWalletResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Utility config must not be provided for this payout destination.");
            }

            var normalizedFrequencyConfig =
                FrequencyConfigHelper.NormalizeConfigJson(request.FrequencyConfig);

            var existingWallet = await _unitOfWork.Query<Wallet>()
                .AsNoTracking()
                .FirstOrDefaultAsync(x =>
                    x.UserPublicId == request.UserPublicId &&
                    x.Name.ToLower() == walletName.ToLower() &&
                    x.Type == WalletType.Scheduled &&
                    x.Status == WalletStatus.Active,
                    cancellationToken);

            if (existingWallet is not null)
            {
                op.Warning("Wallet name already exists.");
                return new BaseResult<CreateWalletResponseDto>(
                    HttpStatusCode.BadRequest,
                    "A wallet with this name already exists.");
            }

            var categoryExists = await _unitOfWork.Query<WalletCategory>()
                .AsNoTracking()
                .AnyAsync(x => x.Id == request.CategoryId, cancellationToken);

            if (!categoryExists)
            {
                op.Warning("Invalid wallet category.");
                return new BaseResult<CreateWalletResponseDto>(
                    HttpStatusCode.BadRequest,
                    "The selected wallet category does not exist.");
            }

            if (goingToBank)
            {
                if (request.BankAccountId <= 0)
                {
                    op.Warning("Payout destination is Bank but no bank account provided.");
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

                if (bankAccount is null)
                {
                    op.Warning($"Bank account not found for user: {request.BankAccountId}");
                    return new BaseResult<CreateWalletResponseDto>(
                        HttpStatusCode.BadRequest,
                        "The selected bank account does not exist or does not belong to you.");
                }

                if (!bankAccount.ConsentGiven)
                {
                    op.Warning($"Consent not given for bank account: {request.BankAccountId}");
                    return new BaseResult<CreateWalletResponseDto>(
                        HttpStatusCode.BadRequest,
                        "You have not given consent for this bank account. Please provide consent first.");
                }

                if (bankAccount.Status != BankAccountStatus.Active)
                {
                    op.Warning(
                        $"Bank account is not active: {request.BankAccountId} - Status: {bankAccount.Status}");
                    return new BaseResult<CreateWalletResponseDto>(
                        HttpStatusCode.BadRequest,
                        "The selected bank account is not active. Please verify your bank account first.");
                }
            }
            else if (request.BankAccountId > 0)
            {
                op.Warning($"Payout destination is {destination} but a bank account was provided.");
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
                var errorMessage = previewResult.Errors.Any()
                    ? string.Join(" | ", previewResult.Errors)
                    : "Invalid schedule configuration.";

                op.Warning($"Schedule preview failed: {errorMessage}");
                return new BaseResult<CreateWalletResponseDto>(
                    HttpStatusCode.BadRequest,
                    errorMessage);
            }

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
                {
                    break;
                }

                finalEndDate = nextRelease.ScheduledFor;
                cursor = nextRelease.ScheduledFor;
            }

            if (finalEndDate is null)
            {
                op.Warning("Unable to calculate the final release date.");
                return new BaseResult<CreateWalletResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Unable to calculate the final release date.");
            }

            var targetMoney = Money.FromNaira(request.TargetAmount);
            var releaseMoney = Money.FromNaira(request.AmountToBeReleased);
            var releases = previewResult.TotalReleases;

            var fees = WalletFeeHelper.Calculate(
                targetAmount: request.TargetAmount,
                releaseAmount: request.AmountToBeReleased,
                payoutDestination: request.PayoutDestination ?? "bank",
                releases: releases);

            var totalUpfrontCharge = fees.TotalUpfrontCharge;

            await using var tx = await _unitOfWork.BeginAtomicAsync(cancellationToken);

            var balanceDebited = await _identityService.DebitBalanceAsync(
                request.UserPublicId,
                totalUpfrontCharge,
                cancellationToken);

            if (!balanceDebited)
            {
                op.Warning("Insufficient account balance or user account not found.");
                return new BaseResult<CreateWalletResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Insufficient account balance.");
            }

            var wallet = new Wallet
            {
                UserPublicId = request.UserPublicId,
                CategoryId = request.CategoryId,
                BankAccountId = goingToBank ? request.BankAccountId : null,
                PayoutDestination = destination.Value,
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
                Type = WalletType.Scheduled,
                UtilityType = goingToUtilities ? parsedUtilityType : null,
                UtilityConfigJson = goingToUtilities ? serializedUtilityConfig : null,
            };

            await _unitOfWork.AddAsync(wallet, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

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
                cancellationToken)
                ?? throw new InfrastructureException(
                    "Unable to generate the first wallet release schedule.");

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
            await tx.CommitAsync(cancellationToken);

            var firstReleaseDate = firstRelease.ScheduledFor;

            var userAfterDebit = await _identityService.GetByIdentifierAsync(
                request.UserPublicId,
                cancellationToken);

            var newMainBalance = userAfterDebit?.Balance.ToDecimal() ?? 0m;

            op.Success(
                $"Wallet created. WalletId: {wallet.Id}, " +
                $"FirstRelease: {firstReleaseDate:u}, " +
                $"NewBalance: {newMainBalance}");

            _notificationQueue.InAppNotificationAsync(
                request.UserPublicId,
                NotificationType.Wallet,
                $"{walletName} wallet created",
                BuildInAppMessage(walletName, firstReleaseDate, destination.Value, parsedUtilityType, request.UtilityConfig),
                "/dashboard/wallets",
                null,
                CancellationToken.None);

            if (!string.IsNullOrWhiteSpace(request.Email))
            {
                _notificationQueue.QueueNotificationEmail(
                    request.FirstName,
                    request.Email,
                    BuildEmailMessage(walletName, firstReleaseDate, destination.Value, parsedUtilityType, request.UtilityConfig),
                    $"Your {walletName} wallet is ready");
            }

            return new BaseResult<CreateWalletResponseDto>(
                HttpStatusCode.Created,
                "Wallet created successfully.",
                new CreateWalletResponseDto
                {
                    WalletId = wallet.Id,
                    FirstReleaseDate = firstReleaseDate,
                    Notification = true,
                    NewMainBalance = newMainBalance,
                });
        }

        private static string BuildInAppMessage(
            string walletName,
            DateTimeOffset firstReleaseDate,
            PayoutDestination destination,
            UtilityType? utilityType,
            UtilityConfig? utilityConfig)
            => $"Your {walletName} wallet is now active. " +
               $"First release scheduled for {firstReleaseDate:MMM d, yyyy}. " +
               BuildDestinationClause(destination, utilityType, utilityConfig);

        private static string BuildEmailMessage(
            string walletName,
            DateTimeOffset firstReleaseDate,
            PayoutDestination destination,
            UtilityType? utilityType,
            UtilityConfig? utilityConfig)
            => $"Your {walletName} wallet has been created and is now active. " +
               $"Your first release is scheduled for {firstReleaseDate:MMM d, yyyy}. " +
               BuildDestinationClause(destination, utilityType, utilityConfig) + " " +
               "MOVA will handle the schedule from here.";

        private static string BuildDestinationClause(
            PayoutDestination destination,
            UtilityType? utilityType,
            UtilityConfig? utilityConfig)
            => destination switch
            {
                PayoutDestination.Bank =>
                    "Releases will be sent to your linked bank account.",
                PayoutDestination.Wallet =>
                    "Releases will stay in your wallet available balance — pay bills or withdraw anytime.",
                PayoutDestination.Main =>
                    "Releases will be added to your main MOVA balance.",
                PayoutDestination.Utilities =>
                    BuildUtilityNotificationClause(utilityType, utilityConfig),
                _ => "Releases will be handled on schedule."
            };

        private static string BuildUtilityNotificationClause(
            UtilityType? utilityType,
            UtilityConfig? config)
        {
            if (utilityType is null || config is null)
                return "Releases will be used to pay your selected utility.";

            return utilityType switch
            {
                UtilityType.Airtime =>
                    $"Releases will top up {config.Network?.ToUpperInvariant()} airtime on {config.PhoneNumber}.",

                UtilityType.Data =>
                    $"Releases will buy {config.Network?.ToUpperInvariant()} data on {config.PhoneNumber}.",

                UtilityType.Cable =>
                    $"Releases will renew your {config.CableProvider} package.",

                UtilityType.Electricity =>
                    $"Releases will top up your {config.Disco} {config.MeterType} meter.",

                _ => "Releases will be used to pay your selected utility."
            };
        }

        private static string? ValidateUtilityConfig(
            UtilityConfig? config,
            out UtilityType? parsedType)
        {
            parsedType = null;

            if (config is null)
                return "Utility config is required when payout destination is utilities.";

            if (string.IsNullOrWhiteSpace(config.UtilityType))
                return "Utility type is required.";

            parsedType = config.UtilityType.Trim().ToLowerInvariant() switch
            {
                "airtime" => UtilityType.Airtime,
                "data" => UtilityType.Data,
                "cable" => UtilityType.Cable,
                "electricity" => UtilityType.Electricity,
                _ => null,
            };

            if (parsedType is null)
                return "Invalid utility type. Must be one of: airtime, data, cable, electricity.";

            switch (parsedType)
            {
                case UtilityType.Airtime:
                    if (string.IsNullOrWhiteSpace(config.Network))
                        return "Network is required for airtime.";
                    if (!AllowedNetworks.Contains(config.Network.Trim()))
                        return "Invalid network. Must be one of: mtn, airtel, glo, 9mobile.";
                    if (string.IsNullOrWhiteSpace(config.PhoneNumber))
                        return "Phone number is required for airtime.";
                    if (!IsValidPhone(config.PhoneNumber))
                        return "Invalid phone number. Must be 11 digits.";
                    break;

                case UtilityType.Data:
                    if (string.IsNullOrWhiteSpace(config.Network))
                        return "Network is required for data.";
                    if (!AllowedNetworks.Contains(config.Network.Trim()))
                        return "Invalid network. Must be one of: mtn, airtel, glo, 9mobile.";
                    if (string.IsNullOrWhiteSpace(config.PhoneNumber))
                        return "Phone number is required for data.";
                    if (!IsValidPhone(config.PhoneNumber))
                        return "Invalid phone number. Must be 11 digits.";
                    if (string.IsNullOrWhiteSpace(config.PlanCode))
                        return "Plan code is required for data.";
                    break;

                case UtilityType.Cable:
                    if (string.IsNullOrWhiteSpace(config.CableProvider))
                        return "Cable provider is required for cable.";
                    if (!AllowedCableProviders.Contains(config.CableProvider.Trim()))
                        return "Invalid cable provider. Must be one of: DSTV, GOtv, Startimes.";
                    if (string.IsNullOrWhiteSpace(config.SmartcardNumber))
                        return "Smartcard number is required for cable.";
                    if (config.SmartcardNumber.Trim().Length < 6)
                        return "Invalid smartcard number.";
                    if (string.IsNullOrWhiteSpace(config.PackageCode))
                        return "Package code is required for cable.";
                    break;

                case UtilityType.Electricity:
                    if (string.IsNullOrWhiteSpace(config.Disco))
                        return "Disco is required for electricity.";
                    if (string.IsNullOrWhiteSpace(config.MeterNumber))
                        return "Meter number is required for electricity.";
                    if (config.MeterNumber.Trim().Length < 6)
                        return "Invalid meter number.";
                    if (string.IsNullOrWhiteSpace(config.MeterType))
                        return "Meter type is required for electricity.";
                    if (!AllowedMeterTypes.Contains(config.MeterType.Trim()))
                        return "Invalid meter type. Must be prepaid or postpaid.";
                    break;
            }

            return null;
        }

        private static bool IsValidPhone(string phone)
        {
            var digits = new string(phone.Where(char.IsDigit).ToArray());
            return digits.Length == 11;
        }
    }
}