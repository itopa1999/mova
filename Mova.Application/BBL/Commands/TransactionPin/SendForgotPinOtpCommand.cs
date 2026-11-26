using System.Net;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mova.Application.Interfaces.Notification;
using Mova.Application.Interfaces.Persistence;
using Mova.Application.Interfaces.Security;
using Mova.Domain.Entities;
using Mova.Shared.Common;
using Mova.Shared.Constants;
using Mova.Shared.Logging;
using Mova.Shared.Common.Results;
using Mova.Application.Extensions;
namespace Mova.Application.BBL.Commands.TransactionPin;

public sealed class SendForgotPinOtpCommand
{
    public sealed class Command : IRequest<BaseResult<object>>
    {
        [JsonIgnore]
        public string UserPublicId { get; set; } = string.Empty;
    }

    public sealed class Handler
        : IRequestHandler<Command, BaseResult<object>>
    {
        private const int OtpExpiryMinutes = 2;

        private readonly IUnitOfWork _unitOfWork;
        private readonly IOtpService _otpService;
        private readonly INotificationQueue _notificationQueue;
        private readonly ILogger<Handler> _logger;

        public Handler(
            IUnitOfWork unitOfWork,
            IOtpService otpService,
            INotificationQueue notificationQueue,
            ILogger<Handler> logger)
        {
            _unitOfWork = unitOfWork;
            _otpService = otpService;
            _notificationQueue = notificationQueue;
            _logger = logger;
        }

        public async Task<BaseResult<object>> Handle(
            Command request,
            CancellationToken cancellationToken)
        {
            using var op = OperationLogger.Start(
                _logger,
                "SendForgotPinOtp",
                ("UserId", request.UserPublicId));

            if (string.IsNullOrWhiteSpace(request.UserPublicId))
            {
                op.Warning("UserPublicId is required.");
                return new BaseResult<object>(
                    HttpStatusCode.BadRequest,
                    "UserPublicId is required.");
            }

            var user = await _unitOfWork.Query<User>()
                .FirstOrDefaultAsync(x => x.PublicId == request.UserPublicId, cancellationToken);

            if (user is null)
                return new BaseResult<object>(HttpStatusCode.Unauthorized, "User not found.");

            var permissions = user.GetUserPermissions();

            if (!permissions.CanPerformSensitiveOperations)
            {
                op.Warning(
                    $"Sensitive-operation gate blocked forgot-PIN OTP for user {request.UserPublicId}. " +
                    $"Status: {permissions.StatusLabel}, Reason: {permissions.RestrictionReason}");

                var userMessage = string.IsNullOrWhiteSpace(permissions.StatusDescription)
                    ? "Your account is currently restricted from performing this operation. Please contact support."
                    : permissions.StatusDescription;

                return new BaseResult<object>(
                    HttpStatusCode.Locked,
                    userMessage);
            }

            await using var tx = await _unitOfWork.BeginAtomicAsync(cancellationToken);

            var existingOtps = await _unitOfWork.Query<OtpVerification>()
                .Where(x => x.UserPublicId == request.UserPublicId
                            && x.Purpose == OtpPurpose.TransactionPin
                            && !x.IsUsed)
                .ToListAsync(cancellationToken);

            foreach (var existing in existingOtps)
            {
                existing.IsUsed = true;
                _unitOfWork.Update(existing);
            }

            var otpCode = _otpService.GenerateSaltedOtp(SecuritySaltPurpose.OTPVerification.ToString());

            var otp = new OtpVerification
            {
                UserPublicId = request.UserPublicId,
                OtpCode = otpCode.HashedOtp,
                Purpose = OtpPurpose.TransactionPin,
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(OtpExpiryMinutes),
                CreatedAt = DateTimeOffset.UtcNow,
                IsUsed = false,
            };

            await _unitOfWork.AddAsync(otp, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);

            op.Success(
                $"Forgot-PIN OTP sent. Expires in {OtpExpiryMinutes} minutes. " +
                $"Superseded: {existingOtps.Count}.");

            _notificationQueue.QueueOtpDelivery(
                user.FirstName,
                user.Email!,
                user.PhoneNumber,
                otpCode.RawOtp,
                otp.Purpose);

            return new BaseResult<object>(
                HttpStatusCode.OK,
                "Verification code sent to your email and phone.");
        }
    }
}