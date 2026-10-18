using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mova.Application.Interfaces.Identity;
using Mova.Application.Interfaces.Notification;
using Mova.Application.Interfaces.Persistence;
using Mova.Application.Interfaces.Security;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Shared.Common;
using Mova.Shared.Constants;
using Mova.Shared.Logging;

namespace Mova.Application.BBL.Commands.TransactionPin;

public sealed class VerifyForgotPinOtpCommand
{
    public sealed class Command : IRequest<BaseResult<object>>
    {
        [JsonIgnore]
        public string UserPublicId { get; set; } = string.Empty;

        [JsonIgnore]
        public long UserId { get; set; }

        [JsonPropertyName("password")]
        [Required, MaxLength(256)]
        public string Password { get; set; } = string.Empty;

        [Required, RegularExpression(@"^\d{6}$", ErrorMessage = "OTP must be exactly 6 digits.")]
        public string Otp { get; set; } = string.Empty;
    }

    public sealed class Handler
        : IRequestHandler<Command, BaseResult<object>>
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IIdentityService _identityService;
        private readonly INotificationQueue _notificationQueue;
        private readonly ILogger<Handler> _logger;
        private readonly ITransactionPinService _transactionPinService;

        public Handler(
            IUnitOfWork unitOfWork,
            IIdentityService identityService,
            INotificationQueue notificationQueue,
            ILogger<Handler> logger,
            ITransactionPinService transactionPinService)
        {
            _unitOfWork = unitOfWork;
            _identityService = identityService;
            _notificationQueue = notificationQueue;
            _logger = logger;
            _transactionPinService = transactionPinService;
        }

        public async Task<BaseResult<object>> Handle(
            Command request,
            CancellationToken cancellationToken)
        {
            using var op = OperationLogger.Start(
                _logger,
                "VerifyForgotPinOtp",
                ("UserId", request.UserPublicId));

            if (string.IsNullOrWhiteSpace(request.UserPublicId))
            {
                op.Warning("UserPublicId is required.");
                return new BaseResult<object>(
                    HttpStatusCode.BadRequest,
                    "UserPublicId is required.");
            }

            if (string.IsNullOrWhiteSpace(request.Password))
            {
                op.Warning("Password is required.");
                return new BaseResult<object>(
                    HttpStatusCode.BadRequest,
                    "Please enter your account password.");
            }

            if (string.IsNullOrWhiteSpace(request.Otp) ||
                request.Otp.Length != 6 ||
                !request.Otp.All(char.IsDigit))
            {
                op.Warning("Invalid OTP format.");
                return new BaseResult<object>(
                    HttpStatusCode.BadRequest,
                    "Please enter a valid 6-digit code.");
            }

            var passwordValid = await _identityService.CheckPasswordAsync(
                request.UserId,
                request.Password);

            if (!passwordValid)
            {
                op.Warning("Invalid account password.");
                return new BaseResult<object>(
                    HttpStatusCode.BadRequest,
                    "Incorrect password. Please try again.");
            }

            var otpRecord = await _unitOfWork.Query<OtpVerification>()
                .Where(x => x.UserPublicId == request.UserPublicId
                            && x.Purpose == OtpPurpose.TransactionPin
                            && !x.IsUsed)
                .OrderByDescending(x => x.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);

            if (otpRecord is null)
            {
                op.Warning("No pending OTP found.");
                return new BaseResult<object>(
                    HttpStatusCode.BadRequest,
                    "No verification code found. Please request a new one.");
            }

            if (!string.Equals(otpRecord.OtpCode, request.Otp))
            {
                op.Warning($"Invalid OTP code for user {request.UserPublicId}.");
                return new BaseResult<object>(
                    HttpStatusCode.BadRequest,
                    "Invalid OTP code.");
            }

            if (otpRecord.ExpiresAt < DateTimeOffset.UtcNow)
            {
                op.Warning($"OTP expired for user {request.UserPublicId}.");
                return new BaseResult<object>(
                    HttpStatusCode.BadRequest,
                    "OTP has expired. Please request a new one.");
            }

            var isReset = await _transactionPinService.ResetPinAsync(
                request.UserPublicId,
                cancellationToken);

            if (!isReset)
            {
                op.Warning("ResetPinAsync returned false.");
                return new BaseResult<object>(
                    HttpStatusCode.BadRequest,
                    "Unable to reset PIN. Please try again.");
            }

            otpRecord.IsUsed = true;
            otpRecord.UsedAt = DateTimeOffset.UtcNow;
            _unitOfWork.Update(otpRecord);

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            op.Success("Forgot-PIN verified with password + OTP.");

            _notificationQueue.InAppNotificationAsync(
                request.UserPublicId,
                NotificationType.Security,
                "PIN reset verified",
                "Your identity was verified and your transaction PIN has been cleared. " +
                "Set a new PIN to continue using secure actions. " +
                "If this wasn't you, contact support immediately.",
                "/dashboard/pin-gate",
                null,
                CancellationToken.None);

            return new BaseResult<object>(
                HttpStatusCode.OK,
                "Verified. You can now set a new PIN.",
                new { notification = true });
        }
    }
}