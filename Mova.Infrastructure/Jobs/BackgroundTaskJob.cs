using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mova.Application.Interfaces.Payment;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Domain.ValueObjects;
using Mova.Infrastructure.Persistence;

namespace Mova.Infrastructure.Jobs;

public sealed class BackgroundTaskJob
{
    private readonly ApplicationDbContext _context;
    private readonly IPaystackService _paystackService;
    private readonly ILogger<BackgroundTaskJob> _logger;

    public BackgroundTaskJob(
        ApplicationDbContext context,
        IPaystackService paystackService,
        ILogger<BackgroundTaskJob> logger)
    {
        _context = context;
        _paystackService = paystackService;
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
            LockedAmount = Money.FromNaira(0),
            ResetAmount = Money.FromNaira(0),
            FundedAmount = Money.FromNaira(0),
            TotalReleasedAmount = Money.FromNaira(0),
            TotalWithdrawnAmount = Money.FromNaira(0),
            PayoutDestination = PayoutDestination.Wallet,
        };

        try
        {
            _context.Wallets.Add(wallet);
            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Circle wallet created. WalletId: {WalletId}, " +
                "User: {UserPublicId}, Circle: {SavingsCircleId}.",
                wallet.Id,
                UserPublicId,
                SavingsCircleId);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            _logger.LogInformation(
                "Circle wallet for user {UserPublicId} in circle {SavingsCircleId} " +
                "was created concurrently. Skipping.",
                UserPublicId,
                SavingsCircleId);
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