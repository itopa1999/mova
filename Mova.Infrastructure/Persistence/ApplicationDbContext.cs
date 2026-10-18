using System.Linq.Expressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Mova.Application.Interfaces.Services;
using Mova.Domain.Common;
using Mova.Domain.Entities;
using Mova.Infrastructure.Identity;

namespace Mova.Infrastructure.Persistence;

public class ApplicationDbContext
    : IdentityDbContext<User, IdentityRole<long>, long>
{
    private readonly ICurrentUserService? _currentUser;

    public ApplicationDbContext(
        DbContextOptions<ApplicationDbContext> options,
        ICurrentUserService? currentUser)
        : base(options)
    {
        _currentUser = currentUser;
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
    public DbSet<Bank> Banks => Set<Bank>();
    public DbSet<Payout> Payouts => Set<Payout>();
    public DbSet<WalletCategory> WalletCategories => Set<WalletCategory>();
    public DbSet<AppNotification> AppNotifications => Set<AppNotification>();
    public DbSet<FeatureFlag> FeatureFlags => Set<FeatureFlag>();
    public DbSet<RenewalPolicy> RenewalPolicies => Set<RenewalPolicy>();
    public DbSet<RenewalEvent> RenewalEvents => Set<RenewalEvent>();
    public DbSet<WalletTemplate> WalletTemplates => Set<WalletTemplate>();
    public DbSet<Feedback> Feedbacks => Set<Feedback>();
    public DbSet<PinChallenge> PinChallenges => Set<PinChallenge>();
    public DbSet<WalletTrustee> WalletTrustees => Set<WalletTrustee>();
    public DbSet<ReleaseRequest> ReleaseRequests => Set<ReleaseRequest>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(ApplicationDbContext).Assembly);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(BaseEntity).IsAssignableFrom(entityType.ClrType))
                continue;

            if (entityType.IsOwned())
                continue;

            var parameter = Expression.Parameter(entityType.ClrType, "e");

            var isDeletedProperty = Expression.Property(
                parameter,
                nameof(BaseEntity.IsDeleted));

            var filter = Expression.Lambda(
                Expression.Equal(
                    isDeletedProperty,
                    Expression.Constant(false)),
                parameter);

            modelBuilder
                .Entity(entityType.ClrType)
                .HasQueryFilter(filter);
        }
    }

    
public override int SaveChanges()
{
    PrepareChanges();
    return base.SaveChanges();
}

public override int SaveChanges(bool acceptAllChangesOnSuccess)
{
    PrepareChanges();
    return base.SaveChanges(acceptAllChangesOnSuccess);
}

    public override Task<int> SaveChangesAsync(
        CancellationToken cancellationToken = default)
    {
        PrepareChanges();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        PrepareChanges();
        return base.SaveChangesAsync(
            acceptAllChangesOnSuccess,
            cancellationToken);
    }

    private void PrepareChanges()
    {
        var now = DateTimeOffset.UtcNow;
        var currentUserId = _currentUser?.UserId.ToString();

        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAt = now;
                    break;

                case EntityState.Modified:
                    entry.Entity.ModifiedAt = now;
                    entry.Entity.ModifiedBy = currentUserId;
                    break;

                case EntityState.Deleted:
                    entry.State = EntityState.Modified;
                    entry.Entity.IsDeleted = true;
                    entry.Entity.DeletedAt = now;
                    entry.Entity.DeletedBy = currentUserId;
                    entry.Entity.ModifiedAt = now;
                    entry.Entity.ModifiedBy = currentUserId;
                    break;
            }
        }

        foreach (var entry in ChangeTracker.Entries()
                    .Where(entry => entry.State is
                        EntityState.Added or EntityState.Modified))
        {
            foreach (var property in entry.Properties)
            {
                if (property.CurrentValue is DateTimeOffset value &&
                    value.Offset != TimeSpan.Zero)
                {
                    property.CurrentValue = value.ToUniversalTime();
                }
            }
        }
    }
}