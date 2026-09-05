using System.Net;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mova.Application.Interfaces.Persistence;
using Mova.Application.Interfaces.Service;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Domain.ValueObjects;
using Mova.Shared.Common;
using Mova.Shared.Logging;

namespace Mova.Application.BBL.Commands.AccountWallet;

public sealed class RelockUnusedFundsCommand
{
    public sealed class Command : IRequest<BaseResult>
    {
        [JsonIgnore]
        public string UserPublicId { get; set; } = string.Empty;

        public long WalletId { get; init; }
    }

    public sealed class Handler : IRequestHandler<Command, BaseResult>
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly ILogger<Handler> _logger;
        private readonly IWalletRuleService _walletRuleService;

        public Handler(
            IUnitOfWork unitOfWork,
            ILogger<Handler> logger,
            IWalletRuleService walletRuleService)
        {
            _unitOfWork = unitOfWork;
            _logger = logger;
            _walletRuleService = walletRuleService;
        }

        public async Task<BaseResult> Handle(
            Command request,
            CancellationToken cancellationToken)
        {
            using var op = OperationLogger.Start(
                _logger,
                "RelockUnusedWalletFunds",
                ("WalletId", request.WalletId),
                ("UserId", request.UserPublicId));

            var wallet = await _unitOfWork.Query<Wallet>()
                .FirstOrDefaultAsync(
                    x => x.Id == request.WalletId
                         && x.UserPublicId == request.UserPublicId,
                    cancellationToken);

            if (wallet is null)
            {
                op.Fail("Wallet not found.");
                return new BaseResult(
                    HttpStatusCode.NotFound,
                    "Wallet not found.");
            }

            if (wallet.UnusedAmount.MinorUnits <= 0)
            {
                op.Fail("Wallet has no unused funds.");
                return new BaseResult(
                    HttpStatusCode.BadRequest,
                    "Wallet has no unused funds to move.");
            }

            var amountToRelock = wallet.UnusedAmount;

            if (wallet.TotalReleasedAmount.MinorUnits < amountToRelock.MinorUnits)
            {
                op.Fail("Wallet balance records are inconsistent.");
                return new BaseResult(
                    HttpStatusCode.Conflict,
                    "Wallet balance could not be reconciled.");
            }

            await _unitOfWork.BeginTransactionAsync(cancellationToken);

            try
            {
                wallet.LockedAmount += amountToRelock;
                wallet.UnusedAmount = Money.FromNaira(0);
                wallet.TotalReleasedAmount -= amountToRelock;

                var hasPendingRelease = await _unitOfWork.Query<ScheduledRelease>()
                    .AnyAsync(
                        x => x.WalletId == wallet.Id
                             && (x.Status == ReleaseStatus.Scheduled
                                 || x.Status == ReleaseStatus.Processing),
                        cancellationToken);

                if (!hasPendingRelease && wallet.LockedAmount.MinorUnits > 0)
                {
                    var walletRule = await _unitOfWork.Query<WalletRule>()
                        .FirstOrDefaultAsync(x => x.WalletId == wallet.Id, cancellationToken);

                    if (walletRule is not null)
                    {
                        var nextRelease = await _walletRuleService.GetNextReleaseAsync(
                            walletRule,
                            DateTimeOffset.UtcNow,
                            cancellationToken);

                        if (nextRelease is not null)
                        {
                            var nextAmount = nextRelease.Amount.MinorUnits > wallet.LockedAmount.MinorUnits
                                ? wallet.LockedAmount
                                : nextRelease.Amount;

                            await _unitOfWork.AddAsync(
                                new ScheduledRelease
                                {
                                    WalletId = wallet.Id,
                                    WalletRuleId = walletRule.Id,
                                    Amount = nextAmount,
                                    ScheduledFor = nextRelease.ScheduledFor,
                                    Status = ReleaseStatus.Scheduled
                                },
                                cancellationToken);
                        }
                    }
                }

                await _unitOfWork.SaveChangesAsync(cancellationToken);
                await _unitOfWork.CommitTransactionAsync(cancellationToken);

                op.Success("Unused funds moved back to locked funds.");

                return new BaseResult(
                    HttpStatusCode.OK,
                    "Unused funds moved back to locked funds.");
            }
            catch (Exception exception)
            {
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                op.Fail("Failed to move unused funds back to locked funds.", exception);

                return new BaseResult(
                    HttpStatusCode.InternalServerError,
                    "An error occurred while updating wallet funds.");
            }
        }
    }
}
