using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mova.Application.Interfaces.Identity;
using Mova.Application.Interfaces.Payment;
using Mova.Application.Interfaces.Service;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Domain.ValueObjects;
using Mova.Infrastructure.Persistence;

namespace Mova.Infrastructure.Jobs;

public sealed class BackgroundTaskJob
{
    private const int MinMembersToStart = 3;

    private readonly ApplicationDbContext _context;
    private readonly IPaystackService _paystackService;
    private readonly IIdentityService _identityService;
    private readonly ISavingsCircleService _savingsCircleService;
    private readonly ILogger<BackgroundTaskJob> _logger;

    public BackgroundTaskJob(
        ApplicationDbContext context,
        IPaystackService paystackService,
        IIdentityService identityService,
        ISavingsCircleService savingsCircleService,
        ILogger<BackgroundTaskJob> logger)
    {
        _context = context;
        _paystackService = paystackService;
        _identityService = identityService;
        _savingsCircleService = savingsCircleService;
        _logger = logger;
    }

    [AutomaticRetry(Attempts = 3)]
    public async Task CreateVirtualAccountAsync(
        string userPublicId,
        string firstName,
        string lastName,
        string email,
        string phoneNumber,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Starting virtual account creation for user {UserPublicId}.",
            userPublicId);

        var userExists = await _context.Users
            .AnyAsync(x => x.PublicId == userPublicId, cancellationToken);

        if (!userExists)
        {
            _logger.LogWarning(
                "User {UserPublicId} was not found while creating virtual account.",
                userPublicId);
            return;
        }

