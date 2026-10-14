using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text.Json.Serialization;
using MediatR;
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
    public class Command : IRequest<BaseResult<LoginResultDto>>
    {
        [Required, MaxLength(254)]
        [JsonPropertyName("emailOrPhone")]
        public string Identifier { get; init; } = string.Empty;

        [Required, MinLength(8), MaxLength(256)]
        public string Password { get; init; } = string.Empty;

        [Required, MaxLength(20)]
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
        public string? AccessToken { get; set; }
        public string? RefreshToken { get; set; }
        public DateTimeOffset? AccessTokenExpiresAt { get; set; }
    }

    public class LoginBlockedDto
    {
        public string UserPublicId { get; set; } = string.Empty;
        public string AccountStatus { get; set; } = string.Empty;
        public string StatusLabel { get; set; } = string.Empty;
        public string StatusDescription { get; set; } = string.Empty;
        public string? RestrictionReason { get; set; }
        public string? RestrictionReasonDetails { get; set; }
        public DateTimeOffset? RestrictionExpiresAt { get; set; }
        public DateTimeOffset? LockoutEndsAt { get; set; }
    }

    public class LoginResultDto
    {
        public LoginResponseDto? Data { get; set; }
        public LoginBlockedDto? Blocked { get; set; }
    }

    public class Handler : IRequestHandler<Command, BaseResult<LoginResultDto>>
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
                Platforms.Swagger,
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

        public async Task<BaseResult<LoginResultDto>> Handle(
            Command request,
            CancellationToken cancellationToken)
        {
            using var op = OperationLogger.Start(
                _logger,
                "LoginUser",
                ("Identifier", request.Identifier));

            if (!ValidPlatforms.Contains(request.Platform))
            {
                op.Warning($"Invalid platform: {request.Platform}");
                return new BaseResult<LoginResultDto>(
                    HttpStatusCode.BadRequest,
                    "Invalid platform specified.");
            }

            var user = await _identityService.GetByIdentifierAsync(
                request.Identifier,
                cancellationToken);

            if (user is null)
            {
                op.Warning("User not found.");
                return new BaseResult<LoginResultDto>(
                    HttpStatusCode.BadRequest,
                    "Invalid email or password.");
            }

            var permissions = await _identityService.GetPermissionsAsync(
                user.PublicId,
                cancellationToken);

            if (permissions is null)
            {
                op.Error($"Permissions unavailable for user {user.PublicId}.");
                return new BaseResult<LoginResultDto>(
                    HttpStatusCode.BadRequest,
                    "Invalid email or password.");
            }

            if (!permissions.CanLogin)
            {
                op.Warning(
                    $"Login blocked for user {user.PublicId}. " +
                    $"Status: {permissions.StatusLabel}, " +
                    $"Reason: {permissions.RestrictionReason}");

                return new BaseResult<LoginResultDto>(
                    HttpStatusCode.Forbidden,
                    permissions.StatusDescription,
                    new LoginResultDto
                    {
                        Blocked = BuildBlockedDto(user.PublicId, permissions),
                    });
            }

            if (await _identityService.IsLockedOutAsync(user.Id, cancellationToken))
            {
                var lockoutEnd = await _identityService.GetLockoutEndAsync(
                    user.Id, cancellationToken);

                op.Warning($"Account locked for user {user.PublicId}.");

                return new BaseResult<LoginResultDto>(
                    HttpStatusCode.Locked,
                    BuildLockoutMessage(lockoutEnd),
                    new LoginResultDto
                    {
                        Blocked = BuildBlockedDto(user.PublicId, permissions, lockoutEnd),
                    });
            }

            if (await _identityService.IsAccountVerifiedAsync(user.Id) is false)
            {
                op.Warning($"Account not verified for user {user.PublicId}.");
                return new BaseResult<LoginResultDto>(
                    HttpStatusCode.BadRequest,
                    "Please verify your account before logging in.");
            }

            var passwordIsValid = await _identityService.CheckPasswordAsync(
                user.Id,
                request.Password);

            if (!passwordIsValid)
            {
                await _identityService.RecordFailedAccessAsync(
                    user.Id, cancellationToken);

                if (await _identityService.IsLockedOutAsync(user.Id, cancellationToken))
                {
                    var lockoutEnd = await _identityService.GetLockoutEndAsync(
                        user.Id, cancellationToken);

                    op.Warning(
                        $"Account locked after failed attempt for user {user.PublicId}.");

                    return new BaseResult<LoginResultDto>(
                        HttpStatusCode.Locked,
                        BuildLockoutMessage(lockoutEnd),
                        new LoginResultDto
                        {
                            Blocked = BuildBlockedDto(user.PublicId, permissions, lockoutEnd),
                        });
                }

                op.Warning($"Invalid password for user {user.PublicId}.");
                return new BaseResult<LoginResultDto>(
                    HttpStatusCode.BadRequest,
                    "Invalid email or password.");
            }

            await _identityService.ResetFailedAccessAsync(user.Id, cancellationToken);

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
                user.FullName,
                request.Platform,
                roles);

            var (refreshToken, refreshTokenEntity) =
                await _refreshTokenService.CreateAsync(
                    user.PublicId,
                    cancellationToken);

            await _unitOfWork.AddAsync(refreshTokenEntity, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            var incomingDeviceId = request.DeviceId?.Trim();
            var previousDeviceId = user.LastKnownDeviceId?.Trim();

            var hasIncomingDevice = !string.IsNullOrWhiteSpace(incomingDeviceId);
            var hasPreviousDevice = !string.IsNullOrWhiteSpace(previousDeviceId);

            var isNewDevice =
                hasIncomingDevice &&
                hasPreviousDevice &&
                previousDeviceId != incomingDeviceId;

            op.Success(
                $"User {user.PublicId} logged in " +
                $"(Identifier: {request.Identifier}, NewDevice: {isNewDevice}).");

            if (isNewDevice && user.NotifyLoginAlerts)
            {
                SendNewDeviceAlert(
                    user.PublicId,
                    user.Email ?? string.Empty,
                    user.FirstName,
                    request.Platform,
                    incomingDeviceId!);
            }

            if (hasIncomingDevice && previousDeviceId != incomingDeviceId)
            {
                await _identityService.UpdateLastKnownDeviceAsync(
                    user.PublicId,
                    incomingDeviceId!,
                    cancellationToken);
            }

            return new BaseResult<LoginResultDto>(
                HttpStatusCode.OK,
                "Login successful.",
                new LoginResultDto
                {
                    Data = new LoginResponseDto
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
                    },
                });
        }

        // ─── Blocked-login DTO builder ────────────────────────

        private static LoginBlockedDto BuildBlockedDto(
            string userPublicId,
            UserPermissionsDto permissions,
            DateTimeOffset? lockoutEndsAt = null) =>
            new()
            {
                UserPublicId = userPublicId,
                AccountStatus = permissions.AccountStatus.ToString(),

                // Lockout is a different axis than account status. Never
                // inherit "Active / fully operational" — the FE would render
                // a success-looking screen on a blocked login.
                StatusLabel = lockoutEndsAt.HasValue
                    ? "Temporarily Locked"
                    : permissions.StatusLabel,

                StatusDescription = lockoutEndsAt.HasValue
                    ? "Your account is temporarily locked due to too many failed " +
                      "login attempts. It will unlock automatically once the " +
                      "lockout period ends."
                    : permissions.StatusDescription,

                RestrictionReason = permissions.RestrictionReason,
                RestrictionReasonDetails = permissions.RestrictionReasonDetails,
                RestrictionExpiresAt = permissions.RestrictionExpiresAt,
                LockoutEndsAt = lockoutEndsAt,
            };

        private static string BuildLockoutMessage(DateTimeOffset? lockoutEnd)
        {
            if (!lockoutEnd.HasValue)
            {
                return "Too many failed login attempts. Your account is locked. " +
                       "Please try again later.";
            }

            var remaining = lockoutEnd.Value - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                return "Too many failed login attempts. Your account was " +
                       "temporarily locked. Please try again.";
            }

            var timeText = remaining.TotalMinutes < 1
                ? $"{(int)Math.Ceiling(remaining.TotalSeconds)} second(s)"
                : remaining.TotalHours < 1
                    ? $"{(int)Math.Ceiling(remaining.TotalMinutes)} minute(s)"
                    : $"{(int)Math.Ceiling(remaining.TotalHours)} hour(s)";

            return $"Too many failed login attempts. Your account is locked. " +
                   $"Please try again in {timeText}.";
        }

        // ─── Alerts ──────────────────────────────────────────

        private void SendNewDeviceAlert(
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

            _notificationQueue.InAppNotificationAsync(
                userPublicId,
                NotificationType.System,
                "New device login",
                $"Your MOVA account was just accessed from a new device ({platformLabel}). " +
                $"If this wasn't you, please change your password and review your account.",
                "/settings/security",
                null,
                CancellationToken.None);

            if (string.IsNullOrWhiteSpace(email))
            {
                return;
            }

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
                "New device login to your MOVA account");
        }
    }
}