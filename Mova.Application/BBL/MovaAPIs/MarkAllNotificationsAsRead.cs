using System.Net;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Mova.Application.Interfaces.Persistence;
using Mova.Domain.Entities;
using Mova.Shared.Common;

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

        public Handler(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }

        public async Task<BaseResult> Handle(
            Command request,
            CancellationToken cancellationToken)
        {
            var unread =
                await _unitOfWork.Query<AppNotification>()
                    .Where(x => x.UserPublicId == request.UserPublicId && !x.IsRead)
                    .ToListAsync(cancellationToken);

            if (unread.Count == 0)
            {
                return new BaseResult(
                    HttpStatusCode.OK,
                    "No unread notifications.");
            }

            var now = DateTimeOffset.UtcNow;

            foreach (var notification in unread)
            {
                notification.IsRead = true;
                notification.ReadAt = now;
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return new BaseResult(
                HttpStatusCode.OK,
                $"{unread.Count} notification(s) marked as read.");
        }
    }
}