using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.Extensions.Logging;
using Mova.Application.Interfaces.Identity;
using Mova.Shared.Common;
using Mova.Shared.Logging;

namespace Mova.Application.BBL.Commands.Authentication;

public sealed class UpdateNotificationPreferenceCommand
{
    public sealed class Command : IRequest<BaseResult<UpdateNotificationPreferenceResponseDto>>
    {
        [JsonIgnore]
        public string UserPublicId { get; set; } = string.Empty;

        [Required, MaxLength(30)]
        public string Key { get; set; } = string.Empty;

        [Required]
        public bool? Enabled { get; set; }
    }

    public sealed class UpdateNotificationPreferenceResponseDto
    {
        public string Key { get; init; } = string.Empty;
        public bool Enabled { get; init; }
    }

    public sealed class Handler
        : IRequestHandler<Command, BaseResult<UpdateNotificationPreferenceResponseDto>>
    {
        private static readonly HashSet<string> AllowedKeys =
            new(StringComparer.OrdinalIgnoreCase)
            {
                "login",
                "release",
                "updates",
                "promotions",
            };

        private readonly IIdentityService _identityService;
        private readonly ILogger<Handler> _logger;

        public Handler(
            IIdentityService identityService,
            ILogger<Handler> logger)
        {
            _identityService = identityService;
            _logger = logger;
        }

        public async Task<BaseResult<UpdateNotificationPreferenceResponseDto>> Handle(
            Command request,
            CancellationToken cancellationToken)
        {
            using var op = OperationLogger.Start(
                _logger,
                "UpdateNotificationPreference",
                ("UserPublicId", request.UserPublicId),
                ("Key", request.Key));

            if (string.IsNullOrWhiteSpace(request.UserPublicId))
            {
                op.Warning("User ID is required.");
                return new BaseResult<UpdateNotificationPreferenceResponseDto>(
                    HttpStatusCode.BadRequest,
                    "User ID is required.");
            }

            if (string.IsNullOrWhiteSpace(request.Key))
            {
                op.Warning("Notification key is required.");
                return new BaseResult<UpdateNotificationPreferenceResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Notification key is required.");
            }

            if (request.Enabled is null)
            {
                op.Warning("Notification preference state is required.");
                return new BaseResult<UpdateNotificationPreferenceResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Notification preference state is required.");
            }

            var key = request.Key.Trim().ToLowerInvariant();

            if (!AllowedKeys.Contains(key))
            {
                op.Warning($"Invalid notification key: {key}");
                return new BaseResult<UpdateNotificationPreferenceResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Invalid notification key. Must be one of: login, release, updates, promotions.");
            }

            var updated = await _identityService.UpdateNotificationPreferenceAsync(
                request.UserPublicId,
                key,
                request.Enabled.Value,
                cancellationToken);

            if (!updated)
            {
                op.Warning($"User not found: {request.UserPublicId}");
                return new BaseResult<UpdateNotificationPreferenceResponseDto>(
                    HttpStatusCode.NotFound,
                    "User not found.");
            }

            op.Success(
                $"Notification preference '{key}' set to {request.Enabled.Value} " +
                $"for user {request.UserPublicId}.");

            return new BaseResult<UpdateNotificationPreferenceResponseDto>(
                HttpStatusCode.OK,
                "Notification preference updated.",
                new UpdateNotificationPreferenceResponseDto
                {
                    Key = key,
                    Enabled = request.Enabled.Value,
                });
        }
    }
}