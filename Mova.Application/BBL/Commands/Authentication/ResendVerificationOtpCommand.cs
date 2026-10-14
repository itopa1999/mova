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
        [Required, EmailAddress, MaxLength(100)]
        public string Email { get; init; } = string.Empty;

        [Required, MaxLength(50)]
        public string Purpose { get; init; } = "account-verification";
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

        public async Task<BaseResult<ResendVerificationOtpResponseDto>> Handle(
            Command request,
            CancellationToken cancellationToken)
        {
            var identifier = !string.IsNullOrWhiteSpace(request.Email)
                ? request.Email
                : "unknown";

            using var op = OperationLogger.Start(
                _logger,
                "ResendVerificationOtp",
                ("Identifier", identifier),
                ("Purpose", request.Purpose));

            if (string.IsNullOrWhiteSpace(request.Email))
            {
                op.Warning("No email provided.");
                return new BaseResult<ResendVerificationOtpResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Email must be provided.");
            }

            var user = await _identityService.GetByIdentifierAsync(
                request.Email,
                cancellationToken);

            if (user is null)
            {
                op.Warning($"User not found for email: {request.Email}");
                return new BaseResult<ResendVerificationOtpResponseDto>(
                    HttpStatusCode.BadRequest,
                    "User not found.");
            }

            var otpPurpose = request.Purpose switch
            {
                "password-reset" => OtpPurpose.PasswordReset,
                _ => OtpPurpose.AccountVerification,
            };

            if (otpPurpose == OtpPurpose.AccountVerification)
            {
                var isVerified = await _identityService.IsAccountVerifiedAsync(user.Id);
                if (isVerified)
                {
                    op.Warning($"Account already verified for user {user.PublicId}.");
                    return new BaseResult<ResendVerificationOtpResponseDto>(
                        HttpStatusCode.BadRequest,
                        "Account is already verified.");
                }
            }

            var userPublicId = user.PublicId;

            await using var tx = await _unitOfWork.BeginAtomicAsync(cancellationToken);

            var existingOtps = await _unitOfWork.Query<OtpVerification>()
                .Where(o => o.UserPublicId == userPublicId
                            && o.Purpose == otpPurpose
                            && !o.IsUsed)
                .ToListAsync(cancellationToken);

            foreach (var existingOtp in existingOtps)
            {
                existingOtp.IsUsed = true;
                _unitOfWork.Update(existingOtp);
            }

            var otpCode = _otpService.GenerateOtp();

            var otp = new OtpVerification
            {
                UserPublicId = userPublicId,
                OtpCode = otpCode,
                Purpose = otpPurpose,
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(2),
                IsUsed = false,
            };

            await _unitOfWork.AddAsync(otp, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);

            op.Success(
                $"OTP resent for user {userPublicId} " +
                $"(purpose: {request.Purpose}, superseded: {existingOtps.Count}).");

            _notificationQueue.QueueOtpDelivery(
                user.FirstName,
                user.Email,
                user.PhoneNumber,
                otpCode,
                otpPurpose.ToString());

            return new BaseResult<ResendVerificationOtpResponseDto>(
                HttpStatusCode.OK,
                "Verification OTP resent successfully.",
                new ResendVerificationOtpResponseDto
                {
                    UserPublicId = userPublicId,
                    Data = "A new verification OTP has been sent to your email and phone.",
                });
        }
    }
}