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
        public string Email { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string? ProfilePicture { get; set; } = string.Empty;
        public decimal Balance { get; set; }
        public string Platform { get; set; } = Platforms.Mobile;
        public string? AccessToken { get; set; }
        public string? RefreshToken { get; set; }
        public DateTimeOffset? AccessTokenExpiresAt { get; set; }
        public string NextStep { get; set; } = string.Empty;

        public VirtualAccountDto? VirtualAccount { get; set; }
    }

    public class VirtualAccountDto
    {
        public string AccountNumber { get; set; } = string.Empty;
        public string AccountName { get; set; } = string.Empty;
        public string BankName { get; set; } = string.Empty;
        public string Currency { get; set; } = "NGN";
        public string Provider { get; set; } = string.Empty;
    }

    public class Handler : IRequestHandler<Command, BaseResult<VerifyAccountResponseDto>>
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IIdentityService _identityService;
        private readonly ILogger<Handler> _logger;
        private readonly INotificationQueue _notificationQueue;
        private readonly IJwtTokenGenerator _jwtTokenGenerator;
        private readonly IRefreshTokenService _refreshTokenService;

        private static readonly HashSet<string> ValidPlatforms =
            new(StringComparer.OrdinalIgnoreCase)
            {
                Platforms.Web,
                Platforms.Mobile,
                Platforms.Swagger
            };

        public Handler(
            IUnitOfWork unitOfWork,
            IIdentityService identityService,
            ILogger<Handler> logger,
            INotificationQueue notificationQueue,
            IJwtTokenGenerator jwtTokenGenerator,
            IRefreshTokenService refreshTokenService)
        {
            _unitOfWork = unitOfWork;
            _identityService = identityService;
            _logger = logger;
            _notificationQueue = notificationQueue;
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

            var userPublicId = user.PublicId;
            var userEmail = user.Email ?? string.Empty;
            var userFirstName = user.FirstName ?? "Customer";
            var accessToken = string.Empty;
            var refreshToken = string.Empty;
            VirtualAccount? createdVirtualAccount = null;

            await _unitOfWork.BeginTransactionAsync(cancellationToken);

            try
            {
                var otpVerification = await _unitOfWork.Query<OtpVerification>()
                    .Where(x => x.UserPublicId == user.PublicId
                                && x.Purpose == OtpPurpose.AccountVerification
                                && !x.IsUsed)
                    .OrderByDescending(x => x.Id)
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

                // ─── Create virtual account (random, synchronous) ───
                var existingVirtualAccount = await _unitOfWork.Query<VirtualAccount>()
                    .AsNoTracking()
                    .FirstOrDefaultAsync(
                        x => x.UserPublicId == userPublicId,
                        cancellationToken);

                if (existingVirtualAccount is null)
                {
                    createdVirtualAccount = BuildRandomVirtualAccount(
                        userPublicId,
                        user.FullName ?? $"{userFirstName} Customer");

                    await _unitOfWork.AddAsync(createdVirtualAccount, cancellationToken);

                    op.Success(
                        $"Virtual account generated: {createdVirtualAccount.AccountNumber} " +
                        $"({createdVirtualAccount.BankName})");
                }
                else
                {
                    createdVirtualAccount = existingVirtualAccount;
                    op.Success(
                        $"Reusing existing virtual account: {existingVirtualAccount.AccountNumber}");
                }

                var roles = await _identityService.GetRolesAsync(user.Id);

                accessToken = _jwtTokenGenerator.GenerateToken(
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

                var (newRefreshToken, refreshTokenEntity) = await _refreshTokenService.CreateAsync(
                    user.PublicId,
                    cancellationToken);

                await _unitOfWork.AddAsync(refreshTokenEntity, cancellationToken);

                await _unitOfWork.SaveChangesAsync(cancellationToken);

                await _unitOfWork.CommitTransactionAsync(cancellationToken);

                refreshToken = newRefreshToken;

                op.Success($"Account verified successfully for user {userPublicId}. Tokens issued, virtual account ready.");
            }
            catch (DbUpdateException dbEx)
            {
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                op.Fail($"Database error during account verification for user {userPublicId}: {dbEx.Message}", dbEx);

                return new BaseResult<VerifyAccountResponseDto>(
                    HttpStatusCode.Conflict,
                    "An error occurred. Please try again.");
            }
            catch (Exception ex)
            {
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                op.Fail($"Account verification failed for user {userPublicId}: {ex.Message}", ex);

                return new BaseResult<VerifyAccountResponseDto>(
                    HttpStatusCode.InternalServerError,
                    $"DEBUG: [{ex.GetType().Name}] {ex.Message}");
            }

            // ─── Post-commit notifications ───
            try
            {
                _notificationQueue.QueueWelcomeEmail(
                    userFirstName,
                    userEmail);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to queue welcome email for {UserPublicId}.",
                    userPublicId);
            }

            var virtualAccountDto = createdVirtualAccount is null
                ? null
                : new VirtualAccountDto
                {
                    AccountNumber = createdVirtualAccount.AccountNumber,
                    AccountName = createdVirtualAccount.AccountName,
                    BankName = createdVirtualAccount.BankName,
                    Currency = createdVirtualAccount.Currency,
                    Provider = createdVirtualAccount.Provider.ToString(),
                };

            return new BaseResult<VerifyAccountResponseDto>(
                HttpStatusCode.OK,
                "Account verified successfully.",
                new VerifyAccountResponseDto
                {
                    UserPublicId = userPublicId,
                    IsAccountVerified = true,
                    Email = userEmail,
                    Phone = user.PhoneNumber ?? string.Empty,
                    FullName = user.FullName ?? string.Empty,
                    ProfilePicture = user.ProfilePicture,
                    Balance = user.Balance.ToDecimal(),
                    Platform = request.Platform,
                    AccessToken = accessToken,
                    RefreshToken = refreshToken,
                    AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddMinutes(15),
                    NextStep = NextSteps.CreateTransactionPin,
                    VirtualAccount = virtualAccountDto,
                });
        }

        // ─── Random virtual account generator ─────────────────
        private static VirtualAccount BuildRandomVirtualAccount(
            string userPublicId,
            string fullName)
        {
            var rng = Random.Shared;

            // A small set of "partner banks" so the account looks plausible
            var partnerBanks = new[]
            {
                "Wema Bank",
                "Sterling Bank",
                "Providus Bank",
                "Titan Trust Bank",
                "Lotus Bank",
            };

            var bankName = partnerBanks[rng.Next(partnerBanks.Length)];

            // Paystack-style 10-digit NUBAN starting with "9"
            var accountNumber = "9" + rng.Next(100_000_000, 999_999_999).ToString();

            return new VirtualAccount
            {
                UserPublicId = userPublicId,
                Provider = PaymentProvider.Paystack,
                ProviderCustomerId = $"CUS_{Guid.NewGuid():N}".Substring(0, 20),
                ProviderAccountId = $"ACC_{Guid.NewGuid():N}".Substring(0, 20),
                AccountNumber = accountNumber,
                BankName = bankName,
                AccountName = fullName,
                Currency = "NGN",
                Status = VirtualAccountStatus.Active,
            };
        }
    }
}