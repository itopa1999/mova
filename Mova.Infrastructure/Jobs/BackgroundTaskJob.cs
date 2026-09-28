using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mova.Application.Interfaces.Payment;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
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

        // 1. Make sure the user exists
        var userExists = await _context.Users
            .AnyAsync(
                x => x.PublicId == userPublicId,
                cancellationToken);

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
                x =>
                    x.UserPublicId == userPublicId &&
                    x.Provider == PaymentProvider.Paystack &&
                    x.Status == VirtualAccountStatus.Active,
                cancellationToken);

        if (existingAccount is not null)
        {
            _logger.LogInformation(
                "User {UserPublicId} already has an active Paystack virtual account {AccountNumber}.",
                userPublicId,
                existingAccount.AccountNumber);

            return;
        }

        try
        {
            // 3. Create/find Paystack customer
            var customer = await _paystackService.CreateCustomerAsync(
                firstName,
                lastName,
                email,
                phoneNumber,
                cancellationToken);

            if (customer is null)
            {
                throw new InvalidOperationException(
                    $"Unable to create Paystack customer for user {userPublicId}.");
            }

            _logger.LogInformation(
                "Paystack customer created/found for {UserPublicId}. Customer: {CustomerCode}.",
                userPublicId,
                customer.CustomerCode);

            // 4. Create Paystack Dedicated Virtual Account
            var dedicatedAccount =
                await _paystackService.CreateDedicatedVirtualAccountAsync(
                    customer.CustomerCode,
                    cancellationToken);

            if (dedicatedAccount is null)
            {
                throw new InvalidOperationException(
                    $"Paystack did not return a dedicated virtual account for user {userPublicId}.");
            }

            if (string.IsNullOrWhiteSpace(dedicatedAccount.AccountNumber))
            {
                throw new InvalidOperationException(
                    $"Paystack returned an empty account number for user {userPublicId}.");
            }

            // 5. Save the virtual account in our own table
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

            _context.VirtualAccounts.Add(virtualAccount);

            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Paystack virtual account created successfully for {UserPublicId}. " +
                "Account: {AccountNumber}, Bank: {BankName}.",
                userPublicId,
                dedicatedAccount.AccountNumber,
                dedicatedAccount.BankName);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to create Paystack virtual account for {UserPublicId}.",
                userPublicId);

            throw;
        }
    }
}