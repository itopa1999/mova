using System.Net;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.Extensions.Logging;
using Mova.Application.Interfaces.Identity;
using Mova.Application.Interfaces.Persistence;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Domain.ValueObjects;
using Mova.Shared.Common;
using Mova.Shared.Logging;

namespace Mova.Application.BBL.Commands.AccountWallet;

/// <summary>Credits a user's main account and records the corresponding deposit.</summary>
public sealed class AddFundsCommand
{
    public sealed class Command : IRequest<BaseResult<AddFundsCommandResponseDto>>
    {
        [JsonIgnore]
        public string UserPublicId { get; set; } = string.Empty;

        public decimal Amount { get; init; }
    }

    public sealed class AddFundsCommandResponseDto
    {
        [JsonPropertyName("transaction_id")]
        public long TransactionId { get; init; }

        [JsonPropertyName("amount")]
        public decimal Amount { get; init; }

        [JsonPropertyName("reference")]
        public string Reference { get; init; } = string.Empty;
    }

    public sealed class Handler : IRequestHandler<Command, BaseResult<AddFundsCommandResponseDto>>
    {
        private const decimal MaximumFundingAmount = 100_000_000m;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IIdentityService _identityService;
        private readonly ILogger<Handler> _logger;

        public Handler(
            IUnitOfWork unitOfWork,
            IIdentityService identityService,
            ILogger<Handler> logger)
        {
            _unitOfWork = unitOfWork;
            _identityService = identityService;
            _logger = logger;
        }

        public async Task<BaseResult<AddFundsCommandResponseDto>> Handle(Command request, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(request.UserPublicId))
            {
                return new BaseResult<AddFundsCommandResponseDto>(HttpStatusCode.BadRequest, "User ID is required.");
            }

            if (request.Amount <= 0 || request.Amount > MaximumFundingAmount || Math.Round(request.Amount, 2) != request.Amount)
            {
                return new BaseResult<AddFundsCommandResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Amount must be greater than zero, no more than ₦100,000,000, and have at most two decimal places.");
            }

            using var op = OperationLogger.Start(
                _logger,
                "AddFunds",
                ("UserPublicId", request.UserPublicId),
                ("Amount", request.Amount));

            await _unitOfWork.BeginTransactionAsync(cancellationToken);

            try
            {
                var credited = await _identityService.UpdateBalanceAsync(
                    request.UserPublicId,
                    request.Amount,
                    cancellationToken);

                if (!credited)
                {
                    await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                    return new BaseResult<AddFundsCommandResponseDto>(HttpStatusCode.NotFound, "User account not found.");
                }

                var reference = $"manual-fund:{Guid.NewGuid():N}";
                var amount = Money.FromNaira(request.Amount);
                var transaction = new Transaction
                {
                    WalletId = null,
                    Title = "Manual Wallet Funding",
                    Amount = amount,
                    Type = TransactionType.Deposit,
                    Status = TransactionStatus.Completed,
                    Reference = reference,
                    CompletedAt = DateTimeOffset.UtcNow
                };

                await _unitOfWork.AddAsync(transaction, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                await _unitOfWork.AddAsync(
                    new LedgerEntry
                    {
                        TransactionId = transaction.Id,
                        Amount = amount,
                        IsCredit = true
                    },
                    cancellationToken);

                await _unitOfWork.SaveChangesAsync(cancellationToken);
                await _unitOfWork.CommitTransactionAsync(cancellationToken);

                op.Success($"Funds added. TransactionId: {transaction.Id}");
                return new BaseResult<AddFundsCommandResponseDto>(
                    HttpStatusCode.OK,
                    "Funds added successfully.",
                    new AddFundsCommandResponseDto
                    {
                        TransactionId = transaction.Id,
                        Amount = request.Amount,
                        Reference = reference
                    });
            }
            catch (Exception exception)
            {
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                op.Fail("Unable to add funds.", exception);
                return new BaseResult<AddFundsCommandResponseDto>(
                    HttpStatusCode.InternalServerError,
                    "Unable to add funds. Please try again.");
            }
        }
    }
}
