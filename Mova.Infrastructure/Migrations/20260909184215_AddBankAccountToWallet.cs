using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mova.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBankAccountToWallet : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "BankAccountId",
                table: "wallets",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_wallets_CategoryId",
                table: "wallets",
                column: "CategoryId");

            migrationBuilder.AddForeignKey(
                name: "FK_wallets_wallet_categories_CategoryId",
                table: "wallets",
                column: "CategoryId",
                principalTable: "wallet_categories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_wallets_wallet_categories_CategoryId",
                table: "wallets");

            migrationBuilder.DropIndex(
                name: "IX_wallets_CategoryId",
                table: "wallets");

            migrationBuilder.DropColumn(
                name: "BankAccountId",
                table: "wallets");
        }
    }
}
