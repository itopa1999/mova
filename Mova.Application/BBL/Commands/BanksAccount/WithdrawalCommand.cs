using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Net;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mova.Application.Interfaces.Identity;
using Mova.Application.Interfaces.Notification;
using Mova.Application.Interfaces.Persistence;
using Mova.Application.Interfaces.Security;
using Mova.Application.Interfaces.Service;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Domain.ValueObjects;
using Mova.Shared.Common;
using Mova.Shared.Logging;
using DataAnnotationValidationResult = System.ComponentModel.DataAnnotations.ValidationResult;

namespace Mova.Application.BBL.Commands.BanksAccount;

public sealed class WithdrawalCommand
{
    public sealed class Command : IRequest<BaseResult<WithdrawalResponse>>, IValidatableObject
    {
        [JsonIgnore]
        public string UserPublicId { get; set; } = string.Empty;

        [Range(1, long.MaxValue)]
        public long WalletId { get; init; }

        [Range(typeof(decimal), "0.01", "92233720368547758.07")]
        public decimal Amount { get; init; }

        [Required]
        [RegularExpression(
            "(?i)^(bank|utilities)$",
            ErrorMessage = "Type must be either 'bank' or 'utilities'.")]
        public string Type { get; init; } = string.Empty;

        [Range(1, long.MaxValue)]
        public long? BankAccountId { get; init; }

        [MaxLength(30)]
        public string? UtilityType { get; init; }

        [MaxLength(50)]
        public string? Network { get; init; }

        [MaxLength(20)]
        public string? PhoneNumber { get; init; }

        [MaxLength(100)]
        public string? PlanCode { get; init; }

        [MaxLength(100)]
        public string? CableProvider { get; init; }

        [MaxLength(50)]
        public string? SmartcardNumber { get; init; }

        [MaxLength(100)]
        public string? PackageCode { get; init; }

        [MaxLength(100)]
        public string? Disco { get; init; }

        [MaxLength(50)]
        public string? MeterNumber { get; init; }

        [MaxLength(20)]
        public string? MeterType { get; init; }

        public IEnumerable<DataAnnotationValidationResult> Validate(
            ValidationContext validationContext)
        {
            if (string.Equals(Type, "bank", StringComparison.OrdinalIgnoreCase))
            {
                if (!BankAccountId.HasValue)
                {
                    yield return new DataAnnotationValidationResult(
                        "BankAccountId is required for bank withdrawals.",
                        [nameof(BankAccountId)]);
                }

                yield break;
            }

            if (!string.Equals(Type, "utilities", StringComparison.OrdinalIgnoreCase))
            {
                yield break;
            }

            if (string.IsNullOrWhiteSpace(UtilityType))
            {
                yield return new DataAnnotationValidationResult(
                    "UtilityType is required for utility withdrawals.",
                    [nameof(UtilityType)]);
                yield break;
            }

            switch (UtilityType.Trim().ToLowerInvariant())
            {
                case "airtime":
                    if (string.IsNullOrWhiteSpace(Network))
                        yield return RequiredField(nameof(Network), "Network");
                    if (string.IsNullOrWhiteSpace(PhoneNumber))
                        yield return RequiredField(nameof(PhoneNumber), "PhoneNumber");
                    break;
                case "data":
                    if (string.IsNullOrWhiteSpace(Network))
                        yield return RequiredField(nameof(Network), "Network");
                    if (string.IsNullOrWhiteSpace(PhoneNumber))
                        yield return RequiredField(nameof(PhoneNumber), "PhoneNumber");
                    if (string.IsNullOrWhiteSpace(PlanCode))
                        yield return RequiredField(nameof(PlanCode), "PlanCode");
                    break;
                case "cable":
                    if (string.IsNullOrWhiteSpace(CableProvider))
                        yield return RequiredField(nameof(CableProvider), "CableProvider");
                    if (string.IsNullOrWhiteSpace(SmartcardNumber))
                        yield return RequiredField(nameof(SmartcardNumber), "SmartcardNumber");
                    if (string.IsNullOrWhiteSpace(PackageCode))
                        yield return RequiredField(nameof(PackageCode), "PackageCode");
                    break;
                case "electricity":
                    if (string.IsNullOrWhiteSpace(Disco))
                        yield return RequiredField(nameof(Disco), "Disco");
                    if (string.IsNullOrWhiteSpace(MeterNumber))
                        yield return RequiredField(nameof(MeterNumber), "MeterNumber");
                    if (string.IsNullOrWhiteSpace(MeterType))
                        yield return RequiredField(nameof(MeterType), "MeterType");
                    break;
                default:
                    yield return new DataAnnotationValidationResult(
                        "UtilityType must be airtime, data, cable, or electricity.",
                        [nameof(UtilityType)]);
                    break;
            }
        }

