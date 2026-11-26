using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mova.Application.Interfaces.Identity;
using Mova.Application.Interfaces.Persistence;
using Mova.Domain.Entities;
using Mova.Shared.Logging;
using Mova.Shared.Common.Results;
using static Mova.Application.Helpers.ExtensionHelpers;
namespace Mova.Application.BBL.Commands.Authentication;

public sealed class AdminChangePasswordCommand
{
    public class Command : IRequest<BaseResult>
    {
        [JsonIgnore]
        public string UserPublicId { get; set; } = string.Empty;

        [Required, MinLength(8, ErrorMessage = "Password must be at least 8 characters."), MaxLength(256)]
        public string OldPassword { get; init; } = string.Empty;

        [Required, MinLength(8, ErrorMessage = "Password must be at least 8 characters."), MaxLength(256)]
        public string NewPassword { get; init; } = string.Empty;

        [Required, MinLength(8, ErrorMessage = "Password must be at least 8 characters."), MaxLength(256)]
        public string ConfirmPassword { get; init; } = string.Empty;
    }

    public class Handler : IRequestHandler<Command, BaseResult>
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IIdentityService _identityService;
        private readonly ILogger<Handler> _logger;

        public Handler(
            IUnitOfWork unitOfWork,
            IIdentityService identityService,
            ILogger<Handler> logger)
        {
            _unitOfWork = unitOfWork;
            _identityService = identityService;
            _logger = logger;
        }

        public async Task<BaseResult> Handle(
            Command request,
            CancellationToken cancellationToken)
        {
            using var op = OperationLogger.Start(
                _logger,
                "ChangePassword",
                ("UserId", request.UserPublicId));

            if (request.NewPassword != request.ConfirmPassword)
            {
                op.Warning("New password and confirmation password do not match.");
                return new BaseResult(
                    HttpStatusCode.BadRequest,
                    "New password and confirmation password do not match.");
            }

            if (request.OldPassword == request.NewPassword)
            {
                op.Warning("New password cannot be the same as old password.");
                return new BaseResult(
                    HttpStatusCode.BadRequest,
                    "New password cannot be the same as the old password.");
            }

            var user = await _unitOfWork.Query<User>()
                .FirstOrDefaultAsync(x => x.PublicId == request.UserPublicId, cancellationToken);

            if (user is null)
                return new BaseResult(HttpStatusCode.Unauthorized, "User not found.");

            var roles = await _identityService.GetRolesAsync(user.UserId, cancellationToken);

            if (!AdminAuthorization.HasAdminRole(roles))
            {
                op.Warning($"User {user.PublicId} denied admin login. Missing admin role.");
                return new BaseResult(
                    HttpStatusCode.Forbidden,
                    "Access denied. You do not have administrator privileges.");
            }

            await using var tx = await _unitOfWork.BeginAtomicAsync(cancellationToken);

            var (success, errorMessage) = await _identityService.ChangePasswordAsync(user.UserId, request.OldPassword, request.NewPassword, cancellationToken);

            if (!success)
            {
                op.Warning($"Password change failed: {errorMessage}");
                return new BaseResult(HttpStatusCode.BadRequest, errorMessage);
            }

            var refreshTokens = await _unitOfWork.Query<RefreshToken>()
                .Where(rt => rt.UserPublicId == user.PublicId && rt.RevokedAt == null)
                .ToListAsync(cancellationToken);

            foreach (var token in refreshTokens)
            {
                token.RevokedAt = DateTimeOffset.UtcNow;
                token.RevocationReason = "Password changed by user";
                _unitOfWork.Update(token);
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);

            op.Success(
                $"Password changed successfully for user {user.PublicId}. " +
                $"Revoked {refreshTokens.Count} refresh token(s).");

            return new BaseResult(
                HttpStatusCode.OK,
                "Password changed successfully.");
        }
    }
}