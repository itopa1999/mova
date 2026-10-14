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
        [Required, MaxLength(100)]
        public string UserPublicId { get; init; } = string.Empty;

        [Required, RegularExpression(@"^\d{6}$", ErrorMessage = "Verification code must be exactly 6 digits.")]
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

        public async Task<BaseResult<VerifyPasswordTokenResponseDto>> Handle(
            Command request,
            CancellationToken cancellationToken)
        {
            using var op = OperationLogger.Start(
                _logger,
                "VerifyPasswordToken",
                ("UserPublicId", request.UserPublicId));

            if (string.IsNullOrWhiteSpace(request.UserPublicId))
            {
                op.Warning("UserPublicId is required.");
                return new BaseResult<VerifyPasswordTokenResponseDto>(
                    HttpStatusCode.BadRequest,
                    "UserPublicId is required.");
            }

            if (string.IsNullOrWhiteSpace(request.Token))
            {
                op.Warning("Token is required.");
                return new BaseResult<VerifyPasswordTokenResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Token is required.");
            }

            if (request.Token.Length != 6)
            {
                op.Warning($"Invalid token length: {request.Token.Length}.");
                return new BaseResult<VerifyPasswordTokenResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Token must be exactly 6 characters.");
            }

            var user = await _identityService.GetByIdentifierAsync(
                request.UserPublicId,
                cancellationToken);

            if (user is null)
            {
                op.Warning($"User not found: {request.UserPublicId}");
                return new BaseResult<VerifyPasswordTokenResponseDto>(
                    HttpStatusCode.BadRequest,
                    "User not found.");
            }

            var otpVerification = await _unitOfWork.Query<OtpVerification>()
                .Where(x => x.UserPublicId == user.PublicId
                            && x.Purpose == OtpPurpose.PasswordReset.ToString()
                            && !x.IsUsed)
                .OrderByDescending(x => x.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);

            if (otpVerification is null)
            {
                op.Warning($"No valid OTP found for user {request.UserPublicId}.");
                return new BaseResult<VerifyPasswordTokenResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Invalid OTP. Please request a new one.");
            }

            if (otpVerification.OtpCode != request.Token)
            {
                op.Warning($"Invalid OTP code for user {request.UserPublicId}.");
                return new BaseResult<VerifyPasswordTokenResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Invalid OTP code.");
            }

            if (otpVerification.ExpiresAt < DateTimeOffset.UtcNow)
            {
                op.Warning($"OTP expired for user {request.UserPublicId}.");
                return new BaseResult<VerifyPasswordTokenResponseDto>(
                    HttpStatusCode.BadRequest,
                    "OTP has expired. Please request a new one.");
            }

            await using var tx = await _unitOfWork.BeginAtomicAsync(cancellationToken);

            otpVerification.IsUsed = true;
            otpVerification.UsedAt = DateTimeOffset.UtcNow;
            _unitOfWork.Update(otpVerification);

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);

            op.Success($"Password reset OTP verified for user {request.UserPublicId}.");

            return new BaseResult<VerifyPasswordTokenResponseDto>(
                HttpStatusCode.OK,
                "OTP verified successfully. You can now reset your password.",
                new VerifyPasswordTokenResponseDto
                {
                    UserPublicId = user.PublicId,
                    IsVerified = true,
                });
        }
    }
}