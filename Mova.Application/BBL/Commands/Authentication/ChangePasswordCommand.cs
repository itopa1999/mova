using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mova.Application.Interfaces.Identity;
using Mova.Application.Interfaces.Persistence;
using Mova.Domain.Entities;
using Mova.Shared.Common;
using Mova.Shared.Logging;

namespace Mova.Application.BBL.Commands.Authentication;

public sealed class ChangePasswordCommand
{
    public class Command : IRequest<BaseResult>
    {
        [JsonIgnore]
        public string UserPublicId { get; set; } = string.Empty;

        [MinLength(8, ErrorMessage = "Password must be at least 8 characters.")]
        public string OldPassword { get; init; } = string.Empty;

        [MinLength(8, ErrorMessage = "Password must be at least 8 characters.")]
        public string NewPassword { get; init; } = string.Empty;

        [MinLength(8, ErrorMessage = "Password must be at least 8 characters.")]
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

        public async Task<BaseResult> Handle(Command request, CancellationToken cancellationToken)
        {
            using var op = OperationLogger.Start(
                _logger,
                "ChangePassword",
                ("UserId", request.UserPublicId));

            // 1. Validate input
            if (request.NewPassword != request.ConfirmPassword)
            {
                op.Fail("New password and confirmation password do not match.");
                return new BaseResult(
                    HttpStatusCode.BadRequest,
                    "New password and confirmation password do not match.");
            }

            if (request.OldPassword == request.NewPassword)
            {
                op.Fail("New password cannot be the same as old password.");
                return new BaseResult(
                    HttpStatusCode.BadRequest,
                    "New password cannot be the same as the old password.");
            }

            // 2. Get user
            var user = await _identityService.GetByIdentifierAsync(
                request.UserPublicId,
                cancellationToken);

            if (user == null)
            {
                op.Fail($"User not found (UserId: {request.UserPublicId})");
                return new BaseResult(
                    HttpStatusCode.BadRequest,
                    "User not found.");
            }

            // 3. Begin transaction
            await _unitOfWork.BeginTransactionAsync(cancellationToken);

            try
            {
                // 4. Change password
                var (success, errorMessage) = await _identityService.ChangePasswordAsync(
                    user.Id,
                    request.OldPassword,
                    request.NewPassword);

                if (!success)
                {
                    op.Fail($"Password change failed: {errorMessage}");
                    await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                    return new BaseResult(HttpStatusCode.BadRequest, errorMessage);
                }

                // 5. Revoke all refresh tokens
                var refreshTokens = await _unitOfWork.Query<RefreshToken>()
                    .Where(rt => rt.UserPublicId == user.PublicId && rt.RevokedAt == null)
                    .ToListAsync(cancellationToken);

                foreach (var token in refreshTokens)
                {
                    token.RevokedAt = DateTimeOffset.UtcNow;
                    token.RevocationReason = "Password changed by user";
                    _unitOfWork.Update(token);
                }

                // 6. Save changes
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                // 7. Commit transaction
                await _unitOfWork.CommitTransactionAsync(cancellationToken);

                op.Success($"Password changed successfully for user {user.PublicId}");

                return new BaseResult(
                    HttpStatusCode.OK,
                    "Password changed successfully.");
            }
            catch (DbUpdateException dbEx)
            {
                // ✅ Rollback immediately on database error
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                op.Fail($"Database error while changing password: {dbEx.Message}", dbEx);

                return new BaseResult(
                    HttpStatusCode.Conflict,
                    "A database conflict occurred. Please try again.");
            }
            catch (Exception ex)
            {
                // ✅ Rollback immediately on any error
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                op.Fail($"Password change failed with exception: {ex.Message}", ex);

                return new BaseResult(
                    HttpStatusCode.InternalServerError,
                    "An error occurred while changing your password. Please try again later.");
            }
        }
    }
}