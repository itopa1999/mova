using System.ComponentModel.DataAnnotations;
using System.Net;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mova.Application.Interfaces.Identity;
using Mova.Application.Interfaces.Persistence;
using Mova.Domain.Entities;
using Mova.Shared.Common;
using Mova.Shared.Logging;

namespace Mova.Application.BBL.Commands.Authentication;

public sealed class ResetPasswordCommand
{
    public class Command : IRequest<BaseResult>
    {
        public string UserPublicId { get; init; } = string.Empty;

        [MinLength(8, ErrorMessage = "Password must be at least 8 characters.")]
        public string NewPassword { get; init; } = string.Empty;
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
                "ResetPassword",
                ("UserPublicId", request.UserPublicId));

            if (string.IsNullOrWhiteSpace(request.UserPublicId))
            {
                op.Fail("UserPublicId is required.");
                return new BaseResult(
                    HttpStatusCode.BadRequest,
                    "UserPublicId is required.");
            }

            if (string.IsNullOrWhiteSpace(request.NewPassword))
            {
                op.Fail("New password is required.");
                return new BaseResult(
                    HttpStatusCode.BadRequest,
                    "New password is required.");
            }

            if (request.NewPassword.Length < 8)
            {
                op.Fail("Password must be at least 8 characters.");
                return new BaseResult(
                    HttpStatusCode.BadRequest,
                    "Password must be at least 8 characters.");
            }

            var user = await _identityService.GetByIdentifierAsync(
                request.UserPublicId,
                cancellationToken);

            if (user == null)
            {
                op.Fail($"User not found: {request.UserPublicId}");
                return new BaseResult(
                    HttpStatusCode.BadRequest,
                    "User not found.");
            }

            var (success, errorMessage) = await _identityService.ResetPasswordAsync(
                user.Id,
                request.NewPassword);

            if (!success)
            {
                op.Fail($"Password reset failed for user {request.UserPublicId}: {errorMessage}");
                return new BaseResult(
                    HttpStatusCode.BadRequest,
                    errorMessage);
            }

            await _unitOfWork.BeginTransactionAsync(cancellationToken);

            try
            {
                var refreshTokens = await _unitOfWork.Query<RefreshToken>()
                    .Where(rt => rt.UserPublicId == user.PublicId && rt.RevokedAt == null)
                    .ToListAsync(cancellationToken);

                foreach (var token in refreshTokens)
                {
                    token.RevokedAt = DateTimeOffset.UtcNow;
                    token.RevocationReason = "Password reset";
                    _unitOfWork.Update(token);
                }

                await _unitOfWork.SaveChangesAsync(cancellationToken);

                await _unitOfWork.CommitTransactionAsync(cancellationToken);

                op.Success($"Password reset successfully for user {request.UserPublicId}");

                return new BaseResult(
                    HttpStatusCode.OK,
                    "Password reset successfully.");
            }
            catch (DbUpdateException dbEx)
            {
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                op.Fail($"Database error while revoking tokens for user {request.UserPublicId}: {dbEx.Message}", dbEx);

                return new BaseResult(
                    HttpStatusCode.Conflict,
                    "An error occurred. Please try again.");
            }
            catch (Exception ex)
            {
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                op.Fail($"Password reset failed with exception for user {request.UserPublicId}: {ex.Message}", ex);

                return new BaseResult(
                    HttpStatusCode.InternalServerError,
                    "An error occurred while resetting your password. Please try again later.");
            }
        }
    }
}