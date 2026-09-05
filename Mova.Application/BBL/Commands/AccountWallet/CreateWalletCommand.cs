using System.Net;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mova.Application.Interfaces.Identity;
using Mova.Application.Interfaces.Persistence;
using Mova.Application.Interfaces.Service;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Domain.ValueObjects;
using Mova.Shared.Common;
using Mova.Shared.Logging;

namespace Mova.Application.BBL.Commands.AccountWallet;

public sealed class CreateWalletCommand
{
    public sealed class Command : IRequest<BaseResult<CreateWalletResponseDto>>
    {
        [JsonIgnore]
        public string UserPublicId { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string? Description { get; set; }

        public decimal TargetAmount { get; set; }

        public ReleaseFrequency Frequency { get; set; }

        public string FrequencyConfig { get; set; } = string.Empty;

        public decimal AmountToBeReleased { get; set; }

        public DateTimeOffset StartDate { get; set; }
    }

    public sealed class CreateWalletResponseDto
    {
        public long WalletId { get; init; }

        public DateTimeOffset FirstReleaseDate { get; init; }
    }

    public sealed class Handler : IRequestHandler<Command, BaseResult<CreateWalletResponseDto>>
    {
        private readonly IIdentityService _identityService;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ILogger<Handler> _logger;
        private readonly ISchedulePreviewService _schedulePreviewService;
        private readonly IWalletRuleService _walletRuleService;

        public Handler(
            IIdentityService identityService,
            IUnitOfWork unitOfWork,
            ILogger<Handler> logger,
            ISchedulePreviewService schedulePreviewService,
            IWalletRuleService walletRuleService)
        {
            _identityService = identityService;
            _unitOfWork = unitOfWork;
            _logger = logger;
            _schedulePreviewService = schedulePreviewService;
            _walletRuleService = walletRuleService;
        }

