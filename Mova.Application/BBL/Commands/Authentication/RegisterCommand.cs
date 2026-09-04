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

namespace Mova.Application.BBL.Commands.Authentication;

public sealed class RegisterCommand
{
    public class Command : IRequest<BaseResult<RegistrationResponseDto>>
    {
        [MinLength(3), MaxLength(100)]
        public string FirstName { get; init; } = string.Empty;

        [MinLength(3), MaxLength(100)]
        public string LastName { get; init; } = string.Empty;

        [EmailAddress, MaxLength(100)]
        public string Email { get; init; } = string.Empty;

        public string PhoneNumber { get; init; } = string.Empty;

        [MinLength(8)]
        public string Password { get; init; } = string.Empty;
    }

    public class RegistrationResponseDto
    {
        public string UserPublicId { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Data { get; set; } = string.Empty;
    }

    public class Handler : IRequestHandler<Command, BaseResult<RegistrationResponseDto>>
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

        public async Task<BaseResult<RegistrationResponseDto>> Handle(Command request, CancellationToken cancellationToken)
        {
            using var op = OperationLogger.Start(
                _logger, 
                "RegisterUser",
                ("Email", request.Email),
                ("Phone", request.PhoneNumber));

            // 1. Format name
            var firstName = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(
                request.FirstName.Trim().ToLower()
            );

            var lastName = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(
                request.LastName.Trim().ToLower()
            );

            // 2. Normalize email and phone
            var normalizedEmail = request.Email.Trim().ToLowerInvariant();
            var normalizedPhoneNumber = ExtensionHelpers.Normalize(request.PhoneNumber);

            if (normalizedPhoneNumber is null)
            {
                op.Fail("Invalid phone number format.");
                return new BaseResult<RegistrationResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Please provide a valid Nigerian phone number.");
            }

            // 3. Check if email exists
            var emailExists = await _identityService.EmailExistsAsync(
                normalizedEmail,
                cancellationToken: cancellationToken);

            if (emailExists)
            {
                op.Fail($"Email already exists: {normalizedEmail}");
                return new BaseResult<RegistrationResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Email is already in use.");
            }

            // 4. Check if phone exists
            var phoneExists = await _identityService.PhoneExistsAsync(
                normalizedPhoneNumber,
                cancellationToken: cancellationToken);

            if (phoneExists)
            {
                op.Fail($"Phone already exists: {normalizedPhoneNumber}");
                return new BaseResult<RegistrationResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Phone number is already in use.");
            }

            // 5. Begin transaction
            await _unitOfWork.BeginTransactionAsync(cancellationToken);

            try
            {
                // 6. Create user
                var (success, errorMessage, userPublicId, userId) = await _identityService.CreateUserAsync(
                    firstName,
                    lastName,
                    normalizedEmail,
                    normalizedPhoneNumber,
                    request.Password);

                if (!success)
                {
                    await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                    op.Fail($"User creation failed: {errorMessage}");
                    return new BaseResult<RegistrationResponseDto>(
                        HttpStatusCode.BadRequest,
                        errorMessage);
                }

                // 7. Assign role
                var (roleSuccess, roleError) = await _identityService.AddToRoleAsync(userId, Roles.Customer);
                if (!roleSuccess)
                {
                    await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                    op.Fail($"Role assignment failed: {roleError}");
                    return new BaseResult<RegistrationResponseDto>(
                        HttpStatusCode.BadRequest,
                        roleError);
                }

                // 8. Generate OTP
                var otpCode = _otpService.GenerateOtp();

                var otp = new OtpVerification
                {
                    UserPublicId = userPublicId,
                    OtpCode = otpCode,
                    Purpose = OtpPurpose.AccountVerification,
                    ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(2),
                    CreatedAt = DateTimeOffset.UtcNow,
                    IsUsed = false
                };

                await _unitOfWork.AddAsync(otp, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                // 9. Commit transaction
                await _unitOfWork.CommitTransactionAsync(cancellationToken);

                // 10. Send OTP via email and SMS (don't fail on send errors)
                try
                {
                    await Task.WhenAll(
                        _emailService.SendOtpAsync(firstName, request.Email, otpCode, cancellationToken),
                        _smsService.SendOtpAsync(request.PhoneNumber ?? string.Empty, otpCode, cancellationToken));
                }
                catch (Exception sendEx)
                {
                    // Log but don't fail - user is created, OTP is saved
                    op.Fail($"OTP sending failed (but user created): {sendEx.Message}", sendEx);
                }

                op.Success($"User {userPublicId} registered successfully.");

                return new BaseResult<RegistrationResponseDto>(
                    HttpStatusCode.Created,
                    "Account created successfully.",
                    new RegistrationResponseDto
                    {
                        UserPublicId = userPublicId,
                        Email = normalizedEmail,
                        Phone = normalizedPhoneNumber,
                        FullName = firstName,
                        Data = "Account created. Please verify your email/phone with the OTP sent."
                    });
            }
            catch (DbUpdateException dbEx)
            {
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                op.Fail($"Database error during registration: {dbEx.Message}", dbEx);

                return new BaseResult<RegistrationResponseDto>(
                    HttpStatusCode.Conflict,
                    "A database conflict occurred. Please try again.");
            }
            catch (Exception ex)
            {
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                op.Fail("Registration failed", ex);

                return new BaseResult<RegistrationResponseDto>(
                    HttpStatusCode.InternalServerError,
                    "An error occurred during registration. Please try again later.");
            }
        }
    }
}