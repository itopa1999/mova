using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mova.Application.Interfaces.Caching;
using Mova.Application.Interfaces.Persistence;
using Mova.Domain.Entities;
using Mova.Shared.Common;
using Mova.Shared.Constants;
using Mova.Shared.Logging;

namespace Mova.Application.BBL.MovaAPIs;

public sealed class MarkNotificationAsRead
{
    public sealed class Command : IRequest<BaseResult>
    {
        [JsonIgnore]
        public string UserPublicId { get; set; } = string.Empty;

        [Range(1, long.MaxValue)]
        public long NotificationId { get; set; }
    }

    public sealed class Handler : IRequestHandler<Command, BaseResult>
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly ICacheService _cache;
        private readonly ILogger<Handler> _logger;

        public Handler(
            IUnitOfWork unitOfWork,
            ICacheService cache,
            ILogger<Handler> logger)
        {
            _unitOfWork = unitOfWork;
            _cache = cache;
            _logger = logger;
        }

        public async Task<BaseResult> Handle(
            Command request,
            CancellationToken cancellationToken)
        {
            using var op = OperationLogger.Start(
                _logger,
                "MarkNotificationAsRead",
                ("UserPublicId", request.UserPublicId),
                ("NotificationId", request.NotificationId));

            if (string.IsNullOrWhiteSpace(request.UserPublicId))
            {
                op.Warning("UserPublicId is required.");
                return new BaseResult(
                    HttpStatusCode.BadRequest,
                    "UserPublicId is required.");
            }

            var notification = await _unitOfWork.Query<AppNotification>()
                .FirstOrDefaultAsync(
                    x => x.Id == request.NotificationId
                         && x.UserPublicId == request.UserPublicId,
                    cancellationToken);

            if (notification is null)
            {
                op.Warning($"Notification not found: {request.NotificationId}.");
                return new BaseResult(
                    HttpStatusCode.NotFound,
                    "Notification not found.");
            }

            if (notification.IsRead)
            {
                op.Success("Notification already read.");
                return new BaseResult(
                    HttpStatusCode.OK,
                    "Notification marked as read.");
            }

            await using var tx = await _unitOfWork.BeginAtomicAsync(cancellationToken);

            notification.IsRead = true;
            notification.ReadAt = DateTimeOffset.UtcNow;
            _unitOfWork.Update(notification);

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);

            await _cache.DeletePrefixAsync(
                CacheKeys.NotificationsPrefix(request.UserPublicId));

            op.Success($"Notification {request.NotificationId} marked as read.");

            return new BaseResult(
                HttpStatusCode.OK,
                "Notification marked as read.");
        }
    }
}