using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Mova.Application.Helpers;
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

public sealed class RequestReleaseCommand
{
    public sealed class Command : IRequest<BaseResult<RequestReleaseResponseDto>>
    {
        [JsonIgnore]
        public string UserPublicId { get; set; } = string.Empty;

        [JsonIgnore]
        public string Email { get; set; } = string.Empty;

        [JsonIgnore]
        public string FirstName { get; set; } = string.Empty;
        public long WalletId { get; set; }

        [Range(100, 100_000_000)]
        public decimal Amount { get; set; }

        [MaxLength(500)]
        public string? UserNote { get; set; }
    }

    public sealed class RequestReleaseResponseDto
    {
        public long RequestId { get; init; }
        public decimal Amount { get; init; }
        public string TrusteeEmail { get; init; } = string.Empty;
        public string? TrusteeName { get; init; }
        public DateTimeOffset ExpiresAt { get; init; }
    }

    public sealed class Handler
        : IRequestHandler<Command, BaseResult<RequestReleaseResponseDto>>
    {
        private readonly IIdentityService _identityService;
        private readonly IUnitOfWork _unitOfWork;
        private readonly INotificationQueue _notificationQueue;
        private readonly IPinChallenge _pinChallenge;
        private readonly TrusteeOptions _trusteeOptions;
        private readonly ILogger<Handler> _logger;

        public Handler(
            IIdentityService identityService,
            IUnitOfWork unitOfWork,
            INotificationQueue notificationQueue,
            IPinChallenge pinChallenge,
            IOptions<TrusteeOptions> trusteeOptions,
            ILogger<Handler> logger)
        {
            _identityService = identityService;
            _unitOfWork = unitOfWork;
            _notificationQueue = notificationQueue;
            _pinChallenge = pinChallenge;
            _trusteeOptions = trusteeOptions.Value;
            _logger = logger;
        }

        public async Task<BaseResult<RequestReleaseResponseDto>> Handle(
            Command request,
            CancellationToken cancellationToken)
        {
            using var op = OperationLogger.Start(
                _logger,
                "RequestRelease",
                ("UserId", request.UserPublicId),
                ("WalletId", request.WalletId));

            // 1. PIN challenge
            var (pinOk, pinMsg) = await _pinChallenge.ValidateAsync(
                request.UserPublicId,
                PinChallengeType.RequestRelease,
                cancellationToken);

            if (!pinOk)
            {
                op.Warning($"PIN failed: {pinMsg}");
                return new BaseResult<RequestReleaseResponseDto>(
                    HttpStatusCode.BadRequest,
                    pinMsg);
            }

            // 2. Load wallet
            var wallet = await _unitOfWork.Query<Wallet>()
                .FirstOrDefaultAsync(
                    w => w.Id == request.WalletId &&
                         w.UserPublicId == request.UserPublicId,
                    cancellationToken);

            if (wallet is null)
            {
                return new BaseResult<RequestReleaseResponseDto>(
                    HttpStatusCode.NotFound,
                    "Wallet not found.");
            }

            if (wallet.Type != WalletType.Trustee)
            {
                return new BaseResult<RequestReleaseResponseDto>(
                    HttpStatusCode.BadRequest,
                    "This is not a trustee wallet.");
            }

            if (wallet.Status != WalletStatus.Active)
            {
                return new BaseResult<RequestReleaseResponseDto>(
                    HttpStatusCode.BadRequest,
                    "This wallet is not active.");
            }

            if (wallet.WalletTrusteeId is null)
            {
                return new BaseResult<RequestReleaseResponseDto>(
                    HttpStatusCode.BadRequest,
                    "This wallet has no trustee assigned.");
            }

            if (wallet.BankAccountId is null)
            {
                return new BaseResult<RequestReleaseResponseDto>(
                    HttpStatusCode.BadRequest,
                    "This wallet has no destination bank account.");
            }

            // 3. Load trustee + bank
            var trustee = await _unitOfWork.Query<WalletTrustee>()
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    t => t.Id == wallet.WalletTrusteeId.Value,
                    cancellationToken);

            if (trustee is null || trustee.Status != WalletTrusteeStatus.Verified)
            {
                return new BaseResult<RequestReleaseResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Your trustee has not verified their email yet.");
            }

            var bank = await _unitOfWork.Query<BankAccount>()
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    b => b.Id == wallet.BankAccountId.Value &&
                         b.UserPublicId == request.UserPublicId,
                    cancellationToken);

            if (bank is null || bank.Status != BankAccountStatus.Active)
            {
                return new BaseResult<RequestReleaseResponseDto>(
                    HttpStatusCode.BadRequest,
                    "The wallet's linked bank account is not active.");
            }

            var now = DateTimeOffset.UtcNow;

            // 4. No pending request for this wallet
            var hasPending = await _unitOfWork.Query<ReleaseRequest>()
                .AsNoTracking()
                .AnyAsync(
                    r => r.WalletId == wallet.Id &&
                         r.Status == ReleaseRequestStatus.Pending,
                    cancellationToken);

            if (hasPending)
            {
                return new BaseResult<RequestReleaseResponseDto>(
                    HttpStatusCode.Conflict,
                    "A release request is already pending for this wallet.");
            }

            // 5. Cooldown after an expired request
            var cooldownThreshold = now.AddHours(-_trusteeOptions.ReleaseRequestExpiryHours);
            var recentExpired = await _unitOfWork.Query<ReleaseRequest>()
                .AsNoTracking()
                .AnyAsync(
                    r => r.WalletId == wallet.Id &&
                         r.Status == ReleaseRequestStatus.Expired &&
                         r.ExpiresAt > cooldownThreshold,
                    cancellationToken);

