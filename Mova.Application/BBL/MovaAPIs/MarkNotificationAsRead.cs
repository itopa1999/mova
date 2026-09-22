using System.Net;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Mova.Application.Interfaces.Caching;
using Mova.Application.Interfaces.Persistence;
using Mova.Domain.Entities;
using Mova.Shared.Common;
using Mova.Shared.Constants;

namespace Mova.Application.BBL.MovaAPIs;

public sealed class MarkNotificationAsRead
{
    public sealed class Command : IRequest<BaseResult>
    {
        [JsonIgnore]
        public string UserPublicId { get; set; } = string.Empty;

        public long NotificationId { get; set; }
    }

    public sealed class Handler : IRequestHandler<Command, BaseResult>
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly ICacheService _cache;

        public Handler(
            IUnitOfWork unitOfWork,
            ICacheService cache)
        {
            _unitOfWork = unitOfWork;
            _cache = cache;
        }

        public async Task<BaseResult> Handle(
            Command request,
            CancellationToken cancellationToken)
        {
            var notification =
                await _unitOfWork.Query<AppNotification>()
                    .FirstOrDefaultAsync(
                        x => x.Id == request.NotificationId
                             && x.UserPublicId == request.UserPublicId,
                        cancellationToken);

            if (notification is null)
            {
                return new BaseResult(
                    HttpStatusCode.NotFound,
                    "Notification not found.");
            }

            if (!notification.IsRead)
            {
                notification.IsRead = true;
                notification.ReadAt = DateTimeOffset.UtcNow;

                await _unitOfWork.SaveChangesAsync(cancellationToken);

                await _cache.DeletePrefixAsync(
                    CacheKeys.NotificationsPrefix(request.UserPublicId));
            }

            return new BaseResult(
                HttpStatusCode.OK,
                "Notification marked as read.");
        }
    }
}