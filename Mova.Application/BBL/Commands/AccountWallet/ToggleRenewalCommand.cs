using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mova.Application.Interfaces.Persistence;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Shared.Common;
using Mova.Shared.Logging;

namespace Mova.Application.BBL.Commands.AccountWallet;

public sealed class ToggleRenewalCommand
{
    public sealed class Command : IRequest<BaseResult<ToggleRenewalResponseDto>>
    {
        [JsonIgnore]
        public string UserPublicId { get; set; } = string.Empty;

        [Range(1, long.MaxValue)]
        public long WalletId { get; set; }
    }

    public sealed class ToggleRenewalResponseDto
    {
        public string Status { get; init; } = string.Empty;
        public bool IsEnabled { get; init; }
    }

    public sealed class Handler
        : IRequestHandler<Command, BaseResult<ToggleRenewalResponseDto>>
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly ILogger<Handler> _logger;

        public Handler(
            IUnitOfWork unitOfWork,
            ILogger<Handler> logger)
        {
            _unitOfWork = unitOfWork;
            _logger = logger;
        }

        public async Task<BaseResult<ToggleRenewalResponseDto>> Handle(
            Command request,
            CancellationToken cancellationToken)
        {
            using var op = OperationLogger.Start(
                _logger,
                "ToggleRenewal",
                ("UserId", request.UserPublicId),
                ("WalletId", request.WalletId));

            var wallet = await _unitOfWork.Query<Wallet>()
                .FirstOrDefaultAsync(
                    x => x.Id == request.WalletId
                         && x.UserPublicId == request.UserPublicId,
                    cancellationToken);

            if (wallet is null)
            {
                op.Warning("Wallet not found.");
                return new BaseResult<ToggleRenewalResponseDto>(
                    HttpStatusCode.NotFound,
                    "Wallet not found.");
            }

            var policy = await _unitOfWork.Query<RenewalPolicy>()
                .FirstOrDefaultAsync(
                    x => x.WalletId == wallet.Id,
                    cancellationToken);

            if (policy is null)
            {
                op.Warning("Renewal policy not found for this wallet.");
                return new BaseResult<ToggleRenewalResponseDto>(
                    HttpStatusCode.NotFound,
                    "This wallet has no automation policy.");
            }

            if (!policy.IsEnabled)
            {
                op.Warning("Renewal policy is not enabled.");
                return new BaseResult<ToggleRenewalResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Automation is not enabled for this wallet.");
            }

            if (policy.Status != RenewalStatus.Active
                && policy.Status != RenewalStatus.Paused)
            {
                op.Warning($"Cannot toggle policy in status {policy.Status}.");
                return new BaseResult<ToggleRenewalResponseDto>(
                    HttpStatusCode.BadRequest,
                    $"Automation cannot be toggled while in {policy.Status} state.");
            }

            var newStatus = policy.Status == RenewalStatus.Active
                ? RenewalStatus.Paused
                : RenewalStatus.Active;

            await using var tx = await _unitOfWork.BeginAtomicAsync(cancellationToken);

            policy.Status = newStatus;
            _unitOfWork.Update(policy);

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);

            op.Success(
                $"Renewal policy toggled. PolicyId: {policy.Id}, " +
                $"Status: {policy.Status}");

            return new BaseResult<ToggleRenewalResponseDto>(
                HttpStatusCode.OK,
                newStatus == RenewalStatus.Active
                    ? "Automation resumed successfully."
                    : "Automation paused successfully.",
                new ToggleRenewalResponseDto
                {
                    Status = policy.Status.ToString(),
                    IsEnabled = policy.IsEnabled,
                });
        }
    }
}