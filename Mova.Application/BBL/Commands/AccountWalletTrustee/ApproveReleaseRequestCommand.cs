using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mova.Application.Helpers;
using Mova.Application.Interfaces.Identity;
using Mova.Application.Interfaces.Notification;
using Mova.Application.Interfaces.Persistence;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Domain.ValueObjects;
using Mova.Shared.Common;
using Mova.Shared.Logging;

namespace Mova.Application.BBL.Commands.AccountWalletTrustee;

public sealed class ApproveReleaseRequestCommand
{
    public sealed class Command : IRequest<BaseResult>
    {
        [Required, MinLength(20), MaxLength(100)]
        public string Token { get; set; } = string.Empty;

        [JsonIgnore] public string? IpAddress { get; set; }
    }

    public sealed class Handler : IRequestHandler<Command, BaseResult>
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IIdentityService _identityService;
        private readonly INotificationQueue _notificationQueue;
        private readonly ILogger<Handler> _logger;

        public Handler(
            IUnitOfWork unitOfWork,
            IIdentityService identityService,
            INotificationQueue notificationQueue,
            ILogger<Handler> logger)
        {
            _unitOfWork = unitOfWork;
            _identityService = identityService;
            _notificationQueue = notificationQueue;
            _logger = logger;
        }

        public async Task<BaseResult> Handle(Command request, CancellationToken cancellationToken)
        {
            using var op = OperationLogger.Start(_logger, "ApproveReleaseRequest");

            var tokenHash = TrusteeTokenHelper.Hash(request.Token);
            var now = DateTimeOffset.UtcNow;

            // Any early return below disposes the transaction without
            // committing → automatic rollback. Exceptions bubble to the
            // middleware which does the same.
            await using var tx = await _unitOfWork.BeginAtomicAsync(cancellationToken);

            // ── 1. Load the pending release request ──────────────────────
            var releaseRequest = await _unitOfWork.Query<ReleaseRequest>()
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.ApprovalTokenHash == tokenHash &&
                         x.Status == ReleaseRequestStatus.Pending &&
                         x.ExpiresAt > now,
                    cancellationToken);

            if (releaseRequest is null)
            {
                op.Warning("No matching pending request to approve.");
                return new BaseResult(
                    HttpStatusCode.Gone,
                    "This request has already been decided or has expired.");
            }

            // ── 2. Load wallet + validate ────────────────────────────────
            var wallet = await _unitOfWork.Query<Wallet>()
                .FirstOrDefaultAsync(
                    w => w.Id == releaseRequest.WalletId,
                    cancellationToken);

            if (wallet is null)
            {
                op.Error("Wallet not found.");
                return new BaseResult(
                    HttpStatusCode.InternalServerError,
                    "Wallet could not be found.");
            }

            if (wallet.Status != WalletStatus.Active)
            {
                op.Warning($"Wallet not active. Status: {wallet.Status}");
                return new BaseResult(
                    HttpStatusCode.BadRequest,
                    "This wallet is no longer active.");
            }

            if (wallet.BankAccountId is null)
            {
                op.Error("Wallet has no destination bank.");
                return new BaseResult(
                    HttpStatusCode.InternalServerError,
                    "Wallet has no destination bank account.");
            }

