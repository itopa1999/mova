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

public sealed class MarkAllNotificationsAsRead
{
    public sealed class Command : IRequest<BaseResult>
    {
        [JsonIgnore]
        public string UserPublicId { get; set; } = string.Empty;
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
                "MarkAllNotificationsAsRead",
                ("UserPublicId", request.UserPublicId));

            if (string.IsNullOrWhiteSpace(request.UserPublicId))
            {
                op.Warning("UserPublicId is required.");
                return new BaseResult(
                    HttpStatusCode.BadRequest,
                    "UserPublicId is required.");
            }

            await using var tx = await _unitOfWork.BeginAtomicAsync(cancellationToken);

            var unread = await _unitOfWork.Query<AppNotification>()
                .Where(x => x.UserPublicId == request.UserPublicId && !x.IsRead)
                .ToListAsync(cancellationToken);

            if (unread.Count == 0)
            {
                op.Success("No unread notifications.");
                return new BaseResult(
                    HttpStatusCode.OK,
                    "No unread notifications.");
            }

            var now = DateTimeOffset.UtcNow;

            foreach (var notification in unread)
            {
                notification.IsRead = true;
                notification.ReadAt = now;
                _unitOfWork.Update(notification);
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);

            await _cache.DeletePrefixAsync(
                CacheKeys.NotificationsPrefix(request.UserPublicId));

            op.Success($"{unread.Count} notification(s) marked as read.");

            return new BaseResult(
                HttpStatusCode.OK,
                $"{unread.Count} notification(s) marked as read.");
        }
    }
}