using System.ComponentModel.DataAnnotations;
using System.Net;
using MediatR;
using Microsoft.Extensions.Logging;
using Mova.Application.Interfaces.Identity;
using Mova.Application.Interfaces.Persistence;
using Mova.Application.Interfaces.Security;
using Mova.Shared.Common;
using Mova.Shared.Constants;
using Mova.Shared.Logging;
using Mova.Shared.Common.Results;
using static Mova.Application.Helpers.ExtensionHelpers;

namespace Mova.Application.BBL.Commands.Authentication;

public sealed class AdminRefreshTokenCommand
{
    public class Command : IRequest<BaseResult<RefreshTokenResponseDto>>
    {
        [MaxLength(4096)]
        public string RefreshToken { get; set; } = string.Empty;

    }

    public class RefreshTokenResponseDto
    {
        public string UserPublicId { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string? AccessToken { get; set; }
        public string? RefreshToken { get; set; }
    }

    public class Handler : IRequestHandler<Command, BaseResult<RefreshTokenResponseDto>>
    {
        private readonly IIdentityService _identityService;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IJwtTokenGenerator _jwtTokenGenerator;
        private readonly IRefreshTokenService _refreshTokenService;
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
            ILogger<Handler> logger)
        {
            _identityService = identityService;
            _unitOfWork = unitOfWork;
            _jwtTokenGenerator = jwtTokenGenerator;
            _refreshTokenService = refreshTokenService;
            _logger = logger;
        }

        public async Task<BaseResult<RefreshTokenResponseDto>> Handle(
            Command request,
            CancellationToken cancellationToken)
        {
            using var op = OperationLogger.Start(
                _logger,
                "RefreshToken",
                ("TokenPrefix", RedactToken(request.RefreshToken)));

            var refreshToken = await _refreshTokenService.ValidateAsync(
                request.RefreshToken,
                cancellationToken);

            if (refreshToken is null)
            {
                op.Warning("Invalid refresh token provided.");
                return new BaseResult<RefreshTokenResponseDto>(
                    HttpStatusCode.Unauthorized,
                    "Invalid refresh token.");
            }

            if (refreshToken.IsExpired)
            {
                op.Warning($"Refresh token expired for user {refreshToken.UserPublicId}.");
                return new BaseResult<RefreshTokenResponseDto>(
                    HttpStatusCode.Unauthorized,
                    "Refresh token has expired.");
            }

            if (refreshToken.IsRevoked)
            {
                op.Warning($"Refresh token already revoked for user {refreshToken.UserPublicId}.");
                return new BaseResult<RefreshTokenResponseDto>(
                    HttpStatusCode.Unauthorized,
                    "Refresh token has been revoked.");
            }

            await using var tx = await _unitOfWork.BeginAtomicAsync(cancellationToken);

            refreshToken.RevokedAt = DateTimeOffset.UtcNow;
            refreshToken.RevocationReason = "Token refreshed";
            _unitOfWork.Update(refreshToken);

            var user = await _identityService.GetByIdentifierAsync(
                refreshToken.UserPublicId,
                cancellationToken);

            if (user is null)
            {
                op.Warning(
                    $"User not found for refresh token: {refreshToken.UserPublicId}.");
                return new BaseResult<RefreshTokenResponseDto>(
                    HttpStatusCode.Unauthorized,
                    "Session is no longer valid.");
            }

            var roles = await _identityService.GetRolesAsync(user.Id, cancellationToken);

            if (!AdminAuthorization.HasAdminRole(roles))
            {
                op.Warning($"User {user.PublicId} denied admin login. Missing admin role.");
                return new BaseResult<RefreshTokenResponseDto>(
                    HttpStatusCode.Forbidden,
                    "Access denied. You do not have administrator privileges.");
            }

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
                roles);

            var (newToken, newRefreshTokenEntity) =
                await _refreshTokenService.CreateAsync(
                    user.PublicId,
                    cancellationToken);

            await _unitOfWork.AddAsync(newRefreshTokenEntity, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);

            op.Success($"Token refreshed for user {refreshToken.UserPublicId}.");

            return new BaseResult<RefreshTokenResponseDto>(
                HttpStatusCode.OK,
                "Token refreshed successfully.",
                new RefreshTokenResponseDto
                {
                    UserPublicId = user.PublicId,
                    FullName = user.FullName,
                    AccessToken = accessToken,
                    RefreshToken = newToken,
                });
        }

        private static string RedactToken(string token)
        {
            if (string.IsNullOrEmpty(token))
            {
                return "(empty)";
            }

            return token.Length <= 8
                ? "(short)"
                : token[..8] + "…";
        }
    }
}