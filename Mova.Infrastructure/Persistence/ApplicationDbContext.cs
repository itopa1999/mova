using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Mova.Domain.Common;
using Mova.Domain.Entities;
using Mova.Infrastructure.Identity;

namespace Mova.Infrastructure.Persistence;

public sealed class ApplicationDbContext
    : IdentityDbContext<User, IdentityRole<long>, long>
{
    public ApplicationDbContext(
        DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<Wallet> Wallets => Set<Wallet>();
    public DbSet<WalletRule> WalletRules => Set<WalletRule>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<ScheduledRelease> ScheduledReleases => Set<ScheduledRelease>();
    public DbSet<LedgerEntry> LedgerEntries => Set<LedgerEntry>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<OtpVerification> OtpVerifications => Set<OtpVerification>();
    public DbSet<VirtualAccount> VirtualAccounts => Set<VirtualAccount>();
    public DbSet<BankAccount> BankAccounts => Set<BankAccount>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(ApplicationDbContext).Assembly);
    }

    public override async Task<int> SaveChangesAsync(
        CancellationToken cancellationToken = default)
    {
        var entries = ChangeTracker
            .Entries<BaseEntity>();

        foreach (var entry in entries)
        {
            switch (entry.State)
            {
                case EntityState.Added:

                    entry.Entity.CreatedAt =
                        DateTimeOffset.UtcNow;

                    break;


                case EntityState.Modified:

                    entry.Entity.ModifiedAt =
                        DateTimeOffset.UtcNow;

                    break;


                case EntityState.Deleted:

                    entry.State = EntityState.Modified;

                    entry.Entity.IsDeleted = true;

                    entry.Entity.DeletedAt =
                        DateTimeOffset.UtcNow;

                    break;
            }
        }

        // PostgreSQL's `timestamp with time zone` stores an instant in UTC. Npgsql rejects
        // DateTimeOffset values with a non-zero offset, so normalize all tracked timestamps
        // at the persistence boundary (including schedule dates supplied by the client).
        foreach (var entry in ChangeTracker.Entries()
                     .Where(x => x.State is EntityState.Added or EntityState.Modified))
        {
            foreach (var property in entry.Properties)
            {
                if (property.CurrentValue is DateTimeOffset value && value.Offset != TimeSpan.Zero)
                {
                    property.CurrentValue = value.ToUniversalTime();
                }
            }
        }

        return await base.SaveChangesAsync(
            cancellationToken);
    }
}
