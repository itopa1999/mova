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

namespace Mova.Application.BBL.Commands.BanksAccount;

public sealed class DeleteBankAccount
{
    public sealed class Command : IRequest<BaseResult<object>>
    {
        [JsonIgnore]
        public string UserPublicId { get; set; } = string.Empty;

        public long BankAccountId { get; init; }
    }

    public sealed class Handler
        : IRequestHandler<Command, BaseResult<object>>
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

        public async Task<BaseResult<object>> Handle(
            Command request,
            CancellationToken cancellationToken)
        {
            using var op = OperationLogger.Start(
                _logger,
                "DeleteBankAccount",
                ("UserId", request.UserPublicId),
                ("BankAccountId", request.BankAccountId));

            var account = await _unitOfWork.Query<BankAccount>()
                .FirstOrDefaultAsync(
                    x =>
                        x.Id == request.BankAccountId &&
                        x.UserPublicId == request.UserPublicId,
                    cancellationToken);

            if (account is null)
            {
                op.Fail($"Bank account not found: {request.BankAccountId}");

                return new BaseResult<object>(
                    HttpStatusCode.NotFound,
                    "Bank account not found.");
            }

            account.IsDeleted = true;

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            await _mediator.Send(
                new CreateNotificationCommand.Command
                {
                    UserPublicId = request.UserPublicId,
                    Type = NotificationType.System,
                    Title = "Bank account removed",
                    Message =
                        $"{account.AccountName} ({account.BankName}) " +
                        $"has been removed from your MOVA account.",
                    ActionUrl = "/bank",
                },
                cancellationToken);

            op.Success(
                $"Bank account deleted successfully. " +
                $"Id: {account.Id}, Bank: {account.BankName}");

            return new BaseResult<object>(
                HttpStatusCode.OK,
                "Bank account deleted successfully.",
                new
                {
                    notification = true,
                });
        }
    }
}