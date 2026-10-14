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

public sealed class ForgotPasswordCommand
{
    public class Command : IRequest<BaseResult<ForgotPasswordResponseDto>>
    {
        [Required, EmailAddress, MaxLength(100)]
        public string? Email { get; init; }
    }

    public class ForgotPasswordResponseDto
    {
        public string UserPublicId { get; set; } = string.Empty;
        public string Data { get; set; } = string.Empty;
    }

    public class Handler : IRequestHandler<Command, BaseResult<ForgotPasswordResponseDto>>
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

        public async Task<BaseResult<ForgotPasswordResponseDto>> Handle(
            Command request,
            CancellationToken cancellationToken)
        {
            var identifier = !string.IsNullOrWhiteSpace(request.Email)
                ? request.Email
                : "unknown";

            using var op = OperationLogger.Start(
                _logger,
                "ForgotPassword",
                ("Identifier", identifier));

            if (string.IsNullOrWhiteSpace(request.Email))
            {
                op.Warning("No email provided.");
                return new BaseResult<ForgotPasswordResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Email must be provided.");
            }

            var user = await _identityService.GetByIdentifierAsync(
                request.Email,
                cancellationToken);

            if (user is null)
            {
                // Generic response — never reveal whether the account exists.
                op.Success("Account not found — returning generic response.");
                return new BaseResult<ForgotPasswordResponseDto>(
                    HttpStatusCode.OK,
                    "If an account exists with this email or phone number, a reset OTP will be sent.",
                    new ForgotPasswordResponseDto
                    {
                        UserPublicId = string.Empty,
                        Data = "If an account exists, OTP has been sent.",
                    });
            }

            if (await _identityService.IsAccountVerifiedAsync(user.Id) is false)
            {
                op.Warning($"Account not verified: {user.PublicId}");
                return new BaseResult<ForgotPasswordResponseDto>(
                    HttpStatusCode.BadRequest,
                    "User account is not verified. Please verify your account first.");
            }

            var userPublicId = user.PublicId;
            var otpCode = _otpService.GenerateOtp();

            await using var tx = await _unitOfWork.BeginAtomicAsync(cancellationToken);

            var otp = new OtpVerification
            {
                UserPublicId = userPublicId,
                OtpCode = otpCode,
                Purpose = OtpPurpose.PasswordReset,
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(2),
                CreatedAt = DateTimeOffset.UtcNow,
                IsUsed = false,
            };

            await _unitOfWork.AddAsync(otp, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);

            op.Success($"OTP generated for user {userPublicId} (password reset).");

            // Fire-and-forget. NotificationQueue.TryEnqueue owns failure handling.
            _notificationQueue.QueueForgotPasswordOtp(
                user.Email ?? string.Empty,
                user.PhoneNumber,
                otpCode);

            return new BaseResult<ForgotPasswordResponseDto>(
                HttpStatusCode.OK,
                "If an account exists with this email or phone number, a reset OTP will be sent.",
                new ForgotPasswordResponseDto
                {
                    UserPublicId = userPublicId,
                    Data = "OTP sent successfully.",
                });
        }
    }
}