            // ── 3. Atomic status flip ────────────────────────────────────
            // WHERE Status = Pending guards against concurrent approvals.
            var rowsAffected = await _unitOfWork.Query<ReleaseRequest>()
                .Where(x =>
                    x.Id == releaseRequest.Id &&
                    x.Status == ReleaseRequestStatus.Pending)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(x => x.Status, ReleaseRequestStatus.Approved)
                        .SetProperty(x => x.DecidedAt, now)
                        .SetProperty(x => x.DecidedByTrusteeId, wallet.WalletTrusteeId)
                        .SetProperty(x => x.ApprovalTokenHash, (byte[]?)null)
                        .SetProperty(x => x.TokenUsedAt, now)
                        .SetProperty(x => x.TokenUsedFromIp, request.IpAddress),
                    cancellationToken);

            if (rowsAffected == 0)
            {
                op.Warning("Race lost — request already decided.");
                return new BaseResult(
                    HttpStatusCode.Gone,
                    "This request has already been decided.");
            }

            // ── 4. Move money in the wallet ──────────────────────────────
            // LockedAmount was already decremented at request time,
            // so we only increase TotalReleasedAmount here.
            wallet.TotalReleasedAmount = Money.FromMinorUnits(
                checked(wallet.TotalReleasedAmount.MinorUnits + releaseRequest.Amount.MinorUnits),
                wallet.TotalReleasedAmount.Currency);

            // ── 5. Create the payout ─────────────────────────────────────
            // ProcessPayoutsJob will pick this up within ~1 minute.
            var payout = new Payout
            {
                UserPublicId = releaseRequest.RequestedByUserPublicId,
                WalletId = wallet.Id,
                BankAccountId = wallet.BankAccountId.Value,
                Destination = PayoutDestination.Bank,
                Amount = releaseRequest.Amount,
                Fee = Money.FromNaira(0),
                NetAmount = releaseRequest.Amount,
                Reference = $"trustee-release:{releaseRequest.Id}",
                Provider = null,
                ProviderReference = null,
                FailedAttempts = 0,
                Status = PayoutStatus.Pending,
                InitiatedAt = now,
            };

            await _unitOfWork.AddAsync(payout, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // ── 6. Wallet transaction + ledger entry ─────────────────────
            var walletTxn = new Transaction
            {
                UserPublicId = releaseRequest.RequestedByUserPublicId,
                WalletId = wallet.Id,
                Title = "Trustee Release Approved",
                Amount = releaseRequest.Amount,
                Type = TransactionType.Withdrawal,
                Status = TransactionStatus.Completed,
                Reference = $"trustee-release:{releaseRequest.Id}",
                CompletedAt = now,
            };

            await _unitOfWork.AddAsync(walletTxn, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            await _unitOfWork.AddAsync(
                new LedgerEntry
                {
                    WalletId = wallet.Id,
                    TransactionId = walletTxn.Id,
                    Amount = releaseRequest.Amount,
                    IsCredit = false,
                },
                cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // ── 7. Commit ────────────────────────────────────────────────
            await tx.CommitAsync(cancellationToken);

            op.Success(
                $"Release approved. RequestId: {releaseRequest.Id}, " +
                $"PayoutId: {payout.Id}, Amount: {releaseRequest.Amount.ToDecimal()}");

            // ── 8. Notifications (outside transaction) ───────────────────
            var bank = await _unitOfWork.Query<BankAccount>()
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    b => b.Id == wallet.BankAccountId.Value,
                    cancellationToken);

            var bankName = bank?.BankName ?? "your bank";

            _notificationQueue.InAppNotificationAsync(
                releaseRequest.RequestedByUserPublicId,
                NotificationType.Wallet,
                "Release approved",
                $"Your trustee approved the release of " +
                $"₦{releaseRequest.Amount.ToDecimal():N0} from \"{wallet.Name}\". " +
                $"The money is on its way to {bankName}.",
                $"/dashboard/trustee/wallet/{wallet.Id}",
                null,
                CancellationToken.None);

            var owner = await _identityService.GetByIdentifierAsync(
                releaseRequest.RequestedByUserPublicId,
                cancellationToken);

            if (!string.IsNullOrWhiteSpace(owner?.Email))
            {
                _notificationQueue.QueueNotificationEmail(
                    owner.FirstName ?? string.Empty,
                    owner.Email,
                    $"Your trustee approved the release of " +
                    $"₦{releaseRequest.Amount.ToDecimal():N0} from your " +
                    $"\"{wallet.Name}\" wallet.\n\n" +
                    $"The money is on its way to {bankName}. You'll get another " +
                    $"email once the transfer completes.",
                    "Release approved");
            }

            return new BaseResult(
                HttpStatusCode.OK,
                "Release approved. The wallet owner has been notified.");
        }
    }
}