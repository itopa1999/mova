using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mova.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBankAccountToWalle2t : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BankImageUrl",
                table: "bank_accounts",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_wallets_BankAccountId",
                table: "wallets",
                column: "BankAccountId");

            migrationBuilder.AddForeignKey(
                name: "FK_wallets_bank_accounts_BankAccountId",
                table: "wallets",
                column: "BankAccountId",
                principalTable: "bank_accounts",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_wallets_bank_accounts_BankAccountId",
                table: "wallets");

            migrationBuilder.DropIndex(
                name: "IX_wallets_BankAccountId",
                table: "wallets");

            migrationBuilder.DropColumn(
                name: "BankImageUrl",
                table: "bank_accounts");
        }
    }
}