        private static DataAnnotationValidationResult RequiredField(
            string memberName,
            string displayName) =>
            new($"{displayName} is required for this utility withdrawal.", [memberName]);
    }

    /// <summary>Identifies the local debit and its current external-payout status.</summary>
    public sealed record WithdrawalResponse(string Reference, string Status, bool Notification);

    public sealed class Handler(
        IUnitOfWork unitOfWork,
        IFeatureFlagService featureFlagService,
        IIdentityService identityService,
        INotificationQueue notificationQueue,
        IPinChallenge pinChallenge,
        ILogger<Handler> logger)
        : IRequestHandler<Command, BaseResult<WithdrawalResponse>>
    {
        public async Task<BaseResult<WithdrawalResponse>> Handle(
            Command request,
            CancellationToken cancellationToken)
        {
            using var op = OperationLogger.Start(
                logger,
                "Withdrawal",
                ("UserId", request.UserPublicId),
                ("WalletId", request.WalletId),
                ("Amount", request.Amount),
                ("Type", request.Type));

            if (string.IsNullOrWhiteSpace(request.UserPublicId))
            {
                op.Fail("UserPublicId is required.");
                return new BaseResult<WithdrawalResponse>(
                    HttpStatusCode.BadRequest,
                    "User identity is required.");
            }

            var permissions = await identityService.GetPermissionsAsync(
                request.UserPublicId,
                cancellationToken);

            if (permissions is null)
            {
                op.Fail($"Permissions unavailable for user {request.UserPublicId}.");
                return new BaseResult<WithdrawalResponse>(
                    HttpStatusCode.InternalServerError,
                    "We couldn't verify your account permissions right now. Please try again in a moment.");
            }

            if (!permissions.CanPerformSensitiveOperations)
            {
                op.Fail(
                    $"Sensitive-operation gate blocked withdrawal for user {request.UserPublicId}. " +
                    $"Status: {permissions.StatusLabel}, " +
                    $"Reason: {permissions.RestrictionReason}");

                var userMessage = string.IsNullOrWhiteSpace(permissions.StatusDescription)
                    ? "Your account is currently restricted from performing this operation. Please contact support."
                    : permissions.StatusDescription;

                return new BaseResult<WithdrawalResponse>(
                    HttpStatusCode.Locked,
                    userMessage);
            }

            if (!await featureFlagService.IsEnabledAsync(
                    FeatureFlagName.AllowWithdrawFunds,
                    cancellationToken))
            {
                op.Fail("Withdrawals feature flag is disabled.");
                return new BaseResult<WithdrawalResponse>(
                    HttpStatusCode.BadRequest,
                    "Withdrawals are currently unavailable.");
            }

            // ─── PIN challenge validation ───
            var isBankWithdrawal = string.Equals(
                request.Type,
                "bank",
                StringComparison.OrdinalIgnoreCase);

            var challengeType = isBankWithdrawal
                ? PinChallengeType.ManualTransferToBank
                : PinChallengeType.ManualPayUtilities;

            var (challengeValid, challengeMessage) = await pinChallenge.ValidateAsync(
                request.UserPublicId,
                challengeType,
                cancellationToken);

            if (!challengeValid)
            {
                op.Fail(
                    $"PIN challenge validation failed for {challengeType}: " +
                    $"{challengeMessage}");

                return new BaseResult<WithdrawalResponse>(
                    HttpStatusCode.BadRequest,
                    challengeMessage);
            }

            var amount = Money.FromNaira(request.Amount);
            await unitOfWork.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

            try
            {
                var wallet = await unitOfWork.Query<Wallet>()
                    .FirstOrDefaultAsync(
                        item => item.Id == request.WalletId &&
                                item.UserPublicId == request.UserPublicId,
                        cancellationToken);

                if (wallet is null)
                {
                    await unitOfWork.RollbackTransactionAsync(cancellationToken);
                    op.Fail($"Wallet {request.WalletId} not found for user {request.UserPublicId}.");

                    return new BaseResult<WithdrawalResponse>(
                        HttpStatusCode.NotFound,
                        "Wallet not found.");
                }

                if (wallet.AvailableAmount.MinorUnits < amount.MinorUnits)
                {
                    await unitOfWork.RollbackTransactionAsync(cancellationToken);
                    op.Fail(
                        $"Insufficient wallet balance. Available: {wallet.AvailableAmount.MinorUnits}, " +
                        $"Requested: {amount.MinorUnits}.");

                    return new BaseResult<WithdrawalResponse>(
                        HttpStatusCode.BadRequest,
                        "The wallet does not have enough available balance.");
                }

                var reference = $"WDL-{Guid.NewGuid():N}";
                wallet.AvailableAmount = Money.FromMinorUnits(
                    wallet.AvailableAmount.MinorUnits - amount.MinorUnits,
                    wallet.AvailableAmount.Currency);
                wallet.TotalWithdrawnAmount = Money.FromMinorUnits(
                    checked(wallet.TotalWithdrawnAmount.MinorUnits + amount.MinorUnits),
                    wallet.TotalWithdrawnAmount.Currency);

                var transaction = new Transaction
                {
                    UserPublicId = request.UserPublicId,
                    WalletId = wallet.Id,
                    Title = "Wallet withdrawal",
                    Amount = amount,
                    Type = TransactionType.Withdrawal,
                    Status = TransactionStatus.Processing,
                    Reference = reference
                };

                await unitOfWork.AddAsync(transaction, cancellationToken);
                await unitOfWork.SaveChangesAsync(cancellationToken);

                await unitOfWork.AddAsync(
                    new LedgerEntry
                    {
                        WalletId = wallet.Id,
                        TransactionId = transaction.Id,
                        Amount = amount,
                        IsCredit = false
                    },
                    cancellationToken);
                await unitOfWork.SaveChangesAsync(cancellationToken);
                await unitOfWork.CommitTransactionAsync(cancellationToken);

                op.Success(
                    $"Withdrawal debit recorded. WalletId: {wallet.Id}, " +
                    $"Reference: {reference}, Amount: {amount.MinorUnits} minor units.");

                await QueueWithdrawalNotificationsAsync(
                    request.UserPublicId,
                    wallet,
                    amount,
                    reference);

                return new BaseResult<WithdrawalResponse>(
                    HttpStatusCode.OK,
                    "Withdrawal debit recorded successfully. External payout processing will be added later.",
                    new WithdrawalResponse(reference, "processing", true));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await unitOfWork.RollbackTransactionAsync(CancellationToken.None);
                op.Fail("Withdrawal cancelled by caller.");
                throw;
            }
            catch (Exception exception)
            {
                await unitOfWork.RollbackTransactionAsync(CancellationToken.None);
                op.Fail(
                    $"Wallet withdrawal debit failed for wallet {request.WalletId} " +
                    $"and user {request.UserPublicId}: {exception.Message}",
                    exception);

                return new BaseResult<WithdrawalResponse>(
                    HttpStatusCode.InternalServerError,
                    "Unable to process the withdrawal right now. Please try again.");
            }
        }

        private async Task QueueWithdrawalNotificationsAsync(
            string userPublicId,
            Wallet wallet,
            Money amount,
            string reference)
        {
            var formattedAmount = $"{amount.ToDecimal():N2} {amount.Currency}";
            var notificationMessage =
                $"{formattedAmount} was deducted from your {wallet.Name} wallet. " +
                $"Withdrawal reference: {reference}. Status: processing.";

            try
            {
                notificationQueue.InAppNotificationAsync(
                    userPublicId,
                    NotificationType.Wallet,
                    "Withdrawal initiated",
                    notificationMessage,
                    "/transactions",
                    null,
                    CancellationToken.None);
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "In-app notification failed for withdrawal {Reference}.",
                    reference);
            }

            UserIdentityDto? user;
            try
            {
                user = await identityService.GetByIdentifierAsync(
                    userPublicId,
                    CancellationToken.None);
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Could not load user details for withdrawal email {Reference}.",
                    reference);
                return;
            }

            if (user is null || string.IsNullOrWhiteSpace(user.Email))
            {
                logger.LogWarning(
                    "Skipped withdrawal email {Reference}: user or email was not found.",
                    reference);
                return;
            }

            try
            {
                notificationQueue.QueueNotificationEmail(
                    user.FirstName,
                    user.Email,
                    $"{notificationMessage} Your withdrawal request has been recorded and is currently processing.",
                    "Your MOVA withdrawal has been initiated");
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Email queue failed for withdrawal {Reference}.",
                    reference);
            }
        }
    }
}