        public async Task<BaseResult<CreateWalletResponseDto>> Handle(
            Command request,
            CancellationToken cancellationToken)
        {
            using var op = OperationLogger.Start(
                _logger,
                "CreateWallet",
                ("UserId", request.UserPublicId));

            if (string.IsNullOrWhiteSpace(request.Name))
            {
                op.Fail("Wallet name is required.");
                return new BaseResult<CreateWalletResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Wallet name is required.");
            }

            if (request.Name.Length > 150)
            {
                op.Fail("Wallet name is too long.");
                return new BaseResult<CreateWalletResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Wallet name cannot exceed 150 characters.");
            }

            var walletName = request.Name.Trim();

            if (request.TargetAmount <= 0)
            {
                op.Fail("Target amount must be greater than zero.");
                return new BaseResult<CreateWalletResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Target amount must be greater than zero.");
            }

            if (request.AmountToBeReleased <= 0)
            {
                op.Fail("Release amount must be greater than zero.");
                return new BaseResult<CreateWalletResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Release amount must be greater than zero.");
            }

            if (request.AmountToBeReleased > request.TargetAmount)
            {
                op.Fail("Release amount cannot exceed target amount.");
                return new BaseResult<CreateWalletResponseDto>(
                    HttpStatusCode.BadRequest,
                    $"Release amount (₦{request.AmountToBeReleased:N0}) cannot exceed target amount (₦{request.TargetAmount:N0}).");
            }

            if (string.IsNullOrWhiteSpace(request.FrequencyConfig))
            {
                op.Fail("Frequency configuration is required.");
                return new BaseResult<CreateWalletResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Frequency configuration is required.");
            }

            var normalizedFrequencyConfig =
                FrequencyConfigHelper.NormalizeConfigJson(request.FrequencyConfig);

            var existingWallet = await _unitOfWork.Query<Wallet>()
                .AsNoTracking()
                .FirstOrDefaultAsync(x => 
                    x.UserPublicId == request.UserPublicId && 
                    x.Name.ToLower() == walletName.ToLower() &&
                    x.Status == WalletStatus.Active,
                    cancellationToken);

            if (existingWallet != null)
            {
                op.Fail("Wallet name already exists.");
                return new BaseResult<CreateWalletResponseDto>(
                    HttpStatusCode.BadRequest,
                    "A wallet with this name already exists.");
            }

            var previewResult = await _schedulePreviewService.PreviewScheduleAsync(
                request.TargetAmount,
                request.AmountToBeReleased,
                request.Frequency,
                normalizedFrequencyConfig,
                request.StartDate,
                1,
                cancellationToken);

            if (!previewResult.IsSuccess)
            {
                op.Fail($"Schedule preview failed: {string.Join(", ", previewResult.Errors)}");
                
                var errorMessage = previewResult.Errors.Any() 
                    ? string.Join(" | ", previewResult.Errors) 
                    : "Invalid schedule configuration.";

                return new BaseResult<CreateWalletResponseDto>(
                    HttpStatusCode.BadRequest,
                    errorMessage);
            }

            // Calculate the final date with the same service used by the release job. The
            // preview service's sampled dates are not suitable for this value because only
            // one sample is requested above.
            var ruleForEndDate = new WalletRule
            {
                Amount = Money.FromNaira(request.AmountToBeReleased),
                Frequency = request.Frequency,
                FrequencyConfig = normalizedFrequencyConfig,
                StartDate = request.StartDate
            };
            var cursor = request.StartDate.AddTicks(-1);
            DateTimeOffset? finalEndDate = null;

            for (var releaseNumber = 0; releaseNumber < previewResult.TotalReleases; releaseNumber++)
            {
                var nextRelease = await _walletRuleService.GetNextReleaseAsync(
                    ruleForEndDate,
                    cursor,
                    cancellationToken);

                if (nextRelease is null)
                    break;

                finalEndDate = nextRelease.ScheduledFor;
                cursor = nextRelease.ScheduledFor;
            }

            if (finalEndDate is null)
            {
                op.Fail("Unable to calculate the final release date.");
                return new BaseResult<CreateWalletResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Unable to calculate the final release date.");
            }

            var targetMoney = Money.FromNaira(request.TargetAmount);
            var releaseMoney = Money.FromNaira(request.AmountToBeReleased);

            await _unitOfWork.BeginTransactionAsync(cancellationToken);

            try
            {
                var balanceDebited = await _identityService.DebitBalanceAsync(
                    request.UserPublicId,
                    request.TargetAmount,
                    cancellationToken);

                if (!balanceDebited)
                {
                    await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                    op.Fail("Insufficient account balance or user account not found.");

                    return new BaseResult<CreateWalletResponseDto>(
                        HttpStatusCode.BadRequest,
                        "Insufficient account balance.");
                }

                var wallet = new Wallet
                {
                    UserPublicId = request.UserPublicId,
                    Name = walletName,
                    Description = string.IsNullOrWhiteSpace(request.Description)
                        ? null
                        : request.Description.Trim(),
                    TargetAmount = targetMoney,
                    FundedAmount = targetMoney,
                    AvailableAmount = Money.FromNaira(0),
                    LockedAmount = targetMoney,
                    UnusedAmount = Money.FromNaira(0),
                    Status = WalletStatus.Active,
                };

                await _unitOfWork.AddAsync(wallet, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                var rule = new WalletRule
                {
                    WalletId = wallet.Id,
                    Amount = releaseMoney,
                    Frequency = request.Frequency,
                    FrequencyConfig = normalizedFrequencyConfig,
                    StartDate = request.StartDate,
                    EndDate = finalEndDate.Value,
                };

                await _unitOfWork.AddAsync(rule, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                var firstRelease = await _walletRuleService.GetNextReleaseAsync(
                    rule,
                    request.StartDate.AddTicks(-1),
                    cancellationToken);

                if (firstRelease is null)
                {
                    throw new InvalidOperationException(
                        "Unable to generate the first wallet release schedule.");
                }

                var firstReleaseAmount = Math.Min(
                    firstRelease.Amount.ToDecimal(),
                    request.TargetAmount);

                var scheduledRelease = new ScheduledRelease
                {
                    WalletId = wallet.Id,
                    WalletRuleId = rule.Id,
                    Amount = Money.FromNaira(firstReleaseAmount),
                    ScheduledFor = firstRelease.ScheduledFor,
                    Status = ReleaseStatus.Scheduled,
                    ReleasedAt = null
                };

                await _unitOfWork.AddAsync(scheduledRelease, cancellationToken);

                await _unitOfWork.SaveChangesAsync(cancellationToken);

                await _unitOfWork.CommitTransactionAsync(cancellationToken);

                op.Success($"Wallet created successfully with first release scheduled for {firstRelease.ScheduledFor:u}. WalletId: {wallet.Id}");

                return new BaseResult<CreateWalletResponseDto>(
                    HttpStatusCode.Created,
                    "Wallet created successfully.",
                    new CreateWalletResponseDto
                    {
                        WalletId = wallet.Id,
                        FirstReleaseDate = firstRelease.ScheduledFor
                    }
                    );
            }
            catch (Exception ex)
            {
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);

                op.Fail($"Error creating wallet: {ex.Message}");

                return new BaseResult<CreateWalletResponseDto>(
                    HttpStatusCode.InternalServerError,
                    "An error occurred while creating the wallet.");
            }
        }
    }
}
