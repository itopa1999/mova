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
        public string RefreshToken { get; init; } = string.Empty;
    }

    public class Handler : IRequestHandler<Command, BaseResult>
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IRefreshTokenService _refreshTokenService;
        private readonly ILogger<Handler> _logger;

        public Handler(IUnitOfWork unitOfWork, IRefreshTokenService refreshTokenService, ILogger<Handler> logger)
        {
            _unitOfWork = unitOfWork;
            _refreshTokenService = refreshTokenService;
            _logger = logger;
        }

        public async Task<BaseResult> Handle(Command request, CancellationToken cancellationToken)
        {
            using var op = OperationLogger.Start(_logger, "Logout",
                ("UserPublicId", request.UserPublicId)
            );

            var refreshToken = await _refreshTokenService.ValidateAsync(request.RefreshToken, cancellationToken);

            if (refreshToken is null)
            {
                op.Fail("Invalid refresh token provided.");
                return new BaseResult(HttpStatusCode.Unauthorized, "Invalid refresh token.");
            }

            if (refreshToken.IsExpired)
            {
                op.Fail($"Refresh token expired (UserId: {request.UserPublicId})");
                return new BaseResult(HttpStatusCode.Unauthorized, "Refresh token has expired.");
            }

            if (refreshToken.IsRevoked)
            {
                op.Fail($"Refresh token already revoked (UserId: {request.UserPublicId})");
                return new BaseResult(HttpStatusCode.Unauthorized, "Refresh token has been revoked.");
            }

            if (refreshToken.UserPublicId != request.UserPublicId)
            {
                op.Fail($"Token owner mismatch: token belongs to {refreshToken.UserPublicId}, request user is {request.UserPublicId}");
                return new BaseResult(HttpStatusCode.Unauthorized, "Token does not belong to the authenticated user.");
            }

            await _unitOfWork.BeginTransactionAsync(cancellationToken);

            try
            {
                refreshToken.RevokedAt = DateTimeOffset.UtcNow;
                _unitOfWork.Update(refreshToken);

                await _unitOfWork.SaveChangesAsync(cancellationToken);
                await _unitOfWork.CommitTransactionAsync(cancellationToken);

                op.Success($"User {request.UserPublicId} logged out successfully.");
                return new BaseResult(HttpStatusCode.OK, "Logout Successful");
            }
            catch (Exception ex)
            {
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                op.Fail($"Logout failed for user {request.UserPublicId}", ex);
                throw;
            }
        }
    }
}
