using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mova.Application.Interfaces.Persistence;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Domain.ValueObjects;
using Mova.Shared.Common;
using Mova.Shared.Logging;
using Mova.Shared.Common.Results;
namespace Mova.Application.BBL.Commands.SavingsCircles;

public sealed class UpdateCircleDetailsCommand
{
    public sealed class Command : IRequest<BaseResult<UpdateCircleDetailsResponse>>
    {
        [JsonIgnore] public string UserPublicId { get; set; } = string.Empty;

        public long WalletId { get; set; }

        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(500)]
        public string Description { get; set; } = string.Empty;

        public string StartPolicy { get; set; } = string.Empty;
        public string RotationStrategy { get; set; } = string.Empty;

        public bool AllowPenalty { get; set; }
        public decimal PenaltyAmount { get; set; }
        public string PenaltySource { get; set; } = string.Empty;
        public int GracePeriodHours { get; set; }

        public int MemberCount { get; set; }
    }

    public sealed class Handler : IRequestHandler<Command, BaseResult<UpdateCircleDetailsResponse>>
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly ILogger<Handler> _logger;

        public Handler(IUnitOfWork unitOfWork, ILogger<Handler> logger)
        {
            _unitOfWork = unitOfWork;
            _logger = logger;
        }

        public async Task<BaseResult<UpdateCircleDetailsResponse>> Handle(
            Command request,
            CancellationToken cancellationToken)
        {
            using var op = OperationLogger.Start(
                _logger,
                "UpdateCircleDetails",
                ("UserId", request.UserPublicId),
                ("WalletId", request.WalletId));

            if (string.IsNullOrWhiteSpace(request.UserPublicId))
                return Bad("You must be signed in.", HttpStatusCode.Unauthorized);

            var wallet = await _unitOfWork.Query<Wallet>()
                .FirstOrDefaultAsync(w => w.Id == request.WalletId && w.Type == WalletType.Circle, cancellationToken);

            if (wallet is null || wallet.SavingsCircleId is null)
                return Bad("Circle wallet not found.", HttpStatusCode.NotFound);

            var circle = await _unitOfWork.Query<SavingsCircle>()
                .Include(c => c.Members)
                .FirstOrDefaultAsync(c => c.Id == wallet.SavingsCircleId, cancellationToken);

            if (circle is null)
                return Bad("Circle not found.", HttpStatusCode.NotFound);

            if (circle.OrganizerUserPublicId != request.UserPublicId)
                return Bad("Only the circle organizer can edit details.", HttpStatusCode.Forbidden);

            if (circle.Status == SavingsCircleStatus.Completed || circle.Status == SavingsCircleStatus.Cancelled)
                return Bad("Cannot edit a completed or cancelled circle.");

            if (circle.Status == SavingsCircleStatus.Active)
            {
                circle.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
                wallet.Description = circle.Description;
                _unitOfWork.Update(circle);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                op.Success("Active circle description updated.");
                return new BaseResult<UpdateCircleDetailsResponse>(
                    HttpStatusCode.OK,
                    "Description updated successfully.",
                    new UpdateCircleDetailsResponse { Updated = true });
            }

            if (circle.Status == SavingsCircleStatus.Draft)
            {
                var activeMemberCount = circle.Members.Count(m => m.Status == CircleMemberStatus.Active);
                var minCapacity = Math.Max(3, activeMemberCount);

                if (request.MemberCount < minCapacity)
                {
                    return Bad($"Total member slots cannot be less than {minCapacity}. " +
                               $"(Minimum 3, and you already have {activeMemberCount} active members).");
                }

                if (!Enum.TryParse<StartPolicy>(request.StartPolicy, true, out var parsedStartPolicy))
                    return Bad("Invalid Start Policy.");

                if (!Enum.TryParse<RotationStrategy>(request.RotationStrategy, true, out var parsedRotationStrategy))
                    return Bad("Invalid Rotation Strategy.");

                if (!Enum.TryParse<PenaltyAmountFrom>(request.PenaltySource, true, out var parsedPenaltySource))
                    return Bad("Invalid Penalty Source.");

                circle.Name = request.Name.Trim();
                circle.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
                circle.StartPolicy = parsedStartPolicy;
                circle.RotationStrategy = parsedRotationStrategy;

                circle.AllowPenalty = request.AllowPenalty;
                circle.PenaltyAmountFrom = parsedPenaltySource;
                circle.GracePeriodHours = request.GracePeriodHours;
                circle.MemberCount = request.MemberCount;

                wallet.Name = circle.Name;
                wallet.Description = circle.Description;

                // Handle Penalty amount logic
                if (request.AllowPenalty)
                    circle.PenaltyAmount = Money.FromNaira(request.PenaltyAmount);
                else
                    circle.PenaltyAmount = Money.FromNaira(0);

                _unitOfWork.Update(circle);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                op.Success("Draft circle details updated successfully.");
                return new BaseResult<UpdateCircleDetailsResponse>(
                    HttpStatusCode.OK,
                    "Circle rules updated successfully.",
                    new UpdateCircleDetailsResponse { Updated = true });
            }

            return Bad("Unable to update circle in its current state.");
        }

        private static BaseResult<UpdateCircleDetailsResponse> Bad(
            string message,
            HttpStatusCode status = HttpStatusCode.BadRequest)
            => new(status, message);
    }
}

public sealed class UpdateCircleDetailsResponse
{
    public bool Updated { get; set; }
}