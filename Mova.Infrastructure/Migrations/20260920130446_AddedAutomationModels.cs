using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Mova.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddedAutomationModels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "renewal_policies",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    WalletId = table.Column<long>(type: "bigint", nullable: false),
                    UserPublicId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    TriggerType = table.Column<int>(type: "integer", nullable: false),
                    RefillAmountType = table.Column<int>(type: "integer", nullable: false),
                    MaxRenewals = table.Column<int>(type: "integer", nullable: true),
                    RenewalsCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "renewal_events");

            migrationBuilder.DropTable(
                name: "renewal_policies");
        }
    }
}
