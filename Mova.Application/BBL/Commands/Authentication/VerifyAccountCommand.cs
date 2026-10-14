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
        [EmailAddress, MaxLength(100)]
        public string? Email { get; init; }

        [Required, RegularExpression(@"^\d{6}$", ErrorMessage = "OTP must be exactly 6 digits.")]
        public string OtpCode { get; init; } = string.Empty;

        [Required, MaxLength(20)]
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
                Platforms.Swagger,
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

        public async Task<BaseResult<VerifyAccountResponseDto>> Handle(
            Command request,
            CancellationToken cancellationToken)
        {
            var identifier = !string.IsNullOrWhiteSpace(request.Email)
                ? request.Email
                : "unknown";

            using var op = OperationLogger.Start(
                _logger,
                "VerifyAccount",
                ("Identifier", identifier),
                ("Platform", request.Platform));

            if (!ValidPlatforms.Contains(request.Platform))
            {
                op.Warning($"Invalid platform: {request.Platform}");
                return new BaseResult<VerifyAccountResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Invalid platform specified.");
            }

            if (string.IsNullOrWhiteSpace(request.Email))
            {
                op.Warning("No email provided.");
                return new BaseResult<VerifyAccountResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Email must be provided.");
            }

            var user = await _identityService.GetByIdentifierAsync(
                request.Email,
                cancellationToken);

            if (user is null)
            {
                op.Warning($"User not found for email: {request.Email}");
                return new BaseResult<VerifyAccountResponseDto>(
                    HttpStatusCode.BadRequest,
                    "User not found.");
            }

            var userPublicId = user.PublicId;
            var userEmail = user.Email ?? string.Empty;
            var userFirstName = string.IsNullOrWhiteSpace(user.FirstName)
                ? "Customer"
                : user.FirstName;

            await using var tx = await _unitOfWork.BeginAtomicAsync(cancellationToken);

            var otpVerification = await _unitOfWork.Query<OtpVerification>()
                .Where(x => x.UserPublicId == user.PublicId
                            && x.Purpose == OtpPurpose.AccountVerification
                            && !x.IsUsed)
                .OrderByDescending(x => x.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);

            if (otpVerification is null)
            {
                op.Warning($"No valid OTP found for user {user.PublicId}.");
                return new BaseResult<VerifyAccountResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Invalid OTP. Please request a new one.");
            }

            if (otpVerification.OtpCode != request.OtpCode)
            {
                op.Warning($"Invalid OTP code for user {user.PublicId}.");
                return new BaseResult<VerifyAccountResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Invalid OTP code.");
            }

            if (otpVerification.ExpiresAt < DateTimeOffset.UtcNow)
            {
                op.Warning($"OTP expired for user {user.PublicId}.");
                return new BaseResult<VerifyAccountResponseDto>(
                    HttpStatusCode.BadRequest,
                    "OTP has expired. Please request a new one.");
            }

            var (markSuccess, markError) = await _identityService
                .MarkEmailAndPhoneAsVerifiedAsync(user.Id);

            if (!markSuccess)
            {
                op.Warning(
                    $"Failed to mark account verified for user {user.PublicId}: {markError}");
                return new BaseResult<VerifyAccountResponseDto>(
                    HttpStatusCode.BadRequest,
                    markError);
            }

            otpVerification.IsUsed = true;
            otpVerification.UsedAt = DateTimeOffset.UtcNow;
            _unitOfWork.Update(otpVerification);

            var existingVirtualAccount = await _unitOfWork.Query<VirtualAccount>()
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.UserPublicId == userPublicId,
                    cancellationToken);

            VirtualAccount virtualAccount;

            if (existingVirtualAccount is null)
            {
                virtualAccount = BuildRandomVirtualAccount(userPublicId, user.FullName);
                await _unitOfWork.AddAsync(virtualAccount, cancellationToken);
            }
            else
            {
                virtualAccount = existingVirtualAccount;
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
                user.FullName,
                request.Platform,
                roles);

            var (refreshTokenValue, refreshTokenEntity) =
                await _refreshTokenService.CreateAsync(user.PublicId, cancellationToken);

            await _unitOfWork.AddAsync(refreshTokenEntity, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);

            op.Success(
                $"Account verified for user {userPublicId}. " +
                $"Virtual account: {virtualAccount.AccountNumber} ({virtualAccount.BankName}).");

            _notificationQueue.QueueWelcomeEmail(userFirstName, userEmail);

            return new BaseResult<VerifyAccountResponseDto>(
                HttpStatusCode.OK,
                "Account verified successfully.",
                new VerifyAccountResponseDto
                {
                    UserPublicId = userPublicId,
                    IsAccountVerified = true,
                    Email = userEmail,
                    Phone = user.PhoneNumber ?? string.Empty,
                    FullName = user.FullName,
                    ProfilePicture = user.ProfilePicture,
                    Balance = user.Balance.ToDecimal(),
                    Platform = request.Platform,
                    AccessToken = accessToken,
                    RefreshToken = refreshTokenValue,
                    AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddMinutes(15),
                    NextStep = NextSteps.CreateTransactionPin,
                    VirtualAccount = new VirtualAccountDto
                    {
                        AccountNumber = virtualAccount.AccountNumber,
                        AccountName = virtualAccount.AccountName,
                        BankName = virtualAccount.BankName,
                        Currency = virtualAccount.Currency,
                        Provider = virtualAccount.Provider.ToString(),
                    },
                });
        }

        private static VirtualAccount BuildRandomVirtualAccount(
            string userPublicId,
            string fullName)
        {
            var rng = Random.Shared;

            var partnerBanks = new[]
            {
                "Wema Bank",
                "Sterling Bank",
                "Providus Bank",
                "Titan Trust Bank",
                "Lotus Bank",
            };

            var bankName = partnerBanks[rng.Next(partnerBanks.Length)];
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