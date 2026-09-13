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
using Mova.Shared.Common;
using Mova.Shared.Constants;
using Mova.Shared.Logging;

namespace Mova.Application.BBL.Commands.TransactionPin;

public sealed class SendForgotPinOtpCommand
{
    public sealed class Command : IRequest<BaseResult<object>>
    {
        [JsonIgnore]
        public string UserPublicId { get; set; } = string.Empty;

        public string Platform { get; set; } = "web";
    }

    public sealed class Handler
        : IRequestHandler<Command, BaseResult<object>>
    {
        private const int OtpExpiryMinutes = 2;

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
                op.Fail("UserPublicId is required.");
                return new BaseResult<object>(
                    HttpStatusCode.BadRequest,
                    "UserPublicId is required.");
            }

            // 1. Confirm the user exists
            var user = await _identityService.GetByIdentifierAsync(
                request.UserPublicId,
                cancellationToken);

            if (user is null)
            {
                op.Fail($"User not found: {request.UserPublicId}");
                return new BaseResult<object>(
                    HttpStatusCode.NotFound,
                    "User not found.");
            }

            var existingOtps = await _unitOfWork.Query<OtpVerification>()
                .Where(x => x.UserPublicId == request.UserPublicId
                            && x.Purpose == OtpPurpose.TransactionPin
                            && !x.IsUsed)
                .ToListAsync(cancellationToken);

            foreach (var existing in existingOtps)
            {
                existing.IsUsed = true;
            }

            var otpCode = _otpService.GenerateOtp();

            var otp = new OtpVerification
            {
                UserPublicId = request.UserPublicId,
                OtpCode = otpCode,
                Purpose = OtpPurpose.TransactionPin,
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(OtpExpiryMinutes),
                CreatedAt = DateTimeOffset.UtcNow,
                IsUsed = false,
            };

            await _unitOfWork.AddAsync(otp, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _notificationQueue.QueueOtpDelivery(
                user.FirstName,
                user.Email,
                user.PhoneNumber,
                otpCode,
                otp.Purpose);

            op.Success(
                $"Forgot-PIN OTP sent. Expires in {OtpExpiryMinutes} minutes.");

            return new BaseResult<object>(
                HttpStatusCode.OK,
                "Verification code sent to your email and phone.");
        }
    }
}