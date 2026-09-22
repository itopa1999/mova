using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mova.Application.Interfaces.Identity;
using Mova.Application.Interfaces.Notification;
using Mova.Application.Interfaces.Persistence;
using Mova.Application.Interfaces.Security;
using Mova.Domain.Enums;
using Mova.Shared.Common;
using Mova.Shared.Constants;
using Mova.Shared.Logging;

namespace Mova.Application.BBL.Commands.Authentication;

public sealed class LoginUserCommand
{
    public class Command : IRequest<BaseResult<LoginResponseDto>>
    {
        [Required]
        [JsonPropertyName("emailOrPhone")]
        public string Identifier { get; init; } = string.Empty;

        [MinLength(8)]
        public string Password { get; init; } = string.Empty;

        public string Platform { get; init; } = Platforms.Mobile;

        [MaxLength(200)]
        public string? DeviceId { get; init; }
    }

    public class LoginResponseDto
    {
        public string UserPublicId { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Platform { get; set; } = Platforms.Mobile;
        public string? ProfilePicture { get; set; } = string.Empty;
        public decimal Balance { get; set; }
        public string AccessToken { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;
        public DateTimeOffset AccessTokenExpiresAt { get; set; }
    }

    public class Handler : IRequestHandler<Command, BaseResult<LoginResponseDto>>
    {
        private readonly IIdentityService _identityService;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IJwtTokenGenerator _jwtTokenGenerator;
        private readonly IRefreshTokenService _refreshTokenService;
        private readonly INotificationQueue _notificationQueue;
        private readonly ILogger<Handler> _logger;

        private static readonly HashSet<string> ValidPlatforms =
            new(StringComparer.OrdinalIgnoreCase)
            {
                Platforms.Web,
                Platforms.Mobile,
                Platforms.Swagger
            };

        public Handler(
            IIdentityService identityService,
            IUnitOfWork unitOfWork,
            IJwtTokenGenerator jwtTokenGenerator,
            IRefreshTokenService refreshTokenService,
            INotificationQueue notificationQueue,
            ILogger<Handler> logger)
        {
            _identityService = identityService;
            _unitOfWork = unitOfWork;
            _jwtTokenGenerator = jwtTokenGenerator;
            _refreshTokenService = refreshTokenService;
            _notificationQueue = notificationQueue;
            _logger = logger;
        }

        public async Task<BaseResult<LoginResponseDto>> Handle(
            Command request,
            CancellationToken cancellationToken)
        {
            using var op = OperationLogger.Start(
                _logger,
                "LoginUser",
                ("Identifier", request.Identifier));

            if (!ValidPlatforms.Contains(request.Platform))
            {
                op.Fail($"Invalid platform: {request.Platform}");
                return new BaseResult<LoginResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Invalid platform specified.");
            }

            var user = await _identityService.GetByIdentifierAsync(
                request.Identifier,
                cancellationToken);

            if (user == null)
            {
                op.Fail("User not found.");
                return new BaseResult<LoginResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Invalid email or password.");
            }

            if (await _identityService.IsAccountVerifiedAsync(user.Id) is false)
            {
                op.Fail($"Account not verified for user {user.PublicId}");
                return new BaseResult<LoginResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Please verify your account before logging in.");
            }

            var passwordIsValid = await _identityService.CheckPasswordAsync(
                user.Id,
                request.Password);

            if (!passwordIsValid)
            {
                op.Fail($"Invalid password for user {user.PublicId}");
                return new BaseResult<LoginResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Invalid email or password.");
            }

            var roles = await _identityService.GetRolesAsync(user.Id);

            var accessToken = _jwtTokenGenerator.GenerateToken(
                user.Id,
                user.PublicId,
                user.FirstName,
                user.OtherNames,
                user.LastName,
                user.Email,
                user.PhoneNumber,
                user.Balance.ToDecimal(),
                user.FullName ?? string.Empty,
                request.Platform,
                roles);

            var (refreshToken, refreshTokenEntity) =
                await _refreshTokenService.CreateAsync(
                    user.PublicId,
                    cancellationToken);

            await _unitOfWork.AddAsync(refreshTokenEntity, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // ─── Device check ────────────────────────────────
            var incomingDeviceId = request.DeviceId?.Trim();
            var previousDeviceId = user.LastKnownDeviceId?.Trim();

            var hasIncomingDevice =
                !string.IsNullOrWhiteSpace(incomingDeviceId);

            var hasPreviousDevice =
                !string.IsNullOrWhiteSpace(previousDeviceId);

            var isNewDevice =
                hasIncomingDevice &&
                hasPreviousDevice &&
                previousDeviceId != incomingDeviceId;

            op.Success(
                $"User {user.PublicId} logged in successfully " +
                $"(Identifier: {request.Identifier}, " +
                $"NewDevice: {isNewDevice})");

            // ─── Alert on new device ─────────────────────────
            if (isNewDevice && user.NotifyLoginAlerts)
            {
                try
                {
                    await SendNewDeviceAlertAsync(
                        user.PublicId,
                        user.Email ?? string.Empty,
                        user.FirstName ?? string.Empty,
                        request.Platform,
                        incomingDeviceId!);
                }
                catch (Exception ex)
                {
                    // Never let a notification failure break login
                    _logger.LogError(
                        ex,
                        "New-device alert failed for user {UserPublicId}.",
                        user.PublicId);
                }
            }

            // ─── Remember this device ────────────────────────
            if (hasIncomingDevice && previousDeviceId != incomingDeviceId)
            {
                try
                {
                    await _identityService.UpdateLastKnownDeviceAsync(
                        user.PublicId,
                        incomingDeviceId!,
                        cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Failed to update LastKnownDeviceId for user {UserPublicId}.",
                        user.PublicId);
                }
            }

            return new BaseResult<LoginResponseDto>(
                HttpStatusCode.OK,
                "Login successful.",
                new LoginResponseDto
                {
                    UserPublicId = user.PublicId,
                    Email = user.Email ?? string.Empty,
                    Phone = user.PhoneNumber ?? string.Empty,
                    FullName = user.FullName,
                    ProfilePicture = user.ProfilePicture,
                    Balance = user.Balance.ToDecimal(),
                    Platform = request.Platform,
                    AccessToken = accessToken,
                    RefreshToken = refreshToken,
                    AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddMinutes(15),
                });
        }

        // ─── Alerts ──────────────────────────────────────────

        private async Task SendNewDeviceAlertAsync(
            string userPublicId,
            string email,
            string firstName,
            string platform,
            string deviceId)
        {
            var platformLabel = string.IsNullOrWhiteSpace(platform)
                ? "a new device"
                : platform;

            var shortDeviceId = deviceId.Length > 8
                ? deviceId[..8]
                : deviceId;

            // ─── In-app ──────────────────────────────────────
            try
            {
                _notificationQueue.InAppNotificationAsync(
                    userPublicId,
                    NotificationType.System,
                    "New device login",
                    $"Your MOVA account was just accessed from a new device ({platformLabel}). " +
                    $"If this wasn't you, please change your password and review your account.",
                    "/settings/security",
                    null,
                    CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "In-app new-device notification failed for user {UserPublicId}.",
                    userPublicId);
            }

            // ─── Email ───────────────────────────────────────
            if (string.IsNullOrWhiteSpace(email))
            {
                return;
            }

            try
            {
                var subject = "New device login to your MOVA account";

                var body =
                    $"Hi {firstName},\n\n" +
                    $"We noticed a login to your MOVA account from a device we don't recognise.\n\n" +
                    $"Platform: {platformLabel}\n" +
                    $"Device reference: {shortDeviceId}\n" +
                    $"Time: {DateTimeOffset.UtcNow:MMM d, yyyy h:mm tt} UTC\n\n" +
                    $"If this was you, you can ignore this message — we'll remember this device.\n\n" +
                    $"If this wasn't you, please:\n" +
                    $"  1. Change your password immediately\n" +
                    $"  2. Review your recent activity\n" +
                    $"  3. Contact support if anything looks wrong\n\n" +
                    $"— The MOVA team";

                _notificationQueue.QueueNotificationEmail(
                    firstName,
                    email,
                    body,
                    subject);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Email new-device notification failed for user {UserPublicId}.",
                    userPublicId);
            }
        }
    }
}