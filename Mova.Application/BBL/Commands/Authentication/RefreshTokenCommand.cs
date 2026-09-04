using System.Net;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mova.Application.Interfaces.Identity;
using Mova.Application.Interfaces.Persistence;
using Mova.Application.Interfaces.Security;
using Mova.Shared.Common;
using Mova.Shared.Constants;
using Mova.Shared.Logging;


namespace Mova.Application.BBL.Commands.Authentication;

public sealed class RefreshTokenCommand
{
    public class Command : IRequest<BaseResult<RefreshTokenResponseDto>>
    {
        public string RefreshToken { get; set; } = string.Empty;
        public string Platform { get; init; } = Platforms.Mobile;
    }

    public class RefreshTokenResponseDto
    {
        public string AccessToken { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;
        public string Platform { get; set; } = Platforms.Mobile;
        public DateTimeOffset AccessTokenExpiresAt { get; set; }
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
            Platforms.Swagger
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

        public async Task<BaseResult<RefreshTokenResponseDto>> Handle(Command request, CancellationToken cancellationToken)
        {
            using var op = OperationLogger.Start(_logger, "RefreshToken",
                ("RefreshToken", request.RefreshToken),
                ("Platform", request.Platform)
            );

            if (!ValidPlatforms.Contains(request.Platform))
            {
                op.Fail($"Invalid platform: {request.Platform}");

                return new BaseResult<RefreshTokenResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Invalid platform specified."
                );
            }

            var refreshToken = await _refreshTokenService.ValidateAsync(request.RefreshToken, cancellationToken);

            if (refreshToken is null)
            {
                op.Fail("Invalid refresh token provided.");
                return new BaseResult<RefreshTokenResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Invalid refresh token.");
            }

            if (refreshToken.IsExpired)
            {
                op.Fail($"Refresh token expired (UserId: {refreshToken.UserPublicId})");
                return new BaseResult<RefreshTokenResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Refresh token has expired.");
            }

            if (refreshToken.IsRevoked)
            {
                op.Fail($"Refresh token already revoked (UserId: {refreshToken.UserPublicId})");
                return new BaseResult<RefreshTokenResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Refresh token has been revoked.");
            }

            await _unitOfWork.BeginTransactionAsync(cancellationToken);

            try
            {
                refreshToken.RevokedAt = DateTimeOffset.UtcNow;
                refreshToken.RevocationReason = "Token refreshed";
                _unitOfWork.Update(refreshToken);

                var user = await _identityService.GetByIdentifierAsync(
                        refreshToken.UserPublicId,
                        cancellationToken);

                if (user is null)
                {
                    op.Fail($"User not found for refresh token user {refreshToken.UserPublicId}");
                    return new BaseResult<RefreshTokenResponseDto>(
                        HttpStatusCode.BadRequest,
                        "User profile not found."
                    );
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

                var (newToken, newRefreshToken) = await _refreshTokenService.CreateAsync(user.PublicId, cancellationToken);

                await _unitOfWork.AddAsync(newRefreshToken, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                await _unitOfWork.CommitTransactionAsync(cancellationToken);

                op.Success($"Token refreshed successfully for user {refreshToken.UserPublicId}");

                return new BaseResult<RefreshTokenResponseDto>(
                    HttpStatusCode.OK,
                    "Token refreshed successfully.",
                    new RefreshTokenResponseDto
                    {
                        AccessToken = accessToken,
                        RefreshToken = newToken,
                        Platform = request.Platform,
                        AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddMinutes(15)
                    });
            }
            catch (Exception ex)
            {
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                op.Fail($"Refresh token failed for user {refreshToken.UserPublicId}", ex);
                throw;
            }
        }
    }
}
