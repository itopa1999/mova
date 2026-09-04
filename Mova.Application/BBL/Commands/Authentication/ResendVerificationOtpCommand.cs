using System.ComponentModel.DataAnnotations;
using System.Net;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mova.Application.Interfaces.Identity;
using Mova.Application.Interfaces.Notification;
using Mova.Application.Interfaces.Persistence;
using Mova.Application.Interfaces.Security;
using Mova.Domain.Entities;
using Mova.Shared.Common;
using Mova.Shared.Constants;
using Mova.Shared.Logging;

namespace Mova.Application.BBL.Commands.Authentication;

public sealed class ResendVerificationOtpCommand
{
    public class Command : IRequest<BaseResult<ResendVerificationOtpResponseDto>>
    {
        [EmailAddress]
        public string Email { get; init; } = string.Empty;
    }

    public class ResendVerificationOtpResponseDto
    {
        public string UserPublicId { get; set; } = string.Empty;
        public string Data { get; set; } = string.Empty;
    }

    public class Handler : IRequestHandler<Command, BaseResult<ResendVerificationOtpResponseDto>>
    {
        private readonly IIdentityService _identityService;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IOtpService _otpService;
        private readonly IEmailService _emailService;
        private readonly ISmsService _smsService;
        private readonly ILogger<Handler> _logger;

        public Handler(
            IIdentityService identityService,
            IUnitOfWork unitOfWork,
            IOtpService otpService,
            IEmailService emailService,
            ISmsService smsService,
            ILogger<Handler> logger)
        {
            _identityService = identityService;
            _unitOfWork = unitOfWork;
            _otpService = otpService;
            _emailService = emailService;
            _smsService = smsService;
            _logger = logger;
        }

        public async Task<BaseResult<ResendVerificationOtpResponseDto>> Handle(Command request, CancellationToken cancellationToken)
        {
            var identifier = !string.IsNullOrWhiteSpace(request.Email) ? request.Email : string.Empty;
            
            using var op = OperationLogger.Start(
                _logger, 
                "ResendVerificationOtp",
                ("Identifier", identifier ?? "unknown"));

            // 1. Validate email
            if (string.IsNullOrWhiteSpace(request.Email))
            {
                op.Fail("No email provided.");
                return new BaseResult<ResendVerificationOtpResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Email must be provided.");
            }

            // 2. Get user
            var user = await _identityService.GetByIdentifierAsync(
                request.Email,
                cancellationToken);

            if (user == null)
            {
                op.Fail($"User not found for email: {request.Email}");
                return new BaseResult<ResendVerificationOtpResponseDto>(
                    HttpStatusCode.BadRequest,
                    "User not found.");
            }

            // 3. Check if account is already verified
            var isVerified = await _identityService.IsAccountVerifiedAsync(user.Id);
            if (isVerified)
            {
                op.Fail($"Account already verified for user {user.PublicId}");
                return new BaseResult<ResendVerificationOtpResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Account is already verified.");
            }

            // 4. Begin transaction
            await _unitOfWork.BeginTransactionAsync(cancellationToken);

            try
            {
                // 5. Generate OTP
                var otpCode = _otpService.GenerateOtp();

                var otp = new OtpVerification
                {
                    UserPublicId = user.PublicId,
                    OtpCode = otpCode,
                    Purpose = OtpPurpose.AccountVerification,
                    ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(2),
                    IsUsed = false
                };

                // 6. Save OTP
                await _unitOfWork.AddAsync(otp, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                // 7. Commit transaction
                await _unitOfWork.CommitTransactionAsync(cancellationToken);

                // 8. Send OTP via email and SMS (don't fail on send errors)
                try
                {
                    await _emailService.SendOtpAsync(
                        user.FirstName ?? "Customer", 
                        user.Email, 
                        otpCode, 
                        cancellationToken);
                }
                catch (Exception emailEx)
                {
                    op.Fail($"Failed to send email OTP: {emailEx.Message}", emailEx);
                }

                try
                {
                    await _smsService.SendOtpAsync(
                        user.PhoneNumber, 
                        otpCode, 
                        cancellationToken);
                }
                catch (Exception smsEx)
                {
                    op.Fail($"Failed to send SMS OTP: {smsEx.Message}", smsEx);
                }

                op.Success($"OTP resent successfully for user {user.PublicId}");

                return new BaseResult<ResendVerificationOtpResponseDto>(
                    HttpStatusCode.OK,
                    "Verification OTP resent successfully.",
                    new ResendVerificationOtpResponseDto
                    {
                        UserPublicId = user.PublicId,
                        Data = "A new verification OTP has been sent to your email and phone."
                    });
            }
            catch (DbUpdateException dbEx)
            {
                // Rollback immediately on database error
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                op.Fail($"Database error while resending OTP for user {user.PublicId}: {dbEx.Message}", dbEx);

                return new BaseResult<ResendVerificationOtpResponseDto>(
                    HttpStatusCode.Conflict,
                    "An error occurred. Please try again.");
            }
            catch (Exception ex)
            {
                // Rollback immediately on any error
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                op.Fail($"Resend OTP failed for user {user.PublicId}: {ex.Message}", ex);

                return new BaseResult<ResendVerificationOtpResponseDto>(
                    HttpStatusCode.InternalServerError,
                    "An error occurred while resending the verification OTP. Please try again later.");
            }
        }
    }
}