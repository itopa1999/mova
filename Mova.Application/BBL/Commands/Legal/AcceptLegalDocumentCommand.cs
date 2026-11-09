using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mova.Application.Interfaces.Identity;
using Mova.Application.Interfaces.Notification;
using Mova.Application.Interfaces.Persistence;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Shared.Logging;
using Mova.Shared.Common.Results;

namespace Mova.Application.BBL.Commands.Legal;

public sealed class AcceptLegalDocumentCommand
{
    public sealed class Command : IRequest<BaseResult<AcceptConsentResponseDto>>
    {
        [JsonIgnore]
        public string UserPublicId { get; set; } = string.Empty;

        [Required]
        public LegalDocumentType Type { get; set; }

        [Required, MaxLength(20)]
        public string DocumentVersion { get; set; } = string.Empty;
    }

    public sealed class Handler : IRequestHandler<Command, BaseResult<AcceptConsentResponseDto>>
    {
        private static readonly Dictionary<LegalDocumentType, (OnboardingStage Target, OnboardingStage Next)> StageMap = new()
        {
            [LegalDocumentType.Agreement] = (OnboardingStage.Agreement, OnboardingStage.Privacy),
            [LegalDocumentType.Privacy]   = (OnboardingStage.Privacy,   OnboardingStage.Terms),
            [LegalDocumentType.Terms]     = (OnboardingStage.Completed, OnboardingStage.Completed),
        };

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

        public async Task<BaseResult<AcceptConsentResponseDto>> Handle(
            Command request,
            CancellationToken cancellationToken)
        {
            using var op = OperationLogger.Start(
                _logger,
                "AcceptLegalDocument",
                ("UserId", request.UserPublicId),
                ("Type", request.Type),
                ("Version", request.DocumentVersion));

            if (string.IsNullOrWhiteSpace(request.UserPublicId))
            {
                op.Warning("UserPublicId is required.");
                return Bad("UserPublicId is required.", HttpStatusCode.Unauthorized);
            }

            if (string.IsNullOrWhiteSpace(request.DocumentVersion))
            {
                op.Warning("DocumentVersion is required.");
                return Bad("Document version is required.");
            }

            if (!StageMap.TryGetValue(request.Type, out var stages))
            {
                op.Warning($"Unsupported document type: {request.Type}.");
                return Bad($"Unsupported document type: {request.Type}.");
            }

            var user = await _identityService.GetByIdentifierAsync(
                request.UserPublicId,
                cancellationToken);

            if (user is null)
            {
                op.Warning($"User not found: {request.UserPublicId}.");
                return Bad("User not found.", HttpStatusCode.NotFound);
            }

            var currentDocument = await _unitOfWork.Query<LegalDocument>()
                .AsNoTracking()
                .Where(d => d.Type == request.Type && d.IsCurrent)
                .OrderByDescending(d => d.PublishedAt)
                .FirstOrDefaultAsync(cancellationToken);

            if (currentDocument is null)
            {
                op.Warning($"No current {request.Type} document found.");
                return Bad(
                    $"The {request.Type} document is not available. Please contact support.",
                    HttpStatusCode.InternalServerError);
            }

            if (!string.Equals(currentDocument.Version, request.DocumentVersion, StringComparison.Ordinal))
            {
                op.Warning(
                    $"Version mismatch for {request.Type}: " +
                    $"client={request.DocumentVersion}, current={currentDocument.Version}.");
                return Bad(
                    $"The {request.Type} document has been updated to version {currentDocument.Version}. " +
                    "Please review the latest version and try again.",
                    HttpStatusCode.Conflict);
            }

            var isOnboarding = user.OnboardingStage < stages.Target;

            await using var tx = await _unitOfWork.BeginAtomicAsync(cancellationToken);

            var alreadyAccepted = await _unitOfWork.Query<UserConsent>()
                .AnyAsync(
                    c => c.UserPublicId == user.PublicId &&
                         c.LegalDocumentId == currentDocument.Id,
                    cancellationToken);

            if (!alreadyAccepted)
            {
                await _unitOfWork.AddAsync(new UserConsent
                {
                    UserPublicId = user.PublicId,
                    LegalDocumentId = currentDocument.Id,
                    DocumentType = currentDocument.Type,
                    DocumentVersion = currentDocument.Version,
                    AcceptedAt = DateTimeOffset.UtcNow,
                }, cancellationToken);
            }

            if (isOnboarding)
            {
                var (stageOk, stageMessage) = await _identityService.UpdateOnboardingStageAsync(
                    user.PublicId,
                    stages.Target,
                    cancellationToken);

                if (!stageOk)
                {
                    op.Warning($"Failed to advance onboarding stage: {stageMessage}");
                    return Bad(stageMessage);
                }

            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);

            op.Success(
                $"User {user.PublicId} accepted {request.Type} v{request.DocumentVersion}. " +
                $"IsOnboarding: {isOnboarding}, NewStage: {stages.Target}.");

            if (isOnboarding)
            {
                _notificationQueue.InAppNotificationAsync(
                    user.PublicId,
                    NotificationType.Security,
                    $"{request.Type} accepted",
                    $"You have accepted the {request.Type} document (v{currentDocument.Version}).",
                    "/dashboard/settings",
                    null,
                    CancellationToken.None);
            }

            return new BaseResult<AcceptConsentResponseDto>(
                HttpStatusCode.OK,
                $"{request.Type} accepted successfully.",
                new AcceptConsentResponseDto
                {
                    IsOnboarding = isOnboarding,
                    Notification = isOnboarding,
                    CurrentStage = stages.Target.ToString(),
                    CurrentStageValue = (int)stages.Target,
                    NextStage = stages.Next.ToString(),
                });
        }

        private static BaseResult<AcceptConsentResponseDto> Bad(
            string message,
            HttpStatusCode status = HttpStatusCode.BadRequest)
            => new(status, message);
    }
}

public sealed class AcceptConsentResponseDto
{
    public bool IsOnboarding { get; set; }
    public bool Notification { get; set; }
    public string CurrentStage { get; set; } = string.Empty;
    public int CurrentStageValue { get; set; }
    public string NextStage { get; set; } = string.Empty;
}