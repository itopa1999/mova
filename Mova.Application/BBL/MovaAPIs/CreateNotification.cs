using System.Net;
using System.Text.Json.Serialization;
using MediatR;
using Mova.Application.Interfaces.Persistence;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Shared.Common;

namespace Mova.Application.BBL.MovaAPIs;

public sealed class CreateNotificationCommand
{
    public sealed class Command : IRequest<BaseResult<long>>
    {
        public string UserPublicId { get; set; } = string.Empty;

        public NotificationType Type { get; set; }

        public string Title { get; set; } = string.Empty;

        public string Message { get; set; } = string.Empty;

        public string? ActionUrl { get; set; }

        public string? Metadata { get; set; }
    }

    public sealed class Handler : IRequestHandler<Command, BaseResult<long>>
    {
        private readonly IUnitOfWork _unitOfWork;

        public Handler(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }

        public async Task<BaseResult<long>> Handle(
            Command request,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(request.UserPublicId))
            {
                return new BaseResult<long>(
                    HttpStatusCode.BadRequest,
                    "User ID is required.");
            }

            if (string.IsNullOrWhiteSpace(request.Title))
            {
                return new BaseResult<long>(
                    HttpStatusCode.BadRequest,
                    "Title is required.");
            }

            var notification = new AppNotification
            {
                UserPublicId = request.UserPublicId,
                Type = request.Type,
                Title = request.Title.Trim(),
                Message = request.Message.Trim(),
                IsRead = false,
                ActionUrl = request.ActionUrl,
                Metadata = request.Metadata,
            };

            await _unitOfWork.AddAsync(notification, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return new BaseResult<long>(
                HttpStatusCode.Created,
                "Notification created.",
                notification.Id);
        }
    }
}