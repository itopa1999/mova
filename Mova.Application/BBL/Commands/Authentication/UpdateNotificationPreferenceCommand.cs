using System.Net;
using System.Text.Json.Serialization;
using MediatR;
using Mova.Application.Interfaces.Identity;
using Mova.Shared.Common;

namespace Mova.Application.BBL.Commands.Authentication;

public sealed class UpdateNotificationPreferenceCommand
{
    public sealed class Command : IRequest<BaseResult<UpdateNotificationPreferenceResponseDto>>
    {
        [JsonIgnore]
        public string UserPublicId { get; set; } = string.Empty;

        /// <summary>
        /// One of: login, release, updates, promotions
        /// </summary>
        public string Key { get; set; } = string.Empty;

        public bool Enabled { get; set; }
    }

    public sealed class UpdateNotificationPreferenceResponseDto
    {
        public string Key { get; init; } = string.Empty;
        public bool Enabled { get; init; }
    }

    public sealed class Handler
        : IRequestHandler<Command, BaseResult<UpdateNotificationPreferenceResponseDto>>
    {
        private static readonly HashSet<string> AllowedKeys = new(StringComparer.OrdinalIgnoreCase)
        {
            "login",
            "release",
            "updates",
            "promotions",
        };

        private readonly IIdentityService _identityService;

        public Handler(IIdentityService identityService)
        {
            _identityService = identityService;
        }

        public async Task<BaseResult<UpdateNotificationPreferenceResponseDto>> Handle(
            Command request,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(request.UserPublicId))
            {
                return new BaseResult<UpdateNotificationPreferenceResponseDto>(
                    HttpStatusCode.BadRequest,
                    "User ID is required.");
            }

            if (string.IsNullOrWhiteSpace(request.Key))
            {
                return new BaseResult<UpdateNotificationPreferenceResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Notification key is required.");
            }

            var key = request.Key.Trim().ToLowerInvariant();

            if (!AllowedKeys.Contains(key))
            {
                return new BaseResult<UpdateNotificationPreferenceResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Invalid notification key. Must be one of: login, release, updates, promotions.");
            }

            var updated = await _identityService.UpdateNotificationPreferenceAsync(
                request.UserPublicId,
                key,
                request.Enabled,
                cancellationToken);

            if (!updated)
            {
                return new BaseResult<UpdateNotificationPreferenceResponseDto>(
                    HttpStatusCode.NotFound,
                    "User not found.");
            }

            return new BaseResult<UpdateNotificationPreferenceResponseDto>(
                HttpStatusCode.OK,
                "Notification preference updated.",
                new UpdateNotificationPreferenceResponseDto
                {
                    Key = key,
                    Enabled = request.Enabled,
                });
        }
    }
}