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
using Mova.Shared.Common.Results;
namespace Mova.Application.BBL.Commands.SavingsCircles;

public sealed class UpdateCircleRotationCommand
{
    public sealed class Command : IRequest<BaseResult<UpdateCircleRotationResponse>>
    {
        [JsonIgnore] public string UserPublicId { get; set; } = string.Empty;

        public long WalletId { get; set; }

        [Required]
        [MinLength(1)]
        public List<RotationPositionItem> Positions { get; set; } = new();
    }

    public sealed class Handler
        : IRequestHandler<Command, BaseResult<UpdateCircleRotationResponse>>
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

        public async Task<BaseResult<UpdateCircleRotationResponse>> Handle(
            Command request,
            CancellationToken cancellationToken)
        {
            using var op = OperationLogger.Start(
                _logger,
                "UpdateCircleRotation",
                ("UserId", request.UserPublicId),
                ("WalletId", request.WalletId));

            if (string.IsNullOrWhiteSpace(request.UserPublicId))
            {
                op.Warning("Missing UserPublicId.");
                return Bad("You must be signed in.", HttpStatusCode.Unauthorized);
            }

            if (request.Positions.Count == 0)
                return Bad("No position updates provided.");

            var wallet = await _unitOfWork.Query<Wallet>()
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    w => w.Id == request.WalletId && w.Type == WalletType.Circle,
                    cancellationToken);

            if (wallet is null || wallet.SavingsCircleId is null)
                return Bad("Circle wallet not found.", HttpStatusCode.NotFound);

            var circle = await _unitOfWork.Query<SavingsCircle>()
                .Include(c => c.Members)
                .FirstOrDefaultAsync(
                    c => c.Id == wallet.SavingsCircleId,
                    cancellationToken);

            if (circle is null)
                return Bad("Circle not found.", HttpStatusCode.NotFound);

            if (circle.OrganizerUserPublicId != request.UserPublicId)
                return Bad("Only the organizer can change the rotation order.",
                    HttpStatusCode.Forbidden);

            if (circle.Status != SavingsCircleStatus.Draft)
                return Bad("Rotation can only be changed while the circle is in Draft status.");

            if (circle.RotationStrategy != RotationStrategy.ModeratorSet)
                return Bad("Rotation can only be changed when the strategy is ModeratorSet.");

            var activeMembers = circle.Members
                .Where(m => m.Status == CircleMemberStatus.Active)
                .ToList();

            if (activeMembers.Count == 0)
                return Bad("No active members to reorder.");

            var activeUserIds = activeMembers
                .Select(m => m.UserPublicId)
                .Where(id => !string.IsNullOrEmpty(id))
                .ToHashSet();

            foreach (var update in request.Positions)
            {
                if (string.IsNullOrWhiteSpace(update.UserPublicId))
                    return Bad("Each position update requires a userPublicId.");

                if (!activeUserIds.Contains(update.UserPublicId))
                    return Bad($"Member {update.UserPublicId} is not active in this circle.");

                if (update.Position < 1 || update.Position > activeMembers.Count)
                    return Bad($"Position {update.Position} is out of range (1..{activeMembers.Count}).");
            }

            var duplicatePositions = request.Positions
                .GroupBy(p => p.Position)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();

            if (duplicatePositions.Count > 0)
                return Bad($"Duplicate positions submitted: {string.Join(", ", duplicatePositions)}.");

            var updatesByUser = request.Positions
                .ToDictionary(p => p.UserPublicId, p => p.Position);

            var projectedPositions = activeMembers
                .Select(m =>
                {
                    if (m.UserPublicId != null &&
                        updatesByUser.TryGetValue(m.UserPublicId, out var newPos))
                        return (int?)newPos;
                    return m.RotationPosition;
                })
                .ToList();

            if (projectedPositions.Any(p => p is null))
                return Bad("One or more active members have no rotation position. " +
                    "Assign all positions before swapping.");

            var finalValues = projectedPositions
                .Where(p => p.HasValue)
                .Select(p => p!.Value)
                .ToList();

            var expected = Enumerable.Range(1, activeMembers.Count).ToHashSet();
            var actual = finalValues.ToHashSet();

            if (!expected.SetEquals(actual))
                return Bad($"Positions must form a complete sequence from 1 to {activeMembers.Count}.");

            var changes = new List<(CircleMember Member, int NewPos)>();
            foreach (var update in request.Positions)
            {
                var member = activeMembers.First(m => m.UserPublicId == update.UserPublicId);
                if (member.RotationPosition != update.Position)
                    changes.Add((member, update.Position));
            }

            if (changes.Count == 0)
            {
                op.Success("No position changes needed.");
                return new BaseResult<UpdateCircleRotationResponse>(
                    HttpStatusCode.OK,
                    "Rotation order updated.",
                    new UpdateCircleRotationResponse { Updated = true });
            }

            await using var tx = await _unitOfWork.BeginAtomicSerializableAsync(
                cancellationToken);

            var tempPos = -changes.Count;
            foreach (var (member, _) in changes)
            {
                member.RotationPosition = tempPos++;
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            foreach (var (member, newPos) in changes)
            {
                member.RotationPosition = newPos;
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            await tx.CommitAsync(cancellationToken);

            op.Success(
                $"Rotation updated for circle {circle.Id} by {request.UserPublicId}. " +
                $"Applied {changes.Count} position change(s).");

            return new BaseResult<UpdateCircleRotationResponse>(
                HttpStatusCode.OK,
                "Rotation order updated.",
                new UpdateCircleRotationResponse { Updated = true });
        }

        private static BaseResult<UpdateCircleRotationResponse> Bad(
            string message,
            HttpStatusCode status = HttpStatusCode.BadRequest)
            => new(status, message);
    }
}

public sealed class RotationPositionItem
{
    [Required]
    public string UserPublicId { get; set; } = string.Empty;

    [Range(1, int.MaxValue)]
    public int Position { get; set; }
}

public sealed class UpdateCircleRotationResponse
{
    public bool Updated { get; set; }
}