        var existingAccount = await _context.VirtualAccounts
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.UserPublicId == userPublicId &&
                     x.Provider == PaymentProvider.Paystack,
                cancellationToken);

        if (existingAccount is not null)
        {
            _logger.LogInformation(
                "User {UserPublicId} already has a Paystack virtual account " +
                "{AccountNumber} with status {Status}. Skipping.",
                userPublicId,
                existingAccount.AccountNumber,
                existingAccount.Status);
            return;
        }

        var customer = await _paystackService.CreateCustomerAsync(
            firstName, lastName, email, phoneNumber, cancellationToken);

        if (!customer.CallSucceeded ||
            string.IsNullOrWhiteSpace(customer.CustomerCode))
        {
            _logger.LogError(
                "Paystack customer creation failed for {UserPublicId}: {Message}",
                userPublicId,
                customer.Message);

            throw new InvalidOperationException(
                $"Unable to create Paystack customer for user {userPublicId}: " +
                $"{customer.Message}");
        }

        _logger.LogInformation(
            "Paystack customer created/found for {UserPublicId}. Customer: {CustomerCode}.",
            userPublicId,
            customer.CustomerCode);

        var dedicatedAccount =
            await _paystackService.CreateDedicatedVirtualAccountAsync(
                customer.CustomerCode,
                firstName,
                lastName,
                phoneNumber,
                cancellationToken);

        if (!dedicatedAccount.CallSucceeded ||
            string.IsNullOrWhiteSpace(dedicatedAccount.AccountNumber))
        {
            _logger.LogError(
                "DVA creation failed for {UserPublicId}: {Message}",
                userPublicId,
                dedicatedAccount.Message);

            throw new InvalidOperationException(
                $"Paystack did not return a dedicated virtual account for user " +
                $"{userPublicId}: {dedicatedAccount.Message}");
        }

        if (!dedicatedAccount.IsActive || !dedicatedAccount.IsAssigned)
        {
            _logger.LogWarning(
                "DVA for {UserPublicId} returned but is not active/assigned " +
                "(Active: {Active}, Assigned: {Assigned}). Not persisting.",
                userPublicId,
                dedicatedAccount.IsActive,
                dedicatedAccount.IsAssigned);

            throw new InvalidOperationException(
                $"Paystack DVA for user {userPublicId} is not active yet " +
                $"(Active: {dedicatedAccount.IsActive}, " +
                $"Assigned: {dedicatedAccount.IsAssigned}).");
        }

        var virtualAccount = new VirtualAccount
        {
            UserPublicId = userPublicId,
            Provider = PaymentProvider.Paystack,
            ProviderCustomerId = customer.CustomerCode,
            ProviderAccountId = dedicatedAccount.AccountId,
            AccountNumber = dedicatedAccount.AccountNumber,
            BankName = dedicatedAccount.BankName,
            AccountName = dedicatedAccount.AccountName,
            Currency = "NGN",
            Status = VirtualAccountStatus.Active
        };

        try
        {
            _context.VirtualAccounts.Add(virtualAccount);
            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Paystack virtual account created for {UserPublicId}. " +
                "Account: {AccountNumber}, Bank: {BankName}.",
                userPublicId,
                dedicatedAccount.AccountNumber,
                dedicatedAccount.BankName);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            _logger.LogInformation(
                "Virtual account for {UserPublicId} was created concurrently. Skipping.",
                userPublicId);
        }
    }

    [AutomaticRetry(Attempts = 3)]
    public async Task CreateCircleWalletAsync(
        string UserPublicId,
        long CategoryId,
        long SavingsCircleId,
        string Name,
        string Description,
        decimal TargetAmount,
        decimal TotalUpfrontCharge,
        decimal TotalMovaFees,
        decimal DepositAmount,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Starting circle wallet creation for user {UserPublicId} in circle {SavingsCircleId}.",
            UserPublicId,
            SavingsCircleId);

        var existingWallet = await _context.Wallets
            .AsNoTracking()
            .FirstOrDefaultAsync(
                w => w.UserPublicId == UserPublicId &&
                    w.SavingsCircleId == SavingsCircleId &&
                    w.Type == WalletType.Circle,
                cancellationToken);

        if (existingWallet is not null)
        {
            _logger.LogInformation(
                "Circle wallet already exists for user {UserPublicId} in circle {SavingsCircleId}. " +
                "WalletId: {WalletId}. Skipping creation.",
                UserPublicId,
                SavingsCircleId,
                existingWallet.Id);

            await TryStartCircleIfReadySafeAsync(SavingsCircleId, cancellationToken);
            return;
        }

        var circleExists = await _context.SavingsCircles
            .AsNoTracking()
            .AnyAsync(c => c.Id == SavingsCircleId, cancellationToken);

        if (!circleExists)
        {
            _logger.LogWarning(
                "Savings circle {SavingsCircleId} not found. " +
                "Cannot create wallet for user {UserPublicId}.",
                SavingsCircleId,
                UserPublicId);

            throw new InvalidOperationException(
                $"Savings circle {SavingsCircleId} not found. Wallet cannot be created.");
        }

        var balanceDebited = await _identityService.DebitBalanceAsync(
            UserPublicId,
            TotalUpfrontCharge,
            cancellationToken);

        if (!balanceDebited)
        {
            _logger.LogWarning(
                "Insufficient balance for user {UserPublicId} to join circle {SavingsCircleId}. " +
                "Required: {TotalUpfrontCharge}.",
                UserPublicId,
                SavingsCircleId,
                TotalUpfrontCharge);

            throw new InvalidOperationException(
                $"Insufficient balance for user {UserPublicId}. Required: {TotalUpfrontCharge}.");
        }

        var nonFeeAmount = TotalUpfrontCharge - TotalMovaFees;

        try
        {
            await RecordDebitAsync(
                UserPublicId,
                nonFeeAmount,
                SavingsCircleId,
                Name,
                isFee: false,
                cancellationToken);

            if (TotalMovaFees > 0)
            {
                await RecordDebitAsync(
                    UserPublicId,
                    TotalMovaFees,
                    SavingsCircleId,
                    Name,
                    isFee: true,
                    cancellationToken);
            }

            var wallet = new Wallet
            {
                UserPublicId = UserPublicId,
                CategoryId = CategoryId,
                SavingsCircleId = SavingsCircleId,
                Name = Name,
                Description = string.IsNullOrWhiteSpace(Description)
                    ? null
                    : Description.Trim(),
                Type = WalletType.Circle,
                Status = WalletStatus.Active,
                TargetAmount = Money.FromNaira(TargetAmount),
                AvailableAmount = Money.FromNaira(0),
                LockedAmount = Money.FromNaira(DepositAmount),
                ResetAmount = Money.FromNaira(0),
                FundedAmount = Money.FromNaira(0),
                TotalReleasedAmount = Money.FromNaira(0),
                TotalWithdrawnAmount = Money.FromNaira(0),
                PayoutDestination = PayoutDestination.Wallet,
            };

            _context.Wallets.Add(wallet);
            await _context.SaveChangesAsync(cancellationToken);

            var member = await _context.CircleMembers
                .FirstOrDefaultAsync(
                    m => m.SavingsCircleId == SavingsCircleId &&
                        m.UserPublicId == UserPublicId,
                    cancellationToken);

            if (member is not null)
            {
                member.WalletId = wallet.Id;
                member.DepositHeld = Money.FromNaira(TargetAmount + DepositAmount);
                await _context.SaveChangesAsync(cancellationToken);

                _logger.LogInformation(
                    "Member {MemberId} DepositHeld set to {DepositHeld} " +
                    "(TargetAmount {TargetAmount} + DepositAmount {DepositAmount}).",
                    member.Id,
                    TargetAmount + DepositAmount,
                    TargetAmount,
                    DepositAmount);
            }

            _logger.LogInformation(
                "Circle wallet created. WalletId: {WalletId}, " +
                "User: {UserPublicId}, Circle: {SavingsCircleId}, " +
                "Debited: {TotalUpfrontCharge}, Fees: {TotalMovaFees}, Deposit: {DepositAmount}.",
                wallet.Id,
                UserPublicId,
                SavingsCircleId,
                TotalUpfrontCharge,
                TotalMovaFees,
                DepositAmount);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            _logger.LogInformation(
                "Circle wallet for user {UserPublicId} in circle {SavingsCircleId} " +
                "was created concurrently. Refunding the debit to avoid double-charge.",
                UserPublicId,
                SavingsCircleId);

            await _identityService.CreditBalanceAsync(
                UserPublicId,
                TotalUpfrontCharge,
                CancellationToken.None);

            await RecordRefundAsync(
                UserPublicId,
                nonFeeAmount,
                TotalMovaFees,
                SavingsCircleId,
                Name,
                CancellationToken.None);

            await TryStartCircleIfReadySafeAsync(SavingsCircleId, CancellationToken.None);
            return;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to create circle wallet for user {UserPublicId} in circle {SavingsCircleId}. " +
                "Initiating refund of {TotalUpfrontCharge}.",
                UserPublicId,
                SavingsCircleId,
                TotalUpfrontCharge);

            await _identityService.CreditBalanceAsync(
                UserPublicId,
                TotalUpfrontCharge,
                CancellationToken.None);

            await RecordRefundAsync(
                UserPublicId,
                nonFeeAmount,
                TotalMovaFees,
                SavingsCircleId,
                Name,
                CancellationToken.None);

            throw;
        }

        await TryStartCircleIfReadySafeAsync(SavingsCircleId, cancellationToken);
    }

    private async Task TryStartCircleIfReadySafeAsync(
        long savingsCircleId,
        CancellationToken cancellationToken)
    {
        try
        {
            await TryStartCircleIfReadyAsync(savingsCircleId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Auto-start check failed for circle {SavingsCircleId}. " +
                "Circle remains in Draft status and can be started manually.",
                savingsCircleId);
        }
    }

    private async Task TryStartCircleIfReadyAsync(
        long savingsCircleId,
        CancellationToken cancellationToken)
    {
        var circle = await _context.SavingsCircles
            .Include(c => c.Members)
            .FirstOrDefaultAsync(c => c.Id == savingsCircleId, cancellationToken);

        if (circle is null)
        {
            _logger.LogWarning(
                "Circle {SavingsCircleId} not found for auto-start check.",
                savingsCircleId);
            return;
        }

        if (circle.Status != SavingsCircleStatus.Draft)
        {
            _logger.LogDebug(
                "Circle {SavingsCircleId} is {Status}; not Draft. Skipping auto-start.",
                savingsCircleId,
                circle.Status);
            return;
        }

        if (circle.ModeratorStartsCircle)
        {
            _logger.LogInformation(
                "Circle {SavingsCircleId} is set for manual start by moderator. " +
                "Skipping auto-start.",
                savingsCircleId);
            return;
        }

        var now = DateTimeOffset.UtcNow;

        if (circle.StartDate > now)
        {
            _logger.LogInformation(
                "Circle {SavingsCircleId} start date {StartDate:O} not yet reached. Waiting.",
                savingsCircleId,
                circle.StartDate);
            return;
        }

        var activeMembers = circle.Members
            .Where(m => m.Status == CircleMemberStatus.Active)
            .ToList();

        var activeMemberCount = activeMembers.Count;
        var pendingInviteCount = circle.Members
            .Count(m => m.Status == CircleMemberStatus.Invited);

        var requiredMemberCount = circle.StartPolicy == StartPolicy.WaitForAll
            ? circle.MemberCount
            : MinMembersToStart;

        var readyToStart = circle.StartPolicy switch
        {
            StartPolicy.WaitForAll => activeMemberCount >= circle.MemberCount,
            StartPolicy.StartWithJoined => activeMemberCount >= MinMembersToStart,
            _ => false
        };

        if (!readyToStart)
        {
            _logger.LogInformation(
                "Circle {SavingsCircleId} not ready to start. " +
                "Policy: {Policy}, Active: {Active}/{Required}, PendingInvites: {Pending}.",
                savingsCircleId,
                circle.StartPolicy,
                activeMemberCount,
                requiredMemberCount,
                pendingInviteCount);
            return;
        }

        var frequencyValidation = await _savingsCircleService.ValidateFrequencyAsync(
            circle.Frequency,
            circle.FrequencyConfig,
            cancellationToken);

        if (!frequencyValidation.Success)
        {
            _logger.LogError(
                "Circle {SavingsCircleId} cannot start: invalid frequency config. {Message}",
                savingsCircleId,
                frequencyValidation.Message);

            throw new InvalidOperationException(
                $"Circle {savingsCircleId} has an invalid frequency config: " +
                $"{frequencyValidation.Message}");
        }

        circle.Status = SavingsCircleStatus.Active;

        if (circle.RotationStrategy == RotationStrategy.RandomDraw)
        {
            var random = Random.Shared;
            for (var i = activeMembers.Count - 1; i > 0; i--)
            {
                var j = random.Next(i + 1);
                (activeMembers[i], activeMembers[j]) =
                    (activeMembers[j], activeMembers[i]);
            }

            for (var i = 0; i < activeMembers.Count; i++)
            {
                activeMembers[i].RotationPosition = i + 1;
            }

            _logger.LogInformation(
                "RandomDraw rotation assigned for circle {SavingsCircleId} " +
                "across {Count} members.",
                savingsCircleId,
                activeMembers.Count);
        }

        var orderedMembers = activeMembers
            .OrderBy(m => m.RotationPosition ?? int.MaxValue)
            .ThenBy(m => m.AcceptedAt ?? m.InvitedAt)
            .ToList();

        if (orderedMembers.Count == 0)
        {
            _logger.LogWarning(
                "Circle {SavingsCircleId} is ready to start but no active " +
                "members have rotation positions. Aborting auto-start.",
                savingsCircleId);
            return;
        }

        if (circle.MemberCount != orderedMembers.Count)
        {
            _logger.LogInformation(
                "Circle {SavingsCircleId} starting with {Active} members " +
                "(configured for {Configured}). Aligning MemberCount.",
                savingsCircleId,
                orderedMembers.Count,
                circle.MemberCount);

            circle.MemberCount = orderedMembers.Count;
        }

        var firstCollectionDate = _savingsCircleService.GetNextCollectionDate(
            circle.Frequency,
            circle.FrequencyConfig,
            now);

        circle.NextCollectionDate = firstCollectionDate;
        circle.NextPayoutDate = firstCollectionDate;

        var roundOneRecipient = orderedMembers[0];

        var firstRound = new CircleRound
        {
            SavingsCircleId = savingsCircleId,
            RoundNumber = 1,
            CollectionDate = firstCollectionDate,
            PayoutDate = firstCollectionDate,
            RecipientUserPublicId = roundOneRecipient.UserPublicId,
            PooledAmount = Money.FromNaira(0),
            PaidOutAmount = Money.FromNaira(0),
            Status = CircleRoundStatus.Collecting,
            RequiresManualReview = false
        };

        _context.CircleRounds.Add(firstRound);
        await _context.SaveChangesAsync(cancellationToken);

        var alreadySeeded = await _context.Contributions
            .AnyAsync(c => c.CircleRoundId == firstRound.Id, cancellationToken);

        if (!alreadySeeded)
        {
            var collectedAt = DateTimeOffset.UtcNow;
            var contributionAmount = circle.ContributionAmount.ToDecimal();
            var totalPooled = 0m;

            foreach (var member in orderedMembers)
            {
                _context.Contributions.Add(new Contribution
                {
                    CircleRoundId = firstRound.Id,
                    CircleMemberId = member.Id,
                    Amount = circle.ContributionAmount,
                    PenaltyApplied = Money.FromNaira(0),
                    Status = ContributionStatus.Collected,
                    CollectedAt = collectedAt,
                    PaymentReference =
                        $"circle-start-{savingsCircleId}-{member.UserPublicId}",
                    FailureReason = null
                });

                var previousDepositHeld = member.DepositHeld.ToDecimal();
                var newDepositHeld = Math.Max(0m, previousDepositHeld - contributionAmount);

                member.DepositHeld = Money.FromNaira(newDepositHeld);
                member.TotalContributed = Money.FromNaira(
                    member.TotalContributed.ToDecimal() + contributionAmount);

                totalPooled += contributionAmount;

                _logger.LogInformation(
                    "Member {MemberId} first-round contribution debited from " +
                    "DepositHeld. Previous: {PreviousDepositHeld}, Debit: " +
                    "{DebitedAmount}, Remaining: {NewDepositHeld}.",
                    member.Id,
                    previousDepositHeld,
                    contributionAmount,
                    newDepositHeld);
            }

            firstRound.PooledAmount = Money.FromNaira(totalPooled);

            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Round 1 seeded for circle {SavingsCircleId}. " +
                "{Count} contributions marked Collected (total {Pooled}).",
                savingsCircleId,
                orderedMembers.Count,
                totalPooled);
        }

        _logger.LogInformation(
            "Circle {SavingsCircleId} started. Round 1 (Collecting) created " +
            "for {Recipient}. Collection date: {CollectionDate:O}.",
            savingsCircleId,
            roundOneRecipient.DisplayName ?? roundOneRecipient.UserPublicId,
            firstCollectionDate);
    }

    private async Task RecordDebitAsync(
        string userPublicId,
        decimal amount,
        long savingsCircleId,
        string circleName,
        bool isFee,
        CancellationToken cancellationToken)
    {
        if (amount <= 0) return;

        var transaction = new Transaction
        {
            UserPublicId = userPublicId,
            WalletId = null,
            Title = isFee
                ? "MOVA circle fee"
                : $"Join circle: {circleName}",
            Provider = null,
            Amount = Money.FromNaira(amount),
            Type = isFee ? TransactionType.Fee : TransactionType.Fee,
            Status = TransactionStatus.Completed,
            Reference = $"circle-{(isFee ? "fee" : "debit")}-" +
                        $"{savingsCircleId}-{userPublicId}-{Guid.NewGuid():N}",
            CompletedAt = DateTimeOffset.UtcNow
        };

        _context.Transactions.Add(transaction);
        await _context.SaveChangesAsync(cancellationToken);

        _context.LedgerEntries.Add(new LedgerEntry
        {
            WalletId = null,
            TransactionId = transaction.Id,
            Amount = Money.FromNaira(amount),
            IsCredit = false
        });

        await _context.SaveChangesAsync(cancellationToken);
    }

    private async Task RecordRefundAsync(
        string userPublicId,
        decimal nonFeeAmount,
        decimal totalMovaFees,
        long savingsCircleId,
        string circleName,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;

        if (nonFeeAmount > 0)
        {
            var refund = new Transaction
            {
                UserPublicId = userPublicId,
                WalletId = null,
                Title = $"Refund: Join circle failed ({circleName})",
                Provider = null,
                Amount = Money.FromNaira(nonFeeAmount),
                Type = TransactionType.Refund,
                Status = TransactionStatus.Completed,
                Reference = $"circle-refund-{savingsCircleId}-{userPublicId}-{Guid.NewGuid():N}",
                CompletedAt = now
            };

            _context.Transactions.Add(refund);
            await _context.SaveChangesAsync(cancellationToken);

            _context.LedgerEntries.Add(new LedgerEntry
            {
                WalletId = null,
                TransactionId = refund.Id,
                Amount = Money.FromNaira(nonFeeAmount),
                IsCredit = true
            });

            await _context.SaveChangesAsync(cancellationToken);
        }

        if (totalMovaFees > 0)
        {
            var feeRefund = new Transaction
            {
                UserPublicId = userPublicId,
                WalletId = null,
                Title = "Refund: MOVA circle fee",
                Provider = null,
                Amount = Money.FromNaira(totalMovaFees),
                Type = TransactionType.Refund,
                Status = TransactionStatus.Completed,
                Reference = $"circle-fee-refund-{savingsCircleId}-{userPublicId}-{Guid.NewGuid():N}",
                CompletedAt = now
            };

            _context.Transactions.Add(feeRefund);
            await _context.SaveChangesAsync(cancellationToken);

            _context.LedgerEntries.Add(new LedgerEntry
            {
                WalletId = null,
                TransactionId = feeRefund.Id,
                Amount = Money.FromNaira(totalMovaFees),
                IsCredit = true
            });

            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    private static bool IsUniqueViolation(DbUpdateException ex)
    {
        var message = ex.InnerException?.Message ?? string.Empty;

        return message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase)
            || message.Contains("unique constraint", StringComparison.OrdinalIgnoreCase)
            || message.Contains("unique_violation", StringComparison.OrdinalIgnoreCase)
            || message.Contains("23505", StringComparison.OrdinalIgnoreCase);
    }
}