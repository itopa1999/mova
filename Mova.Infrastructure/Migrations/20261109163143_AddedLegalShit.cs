using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mova.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddedLegalShit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_bank_accounts_UserPublicId_IsDefault",
                table: "bank_accounts");

            migrationBuilder.CreateIndex(
                name: "IX_bank_accounts_UserPublicId_IsDefault",
                table: "bank_accounts",
                columns: new[] { "UserPublicId", "IsDefault" },
                unique: true,
                filter: "\"IsDefault\" = true AND \"IsDeleted\" = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_bank_accounts_UserPublicId_IsDefault",
                table: "bank_accounts");

            migrationBuilder.CreateIndex(
                name: "IX_bank_accounts_UserPublicId_IsDefault",
                table: "bank_accounts",
                columns: new[] { "UserPublicId", "IsDefault" },
                unique: true,
                filter: "\"IsDefault\" = true");
        }
    }
}
