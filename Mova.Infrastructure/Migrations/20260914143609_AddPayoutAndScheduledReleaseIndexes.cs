using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mova.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPayoutAndScheduledReleaseIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_payouts_bank_accounts_BankAccountId",
                table: "payouts");

            migrationBuilder.DropForeignKey(
                name: "FK_payouts_wallets_WalletId",
                table: "payouts");

            migrationBuilder.DropIndex(
                name: "IX_payouts_ProviderReference",
                table: "payouts");

            migrationBuilder.CreateIndex(
                name: "IX_scheduled_releases_WalletRuleId_ScheduledFor",
                table: "scheduled_releases",
                columns: new[] { "WalletRuleId", "ScheduledFor" });

            migrationBuilder.CreateIndex(
                name: "IX_payouts_ProviderReference",
                table: "payouts",
                column: "ProviderReference",
                unique: true,
                filter: "\"ProviderReference\" IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_payouts_bank_accounts_BankAccountId",
                table: "payouts",
                column: "BankAccountId",
                principalTable: "bank_accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_payouts_wallets_WalletId",
                table: "payouts",
                column: "WalletId",
                principalTable: "wallets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_payouts_bank_accounts_BankAccountId",
                table: "payouts");

            migrationBuilder.DropForeignKey(
                name: "FK_payouts_wallets_WalletId",
                table: "payouts");

            migrationBuilder.DropIndex(
                name: "IX_scheduled_releases_WalletRuleId_ScheduledFor",
                table: "scheduled_releases");

            migrationBuilder.DropIndex(
                name: "IX_payouts_ProviderReference",
                table: "payouts");

            migrationBuilder.CreateIndex(
                name: "IX_payouts_ProviderReference",
                table: "payouts",
                column: "ProviderReference",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_payouts_bank_accounts_BankAccountId",
                table: "payouts",
                column: "BankAccountId",
                principalTable: "bank_accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_payouts_wallets_WalletId",
                table: "payouts",
                column: "WalletId",
                principalTable: "wallets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
