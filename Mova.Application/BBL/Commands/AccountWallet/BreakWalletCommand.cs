using System.Net;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Mova.Application.Interfaces.Identity;
using Mova.Application.Interfaces.Persistence;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Domain.ValueObjects;
using Mova.Shared.Common;

namespace Mova.Application.BBL.Commands.AccountWallet;

public sealed class BreakWalletCommand
{
    public sealed class Command : IRequest<BaseResult>
    {
        [JsonIgnore]
        public string UserPublicId { get; set; } = string.Empty;

        public long WalletId { get; set; }
    }

    public sealed class Handler : IRequestHandler<Command, BaseResult>
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

        public async Task<BaseResult> Handle(
            Command request,
            CancellationToken cancellationToken)
        {
            await _unitOfWork.BeginTransactionAsync(cancellationToken);

            var wallet = await _unitOfWork.Query<Wallet>()
                .FirstOrDefaultAsync(
                    x => x.Id == request.WalletId &&
                         x.UserPublicId == request.UserPublicId,
                    cancellationToken);

            if (wallet is null)
            {
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);

                return new BaseResult(
                    HttpStatusCode.NotFound,
                    "Wallet not found.");
            }

            if (wallet.Status != WalletStatus.Active)
            {
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);

                return new BaseResult(
                    HttpStatusCode.BadRequest,
                    "Only an active wallet can be broken.");
            }

            var amountToReturn =
                wallet.LockedAmount +
                wallet.AvailableAmount +
                wallet.UnusedAmount;

            if (amountToReturn.MinorUnits > 0)
            {
                var reference = $"wallet-break:{wallet.Id}";

                var alreadyProcessed = await _unitOfWork
                    .Query<Transaction>()
                    .AnyAsync(
                        x => x.Reference == reference,
                        cancellationToken);

                if (!alreadyProcessed && amountToReturn.MinorUnits > 0)
                {
                    var credited = await _identityService.UpdateBalanceAsync(
                    request.UserPublicId,
                    amountToReturn.ToDecimal(),
                    cancellationToken);

                    if (!credited)
                    {
                        await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                        return new BaseResult(HttpStatusCode.NotFound, "Unable to return wallet funds.");
                    }



                    var refundTransaction = new Transaction
                    {
                        WalletId = wallet.Id,
                        Title = "Wallet Broken",
                        Amount = amountToReturn,
                        Type = TransactionType.Refund,
                        Status = TransactionStatus.Completed,
                        Reference = reference,
                        CompletedAt = DateTimeOffset.UtcNow
                    };

                    await _unitOfWork.AddAsync(
                        refundTransaction,
                        cancellationToken);

                    await _unitOfWork.SaveChangesAsync(
                        cancellationToken);

                    var ledgerEntry = new LedgerEntry
                    {
                        WalletId = wallet.Id,
                        TransactionId = refundTransaction.Id,
                        Amount = amountToReturn,
                        IsCredit = false
                    };

                    await _unitOfWork.AddAsync(
                        ledgerEntry,
                        cancellationToken);
                }
            }

            var scheduledReleases = await _unitOfWork
                .Query<ScheduledRelease>()
                .Where(x =>
                    x.WalletId == wallet.Id &&
                    (x.Status == ReleaseStatus.Scheduled ||
                     x.Status == ReleaseStatus.Processing))
                .ToListAsync(cancellationToken);

            foreach (var scheduledRelease in scheduledReleases)
            {
                scheduledRelease.Status = ReleaseStatus.Cancelled;
            }

            wallet.LockedAmount = Money.FromNaira(0);
            wallet.AvailableAmount = Money.FromNaira(0);
            wallet.UnusedAmount = Money.FromNaira(0);
            wallet.Status = WalletStatus.Broken;

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            await _unitOfWork.CommitTransactionAsync(cancellationToken);

            return new BaseResult(
                HttpStatusCode.OK,
                "Wallet broken successfully.");
        }
    }
}