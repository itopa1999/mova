using System.Net;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Mova.Application.Interfaces.Persistence;
using Mova.Domain.Entities;
using Mova.Shared.Common;

namespace Mova.Application.BBL.MovaAPIs;

public sealed class GetNotificationsQuery
{
    public sealed class Query
        : IRequest<BaseResult<List<NotificationDto>>>
    {
        public string UserPublicId { get; set; } = string.Empty;

        public bool UnreadOnly { get; set; }
    }

    public sealed class NotificationDto
    {
        public long Id { get; set; }
        public string Type { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public bool IsRead { get; set; }
        public string? ActionUrl { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
    }

    public sealed class Handler
        : IRequestHandler<Query, BaseResult<List<NotificationDto>>>
    {
        private readonly IUnitOfWork _unitOfWork;

        public Handler(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }

        public async Task<BaseResult<List<NotificationDto>>> Handle(
            Query request,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(request.UserPublicId))
            {
                return new BaseResult<List<NotificationDto>>(
                    HttpStatusCode.BadRequest,
                    "User ID is required.");
            }

            var notifications = await LoadNotificationsFromDbAsync(
                request.UserPublicId,
                request.UnreadOnly,
                cancellationToken);

            return new BaseResult<List<NotificationDto>>(
                HttpStatusCode.OK,
                "Notifications retrieved successfully.",
                notifications);
        }

        private async Task<List<NotificationDto>> LoadNotificationsFromDbAsync(
            string userPublicId,
            bool unreadOnly,
            CancellationToken cancellationToken)
        {
            var query = _unitOfWork.Query<AppNotification>()
                .AsNoTracking()
                .Where(x => x.UserPublicId == userPublicId);

            if (unreadOnly)
            {
                query = query.Where(x => !x.IsRead);
            }

            return await query
                .OrderByDescending(x => x.CreatedAt)
                .Take(100)
                .Select(x => new NotificationDto
                {
                    Id = x.Id,
                    Type = x.Type.ToString(),
                    Title = x.Title,
                    Message = x.Message,
                    IsRead = x.IsRead,
                    ActionUrl = x.ActionUrl,
                    CreatedAt = x.CreatedAt,
                })
                .ToListAsync(cancellationToken);
        }
    }
}