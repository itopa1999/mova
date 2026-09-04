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
    public sealed class Command : IRequest<BaseResult>
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

    public sealed class Handler : IRequestHandler<Command, BaseResult>
    {
        private readonly IIdentityService _identityService;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ILogger<Handler> _logger;
        private readonly ISchedulePreviewService _schedulePreviewService;

        public Handler(
            IIdentityService identityService,
            IUnitOfWork unitOfWork,
            ILogger<Handler> logger,
            ISchedulePreviewService schedulePreviewService)
        {
            _identityService = identityService;
            _unitOfWork = unitOfWork;
            _logger = logger;
            _schedulePreviewService = schedulePreviewService;
        }

        public async Task<BaseResult> Handle(
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
                return new BaseResult(
                    HttpStatusCode.BadRequest,
                    "Wallet name is required.");
            }

            if (request.Name.Length > 150)
            {
                op.Fail("Wallet name is too long.");
                return new BaseResult(
                    HttpStatusCode.BadRequest,
                    "Wallet name cannot exceed 150 characters.");
            }

            if (request.TargetAmount <= 0)
            {
                op.Fail("Target amount must be greater than zero.");
                return new BaseResult(
                    HttpStatusCode.BadRequest,
                    "Target amount must be greater than zero.");
            }

            if (request.AmountToBeReleased <= 0)
            {
                op.Fail("Release amount must be greater than zero.");
                return new BaseResult(
                    HttpStatusCode.BadRequest,
                    "Release amount must be greater than zero.");
            }

            if (request.AmountToBeReleased > request.TargetAmount)
            {
                op.Fail("Release amount cannot exceed target amount.");
                return new BaseResult(
                    HttpStatusCode.BadRequest,
                    $"Release amount (₦{request.AmountToBeReleased:N0}) cannot exceed target amount (₦{request.TargetAmount:N0}).");
            }

            if (string.IsNullOrWhiteSpace(request.FrequencyConfig))
            {
                op.Fail("Frequency configuration is required.");
                return new BaseResult(
                    HttpStatusCode.BadRequest,
                    "Frequency configuration is required.");
            }

            var existingWallet = await _unitOfWork.Query<Wallet>()
                .AsNoTracking()
                .FirstOrDefaultAsync(x => 
                    x.UserPublicId == request.UserPublicId && 
                    x.Name.Equals(request.Name, StringComparison.CurrentCultureIgnoreCase) &&
                    x.Status == WalletStatus.Active,
                    cancellationToken);

            if (existingWallet != null)
            {
                op.Fail("Wallet name already exists.");
                return new BaseResult(
                    HttpStatusCode.BadRequest,
                    "A wallet with this name already exists.");
            }

            var previewResult = await _schedulePreviewService.PreviewScheduleAsync(
                request.TargetAmount,
                request.AmountToBeReleased,
                request.Frequency,
                request.FrequencyConfig,
                request.StartDate,
                1,
                cancellationToken);

            if (!previewResult.IsSuccess)
            {
                op.Fail($"Schedule preview failed: {string.Join(", ", previewResult.Errors)}");
                
                var errorMessage = previewResult.Errors.Any() 
                    ? string.Join(" | ", previewResult.Errors) 
                    : "Invalid schedule configuration.";

                return new BaseResult(
                    HttpStatusCode.BadRequest,
                    errorMessage);
            }

            var computedEndDate = previewResult.ComputedEndDate;
            var finalEndDate = computedEndDate;

            if (finalEndDate <= request.StartDate)
            {
                op.Fail("End date must be after start date.");
                return new BaseResult(
                    HttpStatusCode.BadRequest,
                    "End date must be after start date.");
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

                    return new BaseResult(
                        HttpStatusCode.BadRequest,
                        "Insufficient account balance.");
                }

                var wallet = new Wallet
                {
                    UserPublicId = request.UserPublicId,
                    Name = request.Name.Trim(),
                    Description = string.IsNullOrWhiteSpace(request.Description)
                        ? null
                        : request.Description.Trim(),
                    TargetAmount = targetMoney,
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
                    FrequencyConfig = request.FrequencyConfig,
                    StartDate = request.StartDate,
                    EndDate = finalEndDate,
                };

                await _unitOfWork.AddAsync(rule, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                var scheduledReleases = new List<ScheduledRelease>();
                var firstReleaseDate = previewResult.FirstReleaseDate;

                var fullPreviewResult = await _schedulePreviewService.PreviewScheduleAsync(
                    request.TargetAmount,
                    request.AmountToBeReleased,
                    request.Frequency,
                    request.FrequencyConfig,
                    request.StartDate,
                    999,
                    cancellationToken);

                if (fullPreviewResult.IsSuccess && fullPreviewResult.SampleReleaseDates.Any())
                {
                    foreach (var releasePreview in fullPreviewResult.SampleReleaseDates)
                    {
                        var scheduledRelease = new ScheduledRelease
                        {
                            WalletId = wallet.Id,
                            WalletRuleId = rule.Id,
                            Amount = Money.FromNaira(releasePreview.Amount),
                            ScheduledFor = releasePreview.Date,
                            Status = ReleaseStatus.Scheduled,
                            ReleasedAt = null
                        };
                        scheduledReleases.Add(scheduledRelease);
                    }

                    foreach (var scheduledRelease in scheduledReleases)
                    {
                        await _unitOfWork.AddAsync(scheduledRelease, cancellationToken);
                    }

                    await _unitOfWork.SaveChangesAsync(cancellationToken);
                }

                await _unitOfWork.CommitTransactionAsync(cancellationToken);

                op.Success($"Wallet created successfully with {scheduledReleases.Count} scheduled releases. WalletId: {wallet.Id}");

                return new BaseResult(
                    HttpStatusCode.Created,
                    $"Wallet created successfully with {scheduledReleases.Count} scheduled releases."
                    );
            }
            catch (Exception ex)
            {
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);

                op.Fail($"Error creating wallet: {ex.Message}");

                _logger.LogError(
                    ex,
                    "Error creating wallet for user {UserPublicId}",
                    request.UserPublicId);

                return new BaseResult(
                    HttpStatusCode.InternalServerError,
                    "An error occurred while creating the wallet.");
            }
        }
    }
}