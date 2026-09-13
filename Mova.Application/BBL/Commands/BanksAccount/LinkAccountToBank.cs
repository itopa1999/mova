using System.Net;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mova.Application.BBL.MovaAPIs;
using Mova.Application.Interfaces.Persistence;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Shared.Common;
using Mova.Shared.Logging;

namespace Mova.Application.BBL.Commands.AccountWallet;

public sealed class LinkAccountToBank
{
    public sealed class Command : IRequest<BaseResult<LinkAccountToBankDto>>
    {
        [JsonIgnore]
        public string UserPublicId { get; set; } = string.Empty;

        [JsonIgnore]
        public long WalletId { get; set; } = 0;

        public long BankAccountId { get; set; }
    }

    public sealed class LinkAccountToBankDto
    {
        public long Id { get; init; }

        public string AccountName { get; init; } = string.Empty;

        public string AccountNumber { get; init; } = string.Empty;

        public string BankName { get; init; } = string.Empty;

        public string BankImageUrl { get; init; } = string.Empty;

        public bool Notification { get; init; }
    }

    public sealed class Handler
        : IRequestHandler<Command, BaseResult<LinkAccountToBankDto>>
    {
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

        public async Task<BaseResult<LinkAccountToBankDto>> Handle(
            Command request,
            CancellationToken cancellationToken)
        {
            using var op = OperationLogger.Start(
                _logger,
                "LinkAccountToBank",
                ("UserId", request.UserPublicId),
                ("WalletId", request.WalletId),
                ("BankAccountId", request.BankAccountId));

            var wallet = await _unitOfWork
                .Query<Wallet>()
                .FirstOrDefaultAsync(
                    w =>
                        w.Id == request.WalletId &&
                        w.UserPublicId == request.UserPublicId,
                    cancellationToken);

            if (wallet is null)
            {
                op.Fail($"Wallet not found: {request.WalletId}");

                return new BaseResult<LinkAccountToBankDto>(
                    HttpStatusCode.NotFound,
                    "Wallet not found.");
            }

            var bankAccount = await _unitOfWork
                .Query<BankAccount>()
                .FirstOrDefaultAsync(
                    b =>
                        b.Id == request.BankAccountId &&
                        b.UserPublicId == request.UserPublicId &&
                        b.ConsentGiven &&
                        b.Status == BankAccountStatus.Active,
                    cancellationToken);

            if (bankAccount is null)
            {
                op.Fail($"Bank account not found: {request.BankAccountId}");

                return new BaseResult<LinkAccountToBankDto>(
                    HttpStatusCode.NotFound,
                    "Bank account not found.");
            }

            wallet.BankAccountId = bankAccount.Id;

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            await _mediator.Send(
                new CreateNotificationCommand.Command
                {
                    UserPublicId = request.UserPublicId,
                    Type = NotificationType.Wallet,
                    Title = "Bank account linked",
                    Message =
                        $"{bankAccount.BankName} ({bankAccount.AccountNumber}) " +
                        $"has been linked to your {wallet.Name} wallet.",
                    ActionUrl = $"/wallet/{wallet.Id}",
                },
                cancellationToken);

            op.Success(
                $"Bank account linked to wallet successfully. " +
                $"WalletId: {wallet.Id}, BankAccountId: {bankAccount.Id}");

            var response = new LinkAccountToBankDto
            {
                Id = bankAccount.Id,
                AccountName = bankAccount.AccountName,
                AccountNumber = bankAccount.AccountNumber,
                BankName = bankAccount.BankName,
                BankImageUrl = bankAccount.BankImageUrl,
                Notification = true,
            };

            return new BaseResult<LinkAccountToBankDto>(
                HttpStatusCode.OK,
                "Bank account linked to wallet successfully.",
                response);
        }
    }
}