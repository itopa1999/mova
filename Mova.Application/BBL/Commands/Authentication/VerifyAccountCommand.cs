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
using Mova.Domain.Enums;
using Mova.Shared.Common;
using Mova.Shared.Constants;
using Mova.Shared.Logging;

namespace Mova.Application.BBL.Commands.Authentication;

public sealed class VerifyAccountCommand
{
    public class Command : IRequest<BaseResult<VerifyAccountResponseDto>>
    {
        [EmailAddress]
        public string? Email { get; init; }

        [MinLength(6), MaxLength(6)]
        public string OtpCode { get; init; } = string.Empty;

        public string Platform { get; init; } = Platforms.Mobile;
    }

    public class VerifyAccountResponseDto
    {
        public string UserPublicId { get; set; } = string.Empty;
        public bool IsAccountVerified { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string Platform { get; set; } = Platforms.Mobile;
        public string AccessToken { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;
        public DateTimeOffset AccessTokenExpiresAt { get; set; }
    }

    public class Handler : IRequestHandler<Command, BaseResult<VerifyAccountResponseDto>>
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IIdentityService _identityService;
        private readonly ILogger<Handler> _logger;
        private readonly IEmailService _emailService;
        private readonly IJwtTokenGenerator _jwtTokenGenerator;
        private readonly IRefreshTokenService _refreshTokenService;

        private static readonly HashSet<string> ValidPlatforms =
            new(StringComparer.OrdinalIgnoreCase)
            {
                Platforms.Web,
                Platforms.Mobile,
                Platforms.Swagger
            };

        private static string GenerateDummyAccountNumber()
        {
            return Random.Shared.NextInt64(1_000_000_000L, 10_000_000_000L)
                .ToString();
        }

        public Handler(
            IUnitOfWork unitOfWork,
            IIdentityService identityService,
            ILogger<Handler> logger,
            IEmailService emailService,
            IJwtTokenGenerator jwtTokenGenerator,
            IRefreshTokenService refreshTokenService)
        {
            _unitOfWork = unitOfWork;
            _identityService = identityService;
            _logger = logger;
            _emailService = emailService;
            _jwtTokenGenerator = jwtTokenGenerator;
            _refreshTokenService = refreshTokenService;
        }

        public async Task<BaseResult<VerifyAccountResponseDto>> Handle(Command request, CancellationToken cancellationToken)
        {
            var identifier = !string.IsNullOrWhiteSpace(request.Email) ? request.Email : string.Empty;

            using var op = OperationLogger.Start(
                _logger,
                "VerifyAccount",
                ("Identifier", identifier ?? "unknown"),
                ("Platform", request.Platform));

            if (!ValidPlatforms.Contains(request.Platform))
            {
                op.Fail($"Invalid platform: {request.Platform}");
                return new BaseResult<VerifyAccountResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Invalid platform specified.");
            }

            if (string.IsNullOrWhiteSpace(request.Email))
            {
                op.Fail("No email provided.");
                return new BaseResult<VerifyAccountResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Email must be provided.");
            }

            var user = await _identityService.GetByIdentifierAsync(
                request.Email,
                cancellationToken);

            if (user == null)
            {
                op.Fail($"User not found for email: {request.Email}");
                return new BaseResult<VerifyAccountResponseDto>(
                    HttpStatusCode.BadRequest,
                    "User not found.");
            }

            await _unitOfWork.BeginTransactionAsync(cancellationToken);

            try
            {
                var otpVerification = await _unitOfWork.Query<OtpVerification>()
                    .Where(x => x.UserPublicId == user.PublicId
                                && x.Purpose == OtpPurpose.AccountVerification.ToString()
                                && !x.IsUsed)
                    .OrderByDescending(x => x.CreatedAt)
                    .FirstOrDefaultAsync(cancellationToken);

                if (otpVerification is null)
                {
                    op.Fail($"No valid OTP found for user {user.PublicId}");
                    await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                    return new BaseResult<VerifyAccountResponseDto>(
                        HttpStatusCode.BadRequest,
                        "Invalid OTP. Please request a new one.");
                }

                if (otpVerification.OtpCode != request.OtpCode)
                {
                    op.Fail($"Invalid OTP code provided for user {user.PublicId}");
                    await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                    return new BaseResult<VerifyAccountResponseDto>(
                        HttpStatusCode.BadRequest,
                        "Invalid OTP code.");
                }

                if (otpVerification.ExpiresAt < DateTimeOffset.UtcNow)
                {
                    op.Fail($"OTP expired for user {user.PublicId}");
                    await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                    return new BaseResult<VerifyAccountResponseDto>(
                        HttpStatusCode.BadRequest,
                        "OTP has expired. Please request a new one.");
                }

                if (otpVerification.IsUsed)
                {
                    op.Fail($"OTP already used for user {user.PublicId}");
                    await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                    return new BaseResult<VerifyAccountResponseDto>(
                        HttpStatusCode.BadRequest,
                        "This OTP has already been used.");
                }

                var (markSuccess, markError) = await _identityService.MarkEmailAndPhoneAsVerifiedAsync(user.Id);
                if (!markSuccess)
                {
                    op.Fail($"Failed to mark account as verified for user {user.PublicId}: {markError}");
                    await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                    return new BaseResult<VerifyAccountResponseDto>(
                        HttpStatusCode.BadRequest,
                        markError);
                }

                otpVerification.IsUsed = true;
                otpVerification.UsedAt = DateTimeOffset.UtcNow;
                _unitOfWork.Update(otpVerification);

                var accountNumber = GenerateDummyAccountNumber();
                var virtualAccount = new VirtualAccount
                {
                    UserPublicId = user.PublicId,
                    Provider = PaymentProvider.Monnify,
                    ProviderCustomerId = null,
                    ProviderAccountId = null,
                    AccountNumber = accountNumber,
                    BankName = "Mova Bank",
                    AccountName = user.FullName,
                    Currency = "NGN",
                    Status = VirtualAccountStatus.Active,
                };

                await _unitOfWork.AddAsync(virtualAccount, cancellationToken);

                await _unitOfWork.SaveChangesAsync(cancellationToken);

                await _unitOfWork.CommitTransactionAsync(cancellationToken);

                try
                {
                    await _emailService.SendWelcomeEmailAsync(
                        user.FirstName ?? "Customer",
                        user.Email,
                        cancellationToken);
                }
                catch (Exception emailEx)
                {
                    op.Fail($"Failed to send welcome email: {emailEx.Message}", emailEx);
                }

                var roles = await _identityService.GetRolesAsync(user.Id);

                var accessToken = _jwtTokenGenerator.GenerateToken(
                    user.Id,
                    user.PublicId,
                    user.FirstName,
                    user.OtherNames,
                    user.LastName,
                    user.Email,
                    user.PhoneNumber,
                    user.Balance.ToDecimal(),
                    user.FullName ?? string.Empty,
                    request.Platform,
                    roles);

                var (refreshToken, refreshTokenEntity) = await _refreshTokenService.CreateAsync(
                    user.PublicId,
                    cancellationToken);

                await _unitOfWork.AddAsync(refreshTokenEntity, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                op.Success($"Account verified successfully for user {user.PublicId}. Access and refresh tokens generated.");

                return new BaseResult<VerifyAccountResponseDto>(
                    HttpStatusCode.OK,
                    "Account verified successfully.",
                    new VerifyAccountResponseDto
                    {
                        UserPublicId = user.PublicId,
                        IsAccountVerified = true,
                        FullName = user.FullName ?? string.Empty,
                        Platform = request.Platform,
                        AccessToken = accessToken,
                        RefreshToken = refreshToken,
                        AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddMinutes(15)
                    });
            }
            catch (DbUpdateException dbEx)
            {
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                op.Fail($"Database error during account verification for user {user?.PublicId ?? "unknown"}: {dbEx.Message}", dbEx);

                return new BaseResult<VerifyAccountResponseDto>(
                    HttpStatusCode.Conflict,
                    "An error occurred. Please try again.");
            }
            catch (Exception ex)
            {
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                op.Fail($"Account verification failed for user {user?.PublicId ?? "unknown"}: {ex.Message}", ex);

                return new BaseResult<VerifyAccountResponseDto>(
                    HttpStatusCode.InternalServerError,
                    "An error occurred during account verification. Please try again later.");
            }
        }
    }
}