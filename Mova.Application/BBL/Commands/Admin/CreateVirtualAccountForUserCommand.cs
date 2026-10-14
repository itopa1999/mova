using System.ComponentModel.DataAnnotations;
using System.Net;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mova.Application.Interfaces.Identity;
using Mova.Application.Interfaces.Notification;
using Mova.Application.Interfaces.Persistence;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Shared.Common;
using Mova.Shared.Logging;

namespace Mova.Application.BBL.Commands.Admin;

public sealed class CreateVirtualAccountForUserCommand
{
    public sealed class Command : IRequest<BaseResult<CreateVirtualAccountForUserResponseDto>>
    {
        [Required, MaxLength(100)]
        public string UserPublicId { get; set; } = string.Empty;
    }

    public sealed class CreateVirtualAccountForUserResponseDto
    {
        public string UserPublicId { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public VirtualAccountDto? VirtualAccount { get; set; }
    }

    public sealed class VirtualAccountDto
    {
        public string AccountNumber { get; set; } = string.Empty;
        public string AccountName { get; set; } = string.Empty;
        public string BankName { get; set; } = string.Empty;
        public string Currency { get; set; } = "NGN";
        public string Provider { get; set; } = string.Empty;
    }

    public sealed class Handler
        : IRequestHandler<Command, BaseResult<CreateVirtualAccountForUserResponseDto>>
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IIdentityService _identityService;
        private readonly ILogger<Handler> _logger;

        public Handler(
            IUnitOfWork unitOfWork,
            IIdentityService identityService,
            ILogger<Handler> logger)
        {
            _unitOfWork = unitOfWork;
            _identityService = identityService;
            _logger = logger;
        }

        public async Task<BaseResult<CreateVirtualAccountForUserResponseDto>> Handle(
            Command request,
            CancellationToken cancellationToken)
        {
            using var op = OperationLogger.Start(
                _logger,
                "AdminCreateVirtualAccount",
                ("UserPublicId", request.UserPublicId));

            if (string.IsNullOrWhiteSpace(request.UserPublicId))
            {
                op.Warning("UserPublicId is required.");
                return new BaseResult<CreateVirtualAccountForUserResponseDto>(
                    HttpStatusCode.BadRequest,
                    "UserPublicId is required.");
            }

            var user = await _identityService.GetByIdentifierAsync(
                request.UserPublicId,
                cancellationToken);

            if (user is null)
            {
                op.Warning($"User not found: {request.UserPublicId}");
                return new BaseResult<CreateVirtualAccountForUserResponseDto>(
                    HttpStatusCode.NotFound,
                    "User not found.");
            }

            if (string.IsNullOrWhiteSpace(user.Email))
            {
                op.Warning($"User {request.UserPublicId} has no email on record.");
                return new BaseResult<CreateVirtualAccountForUserResponseDto>(
                    HttpStatusCode.BadRequest,
                    "User does not have an email on record.");
            }

            var existing = await _unitOfWork.Query<VirtualAccount>()
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.UserPublicId == request.UserPublicId,
                    cancellationToken);

            if (existing is not null)
            {
                op.Success(
                    $"Virtual account already exists for {request.UserPublicId}.");

                return new BaseResult<CreateVirtualAccountForUserResponseDto>(
                    HttpStatusCode.OK,
                    "User already has a virtual account.",
                    new CreateVirtualAccountForUserResponseDto
                    {
                        UserPublicId = request.UserPublicId,
                        Message = "Virtual account already exists for this user.",
                        VirtualAccount = ToDto(existing),
                    });
            }

            // Inline random account for now. When the real provider call is
            // wired up, this becomes an enqueue and the account is created
            // off-request by QueueCreateVirtualAccount.
            var virtualAccount = BuildRandomVirtualAccount(
                user.PublicId,
                user.FullName);

            await _unitOfWork.AddAsync(virtualAccount, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            op.Success(
                $"Virtual account generated for {request.UserPublicId}. " +
                $"AccountNumber: {virtualAccount.AccountNumber}");

            return new BaseResult<CreateVirtualAccountForUserResponseDto>(
                HttpStatusCode.Created,
                "Virtual account created successfully.",
                new CreateVirtualAccountForUserResponseDto
                {
                    UserPublicId = request.UserPublicId,
                    Message = "Virtual account created successfully.",
                    VirtualAccount = ToDto(virtualAccount),
                });
        }

        private static VirtualAccountDto ToDto(VirtualAccount account) => new()
        {
            AccountNumber = account.AccountNumber,
            AccountName = account.AccountName,
            BankName = account.BankName,
            Currency = account.Currency,
            Provider = account.Provider.ToString(),
        };

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

// ─────────────────────────────────────────────────────
// Background queue is disabled for now — the account is
// created inline above. Uncomment when the real provider
// call is wired up and you want to move it off the request.
// ─────────────────────────────────────────────────────
//
// try
// {
//     _notificationQueue.QueueCreateVirtualAccount(
//         user.PublicId,
//         user.FirstName ?? string.Empty,
//         user.LastName ?? string.Empty,
//         user.Email ?? string.Empty,
//         user.PhoneNumber ?? string.Empty);
// }
// catch (Exception ex)
// {
//     op.Fail(
//         $"Failed to enqueue virtual account job for {request.UserPublicId}.",
//         ex);
//
//     return new BaseResult<CreateVirtualAccountForUserResponseDto>(
//         HttpStatusCode.InternalServerError,
//         "Failed to queue virtual account creation. Please try again.");
// }