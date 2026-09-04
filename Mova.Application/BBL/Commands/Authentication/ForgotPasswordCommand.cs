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
        [EmailAddress, MaxLength(100)]
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

        public async Task<BaseResult<ForgotPasswordResponseDto>> Handle(Command request, CancellationToken cancellationToken)
        {
            var identifier = !string.IsNullOrWhiteSpace(request.Email) ? request.Email : string.Empty;
            
            using var op = OperationLogger.Start(
                _logger, 
                "ForgotPassword",
                ("Identifier", identifier ?? "unknown"));

            if (string.IsNullOrWhiteSpace(request.Email))
            {
                op.Fail("No email provided.");
                return new BaseResult<ForgotPasswordResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Email must be provided.");
            }

            var user = await _identityService.GetByIdentifierAsync(
                request.Email,
                cancellationToken);

            if (user == null)
            {
                op.Fail("User not found (identifier provided but not in system).");
                return new BaseResult<ForgotPasswordResponseDto>(
                    HttpStatusCode.OK,
                    "If an account exists with this email or phone number, a reset OTP will be sent.",
                    new ForgotPasswordResponseDto
                    {
                        UserPublicId = string.Empty,
                        Data = "If an account exists, OTP has been sent."
                    });
            }

            if (await _identityService.IsAccountVerifiedAsync(user.Id) is false)
            {
                op.Fail($"User account is not verified. UserId: {user.PublicId}");
                return new BaseResult<ForgotPasswordResponseDto>(
                    HttpStatusCode.BadRequest,
                    "User account is not verified. Please verify your account first.");
            }

            await _unitOfWork.BeginTransactionAsync(cancellationToken);

            try
            {
                var otpCode = _otpService.GenerateOtp();
                var otp = new OtpVerification
                {
                    UserPublicId = user.PublicId,
                    OtpCode = otpCode,
                    Purpose = OtpPurpose.PasswordReset,
                    ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(2),
                    CreatedAt = DateTimeOffset.UtcNow,
                    IsUsed = false
                };

                await _unitOfWork.AddAsync(otp, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                await _unitOfWork.CommitTransactionAsync(cancellationToken);

                try
                {
                    await _emailService.SendForgotPasswordOtpAsync(user.Email, otpCode, cancellationToken);
                }
                catch (Exception emailEx)
                {
                    op.Fail($"Failed to send email OTP: {emailEx.Message}", emailEx);
                }

                try
                {
                    await _smsService.SendOtpAsync(user.PhoneNumber, otpCode, cancellationToken);
                }
                catch (Exception smsEx)
                {
                    op.Fail($"Failed to send SMS OTP: {smsEx.Message}", smsEx);
                }

                op.Success($"OTP sent to user {user.PublicId} for password reset.");

                return new BaseResult<ForgotPasswordResponseDto>(
                    HttpStatusCode.OK,
                    "If an account exists with this email or phone number, a reset OTP will be sent.",
                    new ForgotPasswordResponseDto
                    {
                        UserPublicId = user.PublicId,
                        Data = "OTP sent successfully."
                    });
            }
            catch (DbUpdateException dbEx)
            {
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                op.Fail($"Database error while saving OTP for user {user?.PublicId ?? "unknown"}: {dbEx.Message}", dbEx);

                return new BaseResult<ForgotPasswordResponseDto>(
                    HttpStatusCode.Conflict,
                    "An error occurred. Please try again.");
            }
            catch (Exception ex)
            {
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                op.Fail($"Forgot password failed for user {user?.PublicId ?? "unknown"}: {ex.Message}", ex);

                return new BaseResult<ForgotPasswordResponseDto>(
                    HttpStatusCode.InternalServerError,
                    "An error occurred while processing your request. Please try again later.");
            }
        }
    }
}