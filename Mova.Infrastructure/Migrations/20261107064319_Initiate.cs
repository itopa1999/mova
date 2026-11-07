using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Mova.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Initiate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AspNetRoles",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUsers",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PublicId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    FirstName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    OtherNames = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    LastName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ProfilePicture = table.Column<string>(type: "text", nullable: true),
                    TransactionPinHash = table.Column<string>(type: "text", nullable: true),
                    BvnHash = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    TransactionPinSetAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TransactionPinResetAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TransactionPinChangedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastKnownDeviceId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    NotifyLoginAlerts = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    NotifyReleaseAlerts = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    NotifyProductUpdates = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    NotifyPromotions = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    AccountStatus = table.Column<int>(type: "integer", nullable: false),
                    RestrictionReason = table.Column<int>(type: "integer", nullable: true),
                    RestrictionReasonDetails = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    RestrictedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RestrictedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    RestrictionExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReactivatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ModifiedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    DeletedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    balance_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    balance_minor_units = table.Column<long>(type: "bigint", nullable: false),
                    UserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedUserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: true),
                    SecurityStamp = table.Column<string>(type: "text", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "text", nullable: true),
                    PhoneNumber = table.Column<string>(type: "text", nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "bank_accounts",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserPublicId = table.Column<string>(type: "text", nullable: false),
                    AccountNumber = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    AccountName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    BankCode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    BankName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    BankImageUrl = table.Column<string>(type: "text", nullable: false),
                    PaystackRecipientCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    FlutterwaveRecipientId = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    VerifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    VerificationMessage = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ConsentGiven = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    ConsentGivenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ConsentVersion = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Institution = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false, defaultValue: "NGN"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ModifiedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    DeletedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bank_accounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "banks",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Slug = table.Column<string>(type: "text", nullable: false),
                    Code = table.Column<string>(type: "text", nullable: false),
                    Ussd = table.Column<string>(type: "text", nullable: true),
                    Logo = table.Column<string>(type: "text", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ModifiedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    DeletedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_banks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "feature_flags",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    Metadata = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ModifiedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    DeletedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_feature_flags", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "feedbacks",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserPublicId = table.Column<string>(type: "text", nullable: false),
                    Rating = table.Column<int>(type: "integer", nullable: false),
                    Experience = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    Improvements = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Message = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: true),
                    IsDone = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ModifiedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    DeletedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_feedbacks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "notifications",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserPublicId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    IsRead = table.Column<bool>(type: "boolean", nullable: false),
                    ActionUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Metadata = table.Column<string>(type: "jsonb", nullable: true),
                    ReadAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ModifiedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    DeletedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notifications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "otp_verifications",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserPublicId = table.Column<string>(type: "text", nullable: false),
                    OtpCode = table.Column<string>(type: "text", nullable: false),
                    Purpose = table.Column<string>(type: "text", nullable: false),
                    IsUsed = table.Column<bool>(type: "boolean", nullable: false),
                    UsedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ModifiedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    DeletedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_otp_verifications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "pin_challenges",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserPublicId = table.Column<string>(type: "text", nullable: false),
                    PinChallengeType = table.Column<int>(type: "integer", maxLength: 256, nullable: false),
                    IsUsed = table.Column<bool>(type: "boolean", nullable: false),
                    UsedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ModifiedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    DeletedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pin_challenges", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "refresh_tokens",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserPublicId = table.Column<string>(type: "text", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokedByIp = table.Column<string>(type: "text", nullable: true),
                    ReplacedByTokenHash = table.Column<string>(type: "text", nullable: true),
                    RevocationReason = table.Column<string>(type: "text", nullable: true),
                    CreatedByIp = table.Column<string>(type: "text", nullable: true),
                    DeviceName = table.Column<string>(type: "text", nullable: true),
                    UserAgent = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ModifiedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    DeletedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_refresh_tokens", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "savings_circles",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    OrganizerUserPublicId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Frequency = table.Column<int>(type: "integer", nullable: false),
                    FrequencyConfig = table.Column<string>(type: "text", nullable: false),
                    MemberCount = table.Column<int>(type: "integer", nullable: false),
                    AllowPenalty = table.Column<bool>(type: "boolean", nullable: false),
                    PenaltyAmountFrom = table.Column<int>(type: "integer", nullable: false),
                    GracePeriodHours = table.Column<int>(type: "integer", nullable: false),
                    RotationStrategy = table.Column<int>(type: "integer", nullable: false),
                    StartDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    StartPolicy = table.Column<int>(type: "integer", nullable: false),
                    ModeratorStartsCircle = table.Column<bool>(type: "boolean", nullable: false),
                    NextCollectionDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    NextPayoutDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    InviteOnly = table.Column<bool>(type: "boolean", nullable: false),
                    InviteCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PoolBankAccountId = table.Column<long>(type: "bigint", nullable: true),
                    contribution_amount_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    contribution_amount_minor_units = table.Column<long>(type: "bigint", nullable: false),
                    deposit_amount_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    deposit_amount_minor_units = table.Column<long>(type: "bigint", nullable: false),
                    penalty_amount_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    penalty_amount_minor_units = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ModifiedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    DeletedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_savings_circles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "virtual_accounts",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserPublicId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Provider = table.Column<int>(type: "integer", maxLength: 50, nullable: false),
                    ProviderCustomerId = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    ProviderAccountId = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    AccountNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    BankName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    AccountName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Currency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false, defaultValue: "NGN"),
                    Status = table.Column<int>(type: "integer", maxLength: 30, nullable: false, defaultValue: 1),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ModifiedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    DeletedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_virtual_accounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "wallet_categories",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Icon = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    WalletType = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ModifiedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    DeletedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wallet_categories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "wallet_trustees",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OwnerPublicId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    VerificationTokenHash = table.Column<byte[]>(type: "bytea", nullable: true),
                    TokenExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    InvitedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    VerifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ConsentedFromIp = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ModifiedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    DeletedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wallet_trustees", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoleClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RoleId = table.Column<long>(type: "bigint", nullable: false),
                    ClaimType = table.Column<string>(type: "text", nullable: true),
                    ClaimValue = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoleClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetRoleClaims_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    ClaimType = table.Column<string>(type: "text", nullable: true),
                    ClaimValue = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetUserClaims_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserLogins",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "text", nullable: false),
                    ProviderKey = table.Column<string>(type: "text", nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "text", nullable: true),
                    UserId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserLogins", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_AspNetUserLogins_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserRoles",
                columns: table => new
                {
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    RoleId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserTokens",
                columns: table => new
                {
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    LoginProvider = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_AspNetUserTokens_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "circle_rounds",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SavingsCircleId = table.Column<long>(type: "bigint", nullable: false),
                    RoundNumber = table.Column<int>(type: "integer", nullable: false),
                    CollectionDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PayoutDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RecipientUserPublicId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    PaidOutAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PayoutTransactionReference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    RequiresManualReview = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    paid_out_amount_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    paid_out_amount_minor_units = table.Column<long>(type: "bigint", nullable: false),
                    pooled_amount_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    pooled_amount_minor_units = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ModifiedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    DeletedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_circle_rounds", x => x.Id);
                    table.ForeignKey(
                        name: "FK_circle_rounds_savings_circles_SavingsCircleId",
                        column: x => x.SavingsCircleId,
                        principalTable: "savings_circles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "wallet_templates",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CategoryId = table.Column<long>(type: "bigint", nullable: false),
                    WalletType = table.Column<int>(type: "integer", nullable: true, defaultValue: 1),
                    DefaultFrequency = table.Column<int>(type: "integer", nullable: false),
                    DefaultFrequencyConfig = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    DefaultPayoutDestination = table.Column<int>(type: "integer", nullable: false, defaultValue: 2),
                    IconName = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "Wallet"),
                    Tags = table.Column<string[]>(type: "text[]", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    UsageCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    default_release_amount_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    default_release_amount_minor_units = table.Column<long>(type: "bigint", nullable: false),
                    default_target_amount_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    default_target_amount_minor_units = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ModifiedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    DeletedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wallet_templates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_wallet_templates_wallet_categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "wallet_categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "wallets",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserPublicId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CategoryId = table.Column<long>(type: "bigint", nullable: false, defaultValue: 24L),
                    BankAccountId = table.Column<long>(type: "bigint", nullable: true),
                    WalletTrusteeId = table.Column<long>(type: "bigint", nullable: true),
                    SavingsCircleId = table.Column<long>(type: "bigint", nullable: true),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    PayoutDestination = table.Column<int>(type: "integer", nullable: false),
                    RestartCount = table.Column<int>(type: "integer", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ClosedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UtilityType = table.Column<int>(type: "integer", nullable: true),
                    UtilityConfigJson = table.Column<string>(type: "text", nullable: true),
                    available_amount_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    available_amount_minor_units = table.Column<long>(type: "bigint", nullable: false),
                    funded_amount_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    funded_amount_minor_units = table.Column<long>(type: "bigint", nullable: false),
                    locked_amount_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    locked_amount_minor_units = table.Column<long>(type: "bigint", nullable: false),
                    reset_amount_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    reset_amount_minor_units = table.Column<long>(type: "bigint", nullable: false),
                    target_amount_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    target_amount_minor_units = table.Column<long>(type: "bigint", nullable: false),
                    total_released_amount_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    total_released_amount_minor_units = table.Column<long>(type: "bigint", nullable: false),
                    total_withdrawn_amount_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    total_withdrawn_amount_minor_units = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ModifiedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    DeletedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wallets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_wallets_bank_accounts_BankAccountId",
                        column: x => x.BankAccountId,
                        principalTable: "bank_accounts",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_wallets_savings_circles_SavingsCircleId",
                        column: x => x.SavingsCircleId,
                        principalTable: "savings_circles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_wallets_wallet_categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "wallet_categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_wallets_wallet_trustees_WalletTrusteeId",
                        column: x => x.WalletTrusteeId,
                        principalTable: "wallet_trustees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "circle_members",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SavingsCircleId = table.Column<long>(type: "bigint", nullable: false),
                    Identifier = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    UserPublicId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Phone = table.Column<string>(type: "text", nullable: true),
                    DisplayName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    InvitedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AcceptedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    WalletId = table.Column<long>(type: "bigint", nullable: true),
                    RotationPosition = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    MissedContributions = table.Column<int>(type: "integer", nullable: false),
                    PayoutReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deposit_held_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    deposit_held_minor_units = table.Column<long>(type: "bigint", nullable: false),
                    total_contributed_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    total_contributed_minor_units = table.Column<long>(type: "bigint", nullable: false),
                    total_received_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    total_received_minor_units = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ModifiedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    DeletedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_circle_members", x => x.Id);
                    table.ForeignKey(
                        name: "FK_circle_members_savings_circles_SavingsCircleId",
                        column: x => x.SavingsCircleId,
                        principalTable: "savings_circles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_circle_members_wallets_WalletId",
                        column: x => x.WalletId,
                        principalTable: "wallets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "payouts",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserPublicId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    WalletId = table.Column<long>(type: "bigint", nullable: false),
                    BankAccountId = table.Column<long>(type: "bigint", nullable: true),
                    Destination = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    Reference = table.Column<string>(type: "text", nullable: false),
                    Provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ProviderReference = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    InitiatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FailedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    MainCreditedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FailedAttempts = table.Column<int>(type: "integer", nullable: false),
                    FailureReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    amount_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    amount_minor_units = table.Column<long>(type: "bigint", nullable: false),
                    fee_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    fee_minor_units = table.Column<long>(type: "bigint", nullable: false),
                    net_amount_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    net_amount_minor_units = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ModifiedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    DeletedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payouts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_payouts_bank_accounts_BankAccountId",
                        column: x => x.BankAccountId,
                        principalTable: "bank_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_payouts_wallets_WalletId",
                        column: x => x.WalletId,
                        principalTable: "wallets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "release_requests",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    WalletId = table.Column<long>(type: "bigint", nullable: false),
                    RequestedByUserPublicId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Destination = table.Column<int>(type: "integer", nullable: false),
                    DestinationBankAccountId = table.Column<long>(type: "bigint", nullable: true),
                    DestinationWalletId = table.Column<long>(type: "bigint", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    UserNote = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    RequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DecidedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DecidedByTrusteeId = table.Column<long>(type: "bigint", nullable: true),
                    DecisionReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    FailureReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ApprovalTokenHash = table.Column<byte[]>(type: "bytea", nullable: true),
                    TokenUsedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TokenUsedFromIp = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    WalletId1 = table.Column<long>(type: "bigint", nullable: true),
                    amount_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    amount_minor_units = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ModifiedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    DeletedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_release_requests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_release_requests_wallet_trustees_DecidedByTrusteeId",
                        column: x => x.DecidedByTrusteeId,
                        principalTable: "wallet_trustees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_release_requests_wallets_WalletId",
                        column: x => x.WalletId,
                        principalTable: "wallets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_release_requests_wallets_WalletId1",
                        column: x => x.WalletId1,
                        principalTable: "wallets",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "renewal_policies",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    WalletId = table.Column<long>(type: "bigint", nullable: false),
                    UserPublicId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    Status = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    TriggerType = table.Column<int>(type: "integer", nullable: false),
                    RefillAmountType = table.Column<int>(type: "integer", nullable: false),
                    MaxRenewals = table.Column<int>(type: "integer", nullable: true),
                    RenewalsCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    RefillUntilMainBalanceExhausted = table.Column<bool>(type: "boolean", nullable: false),
                    min_main_balance_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    min_main_balance_minor_units = table.Column<long>(type: "bigint", nullable: false),
                    refill_amount_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    refill_amount_minor_units = table.Column<long>(type: "bigint", nullable: false),
                    trigger_amount_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    trigger_amount_minor_units = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ModifiedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    DeletedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_renewal_policies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_renewal_policies_wallets_WalletId",
                        column: x => x.WalletId,
                        principalTable: "wallets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "transactions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserPublicId = table.Column<string>(type: "text", nullable: false),
                    WalletId = table.Column<long>(type: "bigint", nullable: true),
                    Title = table.Column<string>(type: "text", nullable: false, defaultValue: ""),
                    Provider = table.Column<int>(type: "integer", nullable: true),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    FailureReason = table.Column<string>(type: "text", nullable: true),
                    Reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    amount_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    amount_minor_units = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ModifiedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    DeletedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_transactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_transactions_wallets_WalletId",
                        column: x => x.WalletId,
                        principalTable: "wallets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "wallet_rules",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    WalletId = table.Column<long>(type: "bigint", nullable: false),
                    Frequency = table.Column<int>(type: "integer", nullable: false),
                    FrequencyConfig = table.Column<string>(type: "text", nullable: false),
                    StartDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    amount_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    amount_minor_units = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ModifiedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    DeletedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wallet_rules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_wallet_rules_wallets_WalletId",
                        column: x => x.WalletId,
                        principalTable: "wallets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "contributions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CircleRoundId = table.Column<long>(type: "bigint", nullable: false),
                    CircleMemberId = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    CollectedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PaymentReference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    FailureReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    amount_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    amount_minor_units = table.Column<long>(type: "bigint", nullable: false),
                    penalty_applied_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    penalty_applied_minor_units = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ModifiedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    DeletedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_contributions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_contributions_circle_members_CircleMemberId",
                        column: x => x.CircleMemberId,
                        principalTable: "circle_members",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_contributions_circle_rounds_CircleRoundId",
                        column: x => x.CircleRoundId,
                        principalTable: "circle_rounds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ledger_entries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    WalletId = table.Column<long>(type: "bigint", nullable: true),
                    TransactionId = table.Column<long>(type: "bigint", nullable: false),
                    IsCredit = table.Column<bool>(type: "boolean", nullable: false),
                    amount_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    amount_minor_units = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ModifiedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    DeletedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ledger_entries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ledger_entries_transactions_TransactionId",
                        column: x => x.TransactionId,
                        principalTable: "transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ledger_entries_wallets_WalletId",
                        column: x => x.WalletId,
                        principalTable: "wallets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "renewal_events",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    WalletId = table.Column<long>(type: "bigint", nullable: false),
                    RenewalPolicyId = table.Column<long>(type: "bigint", nullable: false),
                    UserPublicId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Result = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    TransactionId = table.Column<long>(type: "bigint", nullable: true),
                    amount_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    amount_minor_units = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ModifiedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    DeletedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_renewal_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_renewal_events_renewal_policies_RenewalPolicyId",
                        column: x => x.RenewalPolicyId,
                        principalTable: "renewal_policies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_renewal_events_transactions_TransactionId",
                        column: x => x.TransactionId,
                        principalTable: "transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_renewal_events_wallets_WalletId",
                        column: x => x.WalletId,
                        principalTable: "wallets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "scheduled_releases",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    WalletId = table.Column<long>(type: "bigint", nullable: false),
                    WalletRuleId = table.Column<long>(type: "bigint", nullable: false),
                    ScheduledFor = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    FailedAttempts = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    ReleasedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    amount_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    amount_minor_units = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ModifiedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    DeletedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_scheduled_releases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_scheduled_releases_wallet_rules_WalletRuleId",
                        column: x => x.WalletRuleId,
                        principalTable: "wallet_rules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_scheduled_releases_wallets_WalletId",
                        column: x => x.WalletId,
                        principalTable: "wallets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AspNetRoleClaims_RoleId",
                table: "AspNetRoleClaims",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                table: "AspNetRoles",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserClaims_UserId",
                table: "AspNetUserClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserLogins_UserId",
                table: "AspNetUserLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserRoles_RoleId",
                table: "AspNetUserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "AspNetUsers",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_Email",
                table: "AspNetUsers",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_PhoneNumber",
                table: "AspNetUsers",
                column: "PhoneNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_PublicId",
                table: "AspNetUsers",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_bvn_hash",
                table: "AspNetUsers",
                column: "BvnHash");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "AspNetUsers",
                column: "NormalizedUserName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_bank_accounts_AccountNumber",
                table: "bank_accounts",
                column: "AccountNumber");

            migrationBuilder.CreateIndex(
                name: "IX_bank_accounts_RecipientCode",
                table: "bank_accounts",
                column: "PaystackRecipientCode");

            migrationBuilder.CreateIndex(
                name: "IX_bank_accounts_UserPublicId",
                table: "bank_accounts",
                column: "UserPublicId");

            migrationBuilder.CreateIndex(
                name: "IX_bank_accounts_UserPublicId_AccountNumber",
                table: "bank_accounts",
                columns: new[] { "UserPublicId", "AccountNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_bank_accounts_UserPublicId_IsDefault",
                table: "bank_accounts",
                columns: new[] { "UserPublicId", "IsDefault" },
                unique: true,
                filter: "\"IsDefault\" = true");

            migrationBuilder.CreateIndex(
                name: "IX_bank_accounts_UserPublicId_Status",
                table: "bank_accounts",
                columns: new[] { "UserPublicId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_circle_members_SavingsCircleId_Identifier",
                table: "circle_members",
                columns: new[] { "SavingsCircleId", "Identifier" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_circle_members_SavingsCircleId_RotationPosition",
                table: "circle_members",
                columns: new[] { "SavingsCircleId", "RotationPosition" },
                unique: true,
                filter: "\"RotationPosition\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_circle_members_SavingsCircleId_Status",
                table: "circle_members",
                columns: new[] { "SavingsCircleId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_circle_members_UserPublicId",
                table: "circle_members",
                column: "UserPublicId");

            migrationBuilder.CreateIndex(
                name: "IX_circle_members_WalletId",
                table: "circle_members",
                column: "WalletId");

            migrationBuilder.CreateIndex(
                name: "ix_circle_rounds_circle_round_number",
                table: "circle_rounds",
                columns: new[] { "SavingsCircleId", "RoundNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_circle_rounds_manual_review",
                table: "circle_rounds",
                column: "RequiresManualReview",
                filter: "\"RequiresManualReview\" = true");

            migrationBuilder.CreateIndex(
                name: "ix_circle_rounds_recipient",
                table: "circle_rounds",
                column: "RecipientUserPublicId");

            migrationBuilder.CreateIndex(
                name: "ix_circle_rounds_status_payout_date",
                table: "circle_rounds",
                columns: new[] { "Status", "PayoutDate" });

            migrationBuilder.CreateIndex(
                name: "ix_contributions_member",
                table: "contributions",
                column: "CircleMemberId");

            migrationBuilder.CreateIndex(
                name: "ix_contributions_payment_reference",
                table: "contributions",
                column: "PaymentReference",
                unique: true,
                filter: "\"PaymentReference\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_contributions_round_member",
                table: "contributions",
                columns: new[] { "CircleRoundId", "CircleMemberId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_contributions_status",
                table: "contributions",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_feature_flags_Name",
                table: "feature_flags",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_feedbacks_IsDone",
                table: "feedbacks",
                column: "IsDone");

            migrationBuilder.CreateIndex(
                name: "IX_feedbacks_UserPublicId",
                table: "feedbacks",
                column: "UserPublicId");

            migrationBuilder.CreateIndex(
                name: "IX_ledger_entries_TransactionId",
                table: "ledger_entries",
                column: "TransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_ledger_entries_WalletId",
                table: "ledger_entries",
                column: "WalletId");

            migrationBuilder.CreateIndex(
                name: "IX_ledger_entries_WalletId_CreatedAt",
                table: "ledger_entries",
                columns: new[] { "WalletId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_notifications_UserPublicId_CreatedAt",
                table: "notifications",
                columns: new[] { "UserPublicId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_notifications_UserPublicId_IsRead_CreatedAt",
                table: "notifications",
                columns: new[] { "UserPublicId", "IsRead", "CreatedAt" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "IX_payouts_BankAccountId",
                table: "payouts",
                column: "BankAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_payouts_ProcessingQueue",
                table: "payouts",
                column: "Id",
                filter: "\"IsDeleted\" = FALSE AND (\"Status\" = 1 OR \"Status\" = 2)");

            migrationBuilder.CreateIndex(
                name: "IX_payouts_ProviderReference",
                table: "payouts",
                column: "ProviderReference",
                unique: true,
                filter: "\"ProviderReference\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_payouts_Reference",
                table: "payouts",
                column: "Reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_payouts_UserPublicId",
                table: "payouts",
                column: "UserPublicId");

            migrationBuilder.CreateIndex(
                name: "IX_payouts_WalletId_Status",
                table: "payouts",
                columns: new[] { "WalletId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_pin_challenges_UserPublicId_PinChallengeType_IsUsed_Expires~",
                table: "pin_challenges",
                columns: new[] { "UserPublicId", "PinChallengeType", "IsUsed", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_refresh_tokens_ExpiresAt",
                table: "refresh_tokens",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_refresh_tokens_TokenHash",
                table: "refresh_tokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_refresh_tokens_UserPublicId",
                table: "refresh_tokens",
                column: "UserPublicId");

            migrationBuilder.CreateIndex(
                name: "IX_release_requests_ApprovalTokenHash",
                table: "release_requests",
                column: "ApprovalTokenHash",
                unique: true,
                filter: "\"ApprovalTokenHash\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_release_requests_DecidedByTrusteeId",
                table: "release_requests",
                column: "DecidedByTrusteeId");

            migrationBuilder.CreateIndex(
                name: "IX_release_requests_Status_ExpiresAt",
                table: "release_requests",
                columns: new[] { "Status", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_release_requests_WalletId_RequestedAt",
                table: "release_requests",
                columns: new[] { "WalletId", "RequestedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_release_requests_WalletId_Status_ExpiresAt",
                table: "release_requests",
                columns: new[] { "WalletId", "Status", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_release_requests_WalletId1",
                table: "release_requests",
                column: "WalletId1");

            migrationBuilder.CreateIndex(
                name: "ux_release_requests_pending_per_wallet",
                table: "release_requests",
                column: "WalletId",
                unique: true,
                filter: "\"Status\" = 1");

            migrationBuilder.CreateIndex(
                name: "ix_renewal_events_renewal_policy_id",
                table: "renewal_events",
                column: "RenewalPolicyId");

            migrationBuilder.CreateIndex(
                name: "ix_renewal_events_transaction_id",
                table: "renewal_events",
                column: "TransactionId");

            migrationBuilder.CreateIndex(
                name: "ix_renewal_events_wallet_id_occurred_at",
                table: "renewal_events",
                columns: new[] { "WalletId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "ix_renewal_policies_user_public_id",
                table: "renewal_policies",
                column: "UserPublicId");

            migrationBuilder.CreateIndex(
                name: "ux_renewal_policies_wallet_id_active",
                table: "renewal_policies",
                column: "WalletId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_savings_circles_InviteCode",
                table: "savings_circles",
                column: "InviteCode",
                unique: true,
                filter: "\"InviteCode\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_savings_circles_OrganizerUserPublicId",
                table: "savings_circles",
                column: "OrganizerUserPublicId");

            migrationBuilder.CreateIndex(
                name: "IX_savings_circles_OrganizerUserPublicId_Status",
                table: "savings_circles",
                columns: new[] { "OrganizerUserPublicId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_savings_circles_Status",
                table: "savings_circles",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_savings_circles_Status_StartDate",
                table: "savings_circles",
                columns: new[] { "Status", "StartDate" });

            migrationBuilder.CreateIndex(
                name: "IX_scheduled_releases_Status_ScheduledFor_Id",
                table: "scheduled_releases",
                columns: new[] { "Status", "ScheduledFor", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_scheduled_releases_WalletId",
                table: "scheduled_releases",
                column: "WalletId");

            migrationBuilder.CreateIndex(
                name: "IX_scheduled_releases_WalletRuleId_ScheduledFor_Status",
                table: "scheduled_releases",
                columns: new[] { "WalletRuleId", "ScheduledFor", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_transactions_ProcessingQueue",
                table: "transactions",
                column: "Id",
                filter: "\"IsDeleted\" = FALSE AND (\"Status\" = 1 OR \"Status\" = 2)");

            migrationBuilder.CreateIndex(
                name: "IX_transactions_Reference",
                table: "transactions",
                column: "Reference",
                unique: true,
                filter: "\"Reference\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_transactions_UserPublicId_CreatedAt_Id",
                table: "transactions",
                columns: new[] { "UserPublicId", "CreatedAt", "Id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "IX_transactions_WalletId",
                table: "transactions",
                column: "WalletId");

            migrationBuilder.CreateIndex(
                name: "IX_transactions_WalletId_Status",
                table: "transactions",
                columns: new[] { "WalletId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_virtual_accounts_Provider_AccountNumber",
                table: "virtual_accounts",
                columns: new[] { "Provider", "AccountNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_virtual_accounts_UserPublicId_Provider",
                table: "virtual_accounts",
                columns: new[] { "UserPublicId", "Provider" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_wallet_categories_WalletType_Name",
                table: "wallet_categories",
                columns: new[] { "WalletType", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_wallet_rules_WalletId",
                table: "wallet_rules",
                column: "WalletId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_wallet_templates_CategoryId",
                table: "wallet_templates",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "ix_wallet_templates_is_active",
                table: "wallet_templates",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "ix_wallet_templates_sort_order",
                table: "wallet_templates",
                column: "SortOrder");

            migrationBuilder.CreateIndex(
                name: "ix_wallet_templates_type_active",
                table: "wallet_templates",
                columns: new[] { "WalletType", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_wallet_trustees_Email",
                table: "wallet_trustees",
                column: "Email");

            migrationBuilder.CreateIndex(
                name: "IX_wallet_trustees_Status_TokenExpiresAt",
                table: "wallet_trustees",
                columns: new[] { "Status", "TokenExpiresAt" },
                filter: "\"Status\" = 1");

            migrationBuilder.CreateIndex(
                name: "IX_wallet_trustees_VerificationTokenHash",
                table: "wallet_trustees",
                column: "VerificationTokenHash",
                unique: true,
                filter: "\"VerificationTokenHash\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_wallets_BankAccountId",
                table: "wallets",
                column: "BankAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_wallets_CategoryId",
                table: "wallets",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_wallets_SavingsCircleId",
                table: "wallets",
                column: "SavingsCircleId");

            migrationBuilder.CreateIndex(
                name: "IX_wallets_Type_Status",
                table: "wallets",
                columns: new[] { "Type", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_wallets_UserPublicId",
                table: "wallets",
                column: "UserPublicId");

            migrationBuilder.CreateIndex(
                name: "IX_wallets_UserPublicId_Status",
                table: "wallets",
                columns: new[] { "UserPublicId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_wallets_WalletTrusteeId",
                table: "wallets",
                column: "WalletTrusteeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AspNetRoleClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserLogins");

            migrationBuilder.DropTable(
                name: "AspNetUserRoles");

            migrationBuilder.DropTable(
                name: "AspNetUserTokens");

            migrationBuilder.DropTable(
                name: "banks");

            migrationBuilder.DropTable(
                name: "contributions");

            migrationBuilder.DropTable(
                name: "feature_flags");

            migrationBuilder.DropTable(
                name: "feedbacks");

            migrationBuilder.DropTable(
                name: "ledger_entries");

            migrationBuilder.DropTable(
                name: "notifications");

            migrationBuilder.DropTable(
                name: "otp_verifications");

            migrationBuilder.DropTable(
                name: "payouts");

            migrationBuilder.DropTable(
                name: "pin_challenges");

            migrationBuilder.DropTable(
                name: "refresh_tokens");

            migrationBuilder.DropTable(
                name: "release_requests");

            migrationBuilder.DropTable(
                name: "renewal_events");

            migrationBuilder.DropTable(
                name: "scheduled_releases");

            migrationBuilder.DropTable(
                name: "virtual_accounts");

            migrationBuilder.DropTable(
                name: "wallet_templates");

            migrationBuilder.DropTable(
                name: "AspNetRoles");

            migrationBuilder.DropTable(
                name: "AspNetUsers");

            migrationBuilder.DropTable(
                name: "circle_members");

            migrationBuilder.DropTable(
                name: "circle_rounds");

            migrationBuilder.DropTable(
                name: "renewal_policies");

            migrationBuilder.DropTable(
                name: "transactions");

            migrationBuilder.DropTable(
                name: "wallet_rules");

            migrationBuilder.DropTable(
                name: "wallets");

            migrationBuilder.DropTable(
                name: "bank_accounts");

            migrationBuilder.DropTable(
                name: "savings_circles");

            migrationBuilder.DropTable(
                name: "wallet_categories");

            migrationBuilder.DropTable(
                name: "wallet_trustees");
        }
    }
}
