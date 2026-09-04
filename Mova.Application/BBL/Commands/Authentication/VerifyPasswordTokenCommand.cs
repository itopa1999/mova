using System.ComponentModel.DataAnnotations;
using System.Net;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mova.Application.Interfaces.Identity;
using Mova.Application.Interfaces.Persistence;
using Mova.Domain.Entities;
using Mova.Shared.Common;
using Mova.Shared.Constants;
using Mova.Shared.Logging;

namespace Mova.Application.BBL.Commands.Authentication;

public sealed class VerifyPasswordTokenCommand
{
    public class Command : IRequest<BaseResult<VerifyPasswordTokenResponseDto>>
    {
        public string UserPublicId { get; init; } = string.Empty;

        [MinLength(6), MaxLength(6)]
        public string Token { get; init; } = string.Empty;
    }

    public class VerifyPasswordTokenResponseDto
    {
        public string UserPublicId { get; set; } = string.Empty;
        public bool IsVerified { get; set; }
    }

    public class Handler : IRequestHandler<Command, BaseResult<VerifyPasswordTokenResponseDto>>
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

        public async Task<BaseResult<VerifyPasswordTokenResponseDto>> Handle(Command request, CancellationToken cancellationToken)
        {
            using var op = OperationLogger.Start(
                _logger,
                "VerifyPasswordToken",
                ("UserPublicId", request.UserPublicId));

            // 1. Validate input
            if (string.IsNullOrWhiteSpace(request.UserPublicId))
            {
                op.Fail("UserPublicId is required.");
                return new BaseResult<VerifyPasswordTokenResponseDto>(
                    HttpStatusCode.BadRequest,
                    "UserPublicId is required.");
            }

            if (string.IsNullOrWhiteSpace(request.Token))
            {
                op.Fail("Token is required.");
                return new BaseResult<VerifyPasswordTokenResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Token is required.");
            }

            if (request.Token.Length != 6)
            {
                op.Fail($"Invalid token length: {request.Token.Length}");
                return new BaseResult<VerifyPasswordTokenResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Token must be exactly 6 characters.");
            }

            // 2. Get user
            var user = await _identityService.GetByIdentifierAsync(
                request.UserPublicId,
                cancellationToken);

            if (user == null)
            {
                op.Fail($"User not found: {request.UserPublicId}");
                return new BaseResult<VerifyPasswordTokenResponseDto>(
                    HttpStatusCode.BadRequest,
                    "User not found.");
            }

            // 3. Get OTP verification
            var otpVerification = await _unitOfWork.Query<OtpVerification>()
                .Where(x => x.UserPublicId == user.PublicId
                            && x.Purpose == OtpPurpose.PasswordReset.ToString()
                            && !x.IsUsed)
                .OrderByDescending(x => x.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);

            if (otpVerification is null)
            {
                op.Fail($"No valid OTP found for user {request.UserPublicId}");
                return new BaseResult<VerifyPasswordTokenResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Invalid OTP. Please request a new one.");
            }

            // 4. Validate OTP
            if (otpVerification.OtpCode != request.Token)
            {
                op.Fail($"Invalid OTP code for user {request.UserPublicId}");
                return new BaseResult<VerifyPasswordTokenResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Invalid OTP code.");
            }

            if (otpVerification.ExpiresAt < DateTimeOffset.UtcNow)
            {
                op.Fail($"OTP expired for user {request.UserPublicId}");
                return new BaseResult<VerifyPasswordTokenResponseDto>(
                    HttpStatusCode.BadRequest,
                    "OTP has expired. Please request a new one.");
            }

            if (otpVerification.IsUsed)
            {
                op.Fail($"OTP already used for user {request.UserPublicId}");
                return new BaseResult<VerifyPasswordTokenResponseDto>(
                    HttpStatusCode.BadRequest,
                    "This OTP has already been used.");
            }

            // 5. Begin transaction
            await _unitOfWork.BeginTransactionAsync(cancellationToken);

            try
            {
                // 6. Mark OTP as used
                otpVerification.IsUsed = true;
                otpVerification.UsedAt = DateTimeOffset.UtcNow;
                _unitOfWork.Update(otpVerification);

                // 7. Save changes
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                // 8. Commit transaction
                await _unitOfWork.CommitTransactionAsync(cancellationToken);

                op.Success($"Password reset OTP verified for user {request.UserPublicId}");

                return new BaseResult<VerifyPasswordTokenResponseDto>(
                    HttpStatusCode.OK,
                    "OTP verified successfully. You can now reset your password.",
                    new VerifyPasswordTokenResponseDto
                    {
                        UserPublicId = user.PublicId,
                        IsVerified = true
                    });
            }
            catch (DbUpdateException dbEx)
            {
                // Rollback immediately on database error
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                op.Fail($"Database error while verifying OTP for user {request.UserPublicId}: {dbEx.Message}", dbEx);

                return new BaseResult<VerifyPasswordTokenResponseDto>(
                    HttpStatusCode.Conflict,
                    "A database conflict occurred. Please try again.");
            }
            catch (Exception ex)
            {
                // Rollback immediately on any error
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                op.Fail($"OTP verification failed for user {request.UserPublicId}: {ex.Message}", ex);

                return new BaseResult<VerifyPasswordTokenResponseDto>(
                    HttpStatusCode.InternalServerError,
                    "An error occurred while verifying your OTP. Please try again later.");
            }
        }
    }
}