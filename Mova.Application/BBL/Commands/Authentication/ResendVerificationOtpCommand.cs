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
        private readonly INotificationQueue _notificationQueue;
        private readonly ILogger<Handler> _logger;

        public Handler(
            IIdentityService identityService,
            IUnitOfWork unitOfWork,
            IOtpService otpService,
            INotificationQueue notificationQueue,
            ILogger<Handler> logger)
        {
            _identityService = identityService;
            _unitOfWork = unitOfWork;
            _otpService = otpService;
            _notificationQueue = notificationQueue;
            _logger = logger;
        }

        public async Task<BaseResult<ResendVerificationOtpResponseDto>> Handle(Command request, CancellationToken cancellationToken)
        {
            var identifier = !string.IsNullOrWhiteSpace(request.Email) ? request.Email : string.Empty;
            
            using var op = OperationLogger.Start(
                _logger, 
                "ResendVerificationOtp",
                ("Identifier", identifier ?? "unknown"));

            if (string.IsNullOrWhiteSpace(request.Email))
            {
                op.Fail("No email provided.");
                return new BaseResult<ResendVerificationOtpResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Email must be provided.");
            }

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

            var isVerified = await _identityService.IsAccountVerifiedAsync(user.Id);
            if (isVerified)
            {
                op.Fail($"Account already verified for user {user.PublicId}");
                return new BaseResult<ResendVerificationOtpResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Account is already verified.");
            }

            await _unitOfWork.BeginTransactionAsync(cancellationToken);

            try
            {
                var otpCode = _otpService.GenerateOtp();

                var otp = new OtpVerification
                {
                    UserPublicId = user.PublicId,
                    OtpCode = otpCode,
                    Purpose = OtpPurpose.AccountVerification,
                    ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(2),
                    IsUsed = false
                };

                await _unitOfWork.AddAsync(otp, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                await _unitOfWork.CommitTransactionAsync(cancellationToken);

                _notificationQueue.QueueOtpDelivery(
                    user.FirstName,
                    user.Email,
                    user.PhoneNumber,
                    otpCode);

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
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                op.Fail($"Database error while resending OTP for user {user.PublicId}: {dbEx.Message}", dbEx);

                return new BaseResult<ResendVerificationOtpResponseDto>(
                    HttpStatusCode.Conflict,
                    "An error occurred. Please try again.");
            }
            catch (Exception ex)
            {
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                op.Fail($"Resend OTP failed for user {user.PublicId}: {ex.Message}", ex);

                return new BaseResult<ResendVerificationOtpResponseDto>(
                    HttpStatusCode.InternalServerError,
                    "An error occurred while resending the verification OTP. Please try again later.");
            }
        }
    }
}