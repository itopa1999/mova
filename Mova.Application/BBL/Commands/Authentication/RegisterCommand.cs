using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mova.Application.Helpers;
using Mova.Application.Interfaces.Identity;
using Mova.Application.Interfaces.Notification;
using Mova.Application.Interfaces.Persistence;
using Mova.Application.Interfaces.Security;
using Mova.Domain.Entities;
using Mova.Shared.Common;
using Mova.Shared.Constants;
using Mova.Shared.Logging;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Net;
using System.Text.Json.Serialization;

namespace Mova.Application.BBL.Commands.Authentication;

public sealed class RegisterCommand
{
    public class Command : IRequest<BaseResult<RegistrationResponseDto>>
    {
        [Required, MinLength(3), MaxLength(100)]
        [JsonPropertyName("firstname")]
        public string FirstName { get; init; } = string.Empty;

        [Required, MinLength(3), MaxLength(100)]
        [JsonPropertyName("lastname")]
        public string LastName { get; init; } = string.Empty;

        [Required, EmailAddress, MaxLength(100)]
        [JsonPropertyName("email")]
        public string Email { get; init; } = string.Empty;

        [Required, MaxLength(20)]
        [JsonPropertyName("phonenumber")]
        public string PhoneNumber { get; init; } = string.Empty;

        [Required]
        [RegularExpression(@"^\d{11}$", ErrorMessage = "BVN must be 11 digits.")]
        [JsonPropertyName("bvn")]
        public string Bvn { get; init; } = string.Empty;

        [Required, MinLength(8), MaxLength(256)]
        [JsonPropertyName("password")]
        public string Password { get; init; } = string.Empty;
    }

    public class RegistrationResponseDto
    {
        public string UserPublicId { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Data { get; set; } = string.Empty;
        public string NextStep { get; set; } = string.Empty;
    }

    public class Handler : IRequestHandler<Command, BaseResult<RegistrationResponseDto>>
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

        public async Task<BaseResult<RegistrationResponseDto>> Handle(
            Command request,
            CancellationToken cancellationToken)
        {
            using var op = OperationLogger.Start(
                _logger,
                "RegisterUser",
                ("Email", request.Email),
                ("Phone", request.PhoneNumber));

            var firstName = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(
                request.FirstName.Trim().ToLower());

            var lastName = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(
                request.LastName.Trim().ToLower());

            var normalizedEmail = request.Email.Trim().ToLowerInvariant();
            var normalizedPhoneNumber = ExtensionHelpers.Normalize(request.PhoneNumber);

            if (normalizedPhoneNumber is null)
            {
                op.Warning("Invalid phone number format.");
                return new BaseResult<RegistrationResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Please provide a valid Nigerian phone number.");
            }

            var emailExists = await _identityService.EmailExistsAsync(
                normalizedEmail,
                cancellationToken: cancellationToken);

            if (emailExists)
            {
                op.Warning($"Email already exists: {normalizedEmail}");
                return new BaseResult<RegistrationResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Email is already in use.");
            }

            var bvnExists = await _identityService.BvnExistsAsync(
                request.Bvn,
                cancellationToken: cancellationToken);

            if (bvnExists)
            {
                op.Warning("BVN already registered.");
                return new BaseResult<RegistrationResponseDto>(
                    HttpStatusCode.BadRequest,
                    "This BVN is already registered to another account.");
            }

            var phoneExists = await _identityService.PhoneExistsAsync(
                normalizedPhoneNumber,
                cancellationToken: cancellationToken);

            if (phoneExists)
            {
                op.Warning($"Phone already exists: {normalizedPhoneNumber}");
                return new BaseResult<RegistrationResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Phone number is already in use.");
            }

            await using var tx = await _unitOfWork.BeginAtomicAsync(cancellationToken);

            var (success, errorMessage, createdPublicId, userId) =
                await _identityService.CreateUserAsync(
                    firstName,
                    lastName,
                    normalizedEmail,
                    normalizedPhoneNumber,
                    request.Bvn,
                    request.Password);

            if (!success)
            {
                op.Warning($"User creation failed: {errorMessage}");
                return new BaseResult<RegistrationResponseDto>(
                    HttpStatusCode.BadRequest,
                    errorMessage);
            }

            var (roleSuccess, roleError) = await _identityService.AddToRoleAsync(
                userId,
                Roles.Customer);

            if (!roleSuccess)
            {
                op.Warning($"Role assignment failed: {roleError}");
                return new BaseResult<RegistrationResponseDto>(
                    HttpStatusCode.BadRequest,
                    roleError);
            }

            var otpCode = _otpService.GenerateOtp();

            var otp = new OtpVerification
            {
                UserPublicId = createdPublicId,
                OtpCode = otpCode,
                Purpose = OtpPurpose.AccountVerification,
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(2),
                CreatedAt = DateTimeOffset.UtcNow,
                IsUsed = false,
            };

            await _unitOfWork.AddAsync(otp, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);

            op.Success($"User {createdPublicId} registered successfully.");

            _notificationQueue.QueueOtpDelivery(
                firstName,
                normalizedEmail,
                normalizedPhoneNumber,
                otpCode,
                OtpPurpose.AccountVerification.ToString());

            return new BaseResult<RegistrationResponseDto>(
                HttpStatusCode.Created,
                "Account created successfully.",
                new RegistrationResponseDto
                {
                    UserPublicId = createdPublicId,
                    Email = normalizedEmail,
                    Phone = normalizedPhoneNumber,
                    FullName = $"{firstName} {lastName}",
                    Data = "Account created. Please verify your email/phone with the OTP sent.",
                    NextStep = NextSteps.EmailVerification,
                });
        }
    }
}