            if (recentExpired)
            {
                return new BaseResult<RequestReleaseResponseDto>(
                    HttpStatusCode.BadRequest,
                    $"Please wait {_trusteeOptions.ReleaseRequestExpiryHours} hours " +
                    $"after an expired request before trying again.");
            }

            // 6. Validate amount against locked balance
            if (request.Amount < 100)
            {
                return new BaseResult<RequestReleaseResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Minimum release amount is ₦100.");
            }

            if (request.Amount > wallet.LockedAmount.ToDecimal())
            {
                return new BaseResult<RequestReleaseResponseDto>(
                    HttpStatusCode.BadRequest,
                    $"Amount exceeds locked balance ({wallet.LockedAmount.ToDecimal():N0}).");
            }

            // 7. Build + save
            var rawToken = TrusteeTokenHelper.Generate();
            var tokenHash = TrusteeTokenHelper.Hash(rawToken);
            var expiresAt = now.AddHours(_trusteeOptions.ReleaseRequestExpiryHours);
            var amountMoney = Money.FromNaira(request.Amount);

            await using var tx = await _unitOfWork.BeginAtomicAsync(cancellationToken);

            var releaseRequest = new ReleaseRequest
            {
                WalletId = wallet.Id,
                RequestedByUserPublicId = request.UserPublicId,
                Amount = amountMoney,
                Destination = PayoutDestination.Bank,
                DestinationBankAccountId = wallet.BankAccountId.Value,
                Status = ReleaseRequestStatus.Pending,
                UserNote = string.IsNullOrWhiteSpace(request.UserNote)
                    ? null
                    : request.UserNote.Trim(),
                RequestedAt = now,
                ExpiresAt = expiresAt,
                ApprovalTokenHash = tokenHash,
            };

            // var payout = new Payout
            // {
            //     UserPublicId = wallet.UserPublicId,
            //     WalletId = wallet.Id,
            //     BankAccountId = wallet.BankAccountId,
            //     Destination = wallet.PayoutDestination,
            //     Amount = amountMoney,
            //     Fee = Money.FromNaira(0),
            //     NetAmount = amountMoney,
            //     Reference = $"trustee-release:{releaseRequest.Id}",
            //     Provider = null,
            //     ProviderReference = null,
            //     FailedAttempts = 0,
            //     Status = PayoutStatus.Pending,
            //     InitiatedAt = DateTimeOffset.UtcNow,
            // };

            await _unitOfWork.AddAsync(releaseRequest, cancellationToken);

            // Reserve funds: LockedAmount -= Amount
            wallet.LockedAmount = Money.FromMinorUnits(
                checked(wallet.LockedAmount.MinorUnits - amountMoney.MinorUnits),
                wallet.LockedAmount.Currency);

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);

            // 8. Notify + send email
            var displayName = request.FirstName ?? "The wallet owner";
            var trusteeDisplay = string.IsNullOrWhiteSpace(trustee.Name)
                ? trustee.Email
                : trustee.Name;

            _notificationQueue.InAppNotificationAsync(
                request.UserPublicId,
                NotificationType.Wallet,
                "Release requested",
                $"Your release request of ₦{request.Amount:N0} from \"{wallet.Name}\" " +
                $"has been sent to {trusteeDisplay}. They have " +
                $"{_trusteeOptions.ReleaseRequestExpiryHours} hours to respond.",
                $"/dashboard/trustee/wallet/{wallet.Id}",
                null,
                CancellationToken.None);

            var maskedAccount = MaskAccount(bank.AccountNumber);
            var approvalUrl = $"{_trusteeOptions.ReleaseConsentBaseUrl.TrimEnd('/')}/{rawToken}";

            var noteClause = string.IsNullOrWhiteSpace(releaseRequest.UserNote)
                ? string.Empty
                : $"\nReason: \"{releaseRequest.UserNote}\"\n";

            _notificationQueue.QueueNotificationEmail(
                trusteeDisplay,
                trustee.Email,
                $"{displayName} has requested a release from their \"{wallet.Name}\" " +
                $"trustee wallet.\n\n" +
                $"Amount: ₦{request.Amount:N0}\n" +
                $"Destination: {bank.BankName} • {maskedAccount}\n" +
                noteClause +
                $"\nApprove or reject:\n{approvalUrl}\n\n" +
                $"This request expires in {_trusteeOptions.ReleaseRequestExpiryHours} " +
                $"hours. If you do nothing, the request will expire automatically and " +
                $"no money will move.",
                $"Release request from {displayName}");

            op.Success(
                $"Release requested. RequestId: {releaseRequest.Id}, " +
                $"Amount: {request.Amount}, TrusteeEmail: {trustee.Email}");

            return new BaseResult<RequestReleaseResponseDto>(
                HttpStatusCode.Created,
                "Release request sent. Your trustee will be notified.",
                new RequestReleaseResponseDto
                {
                    RequestId = releaseRequest.Id,
                    Amount = request.Amount,
                    TrusteeEmail = trustee.Email,
                    TrusteeName = trustee.Name,
                    ExpiresAt = expiresAt,
                });
        }

        private static string MaskAccount(string number)
        {
            if (string.IsNullOrEmpty(number) || number.Length <= 4) return "••••";
            return $"••••{number[^4..]}";
        }
    }
}