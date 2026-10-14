using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.Extensions.Logging;
using Mova.Application.Interfaces.Persistence;
using Mova.Application.Interfaces.Security;
using Mova.Shared.Common;
using Mova.Shared.Logging;

namespace Mova.Application.BBL.Commands.Authentication;

public sealed class LogoutCommand
{
    public class Command : IRequest<BaseResult>
    {
        [JsonIgnore]
        public string UserPublicId { get; set; } = string.Empty;

        [MaxLength(4096)]
        public string RefreshToken { get; set; } = string.Empty;
    }

    public class Handler : IRequestHandler<Command, BaseResult>
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IRefreshTokenService _refreshTokenService;
        private readonly ILogger<Handler> _logger;

        public Handler(
            IUnitOfWork unitOfWork,
            IRefreshTokenService refreshTokenService,
            ILogger<Handler> logger)
        {
            _unitOfWork = unitOfWork;
            _refreshTokenService = refreshTokenService;
            _logger = logger;
        }

        public async Task<BaseResult> Handle(
            Command request,
            CancellationToken cancellationToken)
        {
            using var op = OperationLogger.Start(
                _logger,
                "Logout",
                ("UserPublicId", request.UserPublicId));

            var refreshToken = await _refreshTokenService.ValidateAsync(
                request.RefreshToken,
                cancellationToken);

            if (refreshToken is null)
            {
                op.Warning("Invalid refresh token provided.");
                return new BaseResult(
                    HttpStatusCode.Unauthorized,
                    "Invalid refresh token.");
            }

            if (refreshToken.IsExpired)
            {
                op.Warning($"Refresh token expired for user {request.UserPublicId}.");
                return new BaseResult(
                    HttpStatusCode.Unauthorized,
                    "Refresh token has expired.");
            }

            if (refreshToken.IsRevoked)
            {
                op.Warning($"Refresh token already revoked for user {request.UserPublicId}.");
                return new BaseResult(
                    HttpStatusCode.Unauthorized,
                    "Refresh token has been revoked.");
            }

            if (refreshToken.UserPublicId != request.UserPublicId)
            {
                op.Warning(
                    $"Token owner mismatch. Token: {refreshToken.UserPublicId}, " +
                    $"Request: {request.UserPublicId}.");
                return new BaseResult(
                    HttpStatusCode.Unauthorized,
                    "Token does not belong to the authenticated user.");
            }

            await using var tx = await _unitOfWork.BeginAtomicAsync(cancellationToken);

            refreshToken.RevokedAt = DateTimeOffset.UtcNow;
            _unitOfWork.Update(refreshToken);

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);

            op.Success($"User {request.UserPublicId} logged out successfully.");

            return new BaseResult(HttpStatusCode.OK, "Logout Successful");
        }
    }
}