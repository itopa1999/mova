using System.Net;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mova.Application.BBL.MovaAPIs;
using Mova.Application.Interfaces.Persistence;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Domain.ValueObjects;
using Mova.Shared.Common;
using Mova.Shared.Logging;

namespace Mova.Application.BBL.Commands.AccountWallet;

public sealed class BreakWalletCommand
{
    public sealed class Command : IRequest<BaseResult<object>>
    {
        [JsonIgnore]
        public string UserPublicId { get; set; } = string.Empty;

        public long WalletId { get; set; }
    }

    public sealed class Handler : IRequestHandler<Command, BaseResult<object>>
    {
        private const decimal BreakFeePercentage = 0.02m;

        private readonly IUnitOfWork _unitOfWork;
        private readonly IMediator _mediator;
        private readonly ILogger<Handler> _logger;

        public Handler(
            IUnitOfWork unitOfWork,
            IMediator mediator,
            ILogger<Handler> logger)
        {
            _unitOfWork = unitOfWork;
            _mediator = mediator;
            _logger = logger;
        }

        public async Task<BaseResult<object>> Handle(
            Command request,
            CancellationToken cancellationToken)
        {
            using var op = OperationLogger.Start(
                _logger,
                "BreakWallet",
                ("UserId", request.UserPublicId),
                ("WalletId", request.WalletId));

            await _unitOfWork.BeginTransactionAsync(cancellationToken);

            try
            {
                var wallet = await _unitOfWork.Query<Wallet>()
                    .FirstOrDefaultAsync(
                        x => x.Id == request.WalletId &&
                             x.UserPublicId == request.UserPublicId,
                        cancellationToken);

                if (wallet is null)
                {
                    await _unitOfWork.RollbackTransactionAsync(cancellationToken);

                    op.Fail("Wallet not found.");

                    return new BaseResult<object>(
                        HttpStatusCode.NotFound,
                        "Wallet not found.");
                }

                if (wallet.Status != WalletStatus.Active)
                {
                    await _unitOfWork.RollbackTransactionAsync(cancellationToken);

                    op.Fail($"Wallet is not active. Current status: {wallet.Status}");

                    return new BaseResult<object>(
                        HttpStatusCode.BadRequest,
                        "Only an active wallet can be broken.");
                }

                var totalWalletAmount =
                    wallet.LockedAmount +
                    wallet.AvailableAmount +
                    wallet.UnusedAmount;

                if (totalWalletAmount.MinorUnits <= 0)
                {
                    await _unitOfWork.RollbackTransactionAsync(cancellationToken);

                    op.Fail("This wallet has no funds to return.");

                    return new BaseResult<object>(
                        HttpStatusCode.BadRequest,
                        "This wallet has no funds to return.");
                }

                var breakFee = Money.FromNaira(
                    Math.Round(
                        wallet.LockedAmount.ToDecimal() *
                        BreakFeePercentage,
                        2,
                        MidpointRounding.ToEven));

                var amountToReturn = totalWalletAmount - breakFee;

                if (amountToReturn.MinorUnits <= 0)
                {
                    await _unitOfWork.RollbackTransactionAsync(cancellationToken);

                    op.Fail(
                        $"Amount after break fee is invalid. " +
                        $"Total: {totalWalletAmount.ToDecimal():N2}, " +
                        $"Fee: {breakFee.ToDecimal():N2}");

                    return new BaseResult<object>(
                        HttpStatusCode.BadRequest,
                        "The amount available after the break fee is invalid.");
                }

                var reference = $"wallet-break:{wallet.Id}";

                var alreadyProcessed = await _unitOfWork
                    .Query<Transaction>()
                    .AnyAsync(
                        x => x.Reference == reference,
                        cancellationToken);

                if (alreadyProcessed)
                {
                    await _unitOfWork.RollbackTransactionAsync(cancellationToken);

                    op.Fail($"Wallet break already processed. Reference: {reference}");

                    return new BaseResult<object>(
                        HttpStatusCode.Conflict,
                        "This wallet break has already been processed.");
                }

                // Cancel all pending scheduled releases
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

                op.Fail(
                    $"Cancelled {scheduledReleases.Count} pending scheduled release(s).");

                // Break transaction — money going back to user
                var breakTransaction = new Transaction
                {
                    UserPublicId = request.UserPublicId,
                    WalletId = wallet.Id,
                    Title = "Wallet Broken",
                    Amount = amountToReturn,
                    Type = TransactionType.Refund,
                    Status = TransactionStatus.Processing,
                    Reference = reference,
                };

                await _unitOfWork.AddAsync(breakTransaction, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                // Ledger entry for the returned amount
                var returnLedgerEntry = new LedgerEntry
                {
                    WalletId = wallet.Id,
                    TransactionId = breakTransaction.Id,
                    Amount = amountToReturn,
                    IsCredit = false,
                };

                await _unitOfWork.AddAsync(returnLedgerEntry, cancellationToken);

                // Fee transaction
                var feeTransaction = new Transaction
                {
                    UserPublicId = request.UserPublicId,
                    WalletId = wallet.Id,
                    Title = "Wallet Break Fee",
                    Amount = breakFee,
                    Type = TransactionType.Fee,
                    Status = TransactionStatus.Completed,
                    Reference = $"wallet-break-fee:{wallet.Id}",
                    CompletedAt = DateTimeOffset.UtcNow,
                };

                await _unitOfWork.AddAsync(feeTransaction, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                // Ledger entry for the fee
                var feeLedgerEntry = new LedgerEntry
                {
                    WalletId = wallet.Id,
                    TransactionId = feeTransaction.Id,
                    Amount = breakFee,
                    IsCredit = false,
                };

                await _unitOfWork.AddAsync(feeLedgerEntry, cancellationToken);

                // Zero out the wallet
                wallet.LockedAmount = Money.FromNaira(0);
                wallet.AvailableAmount = Money.FromNaira(0);
                wallet.UnusedAmount = Money.FromNaira(0);
                wallet.Status = WalletStatus.Broken;

                await _unitOfWork.SaveChangesAsync(cancellationToken);
                await _unitOfWork.CommitTransactionAsync(cancellationToken);

                await _mediator.Send(
                    new CreateNotificationCommand.Command
                    {
                        UserPublicId = request.UserPublicId,
                        Type = NotificationType.Wallet,
                        Title = $"{wallet.Name} wallet broken",
                        Message =
                            $"₦{amountToReturn.ToDecimal():N0} will be deposited into " +
                            $"your linked bank account within few hours.",
                        ActionUrl = $"/wallet/{wallet.Id}",
                    },
                    cancellationToken);

                op.Success(
                    $"Wallet broken successfully. " +
                    $"Amount returned: ₦{amountToReturn.ToDecimal():N2}, " +
                    $"Break fee: ₦{breakFee.ToDecimal():N2}");

                return new BaseResult<object>(
                    HttpStatusCode.OK,
                    $"Wallet broken successfully. " +
                    $"₦{amountToReturn.ToDecimal():N2} is being returned " +
                    $"to your linked bank account.",
                    new
                    {
                        notification = true,
                    });
            }
            catch (Exception ex)
            {
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);

                op.Fail(
                    $"Error breaking wallet. WalletId: {request.WalletId}",
                    ex);

                throw;
            }
        }
    }
}